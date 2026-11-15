using System;
using HarmonyLib;
using Ostranauts.ShipGUIs.NavStation;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Ostrabus.NavConsole.Rotation;

internal static class RotationPanel {
    internal const float ButtonSize = 0.32f;
    static readonly AccessTools.FieldRef<NavModDuhrt, TMP_Text> MainText = AccessTools.FieldRefAccess<NavModDuhrt, TMP_Text>("txtMainBody");

    internal static void Prepare(GUIOrbitDraw owner, CondOwner console) {
        CondOwner hardware = null;
        if (!CrewSim.bShipEdit && !CrewSim.bDebugFightMode) {
            var items = console.GetCOsSafe(true, DataHandler.GetCondTrigger("TIsFitContainerNavMod"));
            foreach (var item in items) {
                if (item == null || !item.mapGUIPropMaps.TryGetValue("NavMod", out var map) ||
                    !map.TryGetValue("strGUIPrefab", out string prefab) || prefab != RotationModule.PrefabName) continue;
                if (hardware == null || hardware.HasCond("IsDamaged")) hardware = item;
            }
            if (hardware == null) return;
        }
        Make(owner, console, hardware, false);
        Make(owner, console, hardware, true);
    }

    static void Make(GUIOrbitDraw owner, CondOwner console, CondOwner hardware, bool damaged) {
        string name = RotationModule.PrefabName + (damaged ? "Dmg" : "");
        var existing = owner.transform.Find(name);
        if (existing != null) { existing.GetComponent<RotationModule>()?.Bind(owner, console, hardware); return; }
        var prefab = Resources.Load<GameObject>("GUIShip/GUIOrbitDraw/NavModDuhrt");
        if (prefab == null) throw new InvalidOperationException("Native 1x1 DUHRT prefab is unavailable.");
        var staging = new GameObject("Rotation staging");
        staging.SetActive(false);
        staging.transform.SetParent(owner.transform, false);
        GameObject root = null;
        try {
            root = UnityEngine.Object.Instantiate(prefab, staging.transform, false);
            root.name = name;
            root.SetActive(false);
            var original = root.GetComponent<NavModDuhrt>();
            TMP_Text body = original != null ? MainText(original) : null;
            if (body == null) throw new InvalidOperationException("Native DUHRT readout binding changed.");
            foreach (var label in root.GetComponentsInChildren<TMP_Text>(true)) if (label != body && label.text.IndexOf("DUHRT", StringComparison.OrdinalIgnoreCase) >= 0) label.text = "ROTATION";
            foreach (var label in root.GetComponentsInChildren<Text>(true)) if (label.text.IndexOf("DUHRT", StringComparison.OrdinalIgnoreCase) >= 0) label.text = "ROTATION";
            UnityEngine.Object.DestroyImmediate(original);
            var module = root.AddComponent<RotationModule>();
            module.Damaged = damaged;
            module.Bind(owner, console, hardware);
            if (damaged) {
                body.text = "OFFLINE";
                body.alignment = TextAlignmentOptions.Center;
            } else BuildButtons(owner, module, body);
            root.transform.SetParent(owner.transform, false);
            root.transform.SetAsFirstSibling();
        } catch {
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
            throw;
        } finally { UnityEngine.Object.DestroyImmediate(staging); }
    }

