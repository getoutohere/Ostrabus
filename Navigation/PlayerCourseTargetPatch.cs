using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Ostranauts.ShipGUIs.NavStation;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;

namespace Ostrabus.ShipNavigation;

// Native course plotting records the destination in targetRegID. The sensor
// selection is independent and must not become a new destination during playback.
internal static class PlayerCourseTarget {
    static readonly AccessTools.FieldRef<FlyToPath, Ostranauts.Ships.AIPilots.Interfaces.IAICharacter> Pilot =
        AccessTools.FieldRefAccess<FlyToPath, Ostranauts.Ships.AIPilots.Interfaces.IAICharacter>("_ai");

    internal static bool Active(Ship ship) => ship != null && !ship.IsAIShip && ship.objSS?.NavData != null;

    internal static Ship DestinationShip(Ship ship) => string.IsNullOrEmpty(ship.targetRegID)
        ? null : CrewSim.system?.GetShipByRegID(ship.targetRegID);

    internal static void Prepare(Ship ship) {
        if (!Active(ship)) return;
        Ship destination = DestinationShip(ship);
        ShipSitu situation = destination?.objSS ?? CrewSim.system?.GetStellarObjectByID(ship.targetRegID)?.objSS;
        if (situation != null) {
            // A live destination, including an orbital/ground station, retains
            // its native motion rather than the inspected contact's situation.
            var current = ship.shipSituTarget;
            bool ownOffset = current != null && !ReferenceEquals(current, ship.shipScanTarget?.objSS) &&
                current.bIsBO == situation.bIsBO && current.bBOLocked == situation.bBOLocked &&
                current.strBOPORShip == situation.strBOPORShip &&
                (current.vBOOffsetx != situation.vBOOffsetx || current.vBOOffsety != situation.vBOOffsety);
            if (!ownOffset) ship.shipSituTarget = situation;
        } else if (ship.shipSituTarget == null ||
            ReferenceEquals(ship.shipSituTarget, ship.shipScanTarget?.objSS)) {
            // Coordinate courses have no hull identity. Copy their endpoint;
            // native UpdateTime must never mutate the plan's own waypoint.
            var endpoint = ship.objSS.NavData.Destination?.ObjSS;
            if (endpoint != null) ship.shipSituTarget = new ShipSitu(endpoint, false);
        }
    }

    internal static Ship AutopilotSelection(Ship ship) {
        if (!Active(ship)) return ship.shipScanTarget;
        Prepare(ship);
        // Only the command's read is changed. The UI keeps its actual selection.
        return null;
    }

    internal static Ship PathSelection(Ship ship, FlyToPath planner) =>
        Active(ship) && Pilot(planner) is AutoPilot ? DestinationShip(ship) : ship.shipScanTarget;
}

[HarmonyPatch]
internal static class PlayerCourseTargetPatch {
    static IEnumerable<MethodBase> TargetMethods() {
        yield return AccessTools.Method(typeof(FlyToAutoPilot), nameof(FlyToAutoPilot.RunCommand));
        yield return AccessTools.Method(typeof(FlyToPath), nameof(FlyToPath.RunCommand));
    }

    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original) {
        bool autopilot = original.DeclaringType == typeof(FlyToAutoPilot);
        var field = AccessTools.Field(typeof(Ship), nameof(Ship.shipScanTarget));
        var read = AccessTools.Method(typeof(PlayerCourseTarget), autopilot ? nameof(PlayerCourseTarget.AutopilotSelection) : nameof(PlayerCourseTarget.PathSelection));
        int sites = 0;
        foreach (var instruction in instructions) {
            if (instruction.opcode == OpCodes.Ldfld && Equals(instruction.operand, field)) {
                if (!autopilot) yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
                instruction.opcode = OpCodes.Call;
                instruction.operand = read;
                sites++;
            }
            yield return instruction;
        }
        int expected = autopilot ? 3 : 1;
        if (sites != expected) throw new InvalidOperationException("Native course sensor-selection reads changed: " + original + "; found " + sites);
    }
}

// Native PlanTrip forgets to clear an earlier hull ID for an empty-space plot.
// Keep the existing destination field truthful; no additional saved state.
[HarmonyPatch(typeof(NavModCoursePlot), "PlanTrip")]
internal static class PlayerCourseCoordinateTargetPatch {
    static readonly AccessTools.FieldRef<NavModBase, CondOwner> Console = AccessTools.FieldRefAccess<NavModBase, CondOwner>("COSelf");
    static void Postfix(NavModCoursePlot __instance, Ostranauts.ShipGUIs.Utilities.NavData __result) {
        var ship = Console(__instance)?.ship;
        if (__result == null || ship == null || ship.IsAIShip) return;
        var target = GUIOrbitDraw.CrossHairTarget;
        ship.targetRegID = target?.Ship?.strRegID ?? target?.stellarObj?.strID ?? target?.bodyOrbit?.strName;
    }
}
