using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;
using Ostrabus.AlternativeWork;
using UnityEngine;

static partial class Program {
    sealed class TransformIdentity : IEqualityComparer<Transform> {
        public bool Equals(Transform a, Transform b) => ReferenceEquals(a, b);
        public int GetHashCode(Transform a) => RuntimeHelpers.GetHashCode(a);
    }
    static readonly Dictionary<Transform, Vector3> supplyPositions = new Dictionary<Transform, Vector3>(new TransformIdentity());
    static readonly HashSet<string> supplyForbidden = new HashSet<string>();
    static bool supplyManual, supplyThrow;
    static WorkManager supplyManager;
    static bool SupplyPosition(Transform __instance, ref Vector3 __result) { __result = supplyPositions[__instance]; return false; }
    static bool SupplyManual(ref bool __result) { __result = supplyManual; return false; }
    static bool SupplyAllowed(Task2 task, ref bool __result) { __result = !supplyForbidden.Contains(task.strTargetCOID); return false; }
    static bool SupplyEligible(ref bool __result) { __result = true; return false; }
    static bool SupplyInteraction(string strName, ref Interaction __result) {
        __result = Empty<Interaction>(); __result.strName = strName;
        __result.strLootItmInputs = "floor-inputs"; __result.bVerboseTrigger = true;
        return false;
    }
    static bool QueueSupply() {
        Check(AlternativeWorkSystem.Context?.Manager == supplyManager, "Background queue has wrong selection context");
        Task2 upstream = supplyManager.taskUpstream;
        Check(upstream != null, "Background queue lost upstream ownership/duty");
        if (supplyThrow) throw new InvalidOperationException("fixture queue failure");
        var tasks = AlternativeWorkSystem.Tasks(supplyManager)[upstream.strDuty];
        Check(!WorkSupplyPlan.HasDelivery(tasks, upstream.strTargetCOID), "Background planner duplicated a pending delivery");
        var delivery = new Task2 { strDuty = upstream.strDuty, strTargetCOID = upstream.strTargetCOID, strInteraction = "ACTFeedItemFloor" };
        delivery.CopyFrom(upstream); delivery.bManual = false;
        tasks.Add(delivery); AlternativeWorkSystem.Spawned(supplyManager)++;
        inputQueues++;
        return false;
    }
    static CondOwner SupplyCO(string id, Ship ship, float x) {
        var co = MaterialCO(id); Field(co, "_objShip", ship);
        co.tf = Empty<Transform>(); supplyPositions[co.tf] = new Vector3(x, 0, 0);
        co.strPlaceholderInstallReq = "floor"; DataHandler.mapCOs[id] = co;
        return co;
    }
    static void DrainSupply(WorkSupplyPlan plan, CondOwner co, AlternativeWorkSystem.Worker worker) {
        for (int i = 0; i < 200 && plan.Active; i++) plan.Tick(supplyManager, co, worker);
        Check(!plan.Active, "Supply planner failed to finish its bounded window");
    }
    static void CheckSupplyPlan(int window = 4) {
        var fixture = new Harmony("altwork.supply.fixture");
        var oldCOs = DataHandler.mapCOs;
        var oldInteractions = DataHandler.dictInteractions;
        var oldFlag = AlternativeWorkSystem.Flag;
        var oldWindow = WorkSupplyPlan.WindowConfig;
        var oldContext = AlternativeWorkSystem.Context;
        double oldEpoch = StarSystem.fEpoch;
        var worker = new AlternativeWorkSystem.Worker("supply-worker");
        var competitor = new WorkSupplyPlan();
        try {
            CarriedFixture(fixture);
            fixture.Patch(AccessTools.PropertyGetter(typeof(Transform), "position"), prefix: new HarmonyMethod(typeof(Program), nameof(SupplyPosition)));
            fixture.Patch(AccessTools.Method(typeof(CondOwner), "HasCond", new[] { typeof(string) }), prefix: new HarmonyMethod(typeof(Program), nameof(SupplyManual)));
            fixture.Patch(AccessTools.Method(typeof(AlternativeWorkSystem), "Allowed"), prefix: new HarmonyMethod(typeof(Program), nameof(SupplyAllowed)));
            fixture.Patch(AccessTools.Method(typeof(AlternativeWorkSystem), "Eligible"), prefix: new HarmonyMethod(typeof(Program), nameof(SupplyEligible)));
            fixture.Patch(AccessTools.Method(typeof(DataHandler), "GetInteraction"), prefix: new HarmonyMethod(typeof(Program), nameof(SupplyInteraction)));
            fixture.Patch(AccessTools.Method(typeof(Interaction), "QueueMissingInputDeliveries"), prefix: new HarmonyMethod(typeof(Program), nameof(QueueSupply)) { priority = Priority.Last });
            DataHandler.mapCOs = new Dictionary<string, CondOwner>();
            DataHandler.dictInteractions = new Dictionary<string, JsonInteraction> { ["install"] = new JsonInteraction { aLootItms = new[] { "Input,floor-inputs,true" } } };
            AlternativeWorkSystem.Flag = Empty<ConfigEntry<bool>>(); MaterialModuleEnabled(true);
            WorkSupplyPlan.WindowConfig = Empty<ConfigEntry<int>>();
            AccessTools.Field(typeof(ConfigEntry<int>), "_typedValue").SetValue(WorkSupplyPlan.WindowConfig, window);
            StarSystem.fEpoch = 1000;
            var ship = Empty<Ship>(); ship.strRegID = "supply-ship";
            var co = SupplyCO("supply-worker", ship, 0);
            var center = SupplyCO("center", ship, 0);
            var active = new Task2 { strDuty = "Construct", strTargetCOID = center.strID, strInteraction = "install" };
            var pending = new List<Task2>();
            supplyManager = Empty<WorkManager>();
            Field(supplyManager, "dictTasks2", new Dictionary<string, List<Task2>> { ["Construct"] = pending });
            Field(supplyManager, "aTasksActive", new List<Task2> { active });
            var outsider = new Task2(); supplyManager.taskUpstream = outsider; AlternativeWorkSystem.Spawned(supplyManager) = 37;
            var context = new AlternativeWorkSystem.SearchContext(); AlternativeWorkSystem.Context = context;
            // Reverse list order to check proximity ranking, plus more than one scan slice.
            for (int i = 45; i >= 1; i--) {
                var target = SupplyCO("target-" + i, ship, i * .1f);
                pending.Add(new Task2 { strDuty = "Construct", strTargetCOID = target.strID, strInteraction = "install" });
            }
            pending.Find(task => task.strTargetCOID == "target-1").aOwnerIDs = new[] { "other-worker" };
            pending.Find(task => task.strTargetCOID == "target-2").bManual = true;
            supplyForbidden.Add("target-3");
            worker.RetryAfter[new AlternativeWorkSystem.TaskKey(pending.Find(task => task.strTargetCOID == "target-4"))] = 2000;
            carriedInputs = new List<CondOwner> { MaterialCO("floor-carried") }; widerInputs = new List<CondOwner>();
            inputQueues = shipVisits = 0;
            AlternativeWorkSystem.Context = new AlternativeWorkSystem.SearchContext { Manager = supplyManager, Crew = co, Worker = worker };
            AccessTools.Method(typeof(WorkFinalizePatch), "Postfix").Invoke(null, new object[] { supplyManager, co, active });
            AlternativeWorkSystem.Context = context;
            Check(worker.Supply.Active, "Successful work claim did not arm background supply planning");
            Check(inputQueues == 0, "Starting work waited for supply preparation");
            worker.Supply.Tick(supplyManager, co, worker);
            Check(inputQueues == 0 && worker.Supply.Active, "Planner scanned/prepared the entire batch without yielding");
            DrainSupply(worker.Supply, co, worker);
            Check(inputQueues == WorkSupplyPlan.Window - 1, "Active job plus prepared deliveries exceeded four-job window");
            foreach (string id in new[] { "target-5", "target-6", "target-7" }) Check(WorkSupplyPlan.HasDelivery(pending, id), "Planner failed to prepare closest allowed work: " + id);
            Check(!WorkSupplyPlan.HasDelivery(pending, "target-1") && !WorkSupplyPlan.HasDelivery(pending, "target-2") && !WorkSupplyPlan.HasDelivery(pending, "target-3") && !WorkSupplyPlan.HasDelivery(pending, "target-4"), "Supply planning bypassed ownership, manual status, permission or cooldown");
            Check(AlternativeWorkSystem.ActiveTasks(supplyManager).Count == 1 && active.aOwnerIDs == null, "Planner claimed or reserved future work");
            Check(shipVisits == 0, "Background supply probe scanned ship inventories");
            Check(ReferenceEquals(supplyManager.taskUpstream, outsider) && AlternativeWorkSystem.Spawned(supplyManager) == 37 && ReferenceEquals(AlternativeWorkSystem.Context, context), "Supply preparation leaked shared selection scratch");
            competitor.Arm(active); DrainSupply(competitor, co, worker);
            Check(inputQueues == WorkSupplyPlan.Window - 1, "A competing planner overfilled an existing window");
            // Completing the original active job opens one slot, not another full batch.
            AlternativeWorkSystem.ActiveTasks(supplyManager).Clear();
            worker.Supply.Arm(active); DrainSupply(worker.Supply, co, worker);
            Check(inputQueues == WorkSupplyPlan.Window, "Rolling supply window failed to refill exactly one free slot");
            pending.RemoveAll(task => task.strInteraction.StartsWith("ACTFeedItem", StringComparison.Ordinal));
            worker.Supply.Arm(active); worker.Supply.Tick(supplyManager, co, worker);
            MaterialModuleEnabled(false); worker.Supply.Tick(supplyManager, co, worker);
            Check(!worker.Supply.Active && inputQueues == WorkSupplyPlan.Window, "Disabling altwork left background preparation running");
            MaterialModuleEnabled(true);
            worker.Supply.Arm(active); worker.Supply.Tick(supplyManager, co, worker);
            supplyManual = true; worker.Supply.Tick(supplyManager, co, worker);
            Check(!worker.Supply.Active && inputQueues == WorkSupplyPlan.Window, "Manual orders did not cancel speculative preparation");
            supplyManual = false;
            carriedInputs.Clear(); worker.Supply.Arm(active); DrainSupply(worker.Supply, co, worker);
            Check(inputQueues == WorkSupplyPlan.Window && shipVisits == 0, "Missing carried materials caused speculative fetching");
            carriedInputs.Add(MaterialCO("floor-carried"));
            // Candidate checks are repeated after the native material matcher yields.
            worker.Supply.Arm(active);
            worker.Supply.Tick(supplyManager, co, worker); worker.Supply.Tick(supplyManager, co, worker);
            supplyForbidden.Add("target-5");
            var removed = pending.Find(task => task.strTargetCOID == "target-6"); pending.Remove(removed);
            DrainSupply(worker.Supply, co, worker);
            Check(!WorkSupplyPlan.HasDelivery(pending, "target-5") && !WorkSupplyPlan.HasDelivery(pending, "target-6"), "Yielded planner queued revoked/removed work");
            supplyForbidden.Remove("target-5"); pending.Add(removed);
            pending.RemoveAll(task => task.strInteraction.StartsWith("ACTFeedItem", StringComparison.Ordinal));
            worker.Supply.Arm(active); worker.Supply.Tick(supplyManager, co, worker);
            worker.Clear();
            Check(!worker.Supply.Active && ((List<Task2>)AccessTools.Field(typeof(WorkSupplyPlan), "candidates").GetValue(worker.Supply)).Count == 0, "Worker cleanup retained a suspended plan");
            worker.Supply.Arm(active); worker.Supply.Tick(supplyManager, co, worker);
            worker.Supply.Arm(new Task2 { strDuty = "Repair", strInteraction = "repair", strTargetCOID = center.strID });
            Check(!worker.Supply.Active, "Switching to unrelated work retained old preparation");
            int originalCount = pending.Count, queuedBeforeLimit = inputQueues;
            for (int i = pending.Count; i <= WorkSupplyPlan.TaskLimit; i++) pending.Add(active);
            worker.Supply.Arm(active); DrainSupply(worker.Supply, co, worker);
            Check(inputQueues == queuedBeforeLimit, "Oversized work list bypassed background planning cap");
            pending.RemoveRange(originalCount, pending.Count - originalCount);
            var foreign = new Task2 { strInteraction = "ACTFeedItemFloor", strTargetCOID = "target-1", aOwnerIDs = new[] { "other-worker" } };
            var permitted = new Task2 { strInteraction = "ACTFeedItemFloor", strTargetCOID = "target-5" };
            var batch = new Dictionary<Task2, CondOwner> { [foreign] = carriedInputs[0], [permitted] = carriedInputs[0] };
            AccessTools.Method(typeof(WorkBatchFeedPatch), "Postfix").Invoke(null, new object[] { co, batch });
            Check(!batch.ContainsKey(foreign) && batch.ContainsKey(permitted), "Native batching took another worker's explicitly owned prerequisite");
            supplyThrow = true; worker.Supply.Arm(active);
            bool threw = false;
            try { DrainSupply(worker.Supply, co, worker); } catch (InvalidOperationException) { threw = true; }
            Check(threw && !worker.Supply.Active && ReferenceEquals(supplyManager.taskUpstream, outsider) && AlternativeWorkSystem.Spawned(supplyManager) == 37 && ReferenceEquals(AlternativeWorkSystem.Context, context), "Queue exception leaked planner state or shared scratch");
            Check(((List<Task2>)AccessTools.Field(typeof(WorkSupplyPlan), "candidates").GetValue(worker.Supply)).Count == 0, "Cancelled planner retained task references");
        } finally {
            worker.Clear(); competitor.Clear(); supplyPositions.Clear(); supplyForbidden.Clear();
            supplyManual = supplyThrow = false; supplyManager = null; carriedInputs = widerInputs = null;
            DataHandler.mapCOs = oldCOs; DataHandler.dictInteractions = oldInteractions;
            AlternativeWorkSystem.Flag = oldFlag; AlternativeWorkSystem.Context = oldContext; StarSystem.fEpoch = oldEpoch;
            WorkSupplyPlan.WindowConfig = oldWindow;
            fixture.UnpatchSelf();
        }
    }
}
