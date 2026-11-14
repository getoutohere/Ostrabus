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

// Invert native fuel consumption, including the pilot efficiency multiplier.
// Zero/invalid capability must not bypass native low-fuel comparisons through NaN.
[HarmonyPatch(typeof(Ship), nameof(Ship.DeltaVRemainingRCS), MethodType.Getter)]
internal static class InvalidRcsBudgetPatch {
    static readonly AccessTools.FieldRef<Ship, double> Efficiency = AccessTools.FieldRefAccess<Ship, double>("fFuelEfficiencyMod");
    [HarmonyPostfix]
    internal static void Postfix(Ship __instance, ref double __result) {
        double efficiency = Efficiency(__instance);
        __result = NavigationFuel.Finite(efficiency) && efficiency > 0 ? __result / efficiency : 0;
        if (!NavigationFuel.Finite(__result) || __result < 0) __result = 0;
    }
}

[HarmonyPatch(typeof(Ship), nameof(Ship.ClearShipTarget))]
internal static class ClearTargetFuelPatch {
    [HarmonyPrefix]
    internal static void Prefix(Ship __instance) => NavigationFuel.ClearPrediction(__instance);
    [HarmonyPostfix]
    internal static void Postfix(Ship __instance) {
        // Shallow GetJSON clones json and only overwrites optional references
        // when non-null. Native ClearShipTarget must clear its saved copy too.
        JsonShip saved = __instance.json;
        if (saved == null) return;
        saved.strScanTargetID = null;
        saved.strTargetRegID = null;
        saved.objSituScanTarget = null;
    }
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

