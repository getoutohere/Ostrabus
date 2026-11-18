using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ostrabus.ShipNavigation;
using Ostranauts.Core;
using Ostranauts.ShipGUIs.NavStation;
using Ostranauts.ShipGUIs.Utilities;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.AIPilots.Interfaces;
using Ostranauts.Ships.Commands;

static partial class Program {
    static int checks, stops;
    static bool playerManned, detour, throwFuture;
    static JObject fixture;
    static Ship station;
    static readonly MethodInfo Acceleration = AccessTools.Method(typeof(FlyToPath), "GetAcceleration");

    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Set(object obj, string field, object value) => AccessTools.Field(obj.GetType(), field).SetValue(obj, value);
    static void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = ReferenceEquals(__0, __1); return false; }
    static bool Unequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = !ReferenceEquals(__0, __1); return false; }
    static bool Player(ref bool __result) { __result = playerManned; return false; }
    static bool PilotPresent(ref bool __result) { __result = true; return false; }
    static bool Cargo(ref double __result) { __result = 0; return false; }
    static bool Stop() { stops++; return false; }
    static bool Gpm(ref string __result) { __result = "0"; return false; }
    static bool Gravity(ref BodyOrbit boClosest, ref BodyOrbit __result) {
        boClosest = CrewSim.system.aBOs["Venus"]; __result = boClosest; return false;
    }
    static bool Future(double dTime) {
        if (throwFuture && dTime > StarSystem.fEpoch + 1) throw new InvalidOperationException("fixture future ephemeris failure");
        return true;
    }
    static bool Detour(ShipSitu __0, ShipSitu __1, double __2, bool __3, ref ShipSitu __result) {
        __result = null;
        if (detour) {
            detour = false;
            __result = new ShipSitu(station.objSS, false);
            __result.bIsBO = false; __result.bBOLocked = true;
            __result.vBOOffsetx = 100000d / 149597872000d;
            __result.IsRegionBorderPoint = true;
        }
        return false;
    }
    static void World() {
        playerManned = detour = throwFuture = false;
        CrewSim.coPlayer = null;
        var world = Empty<StarSystem>();
        world.aBOs = new Dictionary<string, BodyOrbit>();
        world.dictShips = new Dictionary<string, Ship>();
        CrewSim.system = world;
        foreach (var row in fixture["bodies"]) {
            var body = new BodyOrbit(row.ToObject<JsonBodyOrbitSave>());
            world.aBOs.Add(body.strName, body);
        }
        foreach (string link in fixture["hierarchy"]) {
            string[] pair = link.Split('='); world.aBOs[pair[0]].boParent = world.aBOs[pair[1]];
        }
        world.boStar = world.aBOs["Sol"];
        StarSystem.fEpoch = (double)fixture["epoch"];
        foreach (var body in world.aBOs.Values) body.UpdateTime(StarSystem.fEpoch);
        station = Empty<Ship>(); station.strRegID = "VORB";
        Set(station, "aDockingPorts", new List<string> { "TEST-PORT" });
        Set(station, "<objSS>k__BackingField", new ShipSitu(false) {
            bIsBO = true, strBOPORShip = "VORB", Size = (int)fixture["targetSize"]
        });
        station.objSS.UpdateTime(StarSystem.fEpoch);
        world.dictShips.Add(station.strRegID, station);
    }
    static Ship Vessel(double massMultiplier = 1, bool ai = true) {
        var ship = Empty<Ship>(); ship.strRegID = "TEST-VESSEL";
        Set(ship, "nLoadState", Ship.Loaded.Shallow); ship.IsAIShip = ai;
        Set(ship, "fShallowMass", (double)fixture["massKg"] * massMultiplier);
        Set(ship, "fFuelEfficiencyMod", 1d);
        ship.RCSCount = (float)fixture["rcsCount"];
        Set(ship, "nRCSCountRaw", (int)fixture["rcsCount"]);
        Set(ship, "fRCSExhaustV", (float)fixture["rcsCount"]);
        ship.fFusionThrustMax = (double)fixture["fusionThrustMax"];
        ship.fFusionPelletMax = (double)fixture["fusionPelletMax"];
        ship.fShallowRCSRemass = (double)fixture["rcsFuelKg"];
        Set(ship, "CachedAllDockedShips", new List<Ship>()); Set(ship, "aCores", new List<CondOwner>());
        Set(ship, "<objSS>k__BackingField", new ShipSitu(fixture["origin"].ToObject<JsonShipSitu>()));
        ship.shipScanTarget = station; ship.shipSituTarget = station.objSS; ship.targetRegID = station.strRegID;
        return ship;
    }
    static Pilot Controller(Ship ship, AIType role = AIType.HaulerCargo) => new Pilot {
        ShipUs = ship, Role = role, Speed = AIShip.CalculateMaxSpeed(ship, false), Blackboard = new Blackboard()
    };
    static NavData Plan(Ship ship, float? limit = null, FlyToPath planner = null) {
        planner ??= new FlyToPath(Controller(ship)); planner.nRecursion = 0;
        float limiter = limit ?? NavModTorchDrive.GetLimiterSafetyMax(ship);
        return planner.PlanTrip4(
            new NavDataPoint(StarSystem.fEpoch, new ShipSitu(ship.objSS, false), ship.fShallowRCSRemass, 0, 0, ship.RCSCount),
            new NavDataPoint(StarSystem.fEpoch, new ShipSitu(station.objSS, false)), limiter, 1);
    }
    static NavDataPoint Target() => new NavDataPoint(StarSystem.fEpoch, new ShipSitu(station.objSS, false));
    static NavData Synthetic(Ship ship, double offsetMetres = 0, bool torch = false, double seconds = 600) {
        var target = new ShipSitu(station.objSS, false);
        target.UpdateTime(StarSystem.fEpoch + seconds);
        var route = new NavData(ship);
        route.AddNavDataPoint(new NavDataPoint(StarSystem.fEpoch, new ShipSitu(ship.objSS, false), 0, torch ? .1 : 0, torch ? 100 : 0, 10));
        target.vPosx += offsetMetres / 149597872000d;
        route.AddNavDataPoint(new NavDataPoint(StarSystem.fEpoch + seconds, target, -100, 0, 0, 10));
        station.objSS.UpdateTime(StarSystem.fEpoch);
        return route;
    }
    static int Main() {
        fixture = JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RcsStationApproach.json")));
        var engine = new Harmony("offline.ai-rendezvous.environment");
        var production = new Harmony("offline.ai-rendezvous.production");
        void Prefix(Type type, string name, string handler) => engine.Patch(AccessTools.Method(type, name), prefix: new HarmonyMethod(typeof(Program), handler));
        try {
            Prefix(typeof(UnityEngine.Object), "op_Equality", nameof(Equal));
            Prefix(typeof(UnityEngine.Object), "op_Inequality", nameof(Unequal));
            engine.Patch(AccessTools.PropertyGetter(typeof(Ship), nameof(Ship.NavPlayerManned)), prefix: new HarmonyMethod(typeof(Program), nameof(Player)));
            engine.Patch(AccessTools.PropertyGetter(typeof(Ship), nameof(Ship.NavAIManned)), prefix: new HarmonyMethod(typeof(Program), nameof(PilotPresent)));
            Prefix(typeof(Ostranauts.Trading.MarketManager), "GetCargoMassForShip", nameof(Cargo));
            Prefix(typeof(StarSystem), "GetGreatestGravBO", nameof(Gravity));
            engine.Patch(AccessTools.Method(typeof(FlightCPU), "GetDockingNavPoint", new[] { typeof(ShipSitu), typeof(ShipSitu), typeof(double), typeof(bool) }), prefix: new HarmonyMethod(typeof(Program), nameof(Detour)));
            Prefix(typeof(BodyOrbit), "UpdateTime", nameof(Future));
            Prefix(typeof(Ship), nameof(Ship.Maneuver), nameof(Stop));
            Prefix(typeof(Ship), nameof(Ship.GetReactorGPMValue), nameof(Gpm));
            World();
            var broken = Plan(Vessel());
            Check(broken != null && broken.Destination.ArrivalTime - broken.Origin.ArrivalTime > 86400 * 100,
                "Native moving-station divergence control did not reproduce");
            Check(!AiRendezvous.Arrives(broken, Target()), "Runaway native course passed rendezvous validation");
            production.CreateClassProcessor(typeof(AiRcsAccelerationPatch)).Patch();
            production.CreateClassProcessor(typeof(AiRendezvousValidationPatch)).Patch();
            production.CreateClassProcessor(typeof(FreightRendezvousFailurePatch)).Patch();
            PlanningChecks(); ValidationChecks(); FailureChecks();
            Console.WriteLine(checks + " native AI RCS/rendezvous/freight-destination checks passed.");
            return 0;
        } finally { production.UnpatchSelf(); engine.UnpatchSelf(); CrewSim.system = null; CrewSim.coPlayer = null; }
    }
    sealed class Pilot : IAICharacter {
        internal double Speed;
        internal AIType Role;
        public Ship ShipUs { get; set; }
        public CondOwner CaptainUs => null;
        public QueuedCommandsNode PriorityQueue { get; set; }
        public ICommand BehaviorTree { get; set; }
        public Blackboard Blackboard { get; set; }
        public AIType AIType => Role;
        public TargetData GetTarget() => null;
        public TargetData GetTargetHome() => null;
        public double MaxSpeed(bool? inAtmo = null) => Speed;
        public ICommand FFWD(ICommand command, Blackboard board) => command;
    }
}
