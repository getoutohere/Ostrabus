using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

static class Program
{
    static int checks;
    static readonly List<string> configured = new List<string>();
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Check(bool value, string reason) { checks++; if (!value) throw new Exception(reason); }
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = ReferenceEquals(__0, __1); return false; }
    static bool Unequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = !ReferenceEquals(__0, __1); return false; }
    static GameObject FixtureGameObject(Component component) => null;
    static AudioEmitter FixtureEmitter(GameObject gameObject) => Empty<AudioEmitter>();
    static void Configure(AudioEmitter emitter, JsonAudioEmitter data) => configured.Add(data.strName);

    // Replace only Unity object creation/configuration. GasPump.Start itself,
    // including profile selection, null checks and emitter ownership, stays native.
    static IEnumerable<CodeInstruction> EngineFixtures(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            if (instruction.operand is MethodInfo method)
            {
                string fixture = null;
                if (method.DeclaringType == typeof(Component) && method.Name == "get_gameObject") fixture = nameof(FixtureGameObject);
                else if (method.DeclaringType == typeof(GameObject) && method.Name == "AddComponent" && method.IsGenericMethod && method.GetGenericArguments()[0] == typeof(AudioEmitter)) fixture = nameof(FixtureEmitter);
                else if (method.DeclaringType == typeof(AudioEmitter) && method.Name == nameof(AudioEmitter.SetData)) fixture = nameof(Configure);
                if (fixture != null)
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(Program), fixture);
                }
            }
            yield return instruction;
        }
    }

    static string[] Start(JsonGasRespire profile)
    {
        configured.Clear();
        var co = Empty<CondOwner>();
        AccessTools.Field(typeof(CondOwner), "_objShip").SetValue(co, Empty<Ship>());
        var pump = Empty<GasPump>();
        AccessTools.Field(typeof(GasPump), "co").SetValue(pump, co);
        AccessTools.Field(typeof(GasPump), "gr").SetValue(pump, profile);
        AccessTools.Method(typeof(GasPump), "Start").Invoke(pump, null);
        return configured.ToArray();
    }

    static int Main()
    {
        try { Run(); Console.WriteLine("PASS: " + checks + " scrubber audio/native asset checks."); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    static void Run()
    {
        string data = Ostrabus.Development.InstallationPaths.Data;
        string mod = Path.GetFullPath("MiscBugFixes/ScrubberAudio/Assets/data/gasrespires/scrubber_audio.json");
        var profiles = new Dictionary<string, JsonGasRespire>();
        DataHandler.JsonToData(Path.Combine(data, "gasrespires/gasrespires.json"), profiles);
        Check(profiles.ContainsKey("AtmoScrubber01"), "Native profile loader must succeed.");
        var original = profiles.ToDictionary(pair => pair.Key, pair => pair.Value);
        DataHandler.dictAudioEmitters = new Dictionary<string, JsonAudioEmitter>();
        DataHandler.JsonToData(Path.Combine(data, "audioemitters/audioemitters.json"), DataHandler.dictAudioEmitters);
        var engine = new Harmony("offline.scrubber-audio.engine-fixtures");
        engine.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Equality"), prefix: new HarmonyMethod(typeof(Program), nameof(Equal)));
        engine.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Inequality"), prefix: new HarmonyMethod(typeof(Program), nameof(Unequal)));
        engine.Patch(AccessTools.Method(typeof(GasPump), "Start"), transpiler: new HarmonyMethod(typeof(Program), nameof(EngineFixtures)));

        Check(Start(original["AtmoScrubber01"]).SequenceEqual(new[] { "AirPumpActiveAir", "AirPumpActive" }), "Reproduce 1.1 attaching both extra generic pump loops to CO2 scrubbers.");
        DataHandler.JsonToData(mod, profiles);
        Check(profiles.Count == original.Count, "Override must add no profile identifiers.");
        Check(JArray.Parse(File.ReadAllText(mod)).Count == 1, "Exactly one gas profile is overridden.");
        foreach (var pair in original)
        {
            var before = JObject.FromObject(pair.Value);
            var after = JObject.FromObject(profiles[pair.Key]);
            if (pair.Key == "AtmoScrubber01")
            {
                foreach (string key in new[] { "strAudioEmitterAir", "strAudioEmitterPump" })
                {
                    Check(after[key].Type == JTokenType.Null, "Extra emitter must be explicitly null: " + key);
                    before.Remove(key); after.Remove(key);
                }
            }
            Check(JToken.DeepEquals(before, after), "No gas, control, charge or other profile changes: " + pair.Key);
            if (pair.Key != "AtmoScrubber01")
                Check(Start(pair.Value).SequenceEqual(Start(profiles[pair.Key])), "Preserve native audio selection for other pumps, suits and breathing: " + pair.Key);
        }
        for (int i = 0; i < 4; i++)
        {
            Check(Start(profiles["AtmoScrubber01"]).Length == 0, "No generic loops after repeated device creation/reload.");
            DataHandler.JsonToData(mod, profiles);
        }
        var definitions = JArray.Parse(File.ReadAllText(Path.Combine(data, "condowners/condowners.json")));
        Check(definitions.Single(item => (string)item["strName"] == "ItmAtmoScrubber01")["strAudioEmitter"].Value<string>() == "ItmAtmoScrubberOnSound", "Native scrubber device hum remains authored and untouched.");
        Check(DataHandler.dictAudioEmitters["ItmAtmoScrubberOnSound"].strClipSteady == "lp_drone_02", "Existing quiet device hum is retained.");
    }
}
