using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Ostrabus.NavConsole;

internal static class TutorialNote {
    internal static ConfigEntry<bool> HideFlag;
    static readonly AccessTools.FieldRef<GUIOrbitDraw, Button> Note = AccessTools.FieldRefAccess<GUIOrbitDraw, Button>("btnNote");
    static readonly Action<GUIOrbitDraw, bool> SetInitial = AccessTools.MethodDelegate<Action<GUIOrbitDraw, bool>>(
        AccessTools.Method(typeof(GUIOrbitDraw), "SetNoteInitial"));
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
        // Native MouseHandler rejects map clicks while bNoteOpen is true, even
        // when the note's GameObject is hidden. Close it without animation,
        // tutorial completion, or sound before hiding its presentation.
        if (Hidden) SetInitial(nav, false);
        var note = Note(nav);
        if (note != null && note.gameObject.activeSelf == Hidden) note.gameObject.SetActive(!Hidden);
    }
}

[HarmonyPatch(typeof(GUIOrbitDraw), nameof(GUIOrbitDraw.Init), new[] {
    typeof(CondOwner), typeof(Dictionary<string, string>), typeof(string)
})]
internal static class TutorialNoteRestorePatch {
    // Init opens the note again after Awake, including for a newly installed
    // console with no saved bNote property. Reapply after native restoration.
    static void Postfix(GUIOrbitDraw __instance) { TutorialNote.Apply(__instance); }
}

[HarmonyPatch(typeof(GUIOrbitDraw), "Awake")]
internal static class TutorialNoteInitPatch {
    static void Postfix(GUIOrbitDraw __instance) { TutorialNote.Apply(__instance); }
}
