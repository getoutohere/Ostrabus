using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Ostrabus.ShipNavigation;
using Ostranauts.Core;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots.Interfaces;
using Ostranauts.Ships.Commands;

namespace Ostrabus.CeresNavigation;

// Add the faction reserve to native retreat eligibility and destination selection.
// The existing retreat object still owns its flight/docking stages and execution.
[HarmonyPatch(typeof(LowFuelRetreat), nameof(LowFuelRetreat.RunCommand))]
internal static class CeresCombatRetreat {
    static readonly AccessTools.FieldRef<LowFuelRetreat, IAICharacter> Pilot = AccessTools.FieldRefAccess<LowFuelRetreat, IAICharacter>("_ai");
    static readonly AccessTools.FieldRef<LowFuelRetreat, bool> Retreating = AccessTools.FieldRefAccess<LowFuelRetreat, bool>("_retreating");
    static readonly AccessTools.FieldRef<LowFuelRetreat, bool> Selected = AccessTools.FieldRefAccess<LowFuelRetreat, bool>("_targetSelected");
    static readonly AccessTools.FieldRef<LowFuelRetreat, bool> Docking = AccessTools.FieldRefAccess<LowFuelRetreat, bool>("_docking");

    internal static bool NeedsRetreat(bool nativeLowFuel, LowFuelRetreat command) {
        var pilot = Pilot(command);
        return nativeLowFuel || CeresNavigationGuardrails.PolicyApplies(pilot) && CeresNavigationGuardrails.Reserve(pilot).Retreat;
    }
    internal static void Validate(LowFuelRetreat command) {
        var pilot = Pilot(command);
        if (!Retreating(command) || !Selected(command) || !CeresNavigationGuardrails.PolicyApplies(pilot)) return;
        Ship home = CeresNavigationGuardrails.RetreatHome(pilot);
        Ship ship = pilot.ShipUs;
        // Another priority branch, hull replacement or target removal may have
        // changed the destination since this native command last had control.
        if (home != null && ReferenceEquals(ship.shipScanTarget, home) && ReferenceEquals(ship.shipSituTarget, home.objSS)) return;
        Selected(command) = false; Docking(command) = false;
    }
    internal static CommandStatus SelectHome(TargetNearestStation native, Blackboard board, LowFuelRetreat command) {
        var pilot = Pilot(command);
        if (!CeresNavigationGuardrails.PolicyApplies(pilot)) return native.RunCommand(board);
        Ship home = CeresNavigationGuardrails.RetreatHome(pilot);
        var ship = pilot.ShipUs;
        if (home == null) {
            NavigationRoute.Stop(ship); ship.ClearShipTarget();
            return new CommandStatus(CommandCode.Failure, native);
        }
        NavigationRoute.Stop(ship);
        new TargetData(home).ApplyToShip(ship);
        return new CommandStatus(CommandCode.Finished, native);
    }
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var fuel = AccessTools.Method(typeof(AIShipManager), nameof(AIShipManager.LowFuel));
        var selector = AccessTools.Field(typeof(LowFuelRetreat), "_targetNearestStation");
        int fuels = 0, selections = 0;
        bool selecting = false;
        yield return new CodeInstruction(OpCodes.Ldarg_0);
        yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(CeresCombatRetreat), nameof(Validate)));
        foreach (var instruction in instructions) {
            if (instruction.LoadsField(selector)) selecting = true;
            if (selecting && instruction.operand is System.Reflection.MethodInfo method && method.Name == nameof(BaseCommand.RunCommand)) {
                var owner = new CodeInstruction(OpCodes.Ldarg_0);
                instruction.MoveLabelsTo(owner); instruction.MoveBlocksTo(owner);
                yield return owner;
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(CeresCombatRetreat), nameof(SelectHome)));
                selections++;
                selecting = false;
            } else {
                yield return instruction;
                if (!instruction.Calls(fuel)) continue;
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(CeresCombatRetreat), nameof(NeedsRetreat)));
                fuels++;
            }
        }
        if (fuels != 1 || selections != 1) throw new InvalidOperationException("Native low-fuel retreat decision/selection changed: " + fuels + "/" + selections);
    }
}
