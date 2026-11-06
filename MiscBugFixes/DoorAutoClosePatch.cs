using HarmonyLib;

namespace Ostrabus.MiscBugFixes;

// Correct the four native door power-off effects when an interaction is loaded
// or taken from the pool. Runtime power, mode switching and auto-close stay native.
[HarmonyPatch(typeof(Interaction), "SetData")]
internal static class DoorAutoClosePatch {
    [HarmonyPostfix]
    internal static void Postfix(Interaction __instance) {
        if (__instance.LootCTsUs?.strName != "CTRemoveAutoClose") return;
        switch (__instance.strName) {
            case "MSDoor01ClosedShutdownAllow":
            case "MSDoor01ClosedLockedShutdownAllow":
            case "MSDoor01OpenShutdownAllow":
            case "MSDoor01OpenLockedShutdownAllow":
                // IsAutoClose is a persistent preference, not a power-state flag.
                // Removing it also records a persistent zero that defeats the
                // powered-open definition's default after power is restored.
                __instance.LootCTsUs = null;
                break;
        }
    }
}
