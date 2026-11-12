using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ostrabus.Optimizations;

internal static class Program {
    static int checks, warnings;
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Set(object instance, string field, object value) => AccessTools.Field(instance.GetType(), field).SetValue(instance, value);
    static void Check(bool value, string message) {
        checks++;
        if (!value) throw new Exception(message);
    }
    static bool Quiet() => false;
    static bool Warning() { warnings++; return false; }
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) {
        __result = ReferenceEquals(__0, __1);
        return false;
    }
    static CondTrigger Trigger(string id, string[] reqs = null, bool and = true, bool not = false) {
        var trigger = Empty<CondTrigger>();
        trigger.strName = id;
        trigger.aReqs = reqs ?? Array.Empty<string>();
        trigger.aForbids = Array.Empty<string>();
        trigger.aTriggers = Array.Empty<string>();
        trigger.aTriggersForbid = Array.Empty<string>();
        trigger.aLowerConds = Array.Empty<string>();
        trigger.fChance = 1;
        trigger.bAND = and;
        trigger.bNOT = not;
        return trigger;
    }
    static void Link(CondTrigger parent, params CondTrigger[] children) {
        parent.aTriggers = children.Select(child => child.strName).ToArray();
        Set(parent, "aTriggersConds", children.ToDictionary(child => child.strName));
    }
    static CondOwner Owner(int mask) {
        var owner = Empty<CondOwner>();
        owner.mapConds = new Dictionary<string, Condition>();
        string[] names = { "IsHuman", "IsPlayer", "IsSolid", "IsRobot", "StatMass" };
        for (int i = 0; i < names.Length; i++) if ((mask & (1 << i)) != 0) {
            owner.mapConds[names[i]] = new Condition(null) { strName = names[i], fCount = 1 };
        }
        return owner;
    }

    static int Main() {
        var fixtures = new Harmony("ostrabus.offline.trigger-fixtures");
        fixtures.Patch(AccessTools.Method(typeof(UnityEngine.Debug), "LogWarning", new[] { typeof(object) }), prefix: new HarmonyMethod(typeof(Program), nameof(Warning)));
        fixtures.Patch(AccessTools.Method(typeof(CondOwner), "ValidateParent"), prefix: new HarmonyMethod(typeof(Program), nameof(Quiet)));
        fixtures.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Equality"), prefix: new HarmonyMethod(typeof(Program), nameof(Equal)));

        var human = Trigger("human", new[] { "IsHuman" });
        for (int i = 0; i < 4; i++) human.PostInit();
        Check(!human.RequiresHumans && warnings == 1, "Native false-recursion reproduction changed.");
        var mixed = Trigger("mixed", new[] { "IsHuman", "IsSolid" }, and: false);
        mixed.PostInit();
        Check(mixed.RequiresHumans && mixed.Triggered(Owner(4), logOutcome: false), "Native OR misclassification reproduction changed.");

        var production = new Harmony("ostrabus.offline.trigger-production");
        production.CreateClassProcessor(typeof(TriggerClassificationPatch)).Patch();
        for (int i = 0; i < 20; i++) {
            human.PostInit();
            Check(human.RequiresHumans, "Repeated initialization lost human classification.");
        }
        Check(warnings == 1, "Patched classification still emits false recursion warnings.");
        Check(!TriggerClassificationPatch.Classify(mixed), "Human OR object hides object targets.");
        var bothPeople = Trigger("both-people", new[] { "IsHuman", "IsPlayer" }, and: false);
        Check(TriggerClassificationPatch.Classify(bothPeople), "All-person OR was not narrowed.");
        var notHuman = Trigger("not-human", new[] { "IsHuman" }, not: true);
        Check(!TriggerClassificationPatch.Classify(notHuman), "Negation incorrectly narrows targets.");
        var outer = Trigger("outer");
        Link(outer, human);
        Check(TriggerClassificationPatch.Classify(outer), "Nested human requirement missed.");
        var branch = Trigger("branch", and: false);
        Link(branch, human, mixed);
        Check(!TriggerClassificationPatch.Classify(branch), "Nested mixed OR incorrectly narrows targets.");
        var cycleA = Trigger("cycle-a");
        var cycleB = Trigger("cycle-b");
        Link(cycleA, cycleB);
        Link(cycleB, cycleA);
        Check(!TriggerClassificationPatch.Classify(cycleA), "Cycle must retain broad search.");
        var missing = Trigger("missing");
        missing.aTriggers = new[] { "not-linked" };
        Check(!TriggerClassificationPatch.Classify(missing), "Unresolved child must retain broad search.");
        var numeric = Trigger("numeric", new[] { "IsHuman" }, and: false);
        numeric.strHigherCond = "StatMass";
        numeric.aLowerConds = new[] { "IsSolid" };
        Check(!TriggerClassificationPatch.Classify(numeric), "Numeric OR incorrectly narrows targets.");
        var deep = human;
        for (int i = 0; i < 140; i++) {
            var parent = Trigger("deep-" + i);
            Link(parent, deep);
            deep = parent;
        }
        Check(!TriggerClassificationPatch.Classify(deep), "Extreme nesting must keep broad search.");

        var samples = new List<CondTrigger> { human, mixed, bothPeople, notHuman, outer, branch, numeric };
        string[] atoms = { "IsHuman", "IsPlayer", "IsSolid", "IsRobot" };
        for (int mask = 0; mask < 16; mask++) for (int mode = 0; mode < 4; mode++) {
            var trigger = Trigger("combination", atoms.Where((atom, i) => (mask & (1 << i)) != 0).ToArray(), and: (mode & 1) == 0, not: (mode & 2) != 0);
            samples.Add(trigger);
        }
        foreach (var trigger in samples) for (int mask = 0; mask < 32; mask++) {
            bool matches = trigger.Triggered(Owner(mask), logOutcome: false);
            bool people = TriggerClassificationPatch.Classify(trigger);
            Check(!people || (mask & 3) != 0 || !matches, "Classifier excluded a native valid non-person: " + trigger.strName);
        }

        string folder = Path.Combine(Ostrabus.Development.InstallationPaths.Data, "condtrigs");
        var assets = new Dictionary<string, CondTrigger>();
        foreach (string file in Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories).OrderBy(path => path)) foreach (JObject json in JArray.Parse(File.ReadAllText(file))) {
            string id = (string)json["strName"];
            if (id == null) continue;
            var trigger = Trigger(id, json["aReqs"]?.ToObject<string[]>(), (bool?)json["bAND"] ?? false, (bool?)json["bNOT"] ?? false);
            trigger.aTriggers = json["aTriggers"]?.ToObject<string[]>() ?? Array.Empty<string>();
            trigger.strHigherCond = (string)json["strHigherCond"];
            assets[id] = trigger;
        }
        foreach (var trigger in assets.Values) Set(trigger, "aTriggersConds", trigger.aTriggers.Where(assets.ContainsKey).Distinct().ToDictionary(id => id, id => assets[id]));
        var watch = Stopwatch.StartNew();
        int narrowed = assets.Values.Count(TriggerClassificationPatch.Classify);
        watch.Stop();
        Console.WriteLine("Asset metadata: {0} definitions, {1} people-only, {2:F1} ms (offline metadata, not gameplay timing).", assets.Count, narrowed, watch.Elapsed.TotalMilliseconds);
        Check(!TriggerClassificationPatch.Classify(assets["TIsEmbarkTarget"]), "Authored embark target must retain ship/loot targets.");
        Check(!TriggerClassificationPatch.Classify(assets["TIsExplosionTarget"]), "Authored explosion target must retain ordinary objects.");
        Check(TriggerClassificationPatch.Classify(assets["TCanEat"]), "Authored human-only eating search should narrow.");

        long allocationProbe = GC.GetAllocatedBytesForCurrentThread();
        GC.KeepAlive(new byte[1024]);
        bool allocationCounter = GC.GetAllocatedBytesForCurrentThread() > allocationProbe;
        TriggerClassificationPatch.Classify(outer);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) TriggerClassificationPatch.Classify(outer);
        if (allocationCounter) Check(GC.GetAllocatedBytesForCurrentThread() == before, "Warm classification allocates.");
        Console.WriteLine("Reliable per-thread allocation counter: " + allocationCounter);
        var scratch = (System.Collections.IDictionary)AccessTools.Field(typeof(TriggerClassificationPatch), "scratch").GetValue(null);
        Check(scratch.Count == 0, "Scratch retained trigger references.");
        production.UnpatchSelf();
        fixtures.UnpatchSelf();
        Console.WriteLine("PASS: {0} checks; shipped native evaluation used for target-preservation truth tables.", checks);
        return 0;
    }
}
