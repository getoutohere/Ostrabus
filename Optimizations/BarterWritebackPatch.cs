using System;
using System.Collections.Generic;
using HarmonyLib;
using Ostranauts.Core.Models;
using Ostranauts.ShipGUIs.Trade;
using Ostranauts.Ships;

namespace Ostrabus.Optimizations;

[HarmonyPatch(typeof(BarterZoneShip), nameof(BarterZoneShip.SaveChangedCOs))]
internal static class BarterWritebackPatch {
    static readonly AccessTools.FieldRef<Ship, DictionaryArray<string, CondOwner>> Items = AccessTools.FieldRefAccess<Ship, DictionaryArray<string, CondOwner>>("mapICOs");
    static bool Prefix(BarterZoneShip __instance, Ship originalShip) {
        // Keep malformed-input exception behavior on the native path.
        var source = originalShip?.json?.aItems;
        if (source == null || __instance.json?.aItems == null || Items(__instance) == null) return true;
        foreach (var item in source) if (item == null) return true;
        Write(__instance, originalShip);
        return false;
    }
    internal static void Write(BarterZoneShip ship, Ship original) {
        var scratch = BarterWritebackScratch.Rent();
        try {
            scratch.Index(original.json.aItems);
            foreach (var entry in Items(ship).GetKeyValuePairs()) {
                // A nested native callback may replace the original layout array.
                if (!ReferenceEquals(scratch.Source, original.json.aItems)) scratch.Index(original.json.aItems);
                if (scratch.Ids.Contains(entry.Key)) continue;
                var co = entry.Value;
                co.tf.position -= AsyncShipLoader.SPAWNOFFSET;
                co.mapGUIPropMaps.Add(GUITradeBase.ASYNCIDENTIFIER, new Dictionary<string, string> {
                    { GUITradeBase.ASYNCIDENTIFIER, GUITradeBase.ASYNCIDENTIFIER }
                });
                scratch.Added.Add(ship.GetJsonItem(co));
                var saved = co.GetJSONSave();
                if (saved != null) DataHandler.dictCOSaves[saved.strID] = saved;
                co.tf.position += AsyncShipLoader.SPAWNOFFSET;
                // Native membership remains the original array until final commit.
                // In particular, do not add new keys to Ids here.
            }
            var previous = ship.json.aItems;
            int count = previous.Length + scratch.Added.Count;
            var updated = count == 0 ? Array.Empty<JsonItem>() : new JsonItem[count];
            Array.Copy(previous, updated, previous.Length);
            scratch.Added.CopyTo(updated, previous.Length);
            original.json.aItems = updated;
            ship.json.aItems = updated;
        } finally { BarterWritebackScratch.Release(scratch); }
    }
}
