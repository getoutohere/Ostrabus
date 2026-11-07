using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using HarmonyLib;
using Ostrabus.MiscBugFixes;
using Ostranauts.Core.Models;
using Ostranauts.Ships;
using UnityEngine;

static class Program {
    sealed class RefComparer : IEqualityComparer<UnityEngine.Object> {
        public bool Equals(UnityEngine.Object a, UnityEngine.Object b) => ReferenceEquals(a, b);
        public int GetHashCode(UnityEngine.Object a) => RuntimeHelpers.GetHashCode(a);
    }
    static readonly Dictionary<UnityEngine.Object, GameObject> objects = new Dictionary<UnityEngine.Object, GameObject>(new RefComparer());
    static readonly HashSet<UnityEngine.Object> dead = new HashSet<UnityEngine.Object>(new RefComparer());
    static int checks, physicalDestroys, callbacks;
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Field(object obj, string name, object value) => AccessTools.Field(obj.GetType(), name).SetValue(obj, value);
    static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
    static bool Null(UnityEngine.Object obj) => ReferenceEquals(obj, null) || dead.Contains(obj);
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = ReferenceEquals(__0, __1) || Null(__0) && Null(__1); return false; }
    static bool Unequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { Equal(__0, __1, ref __result); __result = !__result; return false; }
    static bool Bool(UnityEngine.Object __0, ref bool __result) { __result = !Null(__0); return false; }
    static bool GameObject(Component __instance, ref GameObject __result) {
        if (dead.Contains(__instance)) throw new Exception("Native caller accessed tile GameObject after premature physical destruction");
        __result = objects[__instance]; return false;
    }
    static bool Delete(UnityEngine.Object obj) {
        physicalDestroys++;
        if (Null(obj)) return false;
        dead.Add(obj);
        foreach (var entry in objects) {
            if (!ReferenceEquals(entry.Value, obj)) continue;
            dead.Add(entry.Key);
            if (entry.Key is CondOwner co) { callbacks++; AccessTools.Method(typeof(CondOwner), "OnDestroy").Invoke(co, null); }
        }
        return false;
    }
    static void InitializeCollections(CondOwner co) {
        foreach (var field in typeof(CondOwner).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)) {
            Type t = field.FieldType;
            if (!t.IsGenericType) continue;
            Type def = t.GetGenericTypeDefinition();
            if (def == typeof(List<>) || def == typeof(Dictionary<,>) || def == typeof(HashSet<>) || def == typeof(Queue<>) || def.Name == "TrackingCollection`1")
                field.SetValue(co, Activator.CreateInstance(t, true));
        }
    }
    static CondOwner CO(string id, Ship owner, string type = "TIL", GameObject go = null) {
        var co = Empty<CondOwner>(); InitializeCollections(co);
        Field(co, "_strID", id); Field(co, "_objShip", owner); co.strName = type;
        Field(co, "bTriedGettingPfRef", true); Field(co, "_isHumanOrRobot", (bool?)false);
        co.tf = Empty<Transform>(); co.Clock = new Condowner.Utils.Clock();
        co.mapConds.Add("IsTile", new Condition(null) { strName = "IsTile", fCount = 1, aNext = new List<CondTrigger>(), aPer = new List<string>() });
        DataHandler.mapCOs[id] = co; objects[co] = go ?? Empty<GameObject>();
        return co;
    }
    static Tile Helper(CondOwner co, bool shipTile = true) {
        var tile = Empty<Tile>(); tile.bShipTile = shipTile;
        Field(tile, "_coProps", co); objects[tile] = objects[co];
        tile.aConnectedPowerCOs = new List<Powered>(); tile.aAdjRooms = new List<string>();
        return tile;
    }
    static T Hull<T>(string id) where T : Ship {
        var ship = Empty<T>(); Field(ship, "strRegID", id); ship.gameObject = Empty<GameObject>();
        Field(ship, "mapICOs", new DictionaryArray<string, CondOwner>()); ship.aTiles = new List<Tile>();
        return ship;
    }
    static DictionaryArray<string, CondOwner> Items(Ship ship) => (DictionaryArray<string, CondOwner>)AccessTools.Field(typeof(Ship), "mapICOs").GetValue(ship);
    static void Prefix(Harmony h, Type type, string method, string prefix, Type[] args = null) =>
        h.Patch(AccessTools.Method(type, method, args), prefix: new HarmonyMethod(typeof(Program), prefix));
    static int Main() {
        var h = new Harmony("offline.tile.cleanup");
        try {
            Prefix(h, typeof(UnityEngine.Object), "op_Equality", nameof(Equal));
            Prefix(h, typeof(UnityEngine.Object), "op_Inequality", nameof(Unequal));
            Prefix(h, typeof(UnityEngine.Object), "op_Implicit", nameof(Bool));
            Prefix(h, typeof(UnityEngine.Object), "DestroyImmediate", nameof(Delete), new[] { typeof(UnityEngine.Object) });
            h.Patch(AccessTools.PropertyGetter(typeof(Component), "gameObject"), prefix: new HarmonyMethod(typeof(Program), nameof(GameObject)));
            CrewSim.objInstance = null;
            DataHandler.mapCOs = new Dictionary<string, CondOwner>(); CrewSim.COAnimators = new List<Animator>();
            CrewSim.system = Empty<StarSystem>(); CrewSim.system.dictShips = new Dictionary<string, Ship>();

            var owner = Hull<Ship>("OWNER"); var co = CO("tile", owner); var tile = Helper(co);
            tile.Destroy();
            Check(!co.bDestroyed && DataHandler.mapCOs.ContainsKey("tile"), "Native tile fixture did not reproduce missing cleanup");
            h.CreateClassProcessor(typeof(TileHelperCleanupPatch)).Patch();
            h.CreateClassProcessor(typeof(AsyncShipCleanupPatch)).Patch();
            Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(CondOwner), "Destroy")) == null, "Fix added a global CondOwner.Destroy hook");
            tile = Helper(co); int before = physicalDestroys;
            tile.Destroy();
            Check(co.bDestroyed && co.ship == null && co.mapConds == null && co.aQueue == null, "Native managed cleanup did not clear helper state");
            Check(!DataHandler.mapCOs.ContainsKey("tile") && tile.coProps == null, "Helper registry/tile reference survived");
            Check(physicalDestroys == before && !dead.Contains(tile), "Helper cleanup physically destroyed caller's tile too early");
            Check(!(bool)AccessTools.Field(typeof(Condowner.Utils.Clock), "_isDisposed").GetValue(co.Clock), "Native physical lifecycle was advanced prematurely");
            var go = tile.gameObject; // Exact caller access used by TrimTiles.
            UnityEngine.Object.DestroyImmediate(go);
            Check(callbacks == 1 && dead.Contains(tile), "Native caller could not finish physical destruction");
            Check((bool)AccessTools.Field(typeof(Condowner.Utils.Clock), "_isDisposed").GetValue(co.Clock), "Native OnDestroy did not dispose clock");

            co = CO("repeat", owner); tile = Helper(co); tile.Destroy();
            tile.Destroy(); TileHelperCleanup.Clean(co);
            Check(co.bDestroyed && !DataHandler.mapCOs.ContainsKey("repeat"), "Repeated teardown was not idempotent");
            foreach (string type in new[] { "ItmFloor01", "ItmWall01", "Crew", "Room" }) {
                var item = CO(type, owner, type); var t = Helper(item); t.Destroy();
                Check(!item.bDestroyed && DataHandler.mapCOs[type] == item, "Non-helper CO was destroyed: " + type);
            }
            co = CO("non-ship-tile", owner); tile = Helper(co, false); tile.Destroy();
            Check(!co.bDestroyed, "Non-ship tile helper was destroyed");
            co = CO("cross-object", owner); tile = Helper(co); objects[tile] = Empty<GameObject>(); tile.Destroy();
            Check(!co.bDestroyed, "Tile reference destroyed a CO on another GameObject");
            co = CO("alias", owner); tile = Helper(co); var replacement = CO("alias", owner, "Replacement"); tile.Destroy();
            Check(co.bDestroyed && ReferenceEquals(DataHandler.mapCOs["alias"], replacement), "Old helper unregistered newer same-ID object");

            foreach (Type type in new[] { typeof(BarterZoneShip), typeof(AsyncPreviewShip), typeof(AsyncRatingShip) }) {
                var copy = (Ship)FormatterServices.GetUninitializedObject(type);
                Field(copy, "strRegID", type.Name); copy.gameObject = Empty<GameObject>(); Field(copy, "mapICOs", new DictionaryArray<string, CondOwner>()); copy.aTiles = new List<Tile>();
                var item = CO(type.Name + "item", copy, "Equipment", copy.gameObject); Items(copy)[item.strID] = item;
                var ownedItem = CO(type.Name + "owned-item", copy, "Equipment", copy.gameObject); Items(copy)[ownedItem.strID] = ownedItem;
                var helper = CO(type.Name + "helper", copy, "TIL", copy.gameObject); var t = Helper(helper); copy.aTiles.Add(t);
                var newItem = CO(item.strID, owner, "NewEquipment");
                before = physicalDestroys;
                AccessTools.DeclaredMethod(type, "Destroy", new[] { typeof(bool) }).Invoke(copy, new object[] { true });
                Check(helper.bDestroyed && helper.mapConds == null && helper.ship == null, "Async helper managed cleanup skipped: " + type.Name);
                Check(!DataHandler.mapCOs.ContainsKey(helper.strID), "Async helper remained registered: " + type.Name);
                Check(ReferenceEquals(DataHandler.mapCOs[item.strID], newItem), "Async destruction unregistered newer same-ID object: " + type.Name);
                Check(!DataHandler.mapCOs.ContainsKey(ownedItem.strID), "Async destruction retained its own mapped item: " + type.Name);
                Check(physicalDestroys == before + 1 && dead.Contains(t), "Async hierarchy destruction changed: " + type.Name);
            }
            var sameIdOwner = Hull<BarterZoneShip>("same-ID"); var own = CO("owned", sameIdOwner, "Item"); Items(sameIdOwner)[own.strID] = own;
            Check(TileHelperCleanup.RemoveOwned(DataHandler.mapCOs, own.strID, sameIdOwner) && !DataHandler.mapCOs.ContainsKey(own.strID), "Owned item registry removal failed");
            own = CO("transferred", sameIdOwner, "Item"); Items(sameIdOwner)[own.strID] = own; Field(own, "_objShip", owner);
            Check(!TileHelperCleanup.RemoveOwned(DataHandler.mapCOs, own.strID, sameIdOwner) && DataHandler.mapCOs.ContainsKey(own.strID), "Transferred shared object was unregistered");

            var preview = Hull<AsyncPreviewShip>("live-preview"); var live = Hull<Ship>("live-preview");
            Field(live, "nLoadState", Ship.Loaded.Full); CrewSim.system.dictShips["live-preview"] = live;
            co = CO("preserved", preview); tile = Helper(co); preview.aTiles.Add(tile);
            before = physicalDestroys; preview.Destroy();
            Check(!co.bDestroyed && physicalDestroys == before && DataHandler.mapCOs.ContainsKey("preserved"), "Native preview early-return policy was changed");
            var foreign = CO("foreign", owner); var foreignTile = Helper(foreign); var foreignCopy = Hull<BarterZoneShip>("foreign-copy"); foreignCopy.aTiles.Add(foreignTile);
            TileHelperCleanup.DestroyAsyncHierarchy(foreignCopy.gameObject, foreignCopy);
            Check(!foreign.bDestroyed && DataHandler.mapCOs.ContainsKey("foreign"), "Async cleanup followed a foreign tile owner");

            int baseline = DataHandler.mapCOs.Count;
            for (int i = 0; i < 1000; i++) {
                var helper = CO("stress-" + i, owner); var t = Helper(helper);
                t.Destroy();
                Check(helper.bDestroyed && helper.ship == null && helper.mapConds == null, "Repeated helper cycle retained managed state");
                UnityEngine.Object.DestroyImmediate(t.gameObject);
            }
            Check(DataHandler.mapCOs.Count == baseline, "Repeated helper lifecycles accumulated registry entries");

            Console.WriteLine(checks + " native tile-helper lifecycle/physical-order/async/ID-safety checks passed.");
            return 0;
        } finally { h.UnpatchSelf(); }
    }
}
