using HarmonyLib;
using System;
using Ostrabus.Orbital;
using Ostranauts.Core;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;

namespace Ostrabus.ShipNavigation;

internal static class OrbitalWaiting {
    delegate BodyOrbit GravitySelector(StarSystem world, ShipSitu situation, ref UnityEngine.Vector2 gravity, ref BodyOrbit closest);
    static readonly GravitySelector SelectGravity = AccessTools.MethodDelegate<GravitySelector>(
        AccessTools.Method(typeof(StarSystem), "GetGreatestGravBOFast"));

    internal static void Coast(Ship ship, Blackboard blackboard) {
        if (!global::Ostrabus.Orbital.OrbitalSimulation.Enabled || ship == null || ship.bDestroyed || !ship.IsAIShip || ship.HideFromSystem || ship.IsDocked() || ship.IsUsingTorchDrive ||
            ship.shipCombatTarget != null || blackboard != null && blackboard.HasKey("InCombat") && blackboard.Get<bool>("InCombat") ||
            CrewSim.coPlayer?.ship == ship || CrewSim.system == null) return;
        var situ = ship.objSS;
        if (situ == null || !situ.bIgnoreGrav || situ.bIsBO || situ.bBOLocked || situ.bOrbitLocked || situ.bGrounded || situ.NavData != null ||
            situ.vAccIn.x != 0 || situ.vAccIn.y != 0 || situ.vAccRCS.x != 0 || situ.vAccRCS.y != 0 ||
            situ.vAccLift.x != 0 || situ.vAccLift.y != 0 || situ.vAccDrag.x != 0 || situ.vAccDrag.y != 0) return;
        if (CrewSim.coPlayer != null && CrewSim.system.GetShipOwner(ship.strRegID) == CrewSim.coPlayer.strID) return;
        // Use the same bounded hierarchy walk as native free-flight physics,
        // not the nearest station/reference or a new fleet/body-registry scan.
        if (CrewSim.system.boStar == null) return;
        UnityEngine.Vector2 gravity = default;
        BodyOrbit closest = null;
        BodyOrbit body = SelectGravity(CrewSim.system, situ, ref gravity, ref closest);
        double clearance = situ.GetRadiusAU();
        if (!OrbitAcceleration.Finite(clearance) || clearance < 0 ||
            !Clearance(body, clearance, out double minimum) ||
            !NativeEphemeris.State(body, StarSystem.fEpoch, out var center, out var velocity)) return;
        var position = new DVector(situ.vPosx, situ.vPosy);
        var speed = new DVector(situ.vVelX, situ.vVelY);
        if (!SafeBoundCoast(position - center, speed - velocity, (double)(float)2e-44 * body.fMass, minimum)) return;
        // A nearby moon/ground/atmosphere must not be hidden by a fit around
        // its parent. This is admission, not a guarantee against future encounters.
        if (closest != null && !ReferenceEquals(closest, body)) {
            if (!Clearance(closest, clearance, out double nearMinimum) ||
                !NativeEphemeris.Position(closest, StarSystem.fEpoch, out var nearCenter) ||
                (position - nearCenter).Length <= nearMinimum) return;
        }
        situ.bIgnoreGrav = false;
    }

    static bool Clearance(BodyOrbit body, double hullRadius, out double minimum) {
        minimum = 0;
        if (body == null || !OrbitAcceleration.Finite(body.fRadius) || body.fRadius < 0) return false;
        minimum = body.fRadius;
        if (body.aAtmospheres != null) foreach (var layer in body.aAtmospheres) {
            if (layer == null || !OrbitAcceleration.Finite(layer.fMaxAltitude) || layer.fMaxAltitude < 0) return false;
            minimum = Math.Max(minimum, layer.fMaxAltitude / OrbitalMotion.KmPerAU);
        }
        minimum += hullRadius + 1 / OrbitalMotion.KmPerAU;
        return OrbitAcceleration.Finite(minimum);
    }

    // Only certify a bound, nondegenerate two-body coast. No velocity changes,
    // circularization, generated BodyOrbit, registry writes or retained cache.
    internal static bool SafeBoundCoast(DVector position, DVector velocity, double mu, double minimum) {
        double radius = position.Length, speed2 = velocity.Dot(velocity);
        if (!position.Finite || !velocity.Finite || !OrbitAcceleration.Finite(mu) || !(mu > 0) ||
            !OrbitAcceleration.Finite(minimum) || minimum < 0 || !(radius > minimum)) return false;
        double energy = speed2 / 2 - mu / radius;
        double momentum = position.X * velocity.Y - position.Y * velocity.X;
        if (!(energy < 0) || momentum == 0) return false;
        double radial = position.Dot(velocity), scale = speed2 - mu / radius;
        double ex = (scale * position.X - radial * velocity.X) / mu;
        double ey = (scale * position.Y - radial * velocity.Y) / mu;
        double eccentricity = Math.Sqrt(ex * ex + ey * ey);
        double periapsis = momentum * momentum / (mu * (1 + eccentricity));
        return OrbitAcceleration.Finite(periapsis) && eccentricity < 1 && periapsis > minimum;
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
