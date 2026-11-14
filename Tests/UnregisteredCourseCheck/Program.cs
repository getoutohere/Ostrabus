using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using Ostranauts.ShipGUIs.Utilities;
using UnityEngine;
using Ostrabus.ShipNavigation;

// Native unregister/registry/queue and course playback execute. Physical control
// output propagation, condition storage and waypoint ephemeris updates are fixture boundaries.
static partial class Program {
    static int checks;
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Set(object obj, string field, object value) => AccessTools.Field(obj.GetType(), field).SetValue(obj, value);
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = ReferenceEquals(__0, __1); return false; }
    static bool Unequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = !ReferenceEquals(__0, __1); return false; }
    static bool NoRotor(ref float __result) { __result = 0; return false; }
    static bool Quiet() => false;
    static bool NoReactor(ref CondOwner __result) { __result = null; return false; }
    sealed class Pilot : BaseCharacter {
        internal Action OnFastForward;
        public Pilot(Ship ship) { ShipUs = ship; }
        public override AIType AIType => AIType.Civilian;
        public override TargetData GetTarget() => null;
        public override double MaxSpeed(bool? inAtmo) => 0;
        public override ICommand FFWD(ICommand command, Blackboard board) {
            if (OnFastForward == null) return command;
            OnFastForward(); return null;
        }
    }
    static Ship Hull(string id) {
        var ship = Empty<Ship>(); ship.strRegID = id; ship.IsAIShip = true;
        Set(ship, "<objSS>k__BackingField", new ShipSitu(false));
        Set(ship, "<ShipCO>k__BackingField", Empty<CondOwner>());
        Set(ship, "nLoadState", Ship.Loaded.Shallow);
        Set(ship, "aDocked", new Dictionary<string, Ship>());
        var waypoints = AccessTools.Field(typeof(Ship), "aWPs");
        waypoints.SetValue(ship, Activator.CreateInstance(waypoints.FieldType));
        ship.objSS.vPosx = .001; ship.objSS.vVelX = 2e-8; ship.objSS.vVelY = -1e-8;
        ship.objSS.fW = .1f; ship.fShallowRCSRemass = 1000; ship.fShallowFusionRemain = 1000;
        ship.objSS.vAccIn = new Vector2(1e-10f, -8e-11f);
        ship.objSS.vAccRCS = new Vector2(-8e-11f, 1e-10f);
        ship.shipScanTarget = Empty<Ship>(); ship.shipSituTarget = new ShipSitu(false);
        CrewSim.system.dictShips[id] = ship;
        return ship;
    }
    static AIShip Register(Ship ship) {
        var ai = Empty<AIShip>(); Set(ai, "_shipUs", ship); Set(ai, "_aiCharacter", new Pilot(ship));
        AIShipManager.dictAIs["fixture"].Add(ai);
        return ai;
    }
    static NavData Course(Ship ship, bool torch) {
        var path = new NavData(ship);
        var first = new ShipSitu(false) { vPosx = .001, vVelX = 2e-8, vVelY = -1e-8 };
        var last = new ShipSitu(false) { vPosx = .0011, vVelX = 4e-8, vVelY = -1e-8 };
        var acceleration = new Vector2(1e-10f, -8e-11f);
        if (torch) first.vAccIn = last.vAccIn = acceleration;
        else first.vAccRCS = last.vAccRCS = acceleration;
        path.AddNavDataPoint(new NavDataPoint(1000, first, 1000, torch ? .3 : 0, 1000));
        path.AddNavDataPoint(new NavDataPoint(1100, last, 1000, torch ? .3 : 0, torch ? 990 : 1000));
        ship.objSS.NavData = path;
        return path;
    }
    static void Scenario(bool torch, bool cancelBeforeUnregister, bool hidden, bool productionRepair = false) {
        StarSystem.fEpoch = 1000;
        var ship = Hull((torch ? "torch" : "rcs") + (cancelBeforeUnregister ? "-candidate" : "-native"));
        Register(ship); var course = Course(ship, torch);
        double vx = ship.objSS.vVelX, vy = ship.objSS.vVelY; float spin = ship.objSS.fW;
        ship.HideFromSystem = hidden;
        if (cancelBeforeUnregister) ship.objSS.ResetNavData(); // Explicit offline candidate, no production hook.
        AIShipManager.UnregisterShip(ship);
        Check(AIShipManager.GetAIShipByRegID(ship.strRegID) == null, "Native controller was not unregistered");
        Check(ship.shipScanTarget == null && ship.shipSituTarget == null, "Native target cleanup did not execute");
        Check(ship.objSS.vAccIn.x == 0 && ship.objSS.vAccRCS.x == 0, "Native stop boundary was not honored");
        Check(ship.objSS.vVelX == vx && ship.objSS.vVelY == vy && ship.objSS.fW == spin, "Unregistration changed momentum/spin");
        if (cancelBeforeUnregister || productionRepair) {
            Check(ship.objSS.NavData == null, "Cancelled course survived unregistration");
            Check(ship.fShallowRCSRemass == 1000 && ship.fShallowFusionRemain == 1000, "Cancellation changed fuel");
            ship.objSS.TimeAdvance(5);
            Check(ship.objSS.vVelX == vx && ship.objSS.vVelY == vy && ship.objSS.fW == spin, "Cancelled ship did not retain free-coast momentum/spin");
            if (productionRepair) {
                StarSystem.fEpoch = 1050; ship.objSS.TimeAdvance(50);
                StarSystem.fEpoch = 1200; ship.objSS.TimeAdvance(150);
                Check(ship.objSS.NavData == null && ship.objSS.vAccIn.x == 0 && ship.objSS.vAccRCS.x == 0,
                    "Production withdrawal allowed abandoned playback to restore force");
                Check(ship.objSS.vVelX == vx && ship.objSS.vVelY == vy && ship.objSS.fW == spin &&
                    ship.fShallowRCSRemass == 1000 && ship.fShallowFusionRemain == 1000,
                    "Production withdrawal changed coasting or fuel after the old course expired");
            }
        } else {
            Check(ReferenceEquals(ship.objSS.NavData, course), "Native no longer retains the orphaned course");
            StarSystem.fEpoch = 1050;
            ship.objSS.TimeAdvance(1);
            Check(ship.objSS.vVelX > vx && ship.objSS.vPosx > .001, "Orphaned route did not advance an unregistered hidden hull");
            var force = torch ? ship.objSS.vAccIn : ship.objSS.vAccRCS;
            Check(force.x != 0 && force.y != 0, "Orphaned playback did not restore stopped acceleration");
            Check(AIShipManager.GetAIShipByRegID(ship.strRegID) == null && ship.HideFromSystem == hidden,
                "Playback unexpectedly required a registered/visible pilot");
            if (torch) Check(ship.fShallowFusionRemain < 1000, "Hidden orphaned torch playback did not consume fuel");
            StarSystem.fEpoch = 1200;
            course.TimeAdvance(ship.objSS);
            Check(ship.objSS.NavData == null, "Natural route completion did not remove the expired course");
            force = torch ? ship.objSS.vAccIn : ship.objSS.vAccRCS;
            Check(force.x != 0 && force.y != 0, "Native route completion no longer retains the orphaned final acceleration");
            double afterCourseVx = ship.objSS.vVelX, afterCourseVy = ship.objSS.vVelY;
            double fusion = ship.fShallowFusionRemain, rcs = ship.fShallowRCSRemass;
            ship.objSS.TimeAdvance(5);
            Check(Math.Abs((ship.objSS.vVelX - afterCourseVx) - force.x * 5.0) < 1e-20 &&
                Math.Abs((ship.objSS.vVelY - afterCourseVy) - force.y * 5.0) < 1e-20,
                "Raw force did not continue actual native acceleration after course expiry");
            Check(ship.fShallowFusionRemain == fusion && ship.fShallowRCSRemass == rcs,
                "Force-only integration unexpectedly consumed fuel");
        }
        Console.WriteLine((torch ? "torch" : "RCS") + (productionRepair ? ": production withdrawal preserves coasting/fuel"
            : cancelBeforeUnregister ? ": explicit cancellation candidate preserves momentum/fuel"
            : ": native orphaned playback restores force without AI"));
    }
    static int Main(string[] args) {
        if (args.Length >= 2 && (args[0] == "candidate-integration" || args[0] == "candidate-terminal-integration" || args[0] == "production-integration"))
            return CandidateIntegration(args);
        var fixture = new Harmony("offline.unregistered-course.fixture");
        var production = new Harmony("offline.unregistered-course.existing-flight-repair");
        void Patch(Type type, string name, string handler, Type[] args = null) => fixture.Patch(
            AccessTools.Method(type, name, args), prefix: new HarmonyMethod(typeof(Program), handler));
        try {
            Patch(typeof(UnityEngine.Object), "op_Equality", nameof(Equal)); Patch(typeof(UnityEngine.Object), "op_Inequality", nameof(Unequal));
            Patch(typeof(Ship), "get_CurrentRotorEfficiency", nameof(NoRotor));
            Patch(typeof(Ship), nameof(Ship.UpdateDockedShips), nameof(Quiet));
            Patch(typeof(GUIOrbitDraw), nameof(GUIOrbitDraw.TriggerShipRedraw), nameof(Quiet));
            Patch(typeof(CondOwner), nameof(CondOwner.SetCondAmount), nameof(Quiet));
            Patch(typeof(ShipSitu), nameof(ShipSitu.UpdateTime), nameof(Quiet));
            Patch(typeof(Ship), "get_Reactor", nameof(NoReactor));
            CrewSim.system = Empty<StarSystem>(); CrewSim.system.dictShips = new Dictionary<string, Ship>();
            AIShipManager.dictAIs = new Dictionary<string, List<AIShip>> { ["fixture"] = new List<AIShip>() };
            ((AIShipQueue)AccessTools.Field(typeof(AIShipManager), "_aIQueue").GetValue(null)).Clear();
            if (args.Length == 2 && args[0] == "production-external") {
                ProductionChecks(fixture, args[1]);
                Console.WriteLine(checks + " external-build production controller-lifetime checks passed.");
                return 0;
            }
            if (args.Length == 1 && args[0] == "production") {
                ProductionChecks(fixture);
                Console.WriteLine(checks + " production controller-lifetime checks passed.");
                return 0;
            }
            if (args.Length == 1 && args[0] == "caller-audit") {
                CallerAudit(fixture);
                Console.WriteLine(checks + " native controller-removal caller audit checks passed; no production patch installed.");
                return 0;
            }
            if (args.Length == 1 && args[0] == "continuation-audit") {
                ContinuationAudit(fixture);
                Console.WriteLine(checks + " native active-invocation continuation checks passed; no production patch installed.");
                return 0;
            }
            foreach (bool existingRepair in new[] { false, true }) {
                if (existingRepair) {
                    production.CreateClassProcessor(typeof(FlightControlHandoffPatch)).Patch();
                    production.CreateClassProcessor(typeof(ShallowTorchShutdownPatch)).Patch();
                    production.CreateClassProcessor(typeof(ShallowRcsTorchReleasePatch)).Patch();
                }
                foreach (bool torch in new[] { false, true }) foreach (bool cancel in new[] { false, true })
                foreach (bool hidden in new[] { false, true }) Scenario(torch, cancel, hidden);
            }
            OwnershipBoundaryChecks(fixture);
            Console.WriteLine(checks + " native unregistration/course-playback investigation checks passed; no production fix installed.");
            return 0;
        } finally { production.UnpatchSelf(); fixture.UnpatchSelf(); }
    }
}
