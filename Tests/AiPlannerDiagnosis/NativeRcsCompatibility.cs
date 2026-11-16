using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using HarmonyLib;
using Ostrabus.ShipNavigation;
using Ostranauts.ShipGUIs.Utilities;
using Ostranauts.Trading;
using Ostranauts.Core;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;

static class NativeRcsCompatibility {
    static int checks;
    static int maneuverStops;
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Set(object target, string field, object value) => AccessTools.Field(target.GetType(), field).SetValue(target, value);
    static void Check(bool value, string reason) { checks++; if (!value) throw new Exception(reason); }
    static bool NoCargo(ref double __result) { __result = 0; return false; }
    static bool Quiet() => false;
    static bool Stopped() { maneuverStops++; return false; }
    static bool Text(ref string __result) { __result = "fixture"; return false; }
    static Ship Vessel(float count, double efficiency) {
        var ship = Empty<Ship>(); ship.strRegID = "rcs-compatibility";
        Set(ship, "nLoadState", Ship.Loaded.Shallow); Set(ship, "fShallowMass", 30000d);
        Set(ship, "<objSS>k__BackingField", new ShipSitu(false));
        Set(ship, "aDocked", new Dictionary<string, Ship>()); Set(ship, "aNavs", new List<CondOwner>());
        Set(ship, "fFuelEfficiencyMod", efficiency); ship.RCSCount = count;
        Set(ship, "nRCSCountRaw", 8); ship.fShallowRCSRemass = 300;
        return ship;
    }
    internal static void Run() {
        var boundary = new Harmony("offline.rcs-1.1.boundaries");
        var production = new Harmony("offline.rcs-1.1.production");
        try {
            boundary.Patch(AccessTools.Method(typeof(MarketManager), nameof(MarketManager.GetCargoMassForShip)), prefix: new HarmonyMethod(typeof(NativeRcsCompatibility), nameof(NoCargo)));
            production.CreateClassProcessor(typeof(InvalidRcsBudgetPatch)).Patch();
            // Asset-defined exhaust ratios (0.9, 1.0, 1.25, 2.0) and a
            // representative mixed installation; native thrust remains authoritative.
            foreach (float exhaust in new[] { .9f, 1f, 1.25f, 2f, 1.475f })
            foreach (double efficiency in new[] { 1d, .75d }) {
                var ship = Vessel(8, efficiency); Set(ship, "fRCSExhaustV", exhaust * 8);
                double acceleration = ship.RCSAccelMax;
                Check(acceleration > 0 && !double.IsInfinity(acceleration), "Native new-thruster acceleration invalid");
                double budget = ship.DeltaVRemainingRCS;
                Check(Math.Abs(ship.CalculateRCSFuelConsumption(budget) - 300) < 1e-5,
                    "Fuel budget correction does not invert the new native exhaust law");
            }
            // Independent native old-save control: the 1.0 DTO has no raw-count
            // or exhaust fields. InitShip's shallow branch copies their default
            // zero values; no production migration/propulsion repair is installed.
            boundary.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.StopManeuver)), prefix: new HarmonyMethod(typeof(NativeRcsCompatibility), nameof(Stopped)));
            boundary.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.UpdateDockedShips)), prefix: new HarmonyMethod(typeof(NativeRcsCompatibility), nameof(Quiet)));
            foreach (bool legacy in new[] { false, true }) {
                var saved = LitJson.JsonMapper.ToObject<JsonShip>("{\"nRCSCount\":8,\"nRCSDistroCount\":1}");
                var ship = Vessel(saved.nRCSCount, 1);
                Set(ship, "nRCSDistroCount", saved.nRCSDistroCount);
                Set(ship, "nRCSCountRaw", legacy ? saved.nRCSCountRaw : 8);
                Set(ship, "fRCSExhaustV", legacy ? saved.nRCSExhaustV : 8f);
                maneuverStops = 0;
                ship.Maneuver(1, 0, 0, 0, 1);
                Check((maneuverStops > 0) == legacy && (ship.objSS.vAccRCS.x > 0) == !legacy,
                    "Native legacy-cache propulsion control changed; review migration assumption");
            }
            boundary.Unpatch(AccessTools.Method(typeof(Ship), nameof(Ship.StopManeuver)), HarmonyPatchType.Prefix, boundary.Id);
            boundary.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.Maneuver)), prefix: new HarmonyMethod(typeof(NativeRcsCompatibility), nameof(Quiet)));
            boundary.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.SetThrust)), prefix: new HarmonyMethod(typeof(NativeRcsCompatibility), nameof(Quiet)));
            boundary.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.LogAdd)), prefix: new HarmonyMethod(typeof(NativeRcsCompatibility), nameof(Quiet)));
            boundary.Patch(AccessTools.Method(typeof(DataHandler), nameof(DataHandler.GetString)), prefix: new HarmonyMethod(typeof(NativeRcsCompatibility), nameof(Text)));
            production.CreateClassProcessor(typeof(RouteCompletionPatch)).Patch();
            production.CreateClassProcessor(typeof(FlightControllerWithdrawalPatch)).Patch();
            foreach (bool registered in new[] { false, true }) foreach (bool damaged in new[] { false, true })
            foreach (bool legacySnapshot in new[] { false, true }) {
                var ship = Vessel(damaged ? 7 : 8, 1); ship.fShallowRCSRemass = 100;
                AIShipManager.dictAIs = new Dictionary<string, List<AIShip>> { ["fixture-region"] = new List<AIShip>() };
                AIShip controller = null;
                if (registered) {
                    var pilot = Empty<AutoPilot>();
                    Set(pilot, "<ShipUs>k__BackingField", ship); pilot.Blackboard = new Blackboard();
                    controller = Empty<AIShip>(); Set(controller, "_shipUs", ship); Set(controller, "_aiCharacter", pilot);
                    Set(controller, "_activeCommand", new FlyToAutoPilot(pilot));
                    AIShipManager.dictAIs["fixture-region"].Add(controller);
                }
                var course = new NavData(ship);
                // Old JsonNavDataPoint lacks RCSCount and defaults to zero.
                // Preserve that unknown historical snapshot, rather than inventing
                // how many thrusters were installed when the old route was plotted.
                float plottedCount = legacySnapshot ? 0 : 8;
                course.AddNavDataPoint(new NavDataPoint(1000, new ShipSitu(false), 100, 0, 0, plottedCount));
                course.AddNavDataPoint(new NavDataPoint(1100, new ShipSitu(false) { vPosx = .001 }, 50, 0, 0, plottedCount));
                ship.objSS.NavData = course; StarSystem.fEpoch = 1050;
                double interval = 50;
                bool result = RouteCompletion.Advance(course, ship.objSS, ref interval);
                bool abort = damaged && !legacySnapshot;
                Check(result == !abort && (ship.objSS.NavData == null) == abort,
                    "Completion repair changed native mid-course RCS-loss abort");
                Check(interval == 50 && Math.Abs(ship.fShallowRCSRemass - 75) < 1e-5,
                    "RCS capability abort was mistaken for completion or altered physical fuel debit");
                Check(ReferenceEquals(AIShipManager.GetAIShipByRegID(ship.strRegID), registered && !abort ? controller : null),
                    "New native RCS-loss withdrawal failed or removed an intact controller");
            }
            Console.WriteLine(checks + " native 1.1 thruster/budget and mid-course capability-abort compatibility checks passed.");
        } finally { production.UnpatchSelf(); boundary.UnpatchSelf(); }
    }
}
