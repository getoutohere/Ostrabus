using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Ostranauts.ShipGUIs.NavStation;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;

namespace Ostrabus.ShipNavigation;

// Native withdrawal already stops both drives. Revoke its executable course at
// that boundary, without mistaking a replacement or a region move for removal.
internal static class FlightControllerLifetime {
    internal static bool Registered(AIShip controller) {
        if (controller == null || AIShipManager.dictAIs == null) return false;
        foreach (var region in AIShipManager.dictAIs.Values) {
            if (region == null) continue;
            foreach (var current in region)
                if (ReferenceEquals(current, controller)) return true;
        }
        return false;
    }

    internal static bool Withdraw(AIShip controller) {
        // The native private overload removes by hull identity. A detached old
        // request must not stop or remove a controller that has since replaced it.
        if (!Registered(controller)) return false;
        controller.Ship?.objSS?.ResetNavData();
        return true;
    }

    internal static void RunRegistered(AIShip controller) {
        if (Registered(controller)) controller.RunAI();
    }

    internal static void FastForwardRegistered(AIShip controller) {
        if (Registered(controller)) controller.FFWD();
    }
}

[HarmonyPatch(typeof(AIShipManager), "UnregisterShip", new[] { typeof(AIShip) })]
internal static class FlightControllerWithdrawalPatch {
    static void Prepare() {
        // Cancellation and the player publication-order repair are coupled.
        // Refuse cancellation before it can bind if a game update changed the
        // native handoff shape; otherwise partial PatchAll could erase new plans.
        CourseEngagementOrderPatch.Transpiler(ControlInstructionShape.ReadOriginal(
            AccessTools.Method(typeof(NavModCoursePlot), "EngageTrip")));
    }

    static bool Prefix(AIShip __0) => FlightControllerLifetime.Withdraw(__0);
}

// Both native managers copy their dispatch entries. Withdrawal cannot revoke
// those detached references; check identity at the call, not by callsign/hull.
[HarmonyPatch]
internal static class FlightControllerDispatchPatch {
    static IEnumerable<MethodBase> TargetMethods() {
        yield return AccessTools.Method(typeof(AIShipManager), "RunAIQueue");
        yield return AccessTools.Method(typeof(AIShipManager), nameof(AIShipManager.FFWD));
    }

    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod) {
        bool fastForward = __originalMethod == AccessTools.Method(typeof(AIShipManager), nameof(AIShipManager.FFWD));
        if (!fastForward && __originalMethod != AccessTools.Method(typeof(AIShipManager), "RunAIQueue"))
            throw new InvalidOperationException("Unknown native AI dispatch method.");
        var dispatch = AccessTools.Method(typeof(AIShip), fastForward ? nameof(AIShip.FFWD) : nameof(AIShip.RunAI));
        var guarded = AccessTools.Method(typeof(FlightControllerLifetime), fastForward
            ? nameof(FlightControllerLifetime.FastForwardRegistered) : nameof(FlightControllerLifetime.RunRegistered));
        var code = new List<CodeInstruction>(instructions);
        int matches = 0;
        foreach (var instruction in code) {
            if (!instruction.Calls(dispatch)) continue;
            instruction.opcode = OpCodes.Call;
            instruction.operand = guarded;
            matches++;
        }
        if (matches != 1)
            throw new InvalidOperationException("Expected one native AI dispatch call in " + __originalMethod.Name + "; found " + matches);
        return code;
    }
}

internal static class ControlInstructionShape {
    // Harmony's framework Label type differs from the netstandard facade.
    // Inspect label presence only at patch time, without generating new labels.
    static readonly FieldInfo Labels = AccessTools.Field(typeof(CodeInstruction), "labels");
    internal static bool HasLabels(CodeInstruction instruction) => ((ICollection)Labels.GetValue(instruction)).Count != 0;
    internal static bool HasBoundary(CodeInstruction instruction) => HasLabels(instruction) || instruction.blocks.Count != 0;

    internal static List<CodeInstruction> ReadOriginal(MethodBase method) {
        // Select the non-out ILGenerator overload without referring to the
        // framework-specific emitter type through the netstandard facade.
        foreach (var reader in AccessTools.GetDeclaredMethods(typeof(PatchProcessor))) {
            if (reader.Name != "GetOriginalInstructions") continue;
            var parameters = reader.GetParameters();
            if (parameters.Length != 2 || parameters[0].ParameterType != typeof(MethodBase) || parameters[1].ParameterType.IsByRef)
                continue;
            return new List<CodeInstruction>((IEnumerable<CodeInstruction>)reader.Invoke(null, new object[] { method, null }));
        }
        throw new InvalidOperationException("Unable to validate the native course handoff before controller cancellation.");
    }
}

