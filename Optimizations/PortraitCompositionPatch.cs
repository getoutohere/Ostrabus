using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace Ostrabus.Optimizations;

[HarmonyPatch(typeof(FaceAnim2), nameof(FaceAnim2.GetPNG))]
internal static class PortraitCompositionPatch {
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod) {
        var code = instructions.ToList();
        var getPixel = AccessTools.Method(typeof(Texture2D), nameof(Texture2D.GetPixel), new[] { typeof(int), typeof(int) });
        var setPixel = AccessTools.Method(typeof(Texture2D), nameof(Texture2D.SetPixel), new[] { typeof(int), typeof(int), typeof(Color) });
        var apply = AccessTools.Method(typeof(Texture2D), nameof(Texture2D.Apply), Type.EmptyTypes);
        var filter = AccessTools.PropertySetter(typeof(Texture), nameof(Texture.filterMode));
        int firstPixel = code.FindIndex(i => i.Calls(getPixel));
        int filterIndex = code.FindIndex(i => i.Calls(filter));
        int end = code.FindIndex(i => i.Calls(apply)) - 1;
        int start = filterIndex + 1;
        int arrays = 0, layerLocal = -1;
        for (int i = 0; i < code.Count - 1; i++) {
            if (code[i].opcode != OpCodes.Newarr || !Equals(code[i].operand, typeof(Texture2D))) continue;
            arrays++; layerLocal = Local(code[i + 1], store: true);
        }
        var locals = __originalMethod.GetMethodBody()?.LocalVariables;
        int textureLocal = end >= 0 ? Local(code[end], store: false) : -1;
        bool valid = firstPixel > start && end > firstPixel && filterIndex >= 0 &&
            code.Count(i => i.Calls(filter)) == 1 && code.Count(i => i.Calls(getPixel)) == 2 &&
            code.Count(i => i.Calls(setPixel)) == 1 && code.Count(i => i.Calls(apply)) == 1 && arrays == 1 &&
            locals != null && layerLocal >= 0 && layerLocal < locals.Count && textureLocal >= 0 && textureLocal < locals.Count &&
            locals[layerLocal].LocalType == typeof(Texture2D[]) && locals[textureLocal].LocalType == typeof(Texture2D);
        if (!valid) throw new InvalidOperationException("Expected the native FaceAnim2.GetPNG cold pixel loop; refusing to patch an unknown version.");
        // The shipped Harmony assembly binds Label to Mono's mscorlib; enumerate
        // its label collections without imposing a netstandard generic identity.
        var innerLabels = new HashSet<object>();
        var labelField = typeof(CodeInstruction).GetField("labels");
        for (int i = start + 1; i < end; i++) foreach (object label in (IEnumerable)labelField.GetValue(code[i])) innerLabels.Add(label);
        for (int i = 0; i < code.Count; i++) {
            if (i >= start && i < end) {
                if (code[i].blocks.Count != 0) throw new InvalidOperationException("Unexpected exception boundary in portrait pixel loop.");
                continue;
            }
            if (code[i].operand != null && (innerLabels.Contains(code[i].operand) ||
                code[i].operand is Array labels && labels.Cast<object>().Any(innerLabels.Contains)))
                throw new InvalidOperationException("Unexpected external branch into portrait pixel loop.");
        }
        var loadTexture = new CodeInstruction(code[end].opcode, code[end].operand);
        code[start].MoveLabelsTo(loadTexture);
        var replacement = new[] { loadTexture, new CodeInstruction(OpCodes.Ldloc, layerLocal),
            new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(PortraitComposition), nameof(PortraitComposition.Compose))) };
        code.RemoveRange(start, end - start);
        code.InsertRange(start, replacement);
        return code;
    }
    static int Local(CodeInstruction code, bool store) {
        var op = code.opcode;
        if (op == (store ? OpCodes.Stloc_0 : OpCodes.Ldloc_0)) return 0;
        if (op == (store ? OpCodes.Stloc_1 : OpCodes.Ldloc_1)) return 1;
        if (op == (store ? OpCodes.Stloc_2 : OpCodes.Ldloc_2)) return 2;
        if (op == (store ? OpCodes.Stloc_3 : OpCodes.Ldloc_3)) return 3;
        if (op != (store ? OpCodes.Stloc : OpCodes.Ldloc) && op != (store ? OpCodes.Stloc_S : OpCodes.Ldloc_S)) return -1;
        return code.operand is LocalBuilder local ? local.LocalIndex : Convert.ToInt32(code.operand);
    }
}
