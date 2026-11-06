using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using Ostrabus.AlternativeWork;

static partial class Program {
    static int checks;
    static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
    static void Field(object obj, string name, object value) { obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(obj, value); }
    static void StaticField(string name, object value) { typeof(AlternativeWorkSystem).GetField(name, BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, value); }
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    public static int Main() {
        AlternativeWorkSystem.Flag = null;
        var graph = new Graph(6);
        graph.Add(0, 1, 1); graph.Add(1, 5, 20); graph.Add(0, 2, 2); graph.Add(2, 3, 2); graph.Add(3, 5, 2);
        var routes = new IncrementalRoutes<int>();
        routes.Reset(0); routes.Step(graph, 1);
        Check(routes.Count == 1 && !routes.Complete && !routes.TryCost(5, out _), "Unsettled path advertised as final");
        while (!routes.Complete) routes.Step(graph, 1);
        Check(routes.TryCost(5, out float cost) && cost == 6 && !routes.TryCost(4, out _), "Detour or disconnected node wrong");
        Check(routes.Count == 5, "Duplicate heap entry settled twice");
        routes.Reset(0); routes.Step(graph, 100, 2);
        Check(routes.Capped && routes.Complete && routes.Count == 2, "Node cap ignored");
        routes.Clear(); Check(routes.Count == 0 && !routes.TryCost(0, out _), "Reset retained graph references");

        // Compare the incremental heap against an independent relaxation algorithm on random graphs.
        var random = new Random(731);
        for (int run = 0; run < 100; run++) {
            var g = new Graph(24);
            for (int i = 0; i < 24; i++) for (int j = 0; j < 6; j++) g.Add(i, random.Next(24), random.Next(1, 20));
            float[] expected = new float[24]; for (int i = 1; i < 24; i++) expected[i] = float.PositiveInfinity;
            for (int iteration = 0; iteration < 24; iteration++) for (int i = 0; i < 24; i++) foreach (var edge in g.Edges[i])
                expected[edge.Item1] = Math.Min(expected[edge.Item1], expected[i] + edge.Item2);
            routes.Reset(0); while (!routes.Complete) routes.Step(g, 3);
            for (int i = 0; i < 24; i++) Check(routes.TryCost(i, out float actual) ? actual == expected[i] : float.IsPositiveInfinity(expected[i]), "Incremental path differs from reference");
        }
        var a = new Task2 { strTargetCOID = "wall", strInteraction = "build", strTileShip = "ship", nTile = 2 };
        var b = new Task2 { strTargetCOID = "wall", strInteraction = "build", strTileShip = "ship", nTile = 3 };
        Check(!new AlternativeWorkSystem.TaskKey(a).Equals(new AlternativeWorkSystem.TaskKey(b)), "Different haul destinations share cooldown");
        b.nTile = 2;
        Check(new AlternativeWorkSystem.TaskKey(a).Equals(new AlternativeWorkSystem.TaskKey(b)), "Equivalent tasks don't share worker cooldown");
        Check(!AlternativeWorkSystem.Enabled, "Unconfigured module changes vanilla behavior");

        var wm = Empty<WorkManager>();
        var outside = new Task2 { strName = "outside" };
        wm.taskUpstream = outside; AlternativeWorkSystem.Spawned(wm) = 99;
        var context = new AlternativeWorkSystem.SearchContext { Manager = wm };
        var inner = new Probe(wm, context);
        var selection = new AlternativeWorkSystem.Selection(inner, wm, null);
        Field(selection, "started", true); Field(selection, "context", context);
        StaticField("activeSearches", 1);
        Check(selection.MoveNext(), "Explicit wait lost");
        Check(wm.taskUpstream == outside && AlternativeWorkSystem.Spawned(wm) == 99 && AlternativeWorkSystem.Context == null, "Coroutine scratch leaked to other workers");
        Check(!selection.MoveNext() && inner.Resumed && inner.Disposed, "Search state not restored across frames");
        Check(wm.taskUpstream == outside && AlternativeWorkSystem.Spawned(wm) == 99, "Completion overwrote shared state");

        // Diagnostic reads must bind to this game version and must never run the inspected operation.
        const BindingFlags privateFields = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
        foreach (string name in new[] { "_endTurnDispatcher", "_priorityClockDebt" }) Check(typeof(Condowner.Utils.Scheduler).GetField(name, privateFields) != null, "Missing scheduler field " + name);
        foreach (string name in new[] { "_active", "_pending" }) Check(typeof(Condowner.Utils.EndTurnDispatcher).GetField(name, privateFields) != null, "Missing dispatcher field " + name);
        foreach (string name in new[] { "aTickers", "_endTurnTicker" }) Check(typeof(CondOwner).GetField(name, privateFields) != null, "Missing actor field " + name);
        Check(typeof(Condowner.Utils.EndTurnHandle).GetProperty("RequiresContinuation", privateFields) != null, "Missing handle flag");
        var probe = new ReadOnlyProbe();
        var inspect = typeof(Ostrabus.Inspection.WorkSchedulerInspection).GetMethod("Operation", privateFields);
        string diagnostic = Ostrabus.Inspection.SnapshotJson.Encode(inspect.Invoke(null, new object[] { probe, 0, new HashSet<object>() }));
        Check(diagnostic.Contains("ReadOnlyProbe") && diagnostic.Length < 1024, "Diagnostic traversal failed to bound cyclic enumerators");

        // Bind every Harmony patch to the actual shipped game methods, including private overloads.
        var harmony = new Harmony("altwork.offline.check");
        try {
            foreach (Type type in new[] { typeof(WorkSelectionPatch), typeof(WorkCollectionPatch), typeof(WorkFailurePatch), typeof(WorkMaterialPrerequisitePatch), typeof(WorkMaterialDeliveryPatch), typeof(WorkCarriedInputsPatch), typeof(WorkFinalizePatch), typeof(WorkDelayedClaimPatch), typeof(WorkSuitPermissionPatch), typeof(WorkBatchHaulPatch), typeof(WorkBatchFeedPatch), typeof(WorkBatchInventoryPatch), typeof(WorkConsolePatch) }) {
                var patched = harmony.CreateClassProcessor(type).Patch();
                Check(patched != null && patched.Count > 0, "Patch failed to bind: " + type.Name);
            }
            CheckPrerequisites();
            CheckMaterialCooldowns();
            CheckFloorBatchOrdering();
            CheckCarriedInputs();
            CheckSupplyPlan();
            CheckSupplyPlan(8);
            CheckBatchInventory();
            CheckWindowControls();
        } finally { harmony.UnpatchSelf(); }
        Console.WriteLine(checks + " alternative work checks passed.");
        return 0;
    }
    sealed class Graph : IRouteGraph<int> {
        internal List<Tuple<int, float>>[] Edges;
        internal Graph(int n) { Edges = new List<Tuple<int, float>>[n]; for (int i = 0; i < n; i++) Edges[i] = new List<Tuple<int, float>>(); }
        internal void Add(int from, int to, float cost) { Edges[from].Add(Tuple.Create(to, cost)); }
        public int Neighbors(int from, int[] nodes, float[] costs) {
            for (int i = 0; i < Edges[from].Count; i++) { nodes[i] = Edges[from][i].Item1; costs[i] = Edges[from][i].Item2; }
            return Edges[from].Count;
        }
    }
    sealed class ReadOnlyProbe : IEnumerator {
        public IEnumerator Self;
        internal ReadOnlyProbe() { Self = this; }
        public object Current => throw new Exception("Inspection read Current");
        public bool MoveNext() => throw new Exception("Inspection advanced iterator");
        public void Reset() => throw new Exception("Inspection reset iterator");
    }
    sealed class Probe : IEnumerator, IDisposable {
        readonly WorkManager wm;
        readonly AlternativeWorkSystem.SearchContext context;
        readonly Task2 own = new Task2();
        internal bool Resumed, Disposed;
        int stage;
        internal Probe(WorkManager wm, AlternativeWorkSystem.SearchContext context) { this.wm = wm; this.context = context; }
        public object Current => "explicit wait";
        public bool MoveNext() {
            Check(AlternativeWorkSystem.Context == context, "Wrong worker context");
            if (stage++ == 0) {
                Check(wm.taskUpstream == null && AlternativeWorkSystem.Spawned(wm) == 0, "Inherited another worker's upstream task");
                wm.taskUpstream = own; AlternativeWorkSystem.Spawned(wm) = 3; return true;
            }
            Check(wm.taskUpstream == own && AlternativeWorkSystem.Spawned(wm) == 3, "Lost selection's local prerequisite state");
            Resumed = true; return false;
        }
        public void Reset() { throw new NotSupportedException(); }
        public void Dispose() { Disposed = true; }
    }
}

namespace Ostrabus {
    internal static class OstrabusPlugin { internal static BepInEx.Logging.ManualLogSource Logger = null; }
}
