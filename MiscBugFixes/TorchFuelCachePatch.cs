using System;
using HarmonyLib;

namespace Ostrabus.MiscBugFixes;

// Native playback reconciles the tanks after reactor catch-up, but leaves the
// autopilot's cached budget describing the pre-reconciliation inventory.
[HarmonyPatch(typeof(FusionIC), nameof(FusionIC.SetReactants))]
internal static class TorchFuelCachePatch {
    static readonly AccessTools.FieldRef<FusionIC, double> ReactantD = AccessTools.FieldRefAccess<FusionIC, double>("fReactantD");
    static readonly AccessTools.FieldRef<FusionIC, double> ReactantHe3 = AccessTools.FieldRefAccess<FusionIC, double>("fReactantHe3");
    static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
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
            if (tank != null && tank.ship == ship) d += Math.Max(0, tank.GetCondAmount(FusionIC.aReactantNames[0]));
        if (inventory.COMapList.TryGetValue("ItmCanisterLHe02", out var heTanks)) foreach (var tank in heTanks)
            if (tank != null && tank.ship == ship) he3 += Math.Max(0, tank.GetCondAmount(FusionIC.aReactantNames[1]));
        double remaining = Math.Min(d / (usage * FusionIC.aReactantAmounts[0]), he3 / (usage * FusionIC.aReactantAmounts[1]));
        if (!Finite(d) || !Finite(he3) || !Finite(remaining)) return;
        ReactantD(__instance) = d;
        ReactantHe3(__instance) = he3;
        ship.fShallowFusionRemain = Math.Max(0, remaining);
    }
}
