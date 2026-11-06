using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using HarmonyLib;
using Ostrabus.Diagnostics;

static partial class Program {
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = ReferenceEquals(__0, __1); return false; }
    static bool Unequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = !ReferenceEquals(__0, __1); return false; }
    static bool Skip() => false;
    static bool NoCond(ref bool __result) { __result = false; return false; }
    static void CheckTimingEvents() {
        var fixture = new Harmony("offline.worktrace.timing.fixture");
        var previousPlayer = CrewSim.coPlayer;
        try {
            fixture.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Equality"), prefix: new HarmonyMethod(typeof(Program), nameof(Equal)));
            fixture.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Inequality"), prefix: new HarmonyMethod(typeof(Program), nameof(Unequal)));
            fixture.Patch(AccessTools.Method(typeof(CondOwner), "HasCond", new[] { typeof(string) }), prefix: new HarmonyMethod(typeof(Program), nameof(NoCond)));
            fixture.Patch(AccessTools.Method(typeof(Condowner.Utils.Scheduler), "StartAIInteractionSample"), prefix: new HarmonyMethod(typeof(Program), nameof(Skip)));
            fixture.Patch(AccessTools.Method(typeof(Interaction), "ApplyEffects"), prefix: new HarmonyMethod(typeof(Program), nameof(Skip)));
            var co = (CondOwner)FormatterServices.GetUninitializedObject(typeof(CondOwner));
            AccessTools.Field(typeof(CondOwner), "_strID").SetValue(co, "fixture-crew");
            co.mapGUIPropMaps = new Dictionary<string, Dictionary<string, string>>();
            CrewSim.coPlayer = co;
            var interaction = (Interaction)FormatterServices.GetUninitializedObject(typeof(Interaction));
            interaction.strName = "ACTFeedItemAlarm";
            AccessTools.Field(typeof(Interaction), "objUsTemp").SetValue(interaction, co);
            AccessTools.Field(typeof(Interaction), "strThemID").SetValue(interaction, "removed-placeholder");
            WorkTrace.Clear();
            Condowner.Utils.Scheduler.StartAIInteractionSample(co, interaction);
            interaction.ApplyEffects();
            var snapshot = (Dictionary<string, object>)WorkTrace.Snapshot();
            var items = ((List<object>)snapshot["items"]).Cast<Dictionary<string, object>>().ToArray();
            if (items.Length != 3) Console.WriteLine(Ostrabus.Inspection.SnapshotJson.Encode(Ostrabus.Inspection.InspectionLog.Read(0, 20)));
            Check(items.Length == 3, "Timing hooks failed to capture start and effect boundaries");
            Check((string)items[0]["stage"] == "interaction-start" && (string)items[1]["stage"] == "effects-begin" && (string)items[2]["stage"] == "effects-returned", "Timing event order changed");
            Check(items.All(e => (string)e["targetId"] == "removed-placeholder"), "Timing events lost the removed target's stored ID");
            Check(items.All(e => e.ContainsKey("crewEpoch") && e.ContainsKey("wallMsSinceTraceStart")), "Timing clock fields missing");
            interaction.ApplyEffects(null, isCancelIa: true);
            Check((long)((Dictionary<string, object>)WorkTrace.Snapshot())["latestSequence"] == 3, "Cancellation effects were labeled normal completion");
            interaction.strName = "SOCDarkJoke";
            Condowner.Utils.Scheduler.StartAIInteractionSample(co, interaction);
            interaction.ApplyEffects();
            Check((long)((Dictionary<string, object>)WorkTrace.Snapshot())["latestSequence"] == 3, "Social action polluted work timing trace");
        } finally {
            CrewSim.coPlayer = previousPlayer;
            fixture.UnpatchSelf();
            WorkTrace.Clear();
        }
    }
}
