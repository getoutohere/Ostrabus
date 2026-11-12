using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using Ostrabus.ShipNavigation.Towing;
using Ostranauts.Core.Models;
using Ostranauts.ShipGUIs.Utilities;
using Ostranauts.Ships.Commands;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Utils.Models;
using SolarSystem;
using UnityEngine;

static partial class Program {
    static int checks, playbackCalls;
    static NavData throwCourse;
    static bool fastForward;
    static readonly Dictionary<Ship, double> masses = new Dictionary<Ship, double>();
    static readonly Dictionary<Ship, double> loads = new Dictionary<Ship, double>();
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Set(object target, string field, object value) => AccessTools.Field(target.GetType(), field).SetValue(target, value);
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static void Near(double value, double expected, string message, double tolerance = 1e-12) => Check(Math.Abs(value - expected) <= tolerance, message + ": " + value + " != " + expected);
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = ReferenceEquals(__0, __1); return false; }
    static bool Unequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = !ReferenceEquals(__0, __1); return false; }
    static bool Mass(Ship __instance, ref double __result) { __result = masses[__instance]; return false; }
    static bool Gravity() => false;
    static bool Wear(Ship __instance, double accelerationG, double deltaTime) { loads[__instance] = accelerationG * deltaTime; return false; }
    static void CountPlayback(NavData __instance) {
        playbackCalls++;
        if (ReferenceEquals(__instance, throwCourse)) throw new InvalidOperationException("fixture playback failure");
    }
    static bool IsFastForward(ref bool __result) { __result = fastForward; return false; }
    static bool Skip() => false;
    static bool NoController(ref AIShip __result) { __result = null; return false; }
    static Ship Make(string id, double mass) {
        var ship = Empty<Ship>();
        ship.strRegID = id;
        Set(ship, "<objSS>k__BackingField", new ShipSitu(false));
        Set(ship, "nLoadState", Ship.Loaded.Shallow);
        Set(ship, "aDocked", new Dictionary<string, Ship>());
        Set(ship, "aCores", new List<CondOwner>());
        Set(ship, "aWPs", new List<WaypointShip>());
        ship.fFusionThrustMax = 1000000;
        ship.fShallowFusionRemain = 100000;
        Set(ship, "fFuelEfficiencyMod", 1d);
        masses[ship] = mass;
        if (CrewSim.system == null) CrewSim.system = Empty<StarSystem>();
        if (CrewSim.system.dictShips == null) CrewSim.system.dictShips = new Dictionary<string, Ship>();
        CrewSim.system.dictShips[id] = ship;
        return ship;
    }
    static DockGroup Join(Ship a, Ship b, double offset = 1e-8, float rotation = .4f) {
        ((Dictionary<string, Ship>)AccessTools.Field(typeof(Ship), "aDocked").GetValue(a))["a"] = b;
        ((Dictionary<string, Ship>)AccessTools.Field(typeof(Ship), "aDocked").GetValue(b))["b"] = a;
        Set(a, "aSecuredTowBraces", new Dictionary<string, string> { ["brace"] = "a" });
        var group = new DockGroup { Anchor = new ShipSitu(false) };
        foreach (var ship in new[] { a, b }) {
            ship.objSS._dockGroup = group;
            ship.objSS._dockOffsetPos = new Point(ship == a ? offset : -offset, offset / 3);
            ship.objSS._dockOffsetRot = ship == a ? rotation : -rotation;
            group.DockedShips.Add(new DockGroupMember(ship, false));
        }
        return group;
    }
    static NavData Course(Ship ship, double start = 1000, double duration = 100) {
        var nav = new NavData(ship);
        for (int i = 0; i < 2; i++) {
            var situ = new ShipSitu(false) { vPosx = 1 + i * 1e-6, vPosy = 2 + i * 2e-6, fRot = .7f, bIgnoreGrav = true };
            situ.vVelX = 1e-8; situ.vVelY = 2e-8;
            situ.vAccIn = new Vector2(1e-12f, 2e-12f);
            nav.AddNavDataPoint(new NavDataPoint(start + duration * i, situ, 100, .2, 100000 - duration * i));
        }
        ship.objSS.NavData = nav;
        return nav;
    }
    static NavData BurnCoastBrake(Ship ship) {
        var path = Empty<FlyToPath>(); Set(path, "_shipUs", ship);
        var nav = new NavData(ship);
        nav.AddNavDataPoint(new NavDataPoint(1000, new ShipSitu(false), 100, 0, 100000));
        var destination = new NavDataPoint(1000, new ShipSitu(false), 100, 0, 100000);
        var generate = AccessTools.Method(typeof(FlyToPath), "PlotBurnLoop");
        double acceleration = ship.GetMaxTorchThrust(.2f);
        foreach (var stage in new[] { (1200d, acceleration, 2, 0), (21600d, 0d, 0, 0), (1200d, -acceleration, 0, 2) }) {
            generate.Invoke(path, new object[] { nav, 10, stage.Item1 / 10, new Point(stage.Item2, 0), true, false,
                .2f, destination, ":tow-check", stage.Item3, stage.Item4 });
        }
        ship.objSS.NavData = nav;
        return nav;
    }
    static void Step(Ship tug, Ship tow, DockGroup group, double seconds) {
        tug.objSS.TimeAdvance(seconds);
        tow.objSS.TimeAdvance(seconds);
        group.TimeAdvance(seconds);
    }
    static void Baseline() {
        var tug = Make("TUG", 25000); var tow = Make("TOW", 75000);
        var group = Join(tug, tow);
        var course = Course(tug);
        StarSystem.fEpoch = 1050;
        tug.objSS.TimeAdvance(50);
        double plannedX = tug.objSS.vPosx;
        Check(plannedX > 1, "Native course fixture did not execute.");
        Near(tug.fShallowFusionRemain, 99950, "Native playback consumes torch budget before group integration", .01);
        group.TimeAdvance(50);
        Check(Math.Abs(tug.objSS.vPosx - plannedX) > .1, "Native dock group unexpectedly preserved its member's course.");
        Check(group.Anchor.NavData == null && ReferenceEquals(tug.objSS.NavData, course), "Native plan is not exclusively on its tug.");
        double solo = tug.GetMaxTorchThrust(1);
        group.DockedShips.Clear();
        Near(tug.GetMaxTorchThrust(1), solo, "Native torch acceleration ignores the tow mass");
        Console.WriteLine("REPRODUCED: member autopilot burns fuel, then group integration overwrites its route; torch acceleration ignores attached mass.");
    }
    static void Main() {
        var engine = new Harmony("offline.torch-tow.engine");
        engine.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Equality"), prefix: new HarmonyMethod(typeof(Program), nameof(Equal)));
        engine.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Inequality"), prefix: new HarmonyMethod(typeof(Program), nameof(Unequal)));
        engine.Patch(AccessTools.PropertyGetter(typeof(Ship), nameof(Ship.Mass)), prefix: new HarmonyMethod(typeof(Program), nameof(Mass)));
        engine.Patch(AccessTools.PropertySetter(typeof(Ship), nameof(Ship.Gravity)), prefix: new HarmonyMethod(typeof(Program), nameof(Gravity)));
        engine.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.AccrueTowGroupLoad)), prefix: new HarmonyMethod(typeof(Program), nameof(Wear)));
        engine.Patch(AccessTools.Method(typeof(NavData), nameof(NavData.TimeAdvance)), prefix: new HarmonyMethod(typeof(Program), nameof(CountPlayback)));
        engine.Patch(AccessTools.PropertyGetter(typeof(GUIFFWD), nameof(GUIFFWD.Active)), prefix: new HarmonyMethod(typeof(Program), nameof(IsFastForward)));
        engine.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.ClearShipTarget)), prefix: new HarmonyMethod(typeof(Program), nameof(Skip)));
        engine.Patch(AccessTools.Method(typeof(AIShipManager), nameof(AIShipManager.GetAIShipByRegID)), prefix: new HarmonyMethod(typeof(Program), nameof(NoController)));
        engine.Patch(AccessTools.Method(typeof(Debug), nameof(Debug.LogWarning), new[] { typeof(object) }), prefix: new HarmonyMethod(typeof(Program), nameof(Skip)));
        foreach (var method in AccessTools.GetDeclaredMethods(typeof(Ship))) if (method.Name == nameof(Ship.LogAdd))
            engine.Patch(method, prefix: new HarmonyMethod(typeof(Program), nameof(Skip)));
        try {
            Baseline();
            var production = new Harmony("offline.torch-tow.production");
            try {
                production.CreateClassProcessor(typeof(DockedCourseMemberPatch)).Patch();
                production.CreateClassProcessor(typeof(DockedCourseGroupPatch)).Patch();
                production.CreateClassProcessor(typeof(TorchTowMassPatch)).Patch();
                RegressionChecks();
                StationDepartureChecks();
                FullPlannerChecks();
                BraceAndAssetChecks(engine);
                BraceReleaseChecks(engine);
            } finally { production.UnpatchSelf(); }
            Console.WriteLine("Torch tow: " + checks + " checks passed.");
        } finally { engine.UnpatchSelf(); masses.Clear(); loads.Clear(); }
    }
}
