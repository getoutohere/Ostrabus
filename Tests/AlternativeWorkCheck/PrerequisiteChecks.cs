using System;
using System.Collections.Generic;
using HarmonyLib;
using Ostrabus.AlternativeWork;

static partial class Program {
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = ReferenceEquals(__0, __1); return false; }
    static bool Unequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = !ReferenceEquals(__0, __1); return false; }
    static void CheckPrerequisites() {
        var fixture = new Harmony("altwork.prerequisite.fixture");
        var previousMap = DataHandler.mapCOs;
        var previousContext = AlternativeWorkSystem.Context;
        try {
            fixture.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Equality"), prefix: new HarmonyMethod(typeof(Program), nameof(Equal)));
            fixture.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Inequality"), prefix: new HarmonyMethod(typeof(Program), nameof(Unequal)));
            var wm = Empty<WorkManager>();
            var task = new Task2 { strDuty = "Construct", strTargetCOID = "placeholder", strInteraction = "ACTFeedItemTV" };
            var pending = new List<Task2> { task };
            var active = new List<Task2>();
            Field(wm, "dictTasks2", new Dictionary<string, List<Task2>> { [task.strDuty] = pending });
            Field(wm, "aTasksActive", active);
            // Duplicate rejection exits the native method before construction signs or GUI calls.
            DataHandler.mapCOs = new Dictionary<string, CondOwner> { [task.strTargetCOID] = null };
            foreach (bool duplicateActive in new[] { false, true }) {
                if (duplicateActive) { pending.Clear(); active.Add(task); }
                foreach (bool alternative in new[] { false, true }) {
                    AlternativeWorkSystem.Context = alternative ? new AlternativeWorkSystem.SearchContext { Manager = wm } : null;
                    AlternativeWorkSystem.Spawned(wm) = 0;
                    Check(!wm.AddTask(task), "Existing material delivery should not be duplicated");
                    Check(AlternativeWorkSystem.Spawned(wm) == 1, "Lost material prerequisite signal; native selection would enter the suit fallback");
                    AlternativeWorkSystem.Spawned(wm) = 7;
                    Check(!wm.AddTask(task) && AlternativeWorkSystem.Spawned(wm) == 8, "Existing prerequisite counter must be preserved and incremented");
                    Check(pending.Count + active.Count == 1, "Duplicate request changed native pending/active tasks");
                }
            }
            AlternativeWorkSystem.Context = new AlternativeWorkSystem.SearchContext { Manager = wm };
            AlternativeWorkSystem.Spawned(wm) = 0;
            Check(!wm.AddTask(null) && AlternativeWorkSystem.Spawned(wm) == 0, "Invalid requests must not signal prerequisites");
        } finally {
            AlternativeWorkSystem.Context = previousContext;
            DataHandler.mapCOs = previousMap;
            fixture.UnpatchSelf();
        }
    }
}
