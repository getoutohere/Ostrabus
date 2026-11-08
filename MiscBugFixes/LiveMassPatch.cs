using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Ostranauts.Ships;

namespace Ostrabus.MiscBugFixes;

internal static class LiveMass {
    [ThreadStatic] internal static int Initializing;
    [ThreadStatic] internal static CondOwner Writer;
    [ThreadStatic] internal static CondOwner Refreshing;
    static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    internal static bool Active(CondOwner owner) => Initializing == 0 && owner != null && !owner.bFreezeConds && !owner.bFreezeCondRules &&
        CrewSim.objInstance != null && CrewSim.objInstance.FinishedLoading && !CrewSim.bShipEdit &&
        owner.ship != null && owner.ship.LoadState >= Ship.Loaded.Edit && owner.ship is not BarterZoneShip;
    static bool ValidParents(CondOwner owner) {
        CondOwner slow = owner, fast = owner;
        for (int depth = 0; depth < 128; depth++) {
            slow = slow?.objCOParent;
            fast = fast?.objCOParent?.objCOParent;
            if (slow == null) return true;
            if (ReferenceEquals(slow, fast)) return false;
        }
        return false;
    }
    internal static void Changed(Condition condition, CondOwner owner, double before) {
        if (ReferenceEquals(Writer, owner) || !Active(owner)) return;
        double delta = condition.fCount - before;
        if (delta == 0 || !Finite(before) || !Finite(condition.fCount) || !Finite(delta)) return;
        CondOwner parent = owner.objCOParent;
        if (parent != null && ValidParents(owner)) parent.AddMass(delta, true);
        // Reuse native encumbrance/label calculation, including the dolly patch.
        // Zero-mass refresh must not recursively touch ancestors a second time.
        CondOwner previous = Refreshing;
        Refreshing = owner;
        try { owner.AddMass(0, true); }
        finally { Refreshing = previous; }
        owner.ship?.ResetMass();
    }
    internal static double Write(CondOwner owner, string name, double amount, double age, float track) {
        bool active = Active(owner);
        double before = active ? owner.GetCondAmount("StatMass") : 0;
        CondOwner previous = Writer;
        Writer = owner;
        try { owner.AddCondAmount(name, amount, age, track); }
        finally { Writer = previous; }
        if (!active) return amount; // Preserve native load/remote-copy accounting.
        double delta = owner.GetCondAmount("StatMass") - before;
        if (!Finite(before) || !Finite(delta)) return amount;
        if (delta != 0) owner.ship?.ResetMass();
        return delta;
    }
    internal static void Propagate(CondOwner parent, double amount, bool silent, CondOwner source) {
        if (!ReferenceEquals(Refreshing, source)) parent.AddMass(amount, silent);
    }
}

[HarmonyPatch(typeof(CondOwner), nameof(CondOwner.SetData))]
internal static class LiveMassInitializationPatch {
    static void Prefix(out bool __state) { LiveMass.Initializing++; __state = true; }
    static void Finalizer(bool __state) { if (__state) LiveMass.Initializing--; }
}

[HarmonyPatch(typeof(Condition), nameof(Condition.AddAmount))]
internal static class LiveMassConditionPatch {
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator) {
        var code = instructions.ToList();
        var count = AccessTools.Field(typeof(Condition), nameof(Condition.fCount));
        var records = AccessTools.Method(typeof(CondOwner), nameof(CondOwner.UpdateCondRecords));
        int store = code.FindIndex(i => i.LoadsField(count)) + 1;
        if (store <= 0 || !code[store].opcode.Name.StartsWith("stloc", StringComparison.Ordinal) || code.Count(i => i.Calls(records)) != 1)
            throw new InvalidOperationException("Condition mass update shape changed.");
        var old = code[store];
        CodeInstruction LoadBefore() {
            if (old.opcode == OpCodes.Stloc_0) return new CodeInstruction(OpCodes.Ldloc_0);
            if (old.opcode == OpCodes.Stloc_1) return new CodeInstruction(OpCodes.Ldloc_1);
            if (old.opcode == OpCodes.Stloc_2) return new CodeInstruction(OpCodes.Ldloc_2);
            if (old.opcode == OpCodes.Stloc_3) return new CodeInstruction(OpCodes.Ldloc_3);
            return new CodeInstruction(OpCodes.Ldloc, old.operand);
        }
        var after = generator.DefineLabel();
        int at = code.FindIndex(i => i.Calls(records)) + 1;
        ((IList)AccessTools.Field(typeof(CodeInstruction), "labels").GetValue(code[at])).Add(after);
        code.InsertRange(at, new[] {
            new CodeInstruction(OpCodes.Ldarg_0), new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(Condition), nameof(Condition.strName))),
            new CodeInstruction(OpCodes.Ldstr, "StatMass"), new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(string), "op_Equality")),
            new CodeInstruction(OpCodes.Brfalse, after), new CodeInstruction(OpCodes.Ldarg_0), new CodeInstruction(OpCodes.Ldarg_1), LoadBefore(),
            new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(LiveMass), nameof(LiveMass.Changed))) });
        return code;
    }
}

[HarmonyPatch(typeof(CondOwner), nameof(CondOwner.AddMass))]
internal static class LiveMassAddPatch {
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var code = instructions.ToList();
        var write = AccessTools.Method(typeof(CondOwner), nameof(CondOwner.AddCondAmount));
        var add = AccessTools.Method(typeof(CondOwner), nameof(CondOwner.AddMass));
        if (code.Count(i => i.Calls(write)) != 2 || code.Count(i => i.Calls(add)) != 1)
            throw new InvalidOperationException("Native mass bookkeeping shape changed.");
        bool first = true;
        var output = new List<CodeInstruction>();
        foreach (var instruction in code) {
            if (first && instruction.Calls(write)) {
                first = false;
                instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(LiveMass), nameof(LiveMass.Write));
                output.Add(instruction); output.Add(new CodeInstruction(OpCodes.Starg_S, (byte)1));
                continue;
            }
            if (instruction.Calls(add)) {
                var source = new CodeInstruction(OpCodes.Ldarg_0);
                instruction.MoveLabelsTo(source); instruction.MoveBlocksTo(source); output.Add(source);
                instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(LiveMass), nameof(LiveMass.Propagate));
            }
            output.Add(instruction);
        }
        return output;
    }
}
