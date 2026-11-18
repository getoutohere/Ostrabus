using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots.Interfaces;
using Ostranauts.Ships.Commands;
using Ostranauts.ShipGUIs.Utilities;

namespace Ostrabus.ShipNavigation;

internal static class AiRendezvous {
    static readonly AccessTools.FieldRef<FlyToPath, IAICharacter> Pilot =
        AccessTools.FieldRefAccess<FlyToPath, IAICharacter>("_ai");

    internal struct Attempt {
        internal FlyToPath Planner;
        internal bool Rejected;
    }

    [ThreadStatic] static Attempt current;

    readonly struct BodyState {
        readonly BodyOrbit body;
        readonly double positionEpoch, velocityEpoch, x, y, vx, vy;

        internal BodyState(BodyOrbit body) {
            this.body = body;
            positionEpoch = body.dTimeCalcLast; velocityEpoch = body.dVelCalcLast;
            x = body.dXReal; y = body.dYReal; vx = body.dVelX; vy = body.dVelY;
        }

        internal void Restore() {
            body.dTimeCalcLast = positionEpoch; body.dVelCalcLast = velocityEpoch;
            body.dXReal = x; body.dYReal = y; body.dVelX = vx; body.dVelY = vy;
        }
    }

    internal static bool Applies(FlyToPath planner) {
        Ship ship = planner?.ShipUs;
        return ship != null && ship.IsAIShip && !ship.NavPlayerManned &&
            !ReferenceEquals(ship, CrewSim.coPlayer?.ship);
    }

    internal static Attempt Enter(FlyToPath planner) {
        Attempt previous = current;
        current = new Attempt { Planner = planner };
        return previous;
    }

    internal static void Leave(Attempt previous) => current = previous;

    static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    static bool Finite(ShipSitu point) => point != null &&
        Finite(point.vPosx) && Finite(point.vPosy) && Finite(point.vVelX) && Finite(point.vVelY) &&
        Finite(point.vAccIn.x) && Finite(point.vAccIn.y) &&
        Finite(point.vAccRCS.x) && Finite(point.vAccRCS.y);

    internal static bool Arrives(NavData route, NavDataPoint destination) {
        var points = route?.GetNavDataPoints();
        if (points == null || points.Count < 2 || destination?.ObjSS == null) return false;
        double previous = double.NegativeInfinity;
        ShipSitu previousMotion = null;
        foreach (var point in points) {
            // Negative fuel is permitted by native shallow AI. NaN/Infinity is not.
            if (point == null || !Finite(point.ArrivalTime) || point.ArrivalTime < previous ||
                !Finite(point.ObjSS) || !Finite(point.FuelLevel) || !Finite(point.TorchFuelLevel) || !Finite(point.TorchCycle)) return false;
            if (previousMotion != null && point.ArrivalTime == previous &&
                ((point.ObjSS.vPos - previousMotion.vPos).magnitude > .01 / 149597872000d ||
                 (point.ObjSS.vVel - previousMotion.vVel).magnitude > .01 / 149597872000d)) return false;
            previous = point.ArrivalTime;
            previousMotion = point.ObjSS;
        }

        var target = new ShipSitu(destination.ObjSS, createPath: false);
        target.NavData = null;
        bool analytical = target.bIsBO || target.bBOLocked || target.bOrbitLocked;
        BodyOrbit body = analytical && target.strBOPORShip != null
            ? CrewSim.system?.GetBO(target.strBOPORShip) : null;
        if (analytical && body == null) return false;
        List<BodyState> bodyStates = null;
        if (body != null) {
            bodyStates = new List<BodyState>();
            for (var ancestor = body; ancestor != null; ancestor = ancestor.boParent)
                bodyStates.Add(new BodyState(ancestor));
        }
        var end = route.Destination;
        try {
            // Match PlanTrip4's target model: propagate body attachments, but
            // keep free-hull target positions fixed. Introducing a linear free-hull
            // forecast only here would reject native ship-to-ship visit routes.
            target.UpdateTime(end.ArrivalTime, includeDockGroup: false);
            if (!Finite(target)) return false;

            // Native PlanTrip4 intentionally finishes torch legs 300 km short,
            // and RCS legs two collision radii short. Allow 1 km numerical error.
            double clearance = route.IsUsingTorchDrive()
                ? 2.005376018132665e-6
                : 2d * CollisionManager.GetCollisionDistanceAU(destination.ObjSS, route.Origin.ObjSS);
            double error = (end.ObjSS.vPos - target.vPos).magnitude;
            if (!Finite(error) || error > clearance + 1000d / 149597872000d) return false;

            // Intermediate departure waypoints can carry an intentional speed
            // offset. Ordinary rendezvous must reach the native 100 m/s handoff.
            double relativeSpeed = (end.ObjSS.vVel - target.vVel).magnitude;
            return destination.ObjSS.IsRegionBorderPoint ||
                Finite(relativeSpeed) && relativeSpeed <= 100d / 149597872000d;
        } finally {
            // Native sampling also touches parent bodies and a separate velocity
            // clock. Restore the exact caches, including historical SFF/PASS scopes.
            if (bodyStates != null) foreach (var state in bodyStates) state.Restore();
        }
    }

