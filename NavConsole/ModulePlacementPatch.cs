using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using NavDraggable = Ostranauts.ShipGUIs.NavStation.Draggable;

namespace Ostrabus.NavConsole;

[HarmonyPatch(typeof(NavDraggable), "MoveRectTransformUsingAnchors")]
internal static class ModulePlacementPatch
{
    static float PivotX(RectTransform parent) => parent.pivot.x;
    static float PivotY(RectTransform parent) => parent.pivot.y;

    static int Local(CodeInstruction code, bool store)
    {
        if (code.opcode == (store ? OpCodes.Stloc_0 : OpCodes.Ldloc_0)) return 0;
        if (code.opcode == (store ? OpCodes.Stloc_1 : OpCodes.Ldloc_1)) return 1;
        if (code.opcode == (store ? OpCodes.Stloc_2 : OpCodes.Ldloc_2)) return 2;
        if (code.opcode == (store ? OpCodes.Stloc_3 : OpCodes.Ldloc_3)) return 3;
        if (code.opcode != (store ? OpCodes.Stloc : OpCodes.Ldloc)
            && code.opcode != (store ? OpCodes.Stloc_S : OpCodes.Ldloc_S)) return -1;
        return code.operand is LocalVariableInfo local ? local.LocalIndex : Convert.ToInt32(code.operand);
    }

    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        var code = instructions.ToList();
        var parentGetter = AccessTools.PropertyGetter(typeof(Transform), nameof(Transform.parent));
        var locals = __originalMethod.GetMethodBody().LocalVariables;
        var parents = new List<int>();
        for (int i = 0; i + 3 < code.Count; i++)
            if (code[i].Calls(parentGetter) && code[i + 1].opcode == OpCodes.Isinst
                && Equals(code[i + 1].operand, typeof(RectTransform))) parents.Add(i);
        if (parents.Count != 1) throw new InvalidOperationException("Nav placement parent-rectangle binding changed.");
        int parentAt = parents[0], parentIndex = Local(code[parentAt + 2], true);
        var parentLoad = code[parentAt + 3];
        if (parentIndex < 0 || parentIndex >= locals.Count || locals[parentIndex].LocalType != typeof(RectTransform)
            || Local(parentLoad, false) != parentIndex)
            throw new InvalidOperationException("Nav placement parent local changed.");

        var x = AccessTools.Field(typeof(Vector2), nameof(Vector2.x));
        var y = AccessTools.Field(typeof(Vector2), nameof(Vector2.y));
        var axes = new List<bool>();
        var matches = new Dictionary<int, bool>();
        for (int i = 5; i + 1 < code.Count; i++)
        {
            if (code[i].opcode != OpCodes.Ldc_R4 || !Equals(code[i].operand, .5f)) continue;
            if (code[i - 1].opcode != OpCodes.Div || code[i + 1].opcode != OpCodes.Add
                || code[i - 2].opcode != OpCodes.Ldfld || code[i - 4].opcode != OpCodes.Ldfld
                || !Equals(code[i - 2].operand, code[i - 4].operand)
                || Local(code[i - 3], false) < 0 || Local(code[i - 5], false) < 0)
                throw new InvalidOperationException("Nav placement anchor arithmetic changed.");
            bool horizontal = Equals(code[i - 2].operand, x);
            if (!horizontal && !Equals(code[i - 2].operand, y))
                throw new InvalidOperationException("Nav placement anchor axis changed.");
            matches.Add(i, horizontal);
            axes.Add(horizontal);
        }
        if (!axes.SequenceEqual(new[] { true, false, true, false }))
            throw new InvalidOperationException("Expected four native nav anchor offsets in X/Y/X/Y order.");

        // Preserve the native projection, snap rounding, size, overlap tests and
        // persistence. Only replace the assumed center with the actual pivot.
        var output = new List<CodeInstruction>(code.Count + 4);
        for (int i = 0; i < code.Count; i++)
        {
            if (!matches.TryGetValue(i, out bool horizontal)) { output.Add(code[i]); continue; }
            var load = new CodeInstruction(code[i]) { opcode = parentLoad.opcode, operand = parentLoad.operand };
            output.Add(load);
            output.Add(new CodeInstruction(OpCodes.Call,
                AccessTools.Method(typeof(ModulePlacementPatch), horizontal ? nameof(PivotX) : nameof(PivotY))));
        }
        return output;
    }
}
