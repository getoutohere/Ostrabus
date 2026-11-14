using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.Ships.Commands;

namespace Ostrabus.MiscBugFixes;

internal static class AiHoldClock {
    static readonly FieldInfo Pause = AccessTools.Field(typeof(Ship), nameof(Ship.fAIPauseTimer));
    static readonly FieldInfo Epoch = AccessTools.Field(typeof(StarSystem), nameof(StarSystem.fEpoch));
    static readonly AccessTools.FieldRef<CondOwner, List<JsonTicker>> Tickers = AccessTools.FieldRefAccess<CondOwner, List<JsonTicker>>("aTickers");

    internal static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions, bool run) {
        var code = new List<CodeInstruction>(instructions);
        int comparisons = 0, decrements = 0;
        for (int i = 0; i < code.Count; i++) {
            var instruction = code[i];
            // The first comparison is pause > 0. The later RunCommand comparison
            // clamps a negative value; that native normalization remains intact.
            if (comparisons == 0 && instruction.opcode == OpCodes.Ldc_R8 && Equals(instruction.operand, 0d) &&
                i > 0 && code[i - 1].LoadsField(Pause)) {
                instruction.opcode = OpCodes.Ldsfld;
                instruction.operand = Epoch;
                comparisons++;
            }
            if (run && instruction.StoresField(Pause) && i > 0 && code[i - 1].opcode == OpCodes.Sub) {
                // Consume the same ship/value stack without writing a countdown
                // over an absolute deadline. No runtime helper call is needed.
                instruction.opcode = OpCodes.Pop;
                instruction.operand = null;
                yield return instruction;
                yield return new CodeInstruction(OpCodes.Pop);
                decrements++;
            } else yield return instruction;
        }
        if (comparisons != 1 || decrements != (run ? 1 : 0)) throw new InvalidOperationException("Native Hold pause-clock boundary changed.");
    }

    internal static bool HelpRequested(CondTrigger trigger, CondOwner captain, string stats = null, bool logOutcome = true) {
        ExpireHelp(captain);
        return trigger.Triggered(captain, stats, logOutcome);
    }

    internal static void ExpireHelp(CondOwner captain) {
        if (captain == null || captain.ship == null || captain.ship.LoadState > Ship.Loaded.Shallow ||
            captain.mapConds == null || !captain.mapConds.TryGetValue("ShipRequestedHelp", out var condition) || condition == null) return;
        var timers = Tickers(captain);
        if (timers == null) return;
        foreach (var timer in timers) {
            if (timer == null || timer.strCondUpdate != condition.strName || timer.bQueue || timer.bRepeat || !(timer.fPeriod > 0)) continue;
            double end = timer.GetEpochEndS();
            if (double.IsNaN(end) || double.IsInfinity(end) || StarSystem.fEpoch < end) return;
            // Reuse the saved native timer, duration, removal and successor
            // effects. Do not advance the person's Clock or unrelated tickers.
            condition.SetAge((float)((StarSystem.fEpoch - timer.fEpochStart) / 3600));
            condition.Update(0, captain);
            return; // Native removal may mutate the ticker list.
        }
    }
}

[HarmonyPatch(typeof(Hold), nameof(Hold.RunCommand))]
internal static class AiHoldRunClockPatch {
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        int help = 0;
        foreach (var instruction in AiHoldClock.Rewrite(instructions, true)) {
            if (instruction.Calls(AccessTools.Method(typeof(CondTrigger), nameof(CondTrigger.Triggered)))) {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(AiHoldClock), nameof(AiHoldClock.HelpRequested));
                help++;
            }
            yield return instruction;
        }
        if (help != 1) throw new InvalidOperationException("Native Hold help-condition boundary changed.");
    }
}

[HarmonyPatch(typeof(Hold), nameof(Hold.CanExecute))]
internal static class AiHoldEligibilityClockPatch {
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => AiHoldClock.Rewrite(instructions, false);
}
