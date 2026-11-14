using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.AIPilots.Interfaces;
using Ostranauts.Ships.Commands;

namespace Ostrabus.ShipNavigation;

internal static class NeutralizedThreatEligibility {
    internal static bool NeedsController(Ship target, Blackboard board) {
        if (!target.IsAIShip || target.LoadState >= Ship.Loaded.Full) return false;
        var combatants = board.Get<List<Ship>>("Combatants");
        return combatants == null || !combatants.Contains(target);
    }
}

// Match IsTargetNeutralized's existing offscreen-AI lifecycle rule at threat
// acquisition. Existing combatants get one native neutralization pass first.
[HarmonyPatch(typeof(AreThreatsNearby), "HasSurrenderedToUs")]
internal static class NeutralizedThreatPatch {
    static readonly AccessTools.FieldRef<AreThreatsNearby, IAICharacter> Pilot =
        AccessTools.FieldRefAccess<AreThreatsNearby, IAICharacter>("_ai");

    internal static bool Exclude(AIShip controller, Ship target, AreThreatsNearby command) {
        if (controller != null) return false;
        // Filtering an ongoing engagement before IsTargetNeutralized can run
        // would strand its combat lock/target. Let native cleanup finish once;
        // the next acquisition cannot reintroduce that hull.
        return NeutralizedThreatEligibility.NeedsController(target, Pilot(command).Blackboard);
    }

    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator) {
        var lookup = AccessTools.Method(typeof(AIShipManager), nameof(AIShipManager.GetAIShipByRegID));
        var exclude = AccessTools.Method(typeof(NeutralizedThreatPatch), nameof(Exclude));
        int sites = 0;
        foreach (var instruction in instructions) {
            yield return instruction;
            if (!instruction.Calls(lookup)) continue;
            sites++;
            // Reuse the native lookup result; do not perform another registry
            // scan per contact. False retains the original value on the stack.
            var resume = generator.DefineLabel();
            yield return new CodeInstruction(OpCodes.Dup);
            yield return new CodeInstruction(OpCodes.Ldarg_1);
            yield return new CodeInstruction(OpCodes.Ldarg_0);
            yield return new CodeInstruction(OpCodes.Call, exclude);
            yield return new CodeInstruction(OpCodes.Brfalse, resume);
            yield return new CodeInstruction(OpCodes.Pop);
            yield return new CodeInstruction(OpCodes.Ldc_I4_1);
            yield return new CodeInstruction(OpCodes.Ret);
            var continuation = new CodeInstruction(OpCodes.Nop);
            ((IList)AccessTools.Field(typeof(CodeInstruction), "labels").GetValue(continuation)).Add(resume);
            yield return continuation;
        }
        if (sites != 1) throw new InvalidOperationException("Native threat controller lookup changed: " + sites);
    }
}

// Blockade has a separate detector with no surrender helper/controller lookup.
// Skip a fresh neutralized hull using the existing foreach continue, before
// faction/warning work; keep all native policy and list publication intact.
[HarmonyPatch(typeof(AreThreatsNearbyBlockade), nameof(AreThreatsNearbyBlockade.RunCommand))]
internal static class BlockadeNeutralizedThreatPatch {
    internal static bool Exclude(Ship target, Blackboard board) =>
        NeutralizedThreatEligibility.NeedsController(target, board) &&
        AIShipManager.GetAIShipByRegID(target.strRegID) == null;

    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var code = instructions.ToList();
        var classification = AccessTools.Field(typeof(Ship), nameof(Ship.Classification));
        var destroyed = AccessTools.Field(typeof(Ship), nameof(Ship.bDestroyed));
        int site = -1;
        for (int i = 6; i + 3 < code.Count; i++) {
            if (!code[i].LoadsField(classification) || !code[i + 1].LoadsConstant((int)Ship.TypeClassification.Projectile) ||
                code[i + 2].opcode != OpCodes.Beq && code[i + 2].opcode != OpCodes.Beq_S) continue;
            if (site != -1) throw LayoutChanged();
            site = i;
        }
        if (site == -1) throw LayoutChanged();
        var load = code[site - 1];
        var skip = code[site + 2].operand;
        bool SameLoad(CodeInstruction other) => other.opcode == load.opcode && Equals(other.operand, load.operand);
        var labelsField = AccessTools.Field(typeof(CodeInstruction), "labels");
        int continuation = code.FindIndex(instruction => ((IList)labelsField.GetValue(instruction)).Contains(skip));
        var continuationLabels = continuation < 0 ? null : (IList)labelsField.GetValue(code[continuation]);
        // Harmony can give branches to the same native instruction distinct
        // labels. Validate their resolved destination rather than label identity.
        bool Branch(CodeInstruction other, OpCode longForm, OpCode shortForm) =>
            (other.opcode == longForm || other.opcode == shortForm) && continuationLabels?.Contains(other.operand) == true;
        bool localLoad = load.opcode == OpCodes.Ldloc || load.opcode == OpCodes.Ldloc_S ||
            load.opcode == OpCodes.Ldloc_0 || load.opcode == OpCodes.Ldloc_1 ||
            load.opcode == OpCodes.Ldloc_2 || load.opcode == OpCodes.Ldloc_3;
        if (!localLoad || !SameLoad(code[site - 4]) || !SameLoad(code[site - 6]) ||
            !code[site - 3].LoadsField(destroyed) || !Branch(code[site - 2], OpCodes.Brtrue, OpCodes.Brtrue_S) ||
            !Branch(code[site - 5], OpCodes.Brfalse, OpCodes.Brfalse_S) ||
            continuation <= site + 3 || continuation + 1 >= code.Count ||
            !(code[continuation + 1].operand is MethodInfo advance) || advance.Name != "MoveNext" ||
            ((IList)labelsField.GetValue(code[site + 3])).Count != 0 || code[site + 3].blocks.Count != 0)
            throw new InvalidOperationException("Native Blockade candidate guard changed: site=" + site +
                "; load=" + load + "; preceding=" + string.Join(" | ", code.Skip(site - 6).Take(9)) +
                "; continuation=" + continuation + "; next=" + (continuation >= 0 && continuation + 1 < code.Count ? code[continuation + 1].ToString() : "missing") +
                "; entry=" + code[site + 3]);
        code.InsertRange(site + 3, new[] {
            new CodeInstruction(load.opcode, load.operand),
            new CodeInstruction(OpCodes.Ldarg_1),
            new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(BlockadeNeutralizedThreatPatch), nameof(Exclude))),
            new CodeInstruction(OpCodes.Brtrue, skip)
        });
        return code;
    }

    static InvalidOperationException LayoutChanged() => new InvalidOperationException(
        "Native Blockade threat candidate/continue layout changed; acquisition patch refused.");
}
