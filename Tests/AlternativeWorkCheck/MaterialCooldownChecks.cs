using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using Ostranauts.Core.Models;
using Ostrabus.AlternativeWork;

static partial class Program {
    static bool feedAddsItem, feedThrows;
    static void MaterialModuleEnabled(bool enabled) => AccessTools.Field(typeof(ConfigEntry<bool>), "_typedValue").SetValue(AlternativeWorkSystem.Flag, enabled);
    static bool SkipDeliveryPlanning() => false;
    static bool SimulateFeed(Interaction __instance, bool isCancelIa) {
        if (feedThrows) throw new InvalidOperationException("fixture delivery failure");
        if (feedAddsItem && !isCancelIa) {
            var target = (CondOwner)AccessTools.Field(typeof(Interaction), "objThemTemp").GetValue(__instance);
            target.aLot.Add(Empty<CondOwner>());
        }
        return false;
    }
    static CondOwner MaterialCO(string id) {
        var co = Empty<CondOwner>(); Field(co, "_strID", id);
        co.aLot = new TrackingCollection<CondOwner>();
        return co;
    }
    static void FailMaterialTask(WorkManager wm, CondOwner co, Task2 task, CondOwner target) {
        var context = new AlternativeWorkSystem.SearchContext { Manager = wm, Crew = co };
        AlternativeWorkSystem.Context = context;
        wm.taskUpstream = task;
        // Native queue helper is skipped only by the fixture; the production prefix tags
        // the actual upstream order before native FailTask's prefix/postfix observe it.
        AccessTools.Method(typeof(Interaction), "QueueMissingInputDeliveries").Invoke(Empty<Interaction>(), new object[] { target, new List<CondTrigger>() });
        Check(ReferenceEquals(context.MaterialPrerequisite, task), "Delivery prerequisite did not tag its upstream task");
        var args = new object[] { task, co, null };
        AccessTools.Method(typeof(WorkFailurePatch), "Prefix").Invoke(null, args);
        double before = task.fLastCheck;
        task.fLastCheck = StarSystem.fEpoch;
        AccessTools.Method(typeof(WorkFailurePatch), "Postfix").Invoke(null, new[] { (object)task, co, args[2] });
        Check(task.fLastCheck == before, "Material tracking changed vanilla task timestamps");
        AlternativeWorkSystem.Context = null;
    }
    static void CheckMaterialCooldowns() {
        var fixture = new Harmony("altwork.material.fixture");
        var previousFlag = AlternativeWorkSystem.Flag;
        var previousContext = AlternativeWorkSystem.Context;
        try {
            fixture.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Equality"), prefix: new HarmonyMethod(typeof(Program), nameof(Equal)));
            fixture.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Inequality"), prefix: new HarmonyMethod(typeof(Program), nameof(Unequal)));
            fixture.Patch(AccessTools.Method(typeof(Interaction), "QueueMissingInputDeliveries"), prefix: new HarmonyMethod(typeof(Program), nameof(SkipDeliveryPlanning)) { priority = Priority.Last });
            fixture.Patch(AccessTools.Method(typeof(Interaction), "ApplyEffects"), prefix: new HarmonyMethod(typeof(Program), nameof(SimulateFeed)) { priority = Priority.Last });
            // Exercise the real flag getter without initializing the BepInEx process/config singleton.
            AlternativeWorkSystem.Flag = Empty<ConfigEntry<bool>>();
            MaterialModuleEnabled(true);
            AlternativeWorkSystem.Reset();
            var wm = Empty<WorkManager>();
            var co = MaterialCO("worker");
            var coworker = MaterialCO("coworker");
            var target = MaterialCO("placeholder");
            var otherTarget = MaterialCO("other-placeholder");
            var install = new Task2 { strTargetCOID = target.strID, strInteraction = "install" };
            var blockedPath = new Task2 { strTargetCOID = target.strID, strInteraction = "other-install" };
            var otherInstall = new Task2 { strTargetCOID = otherTarget.strID, strInteraction = "install" };
            FailMaterialTask(wm, co, install, target);
            FailMaterialTask(wm, coworker, install, target);
            FailMaterialTask(wm, co, otherInstall, otherTarget);
            AlternativeWorkSystem.Failed(co, blockedPath);
            var worker = AlternativeWorkSystem.GetWorker(co);
            var second = AlternativeWorkSystem.GetWorker(coworker);
            var key = new AlternativeWorkSystem.TaskKey(install);
            Check(worker.MaterialWait.Contains(key), "Material failure not distinguished from a path failure");
            var feed = Empty<Interaction>();
            feed.strName = "ACTFeedItemAlarm"; feed.bLot = true;
            AccessTools.Field(typeof(Interaction), "objThemTemp").SetValue(feed, target);
            feedAddsItem = false; feed.ApplyEffects();
            Check(worker.RetryAfter.ContainsKey(key), "Empty/failed delivery cleared a cooldown");
            feedAddsItem = true; feed.ApplyEffects(null, isCancelIa: true);
            Check(worker.RetryAfter.ContainsKey(key), "Cancelled delivery cleared a cooldown");
            feedThrows = true;
            try { feed.ApplyEffects(); } catch (InvalidOperationException) { }
            feedThrows = false;
            Check(worker.RetryAfter.ContainsKey(key), "Throwing delivery cleared a cooldown");
            feed.bLot = false; feed.ApplyEffects();
            Check(worker.RetryAfter.ContainsKey(key), "Non-lot give cleared material waits");
            feed.bLot = true;
            MaterialModuleEnabled(false); feed.ApplyEffects();
            Check(worker.RetryAfter.ContainsKey(key), "Disabled module changed retry eligibility");
            MaterialModuleEnabled(true); feed.ApplyEffects();
            Check(!worker.RetryAfter.ContainsKey(key) && !worker.MaterialWait.Contains(key), "Successful delivery did not release its install");
            Check(!second.RetryAfter.ContainsKey(key), "Delivery failed to wake another worker waiting on the same target");
            Check(worker.RetryAfter.ContainsKey(new AlternativeWorkSystem.TaskKey(blockedPath)), "Delivery released a non-material failure");
            Check(worker.RetryAfter.ContainsKey(new AlternativeWorkSystem.TaskKey(otherInstall)), "Delivery released a different target");
            FailMaterialTask(wm, co, install, target);
            AlternativeWorkSystem.Failed(co, install);
            feed.ApplyEffects();
            Check(worker.RetryAfter.ContainsKey(key) && !worker.MaterialWait.Contains(key), "Later path failure retained an obsolete material-wait tag");
            worker.Clear();
            Check(worker.MaterialWait.Count == 0 && worker.RetryAfter.Count == 0 && worker.ReadyMaterials.Count == 0, "Reset retained material wait state");
        } finally {
            feedAddsItem = feedThrows = false;
            AlternativeWorkSystem.Reset();
            AlternativeWorkSystem.Flag = previousFlag;
            AlternativeWorkSystem.Context = previousContext;
            fixture.UnpatchSelf();
        }
    }
}
