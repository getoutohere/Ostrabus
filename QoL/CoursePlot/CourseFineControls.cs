using System;
using HarmonyLib;
using Ostranauts.ShipGUIs.NavStation;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Ostrabus.QoL.CoursePlot;

internal static class CourseFineControls {
    const double AuPerMps2 = 6.6845869117759804e-12;
    internal static readonly AccessTools.FieldRef<NavModBase, CondOwner> Console = AccessTools.FieldRefAccess<NavModBase, CondOwner>("COSelf");
    internal static readonly AccessTools.FieldRef<NavModBase, GUIOrbitDraw> Nav = AccessTools.FieldRefAccess<NavModBase, GUIOrbitDraw>("_guiOrbitDraw");
    static readonly AccessTools.FieldRef<NavModCoursePlot, Slider> Drift = AccessTools.FieldRefAccess<NavModCoursePlot, Slider>("slidCrsCoast");
    static readonly AccessTools.FieldRef<NavModCoursePlot, TMP_Text> Acceleration = AccessTools.FieldRefAccess<NavModCoursePlot, TMP_Text>("txtCrsAcc");
    internal static double G(Ship ship, float cycle) => ship.GetMaxTorchThrust(cycle) / AuPerMps2 / 9.81;
    static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    internal static float NextDrift(float value, float min, float max, int direction) {
        if (!Finite(value) || !Finite(min) || !Finite(max) || min > max || direction == 0) return value;
        double next = Math.Round(value * 100d, MidpointRounding.AwayFromZero) / 100 + Math.Sign(direction) * .01;
        return (float)Math.Max(min, Math.Min(max, next));
    }
    internal static float NextCycle(Ship ship, float value, float min, float max, int direction) {
        if (ship == null || !Finite(value) || !Finite(min) || !Finite(max) || min > max || direction == 0) return value;
        double current = G(ship, value), lowG = G(ship, min), highG = G(ship, max);
        if (!Finite(current) || !Finite(lowG) || !Finite(highG) || highG <= lowG) return value;
        double desired = Math.Round(current * 100, MidpointRounding.AwayFromZero) / 100 + Math.Sign(direction) * .01;
        if (desired <= lowG) return min;
        if (desired >= highG) return max;
        // Invert the actual game thrust law, including its float arithmetic. Keep
        // the lower bracket so the requested G ceiling is not overshot.
        float lo = min, hi = max;
        for (int i = 0; i < 40; i++) {
            float mid = lo + (hi - lo) * .5f;
            if (mid == lo || mid == hi) break;
            if (G(ship, mid) <= desired) lo = mid; else hi = mid;
        }
        return lo;
    }
    internal static void Attach(NavModCoursePlot module) {
        Add(module, module.slidCrsLim, true);
        Add(module, Drift(module), false);
    }
    static void Add(NavModCoursePlot module, Slider slider, bool acceleration) {
        if (slider == null) return;
        var control = slider.GetComponent<CourseSliderScroll>();
        if (control == null) control = slider.gameObject.AddComponent<CourseSliderScroll>();
        control.Owner = module; control.Slider = slider; control.IsAcceleration = acceleration;
        // Native slider graphics may only cover the narrow track and handle.
        // Give the whole existing slider rectangle a transparent raycast target.
        if (slider.GetComponent<Graphic>() == null) slider.gameObject.AddComponent<Image>().color = Color.clear;
    }
    internal static void Refresh(NavModCoursePlot module) {
        Ship ship = Console(module)?.ship;
        var label = Acceleration(module);
        if (ship == null || !ship.bFusionReactorRunning || module.slidCrsLim == null || label == null) return;
        var control = module.slidCrsLim.GetComponent<CourseSliderScroll>();
        if (control == null) return;
        double g = G(ship, module.slidCrsLim.value);
        if (Finite(g)) label.text = control.FormatG(g);
    }
}

