using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Ostranauts.Trading;

namespace Ostrabus.Orbital.Economy;

// RegisterAICargoHauler re-adds its local remainder after the pod loop. A fully
// accepted list must be empty there: TryAddStock clones its entries, not consumes them.
[HarmonyPatch(typeof(MarketManager), nameof(MarketManager.RegisterAICargoHauler))]
internal static class CargoLoadingPatch {
    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var original = AccessTools.Method(typeof(MarketActorConfig), nameof(MarketActorConfig.TryAddStock), new[] { typeof(string), typeof(List<MarketItem>) });
        var replacement = AccessTools.Method(typeof(CargoLoadingPatch), nameof(Add));
        int matches = 0;
        var code = new List<CodeInstruction>(instructions);
        foreach (var instruction in code) if (instruction.Calls(original)) { instruction.opcode = OpCodes.Call; instruction.operand = replacement; matches++; }
        if (matches != 1) throw new InvalidOperationException("Cargo-loading call shape changed; expected one TryAddStock.");
        return code;
    }
    internal static int Add(MarketActorConfig pod, string category, List<MarketItem> remaining) {
        int overflow = pod.TryAddStock(category, remaining);
        if (overflow == 0) remaining.Clear();
        return overflow;
    }
}
