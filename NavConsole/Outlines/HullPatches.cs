using HarmonyLib;
using Ostranauts.ShipGUIs.NavStation;
using Ostranauts.ShipGUIs.Utilities;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Ostrabus.NavConsole.Outlines;

[HarmonyPatch(typeof(NavIcon), nameof(NavIcon.GetSilhouette))]
internal static class HullIconPatch {
    static bool Prefix(Ship ship, float fRadiusMeters, ref List<Vector2> __result) {
        __result = HullDisplay.Initial(ship, fRadiusMeters); return false;
    }
}

[HarmonyPatch(typeof(ShipDraw), nameof(ShipDraw.ToggleSilhouetteDrawMode))]
internal static class HullDrawPatch {
    static void Prefix(ShipDraw __instance, ref bool showSilhouette) {
        showSilhouette = HullPresentation.Choose(GUIOrbitDraw.Instance, __instance, showSilhouette);
        HullDisplay.Display(__instance, showSilhouette);
    }
}

[HarmonyPatch(typeof(Ship), "UpdateTiles")]
internal static class HullMutationPatch {
    static void Prefix(Ship __instance, CondOwner objICO, bool bRemove) => HullDisplay.Changed(__instance, objICO, bRemove);
}

[HarmonyPatch(typeof(Ship), nameof(Ship.Destroy))]
internal static class HullRetirePatch {
    static void Prefix(Ship __instance) => HullDisplay.Retire(__instance);
}

[HarmonyPatch]
internal static class HullNavClosePatch {
    static IEnumerable<MethodBase> TargetMethods() {
        yield return AccessTools.Method(typeof(GUIOrbitDraw), "OnDestroy");
        yield return AccessTools.Method(typeof(GUIOrbitDraw), nameof(GUIOrbitDraw.SaveAndClose));
    }
    static void Prefix() => HullDisplay.NavClosed();
}
