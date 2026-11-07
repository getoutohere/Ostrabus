using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using HarmonyLib;
using Newtonsoft.Json;
using Ostrabus.Optimizations;
using Ostranauts.Core.Models;
using Ostranauts.Ships;
using UnityEngine;

static partial class Program {
    sealed class RefComparer<T> : IEqualityComparer<T> where T : class {
        public bool Equals(T a, T b) => ReferenceEquals(a, b);
        public int GetHashCode(T value) => RuntimeHelpers.GetHashCode(value);
    }
    static readonly Dictionary<Transform, Vector3> positions = new Dictionary<Transform, Vector3>(new RefComparer<Transform>());
    static readonly List<string> events = new List<string>();
    static readonly AccessTools.FieldRef<Ship, DictionaryArray<string, CondOwner>> Items = AccessTools.FieldRefAccess<Ship, DictionaryArray<string, CondOwner>>("mapICOs");
    static int checks;
    static Action<CondOwner> onSerialize;
    static string failAt;
    static bool allocationCounter;
    static bool HasAllocationCounter() {
        long before = GC.GetAllocatedBytesForCurrentThread(); GC.KeepAlive(new byte[1024]);
        return GC.GetAllocatedBytesForCurrentThread() > before;
    }
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Field(object obj, string name, object value) => AccessTools.Field(obj.GetType(), name).SetValue(obj, value);
    static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
    sealed class VectorConverter : JsonConverter {
        public override bool CanConvert(Type type) => type == typeof(Vector2) || type == typeof(Vector3);
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) {
            writer.WriteStartArray();
            if (value is Vector3 v) { writer.WriteValue(v.x); writer.WriteValue(v.y); writer.WriteValue(v.z); }
            else { var p = (Vector2)value; writer.WriteValue(p.x); writer.WriteValue(p.y); }
            writer.WriteEndArray();
        }
        public override object ReadJson(JsonReader reader, Type type, object existing, JsonSerializer serializer) => throw new NotSupportedException();
    }
    static readonly JsonSerializerSettings jsonSettings = new JsonSerializerSettings { Converters = new List<JsonConverter> { new VectorConverter() } };
    static string Json(object value) => JsonConvert.SerializeObject(value, jsonSettings);
    static bool Shader(ref int __result) { __result = 1; return false; }
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = ReferenceEquals(__0, __1); return false; }
    static bool Unequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = !ReferenceEquals(__0, __1); return false; }
    static bool GetPosition(Transform __instance, ref Vector3 __result) { __result = positions[__instance]; return false; }
    static bool SetPosition(Transform __instance, Vector3 __0) { positions[__instance] = __0; return false; }
    static bool SerializeItem(CondOwner co, ref JsonItem __result) {
        events.Add("item:" + co.strID); onSerialize?.Invoke(co);
        if (failAt == co.strID) throw new InvalidOperationException("fixture failure");
        var p = positions[co.tf];
        __result = new JsonItem { strID = co.strID, strName = co.strName, fX = p.x, fY = p.y }; return false;
    }
    static bool SerializeCO(CondOwner __instance, ref JsonCondOwnerSave __result) {
        events.Add("co:" + __instance.strID);
        if (saveFailure == __instance.strID) throw new InvalidOperationException("fixture save failure");
        __result = __instance.strName == "null-save" ? null : new JsonCondOwnerSave { strID = __instance.strID, strCODef = __instance.strName }; return false;
    }
    static CondOwner CO(string id, string name = null, Ship ship = null) {
        var co = Empty<CondOwner>(); Field(co, "_strID", id); Field(co, "_objShip", ship);
        co.strName = name ?? id; co.tf = Empty<Transform>(); positions[co.tf] = new Vector3(1234, 5678, 9);
        co.mapGUIPropMaps = new Dictionary<string, Dictionary<string, string>>(); co.aLot = new TrackingCollection<CondOwner>();
        return co;
    }
    static BarterZoneShip Copy(JsonItem[] records, params string[] keys) {
        var ship = Empty<BarterZoneShip>(); ship.json = new JsonShip { aItems = records };
        Items(ship) = new DictionaryArray<string, CondOwner>();
        foreach (string key in keys) Items(ship)[key] = CO(key);
        return ship;
    }
    static void Reset() { events.Clear(); positions.Clear(); failAt = null; onSerialize = null; DataHandler.dictCOSaves = new Dictionary<string, JsonCondOwnerSave>(); }
    static string WriteCase(int variant) {
        Reset();
        var source = new[] { new JsonItem { strID = "a" }, new JsonItem { strID = "a" }, new JsonItem { strID = null } };
        Ship original = Empty<Ship>(); original.json = new JsonShip { aItems = source };
        var copy = Copy(new[] { new JsonItem { strID = "a", strName = "copy" } }, "a", "A", "new", "null-save");
        Items(copy)["null-save"].strName = "null-save";
        if (variant == 5) { copy = Copy(Array.Empty<JsonItem>()); original.json.aItems = null; }
        if (variant == 6) { copy = Copy(Array.Empty<JsonItem>()); original.json.aItems = new JsonItem[] { null }; }
        if (variant == 7) original.json.aItems = null;
        if (variant == 8) original.json.aItems = new JsonItem[] { source[0], null };
        if (variant == 9) copy.json.aItems = null;
        if (variant == 1) Items(copy)["new"].mapGUIPropMaps.Add(Ostranauts.ShipGUIs.Trade.GUITradeBase.ASYNCIDENTIFIER, new Dictionary<string, string>());
        if (variant == 2) failAt = "new";
        if (variant == 3) {
            // Key matching is deliberately different from the CO ID.
            Items(copy)["alias"] = CO("new");
        }
        if (variant == 4) {
            bool nested = false;
            onSerialize = co => {
                if (nested) return; nested = true;
                Ship other = Empty<Ship>(); other.json = new JsonShip { aItems = new[] { new JsonItem { strID = "nested" } } };
                Copy(other.json.aItems, "nested").SaveChangedCOs(ref other);
                original.json.aItems = new[] { new JsonItem { strID = "a" }, new JsonItem { strID = "new" } };
            };
        }
        string error = null;
        try { copy.SaveChangedCOs(ref original); } catch (Exception ex) { error = ex.GetType().Name + ":" + ex.Message; }
        var coordinates = Items(copy).Values.Select(co => new { id = co.strID, p = positions[co.tf], maps = co.mapGUIPropMaps.Keys.ToArray() }).ToArray();
        return Json(new { error, source = original.json.aItems, copy = copy.json.aItems, coordinates, calls = events.ToArray(), saves = DataHandler.dictCOSaves.Values.ToArray(), shared = ReferenceEquals(original.json.aItems, copy.json.aItems) });
    }
    static void WriteChecks(Harmony production) {
        var expected = Enumerable.Range(0, 10).Select(WriteCase).ToArray();
        var saved = Enumerable.Range(0, 8000).Select(i => new JsonItem { strID = "item-" + i.ToString("D8") }).ToArray();
        Reset(); Ship original = Empty<Ship>(); original.json = new JsonShip { aItems = saved };
        var copy = Copy(saved, saved.Select(i => i.strID).ToArray());
        copy.SaveChangedCOs(ref original);
        long before = GC.GetAllocatedBytesForCurrentThread(); var timer = Stopwatch.StartNew();
        copy.SaveChangedCOs(ref original); timer.Stop();
        long nativeBytes = GC.GetAllocatedBytesForCurrentThread() - before; double nativeMs = timer.Elapsed.TotalMilliseconds;
        production.CreateClassProcessor(typeof(BarterWritebackPatch)).Patch();
        for (int i = 0; i < expected.Length; i++) Check(WriteCase(i) == expected[i], "Write-back differs from native in case " + i);
        Reset(); original = Empty<Ship>(); original.json = new JsonShip { aItems = saved }; copy = Copy(saved, saved.Select(i => i.strID).ToArray());
        copy.SaveChangedCOs(ref original);
        before = GC.GetAllocatedBytesForCurrentThread(); timer.Restart(); copy.SaveChangedCOs(ref original); timer.Stop();
        long patchedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Console.WriteLine("Write-back 8,000 matched items: native " + nativeMs.ToString("F2") + " ms; patched " + timer.Elapsed.TotalMilliseconds.ToString("F2") + " ms. Allocation counter available: " + allocationCounter);
        if (allocationCounter) Check(patchedBytes < nativeBytes / 2, "Write-back did not materially reduce allocations");
        var first = BarterWritebackScratch.Rent(); first.Index(saved); BarterWritebackScratch.Release(first);
        Check(first.Source == null && first.Ids.Count == 0 && first.Added.Count == 0, "Write-back pool retains records");
        var outer = BarterWritebackScratch.Rent(); var inner = BarterWritebackScratch.Rent();
        Check(!ReferenceEquals(outer, inner), "Reentrant write-back shares scratch");
        BarterWritebackScratch.Release(inner); BarterWritebackScratch.Release(outer);
        var huge = BarterWritebackScratch.Rent(); huge.Peak = BarterWritebackScratch.RetainedLimit + 1; BarterWritebackScratch.Release(huge);
        var next = BarterWritebackScratch.Rent(); Check(!ReferenceEquals(next, huge), "Oversized scratch retained"); BarterWritebackScratch.Release(next);
    }
    static void Prefix(Harmony h, Type type, string method, string prefix, Type[] args = null) => h.Patch(AccessTools.Method(type, method, args), prefix: new HarmonyMethod(typeof(Program), prefix));
    static void Main(string[] args) {
        allocationCounter = HasAllocationCounter();
        if (args.Contains("--allocations-only")) { AllocationChecks(); return; }
        var fixture = new Harmony("offline.inventory.performance.fixture"); var production = new Harmony("offline.inventory.performance.production");
        try {
            Prefix(fixture, typeof(Shader), "PropertyToID", nameof(Shader), new[] { typeof(string) });
            Prefix(fixture, typeof(UnityEngine.Object), "op_Equality", nameof(Equal)); Prefix(fixture, typeof(UnityEngine.Object), "op_Inequality", nameof(Unequal));
            Prefix(fixture, typeof(Transform), "get_position", nameof(GetPosition)); Prefix(fixture, typeof(Transform), "set_position", nameof(SetPosition));
            Prefix(fixture, typeof(Ship), "GetJsonItem", nameof(SerializeItem)); Prefix(fixture, typeof(CondOwner), "GetJSONSave", nameof(SerializeCO));
            WriteChecks(production); RemainingChecks(production, fixture); RemovalChecks(production, fixture);
            Console.WriteLine(checks + " inventory performance equivalence/lifecycle/allocation checks passed.");
        } finally { production.UnpatchSelf(); fixture.UnpatchSelf(); }
    }
}
