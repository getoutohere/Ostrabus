using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using HarmonyLib;
using Ostrabus.Optimizations;
using Ostranauts.Condowner;
using Ostranauts.Core.Models;
using UnityEngine;

static class Program {
    static int checks, walks;
    static CondOwner actor, local, foreign, queued;
    static Ship home, other;
    static JsonCompany company;
    static Interaction interaction;
    static CondHistory history;
    static readonly HashSet<string> flags = new HashSet<string>();
    static readonly List<CondOwner> candidates = new List<CondOwner>();
    static readonly List<CondOwner> validated = new List<CondOwner>();
    static List<CondOwner> worldResults;
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Set(object obj, string name, object value) => AccessTools.Field(obj.GetType(), name).SetValue(obj, value);
    static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
    static CondOwner CO(string id, Ship ship) {
        var co = Empty<CondOwner>(); Set(co, "_strID", id); Set(co, "_objShip", ship); co.strCODef = id;
        return co;
    }
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = ReferenceEquals(__0, __1); return false; }
    static bool Unequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = !ReferenceEquals(__0, __1); return false; }
    static bool Has(CondOwner __instance, string strName, ref bool __result) { __result = ReferenceEquals(__instance, actor) && flags.Contains(strName); return false; }
    static bool Company(ref JsonCompany __result) { __result = company; return false; }
    static bool True(ref bool __result) { __result = true; return false; }
    static bool False(ref bool __result) { __result = false; return false; }
    static bool Quiet() => false;
    static bool Inventory(ref List<CondOwner> __result) { __result = new List<CondOwner>(); return false; }
    static IEnumerator World(List<CondOwner> results) { yield return null; results.AddRange(candidates); worldResults = results; }
    static bool WorldSearch(List<CondOwner> results, ref IEnumerator __result) { __result = World(results); return false; }
    static bool Definition(ref Interaction __result) { __result = interaction; return false; }
    static bool History(ref CondHistory __result) { __result = history; return false; }
    static bool Trigger(CondTrigger __instance, ref bool __result) { __result = __instance.strName != "TIsPlayerCrew"; return false; }
    static bool Validate(CondOwner objThem, ref bool __result) { validated.Add(objThem); __result = true; return false; }
    static bool Net(ref float __result) { __result = -1; return false; }
    static bool Score(ref float __result) { __result = 0; return false; }
    static bool Queue(CondOwner __0, ref bool __result) { queued = __0; __result = true; return false; }
    static bool Selected(ref CondOwner __result) { __result = actor; return false; }
    static bool Text(string strName, ref string __result) { __result = strName; return false; }
    static IEnumerator Walk(CondOwner co, IteratorResult<bool> result) { walks++; result.Value = co.ship == actor.ship || actor.HasShoreLeave(); yield break; }
    static bool WalkSearch(CondOwner co, IteratorResult<bool> result, ref IEnumerator __result) { __result = Walk(co, result); return false; }
    static void Patch(Harmony h, Type type, string method, string hook, Type[] args = null) {
        var target = AccessTools.Method(type, method, args) ?? throw new Exception("Missing fixture method: " + type + "." + method);
        h.Patch(target, prefix: new HarmonyMethod(typeof(Program), hook));
    }
    static void Getter(Harmony h, Type type, string property, string hook) =>
        h.Patch(AccessTools.PropertyGetter(type, property), prefix: new HarmonyMethod(typeof(Program), hook));
    static void FilterChecks() {
        var list = new List<CondOwner> { foreign, local, actor, foreign, local };
        int capacity = list.Capacity;
        AutonomousDisembarkPatch.Filter(list, actor, interaction);
        Check(list.SequenceEqual(new[] { local, actor, local }), "Stable compaction lost or reordered eligible targets");
        Check(list.Capacity == capacity, "Filtering replaced the candidate buffer");
        foreach (string point in new[] { null, Interaction.POINT_REMOTE }) {
            interaction.strTargetPoint = point; list.Clear(); list.Add(foreign);
            AutonomousDisembarkPatch.Filter(list, actor, interaction);
            Check(list.Count == 1, "Remote/no-walk interaction restricted");
        }
        interaction.strTargetPoint = "use"; interaction.bManual = true; list.Clear(); list.Add(foreign);
        AutonomousDisembarkPatch.Filter(list, actor, interaction); Check(list.Count == 1, "Manual interaction restricted"); interaction.bManual = false;
        foreach (string exemption in new[] { "IsEmergencyOverride", "IsTempDraftedLong" }) {
            flags.Add(exemption); list.Clear(); list.Add(foreign); AutonomousDisembarkPatch.Filter(list, actor, interaction);
            Check(list.Count == 1 && actor.HasShoreLeave(), "Native permission override ignored: " + exemption); flags.Clear();
        }
        company.mapRoster[actor.strID].bShoreLeave = true;
        list.Clear(); list.Add(foreign); AutonomousDisembarkPatch.Filter(list, actor, interaction); Check(list.Count == 1, "Shore leave permission ignored");
        company.mapRoster[actor.strID].bShoreLeave = false;
        var pack = CO("pack", other); Set(pack, "_objCOParent", actor);
        var carried = CO("carried", other); Set(carried, "_objCOParent", pack);
        var unknown = CO("unknown", null);
        list.Clear(); list.AddRange(new[] { carried, unknown, null }); AutonomousDisembarkPatch.Filter(list, actor, interaction);
        Check(list.SequenceEqual(new[] { carried, unknown, null }), "Carried/unknown targets incorrectly discarded");
        Set(pack, "_objCOParent", carried); list.Clear(); list.Add(carried); AutonomousDisembarkPatch.Filter(list, actor, interaction);
        Check(list.Count == 1, "Malformed ownership cycle did not fail open");
        list.Clear(); list.Add(local); AutonomousDisembarkPatch.Filter(list, actor, interaction); Check(list.Count == 1, "Same-ship target restricted");
        list.Clear(); list.Add(foreign); Set(actor, "_objShip", null); AutonomousDisembarkPatch.Filter(list, actor, interaction);
        Check(list.Count == 1, "Actor with unknown ship restricted"); Set(actor, "_objShip", home);
        AutonomousDisembarkPatch.Filter(null, actor, interaction);
        AutonomousDisembarkPatch.Filter(list, null, interaction);
        AutonomousDisembarkPatch.Filter(list, actor, null);
    }
    static void Learned(bool learned) {
        var field = AccessTools.Field(typeof(CondOwner), "aPriorities");
        var priorities = (IList)Activator.CreateInstance(field.FieldType); field.SetValue(actor, priorities);
        actor.mapConds = new Dictionary<string, Condition>();
        if (!learned) return;
        var condition = Empty<Condition>(); condition.strName = "StatFun"; actor.mapConds[condition.strName] = condition;
        priorities.Add(Activator.CreateInstance(field.FieldType.GetGenericArguments()[0], new object[] { -1d, condition }));
    }
    static void Run(bool learned) {
        Learned(learned); queued = null; validated.Clear(); walks = 0; worldResults = null;
        Set(actor, "dictRecentlyTried", new Dictionary<string, double>());
        var choose = (IEnumerator)AccessTools.Method(typeof(CondOwner), "GetMove2").Invoke(actor, null);
        int steps = 0;
        try { while (choose.MoveNext()) Check(++steps < 100, "Native selector failed to finish"); }
        finally { (choose as IDisposable)?.Dispose(); }
    }
    static int Main() {
        var fixture = new Harmony("offline.disembark.fixture");
        Patch(fixture, typeof(UnityEngine.Object), "op_Equality", nameof(Equal)); Patch(fixture, typeof(UnityEngine.Object), "op_Inequality", nameof(Unequal));
        Patch(fixture, typeof(CondOwner), "HasCond", nameof(Has), new[] { typeof(string) });
        Getter(fixture, typeof(CondOwner), "Company", nameof(Company)); Getter(fixture, typeof(CondOwner), "IsHumanOrRobot", nameof(True));
        Getter(fixture, typeof(CondOwner), "bBusy", nameof(False));
        Patch(fixture, typeof(CondOwner), "HasAirlockPermission", nameof(True));
        Patch(fixture, typeof(CondOwner), "GetCOsSafe", nameof(Inventory)); Patch(fixture, typeof(Ship), "GetCosDeferred", nameof(WorldSearch));
        Patch(fixture, typeof(CondOwner), "GetCH", nameof(History)); Patch(fixture, typeof(CondOwner), "GetCOScore", nameof(Score));
        Patch(fixture, typeof(CondOwner), "GetNetInteractionResult", nameof(Net));
        Patch(fixture, typeof(CondOwner), "CheckWalkDeferred", nameof(WalkSearch));
        Patch(fixture, typeof(CondOwner), "QueueInteraction", nameof(Queue), new[] { typeof(CondOwner), typeof(Interaction), typeof(bool), typeof(bool) });
        Patch(fixture, typeof(CondTrigger), "Triggered", nameof(Trigger), new[] { typeof(CondOwner), typeof(string), typeof(bool) });
        Patch(fixture, typeof(Interaction), "Triggered", nameof(Validate), new[] { typeof(CondOwner), typeof(CondOwner), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(List<string>) });
        Patch(fixture, typeof(DataHandler), "GetInteraction", nameof(Definition), new[] { typeof(string), typeof(JsonInteractionSave), typeof(bool) });
        Patch(fixture, typeof(DataHandler), "ReleaseTrackedInteraction", nameof(Quiet)); Patch(fixture, typeof(DataHandler), "KeepInteraction", nameof(Quiet));
        Patch(fixture, typeof(Loot), "ApplyCondLoot", nameof(Quiet));
        Patch(fixture, typeof(Debug), "Log", nameof(Quiet), new[] { typeof(object) });
        Patch(fixture, typeof(CondOwner), "LogMessage", nameof(Quiet));
        Patch(fixture, typeof(DataHandler), "GetString", nameof(Text));
        Patch(fixture, typeof(CrewSim), "GetSelectedCrew", nameof(Selected));
        home = Empty<Ship>(); home.strRegID = "home"; other = Empty<Ship>(); other.strRegID = "other";
        actor = CO("actor", home); local = CO("local-TV", home); foreign = CO("foreign-TV", other);
        actor.aInteractions = new List<string> { "WatchTV" }; actor.socUs = Empty<Social>();
        var clock = AccessTools.Field(typeof(CondOwner), "Clock"); clock.SetValue(actor, FormatterServices.GetUninitializedObject(clock.FieldType));
        Set(actor, "aMyShips", new HashSet<string>()); CrewSim.coPlayer = actor;
        company = new JsonCompany(); company.mapRoster[actor.strID] = new JsonCompanyRules { bShoreLeave = false };
        Set(actor, "_FreeWillLoot", Empty<Loot>());
        AccessTools.Field(typeof(CondOwner), "_ctPlayerCrew").SetValue(null, new CondTrigger { strName = "TIsPlayerCrew" });
        interaction = new Interaction { strName = "WatchTV", strTargetPoint = "use", strThemType = Interaction.TARGET_OTHER,
            bOpener = true, CTTestUs = new CondTrigger { strName = "us" }, CTTestThem = new CondTrigger { strName = "TV" } };
        history = new CondHistory("StatFun"); history.mapInteractions["WatchTV"] = new InteractionHistory("WatchTV") { nIterations = 1, fTotalValue = -1 };
        candidates.AddRange(new[] { foreign, foreign, foreign });
        FilterChecks();
        // Run the shipped selector with engine/world adapters, not a copy of its
        // selection loops. First establish the unpatched failure.
        foreach (bool learned in new[] { false, true }) {
            Run(learned); Check(validated.Count > 0 && validated.All(t => t == foreign) && walks == 1 && queued == null,
                "Native inaccessible-target selection not reproduced (learned=" + learned + ")");
        }
        var production = new Harmony("offline.disembark.production");
        production.CreateClassProcessor(typeof(AutonomousDisembarkPatch)).Patch();
        foreach (bool learned in new[] { false, true }) {
            Run(learned); Check(validated.Count == 0 && walks == 0 && queued == null, "Forbidden target reached contract/path validation");
            Check(worldResults.Count == candidates.Count, "Filter modified cached world search results");
            candidates.Add(local); Run(learned);
            Check(queued == local && walks == 1 && validated.All(t => t == local), "Eligible local target not selected");
            candidates.Remove(local);
            company.mapRoster[actor.strID].bShoreLeave = true; Run(learned);
            Check(queued == foreign && walks == 1, "Permission toggle did not restore foreign selection");
            company.mapRoster[actor.strID].bShoreLeave = false;
            flags.Add("IsEmergencyOverride"); Run(learned); Check(queued == foreign, "Emergency did not restore native foreign selection"); flags.Clear();
        }
        production.UnpatchSelf(); fixture.UnpatchSelf();
        Console.WriteLine(checks + " autonomous disembark checks passed (including both native selection branches).");
        return 0;
    }
}
