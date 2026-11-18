using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;

namespace Ostrabus.MiscBugFixes;

// Native playback reconciles the tanks after reactor catch-up, but leaves the
// autopilot's cached budget describing the pre-reconciliation inventory.
[HarmonyPatch(typeof(FusionIC), nameof(FusionIC.SetReactants))]
internal static class TorchFuelCachePatch {
    static readonly AccessTools.FieldRef<FusionIC, double> ReactantD = AccessTools.FieldRefAccess<FusionIC, double>("fReactantD");
    static readonly AccessTools.FieldRef<FusionIC, double> ReactantHe3 = AccessTools.FieldRefAccess<FusionIC, double>("fReactantHe3");
    static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    // SetReactants can refund as well as debit. Its positive-fuel selectors
    // omit empty tanks, so one reactant can fill repeatedly while its partner
    // has nowhere to go. Keep the native transaction, but supply all receivers
    // and constrain refunds to the paired space actually available.
    static List<CondOwner> ReconciliationTanks(CODicts inventory, CondTrigger unused, string kind) {
        var result = new List<CondOwner>();
        if (inventory?.COMapList != null && inventory.COMapList.TryGetValue(kind, out var tanks))
            foreach (var tank in tanks) if (tank != null && !tank.bDestroyed) result.Add(tank);
        return result;
    }

    static bool TankBalance(CODicts inventory, Ship ship, string kind, string reactant, double density,
        out double amount, out double space) {
        amount = space = 0;
        bool overfilled = false;
        if (!inventory.COMapList.TryGetValue(kind, out var tanks)) return true;
        foreach (var tank in tanks) {
            if (tank == null || tank.bDestroyed || !ReferenceEquals(tank.ship, ship)) continue;
            double contents = tank.GetCondAmount(reactant), capacity = tank.GetCondAmount("StatVolume") * density;
            if (!Finite(contents) || !Finite(capacity) || contents < 0 || capacity < 0) return false;
            amount += contents;
            overfilled |= contents > capacity;
            space += Math.Max(0, capacity - contents);
        }
        // Native refill arithmetic assumes tanks are within capacity. Preserve
        // overfilled contents until an ordinary debit, rather than normalizing
        // them as a side effect of a refund into another canister.
        if (overfilled) space = 0;
        return Finite(amount) && Finite(space);
    }

    [HarmonyPrefix, HarmonyPriority(Priority.Last)]
    static void Prefix(FusionIC __instance, ref double fShallowFusionRemain) {
        if (!Finite(fShallowFusionRemain)) return;
        var ship = __instance.COSelf?.ship;
        var inventory = CrewSim.objInstance?.coDicts;
        double usage = __instance.MassUsageMax;
        if (ship == null || inventory?.COMapList == null || !Finite(usage) || usage <= 0) return;
        if (!TankBalance(inventory, ship, "ItmCanisterLH02", FusionIC.aReactantNames[0], 1107f, out double d, out double dSpace) ||
            !TankBalance(inventory, ship, "ItmCanisterLHe02", FusionIC.aReactantNames[1], 129.11f, out double he, out double heSpace)) return;
        double dRate = usage * FusionIC.aReactantAmounts[0], heRate = usage * FusionIC.aReactantAmounts[1];
        double remaining = Math.Min(d / dRate, he / heRate);
        if (fShallowFusionRemain <= remaining) return; // Ordinary expenditure is unchanged.
        double pairedSpace = Math.Min(dSpace / dRate, heSpace / heRate);
        fShallowFusionRemain = Math.Min(fShallowFusionRemain, remaining + pairedSpace);
    }

    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var select = AccessTools.Method(typeof(CODicts), nameof(CODicts.GetTriggeredCOListByType));
        var receivers = AccessTools.Method(typeof(TorchFuelCachePatch), nameof(ReconciliationTanks));
        int count = 0;
        foreach (var instruction in instructions) {
            if (instruction.Calls(select)) {
                count++;
                instruction.opcode = OpCodes.Call;
                instruction.operand = receivers;
            }
            yield return instruction;
        }
        if (count != 2) throw new InvalidOperationException("Expected two native fusion-reconciliation tank selectors.");
    }
    static void Postfix(FusionIC __instance, double fShallowFusionRemain) {
        if (!Finite(fShallowFusionRemain)) return;
        var ship = __instance.COSelf?.ship;
        var inventory = CrewSim.objInstance?.coDicts;
        if (ship == null || inventory == null) return;
        double usage = __instance.MassUsageMax;
        if (!Finite(usage) || usage <= 0) return;
        double d = 0, he3 = 0;
        // Read raw typed lists: GetTriggeredCOListByType allocates filtered copies.
        // Missing reactant conditions contribute zero, as in the native totals.
        if (inventory.COMapList.TryGetValue("ItmCanisterLH02", out var dTanks)) foreach (var tank in dTanks)
            if (tank != null && !tank.bDestroyed && tank.ship == ship) d += Math.Max(0, tank.GetCondAmount(FusionIC.aReactantNames[0]));
        if (inventory.COMapList.TryGetValue("ItmCanisterLHe02", out var heTanks)) foreach (var tank in heTanks)
            if (tank != null && !tank.bDestroyed && tank.ship == ship) he3 += Math.Max(0, tank.GetCondAmount(FusionIC.aReactantNames[1]));
        double remaining = Math.Min(d / (usage * FusionIC.aReactantAmounts[0]), he3 / (usage * FusionIC.aReactantAmounts[1]));
        if (!Finite(d) || !Finite(he3) || !Finite(remaining)) return;
        ReactantD(__instance) = d;
        ReactantHe3(__instance) = he3;
        ship.fShallowFusionRemain = Math.Max(0, remaining);
    }
}

// The native refill branch overwrites the outstanding negative withdrawal
// with minus this tank's free space. Carry the unused refund onward instead:
// removal += capacity - contents. Ordinary positive withdrawals stay native.
[HarmonyPatch(typeof(FusionIC), "RemoveFromTanks")]
internal static class TorchFuelRefundRemainderPatch {
    static bool Local(CodeInstruction instruction) => instruction.opcode == OpCodes.Ldloc || instruction.opcode == OpCodes.Ldloc_S ||
        instruction.opcode == OpCodes.Ldloc_0 || instruction.opcode == OpCodes.Ldloc_1 ||
        instruction.opcode == OpCodes.Ldloc_2 || instruction.opcode == OpCodes.Ldloc_3;

    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var code = new List<CodeInstruction>(instructions);
        int count = 0;
        for (int i = 0; i + 4 < code.Count; i++) {
            if (!Local(code[i]) || !Local(code[i+1]) || code[i+2].opcode != OpCodes.Sub ||
                code[i+3].opcode != OpCodes.Neg || !code[i+4].IsStarg(1)) continue;
            var load = new CodeInstruction(OpCodes.Ldarg_1).MoveLabelsFrom(code[i]).MoveBlocksFrom(code[i]);
            code[i+3].opcode = OpCodes.Add;
            code.Insert(i, load);
            count++;
            i += 5;
        }
        if (count != 1) throw new InvalidOperationException("Expected one native fusion-refund remainder assignment.");
        return code;
    }
}
