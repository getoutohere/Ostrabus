using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Ostranauts.Ships.Comms;

namespace Ostrabus.MiscBugFixes;

// Ship radio reuses on-foot dialogue effects. Keep the authored effects and
// evaluator, but do not let that transport write another conversation's state.
internal static class RadioSurrenderEffects {
    sealed class Prepared {
        internal string Type;
        internal string[] Conditions, Nested;
        internal Loot Value;
    }
    static readonly ConditionalWeakTable<Loot, Prepared> prepared = new();

    internal static Loot Filter(Loot source) {
        if (source == null || source.strType != Loot.TYPE_COND && source.strType != Loot.TYPE_TRIGGER) return source;
        var entry = prepared.GetValue(source, _ => new Prepared());
        if (ReferenceEquals(entry.Conditions, source.aCOs) && ReferenceEquals(entry.Nested, source.aLoots) && entry.Type == source.strType)
            return entry.Value;
        string[] masked = null;
        for (int i = 0; i < source.aCOs.Length; i++) {
            string equation = source.aCOs[i];
            if (string.IsNullOrEmpty(equation)) continue;
            // Do not use ParseCondEquation here: it randomly selects an
            // alternative even when the caller only wants its name.
            string[] alternatives = equation.Split('|');
            bool changed = false;
            for (int j = 0; j < alternatives.Length; j++) {
                string token = alternatives[j];
                string name = token.TrimStart('-');
                int equals = name.IndexOf('=');
                if (equals < 0) continue;
                name = name.Substring(0, equals);
                if (source.strType == Loot.TYPE_COND) {
                    if (name != "InSocialCombat") continue;
                    // Fixed zero produces no condition/remembered effect.
                    int amount = token.IndexOf('x', token.IndexOf('=') + 1);
                    if (amount < 0) continue;
                    alternatives[j] = token.Substring(0, amount + 1) + "0";
                } else {
                    if (DataHandler.dictCTs == null || !DataHandler.dictCTs.TryGetValue(name, out var trigger) ||
                        trigger.strCondName != "InSocialCombat") continue;
                    // Native TNever cannot pass (it requires and forbids the
                    // same condition). Preserve the selected alternative
                    // without applying or remembering an empty condition.
                    alternatives[j] = (token.StartsWith("-", StringComparison.Ordinal) ? "-" : "") +
                        "TNever" + token.Substring(token.IndexOf('='));
                }
                changed = true;
            }
            if (!changed) continue;
            masked ??= (string[])source.aCOs.Clone();
            masked[i] = string.Join("|", alternatives);
        }
        Loot filtered = source;
        if (masked != null) { filtered = source.Clone(); filtered.aCOs = masked; }
        entry.Type = source.strType; entry.Conditions = source.aCOs; entry.Nested = source.aLoots; entry.Value = filtered;
        return filtered;
    }

    internal static void Apply(Interaction interaction, List<string> log, bool cancelling) {
        if (interaction?.strName == null || !interaction.strName.StartsWith("STKShipSurrender_", StringComparison.Ordinal)) {
            interaction.ApplyEffects(log, cancelling);
            return;
        }
        Loot conditionsUs = interaction.LootCondsUs, conditionsThem = interaction.LootCondsThem;
        Loot triggersUs = interaction.LootCTsUs, triggersThem = interaction.LootCTsThem;
        string contextUs = interaction.strLootContextUs, contextThem = interaction.strLootContextThem;
        try {
            interaction.LootCondsUs = Filter(conditionsUs); interaction.LootCondsThem = Filter(conditionsThem);
            interaction.LootCTsUs = Filter(triggersUs); interaction.LootCTsThem = Filter(triggersThem);
            interaction.strLootContextUs = interaction.strLootContextThem = null;
            interaction.ApplyEffects(log, cancelling);
        } finally {
            interaction.LootCondsUs = conditionsUs; interaction.LootCondsThem = conditionsThem;
            interaction.LootCTsUs = triggersUs; interaction.LootCTsThem = triggersThem;
            interaction.strLootContextUs = contextUs; interaction.strLootContextThem = contextThem;
        }
    }
}

[HarmonyPatch(typeof(Comms), "ReceiveMessage")]
internal static class RadioSurrenderEffectsPatch {
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var apply = AccessTools.Method(typeof(Interaction), nameof(Interaction.ApplyEffects), new[] { typeof(List<string>), typeof(bool) });
        int replacements = 0;
        foreach (var instruction in instructions) {
            if (instruction.Calls(apply)) {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(RadioSurrenderEffects), nameof(RadioSurrenderEffects.Apply));
                replacements++;
            }
            yield return instruction;
        }
        if (replacements != 1) throw new InvalidOperationException("Native radio effect call changed: " + replacements);
    }
}
