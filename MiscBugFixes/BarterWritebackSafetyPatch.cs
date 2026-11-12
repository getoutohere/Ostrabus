using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Ostranauts.Ships;
using UnityEngine;

namespace Ostrabus.MiscBugFixes;

[HarmonyPatch(typeof(BarterZoneShip), nameof(BarterZoneShip.SaveChangedCOs))]
internal static class BarterWritebackSafetyPatch {
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var code = new List<CodeInstruction>(instructions);
        var getPosition = AccessTools.PropertyGetter(typeof(Transform), nameof(Transform.position));
        var setPosition = AccessTools.PropertySetter(typeof(Transform), nameof(Transform.position));
        var add = AccessTools.Method(typeof(List<JsonItem>), nameof(List<JsonItem>.Add));
        int first = -1, end = -1, addAt = -1, reads = 0, writes = 0;
        for (int i = 0; i < code.Count; i++) {
            if (code[i].Calls(getPosition)) { if (first < 0) first = i; reads++; }
            if (code[i].Calls(setPosition)) { end = i; writes++; }
            if (code[i].Calls(add)) { if (addAt >= 0) throw new InvalidOperationException("Barter writeback has multiple item append sites."); addAt = i; }
        }
        int start = first - 5;
        var value = AccessTools.PropertyGetter(typeof(KeyValuePair<string, CondOwner>), "Value");
        if (reads != 2 || writes != 2 || start < 0 || addAt < first || addAt > end ||
            !code[start + 2].Calls(value) || !code[first - 2].LoadsField(AccessTools.Field(typeof(CondOwner), nameof(CondOwner.tf))) ||
            code[first - 1].opcode != OpCodes.Dup || !LoadsLocal(code[start]) || !LoadsLocal(code[addAt - 2]))
            throw new InvalidOperationException("Native barter writeback IL changed; safe serialization block was not recognized.");

        // Only branches inside this block may target its internal labels. Its
        // containing loop/try/finally and final JSON commit remain native.
        // Harmony's labels use Unity's mscorlib Emit types. Box them while
        // inspecting metadata to keep this netstandard build facade-compatible.
        var labels = AccessTools.Field(typeof(CodeInstruction), "labels");
        var internalLabels = new HashSet<object>();
        for (int i = start + 1; i <= end; i++)
            foreach (var label in (IEnumerable)labels.GetValue(code[i])) internalLabels.Add(label);
        for (int i = 0; i < code.Count; i++) {
            if (i >= start && i <= end) {
                if (code[i].blocks.Count != 0) throw new InvalidOperationException("Unexpected exception boundary inside barter serialization.");
                continue;
            }
            if (internalLabels.Contains(code[i].operand))
                throw new InvalidOperationException("Unexpected branch into barter serialization.");
            if (code[i].operand is Array targets)
                foreach (var label in targets) if (internalLabels.Contains(label)) throw new InvalidOperationException("Unexpected switch into barter serialization.");
        }
        var replacement = new List<CodeInstruction> {
            new CodeInstruction(OpCodes.Ldarg_0),
            new CodeInstruction(code[addAt - 2].opcode, code[addAt - 2].operand),
            new CodeInstruction(code[start].opcode, code[start].operand),
            new CodeInstruction(code[start + 1].opcode, code[start + 1].operand),
            new CodeInstruction(code[start + 2].opcode, code[start + 2].operand),
            new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(BarterWritebackSafety), nameof(BarterWritebackSafety.Append)))
        };
        code[start].MoveLabelsTo(replacement[0]);
        code.RemoveRange(start, end - start + 1);
        code.InsertRange(start, replacement);
        return code;
    }
    static bool LoadsLocal(CodeInstruction instruction) => instruction.opcode == OpCodes.Ldloc || instruction.opcode == OpCodes.Ldloc_S ||
        instruction.opcode == OpCodes.Ldloc_0 || instruction.opcode == OpCodes.Ldloc_1 || instruction.opcode == OpCodes.Ldloc_2 || instruction.opcode == OpCodes.Ldloc_3;
}
