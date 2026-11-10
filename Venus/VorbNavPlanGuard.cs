using System;
using Ostrabus.ShipNavigation;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.ShipGUIs.Utilities;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;

namespace Ostrabus.Venus;

[HarmonyPatch(typeof(FlyTo), nameof(FlyTo.RunCommand))]
internal static class VorbNavPlanGuard
{
    const double MaxReturnHorizonSeconds = 1_000_000_000;
    const double MaxReturnCoordinateAU = 100;
    const double MaxReturnVelocityAUPerSecond = 0.01;
    static readonly AccessTools.FieldRef<NavData, List<NavDataPoint>> NavPoints = AccessTools.FieldRefAccess<NavData, List<NavDataPoint>>("_navPoints");
    static readonly AccessTools.FieldRef<NavData, Ship> PlanShip = AccessTools.FieldRefAccess<NavData, Ship>("_shipUs");
    // Persist value identity, not a reference to an object destroyed by shallow
    // replacement. Orbital offsets distinguish a waypoint from the station hull.
    internal sealed class RouteState {
        public RouteState() {}
        internal bool ManualFallback;
        internal string Reason;
        internal WeakReference<ShipSitu> Target;
        internal string TargetId;
        internal WeakReference<NavData> ValidatedPlan;
        internal int Rejections;
        internal double FuelAddedKg;
        internal bool Initialized;
        internal NavigationTargetKey Key;
    }
    static ConditionalWeakTable<Ship, RouteState> States = new ConditionalWeakTable<Ship, RouteState>();

    [HarmonyPrefix]
    private static void Prefix(FlyTo __instance)
    {
        try
        {
            Ship ship = __instance?.ShipUs;
            if (!IsVorbHauler(ship)) return;
            ValidateCurrentPlan(ship);
        }
        catch (Exception ex)
        {
            Ostrabus.OstrabusPlugin.Logger?.LogWarning($"[VORB Nav Guard] Could not inspect a return route: {ex.Message}");
        }
    }

    [HarmonyPostfix]
    private static void Postfix(FlyTo __instance, CommandStatus __result)
    {
        try
        {
            Ship ship = __instance?.ShipUs;
            if (!IsVorbHauler(ship)) return;
            ValidateCurrentPlan(ship);
            // Keep a failed trip in fallback until its target changes; a retry must not regenerate the same bad path.
            if (__result != null && __result.Status == CommandCode.Finished && States.TryGetValue(ship, out RouteState state)) state.ManualFallback = false;
        }
        catch (Exception ex)
        {
            Ostrabus.OstrabusPlugin.Logger?.LogWarning($"[VORB Nav Guard] Could not validate a generated return route: {ex.Message}");
        }
    }

    static RouteState GetState(Ship ship) {
        RouteState state = States.GetOrCreateValue(ship);
        ShipSitu target = ship.shipSituTarget;
        var key = new NavigationTargetKey(ship);
        ShipSitu previous = null;
        state.Target?.TryGetTarget(out previous);
        if (!state.Initialized || !state.Key.Same(key)) {
            if (state.Initialized) ship.objSS?.ResetNavData();
            state.ManualFallback = false;
            state.ValidatedPlan = null;
            state.Reason = null;
        }
        if (!ReferenceEquals(previous, target)) state.Target = target == null ? null : new WeakReference<ShipSitu>(target);
        state.TargetId = key.Id; state.Key = key; state.Initialized = true;
        return state;
    }

    internal static bool ValidateCurrentPlan(Ship ship) {
        RouteState state = GetState(ship);
        NavData plan = ship.objSS?.NavData;
        if (plan == null) { state.ValidatedPlan = null; return true; }
        if (state.ManualFallback) { ship.objSS.ResetNavData(); return false; }
        bool cached = state.ValidatedPlan != null && state.ValidatedPlan.TryGetTarget(out NavData validated) && ReferenceEquals(validated, plan);
        string reason;
        if ((!cached && IsInvalidPlan(plan, StarSystem.fEpoch, out reason)) || IsInvalidActiveSegment(plan, StarSystem.fEpoch, out reason)) {
            RejectPlan(ship, state, reason);
            return false;
        }
        if (!cached) state.ValidatedPlan = new WeakReference<NavData>(plan);
        return true;
    }

    static void RejectPlan(Ship ship, RouteState state, string reason) {
        ship.objSS.ResetNavData();
        state.ValidatedPlan = null;
        state.ManualFallback = true;
        state.Reason = reason;
        state.Rejections++;
        Ostrabus.OstrabusPlugin.Logger?.LogWarning($"[VORB Nav Guard] Rejected route for {ship.strRegID}: {reason}. FlyTo will use its vanilla manual branch.");
    }

    internal static bool ShouldUseManual(Ship ship) {
        return ship != null && States.TryGetValue(ship, out RouteState state) && state.ManualFallback &&
            (ship.LoadState <= Ship.Loaded.Shallow || (ship.NavAIManned && !ship.NavPlayerManned));
    }

    internal static void TargetChanged(Ship ship) {
        if (!IsVorbHauler(ship)) return;
        ship.objSS?.ResetNavData();
        States.Remove(ship);
    }

    internal static void RecordFuelAdded(Ship ship, double amount) {
        // Diagnostics must not validate/replace a route or create regional state
        // while shared fuel accounting is committing the refill.
        if (amount > 0 && States.TryGetValue(ship, out RouteState state)) state.FuelAddedKg += amount;
    }

