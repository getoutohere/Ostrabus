using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;

namespace Ostrabus.MiscBugFixes;

// Skipping an over-damaged item before loading its saved children bypasses the
// native destruction/mode-switch recipes that transfer or drop those children.
[HarmonyPatch(typeof(Ship), "SpawnItems")]
internal static class LoadedInventoryParentPatch {
    static readonly System.Reflection.FieldInfo Labels = typeof(CodeInstruction).GetField("labels");
    internal sealed class Scope {
        internal readonly HashSet<string> Parents = new HashSet<string>(StringComparer.Ordinal);
        internal Scope Previous, NextFree;
    }

    [ThreadStatic] static Scope current;
    [ThreadStatic] static Scope free;

    internal static void Prefix(List<JsonItem> aItemsPlusCrew, bool bTemplateOnly, out Scope __state) {
        var scope = free;
        if (scope == null) scope = new Scope();
        else free = scope.NextFree;
        scope.NextFree = null;
        scope.Previous = current;
        current = __state = scope;
        // Template loot generation has different persistence/eligibility rules.
        if (bTemplateOnly || aItemsPlusCrew == null) return;
        foreach (var item in aItemsPlusCrew) {
            if (item == null || item.strID == null || !DataHandler.dictCOSaves.ContainsKey(item.strID)) continue;
            string parent = item.strParentID ?? item.strSlotParentID;
            if (parent != null) scope.Parents.Add(parent);
        }
    }

    internal static bool KeepParent(JsonItem item) => item != null && item.strID != null && current != null && current.Parents.Contains(item.strID);

    internal static void Finalizer(Scope __state) {
        if (__state == null) return;
        current = __state.Previous;
        __state.Previous = null;
        __state.Parents.Clear();
        __state.NextFree = free;
        free = __state;
    }

    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator) {
        var code = instructions.ToList();
        var check = AccessTools.Method(typeof(Ship), "IsItemDestroyed");
        var output = new List<CodeInstruction>();
        int changed = 0;
        foreach (var instruction in code) {
            if (!instruction.Calls(check)) {
                output.Add(instruction);
                continue;
            }
            if (instruction.blocks.Count != 0) throw new InvalidOperationException("Saved inventory damage-check exception boundary changed.");
            var native = generator.DefineLabel();
            var done = generator.DefineLabel();
            // Stack contains (ship, item). Retain the original damage check for
            // leaf items; saved parents must reach normal native reconstruction.
            var duplicate = new CodeInstruction(OpCodes.Dup);
            instruction.MoveLabelsTo(duplicate);
            output.Add(duplicate);
            output.Add(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(LoadedInventoryParentPatch), nameof(KeepParent))));
            output.Add(new CodeInstruction(OpCodes.Brfalse, native));
            output.Add(new CodeInstruction(OpCodes.Pop));
            output.Add(new CodeInstruction(OpCodes.Pop));
            output.Add(new CodeInstruction(OpCodes.Ldc_I4_0));
            output.Add(new CodeInstruction(OpCodes.Br, done));
            ((System.Collections.IList)Labels.GetValue(instruction)).Add(native);
            output.Add(instruction);
            var join = new CodeInstruction(OpCodes.Nop);
            ((System.Collections.IList)Labels.GetValue(join)).Add(done);
            output.Add(join);
            changed++;
        }
        if (changed != 2) throw new InvalidOperationException("Expected two saved-item damage checks; found " + changed + ".");
        return output;
    }
}
