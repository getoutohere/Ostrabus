using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace Ostrabus.MiscBugFixes;

// Course playback can settle tank contents before the reactor's independent CO
// clock advances. Keep only its pending paid interval, not a history or fuel credit.
internal static class TorchCourseFuelConsumption {
    sealed class Settlement {
        internal double From, Through;
    }
    static readonly ConditionalWeakTable<FusionIC, Settlement> Settlements = new ConditionalWeakTable<FusionIC, Settlement>();

    internal static void Reconciled(FusionIC reactor, TorchCourseFuel.Profile profile) {
        double from = Math.Max(profile.Intervals[0].Start, reactor.TimeLastRun);
        double through = Math.Min(StarSystem.fEpoch, profile.Intervals[profile.Intervals.Length-1].End);
        if (!TorchCourseFuel.Finite(from) || !TorchCourseFuel.Finite(through) || through <= from) return;
        if (!Settlements.TryGetValue(reactor, out var settlement)) {
            settlement = new Settlement();
            Settlements.Add(reactor, settlement);
        }
        settlement.From = from;
        settlement.Through = through;
    }

    internal static double ChargeSeconds(double elapsed, FusionIC reactor) {
        if (!Settlements.TryGetValue(reactor, out var settlement)) return elapsed;
        // A completed native Run has consumed/skipped this interval. Reuse the
        // weak scalar slot on later settlements instead of allocating each tick.
        // It has no effect on later manual operation or a new trip.
        if (reactor.TimeLastRun >= settlement.Through) return elapsed;
        double end = reactor.COSelf.Clock.Epoch;
        if (!TorchCourseFuel.Finite(end) || !TorchCourseFuel.Finite(elapsed) || elapsed <= 0) return elapsed;
        double overlap = Math.Max(0, Math.Min(end, settlement.Through)-Math.Max(end-elapsed, settlement.From));
        return Math.Max(0, elapsed-overlap);
    }
}

[HarmonyPatch(typeof(FusionIC), "Fusion")]
internal static class TorchCourseFuelConsumptionPatch {
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var code = new List<CodeInstruction>(instructions);
        var charge = AccessTools.Method(typeof(TorchCourseFuelConsumption), nameof(TorchCourseFuelConsumption.ChargeSeconds));
        int changed = 0;
        for (int i = 0; i < code.Count; i++) {
            var instruction = code[i];
            yield return instruction;
            // Native num3 multiplies reactor output by elapsed time and the
            // reaction conversion constant. Change only that fuel integration;
            // leave Fusion's other duration uses (rates and wear) untouched.
            if (instruction.opcode != OpCodes.Ldarg_1 || i+3 >= code.Count ||
                code[i+1].opcode != OpCodes.Mul || code[i+2].opcode != OpCodes.Ldc_R8 ||
                !(code[i+2].operand is double factor) || factor != 393.06358381502895 || code[i+3].opcode != OpCodes.Mul) continue;
            yield return new CodeInstruction(OpCodes.Ldarg_0);
            yield return new CodeInstruction(OpCodes.Call, charge);
            changed++;
        }
        if (changed != 1) throw new InvalidOperationException("Expected one native Fusion fuel-duration multiplication.");
    }
}
