using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ostrabus.ShipNavigation;
using Ostranauts.Core;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.AIPilots.Interfaces;
using Ostranauts.Ships.Commands;
using Ostranauts.ShipGUIs.Utilities;

static class Program {
    const double AU = 149597872;
    static int checks;
    static Ship home;
    static bool missingRegion;
    static double epoch;
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Field(object obj, string name, object value) => AccessTools.Field(obj.GetType(), name).SetValue(obj, value);
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = ReferenceEquals(__0, __1); return false; }
    static bool Unequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = !ReferenceEquals(__0, __1); return false; }
    static bool Station(ref Ship __result) { __result = missingRegion ? null : home; return false; }
    static bool IsStation(Ship __instance, ref bool __result) { __result = ReferenceEquals(__instance, home); return false; }
    static bool Safe(ShipSitu objSS, double fRadMin, double dX, double dY, ref bool __result) { objSS.vPosx = dX + fRadMin; objSS.vPosy = dY; __result = true; return false; }
    static bool No(ref bool __result) { __result = false; return false; }
    static bool Quiet() => false;
    static bool EmptyDocked(ref IReadOnlyList<Ship> __result) { __result = Array.Empty<Ship>(); return false; }
    static bool NoSelected(ref CondOwner __result) { __result = null; return false; }
    static bool NoPois(ref List<Ship> __result) { __result = new List<Ship>(); return false; }
    static bool Owner(ref string __result) { __result = "OWNER"; return false; }
    static bool Speed(ref double __result) { __result = 1e-8; return false; }
    static void AddBodies(JObject fixture) {
        var world = CrewSim.system;
        foreach (var token in fixture["bodies"]) { var body = new BodyOrbit(token.ToObject<JsonBodyOrbitSave>()); world.aBOs[body.strName] = body; }
        foreach (string pair in fixture["parents"].Values<string>()) {
            string[] kv = pair.Split('=');
            if (world.aBOs.TryGetValue(kv[0], out var child) && world.aBOs.TryGetValue(kv[1], out var parent)) child.boParent = parent;
        }
    }
    static Ship Make(string id, string reference, bool station = false) {
        var ship = Empty<Ship>(); ship.strRegID = id; ship.origin = "MSUZ"; ship.IsAIShip = !station;
        var situ = new ShipSitu(false) { strBOPORShip = reference, bIsBO = station, bIsRegion = station };
        if (station) situ.UpdateTime(epoch);
        Field(ship, "<objSS>k__BackingField", situ);
        var co = Empty<CondOwner>();
        var influence = Empty<Condition>(); influence.fCount = 4000;
        co.mapConds = new Dictionary<string, Condition> { ["StationInfluenceRange"] = influence };
        Field(ship, "<ShipCO>k__BackingField", co);
        CrewSim.system.dictShips[id] = ship;
        return ship;
    }
    static ShipSitu PointAtHome(double km = 300) {
        home.objSS.UpdateTime(epoch);
        return new ShipSitu(false) { vPosx = home.objSS.vPosx + km / AU, vPosy = home.objSS.vPosy };
    }
    static T Pilot<T>(Ship ship) where T : BaseCharacter {
        var pilot = Empty<T>(); Field(pilot, "<ShipUs>k__BackingField", ship);
        pilot.Blackboard = new Blackboard();
        return pilot;
    }
    static List<Tuple<double, ShipSitu>> Spots(BaseCharacter pilot) => (List<Tuple<double, ShipSitu>>)AccessTools.Method(pilot.GetType(), "GetRandomSpotsWithinInfluenceRange").Invoke(pilot, new object[] { 3 });
    static void Main() {
        var fixture = new Harmony("offline.local-frame.fixture");
        var patch = new Harmony("offline.local-frame.production");
        void Prefix(MethodBase method, string name) => fixture.Patch(method, prefix: new HarmonyMethod(typeof(Program), name));
        try {
            Prefix(AccessTools.Method(typeof(UnityEngine.Object), "op_Equality"), nameof(Equal));
            Prefix(AccessTools.Method(typeof(UnityEngine.Object), "op_Inequality"), nameof(Unequal));
            Prefix(AccessTools.Method(typeof(StarSystem), nameof(StarSystem.GetNearestStationRegional)), nameof(Station));
            Prefix(AccessTools.Method(typeof(StarSystem), nameof(StarSystem.GetNearestStation)), nameof(Station));
            Prefix(AccessTools.Method(typeof(StarSystem), nameof(StarSystem.GetShipOwner)), nameof(Owner));
            Prefix(AccessTools.Method(typeof(StarSystem), nameof(StarSystem.SetSituToRandomSafeCoords)), nameof(Safe));
            Prefix(AccessTools.Method(typeof(Ship), nameof(Ship.IsStation)), nameof(IsStation));
            Prefix(AccessTools.Method(typeof(Ship), nameof(Ship.Maneuver)), nameof(Quiet));
            Prefix(AccessTools.Method(typeof(Ship), nameof(Ship.ClearShipTarget)), nameof(Quiet));
            Prefix(AccessTools.Method(typeof(Ship), nameof(Ship.GetAllDockedShips)), nameof(EmptyDocked));
            Prefix(AccessTools.Method(typeof(CrewSim), nameof(CrewSim.GetSelectedCrew)), nameof(NoSelected));
            Prefix(AccessTools.Method(typeof(GUIOrbitDraw), nameof(GUIOrbitDraw.TriggerShipRedraw)), nameof(Quiet));
            Prefix(AccessTools.Method(typeof(AIShipManager), nameof(AIShipManager.GetPOIs)), nameof(NoPois));
            Prefix(AccessTools.Method(typeof(NavyPilot), nameof(NavyPilot.MaxSpeed)), nameof(Speed));
            Prefix(AccessTools.Method(typeof(BlockadePilot), nameof(BlockadePilot.MaxSpeed)), nameof(Speed));
            Prefix(AccessTools.Method(typeof(PolicePilot), nameof(PolicePilot.MaxSpeed)), nameof(Speed));
            foreach (var method in AccessTools.GetDeclaredMethods(typeof(AIShipManager)).Where(m => m.Name == "BingoFuelCheck")) Prefix(method, nameof(No));
            var world = CrewSim.system = Empty<StarSystem>();
            world.aBOs = new Dictionary<string, BodyOrbit>(); world.dictShips = new Dictionary<string, Ship>();
            AddBodies(JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Orbits.json"))));
            var recorded = JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Frames.json")));
            AddBodies(recorded); epoch = (double)recorded["epoch"]; StarSystem.fEpoch = epoch;
            foreach (var body in world.aBOs.Values) body.UpdateTime(epoch);
            home = Make("MSUZ", "MSUZ", station: true);
            var ship = Make("AI", "Sol"); ship.objSS.CopyFrom(PointAtHome(), false);
            AIShipManager.dictAIs = new Dictionary<string, List<AIShip>> { ["MSUZ"] = new List<AIShip>() }; AIShipManager.strATCLast = "MSUZ";
            var natural = world.GetNearestBO(ship.objSS, epoch, false);
            Check(natural.strName == "Vesta", "Recorded Panmen geometry did not select Vesta natively");
            Check((home.objSS.vPos - natural.vPos).magnitude > .8, "Recorded natural reference unexpectedly local");
            var navy = Pilot<NavyPilot>(ship); var blockade = Pilot<BlockadePilot>(ship);
            foreach (var pilot in new BaseCharacter[] { navy, blockade }) {
                var old = Spots(pilot);
                Check(old.Count == 3 && old.All(x => x.Item2.strBOPORShip == "Vesta"), "Native patrol reference defect not reproduced: " + pilot.GetType().Name);
                var point = old[0].Item2; var start = point.vPos - home.objSS.vPos;
                point.UpdateTime(epoch + 18000); home.objSS.UpdateTime(epoch + 18000);
                Check((point.vPos - home.objSS.vPos - start).magnitude * AU > 100000, "Native five-hour separation not reproduced");
                home.objSS.UpdateTime(epoch);
            }
            var pirate = Pilot<PiratePilot>(ship);
            var oldAmbush = pirate.GetTarget();
            Check(oldAmbush.Situ.strBOPORShip == "Vesta" && oldAmbush.Situ.GetDistance(home.objSS) > .8, "Native pirate destination did not jump to remote Vesta");
            var police = Pilot<PolicePilot>(ship);
            var fly = Empty<FlyTo>(); police.BehaviorTree = new SelectorCommand("Main", fly);
            Check(ReferenceEquals(police.FFWD(null, new Blackboard()), fly) && ship.objSS.GetDistance(home.objSS) > .8, "Native police SFF relocation defect not reproduced");
            ship.objSS.CopyFrom(PointAtHome(), false);
            Check(patch.CreateClassProcessor(typeof(LocalNavigationFramePatch)).Patch().Count == 13, "Local call-site patch count changed");
            Check(patch.CreateClassProcessor(typeof(LocalNavigationPlacementPatch)).Patch().Count == 2, "Local placement patch count changed");
            Check(patch.CreateClassProcessor(typeof(NavyCandidateFuelPatch)).Patch().Count == 1, "Fuel-candidate filter composition failed");
            foreach (var pilot in new BaseCharacter[] { navy, blockade }) {
                foreach (var pair in Spots(pilot)) {
                    var point = pair.Item2;
                    Check(point.strBOPORShip == "MSUZ", "Patrol point did not inherit regional reference");
                    var offset = point.vPos - home.objSS.vPos;
                    point.UpdateTime(epoch + 21600); home.objSS.UpdateTime(epoch + 21600);
                    Check((point.vPos - home.objSS.vPos - offset).magnitude * AU < .01, "Six-hour patrol offset drifted");
                    Check((point.vVel - home.objSS.vVel).magnitude * AU < 1e-7, "Patrol velocity differs from region");
                    home.objSS.UpdateTime(epoch);
                }
                var captain = Empty<CondOwner>(); Field(captain, "_strID", "OWNER"); Field(pilot, "<CaptainUs>k__BackingField", captain);
                var chosen = pilot.GetTarget();
                Check(chosen.Situ.strBOPORShip == "MSUZ" && chosen.Situ.GetDistance(home.objSS) * AU < 5000, "Final native patrol selection rebound the repaired candidate");
            }
            var ambush = pirate.GetTarget();
            Check(ambush.Situ.strBOPORShip == "MSUZ" && ambush.Situ.GetDistance(home.objSS) * AU < 500, "Pirate target remains at distant natural body");
            Check(ReferenceEquals(police.FFWD(null, new Blackboard()), fly), "Police SFF continuation changed");
            Check(ship.objSS.GetDistance(home.objSS) * AU < 151, "Police SFF still relocates to remote natural body");
            var actor = new TestPilot { ShipUs = ship };
            ship.bXPDRAntenna = true; ship.objSS.CopyFrom(PointAtHome(), false);
            new LockPirate(actor).RunCommand(new Blackboard());
            Check(ship.objSS.bBOLocked && ship.objSS.strBOPORShip == "MSUZ", "Native LockPirate still locks Vesta");
            foreach (BaseCommand command in new BaseCommand[] { new Lurk(actor), new Guard(actor) }) {
                ship.objSS.CopyFrom(PointAtHome(), false); ship.fAIPauseTimer = 0;
                command.RunCommand(new Blackboard());
                Check(ship.objSS.bBOLocked && ship.objSS.strBOPORShip == "MSUZ", "Native wait reference wrong: " + command.GetType().Name);
            }
            PlanetaryChecks();
            var free = PointAtHome(); free.LockToBO(epoch, false);
            Check(free.strBOPORShip == world.GetNearestBO(free, epoch, false).strName, "Unscoped native locking changed");
            missingRegion = true; var fallback = PointAtHome(); LocalNavigationFrame.Bind(fallback, epoch, false);
            Check(fallback.strBOPORShip == world.GetNearestBO(fallback, epoch, false).strName, "Absent regional station did not retain native fallback");
            missingRegion = false; var saved = home.objSS.strBOPORShip; home.objSS.strBOPORShip = "missing";
            Check(ReferenceEquals(LocalNavigationFrame.Select(home, natural), natural), "Unresolvable station reference did not preserve native fallback"); home.objSS.strBOPORShip = saved;
            home.bDestroyed = true; Check(ReferenceEquals(LocalNavigationFrame.Select(home, natural), natural), "Destroyed station selected"); home.bDestroyed = false;
            home.bRemove = true; Check(ReferenceEquals(LocalNavigationFrame.Select(home, natural), natural), "Retiring station selected"); home.bRemove = false;
            home.objSS.bIsBO = false; Check(ReferenceEquals(LocalNavigationFrame.Select(home, natural), natural), "Free station without authored reference selected"); home.objSS.bIsBO = true;
            var invalid = new ShipSitu(false) { vPosx = double.NaN };
            Check(ReferenceEquals(LocalNavigationFrame.At(invalid, natural), natural), "Nonfinite position selected a region");
            natural.UpdateTime(epoch);
            var nearVesta = new ShipSitu(false) { vPosx = natural.dXReal + (natural.fRadiusKM + 300) / AU, vPosy = natural.dYReal };
            Check(ReferenceEquals(LocalNavigationFrame.At(nearVesta, natural), natural), "Genuine Vesta gravity-well activity was pulled toward distant station");
            foreach (var method in new MethodBase[] {
                AccessTools.Method(typeof(StarSystem), nameof(StarSystem.GetNearestBO)),
                AccessTools.Method(typeof(StarSystem), nameof(StarSystem.GetGreatestGravBO)),
                AccessTools.Method(typeof(ShipSitu), nameof(ShipSitu.LockToBO), new[] { typeof(double), typeof(bool) }),
                AccessTools.Method(typeof(FlyToPath), nameof(FlyToPath.PlanTrip4)),
                AccessTools.Method(typeof(KeepAtDistance), nameof(KeepAtDistance.RunCommand)),
                AccessTools.Method(typeof(FlyToMissile), nameof(FlyToMissile.RunCommand)),
                AccessTools.Method(typeof(FlightCPU), nameof(FlightCPU.GetDockingNavPoint), new[] { typeof(ShipSitu), typeof(ShipSitu), typeof(double), typeof(bool) })
            }) Check(Harmony.GetPatchInfo(method)?.Owners.Contains(patch.Id) != true, "Local reference patch hooked physical/combat/interplanetary method: " + method);
            Console.WriteLine(checks + " local navigation frame checks passed: native patrol/ambush reproduction, patched selection/locking, six-hour offsets and planetary controls.");
        } finally { patch.UnpatchSelf(); fixture.UnpatchSelf(); }
    }
    static void PlanetaryChecks() {
        var world = CrewSim.system;
        foreach (var pair in new[] { ("VORB", "Venus"), ("OKLG", "1036 Ganymed"), ("BCRS", "BCRS_Anchor") }) {
            home = Make(pair.Item1, pair.Item1, true); home.objSS.UpdateTime(epoch);
            var natural = world.aBOs[pair.Item2];
            Check(ReferenceEquals(LocalNavigationFrame.Select(home, natural), natural), "Planetary ancestor reference changed: " + pair.Item1);
            var point = PointAtHome(); LocalNavigationFrame.BindExplicit(point, natural, epoch);
            Check(point.strBOPORShip == natural.strName, "Explicit planetary wait reference changed: " + pair.Item1);
        }
        var ceres = world.aBOs["Ceres"];
        var ground = new BodyOrbit("CeresHome", 1e-5, 1e-5, 0, 0, .001, .001, 100000, 1, ceres);
        world.aBOs[ground.strName] = ground; home = Make(ground.strName, ground.strName, true);
        Check(ReferenceEquals(LocalNavigationFrame.Select(home, ceres), ceres), "Ceres patrol reference changed");
        var mercury = new BodyOrbit("MercuryControl", .3075, .4667, 48.3, .206, .241, 2440, 3.3e23, 59, world.aBOs["Sol"]);
        var depot = new BodyOrbit("MercuryHome", 1e-5, 1e-5, 0, 0, .001, .001, 100000, 1, mercury);
        world.aBOs[mercury.strName] = mercury; world.aBOs[depot.strName] = depot;
        home = Make(depot.strName, depot.strName, true);
        Check(ReferenceEquals(LocalNavigationFrame.Select(home, mercury), mercury), "Mercury patrol reference changed");
        Check(ReferenceEquals(LocalNavigationFrame.Select(home, world.aBOs["Sol"]), depot), "Local coordinate bound to stationary Sol");
    }
    sealed class TestPilot : IAICharacter {
        public Ship ShipUs { get; set; }
        public CondOwner CaptainUs => null;
        public QueuedCommandsNode PriorityQueue { get; set; }
        public ICommand BehaviorTree { get; set; }
        public Blackboard Blackboard { get; set; }
        public AIType AIType => AIType.Pirate;
        public TargetData GetTarget() => null;
        public TargetData GetTargetHome() => null;
        public double MaxSpeed(bool? inAtmo = null) => 1e-8;
        public ICommand FFWD(ICommand command, Blackboard blackboard) => command;
    }
}
