using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Ostranauts.Ships.Comms;
using Ostranauts.ShipGUIs.NavStation;

namespace Ostrabus.MiscBugFixes;

[HarmonyPatch(typeof(GUIMessageDisplay), nameof(GUIMessageDisplay.ShowPanel))]
internal static class CommsDisplayPatch {
    // Input is already sorted oldest-first by the native method. Keep its tail
    // in that same order; never remove messages from the underlying comms log.
    internal static IEnumerable<ShipMessage> Newest(IEnumerable<ShipMessage> ordered, int count) {
        if (count <= 0) return Array.Empty<ShipMessage>();
        var tail = new Queue<ShipMessage>(count);
        foreach (ShipMessage message in ordered) {
            if (tail.Count == count) tail.Dequeue();
            tail.Enqueue(message);
        }
        return tail;
    }
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var code = instructions.ToList();
        int matches = 0;
        for (int i = 0; i < code.Count; i++) {
            if (!(code[i].operand is MethodInfo method) || method.DeclaringType != typeof(Enumerable) || method.Name != "Take" || !method.IsGenericMethod ||
                method.GetGenericArguments().Length != 1 || method.GetGenericArguments()[0] != typeof(ShipMessage)) continue;
            if (i == 0 || !code[i - 1].LoadsConstant(100)) throw new InvalidOperationException("Native comms display cap changed; review recent-message selection.");
            code[i].opcode = OpCodes.Call;
            code[i].operand = AccessTools.Method(typeof(CommsDisplayPatch), nameof(Newest));
            matches++;
        }
        if (matches != 1) throw new InvalidOperationException("Expected one comms Take(100), found " + matches + ".");
        return code;
    }
}
