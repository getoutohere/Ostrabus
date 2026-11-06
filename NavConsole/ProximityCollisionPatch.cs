using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace Ostrabus.NavConsole;

[HarmonyPatch(typeof(CollisionManager), nameof(CollisionManager.CheckCollisions))]
internal static class ProximityCollisionPatch {
    [ThreadStatic] static int rangeKm;
    internal struct State { internal int Previous; internal bool Started; }
    static void Prefix(Ship shipCheck, out State __state) {
        __state = new State { Previous = rangeKm, Started = true };
        rangeKm = ProximityRange.Get(shipCheck);
    }
    static Exception Finalizer(Exception __exception, State __state) {
        if (__state.Started) rangeKm = __state.Previous;
        return __exception;
    }
    internal static float ShipBuffer(float native) => rangeKm <= 5 ? native : Math.Max(native, (float)(rangeKm / ProximityRange.KmPerAU));
    internal static double BodyBuffer() => rangeKm <= 5 ? ProximityRange.NativeBodyAU : rangeKm / ProximityRange.KmPerAU;
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var code = instructions.ToList();
        var starts = new List<int>();
        var bodies = new List<int>();
        for (int i = 0; i + 1 < code.Count; i++) {
            if (code[i].opcode == OpCodes.Ldc_R4 && code[i].operand is float f && f == ProximityRange.NativeShipAU && Local(code[i + 1], true) >= 0) starts.Add(i + 1);
            if (code[i].opcode == OpCodes.Ldc_R8 && code[i].operand is double d && d == ProximityRange.NativeBodyAU) bodies.Add(i);
        }
        var loads = new List<int>();
        if (starts.Count == 1) {
            int local = Local(code[starts[0]], true);
            for (int i = starts[0] + 1; i + 2 < code.Count; i++) {
                if (Local(code[i], false) == local && Local(code[i + 1], false) >= 0 && code[i + 2].opcode == OpCodes.Add) loads.Add(i);
            }
        }
        if (starts.Count != 1 || loads.Count != 2 || bodies.Count != 2 || bodies.Any(i => code[i].blocks.Count != 0)) {
            Ostrabus.OstrabusPlugin.Logger?.LogWarning("[NavConsole] Proximity range IL changed; range override was not installed.");
            return code;
        }
        var output = new List<CodeInstruction>(code.Count + 2);
        for (int i = 0; i < code.Count; i++) {
            if (bodies.Contains(i)) { code[i].opcode = OpCodes.Call; code[i].operand = AccessTools.Method(typeof(ProximityCollisionPatch), nameof(BodyBuffer)); }
            output.Add(code[i]);
            if (loads.Contains(i)) output.Add(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ProximityCollisionPatch), nameof(ShipBuffer))));
        }
        return output;
    }
    static int Local(CodeInstruction code, bool store) {
        var op = code.opcode;
        if (op == (store ? OpCodes.Stloc_0 : OpCodes.Ldloc_0)) return 0;
        if (op == (store ? OpCodes.Stloc_1 : OpCodes.Ldloc_1)) return 1;
        if (op == (store ? OpCodes.Stloc_2 : OpCodes.Ldloc_2)) return 2;
        if (op == (store ? OpCodes.Stloc_3 : OpCodes.Ldloc_3)) return 3;
        if (op != (store ? OpCodes.Stloc : OpCodes.Ldloc) && op != (store ? OpCodes.Stloc_S : OpCodes.Ldloc_S)) return -1;
        if (code.operand is LocalVariableInfo info) return info.LocalIndex;
        return Convert.ToInt32(code.operand);
    }
}