[HarmonyPatch(typeof(NavModCoursePlot), "EngageTrip")]
internal static class CourseEngagementOrderPatch {
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var code = new List<CodeInstruction>(instructions);
        var plan = AccessTools.Field(typeof(ShipSitu), nameof(ShipSitu.NavData));
        var owner = AccessTools.Field(typeof(NavModBase), "COSelf");
        var ship = AccessTools.PropertyGetter(typeof(CondOwner), nameof(CondOwner.ship));
        var situation = AccessTools.PropertyGetter(typeof(Ship), nameof(Ship.objSS));
        var preview = AccessTools.PropertyGetter(typeof(NavModCoursePlot), nameof(NavModCoursePlot.NavPlan));
        var withdraw = AccessTools.Method(typeof(AIShipManager), nameof(AIShipManager.UnregisterShip), new[] { typeof(Ship) });
        var register = AccessTools.Method(typeof(AIShipManager), nameof(AIShipManager.AddAIToShip),
            new[] { typeof(Ship), typeof(AIType), typeof(string), typeof(JsonAIShipSave), typeof(bool) });

        int store = code.FindIndex(instruction => instruction.StoresField(plan));
        int removal = code.FindIndex(instruction => instruction.Calls(withdraw));
        int registration = code.FindIndex(instruction => instruction.Calls(register));
        if (store < 6 || removal != store + 4 || registration <= removal ||
            code.FindLastIndex(instruction => instruction.StoresField(plan)) != store ||
            code.FindLastIndex(instruction => instruction.Calls(withdraw)) != removal ||
            code.FindLastIndex(instruction => instruction.Calls(register)) != registration ||
            code[store - 6].opcode != OpCodes.Ldarg_0 || !code[store - 5].LoadsField(owner) ||
            !code[store - 4].Calls(ship) || !code[store - 3].Calls(situation) ||
            code[store - 2].opcode != OpCodes.Ldarg_0 || !code[store - 1].Calls(preview) ||
            code[store + 1].opcode != OpCodes.Ldarg_0 || !code[store + 2].LoadsField(owner) ||
            !code[store + 3].Calls(ship))
            throw new InvalidOperationException("Unknown EngageTrip course-publication/withdrawal sequence.");

        int first = store - 6;
        for (int i = first; i <= removal; i++) {
            if (code[i].blocks.Count != 0 || i != first && ControlInstructionShape.HasLabels(code[i]))
                throw new InvalidOperationException("Unexpected control flow inside EngageTrip course handoff.");
        }
        var assignment = code.GetRange(first, 7);
        // The null-preview branch lands at the old publication. Preserve that
        // entry at withdrawal so it cannot jump past the native removal step.
        assignment[0].MoveLabelsTo(code[store + 1]);
        code.RemoveRange(first, 7);
        removal -= 7;
        code.InsertRange(removal + 1, assignment);
        return code;
    }
}

[HarmonyPatch(typeof(LowFuelRetreat), nameof(LowFuelRetreat.RunCommand))]
internal static class RetiredRetreatTerminalPatch {
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var code = new List<CodeInstruction>(instructions);
        var withdraw = AccessTools.Method(typeof(AIShipManager), nameof(AIShipManager.UnregisterShip), new[] { typeof(Ship) });
        var failure = AccessTools.PropertyGetter(typeof(BaseCommand), "Failure");
        var finished = AccessTools.PropertyGetter(typeof(BaseCommand), "Success");
        int site = code.FindIndex(instruction => instruction.Calls(withdraw));
        if (site < 0 || code.FindLastIndex(instruction => instruction.Calls(withdraw)) != site ||
            site + 3 >= code.Count || code[site + 1].opcode != OpCodes.Ldarg_0 ||
            !code[site + 2].Calls(failure) || code[site + 3].opcode != OpCodes.Ret)
            throw new InvalidOperationException("Unknown LowFuelRetreat terminal withdrawal return.");
        for (int i = site + 1; i <= site + 3; i++)
            if (ControlInstructionShape.HasBoundary(code[i]))
                throw new InvalidOperationException("Unexpected branch into LowFuelRetreat terminal return.");

        // Failure otherwise means "no retreat needed" to both combat steering
        // consumers. This invocation has withdrawn, so its work is finished.
        code[site + 2].operand = finished;
        return code;
    }
}
