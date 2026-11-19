using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ostranauts.Core;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.AIPilots.Interfaces;
using Ostranauts.Ships.Commands;
using Ostranauts.ShipGUIs.Utilities;
using Ostranauts.Trading;
using Ostrabus.ShipNavigation;

// Full native planner: real gravity/ephemerides, obstacle selection, no-wake
// departure, propulsion and fuel calculations. Only scene-dependent pilot
// availability and Unity object comparisons are fixture boundaries.
static class FreightCourseChecks {
    const double KmAU = 149597872d;
    internal static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    internal static void Set(object instance, string name, object value) => AccessTools.Field(instance.GetType(), name).SetValue(instance, value);
    internal static void Collections(object instance) {
        foreach (var field in instance.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)) {
            var type = field.FieldType;
            if (type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type) && !type.IsAbstract && !type.IsArray && type.GetConstructor(Type.EmptyTypes) != null)
                field.SetValue(instance, Activator.CreateInstance(type));
        }
    }
    static bool NotPlayer(ref bool __result) { __result = false; return false; }
    static bool HasPilot(ref bool __result) { __result = true; return false; }
    static bool Presentation() => false;
    static bool Scale(ref float __result) { __result = 1; return false; }
    static bool Elapsed(ref float __result) { __result = .125f; return false; }
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = ReferenceEquals(__0, __1); return false; }
    static bool Unequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = !ReferenceEquals(__0, __1); return false; }
    internal static void Scene(Harmony fixture) {
        fixture.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Equality"), prefix: new HarmonyMethod(typeof(FreightCourseChecks), nameof(Equal)));
        fixture.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Inequality"), prefix: new HarmonyMethod(typeof(FreightCourseChecks), nameof(Unequal)));
        fixture.Patch(AccessTools.PropertyGetter(typeof(Ship), nameof(Ship.NavPlayerManned)), prefix: new HarmonyMethod(typeof(FreightCourseChecks), nameof(NotPlayer)));
        fixture.Patch(AccessTools.PropertyGetter(typeof(Ship), nameof(Ship.NavAIManned)), prefix: new HarmonyMethod(typeof(FreightCourseChecks), nameof(HasPilot)));
        fixture.Patch(AccessTools.Method(typeof(GUIOrbitDraw), nameof(GUIOrbitDraw.TriggerShipRedraw)), prefix: new HarmonyMethod(typeof(FreightCourseChecks), nameof(Presentation)));
        fixture.Patch(AccessTools.PropertyGetter(typeof(UnityEngine.Time), "timeScale"), prefix: new HarmonyMethod(typeof(FreightCourseChecks), nameof(Scale)));
        fixture.Patch(AccessTools.Method(typeof(CrewSim), "TimeElapsedScaled"), prefix: new HarmonyMethod(typeof(FreightCourseChecks), nameof(Elapsed)));
    }
    static readonly JArray detours = new JArray();
    static void Detour(ShipSitu originSitu, ShipSitu targetSitu, double fEpoch, bool useTargetBo, ShipSitu __result) {
        if (detours.Count >= 32) return;
        detours.Add(new JObject {
            ["epoch"] = fEpoch, ["useTargetBo"] = useTargetBo,
            ["resultBody"] = __result?.strBOPORShip,
            ["originX"] = originSitu.vPosx, ["originY"] = originSitu.vPosy,
            ["targetX"] = targetSitu.vPosx, ["targetY"] = targetSitu.vPosy,
            ["resultX"] = __result?.vPosx, ["resultY"] = __result?.vPosy
        });
    }
    internal static StarSystem World(JObject data) {
        var world = Empty<StarSystem>(); Collections(world);
        Set(world, "_filteredBoCache", null); // Native lazy initialization must execute.
        CrewSim.system = world; CrewSim.coPlayer = null;
        AIShipManager.dictAIs = new Dictionary<string, List<AIShip>>();
        StarSystem.fEpoch = (double)data["epoch"];
        Set(world, "fGravAccelConstant", 2e-44f);
        foreach (var row in data["bodies"]) {
            var body = new BodyOrbit(row.ToObject<JsonBodyOrbitSave>());
            world.aBOs.Add(body.strName, body);
        }
        foreach (string text in data["hierarchy"]) {
            string[] pair = text.Split('=');
            if (!world.aBOs.TryGetValue(pair[0], out var child) || !world.aBOs.TryGetValue(pair[1], out var parent)) continue;
            child.boParent = parent;
            if (parent.boChildren == null) parent.boChildren = new List<BodyOrbit>();
            parent.boChildren.Add(child);
        }
        world.boStar = world.aBOs["Sol"];
        var stationAssets = JArray.Parse(File.ReadAllText(Ostrabus.Development.InstallationPaths.Data + "star_systems/star_system.json"))
            .Descendants().OfType<JObject>().Where(row => row["strShipType"] != null && row["strName"] != null)
            .GroupBy(row => (string)row["strName"]).ToDictionary(group => group.Key, group => group.First());
        foreach (var row in ((JObject)data["stationMotion"]).Properties()) {
            var ship = Empty<Ship>(); Collections(ship); ship.strRegID = row.Name;
            Set(ship, "<objSS>k__BackingField", new ShipSitu(row.Value.ToObject<JsonShipSitu>()));
            Set(ship, "nLoadState", Ship.Loaded.Shallow);
            var spec = data["stationSpecs"]?[row.Name];
            Set(ship, "aDockingPorts", spec?["aDockingPorts"]?.ToObject<List<string>>() ?? new List<string> { "fixture-port" });
            ship.Classification = spec?["ShipType"] != null ? (Ship.TypeClassification)(int)spec["ShipType"] :
                stationAssets.TryGetValue(row.Name, out var asset) && (string)asset["strOrbitType"] == "GROUND"
                    ? Ship.TypeClassification.GroundStation : Ship.TypeClassification.OrbitalStation;
            ship.HideFromSystem = (bool?)spec?["bShipHidden"] ?? false;
            ship.bNoCollisions = (bool?)spec?["bNoCollisions"] ?? false;
            world.dictShips.Add(ship.strRegID, ship);
        }
        var manager = Empty<MarketManager>(); Collections(manager);
        AccessTools.Field(typeof(MonoSingleton<MarketManager>), "_instance").SetValue(null, manager);
        foreach (var body in world.aBOs.Values) body.UpdateTime(StarSystem.fEpoch);
        foreach (var station in world.dictShips.Values) station.objSS.UpdateTime(StarSystem.fEpoch);
        return world;
    }
    internal static Ship Vessel(JToken row) {
        var saved = row.ToObject<JsonShip>();
        var ship = Empty<Ship>(); Collections(ship);
        ship.strRegID = saved.strRegID; ship.json = saved; ship.IsAIShip = true;
        ship.origin = saved.origin; ship.Classification = saved.ShipType;
        Set(ship, "nLoadState", Ship.Loaded.Shallow);
        Set(ship, "<objSS>k__BackingField", new ShipSitu(saved.objSS));
        ship.objSS.NavData = null;
        Set(ship, "fShallowMass", saved.fShallowMass);
        Set(ship, "fFuelEfficiencyMod", 1d);
        ship.RCSCount = saved.nRCSCount;
        Set(ship, "nRCSCountRaw", saved.nRCSCountRaw);
        Set(ship, "nRCSDistroCount", saved.nRCSDistroCount);
        Set(ship, "fRCSExhaustV", saved.nRCSExhaustV);
        ship.fShallowRCSRemass = saved.fShallowRCSRemass;
        ship.fShallowFusionRemain = saved.fShallowFusionRemain;
        ship.fFusionThrustMax = saved.fFusionThrustMax; ship.fFusionPelletMax = saved.fFusionPelletMax;
        ship.bFusionReactorRunning = saved.bFusionTorch;
        // Recover effective plotted mass from the saved acceleration and native
        // thrust law. This includes the historical virtual cargo without loading
        // or approximating its item assets. The independent saved-path control
        // does not rely on this derived input.
        var burn = saved.objSS.jnd?.aPoints?.Where(point => point.TorchCycle > 0 &&
            point.ObjSS.vAccIn.x * point.ObjSS.vAccIn.x + point.ObjSS.vAccIn.y * point.ObjSS.vAccIn.y > 0).FirstOrDefault();
        if (burn != null) {
            double acceleration = Math.Sqrt((double)burn.ObjSS.vAccIn.x * burn.ObjSS.vAccIn.x + (double)burn.ObjSS.vAccIn.y * burn.ObjSS.vAccIn.y);
            double mass = ship.GetMaxTorchThrust((float)burn.TorchCycle) * saved.fShallowMass / acceleration;
            if (mass > 0 && !double.IsInfinity(mass) && !double.IsNaN(mass)) Set(ship, "fShallowMass", mass);
            var points = saved.objSS.jnd.aPoints;
            for (int i = 1; i < points.Length; i++) {
                double duration = points[i].ArrivalTime - points[i - 1].ArrivalTime;
                double fuel = points[i - 1].TorchFuelLevel - points[i].TorchFuelLevel;
                if (points[i].TorchCycle <= 0 || !(duration > .01) || !(fuel > 0)) continue;
                double efficiency = fuel / duration;
                if (efficiency > 0 && efficiency <= 1.01) Set(ship, "fFuelEfficiencyMod", efficiency);
                break;
            }
        }
        ship.shipScanTarget = CrewSim.system.dictShips[saved.strScanTargetID];
        ship.shipSituTarget = ship.shipScanTarget.objSS; ship.targetRegID = saved.strScanTargetID;
        CrewSim.system.dictShips.Add(ship.strRegID, ship);
        return ship;
    }
    internal static (double Altitude, string Body) Minimum(NavData route) {
        double minimum = double.PositiveInfinity; string name = null;
        if (route == null) return (minimum, name);
        foreach (var point in route.GetNavDataPoints()) foreach (var body in CrewSim.system.aBOs.Values) {
            if (body.IsPlaceholder() || body.IsAsteroidField || !(body.fRadius > 0)) continue;
            body.UpdateTime(point.ArrivalTime);
            double dx = point.ObjSS.vPosx - body.dXReal, dy = point.ObjSS.vPosy - body.dYReal;
            double altitude = (Math.Sqrt(dx * dx + dy * dy) - body.fRadius) * KmAU;
            if (altitude < minimum) { minimum = altitude; name = body.strName; }
        }
        return (minimum, name);
    }
    internal static void Run(string path, string id, bool corrected = false) {
        var fixture = new Harmony("offline.freight-full-planner.scene");
        var production = new Harmony("offline.freight-full-planner.production");
        try {
            fixture.Patch(AccessTools.PropertyGetter(typeof(Ship), nameof(Ship.NavPlayerManned)), prefix: new HarmonyMethod(typeof(FreightCourseChecks), nameof(NotPlayer)));
            fixture.Patch(AccessTools.PropertyGetter(typeof(Ship), nameof(Ship.NavAIManned)), prefix: new HarmonyMethod(typeof(FreightCourseChecks), nameof(HasPilot)));
            fixture.Patch(AccessTools.Method(typeof(FlightCPU), nameof(FlightCPU.GetDockingNavPoint), new[] { typeof(ShipSitu), typeof(ShipSitu), typeof(double), typeof(bool) }), postfix: new HarmonyMethod(typeof(FreightCourseChecks), nameof(Detour)));
            detours.Clear();
            if (corrected) {
                production.CreateClassProcessor(typeof(AiRcsAccelerationPatch)).Patch();
                production.CreateClassProcessor(typeof(AiRendezvousValidationPatch)).Patch();
                production.CreateClassProcessor(typeof(DepartureWaypointPatch)).Patch();
            }
            var data = JObject.Parse(File.ReadAllText(path)); World(data);
            var ship = Vessel(data["ships"][id]);
            var pilot = new Pilot { ShipUs = ship, Blackboard = new Blackboard() };
            var planner = new FlyToPath(pilot);
            var origin = planner.CreateNavPointStatic(ship.objSS, StarSystem.fEpoch, bLockToBO: false);
            var target = planner.CreateNavPointStatic(ship.shipSituTarget, StarSystem.fEpoch);
            var sliders = planner.GetNavSliderValues();
            var result = planner.PlanTrip4(origin, target, sliders.Limit, sliders.Coast);
            var closest = Minimum(result);
            bool clear = CelestialRoutes.Clear(result, ship.objSS.GetRadiusAU(), StarSystem.fEpoch, out var obstruction);
            Console.WriteLine(new JObject {
                ["id"] = id, ["massKg"] = ship.Mass, ["limiter"] = sliders.Limit,
                ["route"] = result != null, ["points"] = result?.GetNavDataPoints().Count,
                ["seconds"] = result == null ? (double?)null : result.Destination.ArrivalTime - StarSystem.fEpoch,
                ["minimumPointAltitudeKm"] = closest.Altitude, ["body"] = closest.Body,
                ["recursion"] = planner.nRecursion, ["detours"] = detours,
                ["corrected"] = corrected, ["clear"] = clear, ["obstructionBody"] = obstruction.Body?.strName,
                ["obstructionEpoch"] = obstruction.Epoch, ["uncertain"] = obstruction.Uncertain
            }.ToString(Newtonsoft.Json.Formatting.None));
        } finally { production.UnpatchSelf(); fixture.UnpatchSelf(); CrewSim.system = null; }
    }
    internal sealed class Pilot : IAICharacter {
        public Ship ShipUs { get; set; }
        public CondOwner CaptainUs => null;
        public QueuedCommandsNode PriorityQueue { get; set; }
        public ICommand BehaviorTree { get; set; }
        public Blackboard Blackboard { get; set; }
        public AIType AIType => AIType.HaulerCargo;
        public TargetData GetTarget() => new TargetData(ShipUs.shipScanTarget);
        public TargetData GetTargetHome() => GetTarget();
        public double MaxSpeed(bool? inAtmo = null) => AIShip.CalculateMaxSpeed(ShipUs, null);
        public ICommand FFWD(ICommand command, Blackboard board) => command;
    }
}
