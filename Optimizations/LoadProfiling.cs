using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Ostrabus.Optimizations;

// Detached values only. The HTTP thread can read this while the Unity thread is blocked in loading.
internal static class LoadProfiling {
    const int MaxSessions = 3, MaxStages = 64, MaxMetrics = 64, MaxSlowCalls = 32;
    static readonly object Gate = new object();
    static readonly List<Session> Sessions = new List<Session>();
    static Session active;
    static int nextId;
    internal static bool Enabled = true;

    internal struct Sample {
        internal int SessionId;
        internal long Start;
        internal string Kind, Detail;
        internal int Items;
        internal bool MainThread;
    }
    sealed class Stage {
        internal string Name;
        internal long Start, Wall, Work;
        internal int NullYields, NestedYields, OtherYields;
    }
    sealed class Metric {
        internal string Name;
        internal long Total, Maximum;
        internal int Calls, Errors, MainThreadCalls, WorkerThreadCalls;
    }
    sealed class SlowCall {
        internal string Kind, Detail;
        internal long Duration;
        internal int Items;
    }
    sealed class Session {
        internal int Id, ThreadId, Depth, DroppedStages, DroppedMetrics;
        internal string Kind, Status = "active", StartedUtc, Error;
        internal long Start, End, WorkSegment, Work;
        internal long HeapStart, HeapEnd;
        internal int[] GcStart, GcEnd;
        internal readonly List<Stage> Stages = new List<Stage>();
        internal readonly Dictionary<string, Metric> Metrics = new Dictionary<string, Metric>(StringComparer.Ordinal);
        internal readonly List<SlowCall> SlowCalls = new List<SlowCall>();
        internal readonly Dictionary<string, long> Counters = new Dictionary<string, long>();
        internal Stage Current;
    }
    static long Now => Stopwatch.GetTimestamp();
    static double Ms(long ticks) => ticks * 1000d / Stopwatch.Frequency;
    static int[] GcCounts() => new[] { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) };
    static string Limit(string text) => text == null || text.Length <= 160 ? text : text.Substring(0, 160);

    internal static bool IsActive(string kind = null) {
        lock (Gate) return Enabled && active != null && (kind == null || active.Kind == kind);
    }
    internal static void Count(string name, long amount) {
        lock (Gate) {
            if (active == null) return;
            active.Counters.TryGetValue(name, out long value);
            if (active.Counters.Count < MaxMetrics || active.Counters.ContainsKey(name)) active.Counters[name] = value + amount;
        }
    }
    internal static void Begin(string kind) {
        if (!Enabled) return;
        lock (Gate) {
            if (active != null) FinishLocked("superseded", null);
            var session = new Session {
                Id = ++nextId, Kind = kind, ThreadId = Thread.CurrentThread.ManagedThreadId,
                Start = Now, StartedUtc = DateTime.UtcNow.ToString("O"), HeapStart = GC.GetTotalMemory(false), GcStart = GcCounts()
            };
            active = session;
            Sessions.Add(session);
            if (Sessions.Count > MaxSessions) Sessions.RemoveAt(0);
            SetStageLocked(kind, Now);
        }
    }
    static void FlushWork(long now) {
        if (active.Depth == 0) return;
        long elapsed = now - active.WorkSegment;
        active.Work += elapsed;
        active.Current.Work += elapsed;
        active.WorkSegment = now;
    }
    internal static Sample Enter(string kind, string detail = null, int items = 0) {
        if (!Enabled) return default;
        lock (Gate) {
            if (active == null) return default;
            long now = Now;
            bool main = Thread.CurrentThread.ManagedThreadId == active.ThreadId;
            if (main && active.Depth++ == 0) active.WorkSegment = now;
            return new Sample { SessionId = active.Id, Start = now, Kind = kind, Detail = Limit(detail), Items = items, MainThread = main };
        }
    }
    internal static void Exit(Sample sample, Exception error = null, int yieldKind = -1) {
        if (sample.SessionId == 0) return;
        lock (Gate) {
            if (active == null || active.Id != sample.SessionId) return;
            long now = Now, elapsed = now - sample.Start;
            if (sample.MainThread) {
                FlushWork(now);
                active.Depth--;
                if (yieldKind == 0) active.Current.NullYields++;
                else if (yieldKind == 1) active.Current.NestedYields++;
                else if (yieldKind == 2) active.Current.OtherYields++;
            }
            if (!active.Metrics.TryGetValue(sample.Kind, out Metric metric)) {
                if (active.Metrics.Count >= MaxMetrics) { active.DroppedMetrics++; return; }
                metric = new Metric { Name = sample.Kind };
                active.Metrics.Add(sample.Kind, metric);
            }
            metric.Calls++;
            if (sample.MainThread) metric.MainThreadCalls++; else metric.WorkerThreadCalls++;
            metric.Total += elapsed;
            metric.Maximum = Math.Max(metric.Maximum, elapsed);
            if (error != null) metric.Errors++;
            // Keep only the slowest calls, rather than an allocation/log per item or per CO.
            if (sample.Detail != null && (active.SlowCalls.Count < MaxSlowCalls || elapsed > active.SlowCalls[active.SlowCalls.Count - 1].Duration)) {
                var call = new SlowCall { Kind = sample.Kind, Detail = sample.Detail, Duration = elapsed, Items = sample.Items };
                int index = active.SlowCalls.FindIndex(x => x.Duration < elapsed);
                active.SlowCalls.Insert(index < 0 ? active.SlowCalls.Count : index, call);
                if (active.SlowCalls.Count > MaxSlowCalls) active.SlowCalls.RemoveAt(MaxSlowCalls);
            }
        }
    }
    internal static void StageChanged(string label) {
        if (!Enabled || string.IsNullOrEmpty(label)) return;
        // Progress labels containing individual names would otherwise create one stage per ship/file.
        if (label.StartsWith("Spawning Ship:", StringComparison.Ordinal)) label = "Spawning saved ships";
        else if (label.StartsWith("Spawning Station:", StringComparison.Ordinal)) label = "Spawning system stations";
        else if (label.StartsWith("Finished reading ", StringComparison.Ordinal)) label = "Load Ships";
        lock (Gate) {
            if (active == null || Thread.CurrentThread.ManagedThreadId != active.ThreadId) return;
            SetStageLocked(Limit(label), Now);
        }
    }
    static void SetStageLocked(string label, long now) {
        if (active.Current?.Name == label) return;
        if (active.Current != null) {
            FlushWork(now);
            active.Current.Wall += now - active.Current.Start;
        }
        if (active.Stages.Count >= MaxStages) {
            active.DroppedStages++;
            active.Current.Start = now;
            return;
        }
        active.Current = new Stage { Name = label, Start = now };
        active.Stages.Add(active.Current);
    }
    internal static void Finish(string kind, Exception error = null) {
        string message = null;
        lock (Gate) {
            if (active == null || active.Kind != kind) return;
            FinishLocked(error == null ? "completed" : "faulted", error?.GetType().Name);
            Session last = Sessions[Sessions.Count - 1];
            message = $"[LoadProfile] {last.Kind} #{last.Id} {last.Status}: {Ms(last.End - last.Start):F0} ms wall, {Ms(last.Work):F0} ms measured main-thread work. GET /profiling/load for details.";
        }
        Ostrabus.OstrabusPlugin.Logger?.LogInfo(message);
    }
    static void FinishLocked(string status, string error) {
        long now = Now;
        FlushWork(now);
        active.Current.Wall += now - active.Current.Start;
        active.End = now;
        active.Status = status;
        active.Error = error;
        active.HeapEnd = GC.GetTotalMemory(false);
        active.GcEnd = GcCounts();
        active = null;
    }
    internal static void Stop() {
        lock (Gate) if (active != null) FinishLocked("pluginStopped", null);
    }
    internal static object Snapshot() {
        lock (Gate) {
            var sessions = new List<object>(Sessions.Count);
            long now = Now;
            foreach (Session s in Sessions) {
                bool running = ReferenceEquals(s, active);
                long liveWork = running && s.Depth > 0 ? now - s.WorkSegment : 0;
                long wall = (running ? now : s.End) - s.Start;
                var stages = new List<object>(s.Stages.Count);
                foreach (Stage stage in s.Stages) {
                    bool current = running && ReferenceEquals(stage, s.Current);
                    long stageWall = stage.Wall + (current ? now - stage.Start : 0);
                    long stageWork = stage.Work + (current ? liveWork : 0);
                    stages.Add(new Dictionary<string, object> {
                        ["name"] = stage.Name, ["wallMs"] = Ms(stageWall), ["measuredMainThreadWorkMs"] = Ms(stageWork),
                        ["outsideInstrumentedWorkMs"] = Ms(Math.Max(0, stageWall - stageWork)),
                        ["nullYields"] = stage.NullYields, ["nestedCoroutineYields"] = stage.NestedYields, ["otherYields"] = stage.OtherYields
                    });
                }
                var metrics = new List<object>(s.Metrics.Count);
                foreach (Metric m in s.Metrics.Values) metrics.Add(new Dictionary<string, object> {
                    ["name"] = m.Name, ["calls"] = m.Calls, ["inclusiveMs"] = Ms(m.Total), ["maxMs"] = Ms(m.Maximum), ["errors"] = m.Errors,
                    ["mainThreadCalls"] = m.MainThreadCalls, ["workerThreadCalls"] = m.WorkerThreadCalls
                });
                var slow = new List<object>(s.SlowCalls.Count);
                foreach (SlowCall call in s.SlowCalls) slow.Add(new Dictionary<string, object> {
                    ["kind"] = call.Kind, ["detail"] = call.Detail, ["ms"] = Ms(call.Duration), ["savedItems"] = call.Items
                });
                int[] gc = running ? GcCounts() : s.GcEnd;
                var counters = new Dictionary<string, object>();
                foreach (var pair in s.Counters) counters.Add(pair.Key, pair.Value);
                sessions.Add(new Dictionary<string, object> {
                    ["id"] = s.Id, ["kind"] = s.Kind, ["status"] = s.Status, ["startedUtc"] = s.StartedUtc, ["error"] = s.Error,
                    ["wallMs"] = Ms(wall), ["measuredMainThreadWorkMs"] = Ms(s.Work + liveWork),
                    ["outsideInstrumentedWorkMs"] = Ms(Math.Max(0, wall - s.Work - liveWork)), ["currentStage"] = running ? s.Current.Name : null,
                    ["managedHeapStartBytes"] = s.HeapStart, ["managedHeapEndBytes"] = running ? (object)null : s.HeapEnd,
                    ["gcCollections"] = new[] { gc[0] - s.GcStart[0], gc[1] - s.GcStart[1], gc[2] - s.GcStart[2] },
                    ["droppedStages"] = s.DroppedStages, ["droppedMetricSamples"] = s.DroppedMetrics,
                    ["stages"] = stages, ["metrics"] = metrics, ["slowestCalls"] = slow, ["counters"] = counters
                });
            }
            return new Dictionary<string, object> {
                ["enabled"] = Enabled, ["capturedUtc"] = DateTime.UtcNow.ToString("O"), ["sessions"] = sessions,
                ["note"] = "Last 3 sessions. Work is elapsed wall time inside instrumented main-thread calls, not CPU time. Outside-work time includes frame waits, rendering, scene loading and uninstrumented work. Method metrics overlap; do not sum them. Heap deltas are not total allocations. Cancelled iterators remain active until another session starts or plugin stops."
            };
        }
    }
}
