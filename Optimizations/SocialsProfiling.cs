using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using Ostrabus.Inspection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ostrabus.Optimizations;

// Explicit registration only: no startup patches, background task or idle
// Update callback. Capture one opening plus deferred UI work, then unhook.
internal static class SocialsProfiling {
    const string Owner = "com.ostranauts.template.profiling.socials";
    static readonly Harmony harmony = new Harmony(Owner);
    static readonly object gate = new object();
    static readonly Dictionary<MethodBase, Metric> targets = new Dictionary<MethodBase, Metric>();
    static readonly MethodInfo root = AccessTools.Method(typeof(GUIPDA), "ShowSocials");
    static readonly MethodInfo frame = AccessTools.Method(typeof(CrewSim), "Update");
    static readonly List<Metric> metrics = new List<Metric>();
    static bool active, allocationCountersAvailable;
    static int thread, depth, finishedFrame, openings, errors;
    static long deadline;
    static string status = "Not started", startedUtc;
    static object firstInputs;
    [ThreadStatic] static long children;
    internal static bool Active => active;
    internal static Func<int> ReadFrame = () => Time.frameCount;
    sealed class Metric {
        internal string Name;
        internal long Total, Own, Maximum, Allocated;
        internal int Calls, Errors;
    }
    internal struct Scope {
        internal long Start, PreviousChildren, Allocated;
        internal object Metric;
        internal bool Root;
    }
    static void Add(Type type, string name, Type[] args = null) {
        var method = AccessTools.Method(type, name, args) ?? throw new MissingMethodException(type.FullName, name);
        if (targets.ContainsKey(method)) return;
        var metric = new Metric { Name = type.Name + "." + name };
        targets.Add(method, metric); metrics.Add(metric);
    }
    internal static void Start() {
        if (active) return;
        long before = GC.GetAllocatedBytesForCurrentThread();
        var probe = new byte[256];
        GC.KeepAlive(probe);
        allocationCountersAvailable = GC.GetAllocatedBytesForCurrentThread() - before >= 256;
        lock (gate) {
            targets.Clear(); metrics.Clear(); depth = finishedFrame = openings = errors = 0;
            children = 0; firstInputs = null; thread = Thread.CurrentThread.ManagedThreadId;
            startedUtc = DateTime.UtcNow.ToString("O"); status = "Armed for next Socials opening";
        }
        try {
            Add(typeof(GUIPDA), "ShowSocials");
            Add(typeof(GUIPDA), nameof(GUIPDA.GetKnownSocialContacts));
            Add(typeof(GUISocialsRow), nameof(GUISocialsRow.SetContact));
            Add(typeof(FaceAnim2), nameof(FaceAnim2.GetPNG));
            Add(typeof(PortraitComposition), nameof(PortraitComposition.TryCompose));
            Add(typeof(PortraitComposition), nameof(PortraitComposition.Blend));
            Add(typeof(PortraitComposition), nameof(PortraitComposition.NativePixels));
            Add(typeof(Texture2D), nameof(Texture2D.SetPixels), new[] { typeof(Color[]) });
            Add(typeof(Texture2D), nameof(Texture2D.Apply), Type.EmptyTypes);
            Add(typeof(DataHandler), nameof(DataHandler.LoadPNG));
            Add(typeof(GUIChargenStack), nameof(GUIChargenStack.GetLatestCareerName));
            Add(typeof(LinkOpener), nameof(LinkOpener.GetShipLink));
            Add(typeof(Resources), nameof(Resources.Load), new[] { typeof(string), typeof(Type) });
            Add(typeof(UnityEngine.Object), nameof(UnityEngine.Object.Instantiate), new[] { typeof(UnityEngine.Object), typeof(Transform), typeof(bool) });
            Add(typeof(LayoutRebuilder), nameof(LayoutRebuilder.Rebuild));
            Add(typeof(TextMeshProUGUI), nameof(TextMeshProUGUI.Rebuild));
            Add(typeof(TextMeshProUGUI), "GenerateTextMesh");
            Add(typeof(CanvasUpdateRegistry), "PerformUpdate");
            Add(typeof(CrewSim), "Update");
            var begin = new HarmonyMethod(typeof(SocialsProfiling), nameof(Begin)) { priority = Priority.First };
            var end = new HarmonyMethod(typeof(SocialsProfiling), nameof(End)) { priority = Priority.Last };
            foreach (var method in targets.Keys) harmony.Patch(method, prefix: begin, finalizer: end);
            harmony.Patch(AccessTools.Method(typeof(PortraitComposition), nameof(PortraitComposition.TryCompose)),
                prefix: new HarmonyMethod(typeof(SocialsProfiling), nameof(ObserveInputs)));
            deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 180;
            active = true;
        } catch {
            harmony.UnpatchSelf();
            lock (gate) { active = false; targets.Clear(); status = "Installation failed"; }
            throw;
        }
    }
    internal static void Stop(string reason = "Stopped") {
        if (!active) return;
        active = false;
        try { harmony.UnpatchSelf(); }
        finally { lock (gate) { targets.Clear(); status = reason; depth = 0; children = 0; } }
    }
    static void Begin(MethodBase __originalMethod, out Scope __state) {
        __state = default;
        if (!active || Thread.CurrentThread.ManagedThreadId != thread || __originalMethod == frame) return;
        if (__originalMethod != root && depth == 0 && finishedFrame == 0) return;
        lock (gate) {
            if (!targets.TryGetValue(__originalMethod, out var metric)) return;
            __state = new Scope { Metric = metric, Start = Stopwatch.GetTimestamp(), PreviousChildren = children,
                Allocated = allocationCountersAvailable ? GC.GetAllocatedBytesForCurrentThread() : 0, Root = __originalMethod == root };
            children = 0;
            if (__state.Root) { depth++; openings++; status = "Capturing Socials and deferred UI"; }
        }
    }
    static Exception End(Exception __exception, MethodBase __originalMethod, Scope __state) {
        if (__originalMethod == frame) {
            if (active && Thread.CurrentThread.ManagedThreadId == thread && depth == 0) {
                if (finishedFrame != 0 && ReadFrame() > finishedFrame + 3) Stop("Completed one opening and deferred UI frames");
                else if (Stopwatch.GetTimestamp() >= deadline) Stop("Expired after 180 seconds");
            }
            return __exception;
        }
        if (__state.Metric == null) return __exception;
        long now = Stopwatch.GetTimestamp(), allocated = allocationCountersAvailable ? GC.GetAllocatedBytesForCurrentThread() - __state.Allocated : 0;
        lock (gate) {
            var metric = (Metric)__state.Metric;
            long elapsed = now - __state.Start;
            metric.Calls++; metric.Total += elapsed; metric.Own += Math.Max(0, elapsed - children);
            metric.Maximum = Math.Max(metric.Maximum, elapsed); metric.Allocated += Math.Max(0, allocated);
            if (__exception != null) { metric.Errors++; errors++; }
            children = __state.PreviousChildren + elapsed;
            if (__state.Root) { depth--; finishedFrame = Math.Max(1, ReadFrame()); }
        }
        return __exception;
    }
    static double Ms(long ticks) => ticks * 1000d / Stopwatch.Frequency;
    static void ObserveInputs(Texture2D __0, Texture2D[] __1) {
        if (!active || depth == 0 || firstInputs != null || Thread.CurrentThread.ManagedThreadId != thread) return;
        try {
            var layers = new List<object>();
            if (__1 != null) for (int i = 0; i < Math.Min(__1.Length, 32); i++) {
                var layer = __1[i];
                layers.Add(layer == null ? null : SnapshotJson.Map("width", layer.width, "height", layer.height,
                    "format", layer.format.ToString(), "readable", layer.isReadable, "supported", PortraitComposition.Supports(layer.format)));
            }
            lock (gate) firstInputs = SnapshotJson.Map("width", __0?.width, "height", __0?.height, "layers", layers);
        } catch (Exception ex) { lock (gate) { errors++; firstInputs = SnapshotJson.Map("inspectionError", ex.Message); } }
    }
    internal static object Snapshot() {
        lock (gate) {
            var rows = new List<object>(metrics.Count);
            foreach (var metric in metrics) rows.Add(SnapshotJson.Map("method", metric.Name, "calls", metric.Calls,
                "inclusiveMs", Ms(metric.Total), "exclusiveMs", Ms(metric.Own), "maximumMs", Ms(metric.Maximum),
                "inclusiveAllocatedBytes", allocationCountersAvailable ? (object)metric.Allocated : null, "errors", metric.Errors));
            return SnapshotJson.Map("active", active, "status", status, "startedUtc", startedUtc, "openings", openings,
                "errors", errors, "allocationCountersAvailable", allocationCountersAvailable, "firstInputs", firstInputs, "metrics", rows,
                "note", "Explicit temporary hooks only; automatic removal after one opening plus deferred frames or 180 seconds. Nested times/allocations overlap. Deferred UI includes other active canvases. No portrait cache eviction or UI actions are performed.");
        }
    }
}
