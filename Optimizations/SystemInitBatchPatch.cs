using System.Collections;
using HarmonyLib;
using Ostranauts;

namespace Ostrabus.Optimizations;

[HarmonyPatch(typeof(StarSystem), nameof(StarSystem.Init), new[] { typeof(JsonStarSystemSave), typeof(JsonShip[]) })]
internal static class SystemInitBatchPatch {
    internal static bool Enabled = true;
    internal static double BudgetMs = 4;
    static bool warnedMissingObserver;

    [HarmonyPostfix]
    static void Postfix(JsonStarSystemSave objSystem, ref IEnumerator __result) {
        // Leave the new-game fallback and other Init overloads with their original scheduling.
        if (!Enabled || objSystem == null || __result == null) return;
        // Progress callbacks delimit the eligible phases. Never batch if that observer failed to install.
        var patches = Harmony.GetPatchInfo(AccessTools.Method(typeof(LoadingScreen), nameof(LoadingScreen.SetProgressBar)));
        if (patches != null) foreach (var patch in patches.Prefixes) {
            if (patch.PatchMethod.DeclaringType != typeof(SystemInitBatchProgressPatch)) continue;
            __result = new SystemInitBatching(__result, BudgetMs);
            return;
        }
        if (!warnedMissingObserver) {
            warnedMissingObserver = true;
            Ostrabus.OstrabusPlugin.Logger?.LogWarning("[Optimizations] System-init phase observer is missing; original coroutine scheduling retained.");
        }
    }
}

[HarmonyPatch(typeof(LoadingScreen), nameof(LoadingScreen.SetProgressBar))]
internal static class SystemInitBatchProgressPatch {
    [HarmonyPrefix]
    static void Prefix(string textToDisplay) { SystemInitBatching.Progress(textToDisplay); }
}
