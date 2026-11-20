using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Ostranauts.Ships;
using SolarSystem;
using UnityEngine;

namespace Ostrabus.MiscBugFixes;

internal static class DerelictName {
    // Same stack arguments as the original field write. Native InitShip has
    // already selected the name; do not draw another name or change the ID.
    internal static void Preserve(Ship ship, string replacement) {
        if (!string.IsNullOrEmpty(ship.publicName) && ship.publicName != replacement)
            Debug.LogWarning("[Ostrabus] Preserved derelict name '" + ship.publicName + "' for " +
                ship.strRegID + "; prevented replacement with its registration ID.");
    }
}

[HarmonyPatch(typeof(EnvironmentEntities), "SpawnShipFromStellarObject", new[] { typeof(IStellarObject), typeof(string) })]
internal static class DerelictNamePatch {
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var code = new List<CodeInstruction>(instructions);
        int start = -1, end = -1, store = -1, matches = 0;
        var name = AccessTools.Field(typeof(Ship), nameof(Ship.publicName));
        var id = AccessTools.Field(typeof(Ship), nameof(Ship.strRegID));
        for (int i = 0; i < code.Count; i++) {
            if (code[i].opcode != OpCodes.Isinst) continue;
            if (Equals(code[i].operand, typeof(StellarDerelict))) {
                if (start >= 0) throw new InvalidOperationException("Deferred wreck conversion has multiple wreck branches.");
                start = i;
            }
            if (start >= 0 && Equals(code[i].operand, typeof(StellarShip))) { end = i; break; }
        }
        if (start >= 0 && end > start) {
            for (int i = start; i < end; i++) {
                if (code[i].opcode != OpCodes.Stfld || !Equals(code[i].operand, name)) continue;
                if (i == 0 || code[i - 1].opcode != OpCodes.Ldfld || !Equals(code[i - 1].operand, id))
                    throw new InvalidOperationException("Deferred wreck conversion no longer replaces its name with its registration.");
                store = i; matches++;
            }
        }
        if (matches != 1)
            throw new InvalidOperationException("Deferred wreck conversion changed; expected one registration-as-name write in the wreck branch.");
        // Harmony's emitted finally block can fall through into the following
        // wreck/type tests instead of taking the asteroid's native leave target.
        // Preserve that exit explicitly, without relabelling the non-asteroid
        // branch that already jumps directly to the next type test.
        int exit = -1, finish = -1, leaves = 0;
        object finishLabel = null;
        var labels = typeof(CodeInstruction).GetField("labels");
        for (int i = 0; i < start; i++) {
            if (code[i].opcode == OpCodes.Endfinally) {
                if (exit >= 0) throw new InvalidOperationException("Deferred conversion has multiple asteroid finally exits.");
                exit = i;
            }
            if (code[i].opcode != OpCodes.Leave && code[i].opcode != OpCodes.Leave_S) continue;
            int target = code.FindIndex(instruction => ((IList)labels.GetValue(instruction)).Contains(code[i].operand));
            if (finish >= 0 && target != finish)
                throw new InvalidOperationException("Asteroid conversion has different finally continuation targets.");
            finish = target; finishLabel = code[i].operand; leaves++;
        }
        var toggle = AccessTools.Method(typeof(Ship), nameof(Ship.ToggleVis), new[] { typeof(bool), typeof(bool) });
        if (exit < 0 || exit + 1 >= start || leaves != 2 || finish <= end || finish + 3 >= code.Count ||
            code[finish].opcode != OpCodes.Ldloc_0 || !code[finish + 3].Calls(toggle))
            throw new InvalidOperationException("Deferred conversion changed; could not preserve the native asteroid finally continuation.");
        // Keep labels and exception boundaries on the replaced instruction.
        code[store] = new CodeInstruction(code[store]) {
            opcode = OpCodes.Call, operand = AccessTools.Method(typeof(DerelictName), nameof(DerelictName.Preserve))
        };
        code.Insert(exit + 1, new CodeInstruction(OpCodes.Br, finishLabel));
        return code;
    }
}
