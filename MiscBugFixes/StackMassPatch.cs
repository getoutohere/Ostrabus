using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace Ostrabus.MiscBugFixes;

// Container -> Ship and stack-head -> Ship can both debit the same removal.
// Keep the native debit at each entry point, but share it across nested calls.
[HarmonyPatch]
internal static class StackMassPatch {
    struct Charge { internal CondOwner Owner, Item; internal double Amount; }
    internal struct State { internal bool Started; }
    [ThreadStatic] static int depth;
    [ThreadStatic] static List<Charge> charges;
    static IEnumerable<MethodBase> TargetMethods() {
        yield return AccessTools.Method(typeof(Container), nameof(Container.RemoveCOSimple));
        yield return AccessTools.Method(typeof(CondOwner), nameof(CondOwner.RemoveCO));
        yield return AccessTools.Method(typeof(Ship), nameof(Ship.RemoveCO), new[] { typeof(CondOwner), typeof(bool) });
    }
    [HarmonyPrefix]
    static void Prefix(out State __state) { depth++; __state = new State { Started = true }; }
    [HarmonyFinalizer]
    static void Finalizer(State __state) {
        if (__state.Started && --depth == 0) charges?.Clear();
    }
    static IList Labels(CodeInstruction instruction) => (IList)AccessTools.Field(typeof(CodeInstruction), "labels").GetValue(instruction);
    [HarmonyTranspiler]
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var code = instructions.ToList();
        var add = AccessTools.Method(typeof(CondOwner), nameof(CondOwner.AddMass));
        if (code.Count(i => i.Calls(add)) != 1) throw new InvalidOperationException("Inventory removal mass call changed; refusing an incomplete fix.");
        foreach (var instruction in code) {
            if (instruction.Calls(add)) {
                if (instruction.blocks.Count != 0) throw new InvalidOperationException("Unexpected exception boundary at removal mass call.");
                var item = new CodeInstruction(OpCodes.Ldarg_1);
                foreach (object label in Labels(instruction)) Labels(item).Add(label);
                Labels(instruction).Clear();
                yield return item;
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(StackMassPatch), nameof(Apply));
            }
            yield return instruction;
        }
    }
    static void Apply(CondOwner owner, double amount, bool silent, CondOwner removed) {
        if (depth == 0) { owner.AddMass(amount, silent); return; }
        if (charges == null) charges = new List<Charge>(8);
        for (int i = 0; i < charges.Count; i++) {
            Charge charge = charges[i];
            if (!ReferenceEquals(charge.Owner, owner) || !ReferenceEquals(charge.Item, removed)) continue;
            double difference = amount - charge.Amount;
            if (difference != 0) owner.AddMass(difference, silent);
            charge.Amount = amount; charges[i] = charge;
            return;
        }
        owner.AddMass(amount, silent);
        charges.Add(new Charge { Owner = owner, Item = removed, Amount = amount });
    }
}
