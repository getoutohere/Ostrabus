using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace Ostrabus.ApartmentRefuel;

[HarmonyPatch(typeof(Item), nameof(Item.CheckFit))]
internal static class PlacementPatch {
    internal static bool Fit(CondTrigger trigger, CondOwner floor, string stats, bool log, Item item, Vector3 center, Ship reference) =>
        trigger.Triggered(floor, stats, log) && (!Runtime.InstalledDefinition(item.CO?.strCODef) || Eligibility.Placement(floor, center, reference));

    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var code = new List<CodeInstruction>(instructions);
        var trigger = AccessTools.Method(typeof(CondTrigger), nameof(CondTrigger.Triggered), new[] { typeof(CondOwner), typeof(string), typeof(bool) });
        int count = 0;
        for (int i = 0; i < code.Count; i++) {
            if (!code[i].Calls(trigger)) continue;
            var call = code[i];
            code.InsertRange(i, new[] { new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(call).MoveBlocksFrom(call),
                new CodeInstruction(OpCodes.Ldarg_1), new CodeInstruction(OpCodes.Ldarg_2) });
            call.opcode = OpCodes.Call;
            call.operand = AccessTools.Method(typeof(PlacementPatch), nameof(Fit));
            i += 3; count++;
        }
        if (count != 1) throw new InvalidOperationException("Native fixture fit checks changed.");
        return code;
    }
}

[HarmonyPatch(typeof(Interaction), nameof(Interaction.Triggered), new[] { typeof(CondOwner), typeof(CondOwner), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(List<string>) })]
internal static class ActionPatch {
    static bool Prefix(Interaction __instance, CondOwner objUs, CondOwner objThem, ref bool __result) {
        if (Eligibility.Action(__instance, objUs, objThem)) return true;
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(GUIStationRefuel), nameof(GUIStationRefuel.Init))]
internal static class VesselListPatch {
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
        Rewriter.Calls(instructions, new Dictionary<MethodInfo, MethodInfo> {
            [AccessTools.Method(typeof(Ship), nameof(Ship.GetDockedShips))] = AccessTools.Method(typeof(RefuelAdapter), nameof(RefuelAdapter.Vessels))
        }, 1);
}

[HarmonyPatch(typeof(GUIStationRefuel), "SetupFields")]
internal static class SetupPatch {
    static bool Prefix(GUIStationRefuel __instance, Ship objShip) => RefuelAdapter.BeginSetup(__instance, objShip);
    static void Postfix(GUIStationRefuel __instance) => RefuelAdapter.EndSetup(__instance);
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => Rewriter.Body(instructions, false);
}

[HarmonyPatch(typeof(GUIStationRefuel), "OnSubmit")]
internal static class SubmitPatch {
    static bool Prefix(GUIStationRefuel __instance) => RefuelAdapter.BeginSubmit(__instance);
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => Rewriter.Body(instructions, true);
}

internal static class Rewriter {
    internal static IEnumerable<CodeInstruction> Calls(IEnumerable<CodeInstruction> instructions, Dictionary<MethodInfo, MethodInfo> replacements, int expected) {
        var code = new List<CodeInstruction>(instructions);
        int count = 0;
        for (int i = 0; i < code.Count; i++) {
            if (!(code[i].operand is MethodInfo method) || !replacements.TryGetValue(method, out var replacement) ||
                !(code[i].opcode == OpCodes.Call || code[i].opcode == OpCodes.Callvirt)) continue;
            var call = code[i];
            code.Insert(i, new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(call).MoveBlocksFrom(call));
            call.opcode = OpCodes.Call;
            call.operand = replacement;
            i++; count++;
        }
        if (count != expected) throw new InvalidOperationException("Native refuel queries changed: expected " + expected + ", found " + count);
        return code;
    }

    internal static IEnumerable<CodeInstruction> Body(IEnumerable<CodeInstruction> instructions, bool submit) {
        var replacements = new Dictionary<MethodInfo, MethodInfo> {
            [AccessTools.Method(typeof(Ship), nameof(Ship.GetICOs1))] = AccessTools.Method(typeof(RefuelAdapter), nameof(RefuelAdapter.Query))
        };
        if (submit) replacements[AccessTools.Method(typeof(Ship), nameof(Ship.GetRCSCans))] = AccessTools.Method(typeof(RefuelAdapter), nameof(RefuelAdapter.RcsCans));
        else {
            replacements[AccessTools.Method(typeof(Ship), nameof(Ship.GetRCSRemain))] = AccessTools.Method(typeof(RefuelAdapter), nameof(RefuelAdapter.RcsRemain));
            replacements[AccessTools.Method(typeof(Ship), nameof(Ship.GetRCSMax))] = AccessTools.Method(typeof(RefuelAdapter), nameof(RefuelAdapter.RcsMax));
            replacements[AccessTools.Method(typeof(Ship), nameof(Ship.GetCOsAtWorldCoords1))] = AccessTools.Method(typeof(RefuelAdapter), nameof(RefuelAdapter.At));
        }
        var code = new List<CodeInstruction>(Calls(instructions, replacements, submit ? 6 : 9));
        var registration = AccessTools.Field(typeof(Ship), nameof(Ship.strRegID));
        int providers = 0;
        for (int i = 0; i + 1 < code.Count; i++) {
            if (code[i].opcode != OpCodes.Ldfld || !Equals(code[i].operand, registration) ||
                code[i + 1].opcode != OpCodes.Ldstr || !Equals(code[i + 1].operand, "GUI_REFUEL_PORT_SUFFIX")) continue;
            var read = code[i];
            code.Insert(i, new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(read).MoveBlocksFrom(read));
            read.opcode = OpCodes.Call;
            read.operand = AccessTools.Method(typeof(RefuelAdapter), nameof(RefuelAdapter.ProviderId));
            i++; providers++;
        }
        if (providers != 1) throw new InvalidOperationException("Native refuel billing identity changed.");
        return code;
    }
}
