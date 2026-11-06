using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Ostrabus.AlternativeWork;

[HarmonyPatch(typeof(WorkManager), nameof(WorkManager.ClaimNextTask))]
internal static class WorkSelectionPatch {
    [HarmonyPostfix]
    static void Postfix(WorkManager __instance, CondOwner co, ref IEnumerator __result) {
        if (AlternativeWorkSystem.Enabled && __result != null) __result = new AlternativeWorkSystem.Selection(__result, __instance, co);
    }
}

[HarmonyPatch(typeof(WorkManager), "CollectTasks")]
internal static class WorkCollectionPatch {
    [HarmonyPrefix]
    static bool Prefix(WorkManager __instance, string strDuty, CondOwner co, bool bAIManual, ref List<Task2> __result) {
        var context = AlternativeWorkSystem.Context;
        if (context == null || context.VanillaCollection || context.Manager != __instance || context.Crew != co) return true;
        try { __result = AlternativeWorkSystem.Collect(__instance, strDuty, co, bAIManual, context.Worker); }
        catch (Exception ex) { AlternativeWorkSystem.PlannerFailed(context, ex); return true; }
        return false;
    }
}

[HarmonyPatch]
internal static class WorkFailurePatch {
    static IEnumerable<MethodBase> TargetMethods() {
        yield return AccessTools.Method(typeof(WorkManager), "FailTask", new[] { typeof(Task2), typeof(Interaction), typeof(CondOwner), typeof(CondOwner) });
        yield return AccessTools.Method(typeof(WorkManager), "FailTask", new[] { typeof(Task2), typeof(CondOwner), typeof(CondOwner), typeof(string), typeof(string) });
    }
    struct State { internal bool Active, MaterialPrerequisite; internal double LastCheck; }
    [HarmonyPrefix]
    static void Prefix(Task2 task, CondOwner co, ref State __state) {
        var context = AlternativeWorkSystem.Context;
        if (context == null || context.Crew != co) return;
        __state = new State { Active = true, LastCheck = task.fLastCheck, MaterialPrerequisite = ReferenceEquals(context.MaterialPrerequisite, task) };
        context.MaterialPrerequisite = null;
    }
    [HarmonyPostfix]
    static void Postfix(Task2 task, CondOwner co, State __state) {
        if (!__state.Active) return;
        task.fLastCheck = __state.LastCheck;
        AlternativeWorkSystem.Failed(co, task, __state.MaterialPrerequisite);
    }
}

[HarmonyPatch(typeof(WorkManager), "FinalizeTask")]
internal static class WorkFinalizePatch {
    [HarmonyPostfix]
    static void Postfix(WorkManager __instance, CondOwner co, Task2 __result) {
        var context = AlternativeWorkSystem.Context;
        if (!AlternativeWorkSystem.Enabled || context == null || context.VanillaCollection || context.Manager != __instance || context.Crew != co || __result == null) return;
        context.Worker?.Supply.Arm(__result);
    }
    [HarmonyPrefix]
    static bool Prefix(WorkManager __instance, CondOwner co, Task2 task, ref Task2 __result) {
        if (AlternativeWorkSystem.Context?.Manager != __instance) return true;
        var ownership = task.GetOwnership(co.strID);
        if (ownership != Task2.Allowed.Forbidden && (!co.HasCond("IsAIManual") || ownership == Task2.Allowed.Owned) &&
            AlternativeWorkSystem.Allowed(co, task) && AlternativeWorkSystem.Tasks(__instance).TryGetValue(task.strDuty, out var pending) && pending.Contains(task)) {
            AlternativeWorkSystem.Claims++;
            return true;
        }
        // A coworker may have taken the order while a deferred validation yielded.
        __result = null;
        return false;
    }
}

[HarmonyPatch(typeof(WorkManager), nameof(WorkManager.ClaimTaskDirect))]
internal static class WorkDelayedClaimPatch {
    [HarmonyPrefix]
    static bool Prefix(Interaction iact) {
        if (!AlternativeWorkSystem.Enabled || iact == null || iact.bManual || !AlternativeWorkSystem.Eligible(iact.objUs)) return true;
        // The suit pledge returns through this entry point. Vanilla omits its path recheck.
        var task = new Task2 { strTargetCOID = iact.objThem?.strID, strInteraction = iact.strName };
        return AlternativeWorkSystem.Allowed(iact.objUs, task) && iact.Triggered(iact.objUs, iact.objThem, bStats: false, bIgnoreItems: false, bCheckPath: true, bFetchItems: false);
    }
}

