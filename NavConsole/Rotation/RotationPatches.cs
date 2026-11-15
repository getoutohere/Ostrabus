using System;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.AIPilots.Interfaces;
using Ostranauts.Ships.Commands;

namespace Ostrabus.NavConsole.Rotation;

[HarmonyPatch(typeof(StarSystem), nameof(StarSystem.Update))]
internal static class RotationObservationPatch {
    [HarmonyPriority(Priority.First)]
    static void Prefix(StarSystem __instance, double fTimeDelta) {
        if (fTimeDelta > 0 && ReferenceEquals(__instance, CrewSim.system)) RotationRuntime.SampleWorld();
    }
}

[HarmonyPatch(typeof(GUIOrbitDraw), "LoadModules")]
internal static class RotationModuleLoadPatch {
    static void Prefix(GUIOrbitDraw __instance, CondOwner coNav) {
        try { RotationPanel.Prepare(__instance, coNav); }
        catch (Exception error) { OstrabusPlugin.Logger?.LogWarning("[Rotation] Could not create module: " + error); }
    }
}

[HarmonyPatch(typeof(AIShip), nameof(AIShip.CreateCommand))]
internal static class RotationCommandFactoryPatch {
    static bool Prefix(string commandName, IAICharacter aiCharacter, ref ICommand __result) {
        if (commandName != OstrabusRotationPilot.CommandName) return true;
        __result = aiCharacter == null ? null : new OstrabusRotationPilot(aiCharacter);
        return false;
    }
}

[HarmonyPatch(typeof(AIShip), nameof(AIShip.FFWD))]
internal static class RotationFastForwardPatch {
    static bool Prefix(AIShip __instance) {
        if (!RotationRuntime.HasCommands) return true;
        var pilot = RotationRuntime.Find(__instance.Ship);
        // Physics advances rotation; preserve the existing queue and avoid the
        // native command-node warning and duplicate OneShotWrapper enqueue.
        return pilot == null || !ReferenceEquals(pilot.Owner, __instance);
    }
}

[HarmonyPatch(typeof(Ship), nameof(Ship.Maneuver))]
internal static class RotationManualInputPatch {
    static bool Prefix(Ship __instance, float fX, float fY, float fR, int nNoiseOnly, float fDeltaTime, Ship.EngineMode engineMode) {
        if (RotationRuntime.Actuating) return true;
        var console = GUIOrbitDraw.Instance;
        bool dockingInput = ReferenceEquals(RotationRuntime.DockInputShip, __instance);
        bool sameConsole = !dockingInput && console != null && ReferenceEquals(console.COSelf?.ship, __instance);
        // Docking input injects automatic yaw while translating near a target.
        // Its explicit rotation keys are handled before KeyHandler runs; the
        // synthesized yaw must not be mistaken for a player's rotation order.
        bool manual = !dockingInput && (fR != 0 || sameConsole && RotationModule.ManualRotation(console));
        if (manual) {
            if (RotationRuntime.HasCommands) RotationRuntime.CancelGroup(__instance);
            return true;
        }
        if (!RotationRuntime.HasCommands) return true;
        var pilot = RotationRuntime.Find(__instance);
        if (pilot == null) return true;
        if (fDeltaTime <= 0 || nNoiseOnly != 0) return true;
        if (engineMode != Ship.EngineMode.RCS) { RotationRuntime.Cancel(__instance); return true; }
        pilot.SetTranslation(fX, fY);
        return false;
    }
}

[HarmonyPatch(typeof(ShipSitu), nameof(ShipSitu.TimeAdvance))]
internal static class RotationPhysicsPatch {
    [HarmonyPriority(Priority.First)]
    static void Prefix(ShipSitu __instance, double fTime, bool bIgnoreAccel, ref RotationPose __state) {
        if (!RotationRuntime.Actuating && !bIgnoreAccel && fTime > 0 && RotationRuntime.HasCommands) __state = RotationRuntime.Advance(__instance, fTime);
    }
    [HarmonyPriority(Priority.Last)]
    static void Postfix(ShipSitu __instance, RotationPose __state) {
        if (!__state.Applied) return;
        __instance.fRot = __state.Rotation;
        __instance.fW = __state.Rate;
        __instance.fA = 0;
        __state.Ship.UpdateDockedShips(__instance);
    }
}

[HarmonyPatch(typeof(GUIDockSys), "KeyHandler")]
internal static class RotationDockInputPatch {
    static void Prefix(GUIDockSys __instance, out Ship __state) {
        __state = RotationRuntime.DockInputShip;
        RotationRuntime.DockInputShip = __instance.COSelf?.ship;
        if (RotationRuntime.HasCommands && RotationRuntime.DockInputShip != null && RotationModule.ManualRotation(__instance))
            RotationRuntime.CancelGroup(RotationRuntime.DockInputShip);
    }
    static Exception Finalizer(Exception __exception, Ship __state) { RotationRuntime.DockInputShip = __state; return __exception; }
}

[HarmonyPatch(typeof(AIShipManager), "UnregisterShip", new[] { typeof(AIShip) })]
internal static class RotationWithdrawalPatch {
    static void Postfix(AIShip __0) => RotationRuntime.Withdrawn(__0);
}
