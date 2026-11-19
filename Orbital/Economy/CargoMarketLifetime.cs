using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.Trading;

namespace Ostrabus.Orbital.Economy;

// Reuse the existing logical-ship replacement/destruction boundary. Never sweep
// shipless records or create a market singleton during loading/teardown.
internal static class CargoMarketLifetime {
    static readonly FieldInfo Singleton = AccessTools.Field(typeof(MonoSingleton<MarketManager>), "_instance");
    static readonly AccessTools.FieldRef<MarketManager, Dictionary<string, ShipMarket>> Markets =
        AccessTools.FieldRefAccess<MarketManager, Dictionary<string, ShipMarket>>("_market");

    internal static ShipMarket Capture(Ship ship) {
        var manager = Singleton.GetValue(null) as MarketManager;
        if (ReferenceEquals(manager, null) || Markets(manager) == null ||
            !Markets(manager).TryGetValue(ship.strRegID, out var market)) return null;
        foreach (var actor in market.GetConfigs()) if (actor.IsCargoPod) return market;
        return null;
    }
    internal static void Finish(StarSystem world, Ship ship, string id, ShipMarket captured) {
        if (captured == null || world == null || !ReferenceEquals(world, CrewSim.system) || world.aBOs == null ||
            world.dictShips == null || ship == null || !ship.bDestroyed || string.IsNullOrEmpty(id) ||
            world.dictShips.ContainsKey(id)) return;
        var manager = Singleton.GetValue(null) as MarketManager;
        if (ReferenceEquals(manager, null) || Markets(manager) == null ||
            !Markets(manager).TryGetValue(id, out var current) || !ReferenceEquals(current, captured)) return;
        // This is a real loss, not delivery: no destination stock is credited.
        MarketManager.UnregisterShip(id);
    }
}
