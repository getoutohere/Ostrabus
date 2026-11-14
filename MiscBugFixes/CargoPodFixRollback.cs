using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using Ostranauts.Trading;

namespace Ostrabus.MiscBugFixes;

// Request-local snapshots retain the native objects and their collection identities.
// Native add/remove rebuild aggregate caches, but do not mutate existing actor stock.
internal sealed class CargoPodFixRollback {
    static readonly string[] MarketFields = {
        "_marketMapping", "_stock", "_maxVirtualInventory", "_priceModifiers",
        "_productionMaps", "_loadedMarketActors", "_cachedTotalMass"
    };
    readonly List<Action> restore = new();
    internal CargoPodFixRollback(Ship ship, Ship remote, Dictionary<string, ShipMarket> markets) {
        string id = ship.strRegID;
        bool existed = markets.TryGetValue(id, out var market);
        restore.Add(() => { if (existed) markets[id] = market; else markets.Remove(id); });
        if (market != null) foreach (string name in MarketFields) {
            var field = AccessTools.Field(typeof(ShipMarket), name);
            Capture(field.GetValue(market), value => field.SetValue(market, value));
        }
        Mappings(ship);
        if (remote != null) Mappings(remote);
    }
    void Mappings(Ship ship) {
        Capture(ship.MarketConfigs, value => ship.MarketConfigs = (Dictionary<string, string>)value);
        var json = CargoPodFix.Saved(ship);
        if (json != null) Capture(json.aMarketConfigs, value => json.aMarketConfigs = (Dictionary<string, string>)value);
    }
    void Capture(object original, Action<object> assign) {
        if (original is IDictionary dictionary) {
            var entries = new DictionaryEntry[dictionary.Count];
            dictionary.CopyTo(entries, 0);
            restore.Add(() => {
                dictionary.Clear();
                foreach (var pair in entries) dictionary.Add(pair.Key, pair.Value);
                assign(original);
            });
        } else if (original is IList list) {
            var values = new object[list.Count];
            list.CopyTo(values, 0);
            restore.Add(() => { list.Clear(); foreach (var value in values) list.Add(value); assign(original); });
        } else restore.Add(() => assign(original));
    }
    internal void Restore() { foreach (var action in restore) action(); }
}
