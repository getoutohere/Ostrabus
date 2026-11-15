using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Ostranauts.ShipGUIs.Utilities;

namespace Ostrabus.ShipNavigation;

internal static class RouteCompletion {
    static readonly AccessTools.FieldRef<NavData, Ship> Owner = AccessTools.FieldRefAccess<NavData, Ship>("_shipUs");
    struct Playback {
        internal NavData Course;
        internal ShipSitu Situation;
        internal bool Completed;
    }
    [ThreadStatic] static Playback current;

    // Called only at the native successful-completion return. Failed/aborted
    // playback retains its own false return and normal physics fallback.
    internal static bool Completed(bool handled, NavData course, ShipSitu situation) {
        if (handled) return true; // Native GUI SFF already owns its placement/coast.
        var ship = Owner(course);
        if (ReferenceEquals(ship.objSS, situation)) {
            ship.StopManeuver(false, false);
            if (course.IsTorching) {
                ship.SetReactorGPMValue("slidFlow", "0.0");
                ship.SetReactorGPMValue("slidCycle", "0.0");
                ship.SetThrust(0);
            }
        }
        // Internal forces belong to the completed trajectory. Ambient gravity,
        // lift and drag still belong to the caller's physical integration.
        situation.ResetAccelerations();
        if (ReferenceEquals(current.Course, course) && ReferenceEquals(current.Situation, situation)) current.Completed = true;
        return true;
    }

    // Replaces only the existing playback call in ShipSitu.TimeAdvance. No
    // registry, heap scope, recursion or per-frame population scan. The local
    // scope distinguishes native completion from another handler's true return.
    internal static bool Advance(NavData course, ShipSitu situation, ref double seconds) {
        var previous = current;
        current = new Playback { Course = course, Situation = situation };
        bool played, completed;
        try {
            played = course.TimeAdvance(situation);
            completed = current.Completed;
        } finally { current = previous; }
        if (!played || !completed || situation.NavData != null || GUIFFWD.Active) return played;
        var end = course.Destination;
        var point = end.ObjSS;
        // Native UpdateTime has already refreshed body-bound endpoints to now.
        if ((point.bIsBO || point.bBOLocked || point.bOrbitLocked) && point.strBOPORShip != null &&
            point.boReference != null && point.boReference.strName == point.strBOPORShip) return true;
        seconds = Math.Max(0, StarSystem.fEpoch - end.ArrivalTime);
        return seconds == 0; // Otherwise native physics coasts only this remainder.
    }
}

[HarmonyPatch]
internal static class RouteCompletionPatch {
    static IEnumerable<MethodBase> TargetMethods() {
        yield return AccessTools.Method(typeof(NavData), nameof(NavData.TimeAdvance), new[] { typeof(ShipSitu) });
        yield return AccessTools.Method(typeof(ShipSitu), nameof(ShipSitu.TimeAdvance), new[] { typeof(double), typeof(bool) });
    }
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod) {
        var code = new List<CodeInstruction>(instructions);
        if (__originalMethod.DeclaringType == typeof(ShipSitu)) {
            var native = AccessTools.Method(typeof(NavData), nameof(NavData.TimeAdvance));
            int count = 0;
            for (int i = 0; i < code.Count; i++) if (code[i].Calls(native)) {
                if (i == 0 || code[i - 1].opcode != OpCodes.Ldarg_0)
                    throw new InvalidOperationException("ShipSitu playback no longer advances its own situation.");
                var time = new CodeInstruction(OpCodes.Ldarga_S, (byte)1);
                code[i].MoveLabelsTo(time); code[i].MoveBlocksTo(time);
                code.Insert(i++, time);
                code[i].opcode = OpCodes.Call;
                code[i].operand = AccessTools.Method(typeof(RouteCompletion), nameof(RouteCompletion.Advance));
                count++;
            }
            if (count != 1) throw new InvalidOperationException("ShipSitu playback handoff changed; expected one native call.");
        } else {
            var reset = AccessTools.Method(typeof(ShipSitu), nameof(ShipSitu.ResetNavData));
            int count = 0;
            for (int i = 0; i + 2 < code.Count; i++) {
                if (!code[i].Calls(reset) || !IsLocalLoad(code[i + 1]) || code[i + 2].opcode != OpCodes.Ret) continue;
                if (i == 0 || code[i - 1].opcode != OpCodes.Ldarg_1)
                    throw new InvalidOperationException("NavData completion no longer clears its supplied situation.");
                // Preserve the native completion flag already on the stack.
                var self = new CodeInstruction(OpCodes.Ldarg_0);
                code[i + 2].MoveLabelsTo(self); code[i + 2].MoveBlocksTo(self);
                code.InsertRange(i + 2, new[] { self, new CodeInstruction(OpCodes.Ldarg_1),
                    new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(RouteCompletion), nameof(RouteCompletion.Completed))) });
                count++;
                i += 5;
            }
            if (count != 1) throw new InvalidOperationException("NavData successful completion return changed; expected one flag return after reset.");
        }
        return code;
    }
    static bool IsLocalLoad(CodeInstruction c) => c.opcode == OpCodes.Ldloc || c.opcode == OpCodes.Ldloc_S ||
        c.opcode == OpCodes.Ldloc_0 || c.opcode == OpCodes.Ldloc_1 || c.opcode == OpCodes.Ldloc_2 || c.opcode == OpCodes.Ldloc_3;
}
