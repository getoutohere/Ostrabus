using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;

namespace Ostrabus.AlternativeWork;

// Only the native batch-feed method's two call sites are redirected. All of its
// room/range, material, exclusion, capacity and batching rules still execute.
[HarmonyPatch(typeof(WorkManager), nameof(WorkManager.CollectBatchFeedTasks))]
internal static class WorkBatchInventoryPatch {
    [ThreadStatic] static Scope current;
    internal static long CarriedBatches, ExpandedBatches;
    internal sealed class Scope {
        internal readonly Scope Previous;
        internal readonly HashSet<CondOwner> Excluded;
        internal Ship Ship;
        internal CondTrigger Filter;
        internal bool SubObjects, Docked, Locked, Expanded;
        internal Scope(Scope previous, List<CondOwner> excluded) {
            Previous = previous;
            Excluded = excluded == null ? new HashSet<CondOwner>() : new HashSet<CondOwner>(excluded);
        }
    }
    [HarmonyPrefix]
    static void Prefix(CondOwner coHauler, List<CondOwner> aExcludeItems, out Scope __state) {
        __state = current;
        current = AlternativeWorkSystem.Enabled && coHauler != null && AlternativeWorkSystem.Eligible(coHauler) ? new Scope(current, aExcludeItems) : null;
    }
    [HarmonyFinalizer]
    static void Finalizer(Scope __state) {
        Scope finished = current;
        current = __state;
        if (finished == null || ReferenceEquals(finished, __state)) return;
        if (finished.Ship != null && !finished.Expanded) CarriedBatches++;
        finished.Excluded.Clear(); finished.Ship = null; finished.Filter = null;
    }
    [HarmonyTranspiler]
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var code = instructions.ToList();
        var source = AccessTools.Method(typeof(Ship), nameof(Ship.GetCOs), new[] { typeof(CondTrigger), typeof(bool), typeof(bool), typeof(bool) });
        var nearest = AccessTools.Method(typeof(CondOwner), nameof(CondOwner.GetNearestTriggeredSync));
        if (code.Count(i => i.Calls(source)) != 1 || code.Count(i => i.Calls(nearest)) != 1)
            throw new InvalidOperationException("Native batch-feed inventory call sites changed; refusing an incomplete rewrite.");
        foreach (var instruction in code) {
            if (instruction.Calls(source)) { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(WorkBatchInventoryPatch), nameof(Materials)); }
            else if (instruction.Calls(nearest)) { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(WorkBatchInventoryPatch), nameof(Nearest)); }
        }
        return code;
    }
    static List<CondOwner> Materials(Ship ship, CondTrigger filter, bool subObjects, bool docked, bool locked) {
        Scope scope = current;
        if (scope == null) return ship.GetCOs(filter, subObjects, docked, locked);
        scope.Ship = ship; scope.Filter = filter; scope.SubObjects = subObjects; scope.Docked = docked; scope.Locked = locked;
        // The following native AddRange(GetCOsSafe) supplies accessible inventory.
        return new List<CondOwner>();
    }
    static CondOwner Nearest(CondOwner crew, CondTrigger trigger, List<CondOwner> available, string persistent, string useCase, bool manual, Interaction interaction, out List<string> failures) {
        CondOwner result = crew.GetNearestTriggeredSync(trigger, available, persistent, useCase, manual, interaction, out failures);
        Scope scope = current;
        if (scope == null) return result;
        if (result == null && !scope.Expanded && scope.Ship != null) {
            scope.Expanded = true; ExpandedBatches++;
            foreach (var item in available) scope.Excluded.Add(item);
            // Expand the SAME native candidate list once, preserving exclusions and
            // items already assigned to earlier deliveries. No loot is rolled twice.
            foreach (var item in scope.Ship.GetCOs(scope.Filter, scope.SubObjects, scope.Docked, scope.Locked))
                if (scope.Excluded.Add(item)) available.Add(item);
            result = crew.GetNearestTriggeredSync(trigger, available, persistent, useCase, manual, interaction, out failures);
        }
        if (result != null) scope.Excluded.Add(result);
        return result;
    }
}
