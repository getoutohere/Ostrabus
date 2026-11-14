using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ostrabus.Development;
using Ostranauts.Core;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using UnityEngine;

static partial class Program {
    static JObject config;
    static readonly List<BodyOrbit> fields = new List<BodyOrbit>();
    static void AddBody(JObject spec, bool ground = false) {
        string name = (string)spec["strName"], parentName = (string)spec["strNameParent"];
        var parent = parentName == null ? null : CrewSim.system.aBOs[parentName];
        var spawn = spec.ToObject<JsonSpawnBodyOrbit>();
        double per = spawn.fPeriapsisAU, aph = spawn.fApoapsisAU, period = spawn.fOrbitalPeriodYears;
        if (ground) { per = aph = parent.fRadius + ShipSitu.MINSTATIONSIZE * 6.684587E-12f; period = parent.fRotationPeriod / 31556926.0; }
        else if (per == 0 || aph == 0) {
            var method = AccessTools.Method(typeof(StarSystem), "CalculatePAFromET");
            object[] args = { 0d, 0d, (double)spawn.fEccentricity, (double)spawn.fOrbitalPeriodYears, parent };
            var parameters = method.GetParameters();
            for (int i = 2; i < 4; i++) args[i] = Convert.ChangeType(args[i], parameters[i].ParameterType);
            method.Invoke(method.IsStatic ? null : CrewSim.system, args); per = (double)args[0]; aph = (double)args[1];
        }
        var body = new BodyOrbit(name, per, aph, spawn.fDegreesCW, spawn.fEccentricity, period,
            spawn.fRadiusKM, spawn.fMassKG, spawn.fRotationPeriodDays, parent);
        CrewSim.system.aBOs[name] = body;
    }
    static void Assets() {
        config ??= (JObject)JArray.Parse(File.ReadAllText(Path.Combine(InstallationPaths.Data, "star_systems", "star_system.json")))[0];
        var fixture = JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Orbits.json")));
        var sol = fixture["bodies"].Single(x => (string)x["strName"] == "Sol").ToObject<JsonBodyOrbitSave>();
        CrewSim.system.aBOs["Sol"] = new BodyOrbit(sol);
        CrewSim.system.boStar = CrewSim.system.aBOs["Sol"];
        foreach (var body in config["aSpawnBodies"].OfType<JObject>().Where(x => new[] { "Ceres", "CeresAsteroidFieldCentral", "BCRS_Anchor" }.Contains((string)x["strName"]))) AddBody(body);
        foreach (string id in new[] { "BCER", "BCRS", "COHO" }) {
            var definition = config["aSpawnStations"].OfType<JObject>().Single(x => (string)x["strName"] == id);
            AddBody(definition, (string)definition["strOrbitType"] == "GROUND");
            Check(definition["aStartingConds"].Values<string>().Any(x => x.StartsWith("StationInfluenceRange=", StringComparison.Ordinal)), "Authored patrol area missing: " + id);
        }
        fields.Clear();
        foreach (var field in config["aSpawnAsteroidFields"].OfType<JObject>().Where(x => (string)x["strNameParent"] == "CeresAsteroidFieldCentral")) {
            AddBody(field); fields.Add(CrewSim.system.aBOs[(string)field["strName"]]);
        }
        var settings = JArray.Parse(File.ReadAllText(Path.Combine(InstallationPaths.Data, "plot_manager", "pm_settings.json")))[0];
        Check((double)settings["dictEventChances"]["tension_factionship"] == .05, "Native faction event weighting changed");
    }
    static double AreaRadius() {
        object[] args = { "BCER", null, 0d };
        Check((bool)AccessTools.Method(Policy, "Area").Invoke(null, args), "Area did not resolve");
        return (double)args[2];
    }
    static void GeometryChecks() {
        FreshWorld();
        Check((bool)Call("CanStage"), "Local patrol encounter excluded");
        double radius = AreaRadius();
        Check(radius > 30000 && radius < 60000, "Ceres bounds do not enclose local infrastructure without expanding to the ATC catchment");
        Console.WriteLine("Authored Ceres encounter envelope radius: " + radius.ToString("F1") + " km");
        foreach (double advance in new[] { 0.0, 21600.0, 86400.0 * 30 }) {
            StarSystem.fEpoch = 65633872851.578644 + advance;
            foreach (var station in new[] { primary, terminal, haven }) {
                Pose(player, station, 100, 0, .1);
                Check((bool)Call("CanStage"), "Station neighborhood excluded: " + station.strRegID);
            }
            var first = State(CrewSim.system.aBOs["BCER"]); var last = State(CrewSim.system.aBOs["BCRS"]);
            for (int i = 0; i <= 10; i++) {
                double t = i / 10d;
                player.objSS.vPosx = first[0] + t * (last[0] - first[0]); player.objSS.vPosy = first[1] + t * (last[1] - first[1]);
                player.objSS.vVelX = first[2]; player.objSS.vVelY = first[3];
                Check((bool)Call("CanStage"), "Artificial quiet gap between BCER and BCRS");
            }
            Check(fields.Count > 0, "No authored Ceres belt fixture");
            foreach (var field in fields) {
                var state = State(field);
                player.objSS.vPosx = state[0]; player.objSS.vPosy = state[1]; player.objSS.vVelX = first[2]; player.objSS.vVelY = first[3];
                Check((bool)Call("CanStage"), "Authored belt excluded: " + field.strName);
            }
        }
        FreshWorld(); Pose(player, primary, radius + 10);
        Check(!Event() && created == 0, "Outside-envelope event created ships");
        Pose(player, primary, 2000, 0, 3); Check(!Event() && created == 0, "High-speed local flyby created escorts");
        Pose(player, primary, 2000, 0, 1.01); Check(!(bool)Call("SlowEnough", player, primary), "Per-depot velocity gate exceeded one km/s");
        Pose(player, primary, 2000, 0, .99); Check((bool)Call("CanStage"), "Plausible local speed excluded");
        Pose(player, primary, 2000, 0, 1); Check((bool)Call("SlowEnough", player, primary), "One km/s boundary rejected by floating rounding");
        Pose(player, primary, 2000, 0, .99);
        Set(player, "bTorchDriveThrusting", true); Check(!Event() && created == 0, "Active torch departure staged an encounter");
        Set(player, "bTorchDriveThrusting", false);
        var peer = Hull("PLAYER-TOW"); Set(peer, "nLoadState", Ship.Loaded.Full); Set(peer, "bTorchDriveThrusting", true);
        var group = new SolarSystem.DockGroup(); group.DockedShips.Add(new SolarSystem.DockGroupMember(player, false)); group.DockedShips.Add(new SolarSystem.DockGroupMember(peer, false));
        Set(player.objSS, "_dockGroup", group);
        Check(!Event() && created == 0, "Torching connected ship escaped the staging gate");
        Set(player.objSS, "_dockGroup", null);
        var centerBody = CrewSim.system.aBOs["BCER"]; double cached = centerBody.dTimeCalcLast;
        primary.objSS.vPosx = 12345; primary.objSS.vVelX = -1e-5;
        Check((bool)Call("CanStage"), "Stale station cache corrupted physical eligibility");
        Check(centerBody.dTimeCalcLast == cached && primary.objSS.vPosx == 12345, "Eligibility mutated native body/ship caches");
        AIShipManager.strATCLast = "MISSING"; Check(!Event() && created == 0, "Missing infrastructure failed open");
        Check(Math.Abs((float)AccessTools.Field(typeof(BeatManager), "fRoll").GetValue(null) + .05f) < 1e-7, "Refusal changed native chance consumption");
    }
    static AIShip Existing(double distance = 200) {
        var hull = Hull("EXISTING"); var ai = Empty<AIShip>(); SetPilot(ai, hull, "BCER");
        Pose(hull, primary, 2000 + distance, 0, .35); hull.objSS.fRot = 1.2345f;
        var command = Empty<FlyToPatrol>(); Set(command, "_shipUs", hull); Set(ai, "_activeCommand", command);
        AIShipManager.dictAIs["BCER"].Add(ai);
        foreach (var station in new[] { primary, terminal, haven }) Conditions[station.ShipCO]["StationMaxFactionNavy"] = 0;
        return ai;
    }
    static void RecruitmentChecks() {
        FreshWorld(); var ai = Existing();
        Check((bool)Call("Recruitable", ai), "Nearby ordinary patrol was excluded");
        double x = ai.Ship.objSS.vPosx, y = ai.Ship.objSS.vPosy, vx = ai.Ship.objSS.vVelX, vy = ai.Ship.objSS.vVelY;
        float rotation = ai.Ship.objSS.fRot;
        Check(Event(1) && created == 0, "Local recruitment failed or exceeded fleet caps");
        Check(ai.Ship.objSS.vPosx == x && ai.Ship.objSS.vPosy == y && ai.Ship.objSS.vVelX == vx && ai.Ship.objSS.vVelY == vy && ai.Ship.objSS.fRot == rotation,
            "Existing actor was teleported, speed-matched or randomly rotated");
        foreach (string state in new[] { "combat", "repair", "return", "bounty", "queue", "reaction", "docked", "distant", "fuel", "station-target", "locked", "paused", "player-owned" }) {
            FreshWorld(); ai = Existing(state == "distant" ? 1000 : 200);
            switch (state) {
                case "combat": ai.Blackboard.Set("InCombat", true); break;
                case "repair": ai.Blackboard.Set("NeedsRepairs", true); break;
                case "return": ai.Blackboard.Set("TemplateExample.StationReturn.Destination", "BCER"); break;
                case "bounty": Conditions[ai.AICharacter.CaptainUs]["IsBountyTarget"] = 1; break;
                case "queue": ai.AICharacter.PriorityQueue.EnqueueCommand(Empty<FlyToPatrol>()); break;
                case "reaction": Set(ai, "_activeReaction", Empty<Hold>()); break;
                case "docked": Set(ai.Ship, "aDocked", new Dictionary<string, Ship> { ["peer"] = player }); break;
                case "fuel": lowFuel = true; break;
                case "station-target": ai.Ship.shipScanTarget = primary; break;
                case "locked": ai.Ship.objSS.bBOLocked = true; break;
                case "paused": ai.Ship.fAIPauseTimer = StarSystem.fEpoch + 60; break;
                case "player-owned": Owners[ai.Ship.strRegID] = "player"; break;
            }
            Check(!(bool)Call("Recruitable", ai), "Busy/protected actor accepted: " + state);
            Check(!Event() && created == 0 && AIShipManager.dictAIs["BCER"].Count == 1, "Excluded actor opened a fleet-cap slot: " + state);
        }
        FreshWorld(); ai = Existing(); productionComposition(ai);
    }
    static void productionComposition(AIShip ai) {
        // Exact IL matching must reject an unrelated/empty future layout.
        bool rejected = false;
        try {
            var method = AccessTools.Method(PatchType, "Transpiler");
            var dynamic = new System.Reflection.Emit.DynamicMethod("probe", typeof(void), Type.EmptyTypes);
            method.Invoke(null, new object[] { Array.Empty<CodeInstruction>(), dynamic.GetILGenerator(), AccessTools.Method(typeof(BeatManager), "FactionShip") });
        } catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is InvalidOperationException) { rejected = true; }
        Check(rejected, "Unknown native staging layout was accepted");
    }
}
