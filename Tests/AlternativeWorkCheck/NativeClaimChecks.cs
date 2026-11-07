using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using Ostrabus.AlternativeWork;
using Ostranauts.WorkManager;

static partial class Program {
    static bool claimFake;
    static bool ClaimHasCond(string __0, ref bool __result) { __result = __0 == "IsAirtightFake" && claimFake; return false; }
    static bool ClaimAddCond(string __0, double __1) { if (__0 == "IsAirtightFake" && __1 > 0) claimFake = true; return false; }
    static bool ClaimZeroCond(string __0) { if (__0 == "IsAirtightFake") claimFake = false; return false; }
    static bool ClaimScale(ref float __result) { __result = 1; return false; }
    static void CheckNativeClaims() {
        var fixture = new Harmony("altwork.native.claim.fixture");
        var oldCOs = DataHandler.mapCOs;
        var oldInteractions = DataHandler.dictInteractions;
        double oldEpoch = StarSystem.fEpoch;
        try {
            fixture.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Equality"), prefix: new HarmonyMethod(typeof(Program), nameof(Equal)));
            fixture.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Inequality"), prefix: new HarmonyMethod(typeof(Program), nameof(Unequal)));
            fixture.Patch(AccessTools.PropertyGetter(typeof(UnityEngine.Time), "timeScale"), prefix: new HarmonyMethod(typeof(Program), nameof(ClaimScale)));
            fixture.Patch(AccessTools.Method(typeof(CondOwner), "HasCond", new[] { typeof(string) }), prefix: new HarmonyMethod(typeof(Program), nameof(ClaimHasCond)));
            fixture.Patch(AccessTools.Method(typeof(CondOwner), "AddCondAmount"), prefix: new HarmonyMethod(typeof(Program), nameof(ClaimAddCond)));
            fixture.Patch(AccessTools.Method(typeof(CondOwner), "ZeroCondAmount"), prefix: new HarmonyMethod(typeof(Program), nameof(ClaimZeroCond)));
            fixture.Patch(AccessTools.Method(typeof(WorkTravelPlan), "Refresh"), prefix: new HarmonyMethod(typeof(Program), nameof(SkipFloorRefresh)));
            fixture.Patch(AccessTools.Method(typeof(WorkTravelPlan), "Cost"), prefix: new HarmonyMethod(typeof(Program), nameof(FloorCost)));
            fixture.Patch(AccessTools.Method(typeof(AlternativeWorkSystem), "Allowed"), prefix: new HarmonyMethod(typeof(Program), nameof(AllowFloorTask)));
            var wm = Empty<WorkManager>();
            var a = MaterialCO("claim-a"); var b = MaterialCO("claim-b");
            var first = new Task2 { strDuty = "Construct", strInteraction = "install", strTargetCOID = "near" };
            var second = new Task2 { strDuty = "Construct", strInteraction = "install", strTargetCOID = "far" };
            Field(wm, "dictTasks2", new Dictionary<string, List<Task2>> { ["Construct"] = new List<Task2> { first, second } });
            DataHandler.mapCOs = new Dictionary<string, CondOwner> { ["near"] = MaterialCO("near"), ["far"] = MaterialCO("far") };
            DataHandler.dictInteractions = new Dictionary<string, JsonInteraction> { ["install"] = new JsonInteraction() };
            floorCosts["near"] = 0; floorCosts["far"] = 10; StarSystem.fEpoch = 1000;
            var jobA = new WorkClaimJob(a); var jobB = new WorkClaimJob(b);
            var reserve = AccessTools.Method(typeof(WorkManager), "TryReserveTask");
            Check((bool)reserve.Invoke(wm, new object[] { jobA, first }), "Native reservation failed");
            Check(!(bool)reserve.Invoke(wm, new object[] { jobB, first }), "Native reservation transferred to competing job");
            var collect = AccessTools.Method(typeof(WorkManager), "CollectTasks");
            List<Task2> Collect(CondOwner co, WorkClaimJob job) => (List<Task2>)collect.Invoke(wm, new object[] { "Construct", co, false, job });
            Check(Collect(b, jobB).Count == 1 && Collect(a, jobA).Count == 2, "Native reservation filtering lost job identity");
            var worker = new AlternativeWorkSystem.Worker(b.strID);
            AlternativeWorkSystem.Context = new AlternativeWorkSystem.SearchContext { Manager = wm, Crew = b, Worker = worker };
            Check(Collect(b, jobB).Count == 1 && Collect(b, jobB)[0] == second, "Alternative ranking reintroduced another job's reserved task");
            Check(!WorkSupplyPlan.Available(first, b, worker, wm), "Background preparation touched a native-reserved task");
            AlternativeWorkSystem.Context = null;

            // Exercise the real native scope around the production wrapper. The
            // wrapper must leave progressed state for RemoveClaimScope to save.
            var outside = new Task2(); wm.taskUpstream = outside; AlternativeWorkSystem.Spawned(wm) = 9;
            jobA.TaskUpstream = new Task2(); jobA.SpawnedPriorities = 99; jobA.FakeAirtight = true;
            var context = new AlternativeWorkSystem.SearchContext { Manager = wm, Crew = a };
            var probe = new Probe(wm, context);
            var selection = new AlternativeWorkSystem.Selection(probe, wm, a);
            Field(selection, "started", true); Field(selection, "context", context);
            Field(selection, "session", AccessTools.Field(typeof(AlternativeWorkSystem), "generation").GetValue(null));
            StaticField("activeSearches", 1);
            var enter = AccessTools.Method(typeof(WorkManager), "SetClaimScope");
            var leave = AccessTools.Method(typeof(WorkManager), "RemoveClaimScope");
            enter.Invoke(wm, new object[] { jobA });
            Check(claimFake && selection.MoveNext(), "Native job scope or alternative iterator failed to start");
            leave.Invoke(wm, new object[] { jobA });
            Check(wm.taskUpstream == outside && AlternativeWorkSystem.Spawned(wm) == 9 && !claimFake && jobA.SpawnedPriorities == 3 && jobA.FakeAirtight,
                "Native claim scratch/fake airtightness did not survive the alternative wrapper");
            enter.Invoke(wm, new object[] { jobA });
            Check(!selection.MoveNext() && probe.Resumed && probe.Disposed && claimFake, "Wrapper swallowed native job state or cleared native-owned airtightness");
            leave.Invoke(wm, new object[] { jobA });
            Check(AlternativeWorkSystem.Context == null && !claimFake && jobA.SpawnedPriorities == 3, "Completed alternative claim leaked context");

            var jobs = (Dictionary<CondOwner, WorkClaimJob>)AccessTools.Field(typeof(WorkManager), "_claimJobs").GetValue(wm);
            var order = (List<WorkClaimJob>)AccessTools.Field(typeof(WorkManager), "_claimOrder").GetValue(wm);
            jobs.Add(a, jobA); order.Add(jobA); jobs.Add(b, jobB); order.Add(jobB);
            reserve.Invoke(wm, new object[] { jobB, second });
            wm.CancelClaim(a);
            Check(!jobs.ContainsKey(a) && !AlternativeWorkSystem.Reservations(wm).ContainsKey(first) && AlternativeWorkSystem.Reservations(wm).ContainsKey(second),
                "Cancelling one claim leaked its reservation or cleared another job's reservation");
            wm.CancelAllClaims();
            Check(jobs.Count == 0 && order.Count == 0 && AlternativeWorkSystem.Reservations(wm).Count == 0, "Claim shutdown retained crew/tasks");
        } finally {
            AlternativeWorkSystem.Context = null; AlternativeWorkSystem.Reset(); claimFake = false;
            DataHandler.mapCOs = oldCOs; DataHandler.dictInteractions = oldInteractions; StarSystem.fEpoch = oldEpoch;
            floorCosts.Clear(); fixture.UnpatchSelf();
        }
    }
}
