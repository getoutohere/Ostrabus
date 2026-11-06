using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Ostranauts.InputControl;

namespace Ostrabus.MiscBugFixes;

[HarmonyPatch]
internal static class ShipRotationPatch {
    internal static IList Labels(CodeInstruction instruction) => (IList)AccessTools.Field(typeof(CodeInstruction), "labels").GetValue(instruction);
    static IEnumerable<MethodBase> TargetMethods() {
        yield return AccessTools.Method(typeof(CommandRotateCW), nameof(CommandRotateCW.Execute));
        yield return AccessTools.Method(typeof(CommandRotateCCW), nameof(CommandRotateCCW.Execute));
    }
    internal static bool ActiveShipIsDocked() {
        Ship ship = CrewSim.shipCurrentLoaded;
        return ship != null && ship.IsDocked();
    }
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod) {
        var code = instructions.ToList();
        var player = AccessTools.Field(typeof(CrewSim), nameof(CrewSim.coPlayer));
        var ship = AccessTools.PropertyGetter(typeof(CondOwner), nameof(CondOwner.ship));
        var unequal = AccessTools.Method(typeof(UnityEngine.Object), "op_Inequality");
        var docked = AccessTools.Method(typeof(Ship), nameof(Ship.IsDocked), new[] { typeof(bool) });
        var matches = new List<int>();
        for (int i = 0; i + 11 < code.Count; i++) {
            if (!code[i].LoadsField(player) || code[i + 1].opcode != OpCodes.Ldnull || !code[i + 2].Calls(unequal)
                || code[i + 3].opcode != OpCodes.Brfalse || !code[i + 4].LoadsField(player) || !code[i + 5].Calls(ship)
                || code[i + 6].opcode != OpCodes.Brfalse || !code[i + 7].LoadsField(player) || !code[i + 8].Calls(ship)
                || code[i + 9].opcode != OpCodes.Ldc_I4_0 || !code[i + 10].Calls(docked) || code[i + 11].opcode != OpCodes.Brfalse) continue;
            // All three vanilla early exits must lead to the same rotation block.
            var exits = new[] { code[i + 3].operand, code[i + 6].operand, code[i + 11].operand };
            if (!code.Any(instruction => exits.All(label => Labels(instruction).Contains(label)))) continue;
            if (code.Skip(i).Take(11).Any(instruction => instruction.blocks.Count != 0)
                || code.Skip(i + 1).Take(10).Any(instruction => Labels(instruction).Count != 0)) continue;
            matches.Add(i);
        }
        if (matches.Count != 1) {
            Ostrabus.OstrabusPlugin.Logger?.LogWarning("[MiscBugFixes] Rotation docking guard changed in " + __originalMethod.Name + "; correction was not installed.");
            return code;
        }
        int start = matches[0];
        // Retain instruction objects and labels; the existing warning branch consumes our result.
        for (int i = start; i < start + 11; i++) { code[i].opcode = OpCodes.Nop; code[i].operand = null; }
        code[start].opcode = OpCodes.Call;
        code[start].operand = AccessTools.Method(typeof(ShipRotationPatch), nameof(ActiveShipIsDocked));
        return code;
    }
}

