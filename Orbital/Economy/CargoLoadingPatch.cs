using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using Ostranauts.Core.Models;
using Ostranauts.Trading;

namespace Ostrabus.Orbital.Economy;

// Retain loaded pod state across consignments and clear accepted remainders.
[HarmonyPatch(typeof(MarketManager), nameof(MarketManager.RegisterAICargoHauler))]
internal static class CargoLoadingPatch {
    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var original = AccessTools.Method(typeof(MarketActorConfig), nameof(MarketActorConfig.TryAddStock), new[] { typeof(string), typeof(List<MarketItem>) });
        var replacement = AccessTools.Method(typeof(CargoLoadingPatch), nameof(Add));
        var factory = AccessTools.Method(typeof(DataHandler), nameof(DataHandler.GetMarketConfig));
        var keyGetter = AccessTools.PropertyGetter(typeof(KeyValuePair<string, string>), "Key");
        var code = new List<CodeInstruction>(instructions);
        var keys = Enumerable.Range(0, code.Count - 1).Where(i => code[i].Calls(keyGetter)).ToArray();
        if (keys.Length != 1 || code.Count(i => i.Calls(factory)) != 1 || code.Count(i => i.Calls(original)) != 1)
            throw new InvalidOperationException("Cargo-loading call shape changed; expected one actor key, pod factory and TryAddStock.");
        var store = code[keys[0] + 1];
        var load = new CodeInstruction(store.opcode == OpCodes.Stloc_0 ? OpCodes.Ldloc_0 :
            store.opcode == OpCodes.Stloc_1 ? OpCodes.Ldloc_1 :
            store.opcode == OpCodes.Stloc_2 ? OpCodes.Ldloc_2 :
            store.opcode == OpCodes.Stloc_3 ? OpCodes.Ldloc_3 :
            store.opcode == OpCodes.Stloc_S ? OpCodes.Ldloc_S :
            store.opcode == OpCodes.Stloc ? OpCodes.Ldloc : throw new InvalidOperationException("Unknown cargo actor-key local."), store.operand);
        var result = new List<CodeInstruction>(code.Count + 2);
        foreach (var instruction in code) {
            if (instruction.Calls(factory)) {
                var owner = new CodeInstruction(OpCodes.Ldarg_0);
                instruction.MoveLabelsTo(owner); instruction.MoveBlocksTo(owner);
                result.Add(owner); result.Add(load);
                instruction.operand = AccessTools.Method(typeof(CargoLoadingPatch), nameof(Pod));
            } else if (instruction.Calls(original)) {
                instruction.opcode = OpCodes.Call; instruction.operand = replacement;
            }
            result.Add(instruction);
        }
        return result;
    }
    internal static MarketActorConfig Pod(string template, Ship ship, string key) =>
        MarketManager.GetShipMarket(ship.strRegID)?.GetConfigForMarketActor(key) ?? DataHandler.GetMarketConfig(template);

    internal static int Add(MarketActorConfig pod, string category, List<MarketItem> remaining) {
        int overflow = pod.TryAddStock(category, remaining);
        if (overflow == 0) remaining.Clear();
        return overflow;
    }
}

[HarmonyPatch(typeof(MarketManager), nameof(MarketManager.GetTradeRoute))]
internal static class CargoTradeBundlePatch {
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var code = new List<CodeInstruction>(instructions);
        var append = AccessTools.Method(typeof(List<TradeRouteDTO>), nameof(List<TradeRouteDTO>.AddRange));
        int matches = 0;
        foreach (var instruction in code) if (instruction.Calls(append)) {
            instruction.opcode = OpCodes.Call;
            instruction.operand = AccessTools.Method(typeof(CargoTradeBundlePatch), nameof(Append));
            matches++;
        }
        if (matches != 1) throw new InvalidOperationException("Native trade bundle changed; expected one AddRange.");
        return code;
    }

    // The seed is already selected. Append its genuine companion consignments
    // once, preserving order and distinct DTOs even when their values coincide.
    internal static void Append(List<TradeRouteDTO> selected, IEnumerable<TradeRouteDTO> companions) {
        var seed = selected[0];
        foreach (var route in companions) if (!ReferenceEquals(route, seed)) selected.Add(route);
    }
}
