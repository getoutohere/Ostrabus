using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Ostranauts.Electrical;
using UnityEngine;

namespace Ostrabus.MiscBugFixes.SignalBoxes;

[HarmonyPatch]
internal static class SignalBoxGatePatch {
    static IEnumerable<MethodBase> TargetMethods() {
        yield return AccessTools.Method(typeof(Electrical), nameof(Electrical.SetGateMode));
        yield return AccessTools.PropertySetter(typeof(Electrical), nameof(Electrical.GateMode));
    }
    static void Prefix(Electrical __instance, out GateMode __state) { __state = __instance.gateMode; }
    static void Postfix(Electrical __instance, GateMode __state) {
        if (__instance.gateMode != __state && SignalBoxRepair.IsBox(__instance)) SignalBoxRepair.Schedule(__instance);
    }
}

[HarmonyPatch(typeof(Electrical), nameof(Electrical.AuditConnections))]
internal static class SignalBoxAuditPatch {
    static bool Prefix(Electrical __instance, out int __state) => SignalBoxRepair.BeforeAudit(__instance, out __state);
    static void Postfix(Electrical __instance, int __state) => SignalBoxRepair.AfterAudit(__instance, __state);
}

[HarmonyPatch(typeof(Electrical), nameof(Electrical.QueueSignal))]
internal static class SignalBoxQueuePatch {
    static void Prefix(Electrical __instance, string destination, ref SignalType signalType) => SignalBoxRepair.Normalize(__instance, destination, ref signalType);
    static void Postfix(Electrical __instance) => SignalBoxRepair.Queued(__instance);
}

[HarmonyPatch(typeof(Electrical), nameof(Electrical.ResolveSignalQueue))]
internal static class SignalBoxDispatchPatch {
    static void Prefix(Electrical __instance, out SignalBoxRepair.DispatchScope __state) { __state = SignalBoxRepair.BeginDispatch(__instance); }
    static void Finalizer(SignalBoxRepair.DispatchScope __state, Exception __exception) => SignalBoxRepair.EndDispatch(__state, __exception == null);

    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var code = instructions.ToList();
        var send = AccessTools.Method(typeof(Electrical), nameof(Electrical.SendSignal));
        var clear = AccessTools.Method(typeof(List<ElectricalSignal>), nameof(List<ElectricalSignal>.Clear));
        var outgoing = AccessTools.Field(typeof(Electrical), "sendQueue");
        int sends = 0, clears = 0;
        var output = new List<CodeInstruction>();
        for (int i = 0; i < code.Count; i++) {
            var instruction = code[i];
            if (instruction.Calls(send)) {
                if (i + 1 >= code.Count || code[i + 1].opcode != OpCodes.Pop) throw new InvalidOperationException("Electrical dispatch now consumes SendSignal's result; review durable delivery.");
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(SignalBoxRepair), nameof(SignalBoxRepair.Dispatch));
                sends++;
            } else if (instruction.Calls(clear) && i > 0 && code[i - 1].opcode == OpCodes.Ldfld && Equals(code[i - 1].operand, outgoing)) {
                if (instruction.blocks.Count != 0) throw new InvalidOperationException("Electrical send-queue clear exception boundary changed.");
                var owner = new CodeInstruction(OpCodes.Ldarg_0);
                instruction.MoveLabelsTo(owner);
                output.Add(owner);
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(SignalBoxRepair), nameof(SignalBoxRepair.ClearDelivered));
                clears++;
            }
            output.Add(instruction);
        }
        if (sends != 1 || clears != 1) throw new InvalidOperationException("Electrical queue dispatch changed: " + sends + " sends, " + clears + " clears.");
        return output;
    }
}

[HarmonyPatch]
internal static class SignalBoxPersistencePatch {
    static IEnumerable<MethodBase> TargetMethods() {
        yield return AccessTools.Method(typeof(Electrical), nameof(Electrical.SetGPM));
        yield return AccessTools.Method(typeof(Electrical), "InitGPMValues");
    }
    static void Prefix(Electrical __instance, MethodBase __originalMethod) {
        if (__originalMethod.Name == "InitGPMValues") SignalBoxRepair.Initializing(__instance);
    }
    static void Postfix(Electrical __instance, MethodBase __originalMethod) {
        if (__originalMethod.Name == "InitGPMValues") SignalBoxRepair.Initialized(__instance);
        else SignalBoxRepair.Persist(__instance);
    }
}

[HarmonyPatch(typeof(Electrical), nameof(Electrical.RenameConnection))]
internal static class SignalBoxLabelPatch {
    internal static void Report() => Debug.LogWarning("Signal-box labels cannot contain commas or # characters; those delimit saved connections. The previous label was retained.");
    static bool Prefix(Electrical __instance, string newName) {
        if (SignalBoxRepair.ValidLabel(__instance, newName)) return true;
        Report();
        return false;
    }
}

[HarmonyPatch(typeof(GUIOutput), nameof(GUIOutput.ChangeName))]
internal static class SignalBoxLabelInputPatch {
    static bool Prefix(GUIOutput __instance, string name) {
        var owner = __instance.Electrical;
        if (SignalBoxRepair.ValidLabel(owner, name)) return true;
        string previous = owner.outputConnections.TryGetValue(__instance.destinationID, out var connection) ? connection.nickName : "";
        if (string.IsNullOrEmpty(previous)) {
            var target = owner.coShip?.GetCOByID(__instance.destinationID);
            if (target != null) previous = target.strNameFriendly;
        }
        if (__instance.label != null) __instance.label.SetTextWithoutNotify(previous ?? "");
        SignalBoxLabelPatch.Report();
        return false;
    }
}

[HarmonyPatch(typeof(Ship), nameof(Ship.InitShip))]
internal static class SignalBoxShipReadyPatch {
    static void Postfix(Ship __instance) => SignalBoxRepair.Wake(__instance);
}

[HarmonyPatch(typeof(Ship), nameof(Ship.AddCO), new[] { typeof(CondOwner), typeof(bool), typeof(bool) })]
internal static class SignalBoxOwnerPatch {
    static void Postfix(Ship __instance, CondOwner objICO) => SignalBoxRepair.Added(__instance, objICO);
}

[HarmonyPatch(typeof(CondOwner), nameof(CondOwner.ModeSwitch))]
internal static class SignalBoxModeSwitchPatch {
    static void Prefix(CondOwner __instance, CondOwner coNew, out SignalBoxRepair.ModeScope __state) { __state = SignalBoxRepair.BeginModeSwitch(__instance, coNew); }
    static void Finalizer(SignalBoxRepair.ModeScope __state) => SignalBoxRepair.EndModeSwitch(__state);
}

[HarmonyPatch(typeof(Electrical), nameof(Electrical.CleanUp))]
internal static class SignalBoxCleanupPatch {
    static void Prefix(Electrical __instance, ref bool disconnect, out bool __state) { __state = SignalBoxRepair.Cleanup(__instance, ref disconnect); }
    static void Postfix(Electrical __instance, bool __state) => SignalBoxRepair.Cleaned(__instance, __state);
}
