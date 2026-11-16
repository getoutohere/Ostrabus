using System;
using HarmonyLib;
using Ostrabus.QoL.CoursePlot;
using Ostranauts.Components;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

static partial class Program
{
    static readonly AccessTools.FieldRef<Slider, float> SliderValue = AccessTools.FieldRefAccess<Slider, float>("m_Value");
    static readonly AccessTools.FieldRef<PrecisionSlider, float> RawPrecision = AccessTools.FieldRefAccess<PrecisionSlider, float>("_rawValue");
    static readonly AccessTools.FieldRef<PrecisionSlider, bool> PrecisionActive = AccessTools.FieldRefAccess<PrecisionSlider, bool>("_precisionEngaged");
    static bool SliderSet(Slider __instance, float __0)
    {
        // Engine presentation/callback boundary. Preserve value clamping; native
        // PrecisionSlider.Set and ApplyPrecisionDrag still execute above this.
        SliderValue(__instance) = Math.Max(__instance.minValue, Math.Min(__instance.maxValue, __0));
        return false;
    }
    static bool Round(float __0, ref float __result)
    { __result = (float)Math.Round(__0, MidpointRounding.ToEven); return false; }
    static PrecisionSlider Precision()
    {
        var slider = Empty<PrecisionSlider>();
        AccessTools.Field(typeof(Slider), "m_MinValue").SetValue(slider, 0f);
        AccessTools.Field(typeof(Slider), "m_MaxValue").SetValue(slider, 1f);
        AccessTools.Field(typeof(Slider), "m_Direction").SetValue(slider, Slider.Direction.BottomToTop);
        SliderValue(slider) = .3f; RawPrecision(slider) = .3f; PrecisionActive(slider) = true;
        return slider;
    }
    static void NativeDrag(PrecisionSlider slider, float delta)
    {
        var pointer = new PointerEventData(null) { delta = new Vector2(0, delta) };
        AccessTools.Method(typeof(PrecisionSlider), "ApplyPrecisionDrag").Invoke(slider, new object[] { pointer });
    }
    static void PrecisionCoexistence(Harmony fixture)
    {
        fixture.Patch(AccessTools.Method(typeof(Slider), "Set", new[] { typeof(float), typeof(bool) }),
            prefix: new HarmonyMethod(typeof(Program), nameof(SliderSet)));
        fixture.Patch(AccessTools.Method(typeof(Mathf), "Round", new[] { typeof(float) }),
            prefix: new HarmonyMethod(typeof(Program), nameof(Round)));
        var slider = Precision();
        slider.value = .4f;
        NativeDrag(slider, 20);
        Check(Math.Abs(slider.value - .302f) < .000002, "Native stale-accumulator negative control did not reproduce the drag jump");

        slider = Precision();
        CourseFineControls.SetValue(slider, .4f);
        Check(Math.Abs(slider.value - .4f) < .000002 && RawPrecision(slider) == slider.value,
            "Wheel update did not synchronize the active native precision baseline");
        NativeDrag(slider, 20);
        Check(Math.Abs(slider.value - .402f) < .000002, "Shift-drag reverted the wheel adjustment");
        Check(PrecisionActive(slider), "Wheel update stole native pointer/precision ownership");
        CourseFineControls.SetValue(slider, .5f);
        NativeDrag(slider, -20);
        Check(Math.Abs(slider.value - .498f) < .000002, "Reverse native drag lost the latest wheel baseline");

        CourseFineControls.SetValue(slider, 2);
        Check(slider.value == 1 && RawPrecision(slider) == 1, "Native maximum clamp was not used for the synchronized baseline");
        NativeDrag(slider, -20);
        Check(Math.Abs(slider.value - .998f) < .000002, "Native drag could not leave the maximum after wheel clamping");
        CourseFineControls.SetValue(slider, -1);
        Check(slider.value == 0 && RawPrecision(slider) == 0, "Native minimum clamp was not used");
        NativeDrag(slider, 20);
        Check(Math.Abs(slider.value - .002f) < .000002, "Native drag could not leave the minimum after wheel clamping");

        slider = Precision(); PrecisionActive(slider) = false;
        CourseFineControls.SetValue(slider, .4f);
        Check(slider.value == .4f && RawPrecision(slider) == .3f && !PrecisionActive(slider),
            "Ordinary wheel use entered native precision mode");
        slider = Precision();
        AccessTools.Field(typeof(PrecisionSlider), "_suppressSet").SetValue(slider, true);
        CourseFineControls.SetValue(slider, .4f);
        Check(slider.value == .3f && RawPrecision(slider) == .3f,
            "Wheel assignment bypassed native pointer-down suppression");
        var ordinary = Empty<Slider>();
        AccessTools.Field(typeof(Slider), "m_MaxValue").SetValue(ordinary, 1f);
        CourseFineControls.SetValue(ordinary, .4f);
        Check(ordinary.value == .4f, "Ordinary slider support was replaced by a precision-only path");
    }
}
