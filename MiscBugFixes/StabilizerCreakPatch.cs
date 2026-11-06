using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace Ostrabus.MiscBugFixes;

// Change only the maneuver sound threshold, using the native wear multiplier.
[HarmonyPatch(typeof(Ship), nameof(Ship.Gravity), MethodType.Setter)]
internal static class StabilizerCreakPatch {
    static readonly AccessTools.FieldRef<Ship, int> ActiveStabilizers = AccessTools.FieldRefAccess<Ship, int>("nActiveStabilizers");
    internal static double Threshold(Ship ship) {
        int count = ActiveStabilizers(ship);
        return count > 0 ? 0.3d / (0.3f + 1f / Mathf.Pow(2f, count)) : 0.3d;
    }
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var code = instructions.ToList();
        var matches = new List<int>();
        for (int i = 1; i + 1 < code.Count; i++) {
            if (code[i].opcode == OpCodes.Ldc_R8 && code[i].operand is double value && value == 0.3d
                && code[i - 1].opcode == OpCodes.Conv_R8 && code[i + 1].opcode.FlowControl == FlowControl.Cond_Branch
                && code[i].blocks.Count == 0) matches.Add(i);
        }
        if (matches.Count != 1) {
            Ostrabus.OstrabusPlugin.Logger?.LogWarning("[MiscBugFixes] Maneuver creak comparison changed; stabilizer audio correction was not installed.");
            return code;
        }
        int index = matches[0];
        // Preserve labels on the first replacement instruction so branches load this.
        code[index].opcode = OpCodes.Ldarg_0; code[index].operand = null;
        code.Insert(index + 1, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(StabilizerCreakPatch), nameof(Threshold))));
        return code;
    }
}
