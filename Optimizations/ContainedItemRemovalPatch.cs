using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Ostranauts.Ships;

namespace Ostrabus.Optimizations;

[HarmonyPatch]
internal static class ContainedItemRemovalPatch {
    static IEnumerable<MethodBase> TargetMethods() {
        yield return AccessTools.Method(typeof(Ship), "SpawnItems");
        var iterator = AccessTools.Method(typeof(AsyncPreviewShip), nameof(AsyncPreviewShip.Init)).GetCustomAttribute<IteratorStateMachineAttribute>().StateMachineType;
        yield return AccessTools.Method(iterator, "MoveNext");
    }
    internal static bool Remove(List<JsonItem> items, JsonItem item, int index) {
        // Saved JSON creates separate record objects even for duplicate IDs.
        // Retain native handling if another patch changed the list/index meantime.
        if (index < 0 || index >= items.Count || !ReferenceEquals(items[index], item)) return items.Remove(item);
        items.RemoveAt(index);
        return true;
    }
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator, MethodBase __originalMethod) {
        var code = instructions.ToList();
        var read = AccessTools.PropertyGetter(typeof(List<JsonItem>), "Item");
        var remove = AccessTools.Method(typeof(List<JsonItem>), nameof(List<JsonItem>.Remove));
        var index = generator.DeclareLocal(typeof(int));
        int removed = 0, reads = 0;
        var output = new List<CodeInstruction>();
        foreach (var instruction in code) {
            if (instruction.Calls(read)) {
                var duplicate = new CodeInstruction(OpCodes.Dup);
                instruction.MoveLabelsTo(duplicate); instruction.MoveBlocksTo(duplicate);
                output.Add(duplicate); output.Add(new CodeInstruction(OpCodes.Stloc, index)); reads++;
            }
            if (instruction.Calls(remove)) {
                var load = new CodeInstruction(OpCodes.Ldloc, index);
                instruction.MoveLabelsTo(load); instruction.MoveBlocksTo(load);
                output.Add(load);
                instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(ContainedItemRemovalPatch), nameof(Remove)); removed++;
            }
            output.Add(instruction);
        }
        int expected = __originalMethod.DeclaringType == typeof(Ship) ? 2 : 1;
        if (reads == 0 || removed != expected) throw new InvalidOperationException("Unexpected contained-item loader shape in " + __originalMethod + ": " + reads + " reads, " + removed + " removals.");
        return output;
    }
}
