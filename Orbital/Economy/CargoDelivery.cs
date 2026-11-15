using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Ostranauts.Trading;

namespace Ostrabus.Orbital.Economy;

// A valid receiver accepts what fits; native freight policy discards overflow.
// An absent receiver or failed transfer is not a successful arrival.
internal static class CargoDelivery {
    static readonly MethodInfo Clone = AccessTools.Method(typeof(object), "MemberwiseClone");
    static readonly Action<ShipMarket, string, float> UpdatePrice = (Action<ShipMarket, string, float>)
        AccessTools.Method(typeof(ShipMarket), "UpdatePriceModifier").CreateDelegate(typeof(Action<ShipMarket, string, float>));
    static readonly AccessTools.FieldRef<ShipMarket, Dictionary<string, MarketActorConfig>> Actors =
        AccessTools.FieldRefAccess<ShipMarket, Dictionary<string, MarketActorConfig>>("_marketMapping");
    static readonly AccessTools.FieldRef<ShipMarket, Dictionary<string, int>> Stock =
        AccessTools.FieldRefAccess<ShipMarket, Dictionary<string, int>>("_stock");
    static readonly AccessTools.FieldRef<ShipMarket, Dictionary<string, int>> Capacity =
        AccessTools.FieldRefAccess<ShipMarket, Dictionary<string, int>>("_maxVirtualInventory");
    static readonly AccessTools.FieldRef<ShipMarket, Dictionary<string, float>> Prices =
        AccessTools.FieldRefAccess<ShipMarket, Dictionary<string, float>>("_priceModifiers");
    static readonly AccessTools.FieldRef<ShipMarket, List<Producer>> Producers =
        AccessTools.FieldRefAccess<ShipMarket, List<Producer>>("_productionMaps");
    static readonly AccessTools.FieldRef<ShipMarket, double> Mass =
        AccessTools.FieldRefAccess<ShipMarket, double>("_cachedTotalMass");

    internal static bool Deliver(string carrier, string receiver) => Transfer(carrier, receiver, discardOverflow: true);
    internal static bool Refund(string carrier, string receiver) => Transfer(carrier, receiver, discardOverflow: false);

    static bool Transfer(string carrier, string receiver, bool discardOverflow) {
        var source = MarketManager.GetShipMarket(carrier);
        if (source == null) return true; // PASS, noncargo arrival, or already committed.
        var destination = MarketManager.GetShipMarket(receiver);
        if (destination == null || ReferenceEquals(source, destination)) return false;

        // Stage only inventory state. Native actor stocking still determines
        // acceptance and clones concrete item definitions. No live stock changes
        // until every category and the native cache rebuild have succeeded.
        try {
            var manifest = new Dictionary<string, List<MarketItem>>();
            foreach (var actor in Actors(source).Values) foreach (var pair in actor.Stock) {
                if (pair.Value.Count == 0) continue;
                if (!manifest.TryGetValue(pair.Key, out var items)) manifest.Add(pair.Key, items = new List<MarketItem>());
                items.AddRange(pair.Value);
            }
            var staged = Copy(destination, manifest);
            foreach (var pair in manifest) {
                var pending = pair.Value;
                foreach (var actor in Actors(staged).Values) {
                    int remainder = actor.TryAddStock(pair.Key, pending);
                    if (remainder == 0) { pending = null; break; }
                    if (remainder < pending.Count) pending = pending.GetRange(pending.Count - remainder, remainder);
                }
                // A rejected departure is a refund, not a delivery: do not dump
                // the load or retire its hull if the origin cannot take it back.
                if (!discardOverflow && pending != null && pending.Count > 0) return false;
            }
            staged.BuildConfig();
            // Native arrival prices only the transferred categories using the
            // carrier's blockade discount. Preserve that contract and unrelated
            // category modifiers, including categories unsupported by receiver.
            Prices(staged) = new Dictionary<string, float>(Prices(destination));
            float discount = source.BlockadeDiscount;
            foreach (string category in manifest.Keys) UpdatePrice(staged, category, discount);
            Commit(destination, staged, manifest);
        } catch (Exception ex) {
            OstrabusPlugin.Logger?.LogError("[Freight] Transfer not committed; carrier retained: " + carrier + " -> " + receiver + ": " + ex);
            return false;
        }
        MarketManager.UnregisterShip(carrier);
        try { destination.RequestTraderUpdate(); }
        catch (Exception ex) {
            // Stock is already committed. A presentation failure must not make
            // callers repeat delivery or treat this as a rejected departure.
            OstrabusPlugin.Logger?.LogError("[Freight] Cargo committed; trader refresh failed: " + receiver + ": " + ex);
        }
        return true;
    }

    static ShipMarket Copy(ShipMarket market, Dictionary<string, List<MarketItem>> manifest) {
        var copy = (ShipMarket)Clone.Invoke(market, null);
        var actors = new Dictionary<string, MarketActorConfig>();
        foreach (var pair in Actors(market)) {
            var actor = (MarketActorConfig)Clone.Invoke(pair.Value, null);
            actor.Stock = new Dictionary<string, List<MarketItem>>(pair.Value.Stock);
            foreach (string category in manifest.Keys)
                if (actor.Stock.TryGetValue(category, out var items)) actor.Stock[category] = new List<MarketItem>(items);
            actors.Add(pair.Key, actor);
        }
        Actors(copy) = actors;
        Stock(copy) = new Dictionary<string, int>(Stock(market));
        Capacity(copy) = new Dictionary<string, int>(Capacity(market));
        Prices(copy) = new Dictionary<string, float>(Prices(market));
        Producers(copy) = new List<Producer>(Producers(market));
        return copy;
    }

    static void Commit(ShipMarket market, ShipMarket staged, Dictionary<string, List<MarketItem>> manifest) {
        // Preserve market, actor, stock-list and price-map identities for open
        // trader views. Native acceptance appends; existing items are unchanged.
        foreach (var pair in Actors(market)) foreach (string category in manifest.Keys) {
            if (!Actors(staged)[pair.Key].Stock.TryGetValue(category, out var incoming)) continue;
            if (!pair.Value.Stock.TryGetValue(category, out var current)) pair.Value.Stock.Add(category, incoming);
            else for (int i = current.Count; i < incoming.Count; i++) current.Add(incoming[i]);
        }
        Stock(market).Clear(); foreach (var pair in Stock(staged)) Stock(market).Add(pair.Key, pair.Value);
        Prices(market).Clear(); foreach (var pair in Prices(staged)) Prices(market).Add(pair.Key, pair.Value);
        Mass(market) = -1;
    }
}
