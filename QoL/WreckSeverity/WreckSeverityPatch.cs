using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Ostranauts.Ships;

namespace Ostrabus.QoL.WreckSeverity;

[HarmonyPatch(typeof(Ship), "BreakIn")]
internal static class WreckSeverityPatch {
    private static bool Prepare() {
        var method = AccessTools.DeclaredMethod(typeof(Ship), "BreakIn", Type.EmptyTypes);
        var prefill = AccessTools.DeclaredField(typeof(Ship), "bPrefill");
        var severity = AccessTools.DeclaredField(typeof(Ship), nameof(Ship.fBreakInMultiplier));
        bool supported = method != null && !method.IsStatic && method.ReturnType == typeof(void) &&
            prefill != null && !prefill.IsStatic && prefill.FieldType == typeof(bool) &&
            severity != null && !severity.IsStatic && severity.FieldType == typeof(float);
        if (!supported)
            OstrabusPlugin.Logger?.LogError("Wreck severity adjustment was not installed: native damage initialization no longer matches the reviewed method/field contract.");
        if (WreckDamageLog.Enabled && !WreckDamageLog.NativeRayLayoutSupported()) {
            WreckDamageLog.Enabled = false;
            OstrabusPlugin.Logger?.LogWarning("Wreck damage logging disabled: native ray damage roll order changed. Severity adjustment is unaffected.");
        }
        return supported;
    }

    // The native caller clears bPrefill after initial damage generation and saves
    // it. No separate processed-ship registry or save marker is required.
    internal static void Prefix(Ship __instance, bool ___bPrefill, out WreckDamageLog.Capture __state) {
        float original = __instance?.fBreakInMultiplier ?? 0;
        WreckSeverity.ApplyInitial(__instance, ___bPrefill);
        bool damage = WreckSeverity.Eligible(__instance, ___bPrefill);
        __state = WreckDamageLog.Enabled && (damage || WreckBonusLog.Pending(CrewSim.coPlayer))
            ? WreckDamageLog.Begin(__instance, original, damage) : null;
    }

    // Always release the short-lived diagnostic scope, including native failures;
    // the original exception and damage results are left untouched.
    internal static void Finalizer(Ship __instance, WreckDamageLog.Capture __state, Exception __exception) =>
        WreckDamageLog.End(__instance, __state, __exception);

    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        if (!WreckDamageLog.Enabled) return instructions;
        var replacements = new Dictionary<MethodInfo, MethodInfo> {
            [AccessTools.Method(typeof(TileUtils), nameof(TileUtils.GetZoneFromTileRadius))] = AccessTools.Method(typeof(WreckDamageLog), nameof(WreckDamageLog.Hole)),
            [AccessTools.Method(typeof(Ship), nameof(Ship.GetCOsInZone), new[] { typeof(JsonZone), typeof(CondTrigger), typeof(bool), typeof(bool) })] = AccessTools.Method(typeof(WreckDamageLog), nameof(WreckDamageLog.HoleSelection)),
            [AccessTools.Method(typeof(Ship), nameof(Ship.GetCOs), new[] { typeof(CondTrigger), typeof(bool), typeof(bool), typeof(bool) })] = AccessTools.Method(typeof(WreckDamageLog), nameof(WreckDamageLog.Selection)),
            [AccessTools.Method(typeof(Ship), nameof(Ship.DamageAllCOs))] = AccessTools.Method(typeof(WreckDamageLog), nameof(WreckDamageLog.Wear)),
            [AccessTools.Method(typeof(DamageSystem), nameof(DamageSystem.DamageRayRandom))] = AccessTools.Method(typeof(WreckDamageLog), nameof(WreckDamageLog.Ray))
        };
        var original = new List<CodeInstruction>(instructions);
        var result = original.ConvertAll(instruction => new CodeInstruction(instruction));
        var counts = new Dictionary<MethodInfo, int>();
        foreach (var instruction in result) {
            if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) &&
                instruction.operand is MethodInfo method && replacements.TryGetValue(method, out var replacement)) {
                instruction.opcode = OpCodes.Call;
                instruction.operand = replacement;
                counts.TryGetValue(method, out int count); counts[method] = count + 1;
            }
        }
        foreach (var pair in replacements) {
            counts.TryGetValue(pair.Key, out int count);
            bool valid = pair.Key.Name == nameof(Ship.GetCOs) ? count >= 2 :
                count == (pair.Key.Name == nameof(Ship.DamageAllCOs) ? 2 : 1);
            if (!valid) {
                // Diagnostics may fail closed without disabling the severity curve.
                WreckDamageLog.Enabled = false;
                OstrabusPlugin.Logger?.LogWarning("Wreck damage logging disabled: native initialization call sites changed.");
                return original;
            }
        }
        return result;
    }
}

