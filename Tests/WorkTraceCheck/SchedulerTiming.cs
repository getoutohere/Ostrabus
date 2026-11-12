using System;
using System.Reflection;
using System.Runtime.Serialization;
using Condowner.Utils;
using HarmonyLib;
using UnityEngine;

// Conditional native reproduction, not a scheduler replacement. Only the empty
// work-manager leaf and engine clock are prescribed. Run/Tick/Clock execute native.
static class SchedulerTiming {
    static int checks;
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static bool Quiet() => false;
    static bool RealTime(ref double __result) { __result = 10; return false; }
    static bool Frame(ref int __result) { __result = 99; return false; }
    static void Check(bool ok, string why) { checks++; if (!ok) throw new Exception(why); }
    internal static void Run() {
        var h = new Harmony("offline.scheduler.budget-clock");
        var previousCrew = CrewSim.objInstance;
        double previousEpoch = StarSystem.fEpoch;
        var dispatcher = (EndTurnDispatcher)AccessTools.Field(typeof(Scheduler), "_endTurnDispatcher").GetValue(null);
        var active = AccessTools.Field(typeof(EndTurnDispatcher), "_active");
        try {
            h.Patch(AccessTools.PropertyGetter(typeof(Time), "realtimeSinceStartupAsDouble"), prefix: new HarmonyMethod(typeof(SchedulerTiming), nameof(RealTime)));
            h.Patch(AccessTools.PropertyGetter(typeof(Time), "frameCount"), prefix: new HarmonyMethod(typeof(SchedulerTiming), nameof(Frame)));
            h.Patch(AccessTools.Method(typeof(WorkManager), "WorkOnClaims"), prefix: new HarmonyMethod(typeof(SchedulerTiming), nameof(Quiet)));
            CrewSim.objInstance = Empty<CrewSim>();
            CrewSim.objInstance.workManager = Empty<WorkManager>();
            StarSystem.fEpoch = 100000;
            var ordinary = new Clock();
            double before = Scheduler.ProcessedTime;
            StarSystem.fEpoch += .5;
            Scheduler.Run(.5);
            Check(Scheduler.ProcessedTime == before + .5 && ordinary.Epoch == 100000.5,
                "Native idle-dispatcher control did not advance ordinary time");
            // Tick returns at the real exhausted-budget check before executing
            // the active request. Its owner/operation need not be materialized.
            active.SetValue(dispatcher, new Request(Empty<CondOwner>(), new EndTurnHandle()));
            AccessTools.Field(typeof(EndTurnDispatcher), "_budgetFrame").SetValue(dispatcher, 99);
            AccessTools.Field(typeof(EndTurnDispatcher), "_frameDeadline").SetValue(dispatcher, 9d);
            for (int i = 0; i < 4; i++) {
                StarSystem.fEpoch += .5;
                Scheduler.Run(.5);
                Check(Scheduler.ProcessedTime == before + .5 && ordinary.Epoch == 100000.5,
                    "Native budget-exhausted Run did not reproduce skipped ordinary time");
            }
            active.SetValue(dispatcher, null);
            StarSystem.fEpoch += .25;
            Scheduler.Run(.25);
            Check(Scheduler.ProcessedTime == before + .75 && ordinary.Epoch == 100000.75,
                "Scheduler replayed skipped time after dispatcher cleared");
            Check(StarSystem.fEpoch - ordinary.Epoch == 2,
                "Native world/ordinary-clock divergence was not retained");
            Console.WriteLine(checks + " native scheduler timing checks passed: 2 s of budget-blocked world time is not replayed for an ordinary clock. No Ostrabus gameplay patches installed.");
        } finally {
            active.SetValue(dispatcher, null); CrewSim.objInstance = previousCrew; StarSystem.fEpoch = previousEpoch;
            h.UnpatchSelf();
        }
    }
}