    internal static void Validate(FlyToPath planner, NavDataPoint destination, int recursionBefore, ref NavData result) {
        if (result == null || !Applies(planner) || planner.nRecursion != recursionBefore + 1) return;
        // A recursive no-wake/obstruction call owns its actual intermediate
        // destination. The outer call must not compare that leg with the final port.
        if (Arrives(result, destination)) return;
        result = null;
        if (ReferenceEquals(current.Planner, planner)) current.Rejected = true;
    }

    internal static void ClearRejectedTarget(Ship ship, FlyToPath planner) {
        var target = ship.shipScanTarget;
        if (ReferenceEquals(current.Planner, planner) && current.Rejected &&
            Pilot(planner)?.AIType == AIType.HaulerCargo && target != null && !target.bDestroyed &&
            CrewSim.system?.dictShips != null &&
            CrewSim.system.dictShips.TryGetValue(target.strRegID, out var live) && ReferenceEquals(target, live)) {
            // A numerical failure does not invalidate the freight destination.
            // Keep native retry counters, stop commands and tree transitions;
            // do not let GetTarget's home fallback silently redirect the load.
            Pilot(planner).Blackboard?.Remove("TripFuelPrediction");
            return;
        }
        ship.ClearShipTarget();
    }
}

[HarmonyPatch(typeof(FlyToPath), "GetAcceleration")]
internal static class AiRcsAccelerationPatch {
    static void Prefix(FlyToPath __instance, bool inAtmo, ref float fLimiter) {
        // The native caller still applies LimitAccelSafe. Preserve the separate
        // atmospheric lift path, as well as torch and player limits.
        if (AiRendezvous.Applies(__instance) && (!inAtmo || __instance.ShipUs.LiftRotorsThrustStrength <= 0)) fLimiter = 1;
    }
}

[HarmonyPatch(typeof(FlyToPath), nameof(FlyToPath.PlanTrip4))]
internal static class AiRendezvousValidationPatch {
    static void Prefix(FlyToPath __instance, out int __state) => __state = __instance.nRecursion;

    static void Postfix(FlyToPath __instance, NavDataPoint navDestination, int __state, ref NavData __result) =>
        AiRendezvous.Validate(__instance, navDestination, __state, ref __result);
}

[HarmonyPatch(typeof(FlyToPath), nameof(FlyToPath.RunCommand))]
internal static class FreightRendezvousFailurePatch {
    static void Prefix(FlyToPath __instance, out AiRendezvous.Attempt __state) => __state = AiRendezvous.Enter(__instance);

    static void Finalizer(AiRendezvous.Attempt __state) => AiRendezvous.Leave(__state);

    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var clear = AccessTools.Method(typeof(Ship), nameof(Ship.ClearShipTarget));
        var replacement = AccessTools.Method(typeof(AiRendezvous), nameof(AiRendezvous.ClearRejectedTarget));
        int count = 0;
        foreach (var instruction in instructions) {
            if (instruction.Calls(clear)) {
                count++;
                var owner = new CodeInstruction(OpCodes.Ldarg_0);
                instruction.MoveLabelsTo(owner); instruction.MoveBlocksTo(owner);
                yield return owner;
                yield return new CodeInstruction(OpCodes.Call, replacement);
            } else yield return instruction;
        }
        if (count != 1) throw new InvalidOperationException("AI rejected-path target cleanup changed.");
    }
}
