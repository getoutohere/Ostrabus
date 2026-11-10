using System;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using Ostrabus.Hotkeys;

namespace Ostrabus.PdaSettings;

// Created only on first open. A closed panel has no active Update or coroutine.
internal sealed class SettingsPanel : MonoBehaviour {
    sealed class Row {
        internal Setting Setting;
        internal TMP_Text Value;
        internal TMP_Text Description;
        internal RectTransform Panel, FirstControl, SecondControl, Number;
        internal Button Minus, Plus;
        internal Slider Slider;
        internal int Displayed = int.MinValue;
        internal string DisplayedBinding;
    }
    static readonly AccessTools.FieldRef<GUIPDA, CanvasGroup> Home = AccessTools.FieldRefAccess<GUIPDA, CanvasGroup>("cgHome");
    static SettingsPanel current;
    GUIPDA owner;
    CanvasGroup home;
    TMP_Text status;
    TMP_FontAsset font;
    Row[] rows;
    RectTransform content;
    float nextRefresh;
    Row capturing;

    internal static void Open(GUIPDA pda) {
        if (pda == null) return;
        // This also restores the native Home canvas if our previous open used it.
        pda.State = GUIPDA.UIState.Home;
        if (current == null || current.owner != pda) {
            if (current != null) Destroy(current.gameObject);
            CanvasGroup homeCanvas = Home(pda);
            var go = new GameObject("TemplateSettings", typeof(RectTransform));
            go.SetActive(false);
            var rect = (RectTransform)go.transform;
            var source = (RectTransform)homeCanvas.transform;
            go.layer = source.gameObject.layer;
            rect.SetParent(source.parent, false);
            rect.anchorMin = source.anchorMin; rect.anchorMax = source.anchorMax;
            rect.pivot = source.pivot; rect.sizeDelta = source.sizeDelta;
            rect.anchoredPosition3D = source.anchoredPosition3D;
            rect.localRotation = source.localRotation; rect.localScale = source.localScale;
            rect.SetSiblingIndex(source.GetSiblingIndex() + 1);
            current = go.AddComponent<SettingsPanel>();
            current.owner = pda; current.home = homeCanvas;
            try { current.Build(); }
            catch { Destroy(go); current = null; throw; }
        }
        CanvasManager.HideCanvasGroup(current.home);
        current.gameObject.SetActive(true);
        current.status.text = "Changes apply immediately. Saved options also update the mod config.";
        current.Refresh();
    }
    internal static void Hide(GUIPDA pda) {
        if (current == null || current.owner != pda || !current.gameObject.activeSelf) return;
        current.gameObject.SetActive(false);
        if (current.home != null) CanvasManager.ShowCanvasGroup(current.home);
    }
    internal static void Stop() {
        if (current == null) return;
        Hide(current.owner);
        Destroy(current.gameObject);
        current = null;
    }
    void OnDestroy() {
        if (ReferenceEquals(current, this)) current = null;
        owner = null; home = null; rows = null; content = null; status = null; font = null;
    }
    void OnEnable() { LayoutRows(); }
    void OnDisable() {
        if (capturing != null) capturing.DisplayedBinding = null;
        capturing = null;
    }
    void OnRectTransformDimensionsChange() { if (isActiveAndEnabled) LayoutRows(); }
    void Update() {
        if (capturing != null) CaptureShortcut();
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.25f;
        Refresh();
    }
    void Build() {
        var nativeLabel = owner.homepage?.m_prefabApp?.GetComponent<GUIPDAApp>()?.m_txtName;
        if (nativeLabel == null) nativeLabel = owner.GetComponentInChildren<TextMeshProUGUI>(true);
        font = nativeLabel != null ? nativeLabel.font : TMP_Settings.defaultFontAsset;
        if (font == null) throw new InvalidOperationException("PDA font is unavailable.");
        var background = gameObject.AddComponent<Image>();
        background.color = new Color(0.055f, 0.07f, 0.075f, 1f);
        var header = Box("Header", transform, 0, 64);
        Text("Title", header, "MOD SETTINGS", 21, 12, 6, -12, 27);
        Text("Subtitle", header, "Work, navigation and crew controls", 13, 12, 32, -12, 22);
        var footer = Box("Footer", transform, 0, 76);
        footer.anchorMin = new Vector2(0, 0); footer.anchorMax = new Vector2(1, 0);
        footer.pivot = new Vector2(0.5f, 0); footer.anchoredPosition = Vector2.zero;
        status = Text("Status", footer, "", 12, 12, 2, -12, 36);
        Button("Home", footer, "HOME", () => GUIPDA.OpenApp("home"), 12, 42, 100, 28);
        var viewport = Box("Viewport", transform, 64, 0);
        viewport.anchorMin = Vector2.zero; viewport.anchorMax = Vector2.one;
        viewport.offsetMin = new Vector2(8, 80); viewport.offsetMax = new Vector2(-28, -64);
        var viewImage = viewport.gameObject.AddComponent<Image>();
        viewImage.color = new Color(1, 1, 1, 0.015f);
        viewport.gameObject.AddComponent<RectMask2D>();
        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30; scroll.viewport = viewport;
        content = Box("Content", viewport, 0, 0);
        scroll.content = content;
        var track = Box("Scrollbar", transform, 0, 0);
        track.anchorMin = new Vector2(1, 0); track.anchorMax = Vector2.one;
        track.offsetMin = new Vector2(-20, 80); track.offsetMax = new Vector2(-8, -64);
        var trackImage = track.gameObject.AddComponent<Image>();
        trackImage.color = new Color(0.12f, 0.17f, 0.19f);
        var slidingArea = Box("SlidingArea", track, 0, 0);
        slidingArea.anchorMin = Vector2.zero; slidingArea.anchorMax = Vector2.one;
        slidingArea.offsetMin = new Vector2(2, 2); slidingArea.offsetMax = new Vector2(-2, -2);
        var handle = Box("Handle", slidingArea, 0, 0);
        handle.anchorMin = Vector2.zero; handle.anchorMax = Vector2.one;
        handle.offsetMin = Vector2.zero; handle.offsetMax = Vector2.zero;
        var handleImage = handle.gameObject.AddComponent<Image>();
        handleImage.color = new Color(0.55f, 0.73f, 0.76f);
        var scrollbar = track.gameObject.AddComponent<Scrollbar>();
        scrollbar.handleRect = handle; scrollbar.targetGraphic = handleImage;
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scrollbar.navigation = new Navigation { mode = Navigation.Mode.None };
        scroll.verticalScrollbar = scrollbar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        rows = new Row[SettingsRegistry.All.Length];
        for (int i = 0; i < rows.Length; i++) {
            var setting = SettingsRegistry.All[i];
            var row = new Row { Setting = setting };
            rows[i] = row;
            var panel = Box(setting.Id, content, 0, 0);
            row.Panel = panel;
            var image = panel.gameObject.AddComponent<Image>();
            image.color = new Color(1, 1, 1, i % 2 == 0 ? 0.055f : 0.025f);
            Text("Title", panel, setting.Title + "  /  " + setting.Lifetime, 15, 8, 5, -8, 23);
            row.Description = Text("Description", panel, setting.Description, 12, 8, 29, -8, 0);
            if (setting.IsKeyBinding) {
                var bind = Button("Bind", panel, "", () => BeginCapture(row), 8, 0, 230, 28);
                row.FirstControl = (RectTransform)bind.transform;
                row.Value = bind.GetComponentInChildren<TMP_Text>();
                var reset = Button("Reset", panel, "RESET", () => SaveBinding(row, EquipmentShortcut.Default), 246, 0, 80, 28);
                row.SecondControl = (RectTransform)reset.transform;
            } else if (setting.IsToggle) {
                var toggle = Button("Toggle", panel, "", () => Change(row, row.Setting.Value == 0 ? 1 : 0), 8, 0, 100, 28);
                row.FirstControl = (RectTransform)toggle.transform;
                row.Value = toggle.GetComponentInChildren<TMP_Text>();
            } else if (setting.IsPercentage) {
                var control = Box("Percentage", panel, 0, 28);
                control.anchorMax = new Vector2(0, 1); control.pivot = new Vector2(0, 1);
                control.sizeDelta = new Vector2(180, 28); control.anchoredPosition = new Vector2(8, 0);
                control.gameObject.AddComponent<Image>().color = new Color(1, 1, 1, 0.025f);
                var percentageTrack = Box("Track", control, 11, 6);
                percentageTrack.offsetMin = new Vector2(7, percentageTrack.offsetMin.y); percentageTrack.offsetMax = new Vector2(-7, percentageTrack.offsetMax.y);
                percentageTrack.gameObject.AddComponent<Image>().color = new Color(0.16f, 0.23f, 0.25f);
                var area = Box("HandleArea", control, 0, 28);
                area.offsetMin = new Vector2(7, area.offsetMin.y); area.offsetMax = new Vector2(-7, area.offsetMax.y);
                var percentageHandle = Box("Handle", area, 3, 22);
                // Slider stretches the thumb vertically. Inset that stretch instead
                // of adding a fixed height to the 28-pixel handle area.
                percentageHandle.anchorMin = Vector2.zero; percentageHandle.anchorMax = new Vector2(0, 1);
                percentageHandle.pivot = new Vector2(0.5f, 0.5f);
                percentageHandle.sizeDelta = new Vector2(14, -6); percentageHandle.anchoredPosition = Vector2.zero;
                var percentageHandleImage = percentageHandle.gameObject.AddComponent<Image>();
                percentageHandleImage.color = new Color(0.55f, 0.73f, 0.76f);
                row.Slider = control.gameObject.AddComponent<Slider>();
                row.Slider.handleRect = percentageHandle; row.Slider.targetGraphic = percentageHandleImage;
                row.Slider.minValue = setting.Min; row.Slider.maxValue = setting.Max; row.Slider.wholeNumbers = true;
                row.Slider.navigation = new Navigation { mode = Navigation.Mode.None };
                row.Slider.SetValueWithoutNotify(setting.Value);
                row.Slider.onValueChanged.AddListener(value => Change(row, Mathf.RoundToInt(value)));
                row.Value = Text("Value", panel, "", 17, 200, 0, -8, 28);
                row.FirstControl = control; row.Number = row.Value.rectTransform;
            } else {
                row.Minus = Button("Decrease", panel, "-", () => Change(row, row.Setting.Value - 1), 8, 0, 38, 28);
                row.Value = Text("Value", panel, "", 17, 50, 0, -52, 28);
                row.Value.alignment = TextAlignmentOptions.Left;
                row.Plus = Button("Increase", panel, "+", () => Change(row, row.Setting.Value + 1), 104, 0, 38, 28);
                row.FirstControl = (RectTransform)row.Minus.transform;
                row.SecondControl = (RectTransform)row.Plus.transform;
                row.Number = row.Value.rectTransform;
            }
        }
    }
    void LayoutRows() {
        if (rows == null || content == null) return;
        float top = 0;
        foreach (var row in rows) {
            if (row?.Description == null) return; // Construction may still be in progress.
            float width = row.Description.rectTransform.rect.width;
            if (width <= 0) return; // OnEnable/layout will retry once the native frame has bounds.
            float height = Mathf.Ceil(row.Description.GetPreferredValues(row.Setting.Description, width, Mathf.Infinity).y);
            row.Description.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            float buttonTop = 29 + height + 6;
            PlaceControl(row.FirstControl, buttonTop);
            PlaceControl(row.SecondControl, buttonTop);
            PlaceControl(row.Number, buttonTop);
            float rowHeight = buttonTop + 28 + 8;
            row.Panel.anchoredPosition = new Vector2(0, -top);
            row.Panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, rowHeight);
            top += rowHeight + 4;
        }
        content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(0, top - 4));
    }
    static void PlaceControl(RectTransform control, float top) {
        if (control != null) control.anchoredPosition = new Vector2(control.anchoredPosition.x, -top);
    }
    void Change(Row row, int value) {
        CancelCapture();
        try {
            row.Setting.Set(value);
            status.text = row.Setting.Title + ": " + row.Setting.Format(value) + " (" + row.Setting.Lifetime.ToLowerInvariant() + ").";
        } catch (Exception error) {
            status.text = "Could not change " + row.Setting.Title + ". See Player.log.";
            Ostrabus.OstrabusPlugin.Logger?.LogError("[PdaSettings] " + error);
        }
        Refresh();
    }
    void BeginCapture(Row row) {
        CancelCapture();
        capturing = row;
        row.Value.text = "PRESS SHORTCUT...";
        status.text = "Press the new shortcut. Esc cancels. Shift+R and Shift+T are reserved.";
    }
    void CancelCapture() {
        if (capturing != null) capturing.DisplayedBinding = null;
        capturing = null;
        Refresh();
    }
    void CaptureShortcut() {
        if (!Application.isFocused) { CancelCapture(); status.text = "Shortcut unchanged."; return; }
        var keyboard = Keyboard.current;
        if (keyboard == null) return;
        if (keyboard.escapeKey.wasPressedThisFrame) {
            CancelCapture(); status.text = "Shortcut unchanged."; return;
        }
        foreach (var key in keyboard.allKeys) {
            if (!key.wasPressedThisFrame || !CrewShortcut.MainKey(key.keyCode)) continue;
            SaveBinding(capturing, CrewShortcut.Capture(keyboard, key.keyCode).ToString());
            return;
        }
    }
    void SaveBinding(Row row, string value) {
        CancelCapture();
        try {
            row.Setting.SetBinding(value);
            status.text = row.Setting.Title + ": " + row.Setting.BindingValue + " (saved).";
        } catch (ArgumentException error) { status.text = error.Message; }
        catch (Exception error) {
            status.text = "Could not save shortcut. See Player.log.";
            Ostrabus.OstrabusPlugin.Logger?.LogError("[PdaSettings] " + error);
        }
        Refresh();
    }
    void Refresh() {
        if (rows == null) return;
        foreach (var row in rows) {
            if (row.Setting.IsKeyBinding) {
                if (row == capturing) continue;
                string binding = row.Setting.BindingValue;
                if (row.DisplayedBinding != binding) { row.DisplayedBinding = binding; row.Value.text = binding; }
                continue;
            }
            int value = row.Setting.Value;
            if (row.Displayed == value) continue;
            row.Displayed = value;
            row.Value.text = row.Setting.Format(value);
            if (row.Slider != null) row.Slider.SetValueWithoutNotify(value);
            row.Value.color = row.Setting.IsToggle && value != 0 ? new Color(0.5f, 0.95f, 0.7f) : Color.white;
            if (row.Minus != null) row.Minus.interactable = value > row.Setting.Min;
            if (row.Plus != null) row.Plus.interactable = value < row.Setting.Max;
        }
    }
    static RectTransform Box(string name, Transform parent, float top, float height) {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 1);
        rect.sizeDelta = new Vector2(0, height); rect.anchoredPosition = new Vector2(0, -top);
        return rect;
    }
    TMP_Text Text(string name, Transform parent, string value, float size, float left, float top, float right, float height) {
        var rect = Box(name, parent, top, height);
        rect.offsetMin = new Vector2(left, rect.offsetMin.y); rect.offsetMax = new Vector2(right, rect.offsetMax.y);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font; text.fontSize = size; text.color = new Color(0.88f, 0.93f, 0.94f);
        text.alignment = TextAlignmentOptions.TopLeft; text.textWrappingMode = TextWrappingModes.Normal;
        text.overflowMode = TextOverflowModes.Ellipsis; text.raycastTarget = false; text.text = value;
        return text;
    }
    Button Button(string name, Transform parent, string title, Action action, float left, float top, float width, float height) {
        var rect = Box(name, parent, top, height);
        rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1);
        rect.sizeDelta = new Vector2(width, height); rect.anchoredPosition = new Vector2(left, -top);
        var image = rect.gameObject.AddComponent<Image>(); image.color = new Color(0.16f, 0.23f, 0.25f);
        var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
        var colors = button.colors; colors.highlightedColor = new Color(0.75f, 0.95f, 1f); colors.pressedColor = new Color(0.45f, 0.65f, 0.75f); button.colors = colors;
        button.onClick.AddListener(() => action());
        var label = Text("Label", rect, title, 15, 2, 2, -2, height - 4);
        label.alignment = TextAlignmentOptions.Center;
        AudioManager.AddBtnAudio(rect.gameObject, null, "ShipUIBtnPDAClick02");
        return button;
    }
}

[HarmonyPatch(typeof(GUIPDA), nameof(GUIPDA.OpenApp))]
internal static class SettingsAppPatch {
    static bool Prefix(string appName) {
        if (appName != SettingsRegistry.AppId) return true;
        try { SettingsPanel.Open(GUIPDA.instance); }
        catch (Exception error) {
            if (GUIPDA.instance != null) SettingsPanel.Hide(GUIPDA.instance);
            Ostrabus.OstrabusPlugin.Logger?.LogError("[PdaSettings] Could not open app: " + error);
        }
        return false;
    }
}

[HarmonyPatch(typeof(GUIPDA), nameof(GUIPDA.State), MethodType.Setter)]
internal static class SettingsStatePatch {
    static void Prefix(GUIPDA __instance) { SettingsPanel.Hide(__instance); }
}
