using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using Ostranauts.Core.Models;
using Ostranauts.Trading;

namespace Ostrabus.Orbital.Economy;

[HarmonyPatch(typeof(ShipMarket), nameof(ShipMarket.RemoveStockForCategory))]
internal static class MarketWithdrawalPatch {
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var code = new List<CodeInstruction>(instructions);
        var append = AccessTools.Method(typeof(List<MarketItem>), nameof(List<MarketItem>.AddRange));
        var count = AccessTools.PropertyGetter(typeof(List<MarketItem>), nameof(List<MarketItem>.Count));
        var sites = Enumerable.Range(0, code.Count).Where(i => code[i].Calls(append)).ToArray();
        if (sites.Length != 2) throw new InvalidOperationException("Market withdrawal append shape changed.");
        int site = sites[1];
        // After appending this actor's result, subtract that result's count,
        // not the accumulated result from every actor visited so far.
        if (!CargoPatchIL.IsLoad(code[site - 1]) || !CargoPatchIL.IsLoad(code[site + 2]) ||
            !code[site + 3].Calls(count) || code[site + 4].opcode != OpCodes.Sub)
            throw new InvalidOperationException("Market withdrawal remainder shape changed.");
        code[site + 2].opcode = code[site - 1].opcode;
        code[site + 2].operand = code[site - 1].operand;
        return code;
    }
}

[HarmonyPatch(typeof(ShipMarket), nameof(ShipMarket.AddStockForCategory),
    new[] { typeof(string), typeof(List<MarketItem>), typeof(float), typeof(string) })]
internal static class MarketBatchDepositPatch {
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var code = new List<CodeInstruction>(instructions);
        var add = AccessTools.Method(typeof(MarketActorConfig), nameof(MarketActorConfig.TryAddStock),
            new[] { typeof(string), typeof(List<MarketItem>) });
        var count = AccessTools.PropertyGetter(typeof(List<MarketItem>), nameof(List<MarketItem>.Count));
        int start = code.FindIndex(i => i.Calls(count));
        if (start < 0 || start + 1 >= code.Count || !CargoPatchIL.IsStore(code[start + 1]) || code.Count(i => i.Calls(add)) != 2)
            throw new InvalidOperationException("Market batch deposit remainder shape changed.");
        var remaining = code[start + 1];
        foreach (var instruction in code) {
            if (instruction.Calls(add)) {
                var load = CargoPatchIL.LoadLocal(remaining);
                instruction.MoveLabelsTo(load); instruction.MoveBlocksTo(load);
                yield return load;
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(MarketBatchDepositPatch), nameof(AddRemaining));
            }
            yield return instruction;
        }
    }

    internal static int AddRemaining(MarketActorConfig actor, string category, List<MarketItem> items, int remaining) {
        if (remaining == 0) return 0;
        // Leave the caller's list intact. Native actor stocking clones the
        // accepted items; subsequent actors receive only the unaccepted tail.
        int remainder = actor.TryAddStock(category, remaining == items.Count ? items : items.GetRange(items.Count - remaining, remaining));
        // A pre-existing over-capacity actor can report more remainder than it
        // received. It accepted nothing; never grow the pending consignment.
        return Math.Min(remaining, remainder);
    }
}

[HarmonyPatch(typeof(AIShipManager), nameof(AIShipManager.GetCargoShipForRoute))]
internal static class CargoHullSizingPatch {
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var code = new List<CodeInstruction>(instructions);
        var ceiling = AccessTools.Method(typeof(UnityEngine.Mathf), nameof(UnityEngine.Mathf.CeilToInt));
        var sites = Enumerable.Range(0, code.Count).Where(i => code[i].Calls(ceiling)).ToArray();
        if (sites.Length != 1 || !CargoPatchIL.IsStore(code[sites[0] + 1]))
            throw new InvalidOperationException("Cargo hull sizing shape changed.");
        for (int i = 0; i < code.Count; i++) {
            yield return code[i];
            if (i != sites[0]) continue;
            yield return CargoPatchIL.LoadLocal(code[i + 1]);
            yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(CargoHullSizingPatch), nameof(Sum)));
        }
    }

    internal static int Sum(int next, int previous) => (int)Math.Min(int.MaxValue, (long)next + previous);
}

internal static class CargoPatchIL {
    internal static bool IsLoad(CodeInstruction code) => code.opcode == OpCodes.Ldloc || code.opcode == OpCodes.Ldloc_S ||
        code.opcode == OpCodes.Ldloc_0 || code.opcode == OpCodes.Ldloc_1 || code.opcode == OpCodes.Ldloc_2 || code.opcode == OpCodes.Ldloc_3;
    internal static bool IsStore(CodeInstruction code) => code.opcode == OpCodes.Stloc || code.opcode == OpCodes.Stloc_S ||
        code.opcode == OpCodes.Stloc_0 || code.opcode == OpCodes.Stloc_1 || code.opcode == OpCodes.Stloc_2 || code.opcode == OpCodes.Stloc_3;
    internal static CodeInstruction LoadLocal(CodeInstruction store) => new CodeInstruction(
        store.opcode == OpCodes.Stloc_0 ? OpCodes.Ldloc_0 :
        store.opcode == OpCodes.Stloc_1 ? OpCodes.Ldloc_1 :
        store.opcode == OpCodes.Stloc_2 ? OpCodes.Ldloc_2 :
        store.opcode == OpCodes.Stloc_3 ? OpCodes.Ldloc_3 :
        store.opcode == OpCodes.Stloc_S ? OpCodes.Ldloc_S :
        store.opcode == OpCodes.Stloc ? OpCodes.Ldloc : throw new InvalidOperationException("Unknown cargo remainder local."), store.operand);
}
