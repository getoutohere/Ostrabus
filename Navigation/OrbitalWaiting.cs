using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;

namespace Ostrabus.ShipNavigation;

internal static class OrbitalWaiting {
    internal static void Coast(Ship ship, Blackboard blackboard) {
        if (!global::Ostrabus.Orbital.OrbitalSimulation.Enabled || ship == null || ship.bDestroyed || !ship.IsAIShip || ship.HideFromSystem || ship.IsDocked() || ship.IsUsingTorchDrive ||
            ship.shipCombatTarget != null || blackboard != null && blackboard.HasKey("InCombat") && blackboard.Get<bool>("InCombat") ||
            CrewSim.coPlayer?.ship == ship || CrewSim.system == null) return;
        var situ = ship.objSS;
        if (situ == null || !situ.bIgnoreGrav || situ.bIsBO || situ.bBOLocked || situ.bOrbitLocked || situ.bGrounded || situ.NavData != null ||
            situ.vAccIn.x != 0 || situ.vAccIn.y != 0 || situ.vAccRCS.x != 0 || situ.vAccRCS.y != 0 ||
            situ.vAccLift.x != 0 || situ.vAccLift.y != 0 || situ.vAccDrag.x != 0 || situ.vAccDrag.y != 0) return;
        if (CrewSim.coPlayer != null && CrewSim.system.GetShipOwner(ship.strRegID) == CrewSim.coPlayer.strID) return;
        // Use loaded atmospheric layer bounds, conservatively preserving all
        // waits inside even the outermost layer. No GasContainer is allocated.
        BodyOrbit body = CrewSim.system.GetNearestBO(situ, StarSystem.fEpoch, bIncludePlaceholders: false);
        if (body == null) return;
        double radius = body.fRadius;
        if (body.aAtmospheres != null) foreach (var layer in body.aAtmospheres)
            if (layer != null) radius = System.Math.Max(radius, layer.fMaxAltitude / 149597872.0);
        double dx = situ.vPosx - body.dXReal, dy = situ.vPosy - body.dYReal;
        if (double.IsNaN(dx) || double.IsNaN(dy) || double.IsInfinity(dx) || double.IsInfinity(dy) ||
            double.IsNaN(radius) || double.IsInfinity(radius) || dx * dx + dy * dy <= radius * radius) return;
        situ.bIgnoreGrav = false;
    }
}

[HarmonyPatch(typeof(Hold), nameof(Hold.RunCommand))]
internal static class OrbitalHoldWaitingPatch {
    static void Postfix(Hold __instance, Blackboard blackboard) => OrbitalWaiting.Coast(__instance.ShipUs, blackboard);
}

[HarmonyPatch(typeof(TargetNewIfNone), nameof(TargetNewIfNone.RunCommand))]
internal static class OrbitalSelectionWaitingPatch {
    static void Postfix(TargetNewIfNone __instance, Blackboard blackboard, CommandStatus __result) {
        if (__result.Status == CommandCode.Running && MiscBugFixes.ScavEmergencyRecovery.GetPilot(__instance).AIType == AIType.Scav &&
            MiscBugFixes.ScavEmergencyRecovery.NoGoal(__instance.ShipUs)) OrbitalWaiting.Coast(__instance.ShipUs, blackboard);
    }
}
