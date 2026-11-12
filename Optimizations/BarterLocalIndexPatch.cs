using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Ostranauts.Core.Models;
using Ostranauts.Ships;

namespace Ostrabus.Optimizations;

[HarmonyPatch(typeof(BarterZoneShip), "AddToLocalInstance")]
internal static class BarterLocalIndexPatch {
    static readonly AccessTools.FieldRef<Ship, DictionaryArray<string, CondOwner>> Items = AccessTools.FieldRefAccess<Ship, DictionaryArray<string, CondOwner>>("mapICOs");
    static bool Prefix(BarterZoneShip __instance) {
        var items = Items(__instance);
        if (items == null || items.Count == 0) return false;
        var saved = __instance.json?.aItems;
        // Preserve native handling of malformed input instead of partially indexing it.
        if (saved == null || saved.Any(item => item == null)) return true;
        var work = Populate(__instance, paced: false);
        try { while (work.MoveNext()) { } }
        finally { (work as IDisposable)?.Dispose(); }
        return false;
    }

    internal static IEnumerator Populate(BarterZoneShip ship, bool paced) {
        var items = Items(ship);
        if (items == null || items.Count == 0) yield break;
        var saved = ship.json?.aItems;
        if (saved == null || saved.Any(item => item == null)) { BarterInitPatch.NativeIndex(ship); yield break; }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in saved) {
            if (paced && BarterFrameBudget.Exhausted()) yield return null;
            ids.Add(item.strID);
        }
        // Match the native snapshot: serializing an item must not change iteration.
        foreach (var co in items.Values.ToList()) {
            if (paced && BarterFrameBudget.Exhausted()) yield return null;
            if (paced && co == null) continue;
            if (ids.Contains(co.strID)) continue;
            var added = ship.GetJsonItem(co);
            var previous = ship.json.aItems;
            var updated = new JsonItem[previous.Length + 1];
            Array.Copy(previous, updated, previous.Length); updated[previous.Length] = added;
            ship.json.aItems = updated;
            if (added == null) { BarterInitPatch.NativeIndex(ship); yield break; }
            ids.Add(added.strID);
        }
    }
}