// Optional read-only observer. No hook is installed with LogDamageRolls=false.
// The scope is opened only by the initial derelict BreakIn call above; ordinary
// combat has no capture and its results/random sequence are never rewritten.
[HarmonyPatch(typeof(JsonAttackMode), nameof(JsonAttackMode.GetDmgAmount))]
internal static class WreckRayRollPatch {
    static bool Prepare() => WreckDamageLog.Enabled;
    static void Postfix(JsonAttackMode __instance, CondOwner co, double __result) =>
        WreckDamageLog.ObserveRoll(__instance, co, __result);
}

// These two observers exist only with the existing startup diagnostic enabled.
[HarmonyPatch(typeof(BeatManager), nameof(BeatManager.GenerateRelease))]
internal static class WreckBonusReleasePatch {
    static bool Prepare() => WreckDamageLog.Enabled;
    static void Prefix(out WreckBonusLog.ReleaseState __state) {
        __state = WreckBonusLog.Release; WreckBonusLog.Release = new WreckBonusLog.ReleaseState { Active = true };
    }
    static Exception Finalizer(Exception __exception, WreckBonusLog.ReleaseState __state) {
        WreckBonusLog.Release = __state; return __exception;
    }
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        if (!WreckDamageLog.Enabled) return instructions;
        var original = new List<CodeInstruction>(instructions);
        var code = original.ConvertAll(instruction => new CodeInstruction(instruction));
        foreach (var names in new[] { new[] { "Plot", nameof(WreckBonusLog.Plot) },
            new[] { "SpawnVideotape", nameof(WreckBonusLog.Videotape) }, new[] { "BonusDerelict", nameof(WreckBonusLog.Bonus) } }) {
            var method = AccessTools.Method(typeof(BeatManager), names[0]); int count = 0;
            foreach (var instruction in code) if (instruction.Calls(method)) {
                instruction.operand = AccessTools.Method(typeof(WreckBonusLog), names[1]); count++;
            }
            if (count != 1) {
                WreckBonusLog.Write("[WreckBonus] release observer unavailable: native call sequence changed; original behavior retained.");
                return original;
            }
        }
        return code;
    }
}

[HarmonyPatch(typeof(Interaction), "ApplyLootCT")]
internal static class WreckBonusInteractionPatch {
    static bool Prepare() => WreckDamageLog.Enabled;
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        if (!WreckDamageLog.Enabled) return instructions;
        var code = new List<CodeInstruction>(instructions);
        var apply = AccessTools.Method(typeof(CondTrigger), nameof(CondTrigger.ApplyChanceID)); int count = 0;
        var result = new List<CodeInstruction>();
        foreach (var source in code) {
            var instruction = new CodeInstruction(source);
            if (!instruction.Calls(apply)) { result.Add(instruction); continue; }
            var loot = new CodeInstruction(OpCodes.Ldarg_1);
            instruction.MoveLabelsTo(loot); instruction.MoveBlocksTo(loot);
            result.Add(loot); result.Add(new CodeInstruction(OpCodes.Ldarg_0));
            result.Add(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(WreckBonusLog), nameof(WreckBonusLog.ApplyTrigger)))); count++;
        }
        if (count != 1) {
            WreckBonusLog.Write("[WreckBonus] interaction observer unavailable: native call sequence changed; original behavior retained.");
            return code;
        }
        return result;
    }
}
