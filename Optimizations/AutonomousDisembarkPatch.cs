using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace Ostrabus.Optimizations;

// Only autonomous needs/leisure selection. Do not restrict shared interaction
// validation, manual orders, item searches or emergency permission overrides.
[HarmonyPatch]
internal static class AutonomousDisembarkPatch {
    internal static MethodBase TargetMethod() => AccessTools.Method(AccessTools.Method(typeof(CondOwner), "GetMove2")
        .GetCustomAttribute<IteratorStateMachineAttribute>().StateMachineType, "MoveNext");
    internal static void Filter(List<CondOwner> targets, CondOwner actor, Interaction interaction) {
        if (targets == null || targets.Count == 0 || actor == null || actor.ship == null || interaction == null ||
            interaction.bManual || interaction.strTargetPoint == null || interaction.strTargetPoint == Interaction.POINT_REMOTE || actor.HasShoreLeave()) return;
        Ship current = actor.ship;
        int keep = 0, count = targets.Count;
        for (int i = 0; i < count; i++) {
            CondOwner target = targets[i];
            // Unknown location is left to vanilla; carried items use the actor's
            // position even if their ship reference has not caught up yet.
            if (target != null && target != actor && target.ship != null && target.ship != current && !CarriedBy(target, actor)) continue;
            if (keep != i) targets[keep] = target;
            keep++;
        }
        if (keep != count) targets.RemoveRange(keep, count - keep);
    }
    static bool CarriedBy(CondOwner target, CondOwner actor) {
        // Walk the ownership chain, never scan the actor's entire inventory for
        // each foreign target. Leave malformed/deep chains to native validation.
        for (int depth = 0; depth < 32; depth++) {
            target = target.objCOParent;
            if (target == null) return false;
            if (target == actor) return true;
        }
        return true;
    }
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod) {
        var code = instructions.ToList();
        Type iterator = __originalMethod.DeclaringType;
        FieldInfo Field(string name, Type type) => iterator.GetFields(AccessTools.all).Single(f => f.Name.StartsWith("<" + name + ">", StringComparison.Ordinal) && f.FieldType == type);
        var targets = Field("aCOs", typeof(List<CondOwner>));
        var actor = AccessTools.Field(iterator, "<>4__this");
        var learned = Field("objInteraction", typeof(Interaction));
        var fallback = Field("objInteractionBest", typeof(Interaction));
        var count = AccessTools.PropertyGetter(typeof(List<CondOwner>), nameof(List<CondOwner>.Count));
        var filter = AccessTools.Method(typeof(AutonomousDisembarkPatch), nameof(Filter));
        int first = -1, random = -1;
        for (int i = 2; i < code.Count - 1; i++) {
            if (!code[i].Calls(count) || !code[i - 1].LoadsField(targets) || code[i - 2].opcode != OpCodes.Ldarg_0) continue;
            if (first < 0) first = i - 2;
            if (code[i + 1].opcode == OpCodes.Brtrue || code[i + 1].opcode == OpCodes.Brtrue_S) {
                if (random >= 0) throw new InvalidOperationException("Ambiguous autonomous fallback candidate list.");
                random = i - 2;
            }
        }
        if (actor == null || first < 0 || first + 4 >= code.Count || random <= first || code[first + 3].opcode != OpCodes.Ldc_I4_1 || code[first + 4].opcode != OpCodes.Sub)
            throw new InvalidOperationException("Unexpected autonomous needs/leisure candidate selection shape.");
        // Preserve branch labels at the common merge points so self, person-spec,
        // inventory and cached/deferred world searches all pass the same filter.
        for (int i = code.Count - 1; i >= 0; i--) {
            if (i != first && i != random) continue;
            var start = new CodeInstruction(OpCodes.Ldarg_0);
            code[i].MoveLabelsTo(start); code[i].MoveBlocksTo(start);
            code.InsertRange(i, new[] { start, new CodeInstruction(OpCodes.Ldfld, targets),
                new CodeInstruction(OpCodes.Ldarg_0), new CodeInstruction(OpCodes.Ldfld, actor),
                new CodeInstruction(OpCodes.Ldarg_0), new CodeInstruction(OpCodes.Ldfld, i == first ? learned : fallback),
                new CodeInstruction(OpCodes.Call, filter) });
        }
        return code;
    }
}
