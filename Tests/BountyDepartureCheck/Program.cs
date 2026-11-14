using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ostrabus.Development;
using Ostrabus.ShipNavigation;
using Ostranauts.Core;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using Ostranauts.Systems;

static class Program {
    static int checks, transfers, activations, selections, homeSelections;
    static bool unconscious, playerOwned, missingHull, transferFails, throwTransfer;
    static Ship station, source, hull;
    static double activatedFuel, remainingDeltaV = 10000;
    static readonly Dictionary<string, Ship> Ships = new Dictionary<string, Ship>();
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Set(object item, string name, object value) => AccessTools.Field(item.GetType(), name).SetValue(item, value);
    static void Check(bool value, string why) { checks++; if (!value) throw new Exception(why); }
    static bool Quiet() => false;
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = ReferenceEquals(__0, __1); return false; }
    static bool Unequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = !ReferenceEquals(__0, __1); return false; }
    static bool Owner(ref string __result) { __result = playerOwned ? "player" : "npc"; return false; }
    static bool Has(string strName, ref bool __result) { __result = unconscious && strName == "Unconscious"; return false; }
    static bool Lookup(string strRegID, ref Ship __result) { __result = strRegID == "hull" && missingHull ? null : Ships[strRegID]; return false; }
    static bool Spawn(ref Ship __result) { missingHull = false; __result = hull; return false; }
    static bool Transfer(CondOwner crew, Ship ship) {
        transfers++;
        if (throwTransfer) throw new InvalidOperationException("fixture transfer failed");
        if (!transferFails) crew.ship = ship;
        return false;
    }
    static bool Activate(Ship ship) { activations++; activatedFuel = ship.fShallowRCSRemass; Check(ReferenceEquals(ship, hull), "Activated a replacement hull"); return false; }
    static bool DeltaV(ref double __result) { __result = remainingDeltaV; return false; }
    static bool Speed(ref double __result) { __result = 100; return false; }
    static bool Home(ref TargetData __result) { homeSelections++; __result = new TargetData(station); return false; }
    static bool Plan() => false;
    static bool Find(ref object __result) {
        selections++;
        __result = EmptyDto();
        return false;
    }
    static object EmptyDto() {
        var dto = Activator.CreateInstance(AccessTools.Inner(typeof(TargetNewIfNone), "TargetDataDTO"), true);
        Set(dto, "TargetData", new TargetData(new ShipSitu(false)));
        return dto;
    }
    static Ship Vessel(string id, bool isStation = false) {
        var ship = Empty<Ship>(); ship.strRegID = id;
        Set(ship, "<objSS>k__BackingField", new ShipSitu(false) { bIsBO = isStation });
        Set(ship, "<ShipCO>k__BackingField", Empty<CondOwner>());
        Set(ship, "nLoadState", Ship.Loaded.Shallow);
        Set(ship, "aDockingPorts", isStation ? new List<string> { "Primary" } : new List<string>());
        ship.fShallowRCSRemassMax = 7493.64694715254;
        ship.fShallowRCSRemass = 656.062438979404;
        Set(ship, "fShallowMass", 64865.4668);
        Ships[id] = ship;
        return ship;
    }
    static CondOwner Actor(Ship origin) {
        var person = Empty<CondOwner>(); person.bAlive = true; person.ship = origin;
        return person;
    }
    static void Choose(Ship ship, bool expectHome) {
        int before = homeSelections, selected = selections;
        var pilot = Empty<NavyPilot>(); Set(pilot, "<ShipUs>k__BackingField", ship);
        var command = Empty<TargetNewIfNone>(); Set(command, "_ai", pilot); Set(command, "_shipUs", ship);
        ship.shipScanTarget = null; ship.shipSituTarget = null;
        command.RunCommand(new Blackboard());
        Check(homeSelections - before == (expectHome ? 1 : 0), "Native low-fuel home branch disagrees");
        Check(selections - selected == (expectHome ? 0 : 1), "Native ordinary target selection disagrees");
        Check(ReferenceEquals(ship.shipScanTarget, station) == expectHome, "Native target application disagrees");
    }
    static void Relaunch(AIType role, bool patched, bool inSpace = false, bool create = false) {
        transfers = activations = 0; missingHull = create;
        source = Vessel("source", !inSpace); station = source; hull = Vessel("hull");
        Ships["region"] = source;
        var person = Actor(source);
        var bounty = new JsonBountySave { strTargetRegId = "hull", strTargetRegion = "region", strTargetShip = "fixture", enumAIType = role };
        double mass = (double)AccessTools.Field(typeof(Ship), "fShallowMass").GetValue(hull);
        AccessTools.Method(typeof(BountyManager), "SpawnBountyTarget").Invoke(null, new object[] { bounty, person, false });
        Check(transfers == 1 && activations == 1, "Native activation/transfer count changed");
        Check(ReferenceEquals(person.ship, hull), "NPC identity or boarding changed");
        Check(hull.fShallowRCSRemass == (patched && !inSpace ? hull.fShallowRCSRemassMax : 656.062438979404), "Departure fuel scope changed");
        Check(activatedFuel == hull.fShallowRCSRemass, "Station service happened after controller activation");
        Check((double)AccessTools.Field(typeof(Ship), "fShallowMass").GetValue(hull) == mass, "Fuel servicing changed stored hull mass");
        Choose(hull, !patched || inSpace);
    }
    static void Assets() {
        var rows = JArray.Parse(File.ReadAllText(Path.Combine(InstallationPaths.Data, "condtrigs", "condtrigs.json")));
        var arrest = rows.Cast<JObject>().Single(row => (string)row["strName"] == "TIsUnderArrestAlive");
        Check(!(bool)arrest["bAND"], "Native arrest trigger no longer ORs custody/unconscious conditions");
        Check(arrest["aReqs"].Values<string>().OrderBy(x => x).SequenceEqual(new[] { "IsSubdued", "IsTempDrafted", "IsTempDraftedLong", "Unconscious" }), "Native custody conditions changed");
        var drop = rows.Cast<JObject>().Single(row => (string)row["strName"] == "TIsValidStationDropOff");
        Check(drop["aForbids"].Values<string>().Contains("IsDead"), "Station drop-off no longer excludes dead NPCs");
    }
    static void Main() {
        var boundaries = new Harmony("offline.bounty-departure.boundaries");
        var repair = new Harmony("offline.bounty-departure.repair");
        void Patch(Type type, string method, string handler) {
            foreach (var target in AccessTools.GetDeclaredMethods(type).Where(m => m.Name == method))
                boundaries.Patch(target, prefix: new HarmonyMethod(typeof(Program), handler));
        }
        try {
            Patch(typeof(UnityEngine.Object), "op_Equality", nameof(Equal));
            Patch(typeof(UnityEngine.Object), "op_Inequality", nameof(Unequal));
            CrewSim.system = Empty<StarSystem>(); CrewSim.coPlayer = Empty<CondOwner>(); Set(CrewSim.coPlayer, "_strID", "player");
            Patch(typeof(StarSystem), nameof(StarSystem.GetShipOwner), nameof(Owner));
            Patch(typeof(StarSystem), nameof(StarSystem.GetShipByRegID), nameof(Lookup));
            Patch(typeof(StarSystem), nameof(StarSystem.SpawnShip), nameof(Spawn));
            foreach (var method in AccessTools.GetDeclaredMethods(typeof(CondOwner)).Where(m => m.Name == "HasCond" && m.GetParameters()[0].ParameterType == typeof(string)))
                boundaries.Patch(method, prefix: new HarmonyMethod(typeof(Program), nameof(Has)));
            Patch(typeof(CondOwner), nameof(CondOwner.AddCondAmount), nameof(Quiet));
            boundaries.Patch(AccessTools.Method(typeof(AIShip), nameof(AIShip.AddCrew), new[] { typeof(CondOwner), typeof(Ship), typeof(bool), typeof(string) }), prefix: new HarmonyMethod(typeof(Program), nameof(Transfer)));
            Patch(typeof(Ship), nameof(Ship.ToggleVis), nameof(Quiet));
            Patch(typeof(AIShip), nameof(AIShip.ResetShipState), nameof(Quiet));
            Patch(typeof(AIShipManager), nameof(AIShipManager.AddAIToShip), nameof(Activate));
            Patch(typeof(ShipSitu), nameof(ShipSitu.PushbackFrom), nameof(Quiet));
            boundaries.Patch(AccessTools.PropertyGetter(typeof(Ship), nameof(Ship.DeltaVRemainingRCS)), prefix: new HarmonyMethod(typeof(Program), nameof(DeltaV)));
            Patch(typeof(NavyPilot), nameof(NavyPilot.MaxSpeed), nameof(Speed));
            Patch(typeof(NavyPilot), nameof(NavyPilot.GetTargetHome), nameof(Home));
            Patch(typeof(TargetNewIfNone), "GenerateNavData", nameof(Plan));
            Patch(typeof(TargetNewIfNone), "FindTarget", nameof(Find));
            Assets();
            Relaunch(AIType.Navy, false);
            repair.CreateClassProcessor(typeof(BountyDepartureFuelPatch)).Patch();
            foreach (var role in new[] { AIType.Navy, AIType.Police, AIType.Pirate, AIType.Scav, AIType.Civilian }) {
                Relaunch(role, true);
                Relaunch(role, true, inSpace: true);
                Relaunch(role, true, create: true);
            }
            // No full/edit/unloaded canister mutations; no station-person refills.
            foreach (Ship.Loaded state in Enum.GetValues(typeof(Ship.Loaded))) {
                source = Vessel("source", true); hull = Vessel("hull"); Set(hull, "nLoadState", state);
                var person = Actor(source); BountyDepartureFuelPatch.Embark(person, hull, true, "region");
                Check(hull.fShallowRCSRemass == (state == Ship.Loaded.Shallow ? hull.fShallowRCSRemassMax : 656.062438979404), "Materialization scope changed: " + state);
            }
            source = Vessel("source", true); hull = Vessel("hull", true);
            BountyDepartureFuelPatch.Embark(Actor(source), hull, true, "region");
            Check(hull.fShallowRCSRemass == 656.062438979404, "Station-to-station transfer refueled");
            source = Vessel("source", true); hull = Vessel("hull");
            BountyDepartureFuelPatch.Embark(Actor(hull), hull, true, "region");
            Check(hull.fShallowRCSRemass == 656.062438979404, "Already-aboard save restoration refueled");
            transferFails = true;
            BountyDepartureFuelPatch.Embark(Actor(source), hull, true, "region"); transferFails = false;
            Check(hull.fShallowRCSRemass == 656.062438979404, "Failed transfer refueled");
            throwTransfer = true;
            try { BountyDepartureFuelPatch.Embark(Actor(source), hull, true, "region"); throw new Exception("Lost native exception"); }
            catch (InvalidOperationException) { checks++; } finally { throwTransfer = false; }
            Check(hull.fShallowRCSRemass == 656.062438979404, "Exceptional transfer refueled");
            hull.bDestroyed = true;
            BountyDepartureFuelPatch.Embark(Actor(source), hull, true, "region");
            Check(hull.fShallowRCSRemass == 656.062438979404, "Destroyed hull refueled");
            hull.bDestroyed = false;
            BountyDepartureFuelPatch.Embark(Actor(null), hull, true, "region");
            Check(hull.fShallowRCSRemass == 656.062438979404, "Missing origin refueled");
            hull.fShallowRCSRemass = 0;
            BountyDepartureFuelPatch.Embark(Actor(source), hull, true, "region");
            Check(hull.fShallowRCSRemass == hull.fShallowRCSRemassMax, "Empty parked hull not serviced");
            remainingDeltaV = 0;
            Choose(hull, true);
            remainingDeltaV = 10000;
            foreach (string blocked in new[] { "dead", "unconscious", "player-owned" }) {
                source = Vessel("source", true); hull = Vessel("hull"); Ships["region"] = source;
                var person = Actor(source); person.bAlive = blocked != "dead"; unconscious = blocked == "unconscious"; playerOwned = blocked == "player-owned";
                int count = transfers;
                AccessTools.Method(typeof(BountyManager), "SpawnBountyTarget").Invoke(null, new object[] { new JsonBountySave { strTargetRegId = "hull", strTargetRegion = "region", enumAIType = AIType.Navy }, person, false });
                Check(transfers == count && ReferenceEquals(person.ship, source) && hull.fShallowRCSRemass == 656.062438979404, "Native blocked departure bypassed: " + blocked);
                unconscious = playerOwned = false;
            }
            try { BountyDepartureFuelPatch.Transpiler(Array.Empty<CodeInstruction>()).ToArray(); throw new Exception("Missing call sites accepted"); }
            catch (InvalidOperationException) { checks++; }
            Console.WriteLine("Bounty departure: " + checks + " native activation, low-fuel branch, scope and asset checks passed.");
        } finally { repair.UnpatchSelf(); boundaries.UnpatchSelf(); CrewSim.coPlayer = null; CrewSim.system = null; }
    }
}