    static bool IsVorbHauler(Ship ship)
    {
        if (ship == null || ship.bDestroyed || ship.objSS == null) return false;
        if (ship.LoadState > Ship.Loaded.Shallow && ship.NavPlayerManned) return false;
        AIShip aiShip = AIShipManager.GetAIShipByRegID(ship.strRegID);
        return aiShip != null && aiShip.AIType == AIType.HaulerDeployer && string.Equals(aiShip.HomeStation, "VORB", StringComparison.Ordinal);
    }

    internal static Ship GetPlanShip(NavData navData) { return navData == null ? null : PlanShip(navData); }
    internal static bool TryGetState(Ship ship, out RouteState state) { return States.TryGetValue(ship, out state); }
    internal static bool OwnsRouteGuard(Ship ship) { return ship?.objSS?.NavData != null && IsVorbHauler(ship); }
    static bool IsInvalidPlan(NavData navData, double epoch, out string reason)
    {
        List<NavDataPoint> points = NavPoints(navData);
        if (points == null || points.Count < 2) { reason = "missing or incomplete waypoint list"; return true; }
        double previousArrival = double.NegativeInfinity;
        for (int i = 0; i < points.Count; i++) {
            NavDataPoint point = points[i];
            if (!IsPlausiblePoint(point, epoch, out reason)) { reason = $"waypoint {i}: {reason}"; return true; }
            if (point.ArrivalTime < previousArrival) { reason = $"waypoint {i} arrival precedes the prior point"; return true; }
            previousArrival = point.ArrivalTime;
        }
        reason = null;
        return false;
    }

    static bool IsInvalidActiveSegment(NavData navData, double epoch, out string reason)
    {
        List<NavDataPoint> points = NavPoints(navData);
        if (points == null || points.Count < 2) { reason = "missing active waypoint pair"; return true; }
        int previous = 0;
        int next = points.Count - 1;
        bool found = false;
        for (int i = 1; i < points.Count; i++) {
            if (epoch <= points[i].ArrivalTime) { previous = i - 1; next = i; found = true; break; }
        }
        if (!found) { reason = "waypoint plan expired without being cleared"; return true; }
        if (!IsPlausiblePoint(points[previous], epoch, out reason)) { reason = $"active waypoint {previous}: {reason}"; return true; }
        if (!IsPlausiblePoint(points[next], epoch, out reason)) { reason = $"active waypoint {next}: {reason}"; return true; }
        reason = null;
        return false;
    }

    static bool IsPlausiblePoint(NavDataPoint point, double epoch, out string reason)
    {
        ShipSitu situation = point?.ObjSS;
        if (point == null || situation == null) { reason = "missing waypoint data"; return false; }
        if (!Finite(point.ArrivalTime) || !Finite(situation.vPosx) || !Finite(situation.vPosy) ||
            !Finite(point.VelX) || !Finite(point.VelY) || !Finite(situation.vAccRCS.x) || !Finite(situation.vAccRCS.y) ||
            !Finite(situation.vAccIn.x) || !Finite(situation.vAccIn.y) || !Finite(point.FuelLevel) ||
            !Finite(point.TorchCycle) || !Finite(point.TorchFuelLevel)) { reason = "non-finite waypoint value"; return false; }
        if (Math.Abs(situation.vPosx) > MaxReturnCoordinateAU || Math.Abs(situation.vPosy) > MaxReturnCoordinateAU) { reason = "position exceeds the 100 AU return envelope"; return false; }
        if (Math.Abs(point.VelX) > MaxReturnVelocityAUPerSecond || Math.Abs(point.VelY) > MaxReturnVelocityAUPerSecond) { reason = "velocity exceeds the return envelope"; return false; }
        if (point.ArrivalTime - epoch > MaxReturnHorizonSeconds) { reason = "arrival exceeds the 1e9 second return horizon"; return false; }
        if (point.FuelLevel < -0.001) { reason = "predicted RCS fuel falls below zero"; return false; }
        reason = null;
        return true;
    }

    static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }

    internal static void ResetRuntimeState(UnityEngine.SceneManagement.Scene scene)
    {
        States = new ConditionalWeakTable<Ship, RouteState>();
    }
}

[HarmonyPatch(typeof(FlyTo), "HasValidPathTarget")]
internal static class VorbManualBranchPatch {
    [HarmonyPostfix]
    private static void Postfix(FlyTo __instance, ref bool __result) {
        if (VorbNavPlanGuard.ShouldUseManual(__instance.ShipUs)) __result = false;
    }
}

[HarmonyPatch(typeof(NavData), nameof(NavData.TimeAdvance))]
internal static class VorbNavFuelPlanGuard
{
    [HarmonyPrefix]
    private static bool Prefix(NavData __instance, ShipSitu situUs, ref bool __result)
    {
        try
        {
            Ship ship = VorbNavPlanGuard.GetPlanShip(__instance);
            if (!VorbNavPlanGuard.OwnsRouteGuard(ship) || !ReferenceEquals(ship.objSS.NavData, __instance)) return true;
            if (VorbNavPlanGuard.ValidateCurrentPlan(ship) && ReferenceEquals(ship.objSS.NavData, __instance)) return true;
            __result = false;
            return false;
        }
        catch (Exception ex)
        {
            Ostrabus.OstrabusPlugin.Logger?.LogWarning($"[VORB Nav Guard] Could not protect a hauler from a negative-fuel route: {ex.Message}");
            return true;
        }
    }
}


