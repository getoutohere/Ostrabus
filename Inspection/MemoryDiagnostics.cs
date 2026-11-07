using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.Core.Models;
using Ostranauts.Ships;
using UnityEngine;
using UnityEngine.Profiling;

namespace Ostrabus.Inspection;

// No startup registration, Update polling or strong references to game objects.
internal static class MemoryDiagnostics {
    const int MaxEvents = 256, MaxGroups = 128, MaxSamples = 512, MaxLogEvents = 1024;
    static readonly object gate = new object();
    static readonly Queue<Dictionary<string, object>> events = new Queue<Dictionary<string, object>>();
    static readonly List<Sample> samples = new List<Sample>();
    static readonly Dictionary<string, int> unmanagedTypes = new Dictionary<string, int>();
    static readonly System.Reflection.FieldInfo AsyncInstance = AccessTools.Field(typeof(MonoSingleton<AsyncShipLoader>), "_instance");
    static readonly AccessTools.FieldRef<AsyncShipLoader, Dictionary<string, IAsyncLoadable>> AsyncShips =
        AccessTools.FieldRefAccess<AsyncShipLoader, Dictionary<string, IAsyncLoadable>>("_loadStateDict");
    static readonly AccessTools.FieldRef<Ship, DictionaryArray<string, CondOwner>> Items = AccessTools.FieldRefAccess<Ship, DictionaryArray<string, CondOwner>>("mapICOs");
    internal static int? ItemCount(Ship ship) => ship == null ? null : Items(ship)?.Count;
    static long sequence;
    static int logged, errors;
    internal static long ManagedDestroyCalls, UnityDestroyCalls, WithoutManagedCleanup, RegisteredAtUnityDestroy, TileDestroyCalls;
    sealed class Sample {
        internal WeakReference Reference;
        internal string Id, Type, Owner, Origin;
    }
    internal static bool Active => MemoryDiagnosticHooks.Installed;
    static Dictionary<string, object> Map(params object[] pairs) => SnapshotJson.Map(pairs);
    internal static object Metrics(bool process = false) {
        var result = Map("managedUsedBytes", GC.GetTotalMemory(false),
            "monoUsedBytes", Profiler.GetMonoUsedSizeLong(), "monoReservedBytes", Profiler.GetMonoHeapSizeLong(),
            "unityAllocatedBytes", Profiler.GetTotalAllocatedMemoryLong(), "unityReservedBytes", Profiler.GetTotalReservedMemoryLong(),
            "unityUnusedReservedBytes", Profiler.GetTotalUnusedReservedMemoryLong(),
            "gcCollections", new[] { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) },
            "registeredCOs", DataHandler.mapCOs?.Count, "debugCOCount", DataHandler.debugCOCount,
            "worldShips", CrewSim.system?.dictShips?.Count, "sceneShips", CrewSim.GetAllLoadedShips()?.Count,
            "animatorEntries", CrewSim.COAnimators?.Count);
        if (process) using (var p = Process.GetCurrentProcess()) {
            p.Refresh(); result["processPrivateBytes"] = p.PrivateMemorySize64; result["processWorkingSetBytes"] = p.WorkingSet64;
        }
        return result;
    }
    static void Count(Dictionary<string, int> groups, string key) {
        key ??= "<null>";
        if (groups.TryGetValue(key, out int count)) groups[key] = count + 1;
        else if (groups.Count < MaxGroups) groups.Add(key, 1);
        else { groups.TryGetValue("<other>", out count); groups["<other>"] = count + 1; }
    }
    static object Groups(Dictionary<string, int> groups) => groups.OrderByDescending(p => p.Value).Take(32)
        .Select(p => (object)Map("key", p.Key, "count", p.Value)).ToArray();
    static string OwnerKind(Ship ship) {
        if (ship == null) return "unowned";
        if (ship.bDestroyed) return "destroyed-ship";
        if (ship is BarterZoneShip || ship is AsyncPreviewShip || ship is AsyncRatingShip) return "async-copy";
        if (ship.strRegID != null && CrewSim.system?.dictShips != null && CrewSim.system.dictShips.TryGetValue(ship.strRegID, out var original) && ReferenceEquals(original, ship)) return "world-ship";
        return "outside-world-registry";
    }
    internal static object Census() {
        var types = new Dictionary<string, int>(); var owners = new Dictionary<string, int>(); var kinds = new Dictionary<string, int>();
        var examples = new List<object>();
        int missing = 0, unityDead = 0, markedDestroyed = 0, scanned = 0;
        if (DataHandler.mapCOs != null) foreach (var pair in DataHandler.mapCOs) {
            var co = pair.Value; scanned++;
            if (ReferenceEquals(co, null)) { missing++; continue; }
            if (co == null) unityDead++;
            if (co.bDestroyed) markedDestroyed++;
            string kind = OwnerKind(co.ship);
            Count(types, co.strName); Count(owners, co.ship?.strRegID); Count(kinds, kind);
            if (examples.Count < 32 && (kind != "world-ship" || co == null || co.bDestroyed)) {
                var owner = co.ship;
                bool mapped = owner != null && Items(owner) != null && co.strID != null && Items(owner).TryGetValue(co.strID, out var found) && ReferenceEquals(found, co);
                examples.Add(Map("id", co.strID, "type", co.strName, "unityAlive", co != null, "managedDestroyed", co.bDestroyed,
                    "ownerId", owner?.strRegID, "ownerKind", kind, "ownerType", owner?.GetType().Name,
                    "ownerReference", owner == null ? null : (object)RuntimeHelpers.GetHashCode(owner),
                    "ownerDestroyed", owner?.bDestroyed, "mappedByOwner", mapped, "parentId", co.objCOParent?.strID));
            }
        }
        int deadAnimators = 0;
        if (CrewSim.COAnimators != null) foreach (var animator in CrewSim.COAnimators) if (animator == null) deadAnimators++;
        var async = AsyncState();
        int cachedImages = 0;
        if (DataHandler.dictShipImages != null) foreach (var images in DataHandler.dictShipImages.Values) cachedImages += images?.Count ?? 0;
        return Map("scanned", scanned, "nullEntries", missing, "unityDestroyedWrappers", unityDead,
            "markedDestroyed", markedDestroyed, "types", Groups(types), "owners", Groups(owners), "ownerKinds", Groups(kinds),
            "groupLimit", MaxGroups, "topGroupsReturned", 32, "deadAnimatorEntries", deadAnimators,
            "detachedExamples", examples, "savedCOs", DataHandler.dictCOSaves?.Count,
            "cachedShipImageGroups", DataHandler.dictShipImages?.Count, "cachedShipImages", cachedImages,
            "asyncLoader", async, "weakSamples", SampleState());
    }
    internal static object AsyncState() {
        // Do not access MonoSingleton.Instance: an inspection must not create it.
        var loader = (AsyncShipLoader)AsyncInstance.GetValue(null);
        if (ReferenceEquals(loader, null)) return Map("managedInstance", false, "unityInstance", false, "count", 0);
        var ships = AsyncShips(loader);
        var result = new List<object>();
        if (ships != null) foreach (var pair in ships) {
            if (result.Count >= 32) break;
            var ship = pair.Value as Ship;
            result.Add(Map("id", pair.Key, "type", pair.Value?.GetType().Name, "fullyLoaded", pair.Value?.FullyLoaded,
                "destroyed", ship?.bDestroyed, "mappedCOs", ItemCount(ship), "tiles", ship?.aTiles?.Count,
                "jsonItems", pair.Value?.json?.aItems?.Length));
        }
        return Map("managedInstance", true, "unityInstance", loader != null, "count", ships?.Count, "ships", result, "truncated", ships?.Count > result.Count);
    }
    internal static void SampleCO(CondOwner co, string origin) {
        if (samples.Count >= MaxSamples || ReferenceEquals(co, null)) return;
        samples.Add(new Sample { Reference = new WeakReference(co), Id = co.strID, Type = co.strName, Owner = co.ship?.strRegID, Origin = origin });
    }
    internal static void SampleShip(Ship ship) {
        if (ship == null || ship.LoadState < Ship.Loaded.Edit || samples.Count >= MaxSamples) return;
        int count = 0;
        if (Items(ship) != null) foreach (var co in Items(ship).Values) { SampleCO(co, "ship-destroy-item"); if (++count >= 16) break; }
        if (ship.aTiles != null && ship.aTiles.Count > 0) {
            int stride = Math.Max(1, ship.aTiles.Count / 16);
            for (int i = 0; i < ship.aTiles.Count && count < 32; i += stride, count++) SampleCO(ship.aTiles[i]?.coProps, "ship-destroy-tile");
        }
    }
    static object SampleState() {
        int collected = 0, wrappers = 0, nativeAlive = 0, registered = 0;
        var retained = new List<object>();
        foreach (var sample in samples) {
            var co = sample.Reference.Target as CondOwner;
            if (ReferenceEquals(co, null)) { collected++; continue; }
            bool alive = co != null;
            if (alive) nativeAlive++; else wrappers++;
            bool inRegistry = sample.Id != null && DataHandler.mapCOs != null && DataHandler.mapCOs.TryGetValue(sample.Id, out var found) && ReferenceEquals(found, co);
            if (inRegistry) registered++;
            if (retained.Count < 32) retained.Add(Map("id", sample.Id, "type", sample.Type, "originalOwner", sample.Owner,
                "origin", sample.Origin, "unityAlive", alive, "managedDestroyed", co.bDestroyed,
                "registeredSameObject", inRegistry, "currentOwner", co.ship?.strRegID, "ownerDestroyed", co.ship?.bDestroyed,
                "conditions", co.mapConds?.Count, "queuedInteractions", co.aQueue?.Count));
        }
        return Map("count", samples.Count, "capacity", MaxSamples, "collected", collected, "destroyedWrappersRetained", wrappers,
            "nativeAlive", nativeAlive, "registeredSameObject", registered, "examples", retained,
            "note", "Weak references only. Surviving an observation is not proof of a leak; no collection is forced.");
    }
    internal static object Assets() {
        var objects = Resources.FindObjectsOfTypeAll<UnityEngine.Object>();
        var types = new Dictionary<string, int>(); var largest = new List<Dictionary<string, object>>();
        long textureBytes = 0, meshBytes = 0, materialBytes = 0;
        int cos = 0;
        foreach (var obj in objects) {
            if (obj == null) continue;
            Count(types, obj.GetType().FullName);
            if (obj is CondOwner) cos++;
            if (!(obj is Texture) && !(obj is Mesh) && !(obj is Material)) continue;
            long bytes = Profiler.GetRuntimeMemorySizeLong(obj);
            if (obj is Texture) textureBytes += bytes;
            else if (obj is Mesh) meshBytes += bytes;
            else materialBytes += bytes;
            if (largest.Count == 16 && bytes <= (long)largest[15]["bytes"]) continue;
            largest.Add(Map("type", obj.GetType().Name, "name", obj.name, "bytes", bytes));
            largest.Sort((a, b) => ((long)b["bytes"]).CompareTo((long)a["bytes"]));
            if (largest.Count > 16) largest.RemoveAt(16);
        }
        return Map("unityObjects", objects.Length, "unityCOComponents", cos, "types", Groups(types), "textureBytes", textureBytes,
            "meshBytes", meshBytes, "materialBytes", materialBytes, "largestAssets", largest,
            "note", "Explicit expensive scan, including loaded assets/inactive objects; estimates are not a complete process-memory accounting. Temporary arrays are released after this call.");
    }
    internal static void Record(string kind, object detail, bool census = false, bool process = false) {
        var record = Map("utc", DateTime.UtcNow.ToString("O"), "epoch", StarSystem.fEpoch, "kind", kind,
            "metrics", Metrics(process), "detail", detail,
            "managedDestroyCalls", ManagedDestroyCalls, "unityDestroyCalls", UnityDestroyCalls,
            "withoutManagedCleanup", WithoutManagedCleanup, "registeredAtUnityDestroy", RegisteredAtUnityDestroy,
            "tileDestroyCalls", TileDestroyCalls, "diagnosticErrors", errors);
        if (census) record["census"] = Census();
        lock (gate) { record["sequence"] = ++sequence; events.Enqueue(record); while (events.Count > MaxEvents) events.Dequeue(); }
        if (logged++ < MaxLogEvents) OstrabusPlugin.Logger?.LogInfo("[MemoryDiagnostics] " + SnapshotJson.Encode(record));
    }
    internal static void UnityDestroyed(CondOwner co) {
        UnityDestroyCalls++;
        if (co.bDestroyed) return;
        WithoutManagedCleanup++; Count(unmanagedTypes, co.strName);
        if (co.strID != null && DataHandler.mapCOs != null && DataHandler.mapCOs.TryGetValue(co.strID, out var found) && ReferenceEquals(found, co)) {
            RegisteredAtUnityDestroy++; SampleCO(co, "unity-destroy-without-managed-cleanup");
        }
    }
    internal static void Failure(Exception ex) {
        errors++;
        if (errors <= 4) OstrabusPlugin.Logger?.LogWarning("[MemoryDiagnostics] Diagnostic failed: " + ex);
    }
    internal static object Read(long since, int limit) {
        lock (gate) return Map("active", Active, "latestSequence", sequence, "capacity", MaxEvents,
            "oldestSequence", events.Count == 0 ? null : events.Peek()["sequence"],
            "items", (since == 0 ? events.Reverse().Take(limit).Reverse() : events.Where(e => (long)e["sequence"] > since).Take(limit)).ToArray(),
            "note", "Detached bounded records; no Unity scan or GC occurs on GET. Trace survives transit/save reload until explicitly stopped or the plugin exits.");
    }
    internal static void Start() {
        if (Active) return;
        lock (gate) { events.Clear(); sequence = 0; }
        samples.Clear(); unmanagedTypes.Clear(); errors = logged = 0;
        ManagedDestroyCalls = UnityDestroyCalls = WithoutManagedCleanup = RegisteredAtUnityDestroy = TileDestroyCalls = 0;
        MemoryDiagnosticHooks.Install();
        try { Record("start", null, census: true, process: true); }
        catch { MemoryDiagnosticHooks.Remove(); samples.Clear(); throw; }
    }
    internal static void Snapshot(bool assets = false) {
        Record(assets ? "assets" : "snapshot", assets ? Assets() : null, census: true, process: true);
    }
    internal static void Stop() {
        try { if (Active) Record("stop", Map("withoutManagedCleanupTypes", Groups(unmanagedTypes)), census: true, process: true); }
        catch (Exception ex) { Failure(ex); }
        finally { MemoryDiagnosticHooks.Remove(); samples.Clear(); unmanagedTypes.Clear(); }
    }
}