    static void BuildButtons(GUIOrbitDraw owner, RotationModule module, TMP_Text body) {
        var surface = body.rectTransform;
        // DUHRT is a readout: its pnlTxt CanvasGroup explicitly blocks both
        // interaction and raycasts. Reusing its child text rect without adapting
        // that parent renders buttons that can never receive pointer events.
        var readout = surface.parent as RectTransform;
        if (readout == null) throw new InvalidOperationException("Native DUHRT readout container is unavailable.");
        ConfigureInputSurface(readout.GetComponent<CanvasGroup>());
        readout.anchorMin = new Vector2(0.1f, 0.08f);
        readout.anchorMax = new Vector2(0.9f, 0.8f);
        readout.offsetMin = readout.offsetMax = Vector2.zero;
        var backing = readout.parent.Find("pnlBg") as RectTransform;
        if (backing == null) throw new InvalidOperationException("Native DUHRT backing panel is unavailable.");
        backing.anchorMin = readout.anchorMin;
        backing.anchorMax = readout.anchorMax;
        backing.offsetMin = backing.offsetMax = Vector2.zero;
        surface.anchorMin = new Vector2(0.02f, 0.02f);
        surface.anchorMax = new Vector2(0.98f, 0.98f);
        surface.offsetMin = surface.offsetMax = Vector2.zero;
        UnityEngine.Object.DestroyImmediate(body);
        for (int i = surface.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(surface.GetChild(i).gameObject);
        surface.gameObject.name = "Rotation controls";
        var dial = new GameObject("Direction pad", typeof(RectTransform), typeof(AspectRatioFitter));
        dial.transform.SetParent(surface, false);
        var fitter = dial.GetComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        fitter.aspectRatio = 1;
        var keys = owner.transform.Find("NavModControls/Container/prefabPnlWASD");
        if (keys == null || owner.chkStationKeeping == null) throw new InvalidOperationException("Native Controls button bindings changed.");
        module.Arrows = new[] {
            Arrow(keys, "W", dial.transform, 0.5f, 0.83f, () => module.Command(RotationDirection.Forward)),
            Arrow(keys, "S", dial.transform, 0.5f, 0.17f, () => module.Command(RotationDirection.Backward)),
            Arrow(keys, "A", dial.transform, 0.17f, 0.5f, () => module.Command(RotationDirection.Left)),
            Arrow(keys, "D", dial.transform, 0.83f, 0.5f, () => module.Command(RotationDirection.Right))
        };
        var hold = UnityEngine.Object.Instantiate(owner.chkStationKeeping, dial.transform, false);
        hold.name = "Hold bearing";
        hold.group = null;
        hold.onValueChanged = new Toggle.ToggleEvent();
        hold.SetIsOnWithoutNotify(false);
        hold.onValueChanged.AddListener(module.ToggleHold);
        hold.navigation = new Navigation { mode = Navigation.Mode.None };
        Place(hold.transform as RectTransform, 0.5f, 0.5f, ButtonSize);
        foreach (var label in hold.GetComponentsInChildren<TMP_Text>(true)) {
            label.text = "HOLD";
            label.enableAutoSizing = true;
            label.fontSizeMin = 6;
            label.fontSizeMax = 8;
            label.alignment = TextAlignmentOptions.Center;
            label.margin = Vector4.zero;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Truncate;
            FitHoldLabel(label.rectTransform);
            label.raycastTarget = false;
        }
        foreach (var label in hold.GetComponentsInChildren<Text>(true)) {
            label.text = "HOLD";
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 6;
            label.resizeTextMaxSize = 8;
            FitHoldLabel(label.rectTransform);
            label.raycastTarget = false;
        }
        module.HoldButton = hold;
        module.HoldOnLabel = hold.transform.Find("Background/Checkmark/lblOn")?.gameObject;
        module.HoldOffLabel = hold.transform.Find("Background/lblOff")?.gameObject;
    }

    internal static void ConfigureInputSurface(CanvasGroup readout) {
        if (readout == null) throw new InvalidOperationException("Native DUHRT readout CanvasGroup is unavailable.");
        readout.interactable = true;
        readout.blocksRaycasts = true;
        // Native Draggable disables the outer Container during layout editing.
        // Keep respecting that parent rather than bypassing its input gate.
        readout.ignoreParentGroups = false;
    }

    static void FitHoldLabel(RectTransform rect) {
        rect.anchorMin = new Vector2(0.25f, 0.24f);
        rect.anchorMax = new Vector2(0.75f, 0.76f);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    static Button Arrow(Transform source, string key, Transform parent, float x, float y, UnityEngine.Events.UnityAction action) {
        var native = source.Find("bmpKey" + key);
        if (native == null) throw new InvalidOperationException("Native Controls arrow missing: " + key);
        var copy = UnityEngine.Object.Instantiate(native.gameObject, parent, false);
        copy.name = "Rotation " + key;
        foreach (var input in copy.GetComponents<GUIBtnPressHold>()) UnityEngine.Object.DestroyImmediate(input);
        foreach (var events in copy.GetComponents<EventTrigger>()) UnityEngine.Object.DestroyImmediate(events);
        foreach (var lamp in copy.GetComponentsInChildren<GUILamp>(true)) UnityEngine.Object.DestroyImmediate(lamp.gameObject);
        var button = copy.GetComponent<Button>() ?? copy.AddComponent<Button>();
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(action);
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        Place(copy.transform as RectTransform, x, y, ButtonSize);
        var face = copy.GetComponent<Image>();
        if (face == null) throw new InvalidOperationException("Native Controls button face missing: " + key);
        face.raycastTarget = true;
        button.targetGraphic = face;
        var glyph = copy.transform.Find("txt") as RectTransform;
        if (glyph == null) throw new InvalidOperationException("Native Controls arrow glyph missing: " + key);
        // The face sprite's outer rect includes transparent bezel padding.
        // Leave room inside its visible face, not merely inside that outer rect.
        glyph.pivot = new Vector2(0.5f, 0.5f);
        glyph.localScale = Vector3.one;
        var ink = glyph.GetComponent<Image>();
        if (ink?.sprite == null) throw new InvalidOperationException("Native Controls arrow sprite missing: " + key);
        var bounds = GlyphBounds(ink.sprite.rect.size, UnityEngine.Sprites.DataUtility.GetPadding(ink.sprite), glyph.localRotation);
        glyph.anchorMin = bounds.min;
        glyph.anchorMax = bounds.max;
        glyph.offsetMin = glyph.offsetMax = Vector2.zero;
        ink.type = Image.Type.Simple;
        ink.useSpriteMesh = false;
        ink.preserveAspect = false; // Bounds already preserve the sprite's aspect.
        ink.raycastTarget = false;
        AudioManager.AddBtnAudio(copy, "ShipUIBtnNSDockSysClampIn", "ShipUIBtnNSDockSysClampOut");
        return button;
    }

    internal static (Vector2 min, Vector2 max) GlyphBounds(Vector2 size, Vector4 padding, Quaternion rotation) {
        float width = size.x - padding.x - padding.z, height = size.y - padding.y - padding.w;
        if (width <= 0 || height <= 0) throw new InvalidOperationException("Native arrow sprite has no visible area.");
        float scale = 0.35f / Math.Max(width, height);
        var shift = rotation * new Vector3((padding.x - padding.z) * scale / 2, (padding.y - padding.w) * scale / 2, 0);
        var center = new Vector2(0.5f - shift.x, 0.5f - shift.y);
        var half = size * (scale / 2);
        return (center - half, center + half);
    }

    static void Place(RectTransform rect, float x, float y, float size) {
        rect.anchorMin = new Vector2(x - size / 2, y - size / 2);
        rect.anchorMax = new Vector2(x + size / 2, y + size / 2);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }
}
