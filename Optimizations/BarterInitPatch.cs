using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HarmonyLib;
using Ostranauts.Core.Models;
using Ostranauts.Ships;
using UnityEngine;

namespace Ostrabus.Optimizations;

// Remote barter copies only. All Unity/game work remains on the main thread.
[HarmonyPatch(typeof(BarterZoneShip), nameof(BarterZoneShip.Init))]
internal static class BarterInitPatch {
    static readonly AccessTools.FieldRef<Ship, DictionaryArray<string, CondOwner>> Items = AccessTools.FieldRefAccess<Ship, DictionaryArray<string, CondOwner>>("mapICOs");
    static readonly AccessTools.FieldRef<BarterZoneShip, bool> Ready = AccessTools.FieldRefAccess<BarterZoneShip, bool>("_fullyLoaded");
    static readonly Action<Ship, JsonZone[]> SetZones = AccessTools.MethodDelegate<Action<Ship, JsonZone[]>>(AccessTools.Method(typeof(Ship), "SetZoneData"));
    static readonly Action<BarterZoneShip> Index = AccessTools.MethodDelegate<Action<BarterZoneShip>>(AccessTools.Method(typeof(BarterZoneShip), "AddToLocalInstance"));

    static bool Prefix(BarterZoneShip __instance, int iteratorCounter, ref IEnumerator __result) {
        __result = Initialize(__instance, iteratorCounter);
        return false;
    }

    internal static IEnumerator Initialize(BarterZoneShip ship, int iteratorCounter) {
        var subItems = new List<JsonItem>();
        var saved = new List<JsonItem>(ship.json.aItems);
        ship.gameObject.SetActive(true);
        for (int i = 0; i < saved.Count; i++) {
            if (BarterFrameBudget.Exhausted()) yield return null;
            var item = saved[i];
            if (item.strParentID != null || item.strSlotParentID != null) { subItems.Add(item); continue; }
            var go = ship.CreatePart(item, item.strID, bLoot: true);
            if (go == null) continue;
            SetLayer(go);
            ship.AddCO(go.GetComponent<CondOwner>(), bTiles: true);
            // Retain the native outer-loop pacing as well as bounding its work.
            if (i % iteratorCounter == 0) yield return new WaitForSecondsRealtime(0.2f);
        }
        int progress = -1, next = -1;
        while (subItems.Count > 0) {
            if (BarterFrameBudget.Exhausted()) yield return null;
            if (next < 0) {
                if (progress == 0) break;
                next = subItems.Count - 1;
                progress = 0;
            }
            var item = subItems[next--];
            var items = Items(ship);
            string parentId = item.strParentID ?? item.strSlotParentID;
            if (!items.ContainsKey(parentId)) continue;
            var parent = items[parentId];
            var go = ship.CreatePart(item, item.strID, bLoot: false);
            if (go == null) continue;
            var co = go.GetComponent<CondOwner>();
            co.tf.localPosition = new Vector3(parent.tf.position.x, parent.tf.position.y, Container.fZSubOffset);
            bool add = true;
            if (parent.objContainer != null) {
                if (!parent.objContainer.Contains(co)) {
                    bool stacking = parent.objContainer.bAllowStacking;
                    parent.objContainer.bAllowStacking = false;
                    parent.objContainer.AddCOSimple(co, co.pairInventoryXY);
                    parent.objContainer.bAllowStacking = stacking;
                }
            } else if (item.strSlotParentID != null && co.jCOS != null) {
                if (parent.compSlots == null) {
                    UnityEngine.Debug.LogError("ERROR: Attempting to slot " + co.strCODef + " - " + co.strID + " into parent with no slot: " + parent.strCODef + " - " + parent.strID);
                    add = false;
                } else if (!parent.compSlots.SlotItem(co.jCOS.strSlotName, co)) continue;
            }
            if (add) items[co.strID] = co;
            ContainedItemRemovalPatch.Remove(subItems, item, next + 1);
            progress++;
        }
        if (BarterFrameBudget.Exhausted()) yield return null;
        SetZones(ship, ship.json.aZones);
        if (Items(ship) != null) {
            var snapshot = Items(ship).Values.ToArray();
            foreach (var co in snapshot) {
                if (BarterFrameBudget.Exhausted()) yield return null;
                co.PostGameLoad(Ship.Loaded.Shallow);
                SetLayer(co.gameObject);
            }
            // Same index algorithm as the synchronous patch, with yield points.
            var index = BarterLocalIndexPatch.Populate(ship, paced: true);
            try { while (index.MoveNext()) yield return index.Current; }
            finally { (index as IDisposable)?.Dispose(); }
        }
        if (BarterFrameBudget.Exhausted()) yield return null;
        ship.MoveShip(AsyncShipLoader.SPAWNOFFSET);
        for (int x = 0; x < ship.nCols; x++) {
            for (int y = 0; y < ship.nRows; y++) {
                if (BarterFrameBudget.Exhausted()) yield return null;
                int offset = y * ship.nCols + x;
                if (offset >= ship.aTiles.Count) break;
                if (y == 0 || y == ship.nRows - 1 || x == 0 || x == ship.nCols - 1) ship.aTiles[offset].coProps.AddCondAmount("IsObstruction", 1.0);
            }
        }
        Ready(ship) = true;
    }

    static void SetLayer(GameObject go) {
        int layer = LayerMask.NameToLayer(AsyncShipLoader.ASYNCLAYERNAME);
        go.layer = layer;
        foreach (Transform child in go.transform) child.gameObject.layer = layer;
    }

    // Malformed data retains the native error path used by the index patch.
    internal static void NativeIndex(BarterZoneShip ship) => Index(ship);
}

internal static class BarterFrameBudget {
    internal const double Milliseconds = 4;
    static int frame = -1;
    static long deadline;
    internal static bool Exhausted() {
        int current = Time.frameCount;
        long now = Stopwatch.GetTimestamp();
        if (frame != current) {
            frame = current;
            deadline = now + (long)(Stopwatch.Frequency * Milliseconds / 1000);
        }
        return now >= deadline;
    }
}
