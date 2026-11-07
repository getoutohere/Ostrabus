using HarmonyLib;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Trading;

namespace Ostrabus.Orbital.Economy;

[HarmonyPatch(typeof(StarSystem), "UpdateShip")]
internal static class EconomicFreightMotionPatch {
    [HarmonyPrefix]
    internal static bool Prefix(Ship ship) => SffEconomy.Current == null || !SffEconomy.Current.Owns(ship);
}
[HarmonyPatch(typeof(AIShipManager), nameof(AIShipManager.FFWD))]
internal static class EconomicCatchupPatch {
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    internal static void Prefix() { SffEconomy.Current?.Run(); }
}
[HarmonyPatch(typeof(AIShip), nameof(AIShip.FFWD))]
internal static class EconomicFreightCatchupPatch {
    [HarmonyPrefix]
    internal static bool Prefix(AIShip __instance) => SffEconomy.Current == null || !SffEconomy.Current.Owns(__instance.Ship);
}
[HarmonyPatch(typeof(MarketManager), nameof(MarketManager.Run))]
internal static class EconomicProductionPatch {
    [HarmonyPrefix]
    internal static bool Prefix() => SffEconomy.Current == null || !SffEconomy.Current.Completed;
}
[HarmonyPatch(typeof(AIShipManager), nameof(AIShipManager.CheckTradeRoutes))]
internal static class EconomicDispatchPatch {
    [HarmonyPrefix]
    internal static bool Prefix() => SffEconomy.Current == null || !SffEconomy.Current.Completed;
}
[HarmonyPatch(typeof(ShipMarket), nameof(ShipMarket.RequestTraderUpdate))]
internal static class EconomicTraderSyncPatch {
    [HarmonyPrefix]
    internal static bool Prefix(ShipMarket __instance) {
        var context = SffEconomy.Current;
        if (context == null || !context.Running) return true;
        context.DeferTrader(__instance);
        return false;
    }
}
