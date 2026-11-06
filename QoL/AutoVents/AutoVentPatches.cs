using HarmonyLib;

namespace Ostrabus.QoL.AutoVents;

[HarmonyPatch(typeof(GasPressureSense), nameof(GasPressureSense.Run))]
internal static class AutoVentSensePatch {
    [HarmonyPrefix]
    static bool Prefix(GasPressureSense __instance) => AutoVentControl.Process(__instance, false);
}

// GasExchange caches this result at initialization. Use the same policy here
// so a marginal transient cannot silently disable flow before the timer expires.
[HarmonyPatch(typeof(GasPressureSense), nameof(GasPressureSense.FixForVentSensors))]
internal static class AutoVentInitialPatch {
    [HarmonyPrefix]
    static bool Prefix(GasPressureSense __instance) => AutoVentControl.Process(__instance, true);
}
