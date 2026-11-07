using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Ostrabus.Optimizations;
using Ostranauts.Ships;
using UnityEngine;

static partial class Program {
    static readonly Dictionary<GameObject, CondOwner> components = new Dictionary<GameObject, CondOwner>(new RefComparer<GameObject>());
    static bool CreatePart(JsonItem __0, ref GameObject __result) {
        events.Add("create:" + __0.strID);
        if (__0.strName == "fail") { __result = null; return false; }
        var go = Empty<GameObject>(); var co = CO(__0.strID, __0.strName);
        if (__0.strName == "slots") Field(co, "_compSlots", Empty<Slots>());
        else { co.objContainer = Empty<Container>(); co.objContainer.bAllowStacking = true; }
        if (__0.strSlotParentID != null) co.jCOS = new JsonCondOwnerSave { strSlotName = "heldR" };
        components[go] = co; DataHandler.mapCOs[co.strID] = co; __result = go; return false;
    }
    static bool GetComponent(GameObject __instance, ref CondOwner __result) { __result = components[__instance]; return false; }
    static bool AddRoot(Ship __instance, CondOwner __0) { Items(__instance)[__0.strID] = __0; events.Add("root:" + __0.strID); return false; }
    static bool HasCond(ref bool __result) { __result = false; return false; }
    static bool Destroyed(JsonItem __0, ref bool __result) { __result = __0.strName == "destroyed"; return false; }
    static bool Contains(ref bool __result) { __result = false; return false; }
    static bool AddContents(Container __instance, CondOwner __0) {
        Check(!__instance.bAllowStacking, "Stacking enabled during loader restoration"); events.Add("contained:" + __0.strID); return false;
    }
    static bool Slot(string __0, CondOwner __1, ref bool __result) { events.Add("slot:" + __1.strID); __result = __1.strName != "reject"; return false; }
    static void LoaderFixture(Harmony fixture) {
        Prefix(fixture, typeof(Ship), "CreatePart", nameof(CreatePart));
        Prefix(fixture, typeof(Ship), "AddCO", nameof(AddRoot), new[] { typeof(CondOwner), typeof(bool), typeof(bool) });
        Prefix(fixture, typeof(AsyncPreviewShip), "AddCO", nameof(AddRoot));
        Prefix(fixture, typeof(CondOwner), "HasCond", nameof(HasCond), new[] { typeof(string) });
        Prefix(fixture, typeof(CondOwner), "HasCond", nameof(HasCond), new[] { typeof(string), typeof(bool) });
        Prefix(fixture, typeof(Ship), "IsItemDestroyed", nameof(Destroyed));
        Prefix(fixture, typeof(GameObject), "SetActive", nameof(Quiet)); Prefix(fixture, typeof(GameObject), "set_layer", nameof(Quiet));
        Prefix(fixture, typeof(Transform), "set_localPosition", nameof(SetPosition));
        Prefix(fixture, typeof(Container), "Contains", nameof(Contains)); Prefix(fixture, typeof(Container), "AddCOSimple", nameof(AddContents));
        Prefix(fixture, typeof(Slots), "SlotItem", nameof(Slot), new[] { typeof(string), typeof(CondOwner), typeof(bool) });
        Prefix(fixture, typeof(Ship), "MoveShip", nameof(Quiet), new[] { typeof(Vector2) }); Prefix(fixture, typeof(Debug), "Break", nameof(Quiet));
        var generic = typeof(GameObject).GetMethods().Single(m => m.Name == "GetComponent" && m.IsGenericMethod && m.GetParameters().Length == 0).MakeGenericMethod(typeof(CondOwner));
        fixture.Patch(generic, prefix: new HarmonyMethod(typeof(Program), nameof(GetComponent)));
    }
    static string LoadCase(JsonItem[] records, bool preview) {
        Reset(); components.Clear(); DataHandler.mapCOs = new Dictionary<string, CondOwner>();
        foreach (var record in records) DataHandler.dictCOSaves[record.strID] = new JsonCondOwnerSave { strID = record.strID };
        Ship ship;
        if (preview) {
            var copy = Empty<AsyncPreviewShip>(); ship = copy; copy.json = new JsonShip { aItems = records };
            copy.gameObject = Empty<GameObject>(); Items(copy) = new Ostranauts.Core.Models.DictionaryArray<string, CondOwner>();
            var iterator = copy.Init(100); int steps = 0;
            while (iterator.MoveNext()) if (++steps > 1000) throw new Exception("Preview did not finish");
            Check(copy.FullyLoaded, "Preview readiness changed");
        } else {
            ship = Empty<Ship>(); Items(ship) = new Ostranauts.Core.Models.DictionaryArray<string, CondOwner>();
            Field(ship, "mapIDRemap", new Dictionary<string, string>());
            var args = new object[] { records.ToList(), false, Ship.Loaded.Shallow, new Dictionary<string, CondOwner>(), new List<CondOwner>() };
            AccessTools.Method(typeof(Ship), "SpawnItems").Invoke(ship, args);
        }
        return Json(new { calls = events.ToArray(), ids = Items(ship).Keys.ToArray(), stacking = Items(ship).Values.Select(co => co.objContainer?.bAllowStacking).ToArray() });
    }
    static void RemovalChecks(Harmony production, Harmony fixture) {
        LoaderFixture(fixture);
        var cases = new[] {
            Array.Empty<JsonItem>(),
            new[] { new JsonItem { strID = "root" }, new JsonItem { strID = "child", strParentID = "root", bForceLoad = true }, new JsonItem { strID = "grand", strParentID = "child", bForceLoad = true } },
            new[] { new JsonItem { strID = "root", strName = "slots" }, new JsonItem { strID = "good", strSlotParentID = "root", bForceLoad = true }, new JsonItem { strID = "bad", strName = "reject", strSlotParentID = "root", bForceLoad = true }, new JsonItem { strID = "unresolved", strParentID = "absent", bForceLoad = true } },
            new[] { new JsonItem { strID = "root" }, new JsonItem { strID = "dead", strName = "destroyed", strParentID = "root", bForceLoad = true }, new JsonItem { strID = "no-create", strName = "fail", strParentID = "root", bForceLoad = true } },
            Enumerable.Range(0, 80).Select(i => new JsonItem { strID = "chain" + i, strParentID = i == 0 ? null : "chain" + (i - 1), bForceLoad = true }).ToArray()
        };
        var baseline = cases.Select(records => new[] { LoadCase(records, false), LoadCase(records, true) }).ToArray();
        production.CreateClassProcessor(typeof(ContainedItemRemovalPatch)).Patch();
        for (int i = 0; i < cases.Length; i++) for (int mode = 0; mode < 2; mode++)
            Check(LoadCase(cases[i], mode == 1) == baseline[i][mode], "Native loader behavior changed for " + i + "/" + mode);
        var random = new System.Random(17);
        for (int trial = 0; trial < 100; trial++) {
            var records = Enumerable.Range(0, 200).Select(i => new JsonItem { strID = "id" + (i % 10) }).ToArray();
            var native = records.ToList(); var indexed = records.ToList();
            while (native.Count > 0) {
                int index = random.Next(native.Count); var item = native[index];
                Check(native.Remove(item) == ContainedItemRemovalPatch.Remove(indexed, item, index), "Removal result changed");
                Check(native.SequenceEqual(indexed), "Known-index removal changed record order");
            }
        }
        var list = cases[1].ToList(); var target = list[1];
        Check(ContainedItemRemovalPatch.Remove(list, target, 0) && !list.Contains(target), "Changed-index fallback failed");
        var single = new List<JsonItem>(1); var recordOne = new JsonItem(); single.Add(recordOne); ContainedItemRemovalPatch.Remove(single, recordOne, 0);
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) { single.Add(recordOne); ContainedItemRemovalPatch.Remove(single, recordOne, 0); }
        Check(GC.GetAllocatedBytesForCurrentThread() == allocated, "Known-index removal allocates");
    }
}
