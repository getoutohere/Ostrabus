using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Ostrabus.Optimizations;
using Ostrabus.MiscBugFixes;
using Ostranauts.Core.Models;
using Ostranauts.Ships;
using Ostranauts.ShipGUIs.Trade;
using UnityEngine;

static partial class Program {
    static Vector3 itemPosition;
    static string serializationFailure;
    static bool Position(ref Vector3 __result) { __result = itemPosition; return false; }
    static bool Move(Vector3 value) { itemPosition = value; return false; }
    static bool SerializeItem(CondOwner co, ref JsonItem __result) {
        if (serializationFailure == "item") throw new InvalidOperationException("fixture item serialization failure");
        __result = new JsonItem { strID = co.strID, strName = co.strCODef, fX = itemPosition.x, fY = itemPosition.y };
        return false;
    }
    static bool SerializeSave(CondOwner __instance, ref JsonCondOwnerSave __result) {
        if (serializationFailure == "save") throw new InvalidOperationException("fixture CO serialization failure");
        __result = new JsonCondOwnerSave { strID = __instance.strID, strCODef = __instance.strCODef };
        return false;
    }
    static DictionaryArray<string, CondOwner> Items(Ship ship) => (DictionaryArray<string, CondOwner>)AccessTools.Field(typeof(Ship), "mapICOs").GetValue(ship);
    static void CheckWritebackExceptions() {
        foreach (string failure in new[] { "item", "save" }) {
            var copy = Copy("THROW-CONTROL", true);
            var original = Empty<Ship>();
            original.json = copy.json.Clone();
            var previous = original.json.aItems;
            var co = Empty<CondOwner>();
            Set(co, "_strID", "throw-item");
            co.tf = Empty<Transform>();
            co.mapGUIPropMaps = new Dictionary<string, Dictionary<string, string>>();
            var marker = new Dictionary<string, string> { ["custom"] = "preserved" };
            co.mapGUIPropMaps[GUITradeBase.ASYNCIDENTIFIER] = marker;
            Items(copy)[co.strID] = co;
            var position = new Vector3(0.00000125f, -0.00000125f, 0.25f);
            itemPosition = position;
            serializationFailure = failure;
            bool threw = false;
            try { copy.SaveChangedCOs(ref original); }
            catch (InvalidOperationException error) { threw = error.Message.StartsWith("fixture "); }
            finally { serializationFailure = null; }
            Check(threw, "Serialization fault was swallowed.");
            Check(itemPosition.Equals(position), "Serialization failure did not restore the exact transform position.");
            Check(ReferenceEquals(previous, original.json.aItems), "Serialization failure committed a partial layout.");
            Check(ReferenceEquals(marker, co.mapGUIPropMaps[GUITradeBase.ASYNCIDENTIFIER]) && marker["custom"] == "preserved", "Existing marker payload was overwritten.");
            copy.SaveChangedCOs(ref original);
            Check(original.json.aItems.Length == 1 && itemPosition.Equals(position), "Retry after serialization failure did not recover cleanly.");
        }
    }
    static void TransferCase(bool repaired) {
        var a = Copy("TRANSFER-A", true); var b = Copy("TRANSFER-B", true);
        var originalA = Empty<Ship>(); originalA.json = a.json.Clone();
        var originalB = Empty<Ship>(); originalB.json = b.json.Clone();
        var item = Empty<CondOwner>(); Set(item, "_strID", "remote-purchase"); item.strCODef = "ItmWallWindow1x1Loose";
        item.tf = Empty<Transform>(); item.mapGUIPropMaps = new Dictionary<string, Dictionary<string, string>>();
        Set(item, "_objShip", a); Items(a)[item.strID] = item;
        itemPosition = new Vector3(300, 300, 0);
        a.SaveChangedCOs(ref originalA);
        Check(originalA.json.aItems.Length == 1 && item.mapGUIPropMaps.ContainsKey(GUITradeBase.ASYNCIDENTIFIER), "Initial remote delivery did not create its native marker.");
        Check(itemPosition == new Vector3(300, 300, 0), "Successful initial writeback moved the item.");
        AccessTools.Method(typeof(BarterZoneShip), "RemoveFromLocalInstance").Invoke(a, new object[] { item.strID });
        Items(a).Remove(item.strID);
        a.SaveChangedCOs(ref originalA);
        Check(originalA.json.aItems.Length == 0, "Transfer source retained the item after its specialized removal.");
        // Model the physical transfer leaf. Native save-back, JSON membership and
        // marker handling execute unchanged on both independent remote copies.
        Set(item, "_objShip", b); Items(b)[item.strID] = item;
        itemPosition = new Vector3(300, 300, 0);
        bool failed = false;
        try { b.SaveChangedCOs(ref originalB); }
        catch (ArgumentException) { failed = true; }
        Check(failed != repaired, "Unexpected existing-marker writeback result.");
        Check(originalB.json.aItems.Length == (repaired ? 1 : 0) && Items(b).ContainsKey(item.strID), "Unexpected destination commit after reimport.");
        Check(itemPosition == new Vector3(repaired ? 300 : 100, repaired ? 300 : 100, 0), "Unexpected position after reimport.");
    }
    static void CheckCrossShipWriteback() {
        var fixture = new Harmony("offline.multi-barter.writeback-fixture");
        var production = new Harmony("offline.multi-barter.existing-optimization");
        var previousSaves = DataHandler.dictCOSaves;
        try {
            DataHandler.dictCOSaves = new Dictionary<string, JsonCondOwnerSave>();
            fixture.Patch(AccessTools.PropertyGetter(typeof(Transform), nameof(Transform.position)), prefix: new HarmonyMethod(typeof(Program), nameof(Position)));
            fixture.Patch(AccessTools.PropertySetter(typeof(Transform), nameof(Transform.position)), prefix: new HarmonyMethod(typeof(Program), nameof(Move)));
            fixture.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.GetJsonItem)), prefix: new HarmonyMethod(typeof(Program), nameof(SerializeItem)));
            fixture.Patch(AccessTools.Method(typeof(CondOwner), nameof(CondOwner.GetJSONSave)), prefix: new HarmonyMethod(typeof(Program), nameof(SerializeSave)));
            TransferCase(false);
            CheckNativeTradeRoundTrips(false);
            production.CreateClassProcessor(typeof(BarterWritebackSafetyPatch)).Patch();
            TransferCase(true);
            CheckNativeTradeRoundTrips(true);
            CheckWritebackExceptions();
            production.CreateClassProcessor(typeof(BarterWritebackPatch)).Patch();
            TransferCase(true);
            CheckNativeTradeRoundTrips(true);
            CheckWritebackExceptions();
            Console.WriteLine("FIXED: existing-marker imports and native buy/sell/rebuy sequences persist with both native and optimized writeback.");
        } finally { production.UnpatchSelf(); fixture.UnpatchSelf(); DataHandler.dictCOSaves = previousSaves; }
    }
}
