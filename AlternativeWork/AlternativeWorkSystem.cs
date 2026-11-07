using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine.SceneManagement;
using Ostrabus.Inspection;

namespace Ostrabus.AlternativeWork;

internal static class AlternativeWorkSystem {
    internal static ConfigEntry<bool> Flag;
    internal static bool Enabled => Flag != null && Flag.Value;
    internal static SearchContext Context;
    internal static readonly AccessTools.FieldRef<WorkManager, Dictionary<string, List<Task2>>> Tasks = AccessTools.FieldRefAccess<WorkManager, Dictionary<string, List<Task2>>>("dictTasks2");
    internal static readonly AccessTools.FieldRef<WorkManager, int> Spawned = AccessTools.FieldRefAccess<WorkManager, int>("nSpawnedPriorities");
    internal static readonly AccessTools.FieldRef<WorkManager, List<Task2>> ActiveTasks = AccessTools.FieldRefAccess<WorkManager, List<Task2>>("aTasksActive");
    internal static readonly AccessTools.FieldRef<WorkManager, Dictionary<Task2, Ostranauts.WorkManager.WorkClaimJob>> Reservations =
        AccessTools.FieldRefAccess<WorkManager, Dictionary<Task2, Ostranauts.WorkManager.WorkClaimJob>>("_taskReservations");
    static readonly Dictionary<string, Worker> workers = new Dictionary<string, Worker>();
    static readonly List<string> crew = new List<string>();
    static readonly List<string> expired = new List<string>();
    static WorkManager manager;
    static int nextWorker, activeSearches, generation;
    static long nextRosterCheck;
    internal static long Searches, Claims, Failures, Filtered;
    internal static double PlanningMs, SelectionMs;

