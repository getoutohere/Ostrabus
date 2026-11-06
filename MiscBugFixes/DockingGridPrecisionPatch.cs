using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Ostrabus.MiscBugFixes;

internal static class DockingGridPrecision {
    [ThreadStatic] static int depth;
    internal static bool Active => depth > 0;
    internal static int Enter() { int previous = depth; depth++; return previous; }
    internal static void Exit(int previous) { depth = previous; }
    internal static float Snap(float value) {
        double nearest = Math.Round((double)value * 2) * 0.5;
        return Math.Abs(value - nearest) < 0.0001 ? (float)nearest : value;
    }
}

// Scope the native fit operation instead of rewriting its large grid-builder body.
[HarmonyPatch(typeof(Ship), nameof(Ship.GetAvailableDockingPorts))]
internal static class DockingFitScopePatch {
    static void Prefix(out int __state) { __state = DockingGridPrecision.Enter(); }
    static Exception Finalizer(Exception __exception, int __state) {
        DockingGridPrecision.Exit(__state);
        return __exception;
    }
}

[HarmonyPatch]
internal static class DockingSavedCoordinatePatch {
    static System.Collections.Generic.IEnumerable<MethodBase> TargetMethods() {
        yield return AccessTools.PropertyGetter(typeof(JsonItem), nameof(JsonItem.fX));
        yield return AccessTools.PropertyGetter(typeof(JsonItem), nameof(JsonItem.fY));
    }
    static void Postfix(ref float __result) {
        if (DockingGridPrecision.Active) __result = DockingGridPrecision.Snap(__result);
    }
}

[HarmonyPatch(typeof(Transform), nameof(Transform.position), MethodType.Getter)]
internal static class DockingLiveCoordinatePatch {
    static void Postfix(ref Vector3 __result) {
        if (!DockingGridPrecision.Active) return;
        __result.x = DockingGridPrecision.Snap(__result.x);
        __result.y = DockingGridPrecision.Snap(__result.y);
    }
}