internal sealed class CourseSliderScroll : MonoBehaviour, IScrollHandler, IPointerEnterHandler, IPointerExitHandler {
    static WeakReference<CourseSliderScroll> hovered;
    internal NavModCoursePlot Owner;
    internal Slider Slider;
    internal bool IsAcceleration;
    double displayedG = double.NaN;
    string displayText;
    internal string FormatG(double g) {
        double rounded = Math.Round(g, 2, MidpointRounding.AwayFromZero);
        if (displayText == null || rounded != displayedG) { displayedG = rounded; displayText = rounded.ToString("0.00") + "G"; }
        return displayText;
    }
    bool Ready => isActiveAndEnabled && Owner != null && Owner.isActiveAndEnabled && Slider != null && Slider.IsActive() && Slider.IsInteractable();
    public void OnPointerEnter(PointerEventData data) {
        if (!Ready) return;
        camera = data.enterEventCamera;
        if (hovered == null) hovered = new WeakReference<CourseSliderScroll>(this); else hovered.SetTarget(this);
    }
    public void OnPointerExit(PointerEventData data) { ClearHover(); }
    void ClearHover() { if (hovered != null && hovered.TryGetTarget(out var control) && ReferenceEquals(control, this)) hovered.SetTarget(null); }
    internal static bool OwnsHover(GUIOrbitDraw nav) => hovered != null && hovered.TryGetTarget(out var control) && control != null && control.Ready &&
        ReferenceEquals(CourseFineControls.Nav(control.Owner), nav) &&
        Mouse.current != null && RectTransformUtility.RectangleContainsScreenPoint((RectTransform)control.Slider.transform, Mouse.current.position.ReadValue(), control.camera);
    Camera camera;
    public void OnScroll(PointerEventData data) {
        if (data == null || !Ready || data.scrollDelta.y == 0 || float.IsNaN(data.scrollDelta.y)) return;
        camera = data.enterEventCamera;
        if (!RectTransformUtility.RectangleContainsScreenPoint((RectTransform)Slider.transform, data.position, camera)) return;
        int direction = Math.Sign(data.scrollDelta.y);
        float next;
        if (IsAcceleration) {
            Ship ship = CourseFineControls.Console(Owner)?.ship;
            if (ship == null || !ship.bFusionReactorRunning) return;
            next = CourseFineControls.NextCycle(ship, Slider.value, Slider.minValue, Slider.maxValue, direction);
        } else next = CourseFineControls.NextDrift(Slider.value, Slider.minValue, Slider.maxValue, direction);
        // Native value assignment preserves persistence and the existing delayed
        // replan callback. No planner or coroutine is duplicated here.
        Slider.value = next;
        CourseFineControls.Refresh(Owner);
        data.Use();
    }
    void OnDisable() { ClearHover(); camera = null; }
    void OnDestroy() { ClearHover(); Owner = null; Slider = null; camera = null; displayText = null; }
}

[HarmonyPatch(typeof(NavModCoursePlot), "Init")]
internal static class CourseFineInitPatch {
    static void Postfix(NavModCoursePlot __instance) { CourseFineControls.Attach(__instance); }
}

[HarmonyPatch(typeof(NavModCoursePlot), "UpdateUI")]
internal static class CourseFineReadoutPatch {
    static void Postfix(NavModCoursePlot __instance) {
        if (__instance.NavPlan == null && CourseFineControls.Console(__instance)?.ship?.objSS?.NavData == null) CourseFineControls.Refresh(__instance);
    }
}

// Native MouseHandler calls ScrollLog for wheel motion even over unrelated UI;
// ScrollLog always plays ShipUINSMapPan02. Suppress only that log-scroll path
// while one of our two slider rectangles owns the pointer.
[HarmonyPatch(typeof(GUIOrbitDraw), "ScrollLog")]
internal static class CourseFineLogScrollPatch {
    static bool Prefix(GUIOrbitDraw __instance) => !CourseSliderScroll.OwnsHover(__instance);
}
