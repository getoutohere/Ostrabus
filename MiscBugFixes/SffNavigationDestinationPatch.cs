using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Reflection;
using HarmonyLib;
using Ostranauts.ShipGUIs.Utilities;
using Ostranauts.Ships.Commands;

namespace Ostrabus.MiscBugFixes;

// Native SFF completion needs a temporary placement point, not a new mission.
// Keep that point local so an autonomous pilot retains its actual destination.
[HarmonyPatch]
internal static class SffNavigationDestinationPatch {
    static IEnumerable<MethodBase> TargetMethods() {
        yield return AccessTools.Method(typeof(NavData), nameof(NavData.TimeAdvance));
        yield return AccessTools.Method(typeof(Dock), nameof(Dock.ResolveInstantly));
    }
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator, MethodBase __originalMethod) {
        if (__originalMethod.DeclaringType == typeof(Dock)) {
            int sites = 0;
            var native = AccessTools.Method(typeof(ShipSitu), nameof(ShipSitu.PlaceOrbitPosition));
            foreach (var instruction in instructions) {
                if (!instruction.Calls(native)) { yield return instruction; continue; }
                sites++;
                yield return new CodeInstruction(instruction) { opcode = OpCodes.Call,
                    operand = AccessTools.Method(typeof(SffNavigationDestinationPatch), nameof(PlaceCurrent)) };
            }
            if (sites != 1) throw new InvalidOperationException("Expected one native instant-moor placement; coordinate refresh needs review.");
            yield break;
        }
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
    static void PlaceCurrent(ShipSitu incoming, ShipSitu target) {
        // Match Dock.RunCommand's coordinate refresh at the actual placement,
        // independent of GUI SFF, PASS, or whether any course just completed.
        target.UpdateTime(StarSystem.fEpoch);
        incoming.PlaceOrbitPosition(target);
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
