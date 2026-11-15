using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Ostranauts.ShipGUIs.Utilities;

namespace Ostrabus.MiscBugFixes;

// Native SFF completion needs a temporary placement point, not a new mission.
// Keep that point local so an autonomous pilot retains its actual destination.
[HarmonyPatch(typeof(NavData), nameof(NavData.TimeAdvance))]
internal static class SffNavigationDestinationPatch {
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator) {
        var target = AccessTools.Field(typeof(Ship), nameof(Ship.shipSituTarget));
        var point = generator.DeclareLocal(typeof(ShipSitu));
        var prepare = AccessTools.Method(typeof(SffNavigationDestinationPatch), nameof(CompletionPoint));
        int writes = 0, reads = 0;
        foreach (var instruction in instructions) {
            if (instruction.StoresField(target)) {
                if (++writes != 1) throw new InvalidOperationException("Multiple SFF destination replacements in NavData.TimeAdvance.");
                instruction.opcode = OpCodes.Call;
                instruction.operand = prepare;
                yield return instruction;
                yield return new CodeInstruction(OpCodes.Stloc, point);
            } else if (instruction.LoadsField(target)) {
                if (writes != 1) throw new InvalidOperationException("Unexpected destination read before SFF placement.");
                // Discard the receiver; the placement calls operate on the local point.
                instruction.opcode = OpCodes.Pop;
                instruction.operand = null;
                yield return instruction;
                yield return new CodeInstruction(OpCodes.Ldloc, point);
                reads++;
            } else yield return instruction;
        }
        if (writes != 1 || reads != 4) throw new InvalidOperationException("Expected one SFF destination write and four placement reads; found " + writes + "/" + reads + ".");
    }
    static ShipSitu CompletionPoint(Ship ship, ShipSitu point) {
        // Player torch/autopilot completion retains its existing semantics.
        bool autonomous = ship.IsAIShip && !ship.NavPlayerManned && !ReferenceEquals(ship, CrewSim.coPlayer?.ship) &&
            (CrewSim.coPlayer == null || CrewSim.system?.GetShipOwner(ship.strRegID) != CrewSim.coPlayer.strID);
        if (!autonomous) ship.shipSituTarget = point;
        else {
            // Playback normally replaced this field with an end-of-skip point.
            // Keeping the real destination also requires advancing its locked
            // coordinates: Dock.ResolveInstantly places beside this situation
            // without the UpdateTime performed by ordinary Dock.RunCommand.
            // Otherwise mooring saves a skip-sized offset from the real asteroid.
            ship.shipSituTarget?.UpdateTime(StarSystem.fEpoch);
        }
        return point;
    }
}
