using System;
using HarmonyLib;
using Ostranauts.ShipGUIs.NavStation;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Ostrabus.NavConsole;

internal sealed class ProximityDial : MonoBehaviour {
    static readonly AccessTools.FieldRef<NavModBase, CondOwner> Console = AccessTools.FieldRefAccess<NavModBase, CondOwner>("COSelf");
    static readonly AccessTools.FieldRef<NavModWarnings, Toggle> Switch = AccessTools.FieldRefAccess<NavModWarnings, Toggle>("chkProxMute");
    static readonly AccessTools.FieldRef<NavModDisplayControls, GUIKnob> Labels = AccessTools.FieldRefAccess<NavModDisplayControls, GUIKnob>("knobLabels");
    NavModWarnings owner;
    RotaryKnob knob;
    RectTransform dialRect, switchRect, lampRect;
    GameObject root;
    bool syncing;
    int shown;
    internal void Init(NavModWarnings module) {
        owner = module;
        if (knob != null) { Sync(); return; }
        var orbit = module.GetComponentInParent<GUIOrbitDraw>();
        var controls = orbit != null ? orbit.GetComponentInChildren<NavModDisplayControls>(true) : null;
        var source = controls != null ? Labels(controls) : null;
        if (source == null || source.aStates == null || source.aStates.Length < 4) throw new InvalidOperationException("Native four-position knob artwork is unavailable.");
        var toggle = Switch(module);
        var parent = (RectTransform)toggle.transform.parent;
        var go = new GameObject("ProximityRange", typeof(RectTransform), typeof(Image));
        root = go;
        go.SetActive(false); go.layer = parent.gameObject.layer;
        var rect = (RectTransform)go.transform; rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = parent.pivot; rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(44, 44);
        dialRect = rect; switchRect = (RectTransform)toggle.transform;
        lampRect = (RectTransform)module.goEngageDock.transform;
        Align();
        go.GetComponent<Image>().color = Color.clear; // Larger input area than the dial artwork.
        var visual = new GameObject("Dial", typeof(RectTransform), typeof(Image));
        visual.layer = go.layer; var vr = (RectTransform)visual.transform; vr.SetParent(rect, false);
        // V3 has more transparent padding; preserve the previous visible body size.
        vr.anchorMin = vr.anchorMax = new Vector2(0.5f, 0.5f); vr.sizeDelta = new Vector2(33.5f, 33.5f);
        var image = visual.GetComponent<Image>();
        image.sprite = RoundKnobArt.Get(); image.color = new Color(0.65f, 0.65f, 0.65f, 1);
        image.preserveAspect = true; image.raycastTarget = false;
        var shadow = visual.AddComponent<Shadow>();
        shadow.effectColor = new Color(0, 0, 0, 0.85f);
        shadow.effectDistance = new Vector2(2, -2); shadow.useGraphicAlpha = true;
        // Keep baked body lighting/shadow stationary; rotate only the index.
        var pointer = new GameObject("Pointer", typeof(RectTransform)); pointer.layer = go.layer;
        var pr = (RectTransform)pointer.transform; pr.SetParent(rect, false);
        pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0.5f); pr.sizeDelta = new Vector2(24, 24);
        var mark = new GameObject("Index", typeof(RectTransform), typeof(Image)); mark.layer = go.layer;
        var mr = (RectTransform)mark.transform; mr.SetParent(pr, false);
        mr.anchorMin = mr.anchorMax = new Vector2(0.5f, 0.5f);
        mr.sizeDelta = new Vector2(2, 8); mr.anchoredPosition = new Vector2(0, 4);
        var ink = mark.GetComponent<Image>(); ink.color = new Color32(160, 161, 167, 255); ink.raycastTarget = false;
        float[] angles = AddDetentLabels(controls, source, rect);
        knob = go.AddComponent<RotaryKnob>();
        knob.Init(pr, angles, source.strAudioEmitterChange);
        knob.Callback = Changed;
        go.SetActive(true);
        ProximityRange.Remember(Console(owner)?.ship);
        Sync();
    }
    void Changed(int position) {
        if (syncing || owner == null) return;
        ProximityRange.Set(Console(owner)?.ship, (position + 1) * 5);
        shown = 0; Sync();
    }
    internal void Sync() {
        if (owner == null || knob == null) return;
        Align();
        int km = ProximityRange.Get(Console(owner)?.ship);
        if (km == shown) return;
        syncing = true;
        try { knob.SetStateSilent(km / 5 - 1); shown = km; }
        finally { syncing = false; }
    }
    void Align() {
        if (dialRect == null || switchRect == null || lampRect == null) return;
        var parent = dialRect.parent;
        Vector3 lamp = parent.InverseTransformPoint(lampRect.TransformPoint(lampRect.rect.center));
        Vector3 toggle = parent.InverseTransformPoint(switchRect.TransformPoint(switchRect.rect.center));
        dialRect.anchoredPosition = new Vector2(lamp.x, toggle.y);
    }
    static float[] AddDetentLabels(NavModDisplayControls controls, GUIKnob source, RectTransform parent) {
        var texts = controls.GetComponentsInChildren<TMP_Text>(true);
        var nativeLabels = new TMP_Text[4];
        var scores = new[] { float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity };
        TMP_Text style = null;
        float nearest = float.PositiveInfinity;
        Vector3 origin = source.transform.TransformPoint(((RectTransform)source.transform).rect.center);
        foreach (var text in texts) {
            if (string.IsNullOrWhiteSpace(text.text)) continue;
            float distance = (text.transform.position - origin).sqrMagnitude;
            if (distance < nearest) { nearest = distance; style = text; }
            string name = text.text.Trim().Replace(" ", "").ToUpperInvariant();
            int index = name == "OFF" || name == "NONE" ? 0 : name == "CALLSIGN" || name == "CALL" || name == "ID" || name == "C/S" ? 1 : name == "NAME" ? 2 : name == "BOTH" || name == "ALL" ? 3 : -1;
            if (index >= 0 && distance < scores[index]) { scores[index] = distance; nativeLabels[index] = text; }
        }
        if (nativeLabels[0] != null) style = nativeLabels[0];
        if (style == null) throw new InvalidOperationException("DISPLAY CONTROLS label style is unavailable.");
        float sourceSize = Mathf.Max(1, ((RectTransform)source.transform).rect.width);
        var angles = new float[4];
        for (int i = 0; i < 4; i++) {
            var native = nativeLabels[i] != null ? nativeLabels[i] : style;
            Vector2 position = RotaryKnob.DetentPosition(i);
            angles[i] = RotaryKnob.Angle(position);
            var go = new GameObject("Detent" + i, typeof(RectTransform)); go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform; rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(22, 14); rect.anchoredPosition = position;
            var label = go.AddComponent<TextMeshProUGUI>();
            label.font = native.font; label.fontSharedMaterial = native.fontSharedMaterial;
            label.color = native.color; label.fontStyle = native.fontStyle;
            label.fontSize = Mathf.Clamp(native.fontSize * (24f / sourceSize), 8, 10);
            label.enableVertexGradient = native.enableVertexGradient; label.colorGradient = native.colorGradient;
            label.textWrappingMode = TextWrappingModes.NoWrap; label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false; label.text = ((i + 1) * 5).ToString();
        }
        var caption = new GameObject("RangeCaption", typeof(RectTransform)); caption.layer = parent.gameObject.layer;
        var cr = (RectTransform)caption.transform; cr.SetParent(parent, false);
        cr.anchorMin = cr.anchorMax = cr.pivot = new Vector2(0.5f, 0.5f);
        cr.sizeDelta = new Vector2(48, 14); cr.anchoredPosition = new Vector2(0, -21);
        var title = caption.AddComponent<TextMeshProUGUI>();
        title.font = style.font; title.fontSharedMaterial = style.fontSharedMaterial;
        title.color = style.color; title.fontStyle = style.fontStyle;
        title.fontSize = Mathf.Clamp(style.fontSize * (24f / sourceSize), 8, 10);
        title.enableVertexGradient = style.enableVertexGradient; title.colorGradient = style.colorGradient;
        title.textWrappingMode = TextWrappingModes.NoWrap; title.alignment = TextAlignmentOptions.Center;
        title.raycastTarget = false; title.text = "RANGE";
        return angles;
    }
    internal void Cleanup() {
        if (knob != null) knob.Callback = null;
        if (root != null) Destroy(root);
        root = null; owner = null; knob = null; dialRect = switchRect = lampRect = null; shown = 0;
    }
    void OnDestroy() { Cleanup(); }
}

[HarmonyPatch(typeof(NavModWarnings), "Init")]
internal static class ProximityDialInitPatch {
    static void Postfix(NavModWarnings __instance) {
        try { (__instance.GetComponent<ProximityDial>() ?? __instance.gameObject.AddComponent<ProximityDial>()).Init(__instance); }
        catch (Exception error) {
            __instance.GetComponent<ProximityDial>()?.Cleanup();
            Ostrabus.OstrabusPlugin.Logger?.LogWarning("[NavConsole] Could not create proximity dial: " + error);
        }
    }
}

[HarmonyPatch(typeof(NavModWarnings), "UpdateUI")]
internal static class ProximityDialRefreshPatch {
    static void Postfix(NavModWarnings __instance) { __instance.GetComponent<ProximityDial>()?.Sync(); }
}
