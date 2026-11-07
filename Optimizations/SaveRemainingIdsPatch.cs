using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using Ostranauts.Core;

namespace Ostrabus.Optimizations;

// Change only SaveGameData's private remaining-ID lists, not serialization.
[HarmonyPatch(typeof(LoadManager), "SaveGameData")]
internal static class SaveRemainingIdsPatch {
    internal const int RetainedLimit = 32768;
    [ThreadStatic] static Scope current;
    [ThreadStatic] static Scope cached;
    internal sealed class Scope {
        internal readonly Dictionary<string, int> Counts = new Dictionary<string, int>(StringComparer.Ordinal);
        internal List<string> Pending;
        internal Scope Previous;
        internal int Peak;
        internal void Flush() {
            if (Pending == null) return;
            int write = 0;
            for (int read = 0; read < Pending.Count; read++) {
                string id = Pending[read];
                if (id != null && Counts.TryGetValue(id, out int count) && count > 0) { Counts[id] = count - 1; continue; }
                Pending[write++] = id;
            }
            if (write < Pending.Count) Pending.RemoveRange(write, Pending.Count - write);
            Pending = null; Counts.Clear();
        }
    }
    internal static Scope Begin() {
        var scope = cached ?? new Scope(); cached = null;
        scope.Previous = current; current = scope;
        return scope;
    }
    internal static void End(Scope scope) {
        if (scope == null) return;
        current = scope.Previous;
        scope.Counts.Clear(); scope.Pending = null; scope.Previous = null;
        bool keep = scope.Peak <= RetainedLimit; scope.Peak = 0;
        if (keep && cached == null) cached = scope;
    }
    static void Prefix(out Scope __state) => __state = Begin();
    static Exception Finalizer(Exception __exception, Scope __state) { End(__state); return __exception; }
    // The transpiler verifies all four return values are immediately discarded.
    internal static bool Remove(List<string> ids, string id) {
        var scope = current;
        if (scope == null || ids == null) return ids.Remove(id);
        if (!ReferenceEquals(scope.Pending, ids)) { scope.Flush(); scope.Pending = ids; }
        if (id == null) { scope.Flush(); return ids.Remove(id); }
        scope.Counts.TryGetValue(id, out int count);
        scope.Counts[id] = count + 1; scope.Peak = Math.Max(scope.Peak, scope.Counts.Count);
        return true;
    }
    internal static Dictionary<string, List<string>>.ValueCollection Values(Dictionary<string, List<string>> groups) {
        current?.Flush();
        return groups.Values;
    }
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var code = instructions.ToList();
        var remove = AccessTools.Method(typeof(List<string>), nameof(List<string>.Remove));
        var values = AccessTools.PropertyGetter(typeof(Dictionary<string, List<string>>), "Values");
        int removals = 0, enumerations = 0;
        for (int i = 0; i < code.Count; i++) {
            if (code[i].Calls(remove)) {
                if (i + 1 >= code.Count || code[i + 1].opcode != OpCodes.Pop) throw new InvalidOperationException("Save remaining-ID removal result is now observed.");
                code[i].opcode = OpCodes.Call; code[i].operand = AccessTools.Method(typeof(SaveRemainingIdsPatch), nameof(Remove)); removals++;
            } else if (code[i].Calls(values)) {
                code[i].opcode = OpCodes.Call; code[i].operand = AccessTools.Method(typeof(SaveRemainingIdsPatch), nameof(Values)); enumerations++;
            }
        }
        if (removals != 4 || enumerations != 1) throw new InvalidOperationException("Expected four save remaining-ID removals and one final enumeration; found " + removals + "/" + enumerations);
        return code;
    }
}
