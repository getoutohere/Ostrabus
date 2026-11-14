using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.Ships.Commands;

namespace Ostrabus.ShipNavigation;

// Failed flight is not arrival. Failed docking needs another approach instead
// of retrying Dock indefinitely from the same invalid range/route state.
[HarmonyPatch(typeof(LowFuelRetreat), nameof(LowFuelRetreat.RunCommand))]
internal static class LowFuelRetreatProgressPatch {
    static readonly AccessTools.FieldRef<LowFuelRetreat, bool> Docking = AccessTools.FieldRefAccess<LowFuelRetreat, bool>("_docking");
    static readonly AccessTools.FieldRef<LowFuelRetreat, bool> Selected = AccessTools.FieldRefAccess<LowFuelRetreat, bool>("_targetSelected");
    static void Retry(LowFuelRetreat command) {
        Docking(command) = false;
        Ship ship = command.ShipUs;
        if (ship.shipScanTarget == null || ship.shipScanTarget.bDestroyed) Selected(command) = false;
        NavigationRoute.Stop(ship);
    }
    internal static CommandStatus Approach(CommandStatus result, LowFuelRetreat command) {
        if (result.Status != CommandCode.Failure) return result;
        Retry(command);
        return new CommandStatus(CommandCode.Running, command);
    }
    internal static CommandStatus Dock(CommandStatus result, LowFuelRetreat command) {
        if (result.Status == CommandCode.Failure) Retry(command);
        return result;
    }
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var fly = AccessTools.Field(typeof(LowFuelRetreat), "_flyTo");
        var dock = AccessTools.Field(typeof(LowFuelRetreat), "_dock");
        string pending = null;
        int flights = 0, docks = 0;
        foreach (var instruction in instructions) {
            if (instruction.LoadsField(fly)) pending = nameof(Approach);
            if (instruction.LoadsField(dock)) pending = nameof(Dock);
            yield return instruction;
            if (pending == null || !(instruction.operand is System.Reflection.MethodInfo method) || method.Name != nameof(BaseCommand.RunCommand)) continue;
            yield return new CodeInstruction(OpCodes.Ldarg_0);
            yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(LowFuelRetreatProgressPatch), pending));
            if (pending == nameof(Approach)) flights++; else docks++;
            pending = null;
        }
        if (flights != 1 || docks != 1) throw new InvalidOperationException("Native low-fuel retreat approach/dock calls changed: " + flights + "/" + docks);
    }
}
