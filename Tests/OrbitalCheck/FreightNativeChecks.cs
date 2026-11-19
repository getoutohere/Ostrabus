using System;
using System.IO;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ostrabus.Orbital;
using Ostrabus.Orbital.Economy;
using Ostrabus.ShipNavigation;
using Ostranauts.Core;
using Ostranauts.Ships.AIPilots;
using Ostranauts.ShipGUIs.Utilities;

// Full saved-world planner and production freight propagation. The separate
// economic suite executes transfer, overflow, retirement and PASS scope hooks.
static class FreightNativeChecks {
    internal static void Run(string file, string id, string mode) {
        var scene = new Harmony("offline.native-freight.scene");
        var production = new Harmony("offline.native-freight.production");
        try {
            FreightCourseChecks.Scene(scene);
            production.CreateClassProcessor(typeof(DepartureWaypointPatch)).Patch();
            production.CreateClassProcessor(typeof(FreightNoWakePatch)).Patch();
            production.CreateClassProcessor(typeof(FreightPlanningClockPatch)).Patch();
            var data = JObject.Parse(File.ReadAllText(file));
            var world = FreightCourseChecks.World(data);
            var ship = FreightCourseChecks.Vessel(data["ships"][id]);
            ship.objSS.NavData = new NavData(data["ships"][id]["objSS"]["jnd"].ToObject<JsonNavData>());
            var original = ship.objSS.NavData;
            // Historical failures are preventive departure replays. Current
            // fleet cases start at their actual saved position and velocity.
            if ((string)data["save"] != "roll") {
                StarSystem.fEpoch = original.Origin.ArrivalTime;
                ship.objSS.CopyFrom(original.Origin.ObjSS, false); ship.objSS.NavData = original;
                ship.fShallowRCSRemass = original.Origin.FuelLevel;
                ship.fShallowFusionRemain = original.Origin.TorchFuelLevel;
            }
            var character = new FreightCourseChecks.Pilot { ShipUs = ship, Blackboard = new Blackboard() };
            var pilot = FreightCourseChecks.Empty<AIShip>();
            FreightCourseChecks.Set(pilot, "_shipUs", ship);
            FreightCourseChecks.Set(pilot, "_aiCharacter", character);
            FreightCourseChecks.Set(pilot, "<Blackboard>k__BackingField", character.Blackboard);
            var environment = new SffEnvironment();
            foreach (var body in world.aBOs.Values) if (!body.IsPlaceholder() && !body.IsAsteroidField && body.fMass > 0)
                environment.Bodies.Add(new SffBody(body));
            double start = StarSystem.fEpoch;
            int windows = 0, planned = 0;
            bool arrived = false;
            double quantum = mode == "pass" ? 28800 : 7200;
            using (mode == "pass" ? new JumpBoundary("offline-native-freight") : null) {
                for (; windows < 1500; windows++) {
                    double begin = StarSystem.fEpoch, end = begin + quantum;
                    var route = new FreightRoute(pilot, ship.shipScanTarget, begin, end, environment, ship.objSS.NavData);
                    if (!route.Prepare()) throw new Exception(id + ": freight propagation declined window " + windows + " at " + (begin - start) / 3600 + " h; stages=" + route.StagesPlanned +
                        "; impact=" + route.Approach.Impact?.Native.strName + "; budget=" + route.Approach.BudgetFallback + "; steps=" + route.Approach.Steps);
                    planned += route.StagesPlanned;
                    StarSystem.fEpoch = end;
                    foreach (var body in world.aBOs.Values) body.UpdateTime(end);
                    route.Apply();
                    if (!ReferenceEquals(ship.shipSituTarget, ship.shipScanTarget.objSS)) throw new Exception(id + ": station target detached during skip");
                    if (route.Arrival <= end) { arrived = true; break; }
                }
            }
            if (!arrived) throw new Exception(id + ": skipped voyage did not reach its receiver");
            Console.WriteLine(new JObject { ["id"] = id, ["mode"] = mode, ["arrived"] = true,
                ["windows"] = windows + 1, ["replannedStages"] = planned, ["hours"] = (StarSystem.fEpoch - start) / 3600
            }.ToString(Newtonsoft.Json.Formatting.None));
        } finally { production.UnpatchSelf(); scene.UnpatchSelf(); CrewSim.system = null; }
    }
}
