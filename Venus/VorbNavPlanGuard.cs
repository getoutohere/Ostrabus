using System;
using Ostrabus.ShipNavigation;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Ostrabus.Haulers;
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
            AIShip aiShip = HaulerRecovery.GetVorbHaulerAI(ship);
            if (aiShip != null && IsTargetlessAndEmpty(ship)) HaulerRecovery.RestoreHomeTarget(ship, aiShip, "targetless return recovery");
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

    internal static void FuelAdded(Ship ship, double amount) {
        if (amount <= 0 || !IsVorbHauler(ship)) return;
        // A route stores absolute remaining fuel. Rebase it after a refill, or TimeAdvance removes that refill as catch-up.
        if (!ValidateCurrentPlan(ship)) return;
        RouteState state = GetState(ship);
        state.FuelAddedKg += amount;
        NavData plan = ship.objSS.NavData;
        if (plan == null) return;
        List<NavDataPoint> points = NavPoints(plan);
        for (int i = 0; i < points.Count; i++) points[i].FuelLevel += amount;
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
    internal static bool CanRebase(Ship ship) { return ship?.objSS?.NavData != null && IsVorbHauler(ship); }


    static bool IsTargetlessAndEmpty(Ship ship)
    {
        if (ship == null || ship.IsDocked() || ship.shipScanTarget != null || ship.targetRegID != null || ship.TargetData != null) return false;
        IReadOnlyList<Ship> dockedShips = ship.GetAllDockedShips();
        return dockedShips == null || dockedShips.Count == 0;
    }

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

[HarmonyPatch(typeof(Ship), nameof(Ship.RefuelRCS), new Type[] { typeof(float) })]
internal static class VorbMaterializedRefuelPatch {
    [HarmonyPrefix]
    private static void Prefix(Ship __instance, out double __state) {
        __state = -1;
        if (__instance.LoadState > Ship.Loaded.Shallow && VorbNavPlanGuard.CanRebase(__instance)) __state = __instance.GetRCSRemain();
    }
    [HarmonyPostfix]
    private static void Postfix(Ship __instance, double __state) {
        if (__state >= 0) VorbNavPlanGuard.FuelAdded(__instance, __instance.GetRCSRemain() - __state);
    }
}

[HarmonyPatch(typeof(Ship), nameof(Ship.AIRefuel))]
internal static class VorbAutomaticRefuelPatch {
    [HarmonyPrefix]
    private static void Prefix(Ship __instance, out double __state) {
        __state = __instance.LoadState <= Ship.Loaded.Shallow && VorbNavPlanGuard.CanRebase(__instance) ? __instance.fShallowRCSRemass : -1;
    }
    [HarmonyPostfix]
    private static void Postfix(Ship __instance, double __state) {
        if (__state >= 0) VorbNavPlanGuard.FuelAdded(__instance, __instance.fShallowRCSRemass - __state);
    }
}

[HarmonyPatch(typeof(FlyTo), "HasValidPathTarget")]
internal static class VorbManualBranchPatch {
    [HarmonyPostfix]
    private static void Postfix(FlyTo __instance, ref bool __result) {
        if (VorbNavPlanGuard.ShouldUseManual(__instance.ShipUs)) __result = false;
    }
}

[HarmonyPatch(typeof(TargetData), nameof(TargetData.ApplyToShip))]
internal static class VorbTargetChangePatch {
    [HarmonyPrefix]
    private static void Prefix(Ship ship, out NavigationTargetKey __state) {
        __state = new NavigationTargetKey(ship);
    }
    [HarmonyPostfix]
    private static void Postfix(Ship ship, NavigationTargetKey __state) {
        if (!__state.Same(new NavigationTargetKey(ship))) VorbNavPlanGuard.TargetChanged(ship);
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
            if (!VorbNavPlanGuard.CanRebase(ship) || !ReferenceEquals(ship.objSS.NavData, __instance)) return true;
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


