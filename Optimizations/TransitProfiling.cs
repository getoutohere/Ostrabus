using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Ostrabus.Inspection;
using Ostranauts.Ships;
using UnityEngine;

namespace Ostrabus.Optimizations;

// Explicit API registration only. No startup patch attribute or Update polling.
internal static class TransitProfiling {
    const string Owner = "com.ostranauts.template.profiling.transit";
    static readonly Harmony harmony = new Harmony(Owner);
    static readonly Dictionary<MethodBase, Tag> targets = new Dictionary<MethodBase, Tag>();
    static readonly object gate = new object();
    static readonly Queue<object> gaps = new Queue<object>();
    static readonly MethodInfo Frame = AccessTools.Method(typeof(CrewSim), "Update");
    static volatile bool installed;
    static bool previousEnabled;
    static long previousFrame;
    static int previousFrameGC;
    static int failures;
    [ThreadStatic] static int loadingDepth;
    [ThreadStatic] static string loadingDetail;
    internal static bool Active => installed;
    sealed class Tag {
        internal string Name;
        internal bool Root, InsideOnly;
        internal FieldInfo Host;
    }
    internal struct Scope {
        internal LoadProfiling.Sample Timing;
        internal bool Started;
        internal int PreviousDepth, GC;
        internal string Name;
        internal string PreviousDetail;
    }
    static void Add(Type type, string name, bool insideOnly = false, Type[] args = null) {
        var method = AccessTools.Method(type, name, args) ?? throw new MissingMethodException(type.FullName, name);
        targets.Add(method, new Tag { Name = type.Name + "." + name, InsideOnly = insideOnly });
    }
    static void Iterator(Type type, string name, params Type[] args) {
        var factory = AccessTools.Method(type, name, args);
        var iterator = factory?.GetCustomAttribute<IteratorStateMachineAttribute>()?.StateMachineType;
        var method = iterator == null ? null : AccessTools.Method(iterator, "MoveNext");
        if (method == null) throw new MissingMethodException(type.FullName, name + ".MoveNext");
        targets.Add(method, new Tag { Name = type.Name + "." + name + ".MoveNext", Root = true, Host = AccessTools.Field(iterator, "<>4__this") ?? AccessTools.Field(iterator, "ship") });
    }
    internal static void Start() {
        if (Active) return;
        if (LoadProfiling.IsActive()) throw new InvalidOperationException("Another load profile is active.");
        previousEnabled = LoadProfiling.Enabled;
        previousFrame = 0; loadingDepth = failures = 0; loadingDetail = null;
        lock (gate) gaps.Clear();
        targets.Clear();
        try {
            Add(typeof(CrewSim), "Update");
            Iterator(typeof(GUITransit), "_Go", typeof(JsonTransitConnection));
            Iterator(typeof(BarterZoneShip), "Init", typeof(int));
            // The optimized coroutine replaces the native iterator at its factory.
            // Lookup also lets the standalone profiler fixture run without this patch.
            var optimized = typeof(TransitProfiling).Assembly.GetType("Ostrabus.Optimizations.BarterInitPatch");
            if (optimized != null) Iterator(optimized, "Initialize", typeof(BarterZoneShip), typeof(int));
            Add(typeof(CrewSim), "SaveToShallow");
            Add(typeof(Ship), "Destroy", args: new[] { typeof(bool) });
            Add(typeof(Ship), "CreatePart", true);
            Add(typeof(CondOwner), "PostGameLoad", true);
            Add(typeof(Container), "AddCOSimple", true);
            Add(typeof(Slots), "SlotItem", true, new[] { typeof(string), typeof(CondOwner), typeof(bool) });
            Add(typeof(Ship), "SetZoneData", true);
            Add(typeof(Ship), "MoveShip", true, new[] { typeof(Vector2) });
            Add(typeof(BarterZoneShip), "AddToLocalInstance", true);
            var prefix = new HarmonyMethod(typeof(TransitProfiling), nameof(Begin)) { priority = Priority.First };
            var finalizer = new HarmonyMethod(typeof(TransitProfiling), nameof(End));
            foreach (var method in targets.Keys) harmony.Patch(method, prefix: prefix, finalizer: finalizer);
            LoadProfiling.Enabled = true;
            LoadProfiling.Begin("transit"); installed = true;
        } catch {
            harmony.UnpatchSelf(); targets.Clear(); LoadProfiling.Enabled = previousEnabled;
            throw;
        }
    }
    internal static void Stop() {
        if (!Active) return;
        try { harmony.UnpatchSelf(); LoadProfiling.Finish("transit"); }
        finally { installed = false; loadingDepth = 0; loadingDetail = null; targets.Clear(); LoadProfiling.Enabled = previousEnabled; }
    }
    static void Begin(object __instance, MethodBase __originalMethod, out Scope __state) {
        __state = default;
        __state.PreviousDepth = loadingDepth;
        __state.PreviousDetail = loadingDetail;
        try {
            if (!Active || !targets.TryGetValue(__originalMethod, out var tag) || tag.InsideOnly && loadingDepth == 0) return;
            int gc = GC.CollectionCount(0);
            int frame = tag.InsideOnly ? 0 : Time.frameCount;
            if (__originalMethod == Frame) {
                long now = Stopwatch.GetTimestamp();
                double gapMs = (now - previousFrame) * 1000d / Stopwatch.Frequency;
                if (previousFrame != 0 && gapMs >= 50) lock (gate) {
                    gaps.Enqueue(SnapshotJson.Map("frame", frame, "utc", DateTime.UtcNow.ToString("O"), "gapMs", gapMs,
                        "gcCollectionsSincePreviousUpdate", gc - previousFrameGC));
                    while (gaps.Count > 16) gaps.Dequeue();
                }
                previousFrame = now; previousFrameGC = gc;
            }
            string detail = loadingDetail;
            if (__originalMethod != Frame && !tag.InsideOnly) {
                detail = "frame=" + frame;
                var host = tag.Host == null ? __instance : tag.Host.GetValue(__instance);
                if (host is Ship ship) detail += "; ship=" + ship.strRegID;
            }
            __state.PreviousDepth = loadingDepth;
            if (tag.Root) { loadingDepth++; loadingDetail = detail; }
            __state.Name = tag.Name; __state.GC = gc;
            __state.Timing = LoadProfiling.Enter(tag.Name, detail); __state.Started = true;
        } catch (Exception ex) { loadingDepth = __state.PreviousDepth; loadingDetail = __state.PreviousDetail; Failure(ex); }
    }
    static Exception End(Exception __exception, Scope __state) {
        if (!__state.Started) return __exception;
        try {
            LoadProfiling.Exit(__state.Timing, __exception);
            int gc = GC.CollectionCount(0) - __state.GC;
            if (gc > 0) LoadProfiling.Count(__state.Name + ".gcCollections", gc);
        } catch (Exception ex) { Failure(ex); }
        finally { loadingDepth = __state.PreviousDepth; loadingDetail = __state.PreviousDetail; }
        return __exception;
    }
    internal static object Snapshot() {
        object[] frames;
        lock (gate) frames = gaps.ToArray();
        return SnapshotJson.Map("active", Active, "diagnosticErrors", failures, "frameGaps", frames, "loadProfiles", LoadProfiling.Snapshot(),
            "note", "Temporary method/iterator wall timings; nested durations overlap. Frame gaps include rendering, scheduling and GC, not just simulation work. All hooks are removed on stop.");
    }
    static void Failure(Exception ex) { if (++failures <= 4) OstrabusPlugin.Logger?.LogWarning("[TransitProfile] " + ex.Message); }
}
