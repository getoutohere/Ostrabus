using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using HarmonyLib;
using LitJson;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;
using Ostranauts.ShipGUIs.Utilities;
using Ostrabus.MiscBugFixes;

static class RecoveryChecks {
    static int checks;
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Field(object target, string name, object value) => AccessTools.Field(target.GetType(), name).SetValue(target, value);
    static void Check(bool ok, string reason) { checks++; if (!ok) throw new Exception(reason); }
    static double Number(JToken value) => value.Value<double>();
    static readonly List<AIShip> pilots = new List<AIShip>();
    static readonly List<string> messages = new List<string>();
    static bool Log(string logString) { messages.Add(logString); return false; }
    static Ship Make(string id, bool ai = true) {
        var ship = Empty<Ship>(); ship.strRegID = id; ship.IsAIShip = ai;
        Field(ship, "<objSS>k__BackingField", new ShipSitu(false));
        Field(ship, "nLoadState", Ship.Loaded.Shallow);
        Field(ship, "aDocked", new Dictionary<string, Ship>()); ship.aWPs = new List<WaypointShip>();
        ship.RCSCount = 12; Field(ship, "fFuelEfficiencyMod", .75f);
        Field(ship, "fShallowMass", 5768.85d);
        CrewSim.system.dictShips[id] = ship;
        if (ai) {
            var controller = Empty<AIShip>(); Field(controller, "_shipUs", ship);
            Field(controller, "_aiCharacter", new Program.Pilot { ShipUs = ship, Speed = 5e-9 });
            pilots.Add(controller);
        }
        return ship;
    }
    static string State(Ship ship) => JsonMapper.ToJson(ship.objSS.GetJSON()) + "|" +
        ship.fShallowRCSRemass.ToString("R", CultureInfo.InvariantCulture) + "|" + ship.fShallowFusionRemain.ToString("R", CultureInfo.InvariantCulture);
    static void Corrupt(Ship ship, Ship destination) {
        new TargetData(destination).ApplyToShip(ship);
        ship.shipSituTarget = new ShipSitu(destination.objSS, false);
        ship.shipSituTarget.destroy();
        ship.TargetData = null; // Matches native CheckTargets restoration in captures.
    }
    internal static void Run(Func<Ship, double, double, NavData> plan) {
        var system = Empty<StarSystem>(); system.dictShips = new Dictionary<string, Ship>(); CrewSim.system = system; CrewSim.coPlayer = null;
        AIShipManager.dictAIs = new Dictionary<string, List<AIShip>> { ["fixture"] = pilots };
        var station = Make("OKLG", false); station.objSS.bIsBO = true; station.objSS.strBOPORShip = "OKLG";
        var stationMotion = JObject.Parse(File.ReadAllText(Ostrabus.Development.InstallationPaths.Fixture("AiPlanner/RING-OKLG.json")))["data"]["navigation"]["currentMotion"];
        station.objSS.vPosx = Number(stationMotion["positionAU"]["x"]); station.objSS.vPosy = Number(stationMotion["positionAU"]["y"]);
        station.objSS.vVelX = Number(stationMotion["velocityAUPerSecond"]["x"]); station.objSS.vVelY = Number(stationMotion["velocityAUPerSecond"]["y"]);
        var outcomes = new List<object>();
        foreach (string id in new[] { "O-CMK", "O-ZWZA", "O-GLN", "O-QVQ" }) {
            var wrapper = JObject.Parse(File.ReadAllText(Ostrabus.Development.InstallationPaths.Fixture("AiPlanner/RING-ai-" + id + ".json"))); var data = wrapper["data"];
            var detail = JObject.Parse(File.ReadAllText(Ostrabus.Development.InstallationPaths.Fixture("AiPlanner/RING-ship-" + id + ".json")))["data"];
            var ship = Make(id); var motion = data["navigation"]["currentMotion"];
            ship.objSS.vPosx = Number(motion["positionAU"]["x"]); ship.objSS.vPosy = Number(motion["positionAU"]["y"]);
            ship.objSS.vVelX = Number(motion["velocityAUPerSecond"]["x"]); ship.objSS.vVelY = Number(motion["velocityAUPerSecond"]["y"]);
            ship.fShallowRCSRemass = Number(data["navigation"]["propulsion"]["rcsFuelKg"]);
            ship.RCSCount = (float)Number(data["navigation"]["propulsion"]["rcsCount"]);
            Field(ship, "fFuelEfficiencyMod", (float)Number(data["navigation"]["propulsion"]["fuelEfficiencyMod"]));
            Field(ship, "fShallowMass", Number(detail["storedShallowMassKg"]));
            var oldRoute = new NavData(ship); oldRoute.AddNavDataPoint(new NavDataPoint(0, new ShipSitu(false)));
            ship.objSS.NavData = oldRoute; Corrupt(ship, station);
            string before = State(ship); ShipSitu stale = ship.shipSituTarget;
            Check(NavigationTargetRecovery.Candidate(ship, system, out Ship target, out _) && ReferenceEquals(target, station), id + ": capture not eligible");
            Check(State(ship) == before && ReferenceEquals(ship.shipSituTarget, stale), id + ": dry evaluation mutated state");
            Check(NavigationTargetRecovery.Recover(ship, system, out _), id + ": recovery failed");
            Check(ReferenceEquals(ship.shipSituTarget, station.objSS) && ReferenceEquals(ship.shipScanTarget, station) && ship.targetRegID == "OKLG", id + ": wrong destination");
            Check(ship.objSS.NavData == null, id + ": bad plan retained");
            ship.objSS.NavData = oldRoute; Check(State(ship) == before, id + ": recovery changed physical motion or fuel"); ship.objSS.NavData = null;
            Check(!NavigationTargetRecovery.Recover(ship, system, out _), id + ": repeat recovery should be a no-op");
            double speed = double.Parse((string)data["commands"]["chain"][0]["saveData"][0], CultureInfo.InvariantCulture);
            NavData replacement = plan(ship, Number(wrapper["epoch"]), speed);
            double seconds = replacement.Destination.ArrivalTime - replacement.Origin.ArrivalTime;
            Check(seconds > 0 && seconds < 86400, id + ": recovery route did not converge");
            double dx = replacement.Destination.ObjSS.vPosx - (station.objSS.vPosx + station.objSS.vVelX * seconds);
            double dy = replacement.Destination.ObjSS.vPosy - (station.objSS.vPosy + station.objSS.vVelY * seconds);
            double errorKm = Math.Sqrt(dx * dx + dy * dy) * 149597872;
            Check(errorKm < .01, id + ": route endpoint does not meet predicted target");
            Check(replacement.Destination.FuelLevel >= 0, id + ": captured fuel insufficient in isolated native calculation");
            outcomes.Add(new { id, seconds, errorKm, fuelStartKg = ship.fShallowRCSRemass, fuelArrivalKg = replacement.Destination.FuelLevel });
            Console.WriteLine(id + ": recovery route " + seconds.ToString("F1") + " s; arrival fuel " + replacement.Destination.FuelLevel.ToString("F2") + " kg.");
        }
        foreach (string mode in new[] { "healthy-long-trip", "non-ai", "hidden", "docked", "group", "anchored", "combat", "missing-id", "mismatched-id", "missing-station", "offset", "border", "no-pilot", "invalid-motion", "invalid-destination", "player" }) {
            var ship = Make("case-" + mode); Corrupt(ship, station);
            var route = new NavData(ship); route.AddNavDataPoint(new NavDataPoint(1e12, new ShipSitu(false) { vPosx = 1000 })); ship.objSS.NavData = route;
            if (mode == "healthy-long-trip") new TargetData(station).ApplyToShip(ship);
            if (mode == "non-ai") ship.IsAIShip = false;
            if (mode == "hidden") ship.HideFromSystem = true;
            if (mode == "docked") ((Dictionary<string, Ship>)AccessTools.Field(typeof(Ship), "aDocked").GetValue(ship))["port"] = station;
            if (mode == "group") ship.objSS._dockGroup = new SolarSystem.DockGroup();
            if (mode == "anchored") ship.objSS.bBOLocked = true;
            if (mode == "combat") ship.shipCombatTarget = station;
            if (mode == "missing-id") ship.targetRegID = null;
            if (mode == "mismatched-id") ship.targetRegID = "other";
            if (mode == "missing-station") system.dictShips.Remove("OKLG");
            if (mode == "offset") ship.shipSituTarget.vBOOffsetx = 1e-7;
            if (mode == "border") ship.shipSituTarget.IsRegionBorderPoint = true;
            if (mode == "no-pilot") pilots.RemoveAt(pilots.Count - 1);
            if (mode == "invalid-motion") ship.objSS.vPosx = double.NaN;
            if (mode == "invalid-destination") station.objSS.strBOPORShip = "missing-body";
            if (mode == "player") { var player = Empty<CondOwner>(); Field(player, "_objShip", ship); CrewSim.coPlayer = player; }
            string before = State(ship); ShipSitu targetBefore = ship.shipSituTarget;
            Check(!NavigationTargetRecovery.Candidate(ship, system, out _, out _), mode + ": unsafe candidate accepted");
            Check(!NavigationTargetRecovery.Recover(ship, system, out _), mode + ": unsafe recovery accepted");
            Check(State(ship) == before && ReferenceEquals(ship.shipSituTarget, targetBefore) && ReferenceEquals(ship.objSS.NavData, route), mode + ": rejected vessel changed");
            system.dictShips["OKLG"] = station; station.objSS.strBOPORShip = "OKLG"; CrewSim.coPlayer = null;
        }
        var controls = Make("old-controls"); Corrupt(controls, station);
        controls.objSS.vVelX = 1e-7; controls.objSS.vVelY = -2e-8; controls.objSS.fW = .3f;
        controls.objSS.vAccRCS = new UnityEngine.Vector2(1e-10f, -1e-10f); controls.objSS.fA = .1f;
        controls.objSS.vAccIn = new UnityEngine.Vector2(0, 1e-10f); controls.fShallowRCSRemass = 123;
        Check(NavigationTargetRecovery.Recover(controls, system, out _), "Old thrust recovery failed");
        Check(controls.objSS.vAccRCS.x == 0 && controls.objSS.vAccRCS.y == 0 && controls.objSS.fA == 0 && controls.objSS.vAccIn.y == 0, "Old controls not cancelled by native cleanup");
        Check(controls.objSS.vVelX == 1e-7 && controls.objSS.vVelY == -2e-8 && controls.objSS.fW == .3f && controls.fShallowRCSRemass == 123, "Native cleanup changed momentum/fuel");
        system.dictShips.Clear(); pilots.Clear(); system.dictShips["OKLG"] = station;
        var brokenCommand = Make("arbitrary-registration"); Corrupt(brokenCommand, station);
        var healthyCommand = Make("interplanetary-cargo"); new TargetData(station).ApplyToShip(healthyCommand);
        var healthyRoute = new NavData(healthyCommand); healthyRoute.AddNavDataPoint(new NavDataPoint(1e12, new ShipSitu(false) { vPosx = 1000 }));
        healthyCommand.objSS.NavData = healthyRoute;
        string brokenBefore = State(brokenCommand), healthyBefore = State(healthyCommand);
        var console = Empty<ConsoleToGUI>(); AccessTools.Field(typeof(ConsoleToGUI), "instance").SetValue(null, console);
        var consoleHooks = new Harmony("offline.navfix.console");
        try {
            consoleHooks.Patch(AccessTools.Method(typeof(ConsoleToGUI), "LogInfo"), prefix: new HarmonyMethod(typeof(RecoveryChecks), nameof(Log)));
            consoleHooks.CreateClassProcessor(typeof(NavigationTargetRecoveryCommand)).Patch();
            string command = "navfix";
            Check(!ConsoleResolver.ResolveString(ref command), "Console command not consumed");
            Check(State(brokenCommand) == brokenBefore && State(healthyCommand) == healthyBefore && NavigationTargetRecovery.HasCorruptReference(brokenCommand), "Default dry run changed world state");
            Check(messages.Exists(s => s.Contains("1 eligible, 0 recovered")), "Dry-run summary incorrect");
            messages.Clear(); command = "navfix apply"; ConsoleResolver.ResolveString(ref command);
            Check(!NavigationTargetRecovery.HasCorruptReference(brokenCommand) && messages.Exists(s => s.Contains("1 eligible, 1 recovered")), "Apply command did not recover arbitrary eligible call sign");
            Check(State(healthyCommand) == healthyBefore && ReferenceEquals(healthyCommand.objSS.NavData, healthyRoute), "Apply changed healthy long-distance route");
            messages.Clear(); command = "navfix apply"; ConsoleResolver.ResolveString(ref command);
            Check(messages.Exists(s => s.Contains("0 corrupt targets")), "Repeated command not idempotent");
            command = "echo test";
            Check(ConsoleResolver.ResolveString(ref command) && command.EndsWith("\ntest"), "Other console commands intercepted");
        } finally { consoleHooks.UnpatchSelf(); }
        File.WriteAllText("obj/AI-NAV-RECOVERY.json", JsonConvert.SerializeObject(outcomes, Formatting.Indented));
        Console.WriteLine(checks + " navigation recovery checks passed.");
    }
}
