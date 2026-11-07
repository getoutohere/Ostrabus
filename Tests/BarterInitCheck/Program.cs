using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using HarmonyLib;
using Ostrabus.Optimizations;
using Ostranauts.Core.Models;
using Ostranauts.Ships;
using UnityEngine;

static class Program {
    sealed class RefComparer : IEqualityComparer<UnityEngine.Object> {
        public bool Equals(UnityEngine.Object a, UnityEngine.Object b) => ReferenceEquals(a, b);
        public int GetHashCode(UnityEngine.Object value) => RuntimeHelpers.GetHashCode(value);
    }
    static readonly Dictionary<UnityEngine.Object, GameObject> objects = new Dictionary<UnityEngine.Object, GameObject>(new RefComparer());
    static readonly Dictionary<UnityEngine.Object, Transform> transforms = new Dictionary<UnityEngine.Object, Transform>(new RefComparer());
    static readonly Dictionary<UnityEngine.Object, CondOwner> components = new Dictionary<UnityEngine.Object, CondOwner>(new RefComparer());
    static readonly List<string> events = new List<string>();
    static int checks, steps, frame, polls, quota = 3;
    static bool failPost;
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Check(bool ok, string why) { checks++; if (!ok) throw new Exception(why); }
    static DictionaryArray<string, CondOwner> Items(Ship ship) => (DictionaryArray<string, CondOwner>)AccessTools.Field(typeof(Ship), "mapICOs").GetValue(ship);
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = ReferenceEquals(__0, __1); return false; }
    static bool Unequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = !ReferenceEquals(__0, __1); return false; }
    static bool Shader(ref int __result) { __result = 1; return false; }
    static bool Noop() => false;
    static bool Zero(ref int __result) { __result = 0; return false; }
    static bool Frame(ref int __result) { __result = frame; return false; }
    static bool Budget(ref bool __result) { __result = ++polls % quota == 0; return false; }
    static bool GetGO(Component __instance, ref GameObject __result) { __result = objects[__instance]; return false; }
    static bool GetTF(GameObject __instance, ref Transform __result) { __result = transforms[__instance]; return false; }
    static bool Component(GameObject __instance, ref CondOwner __result) { __result = components[__instance]; return false; }
    static bool Position(ref Vector3 __result) { __result = Vector3.zero; return false; }
    static bool Create(JsonItem __0, bool __2, ref GameObject __result) {
        events.Add("create:" + __0.strID + ":" + __2);
        if (__0.strName == "fail") { __result = null; return false; }
        var go = Empty<GameObject>(); var co = Empty<CondOwner>(); co.tf = Empty<Transform>();
        AccessTools.Field(typeof(CondOwner), "_strID").SetValue(co, __0.strID);
        if (__0.strName == "slots") AccessTools.Field(typeof(CondOwner), "_compSlots").SetValue(co, Empty<Slots>());
        else { co.objContainer = Empty<Container>(); co.objContainer.bAllowStacking = true; }
        if (__0.strSlotParentID != null) co.jCOS = new JsonCondOwnerSave { strSlotName = "heldR" };
        objects[co] = go; transforms[go] = co.tf; components[go] = co;
        __result = go; return false;
    }
    static bool Add(BarterZoneShip __instance, CondOwner __0) { Items(__instance)[__0.strID] = __0; events.Add("root:" + __0.strID); return false; }
    static bool Contains(ref bool __result) { __result = false; return false; }
    static bool Slot(string __0, CondOwner __1, ref bool __result) {
        events.Add("slot:" + __1.strID + ":" + __0);
        __result = __1.strID != "reject"; return false;
    }
    static bool Contain(Container __instance, CondOwner __0) {
        Check(!__instance.bAllowStacking, "Stacking was enabled while restoring saved contents");
        events.Add("contain:" + __0.strID); return false;
    }
    static bool Post(CondOwner __instance, Ship.Loaded __0) {
        Check(__0 == Ship.Loaded.Shallow, "Wrong post-load mode");
        if (failPost) throw new InvalidOperationException("fixture failure");
        events.Add("post:" + __instance.strID); return false;
    }
    static bool Zones() { events.Add("zones"); return false; }
    static bool Move() { events.Add("move"); return false; }
    static BarterZoneShip Hull(JsonItem[] saved) {
        events.Clear(); objects.Clear(); transforms.Clear(); components.Clear(); polls = steps = 0;
        var ship = Empty<BarterZoneShip>(); ship.json = new JsonShip { aItems = saved, aZones = Array.Empty<JsonZone>() };
        ship.gameObject = Empty<GameObject>(); ship.aTiles = new List<Tile>();
        AccessTools.Field(typeof(Ship), "mapICOs").SetValue(ship, new DictionaryArray<string, CondOwner>());
        return ship;
    }
    static string[] Run(BarterZoneShip ship) {
        var iterator = ship.Init(100);
        try {
            while (iterator.MoveNext()) {
                Check(!ship.FullyLoaded, "Published readiness before the final step");
                frame++; if (++steps > 100000) throw new Exception("Loader failed to finish");
            }
        } finally { (iterator as IDisposable)?.Dispose(); }
        Check(ship.FullyLoaded, "Final readiness was not published");
        Check(Items(ship).Values.All(co => ReferenceEquals(co.objContainer, null) || co.objContainer.bAllowStacking), "Stacking setting was not restored");
        return events.ToArray();
    }
    static void Prefix(Harmony h, Type type, string name, string method, Type[] args = null) => h.Patch(AccessTools.Method(type, name, args), prefix: new HarmonyMethod(typeof(Program), method));
    static void Main() {
        var fixture = new Harmony("offline.barter.init.fixture"); var production = new Harmony("offline.barter.init.production");
        try {
            Prefix(fixture, typeof(UnityEngine.Object), "op_Equality", nameof(Equal));
            Prefix(fixture, typeof(UnityEngine.Object), "op_Inequality", nameof(Unequal));
            Prefix(fixture, typeof(UnityEngine.Shader), "PropertyToID", nameof(Shader), new[] { typeof(string) });
            Prefix(fixture, typeof(GameObject), "SetActive", nameof(Noop));
            Prefix(fixture, typeof(GameObject), "set_layer", nameof(Noop));
            Prefix(fixture, typeof(GameObject), "get_transform", nameof(GetTF));
            Prefix(fixture, typeof(Component), "get_gameObject", nameof(GetGO));
            Prefix(fixture, typeof(Component), "get_transform", nameof(GetComponentTF));
            var generic = typeof(GameObject).GetMethods().Single(m => m.Name == "GetComponent" && m.IsGenericMethod && m.GetParameters().Length == 0).MakeGenericMethod(typeof(CondOwner));
            fixture.Patch(generic, prefix: new HarmonyMethod(typeof(Program), nameof(Component)));
            Prefix(fixture, typeof(Transform), "get_childCount", nameof(Zero));
            Prefix(fixture, typeof(Transform), "get_position", nameof(Position));
            Prefix(fixture, typeof(Transform), "set_localPosition", nameof(Noop));
            Prefix(fixture, typeof(LayerMask), "NameToLayer", nameof(Zero));
            Prefix(fixture, typeof(Time), "get_frameCount", nameof(Frame));
            Prefix(fixture, typeof(Ship), "CreatePart", nameof(Create));
            Prefix(fixture, typeof(BarterZoneShip), "AddCO", nameof(Add));
            Prefix(fixture, typeof(Container), "Contains", nameof(Contains));
            Prefix(fixture, typeof(Container), "AddCOSimple", nameof(Contain));
            Prefix(fixture, typeof(Slots), "SlotItem", nameof(Slot), new[] { typeof(string), typeof(CondOwner), typeof(bool) });
            Prefix(fixture, typeof(CondOwner), "PostGameLoad", nameof(Post));
            Prefix(fixture, typeof(Ship), "SetZoneData", nameof(Zones));
            Prefix(fixture, typeof(Ship), "MoveShip", nameof(Move), new[] { typeof(Vector2) });
            // Exercise the real shared clock before substituting a deterministic quota.
            frame++; Check(!BarterFrameBudget.Exhausted(), "New frame did not receive budget");
            System.Threading.Thread.Sleep(8);
            Check(BarterFrameBudget.Exhausted() && BarterFrameBudget.Exhausted(), "Budget reset for another loader in the same frame");
            frame++; Check(!BarterFrameBudget.Exhausted(), "New frame did not reset the budget");
            Prefix(fixture, typeof(BarterFrameBudget), "Exhausted", nameof(Budget));
            var cases = new[] {
                Array.Empty<JsonItem>(),
                new[] { new JsonItem { strID = "child", strParentID = "bag" }, new JsonItem { strID = "root" }, new JsonItem { strID = "bag", strParentID = "root" }, new JsonItem { strID = "orphan", strParentID = "missing" } },
                new[] { new JsonItem { strID = "fail", strName = "fail" }, new JsonItem { strID = "root" }, new JsonItem { strID = "child", strParentID = "root", strName = "fail" } },
                Enumerable.Range(0, 300).Select(i => new JsonItem { strID = "part" + i, strParentID = i == 0 ? null : "part" + (i - 1) }).ToArray(),
                new[] { new JsonItem { strID = "root", strName = "slots" }, new JsonItem { strID = "accepted", strSlotParentID = "root" }, new JsonItem { strID = "reject", strSlotParentID = "root" } }
            };
            var expected = cases.Select(saved => Run(Hull(saved))).ToArray();
            production.CreateClassProcessor(typeof(BarterLocalIndexPatch)).Patch();
            production.CreateClassProcessor(typeof(BarterInitPatch)).Patch();
            for (int i = 0; i < cases.Length; i++) {
                var ship = Hull(cases[i]); var actual = Run(ship);
                Check(actual.SequenceEqual(expected[i]), "Native call order/output changed for case " + i);
                Check(ReferenceEquals(ship.json.aItems, cases[i]), "Existing JSON records were changed");
                if (i == 3) Check(steps > 100, "Large nested inventory did not yield during inner work");
            }
            var cancelled = Hull(cases[3]); var pending = cancelled.Init(100); pending.MoveNext(); (pending as IDisposable)?.Dispose();
            Check(!cancelled.FullyLoaded && !events.Contains("move"), "Cancelled loader finalized its copy");
            cancelled = Hull(cases[3]); pending = cancelled.Init(100);
            while (!events.Any(e => e.StartsWith("contain:"))) { Check(pending.MoveNext(), "No inner yield before completion"); frame++; }
            int before = events.Count; (pending as IDisposable)?.Dispose();
            Check(!cancelled.FullyLoaded && events.Count == before && !events.Contains("move"), "Cancellation inside reconstruction ran finalization");
            var broken = Hull(cases[1]); failPost = true;
            try { Run(broken); throw new Exception("Exception swallowed"); }
            catch (InvalidOperationException error) { Check(error.Message == "fixture failure" && !broken.FullyLoaded, "Failure published readiness or changed exception"); }
            Console.WriteLine(checks + " native/budgeted barter initialization checks passed.");
        } finally { production.UnpatchSelf(); fixture.UnpatchSelf(); }
    }
    static bool GetComponentTF(Component __instance, ref Transform __result) { __result = transforms[objects[__instance]]; return false; }
}
