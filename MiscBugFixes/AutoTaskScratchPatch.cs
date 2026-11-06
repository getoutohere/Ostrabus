using System;
using System.Collections.Generic;
using HarmonyLib;

namespace Ostrabus.MiscBugFixes;

// Native ProcessAutoTasks assigns aATsRestore to another category's list to
// choose its merged order. Preserve the three independent scratch buffers across
// calls, or subsequent scans insert different categories into the same list.
[HarmonyPatch(typeof(CondOwner), "ProcessAutoTasks")]
internal static class AutoTaskScratchPatch {
    static readonly AccessTools.FieldRef<CondOwner, List<AutoTask>> PatchTasks = AccessTools.FieldRefAccess<CondOwner, List<AutoTask>>("aATsPatch");
    static readonly AccessTools.FieldRef<CondOwner, List<AutoTask>> RepairTasks = AccessTools.FieldRefAccess<CondOwner, List<AutoTask>>("aATsRepair");
    static readonly AccessTools.FieldRef<CondOwner, List<AutoTask>> RestoreTasks = AccessTools.FieldRefAccess<CondOwner, List<AutoTask>>("aATsRestore");
    internal readonly struct Scratch {
        internal readonly List<AutoTask> Patch, Repair, Restore;
        internal Scratch(List<AutoTask> patch, List<AutoTask> repair, List<AutoTask> restore) { Patch = patch; Repair = repair; Restore = restore; }
    }
    [HarmonyPrefix]
    internal static void Prefix(CondOwner __instance, out Scratch __state) {
        var patch = PatchTasks(__instance) ?? new List<AutoTask>();
        var repair = RepairTasks(__instance);
        if (repair == null || ReferenceEquals(repair, patch)) repair = new List<AutoTask>();
        var restore = RestoreTasks(__instance);
        if (restore == null || ReferenceEquals(restore, patch) || ReferenceEquals(restore, repair)) restore = new List<AutoTask>();
        PatchTasks(__instance) = patch; RepairTasks(__instance) = repair; RestoreTasks(__instance) = restore;
        __state = new Scratch(patch, repair, restore);
    }
    [HarmonyFinalizer]
    internal static Exception Finalizer(CondOwner __instance, Exception __exception, Scratch __state) {
        if (__state.Patch != null) {
            __state.Patch.Clear(); __state.Repair.Clear(); __state.Restore.Clear();
            PatchTasks(__instance) = __state.Patch; RepairTasks(__instance) = __state.Repair; RestoreTasks(__instance) = __state.Restore;
        }
        return __exception;
    }
}
