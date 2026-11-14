using HarmonyLib;

namespace Ostrabus.MiscBugFixes;

// Arrest assets grant IsTempDraftedLong and native HasShoreLeave honors it.
// Honor the same temporary control for explicit airlock movement orders; do
// not mutate the prisoner's company roster or relax autonomous suit checks.
[HarmonyPatch(typeof(CondOwner), nameof(CondOwner.HasAirlockPermission))]
internal static class BountyEscortAirlockPatch {
    static bool Prefix(CondOwner __instance, bool bManual, ref bool __result) {
        if (!bManual || !__instance.HasCond("IsTempDraftedLong")) return true;
        __result = true;
        return false;
    }
}
