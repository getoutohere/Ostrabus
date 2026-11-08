using System;
using BepInEx.Configuration;
using HarmonyLib;
using Ostranauts.Ships.Sensors;

namespace Ostrabus.EMPropagation;

// Explicitly installed so a disabled module adds no sensor hooks.
internal static class EmPropagation {
    internal static ConfigEntry<bool> Flag;
    static readonly Harmony harmony = new Harmony("com.ostranauts.template.em-propagation");
    internal static bool Enabled { get; private set; }
    internal static void Initialize(ConfigFile config) {
        Flag = config.Bind("EMPropagation", "Enabled", false, "Use inverse-square EM falloff beyond each receiver's reference range. Affects player and AI reception. Toggle live in PDA Mod Settings.");
        SetEnabled(Flag.Value);
    }
    internal static void SetEnabled(bool value) {
        if (value != Enabled) {
            harmony.UnpatchSelf();
            Enabled = false;
            if (value) {
                try {
                    harmony.Patch(AccessTools.Method(typeof(EM),nameof(EM.GetSignalStrength)), prefix: new HarmonyMethod(typeof(EmPropagation),nameof(Strength)));
                    harmony.Patch(AccessTools.Method(typeof(EM),nameof(EM.GetDetectionDistance)), prefix: new HarmonyMethod(typeof(EmPropagation),nameof(Distance)));
                    Enabled = true;
                } catch { harmony.UnpatchSelf(); throw; }
            }
            Sensors.SensorContacts.Reset();
        }
        if (Flag != null) Flag.Value = value;
    }
    internal static void Stop() { harmony.UnpatchSelf(); Enabled = false; }
    static bool Strength(EM __instance, ShipSignature __0, double __1, ref double __result) {
        __result = __instance.On ? PropagationModel.Signal(__1,__instance.RangeKm,__0.EMEmissions) : 0;
        return false;
    }
    static bool Distance(EM __instance, ShipSignature __0, float __2, ref double __result) {
        // Match the native helper's state contract: its caller filters receivers by On.
        __result = PropagationModel.DetectionDistance(__instance.RangeKm,__0.EMEmissions,__2);
        return false;
    }
}
