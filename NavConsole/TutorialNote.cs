using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Ostrabus.NavConsole;

internal static class TutorialNote {
    internal static ConfigEntry<bool> HideFlag;
    static readonly AccessTools.FieldRef<GUIOrbitDraw, Button> Note = AccessTools.FieldRefAccess<GUIOrbitDraw, Button>("btnNote");
    internal static bool Hidden => HideFlag?.Value == true;
    internal static void SetHidden(bool hidden) {
        if (HideFlag == null) throw new System.InvalidOperationException("Nav tutorial setting is not initialized.");
        HideFlag.Value = hidden;
        // Only runs when the setting changes; include inactive consoles, not prefab assets.
        foreach (var nav in Resources.FindObjectsOfTypeAll<GUIOrbitDraw>()) {
            if (nav.gameObject.scene.IsValid()) Apply(nav);
        }
    }
    internal static void Apply(GUIOrbitDraw nav) {
        var note = Note(nav);
        if (note != null && note.gameObject.activeSelf == Hidden) note.gameObject.SetActive(!Hidden);
    }
}

[HarmonyPatch(typeof(GUIOrbitDraw), "Awake")]
internal static class TutorialNoteInitPatch {
    static void Postfix(GUIOrbitDraw __instance) { TutorialNote.Apply(__instance); }
}
