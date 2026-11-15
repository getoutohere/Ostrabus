using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using HarmonyLib;
using Ostrabus.Population;

static class Program {
    static int checks, rolls;
    static float value;
    static bool rangeValid = true;
    static readonly MethodInfo gender = AccessTools.Method(typeof(PersonSpec), "GetRandomGender");
    static readonly PersonSpec person = (PersonSpec)FormatterServices.GetUninitializedObject(typeof(PersonSpec));
    static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
    static bool Range(float minInclusive, float maxInclusive, ref float __result) {
        rangeValid &= minInclusive == 0f && maxInclusive == 100f;
        rolls++; __result = value; return false;
    }
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = ReferenceEquals(__0, __1); return false; }
    static bool Name(out string strFirstName, out string strLastName) { strFirstName = "Fixture"; strLastName = "Person"; return false; }
    static bool Home(ref string __result) { __result = "HW_BCRS"; return false; }
    static string Roll(float sample) { value = sample; return (string)gender.Invoke(person, null); }
    static void Distribution(int nb, int male, int female) {
        int before = rolls;
        var counts = new Dictionary<string, int> { ["IsNB"] = 0, ["IsMale"] = 0, ["IsFemale"] = 0 };
        for (int i = 0; i < 10000; i++) counts[Roll((i + 0.5f) / 100f)]++;
        Check(counts["IsNB"] == nb && counts["IsMale"] == male && counts["IsFemale"] == female, "Unexpected native distribution.");
        Check(rolls - before == 10000 && rangeValid, "Changed native RNG count or range.");
    }
    static int Main() {
        var environment = new Harmony("offline.population.environment");
        var production = new Harmony("offline.population.production");
        try {
            environment.Patch(AccessTools.Method(typeof(UnityEngine.Random), nameof(UnityEngine.Random.Range), new[] { typeof(float), typeof(float) }),
                prefix: new HarmonyMethod(typeof(Program), nameof(Range)));
            Distribution(1000, 4400, 4600);
            Check(Roll(10f) == "IsNB" && Roll(54f) == "IsFemale", "Native threshold baseline changed.");
            production.CreateClassProcessor(typeof(RandomGenderPatch)).Patch();
            Distribution(100, 4950, 4950);
            foreach (var sample in new[] { 0f, 0.999f, 1f }) Check(Roll(sample) == "IsNB", "NB boundary changed.");
            foreach (var sample in new[] { 1.001f, 50.499f }) Check(Roll(sample) == "IsMale", "Male boundary changed.");
            foreach (var sample in new[] { 50.5f, 100f }) Check(Roll(sample) == "IsFemale", "Female boundary changed.");

            // Constructor integration: asset assignments bypass this random roll.
            environment.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Equality"), prefix: new HarmonyMethod(typeof(Program), nameof(Equal)));
            environment.Patch(AccessTools.Method(typeof(DataHandler), nameof(DataHandler.GetFullName)), prefix: new HarmonyMethod(typeof(Program), nameof(Name)));
            environment.Patch(AccessTools.Method(typeof(PersonSpec), "GetHomeWorld"), prefix: new HarmonyMethod(typeof(Program), nameof(Home)));
            CrewSim.coPlayer = null;
            foreach (var explicitGender in new[] { "IsNB", "IsMale", "IsFemale" }) {
                int before = rolls;
                var created = new PersonSpec(new JsonPersonSpec { strGender = explicitGender }, false);
                Check(created.strGender == explicitGender && rolls == before, "Explicit asset gender changed or drew random gender.");
            }
            foreach (var selector in new[] { "", "player", "notplayer" }) {
                value = 5f;
                var created = new PersonSpec(new JsonPersonSpec { strGender = selector }, false);
                Check(created.strGender == "IsMale", "Constructor random fallback ignored the new distribution.");
            }
            var reader = typeof(PatchProcessor).GetMethods().Single(m => m.Name == "GetOriginalInstructions" && m.GetParameters().Length == 2 && !m.GetParameters()[1].ParameterType.IsByRef);
            var original = ((IEnumerable<CodeInstruction>)reader.Invoke(null, new object[] { gender, null })).ToList();
            var transpiler = AccessTools.Method(typeof(RandomGenderPatch), "Transpiler");
            var patched = ((IEnumerable<CodeInstruction>)transpiler.Invoke(null, new object[] { original.Select(c => new CodeInstruction(c)).ToList() })).ToList();
            Check(original.Count == patched.Count && original.Zip(patched, (a, b) => a.opcode == b.opcode).All(same => same), "Changed native instruction/control-flow shape.");
            Check(original.Zip(patched, (a, b) => Equals(a.operand, b.operand)).Count(same => !same) == 2, "Changed more than two native operands.");
            var altered = original.Select(c => new CodeInstruction(c)).ToList();
            altered.Single(c => c.opcode == OpCodes.Ldc_R4 && Equals(c.operand, 10f)).operand = 12f;
            bool refused = false;
            try { transpiler.Invoke(null, new object[] { altered }); }
            catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { refused = true; }
            Check(refused, "Changed native thresholds were silently accepted.");
            production.UnpatchSelf();
            Distribution(1000, 4400, 4600);
            Console.WriteLine(checks + " population checks passed: native/revised distribution, boundaries, explicit assets, constructor fallbacks, RNG and IL scope.");
            return 0;
        } finally { production.UnpatchSelf(); environment.UnpatchSelf(); }
    }
}