[HarmonyPatch(typeof(WorkManager), "HandleSuitRequirement")]
internal static class WorkSuitPermissionPatch {
    [HarmonyPrefix]
    static bool Prefix(CondOwner co, Task2 task, Interaction iact, ref Task2 __result) {
        if (AlternativeWorkSystem.Context == null || !iact.bAirlockBlocked || co.HasAirlockPermission(task.bManual)) return true;
        if (co.Company == null || !co.Company.mapRoster.TryGetValue(co.strID, out var rules) || rules.bAirlockPermission) return true;
        AlternativeWorkSystem.Failed(co, task);
        __result = null;
        return false;
    }
}

[HarmonyPatch(typeof(WorkManager), nameof(WorkManager.CollectBatchHaulTasks))]
internal static class WorkBatchHaulPatch {
    [HarmonyPostfix]
    static void Postfix(CondOwner coHauler, List<Tuple<Task2, JsonZone>> __result) {
        if (!AlternativeWorkSystem.Enabled || __result == null || !AlternativeWorkSystem.Eligible(coHauler)) return;
        for (int i = __result.Count - 1; i >= 0; i--) if (!AlternativeWorkSystem.Allowed(coHauler, __result[i].Item1)) __result.RemoveAt(i);
    }
}

[HarmonyPatch(typeof(WorkManager), nameof(WorkManager.CollectBatchFeedTasks))]
internal static class WorkBatchFeedPatch {
    [HarmonyPostfix]
    static void Postfix(CondOwner coHauler, Dictionary<Task2, CondOwner> __result) {
        if (!AlternativeWorkSystem.Enabled || __result == null || !AlternativeWorkSystem.Eligible(coHauler)) return;
        List<Task2> remove = null;
        foreach (Task2 task in __result.Keys) if (task.GetOwnership(coHauler.strID) == Task2.Allowed.Forbidden || !AlternativeWorkSystem.Allowed(coHauler, task)) {
            if (remove == null) remove = new List<Task2>();
            remove.Add(task);
        }
        if (remove != null) foreach (Task2 task in remove) __result.Remove(task);
    }
}

[HarmonyPatch(typeof(ConsoleResolver), nameof(ConsoleResolver.ResolveString))]
internal static class WorkConsolePatch {
    [HarmonyPrefix]
    static bool Prefix(string strInput) {
        if (strInput == null) return true;
        string command = strInput.Trim().ToLowerInvariant();
        if (command != "altwork" && !command.StartsWith("altwork ", StringComparison.Ordinal)) return true;
        string argument = command.Length > 7 ? command.Substring(7).Trim() : "status";
        if (argument == "on") AlternativeWorkSystem.SetEnabled(true);
        else if (argument == "off") AlternativeWorkSystem.SetEnabled(false);
        else if (argument == "toggle") AlternativeWorkSystem.SetEnabled(!AlternativeWorkSystem.Enabled);
        else if (argument.StartsWith("window ", StringComparison.Ordinal) && int.TryParse(argument.Substring(7).Trim(), out int window)) WorkSupplyPlan.SetWindow(window);
        else if (argument == "status" || argument.Length == 0) {
            ConsoleToGUI.instance?.LogInfo($"[AlternativeWork] {(AlternativeWorkSystem.Enabled ? "ON" : "OFF")}; supply window {WorkSupplyPlan.Window}, searches {AlternativeWorkSystem.Searches}, claims {AlternativeWorkSystem.Claims}, failed attempts {AlternativeWorkSystem.Failures}, permission exclusions {AlternativeWorkSystem.Filtered}, planner {AlternativeWorkSystem.PlanningMs:F2} ms total. Read-only details: GET /workorders.");
        } else ConsoleToGUI.instance?.LogInfo("Usage: altwork on | off | toggle | status | window 1..30");
        return false;
    }
}
