using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace Ostrabus.AlternativeWork;

// A small rolling window of ordinary delivery prerequisites, never work claims.
// Native material matching runs only against carried contents. All world/path/tool
// validation stays at dispatch; this planner never changes a live pathfinder.
internal sealed class WorkSupplyPlan {
    internal const int MaxWindow = 30, ScanSlice = 32, TaskLimit = 2048;
    internal static BepInEx.Configuration.ConfigEntry<int> WindowConfig;
    internal static int Window => Math.Max(1, Math.Min(MaxWindow, WindowConfig?.Value ?? 8));
    internal static void SetWindow(int value) {
        if (value < 1 || value > MaxWindow) { ConsoleToGUI.instance?.LogInfo("Usage: altwork window 1..30"); return; }
        if (WindowConfig != null) WindowConfig.Value = value;
        ConsoleToGUI.instance?.LogInfo($"[AlternativeWork] Supply window: {Window}. Existing native work continues; preparation uses the new limit.");
    }
    internal static long Prepared, Probes;
    internal static double PlanningMs;
    readonly List<Task2> candidates = new List<Task2>(MaxWindow);
    readonly List<float> distances = new List<float>(MaxWindow);
    readonly HashSet<string> supplied = new HashSet<string>();
    IEnumerator operation;
    string duty, anchor;
    bool requested;
    internal bool Active => operation != null || requested;
    internal void Arm(Task2 task) {
        if (task == null || string.IsNullOrEmpty(task.strTargetCOID)) return;
        DataHandler.dictInteractions.TryGetValue(task.strInteraction, out JsonInteraction definition);
        if (!task.strInteraction.StartsWith("ACTFeedItem", StringComparison.Ordinal) && !AlternativeWorkSystem.RequiresInputs(definition)) { Clear(); return; }
        if (duty != task.strDuty) Clear();
        duty = task.strDuty; anchor = task.strTargetCOID; requested = true;
    }
    internal void Clear() {
        try { (operation as IDisposable)?.Dispose(); }
        finally { operation = null; candidates.Clear(); distances.Clear(); supplied.Clear(); duty = anchor = null; requested = false; }
    }
    internal void Tick(WorkManager wm, CondOwner co, AlternativeWorkSystem.Worker worker) {
        if (!AlternativeWorkSystem.Enabled || co.HasCond("IsAIManual")) { Clear(); return; }
        if (!Active) return;
        long start = Stopwatch.GetTimestamp();
        try {
            if (operation == null) { requested = false; operation = Prepare(wm, co, worker); }
            // One resumable slice for one crew member per Update. Native helpers can
            // exceed the cooperative budget; don't compound that with more slices.
            if (!operation.MoveNext()) {
                (operation as IDisposable)?.Dispose(); operation = null;
                candidates.Clear(); distances.Clear(); supplied.Clear();
            }
        } catch { Clear(); throw; }
        finally { PlanningMs += (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency; }
    }
    internal static bool Available(Task2 task, CondOwner co, AlternativeWorkSystem.Worker worker, WorkManager manager = null) {
        if (task == null || task.bManual || task.GetOwnership(co.strID) == Task2.Allowed.Forbidden || StarSystem.fEpoch - task.fLastCheck <= 5 ||
            (manager != null && AlternativeWorkSystem.Reservations(manager).ContainsKey(task))) return false;
        return !(worker.RetryAfter.TryGetValue(new AlternativeWorkSystem.TaskKey(task), out double retry) && StarSystem.fEpoch < retry) && AlternativeWorkSystem.Allowed(co, task);
    }
    internal static bool IsSupplied(Task2 task) {
        if (task.strInteraction.StartsWith("ACTFeedItem", StringComparison.Ordinal)) return true;
        DataHandler.dictInteractions.TryGetValue(task.strInteraction, out JsonInteraction definition);
        return AlternativeWorkSystem.RequiresInputs(definition) && DataHandler.mapCOs.TryGetValue(task.strTargetCOID, out CondOwner target) && target != null && target.aLot != null && target.aLot.Count > 0;
    }
    void Insert(Task2 task, float distance) {
        int index = 0;
        while (index < distances.Count && distances[index] <= distance) index++;
        if (index >= Window) return;
        while (candidates.Count >= Window) { candidates.RemoveAt(candidates.Count - 1); distances.RemoveAt(distances.Count - 1); }
        candidates.Insert(index, task); distances.Insert(index, distance);
    }
    void CountOutstanding(WorkManager wm, List<Task2> tasks, string ship, Vector2 point) {
        supplied.Clear();
        foreach (Task2 task in tasks) {
            if (!IsSupplied(task)) continue;
            CountOutstanding(task, ship, point);
            if (supplied.Count >= Window) return;
        }
        var active = AlternativeWorkSystem.ActiveTasks(wm);
        if (active == null) return;
        foreach (Task2 task in active) {
            if (task.strDuty != duty) continue;
            CountOutstanding(task, ship, point);
            if (supplied.Count >= Window) return;
        }
    }
    void CountOutstanding(Task2 task, string ship, Vector2 point) {
        if (string.IsNullOrEmpty(task.strTargetCOID) || !DataHandler.mapCOs.TryGetValue(task.strTargetCOID, out CondOwner target) || target == null || target.ship == null || target.ship.strRegID != ship || target.tf == null) return;
        if (((Vector2)target.tf.position - point).sqrMagnitude <= 64) supplied.Add(task.strTargetCOID);
    }
    IEnumerator Prepare(WorkManager wm, CondOwner co, AlternativeWorkSystem.Worker worker) {
        candidates.Clear(); distances.Clear(); supplied.Clear();
        string plannedDuty = duty;
        if (!AlternativeWorkSystem.Tasks(wm).TryGetValue(plannedDuty, out var tasks) || tasks.Count > TaskLimit || !DataHandler.mapCOs.TryGetValue(anchor, out CondOwner center) || center == null || center.ship == null || center.tf == null) yield break;
        string ship = center.ship.strRegID;
        Vector2 point = center.tf.position;
        CountOutstanding(wm, tasks, ship, point);
        if (supplied.Count >= Window) yield break;
        // Only same-ship blueprints in the native feed batch's eight-tile radius.
        // Scan cooperatively, retaining at most Window closest candidates.
        for (int i = 0; i < tasks.Count; i++) {
            if (i > 0 && i % ScanSlice == 0) yield return null;
            Task2 task = tasks[i];
            if (!Available(task, co, worker, wm) || !DataHandler.mapCOs.TryGetValue(task.strTargetCOID, out CondOwner target) || target == null || target.ship == null || target.ship.strRegID != ship || target.tf == null) continue;
            float distance = ((Vector2)target.tf.position - point).sqrMagnitude;
            if (distance > 64) continue;
            if (IsSupplied(task) || supplied.Contains(task.strTargetCOID)) continue;
            DataHandler.dictInteractions.TryGetValue(task.strInteraction, out JsonInteraction definition);
            if (!AlternativeWorkSystem.RequiresInputs(definition) || string.IsNullOrEmpty(target.strPlaceholderInstallReq)) continue;
            Insert(task, distance);
        }
        for (int i = 0; i < candidates.Count && supplied.Count < Window; i++) {
            Task2 task = candidates[i];
            CountOutstanding(wm, tasks, ship, point);
            if (supplied.Count >= Window) break;
            if (supplied.Contains(task.strTargetCOID) || !tasks.Contains(task) || !Available(task, co, worker, wm)) continue;
            // Another worker can prepare or take a job while this operation yields.
            if (HasDelivery(tasks, task.strTargetCOID)) continue;
            if (!DataHandler.mapCOs.TryGetValue(task.strTargetCOID, out CondOwner target) || target == null || target.ship == null || target.ship.strRegID != ship) continue;
            int lotCount = target.aLot?.Count ?? 0;
            Interaction interaction = DataHandler.GetInteraction(task.strInteraction);
            if (interaction?.strLootItmInputs == null) continue;
            var result = new IteratorResult<List<CondTrigger>>();
            var probe = WorkCarriedInputsPatch.ProbeCarried(interaction, co, target, result);
            Probes++;
            try { while (probe.MoveNext()) yield return probe.Current; }
            finally { (probe as IDisposable)?.Dispose(); }
            CountOutstanding(wm, tasks, ship, point);
            if (result.Value == null || supplied.Count >= Window || supplied.Contains(task.strTargetCOID) || !AlternativeWorkSystem.Enabled || !tasks.Contains(task) || !Available(task, co, worker, wm) ||
                !DataHandler.mapCOs.TryGetValue(task.strTargetCOID, out CondOwner current) || !ReferenceEquals(current, target) || target.ship == null || target.ship.strRegID != ship ||
                string.IsNullOrEmpty(target.strPlaceholderInstallReq) || (target.aLot?.Count ?? 0) != lotCount || HasDelivery(tasks, task.strTargetCOID)) continue;
            // QueueMissingInputDeliveries uses manager-wide scratch. Install it only
            // for this synchronous call and restore it even if native AddTask throws.
            var previous = AlternativeWorkSystem.Context;
            Task2 upstream = wm.taskUpstream;
            int spawned = AlternativeWorkSystem.Spawned(wm);
            try {
                AlternativeWorkSystem.Context = new AlternativeWorkSystem.SearchContext { Manager = wm, Crew = co, Worker = worker };
                wm.taskUpstream = task; AlternativeWorkSystem.Spawned(wm) = 0;
                WorkCarriedInputsPatch.QueuePrepared(interaction, target, result.Value);
                if (HasDelivery(tasks, task.strTargetCOID)) { supplied.Add(task.strTargetCOID); Prepared++; }
            } finally { wm.taskUpstream = upstream; AlternativeWorkSystem.Spawned(wm) = spawned; AlternativeWorkSystem.Context = previous; }
            yield return null;
        }
        candidates.Clear(); distances.Clear(); supplied.Clear();
    }
    internal static bool HasDelivery(List<Task2> tasks, string target) {
        foreach (var task in tasks) if (task.strTargetCOID == target && task.strInteraction.StartsWith("ACTFeedItem", StringComparison.Ordinal)) return true;
        return false;
    }
}
