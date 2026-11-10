using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Ostranauts.ShipGUIs.NavStation;
using UnityEngine;

namespace Ostrabus.NavConsole;

[HarmonyPatch]
internal static class ScrollbackBindingPatch {
    static IEnumerable<MethodBase> TargetMethods() {
        yield return AccessTools.Method(typeof(GUIOrbitDraw), "Start");
        yield return AccessTools.Method(typeof(GUIDockSys), nameof(GUIDockSys.Init));
    }
    static void Postfix(GUIData __instance) => Scrollbacks.Bind(__instance);
}

[HarmonyPatch(typeof(GUIOrbitDraw), "MouseHandler")]
internal static class NavScrollbackInputPatch {
    static bool Prefix(GUIOrbitDraw __instance) => Scrollbacks.NavInput(__instance);
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var code = instructions.ToList(); int matches = 0;
        var scroll = AccessTools.Method(typeof(GUIOrbitDraw), "ScrollLog");
        foreach (var instruction in code) if (instruction.Calls(scroll)) {
            instruction.opcode = OpCodes.Call;
            instruction.operand = AccessTools.Method(typeof(Scrollbacks), nameof(Scrollbacks.LegacyNavWheel)); matches++;
        }
        if (matches != 4) throw new InvalidOperationException("Expected four native nav-log scroll calls, found " + matches + ".");
        return code;
    }
}

[HarmonyPatch(typeof(GUIMessageDisplay), "Update")]
internal static class CommsScrollbackInputPatch {
    static bool Prefix(GUIMessageDisplay __instance) => Scrollbacks.CommsInput(__instance);
}

[HarmonyPatch]
internal static class CommsScrollbackOpenPatch {
    static IEnumerable<MethodBase> TargetMethods() {
        yield return AccessTools.Method(typeof(GUIMessageDisplay), "PreSetup");
        yield return AccessTools.Method(typeof(GUIMessageDisplay), nameof(GUIMessageDisplay.ShowLog));
    }
    static void Prefix(GUIMessageDisplay __instance) => Scrollbacks.Open(__instance);
}

[HarmonyPatch(typeof(GUIMessageDisplay), "LogConversation")]
internal static class CommsScrollbackFollowPatch {
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var code = instructions.ToList();
        var bottom = AccessTools.Method(typeof(CrewSim), nameof(CrewSim.ScrollBottom));
        var start = AccessTools.Method(typeof(MonoBehaviour), nameof(MonoBehaviour.StartCoroutine), new[] { typeof(System.Collections.IEnumerator) });
        int call = code.FindIndex(i => i.Calls(bottom));
        // ldarg.0; ldsfld CrewSim.objInstance; ldarg.0; ldfld srLog;
        // callvirt ScrollBottom; call StartCoroutine; pop. Change only this block.
        if (call < 4 || call + 2 >= code.Count || code[call - 4].opcode != OpCodes.Ldarg_0 ||
            code[call - 3].opcode != OpCodes.Ldsfld || code[call - 2].opcode != OpCodes.Ldarg_0 ||
            code[call - 1].opcode != OpCodes.Ldfld || !code[call + 1].Calls(start) || code[call + 2].opcode != OpCodes.Pop || code.Count(i => i.Calls(bottom)) != 1)
            throw new InvalidOperationException("Comms scroll-to-bottom scheduling changed; review scoped auto-follow.");
        var labels = AccessTools.Field(typeof(CodeInstruction), "labels");
        for (int i = call - 3; i <= call + 2; i++)
            if (((System.Collections.ICollection)labels.GetValue(code[i])).Count != 0 || code[i].blocks.Count != 0)
                throw new InvalidOperationException("Comms scroll scheduling contains new branch or exception boundaries.");
        var append = new CodeInstruction(OpCodes.Ldarg_0);
        code[call - 4].MoveLabelsTo(append); code[call - 4].MoveBlocksTo(append);
        code.RemoveRange(call - 4, 7);
        code.InsertRange(call - 4, new[] { append, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Scrollbacks), nameof(Scrollbacks.AfterAppend))) });
        var before = new CodeInstruction(OpCodes.Ldarg_0);
        code[0].MoveLabelsTo(before); code[0].MoveBlocksTo(before);
        code.InsertRange(0, new[] { before, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Scrollbacks), nameof(Scrollbacks.BeforeAppend))) });
        return code;
    }
}
