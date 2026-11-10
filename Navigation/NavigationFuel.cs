using System;
using HarmonyLib;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;

namespace Ostrabus.ShipNavigation;

internal static class NavigationFuel {
    internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    internal static AIShip Controller(Ship ship) {
        if (ship == null || AIShipManager.dictAIs == null) return null;
        foreach (var region in AIShipManager.dictAIs.Values) {
            if (region == null) continue;
            foreach (var ai in region) if (ai?.Ship?.strRegID == ship.strRegID) return ai;
        }
        return null;
    }
    internal static void ClearPrediction(Ship ship) {
        Controller(ship)?.Blackboard?.Remove("TripFuelPrediction");
    }
}

// Zero propulsion produces 0/0 in native arithmetic. It is no usable delta-v,
// rather than an unordered value that defeats every native low-fuel comparison.
[HarmonyPatch(typeof(Ship), nameof(Ship.DeltaVRemainingRCS), MethodType.Getter)]
internal static class InvalidRcsBudgetPatch {
    [HarmonyPostfix]
    internal static void Postfix(ref double __result) {
        if (!NavigationFuel.Finite(__result) || __result < 0) __result = 0;
    }
}

[HarmonyPatch(typeof(Ship), nameof(Ship.ClearShipTarget))]
internal static class ClearTargetFuelPatch {
    [HarmonyPrefix]
    internal static void Prefix(Ship __instance) => NavigationFuel.ClearPrediction(__instance);
}

[HarmonyPatch(typeof(TargetData), nameof(TargetData.ApplyToShip))]
internal static class RetargetFuelPatch {
    [HarmonyPrefix]
    internal static void Prefix(Ship ship, out NavigationTargetKey __state) => __state = new NavigationTargetKey(ship);
    [HarmonyPostfix]
    internal static void Postfix(Ship ship, NavigationTargetKey __state) {
        if (__state.Same(new NavigationTargetKey(ship))) return;
        NavigationFuel.ClearPrediction(ship);
        // One semantic change observation, two distinct cleanup responsibilities.
        Ostrabus.Venus.VorbNavPlanGuard.TargetChanged(ship);
    }
}

