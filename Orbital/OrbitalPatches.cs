using System;
using HarmonyLib;
using UnityEngine;
using Ostranauts.Ships.AIPilots;

namespace Ostrabus.Orbital;

[HarmonyPatch(typeof(StarSystem), nameof(StarSystem.Update))]
internal static class OrbitalStepObservationPatch {
    internal sealed class Step : IDisposable {
        internal SffSimulation Motion;
        internal Economy.SffEconomy Economy;
        public void Dispose() { try { Economy?.Dispose(); } finally { Motion?.Dispose(); } }
    }
    [HarmonyPrefix]
    internal static void Prefix(StarSystem __instance, double fTimeDelta, out Step __state) {
        OrbitalSimulation.ObserveUpdate(__instance, fTimeDelta);
        __state = null;
        if (!GUIFFWD.Active || fTimeDelta < 60) return;
        var economy = Economy.SffEconomy.Begin(__instance, fTimeDelta, true);
        var motion = SffSimulation.Begin(__instance, fTimeDelta, true);
        if (economy != null || motion != null) __state = new Step { Motion = motion, Economy = economy };
    }
    [HarmonyFinalizer]
    internal static Exception Finalizer(Exception __exception, Step __state) {
        try { __state?.Dispose(); }
        catch (Exception ex) { Ostrabus.OstrabusPlugin.Logger?.LogError("[Orbital SFF] Cleanup: " + ex); }
        return __exception;
    }
}

// Restrict the correction to the numerical simulation's fast selector. Public
// planning/UI/atmosphere queries and individual-body gravity functions are unchanged.
[HarmonyPatch(typeof(StarSystem), "GetGreatestGravBOFast")]
internal static class OrbitalGravityPatch {
    [HarmonyPrefix]
    internal static bool Prefix(ShipSitu objSS, ref Vector2 ptGrav, ref BodyOrbit boClosest, ref BodyOrbit __result) {
        return SffSimulation.Current == null || !SffSimulation.Current.Gravity(objSS, ref ptGrav, ref boClosest, ref __result);
    }
    [HarmonyPostfix]
    internal static void Postfix(StarSystem __instance, ShipSitu objSS, BodyOrbit __result, ref Vector2 ptGrav) {
        if (!OrbitalSimulation.Enabled) return;
        Vector2 original = ptGrav;
        try {
            OrbitalSimulation.Correct(__instance, objSS, __result, ref ptGrav);
            CoastIntegration.Selected(objSS, __result, ptGrav);
        }
        catch (Exception ex) { ptGrav = original; OrbitalSimulation.Fault(ex); }
    }
}

[HarmonyPatch(typeof(ShipSitu), nameof(ShipSitu.TimeAdvance))]
internal static class SffMotionPatch {
    [HarmonyPrefix]
    internal static bool Prefix(ShipSitu __instance, double fTime, bool bIgnoreAccel) {
        return SffSimulation.Current == null || !SffSimulation.Current.Advance(__instance, fTime, bIgnoreAccel);
    }
}

[HarmonyPatch(typeof(StarSystem), "UpdateShip")]
internal static class CoastScopePatch {
    [HarmonyPrefix]
    internal static void Prefix(Ship ship, double fTimeDelta, out CoastIntegration.Context __state) {
        __state = CoastIntegration.Enter(ship, fTimeDelta);
    }
    [HarmonyFinalizer]
    internal static Exception Finalizer(Exception __exception, CoastIntegration.Context __state) {
        CoastIntegration.Leave(__state);
        return __exception;
    }
}

[HarmonyPatch(typeof(ShipSitu), nameof(ShipSitu.TimeAdvance))]
internal static class CoastMotionPatch {
    [HarmonyPrefix]
    internal static void Prefix(ShipSitu __instance, double fTime, bool bIgnoreAccel, out CoastIntegration.Result __state) {
        __state = default;
        try { __state = CoastIntegration.Prepare(__instance, fTime, bIgnoreAccel); }
        catch (Exception ex) { OrbitalSimulation.Fault(ex); }
    }
    [HarmonyPostfix]
    internal static void Postfix(ShipSitu __instance, CoastIntegration.Result __state) {
        CoastIntegration.Complete(__instance, __state);
    }
}

[HarmonyPatch(typeof(AIShipManager), nameof(AIShipManager.FFWD))]
internal static class SffBeforeCatchupPatch {
    [HarmonyPrefix]
    internal static void Prefix() { SffSimulation.Current?.BeforeAi(); }
}

[HarmonyPatch(typeof(AIShip), nameof(AIShip.FFWD))]
internal static class SffCatchupPatch {
    [HarmonyPrefix]
    internal static bool Prefix(AIShip __instance) { return SffSimulation.Current == null || SffSimulation.Current.AllowCatchup(__instance); }
}

[HarmonyPatch(typeof(ConsoleResolver), nameof(ConsoleResolver.ResolveString))]
internal static class OrbitalConsolePatch {
    [HarmonyPrefix]
    internal static bool Prefix(string strInput) {
        if (strInput == null) return true;
        string command = strInput.Trim().ToLowerInvariant();
        if (command != "orbital" && !command.StartsWith("orbital ", StringComparison.Ordinal)) return true;
        string argument = command.Length > 7 ? command.Substring(7).Trim() : "status";
        if (argument == "on") OrbitalSimulation.SetEnabled(true);
        else if (argument == "off") OrbitalSimulation.SetEnabled(false);
        else if (argument == "toggle") OrbitalSimulation.SetEnabled(!OrbitalSimulation.Enabled);
        else if (argument == "sff on" || argument == "sff off") {
            SffSimulation.Enabled = argument == "sff on";
            OrbitalSimulation.Status();
        }
        else if (argument == "coast on" || argument == "coast off") {
            CoastIntegration.SetEnabled(argument == "coast on");
            OrbitalSimulation.Status();
        }
        else if (argument == "status" || argument.Length == 0) OrbitalSimulation.Status();
        else ConsoleToGUI.instance?.LogInfo("Usage: orbital on | off | toggle | status | sff on | sff off | coast on | coast off");
        return false;
    }
}