    internal readonly struct TaskKey : IEquatable<TaskKey> {
        readonly string target, interaction, ship;
        readonly int tile;
        internal TaskKey(Task2 task) { target = task.strTargetCOID; interaction = task.strInteraction; ship = task.strTileShip; tile = task.nTile; }
        internal string Target => target;
        public bool Equals(TaskKey other) => target == other.target && interaction == other.interaction && ship == other.ship && tile == other.tile;
        public override bool Equals(object obj) => obj is TaskKey key && Equals(key);
        public override int GetHashCode() => ((target?.GetHashCode() ?? 0) * 397 ^ (interaction?.GetHashCode() ?? 0)) * 397 ^ (ship?.GetHashCode() ?? 0) ^ tile;
    }
    internal sealed class Worker {
        internal readonly WorkTravelPlan Plan;
        internal readonly WorkSupplyPlan Supply = new WorkSupplyPlan();
        internal readonly Dictionary<TaskKey, double> RetryAfter = new Dictionary<TaskKey, double>();
        internal readonly HashSet<TaskKey> MaterialWait = new HashSet<TaskKey>();
        internal readonly List<TaskKey> ReadyMaterials = new List<TaskKey>();
        internal Worker(string id) { Plan = new WorkTravelPlan(id); }
        internal void Clear() { Supply.Clear(); Plan.Clear(); RetryAfter.Clear(); MaterialWait.Clear(); ReadyMaterials.Clear(); }
    }
    internal sealed class SearchContext {
        internal WorkManager Manager;
        internal CondOwner Crew;
        internal Worker Worker;
        internal bool VanillaCollection;
        internal Task2 MaterialPrerequisite;
    }
    struct Ranked {
        internal Task2 Task;
        internal bool Owned;
        internal int Preparation;
        internal float Cost;
        internal int Index;
    }
    static readonly Comparison<Ranked> Compare = (a, b) => {
        int order = b.Owned.CompareTo(a.Owned);
        if (order == 0) order = a.Preparation.CompareTo(b.Preparation);
        if (order == 0) order = a.Cost.CompareTo(b.Cost);
        return order == 0 ? a.Index.CompareTo(b.Index) : order;
    };
    internal static bool RequiresInputs(JsonInteraction interaction) {
        if (interaction?.aLootItms == null) return false;
        foreach (string contract in interaction.aLootItms)
            if (contract != null && contract.StartsWith("input,", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
    internal static bool Eligible(CondOwner co) => co != null && co.bAlive && co.IsHumanOrRobot && co.ship != null &&
        co.Pathfinder != null && CrewSim.coPlayer != null && co.Company == CrewSim.coPlayer.Company && co.Company != null && co.Company.mapRoster.ContainsKey(co.strID);

    internal static Worker GetWorker(CondOwner co) {
        if (!workers.TryGetValue(co.strID, out Worker worker)) {
            // Bounded per-crew scratch state; exceeding the cap uses vanilla selection.
            if (workers.Count >= 32) return null;
            worker = new Worker(co.strID); workers.Add(co.strID, worker);
        }
        return worker;
    }
    internal static void SetEnabled(bool value) {
        if (Flag == null) return;
        Flag.Value = value;
        if (!value && activeSearches == 0) ClearWorkers();
        Log($"[AlternativeWork] {(value ? "ON" : "OFF")}. {activeSearches} selection(s) in progress; existing work continues. New selections use {(value ? "alternative" : "vanilla")} behavior.");
    }
    internal static void PlannerFailed(SearchContext context, Exception error) {
        context.VanillaCollection = true;
        context.Worker.Plan.Clear();
        Log("[AlternativeWork] Travel planning failed; continuing this selection with vanilla candidates: " + error.Message);
        SetEnabled(false);
    }
    static void Log(string message) {
        Ostrabus.OstrabusPlugin.Logger?.LogInfo(message);
        ConsoleToGUI.instance?.LogInfo(message);
    }
    internal static void SceneUnloaded(Scene _) { Reset(); }
    internal static void Reset() {
        generation++;
        ClearWorkers(); manager = null; nextRosterCheck = 0;
        activeSearches = 0;
        Searches = Claims = Failures = Filtered = 0; PlanningMs = SelectionMs = 0;
        WorkCarriedInputsPatch.CarriedMatches = WorkCarriedInputsPatch.WiderSearches = 0;
        WorkSupplyPlan.Prepared = WorkSupplyPlan.Probes = 0; WorkSupplyPlan.PlanningMs = 0;
        WorkBatchInventoryPatch.CarriedBatches = WorkBatchInventoryPatch.ExpandedBatches = 0;
    }
    internal static void EnsureManager(WorkManager wm) {
        if (manager == wm) return;
        Reset(); manager = wm;
    }
    static void ClearWorkers() {
        foreach (Worker worker in workers.Values) worker.Clear();
        workers.Clear(); crew.Clear(); expired.Clear(); nextWorker = 0;
    }
    internal static void Tick() {
        if (!Enabled) return;
        try {
            if (CrewSim.objInstance == null || !CrewSim.objInstance.FinishedLoading || CrewSim.coPlayer == null) return;
            WorkManager current = CrewSim.objInstance.workManager;
            if (current == null) return;
            EnsureManager(current);
            bool pending = false;
            foreach (var list in Tasks(current).Values) if (list.Count > 0) { pending = true; break; }
            if (!pending) {
                if (activeSearches == 0 && workers.Count > 0) ClearWorkers();
                return;
            }
            long start = Stopwatch.GetTimestamp();
            if (start >= nextRosterCheck) {
                nextRosterCheck = start + Stopwatch.Frequency;
                crew.Clear();
                if (CrewSim.coPlayer.Company != null) foreach (string id in CrewSim.coPlayer.Company.mapRoster.Keys) {
                    if (crew.Count >= 32) break;
                    if (DataHandler.mapCOs.TryGetValue(id, out CondOwner co) && Eligible(co)) crew.Add(id);
                }
                expired.Clear();
                foreach (string id in workers.Keys) if (!crew.Contains(id)) expired.Add(id);
                foreach (string id in expired) { workers[id].Clear(); workers.Remove(id); }
                expired.Clear();
            }
            if (crew.Count == 0) return;
            if (nextWorker >= crew.Count) nextWorker = 0;
            if (DataHandler.mapCOs.TryGetValue(crew[nextWorker++], out CondOwner workerCO) && Eligible(workerCO)) {
                Worker state = GetWorker(workerCO);
                if (state != null) state.Supply.Tick(current, workerCO, state);
                if (state != null && state.Plan.Refresh(workerCO)) {
                    // One crew member per frame, with a global half-millisecond cooperative budget.
                    do { state.Plan.Step(); }
                    while (!state.Plan.Complete && Stopwatch.GetTimestamp() - start < Stopwatch.Frequency / 2000);
                }
            }
            PlanningMs += (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
        } catch (Exception ex) {
            Log("[AlternativeWork] Planner disabled after error: " + ex.Message);
            SetEnabled(false);
        }
    }
    internal static bool Allowed(CondOwner co, Task2 task) {
        if (co == null || co.ship == null || task == null) return false;
        if (!DataHandler.mapCOs.TryGetValue(task.strTargetCOID ?? "", out CondOwner target) || target == null || target.ship == null) return false;
        bool leave = co.HasShoreLeave();
        if (!CanVisit(co.ship, target.ship.strRegID, leave)) return false;
        if (!string.IsNullOrEmpty(task.strTileShip) && !CanVisit(co.ship, task.strTileShip, leave)) return false;
        return true;
    }
    static bool CanVisit(Ship current, string id, bool leave) {
        if (current.strRegID == id) return true;
        if (!leave) return false;
        foreach (Ship docked in current.GetAllDockedShips()) if (docked != null && docked.strRegID == id) return true;
        return false;
    }
    internal static List<Task2> Collect(WorkManager wm, string duty, CondOwner co, bool manual, Worker worker, List<Task2> nativeCandidates = null) {
        var result = new List<Task2>();
        List<Task2> tasks = nativeCandidates;
        if (tasks == null && !Tasks(wm).TryGetValue(duty, out tasks)) return result;
        var ranked = new List<Ranked>(tasks.Count);
        worker.Plan.Refresh(co);
        for (int i = 0; i < tasks.Count; i++) {
            Task2 task = tasks[i];
            var ownership = task.GetOwnership(co.strID);
            if (ownership == Task2.Allowed.Forbidden || (manual && ownership != Task2.Allowed.Owned)) continue;
            if ((nativeCandidates == null && StarSystem.fEpoch - task.fLastCheck <= 5) || worker.RetryAfter.TryGetValue(new TaskKey(task), out double retry) && StarSystem.fEpoch < retry) continue;
            if (!Allowed(co, task)) { Filtered++; continue; }
            DataHandler.mapCOs.TryGetValue(task.strTargetCOID, out CondOwner target);
            DataHandler.dictInteractions.TryGetValue(task.strInteraction, out JsonInteraction interaction);
            // Finish executable work before expanding the batch's material prerequisites.
            // A partially supplied lot is only a hint: native validation remains authoritative.
            int preparation = task.strInteraction.StartsWith("ACTFeedItem", StringComparison.Ordinal) ? 1 :
                RequiresInputs(interaction) && (target?.aLot == null || target.aLot.Count == 0) ? 2 : 0;
            ranked.Add(new Ranked { Task = task, Owned = ownership == Task2.Allowed.Owned, Preparation = preparation, Cost = worker.Plan.Cost(target, interaction), Index = i });
        }
        ranked.Sort(Compare);
        foreach (Ranked item in ranked) result.Add(item.Task);
        worker.Plan.SuggestedTarget = result.Count > 0 ? result[0].strTargetCOID : null;
        return result;
    }
    internal static void Failed(CondOwner co, Task2 task, bool materialPrerequisite = false) {
        Worker worker = GetWorker(co);
        if (worker == null) return;
        if (worker.RetryAfter.Count >= 128) { worker.RetryAfter.Clear(); worker.MaterialWait.Clear(); }
        var key = new TaskKey(task);
        worker.RetryAfter[key] = StarSystem.fEpoch + 5;
        if (materialPrerequisite) worker.MaterialWait.Add(key);
        else worker.MaterialWait.Remove(key);
        Failures++;
    }
    internal static void MaterialDelivered(string targetId) {
        if (!Enabled || string.IsNullOrEmpty(targetId)) return;
        foreach (Worker worker in workers.Values) {
            foreach (TaskKey key in worker.MaterialWait) if (key.Target == targetId) worker.ReadyMaterials.Add(key);
            foreach (TaskKey key in worker.ReadyMaterials) { worker.RetryAfter.Remove(key); worker.MaterialWait.Remove(key); }
            worker.ReadyMaterials.Clear();
        }
        // Only eligibility is refreshed. Native selection still validates materials, route,
        // permissions and ownership, and decides when the character can start the work.
    }
    internal static object Snapshot() {
        var plans = new List<object>();
        foreach (var pair in workers) plans.Add(SnapshotJson.Map("crewId", pair.Key, "settledTiles", pair.Value.Plan.Nodes,
            "complete", pair.Value.Plan.Complete, "capped", pair.Value.Plan.Capped, "lastSuggestedTarget", pair.Value.Plan.SuggestedTarget, "retryEntries", pair.Value.RetryAfter.Count,
            "supplyPlanning", pair.Value.Supply.Active));
        return SnapshotJson.Map("enabled", Enabled, "activeSelections", activeSearches, "searches", Searches, "claims", Claims,
            "failures", Failures, "permissionFiltered", Filtered, "planningMs", PlanningMs, "selectionMs", SelectionMs,
            "carriedInputMatches", WorkCarriedInputsPatch.CarriedMatches, "widerInputSearches", WorkCarriedInputsPatch.WiderSearches,
            "supplyProbes", WorkSupplyPlan.Probes, "supplyPrepared", WorkSupplyPlan.Prepared, "supplyPlanningMs", WorkSupplyPlan.PlanningMs, "supplyWindow", WorkSupplyPlan.Window,
            "carriedOnlyFeedBatches", WorkBatchInventoryPatch.CarriedBatches, "expandedFeedBatches", WorkBatchInventoryPatch.ExpandedBatches, "workers", plans);
    }

    // 1.0.1.10 owns scratch fields, fake airtightness and frame budgets in WorkClaimJob.
    // Scope only our ranking context around each native step; leave native job state intact.
    internal sealed class Selection : IEnumerator, IDisposable {
        readonly IEnumerator inner;
        readonly WorkManager wm;
        readonly CondOwner co;
        SearchContext context;
        int session;
        bool started, done;
        internal Selection(IEnumerator original, WorkManager manager, CondOwner worker) { inner = original; wm = manager; co = worker; }
        public object Current => inner.Current;
        public bool MoveNext() {
            if (done) return false;
            if (!started) {
                started = true; session = generation;
                if (Enabled && Eligible(co)) {
                    EnsureManager(wm);
                    session = generation;
                    Worker worker = GetWorker(co);
                    if (worker != null) {
                        context = new SearchContext { Manager = wm, Crew = co, Worker = worker };
                        activeSearches++; Searches++;
                        long prepareStart = Stopwatch.GetTimestamp();
                        try {
                            if (worker.Plan.Refresh(co)) do { worker.Plan.Step(); }
                            while (!worker.Plan.Complete && Stopwatch.GetTimestamp() - prepareStart < Stopwatch.Frequency / 2000);
                        } catch (Exception ex) { PlannerFailed(context, ex); }
                        PlanningMs += (Stopwatch.GetTimestamp() - prepareStart) * 1000d / Stopwatch.Frequency;
                    }
                }
            }
            if (context == null) return inner.MoveNext();
            if (session != generation) { Dispose(); return false; }
            long selectionStart = Stopwatch.GetTimestamp();
            try {
                SearchContext previous = Context;
                Context = context;
                bool more;
                try { more = inner.MoveNext(); }
                catch { Dispose(); throw; }
                finally { Context = previous; }
                if (!more) Dispose();
                return more;
            } finally { SelectionMs += (Stopwatch.GetTimestamp() - selectionStart) * 1000d / Stopwatch.Frequency; }
        }
        public void Reset() { throw new NotSupportedException(); }
        public void Dispose() {
            if (done) return;
            done = true;
            try { (inner as IDisposable)?.Dispose(); }
            finally {
                if (context != null) {
                    if (session == generation) activeSearches--;
                    context = null;
                    if (!Enabled && activeSearches == 0) ClearWorkers();
                }
            }
        }
    }
}
