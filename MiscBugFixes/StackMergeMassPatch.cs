using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;

namespace Ostrabus.MiscBugFixes;

[HarmonyPatch(typeof(Container), "StackOnInsideItem")]
internal static class StackMergeMassPatch {
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var code = instructions.ToList();
        var add = AccessTools.Method(typeof(CondOwner), nameof(CondOwner.AddMass));
        if (code.Count(i => i.Calls(add)) != 1) throw new InvalidOperationException("Stack merge alias cleanup changed; refusing mass patch.");
        foreach (var instruction in code) {
            if (!instruction.Calls(add)) { yield return instruction; continue; }
            // The physical items remain in the merged stack. Removing their old
            // container-list alias must not debit the owner a second time.
            var pop = new CodeInstruction(OpCodes.Pop);
            instruction.MoveLabelsTo(pop); instruction.MoveBlocksTo(pop);
            yield return pop; yield return new CodeInstruction(OpCodes.Pop); yield return new CodeInstruction(OpCodes.Pop);
        }
    }
}
