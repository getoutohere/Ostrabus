using System;
using HarmonyLib;
using Ostranauts.ShipGUIs.Utilities;
using Ostranauts.Ships.Commands;
using Ostranauts.Utils.Models;

namespace Ostrabus.ShipNavigation.TorchCourses;

internal static class TorchTransferPlanning {
    // Only the player's nav-console plotting call opts in. Thread-local scope
    // prevents a simultaneous native AI planning worker from inheriting it.
    internal struct Scope {
        internal int Depth;
        internal NavDataPoint Target;
        internal Ship Vessel;
        internal bool Changed;
    }
    [ThreadStatic] static Scope current;
    internal static int ScopeDepth => current.Depth;
    internal static Scope Enter() {
        var previous = current;
        current = new Scope { Depth = previous.Depth+1 };
        return previous;
    }
    internal static void Leave(Scope previous) => current = previous;
    internal static void Target(NavDataPoint target) => current.Target = target;
    internal static bool ArrivalValid(NavData nav) {
        if (!current.Changed || nav == null) return true;
        if (current.Target?.ObjSS == null || nav.Destination?.ObjSS == null) return false;
        try {
            current.Target.ObjSS.UpdateTime(nav.Destination.ArrivalTime);
            double error = (nav.Destination.ObjSS.vPos-current.Target.ObjSS.vPos).magnitude;
            // Target already includes the native 300 km approach offset. This
            // tests convergence, not distance to its center or collision rules.
            return TransferSolver.Finite(error) && error <= 1000d/149597872000;
        } finally {
            // BodyOrbit ephemerides are shared mutable state. Match the native
            // wrapper's cleanup rather than leaving a planet at future time.
            current.Target.ObjSS.UpdateTime(StarSystem.fEpoch);
        }
    }
    internal static NavData Complete(NavData nav) {
        if (ArrivalValid(nav)) return nav;
        current.Vessel?.LogAdd("Course did not converge. Try a shorter drift phase.",StarSystem.fEpoch,bShowEpoch:true);
        return null;
    }
    delegate void NativeBurn(FlyToPath path, NavData nav, int count, double step, Point acceleration,
        bool torch, bool atmosphere, float limiter, NavDataPoint target, string label, int easeStart, int easeEnd);
    static readonly NativeBurn Burn = AccessTools.MethodDelegate<NativeBurn>(AccessTools.Method(typeof(FlyToPath),"PlotBurnLoop"));
    internal static bool TryPlan(FlyToPath path, NavDataPoint origin, NavDataPoint target, float limiter,
        double estimate, out NavData result) {
        result = null;
        if (path?.ShipUs == null || origin?.ObjSS == null || target?.ObjSS == null ||
            !TransferSolver.Finite(estimate) || estimate < 0 || !TransferSolver.Finite(origin.ArrivalTime) ||
            !TransferSolver.Finite(limiter) || limiter <= 0 || limiter > 1) return false;
        var ship = path.ShipUs;
        double acceleration = ship.GetMaxTorchThrust(limiter);
        target.ObjSS.UpdateTime(origin.ArrivalTime+estimate);
        Point arrivalVelocity = target.ObjSS.vVel;
        // Same arrival-frame linearization as the native kernel. PlanTrip4's
        // outer iterations continue to update the real target ephemeris.
        Point displacement = target.ObjSS.vPos-origin.ObjSS.vPos-arrivalVelocity*estimate;
        Point incoming = origin.ObjSS.vVel-arrivalVelocity;
        if (!TransferSolver.Solve(displacement,incoming,acceleration,path.fTorchSpeedLimit,out var plan)) return false;
        // Avoid non-advancing timestamps in native easing at huge save epochs.
        if (!Representable(origin.ArrivalTime,plan.FirstSeconds) || !Representable(origin.ArrivalTime,plan.LastSeconds) ||
            !Representable(origin.ArrivalTime,plan.CoastSeconds)) return false;
        var nav = new NavData(ship);
        var start = origin.Clone();
        start.ObjSS.bIsBO = start.ObjSS.bBOLocked = false;
        start.ObjSS.vBOOffsetx = start.ObjSS.vBOOffsety = 0;
        nav.AddNavDataPoint(start);
        double previousPeak = path.LastSpeedMax;
        if (plan.FirstSeconds > 0) Burn(path,nav,10,plan.FirstSeconds/10,plan.FirstAcceleration,true,false,limiter,target,":MOM+",2,0);
        if (plan.CoastSeconds > 0) Burn(path,nav,10,plan.CoastSeconds/10,default,true,false,limiter,target,":MOMC",0,0);
        if (plan.LastSeconds > 0) Burn(path,nav,10,plan.LastSeconds/10,plan.LastAcceleration,true,false,limiter,target,":MOM-",0,2);
        nav.Destination.TorchCycle = 0;
        Point expected = origin.ObjSS.vPos+arrivalVelocity*plan.Duration+displacement;
        double positionTolerance = Math.Max(1e-12,expected.magnitude*1e-14);
        double velocityTolerance = Math.Max(1e-15,arrivalVelocity.magnitude*1e-11);
        if ((nav.Destination.ObjSS.vPos-expected).magnitude > positionTolerance ||
            (nav.Destination.ObjSS.vVel-arrivalVelocity).magnitude > velocityTolerance) {
            path.LastSpeedMax = previousPeak;
            return false;
        }
        result = nav;
        current.Changed = true;
        current.Vessel = ship;
        return true;
    }
    static bool Representable(double epoch, double duration) => duration == 0 ||
        (duration > 0 && TransferSolver.Finite(duration) && epoch+duration/1000 > epoch);
}

[HarmonyPatch(typeof(FlyToPath),"PlanTrip4Sub")]
internal static class TorchTransferKernelPatch {
    static bool Prefix(FlyToPath __instance, NavDataPoint navOrigin, NavDataPoint navDestination,
        float fLimiter, double fDurationEst, bool bUsesTorch, bool bInAtmo, ref NavData __result) {
        if (TorchTransferPlanning.ScopeDepth == 0 || !bUsesTorch || bInAtmo) return true;
        TorchTransferPlanning.Target(navDestination);
        if (!TorchTransferPlanning.TryPlan(__instance,navOrigin,navDestination,fLimiter,fDurationEst,out var nav)) return true;
        __result = nav;
        return false;
    }
}
