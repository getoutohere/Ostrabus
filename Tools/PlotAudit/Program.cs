using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ostrabus.Optimizations;

internal static class Program {
    static string Data => Ostrabus.Development.InstallationPaths.Data;
    static bool Quiet() => false;
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) {
        __result = ReferenceEquals(__0, __1);
        return false;
    }
    static CondOwner Owner(params string[] conditions) {
        var owner = (CondOwner)FormatterServices.GetUninitializedObject(typeof(CondOwner));
        owner.mapConds = conditions.ToDictionary(name => name, name => new Condition(null) { strName = name, fCount = 1 });
        return owner;
    }
    static IEnumerable<JObject> Records(string folder) {
        foreach (string file in Directory.GetFiles(Path.Combine(Data, folder), "*.json", SearchOption.AllDirectories).OrderBy(path => path)) {
            foreach (JObject entry in JArray.Parse(File.ReadAllText(file))) yield return entry;
        }
    }
    static Dictionary<string, T> Table<T>(string folder) {
        var result = new Dictionary<string, T>();
        foreach (var json in Records(folder)) {
            string id = (string)json["strName"];
            if (id != null) result[id] = json.ToObject<T>();
        }
        return result;
    }
    static object Describe(JsonPlot plot) {
        var phases = new List<object>();
        var reachable = new HashSet<int> { 0 };
        bool changed;
        do {
            changed = false;
            foreach (int phase in reachable.ToArray()) {
                if (phase < 0 || phase >= plot.aPhases.Length) continue;
                foreach (string name in plot.aPhases[phase]) {
                    if (DataHandler.dictPlotBeats.TryGetValue(name, out var beat) && DataHandler.dictInteractions.ContainsKey(PlotManager.GetIANameFromString(beat.strIATrigger))) {
                        changed |= reachable.Add(phase + beat.nPhaseChange);
                    }
                }
            }
        } while (changed);
        for (int phase = 0; phase < plot.aPhases.Length; phase++) {
            phases.Add(new {
                phase,
                beats = plot.aPhases[phase].Select(name => {
                    DataHandler.dictPlotBeats.TryGetValue(name, out var beat);
                    JsonInteraction interaction = null;
                    if (beat != null) DataHandler.dictInteractions.TryGetValue(PlotManager.GetIANameFromString(beat.strIATrigger), out interaction);
                    return new { requested = name, found = beat != null, beat, interaction };
                }).ToArray(),
                actions = (plot.aIADos != null && phase < plot.aIADos.Length ? plot.aIADos[phase] : Array.Empty<string>()).Select(action => {
                    DataHandler.dictInteractions.TryGetValue(PlotManager.GetIANameFromString(action), out var interaction);
                    return new { requested = action, found = interaction != null, interaction };
                }).ToArray()
            });
        }
        return new {
            plot.strName, plot.strLootNextPlot, phases,
            phaseGraphHasEnding = reachable.Any(phase => phase >= plot.aPhases.Length),
            reachablePhases = reachable.OrderBy(phase => phase).ToArray(),
            limitation = "Structural reachability only; interaction eligibility and choices require separate review."
        };
    }

    static void Main(string[] args) {
        if (args.Length != 1) throw new ArgumentException("Usage: PlotAudit <output.json> (run through bundled Mono host)");
        var fixtures = new Harmony("ostrabus.offline.plot-audit");
        foreach (string method in new[] { "Log", "LogWarning", "LogError" }) {
            fixtures.Patch(AccessTools.Method(typeof(UnityEngine.Debug), method, new[] { typeof(object) }), prefix: new HarmonyMethod(typeof(Program), nameof(Quiet)));
        }
        fixtures.CreateClassProcessor(typeof(TriggerClassificationPatch)).Patch();
        fixtures.Patch(AccessTools.Method(typeof(CondOwner), "ValidateParent"), prefix: new HarmonyMethod(typeof(Program), nameof(Quiet)));
        fixtures.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Equality"), prefix: new HarmonyMethod(typeof(Program), nameof(Equal)));
        foreach (var field in typeof(DataHandler).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)) {
            if (field.FieldType.IsGenericType && field.FieldType.GetGenericTypeDefinition() == typeof(Dictionary<,>)) field.SetValue(null, Activator.CreateInstance(field.FieldType));
        }
        DataHandler.dictConds = Table<JsonCond>("conditions");
        DataHandler.dictCOs = Table<JsonCondOwner>("condowners");
        DataHandler.dictCOOverlays = Table<JsonCOOverlay>("cooverlays");
        DataHandler.dictInteractions = Table<JsonInteraction>("interactions");
        DataHandler.dictPlotBeats = Table<JsonPlotBeat>("plot_beats");
        DataHandler.dictPlots = Table<JsonPlot>("plots");
        DataHandler.dictPledges = Table<JsonPledge>("pledges");
        DataHandler.dictPersonSpecs = Table<JsonPersonSpec>("personspecs");
        DataHandler.dictShipSpecs = Table<JsonShipSpec>("shipspecs");
        foreach (var pair in Table<JsonLoot>("loot")) DataHandler.dictLoot[pair.Key] = Loot.CloneFromJSON(pair.Value);
        foreach (var pair in Table<JsonCondTrigger>("condtrigs")) DataHandler.dictCTs[pair.Key] = CondTrigger.CloneFromJSON(pair.Value);
        foreach (var trigger in DataHandler.dictCTs.Values.ToArray()) trigger.PostInit();
        // Match the native post-load order: interaction overrides precede beats.
        foreach (var definition in Records("interaction_overrides")) definition.ToObject<JsonInteractionOverride>().Generate();

        var generation = new List<object>();
        var overrides = Records("plot_beat_overrides").ToDictionary(json => (string)json["strName"]);
        var generated = new HashSet<string>();
        JsonPlotBeat Generate(string id) {
            if (DataHandler.dictPlotBeats.TryGetValue(id, out var existing)) return existing;
            if (!overrides.TryGetValue(id, out var json) || !generated.Add(id)) return null;
            var definition = json.ToObject<JsonPlotBeatOverride>();
            Generate(definition.strTemplateBeat);
            return definition.Generate();
        }
        foreach (var definition in overrides.Values) {
            if (!((string)definition["strName"]).StartsWith("WrongfullyAccusedCrim", StringComparison.Ordinal)) continue;
            var item = definition.ToObject<JsonPlotBeatOverride>();
            var beat = Generate(item.strName);
            generation.Add(new { requested = item.strName, generated = beat?.strName, definition });
            Console.WriteLine("Native Generate: {0} -> {1}", item.strName, beat?.strName ?? "<null>");
        }
        var original = new[] { "Lonesome", "WrongfullyAccusedCrimBEG" }.Select(id => Describe(DataHandler.dictPlots[id])).ToArray();
        foreach (string id in new[] { "Lonesome", "WrongfullyAccusedCrimBEG" }) {
            var plot = DataHandler.dictPlots[id];
            for (int i = 0; i < plot.aPhases.Length; i++) for (int j = 0; j < plot.aPhases[i].Length; j++) {
                string old = plot.aPhases[i][j];
                if (id == "Lonesome" && old.StartsWith("PLOT_Lonesome_", StringComparison.Ordinal)) plot.aPhases[i][j] = old.Replace("PLOT_Lonesome_", "Beat_Lonesome_");
                if (id == "WrongfullyAccusedCrimBEG" && (old == "WrongfullyAccusedCrimMIDStart" || old == "WrongfullyAccusedCrimMIDTurnIn")) plot.aPhases[i][j] = old + "Reverse";
            }
        }
        var candidate = new[] { "Lonesome", "WrongfullyAccusedCrimBEG" }.Select(id => Describe(DataHandler.dictPlots[id])).ToArray();
        var gateChecks = new List<object>();
        foreach (string condition in new[] { "DcIntimacy01", "DcIntimacy02", "IsChaste" }) {
            bool allowed = DataHandler.dictCTs["CTPLOT_Lonesome_End"].Triggered(Owner("IsHuman", condition), logOutcome: false);
            if (!allowed) throw new Exception("Authored Lonesome exit gate rejected " + condition);
            gateChecks.Add(new { condition, lonesomeEndAllowed = allowed });
        }
        var corpseInteraction = DataHandler.dictInteractions["PLOTWrongfullyAccusedCrimMIDTurnIn"];
        bool corpseAllowed = DataHandler.dictCTs[corpseInteraction.CTTestThem].Triggered(Owner("IsHuman", "IsDead"), logOutcome: false);
        if (corpseAllowed) throw new Exception("Native corpse gate finding changed.");
        gateChecks.Add(new { insuranceCorpseAction = corpseInteraction.strName, corpseAllowed, corpseInteraction.CTTestThem });
        Console.WriteLine("Native gate checks: all three Lonesome recovery exits pass; Insurance Fraud's corpse action rejects dead targets.");
        var interactions = DataHandler.dictInteractions.Where(pair => pair.Key.Contains("Lonesome") || pair.Key.Contains("WrongfullyAccusedCrim"));
        string output = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        File.WriteAllText(output, JsonConvert.SerializeObject(new {
            generation, original, candidate, gateChecks, interactions,
            triggers = DataHandler.dictCTs.Where(pair => pair.Key.Contains("Lonesome") || pair.Key.Contains("WrongfullyAccusedCrim")).Select(pair => new {
                name = pair.Key, pair.Value.bAND, pair.Value.bNOT, pair.Value.aReqs, pair.Value.aForbids, pair.Value.aTriggers, pair.Value.aTriggersForbid
            }),
            loots = DataHandler.dictLoot.Where(pair => pair.Key.Contains("Lonesome") || pair.Key.Contains("WrongfullyAccusedCrim")).Select(pair => new {
                name = pair.Key, pair.Value.strType, pair.Value.aCOs, pair.Value.aLoots
            })
        }, Formatting.Indented));
        Console.WriteLine("Wrote native-generated plot structure to " + output + ". No installed assets or saves changed.");
        fixtures.UnpatchSelf();
    }
}
