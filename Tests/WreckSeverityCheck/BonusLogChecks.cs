using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BepInEx.Logging;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ostrabus.Development;
using Ostrabus.QoL.WreckSeverity;

static class BonusLogChecks {
    static readonly Dictionary<string, double> counts = new Dictionary<string, double>();
    static readonly List<string> messages = new List<string>();
    static float draw;
    static int draws, writes;
    static bool failPlot;
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Set(object instance, string field, object value) => AccessTools.Field(instance.GetType(), field).SetValue(instance, value);
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = ReferenceEquals(__0, __1); return false; }
    static bool Unequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = !ReferenceEquals(__0, __1); return false; }
    static bool Selected(ref CondOwner __result) { __result = CrewSim.coPlayer; return false; }
    static bool Quiet() => false;
    static bool Plot(ref bool __result) { if (failPlot) throw new InvalidOperationException("fixture plot failure"); __result = false; return false; }
    static bool Amount(string strName, ref double __result) { __result = counts.TryGetValue(strName, out var value) ? value : 0; return false; }
    static bool Has(string strName, ref bool __result) { __result = counts.ContainsKey(strName); return false; }
    static bool Add(string strName, double fAmount) { writes++; counts.TryGetValue(strName, out var value); counts[strName] = value + fAmount; return false; }
    static bool Zero(string strName) { counts.Remove(strName); return false; }
    static bool Rand(float min, float max, ref float __result) { draws++; __result = min + (max - min) * draw; return false; }
    static bool Trigger(ref bool __result) { __result = true; return false; }
    static bool GetTrigger(string strName, ref CondTrigger __result) {
        __result = new CondTrigger { strName = strName, strCondName = strName == "TUpIsDueBonusDerelict" ? WreckBonusLog.Condition : "OTHER", fCount = 1, fChance = 1 };
        return false;
    }
    static void Patch(Harmony h, Type type, string name, string replacement, Type[] arguments = null) =>
        h.Patch(AccessTools.Method(type, name, arguments), prefix: new HarmonyMethod(typeof(BonusLogChecks), replacement));
    static void Reset() { counts.Clear(); counts["IsDueVideotapeSpawn"] = 1; draws = writes = 0; draw = .3f; messages.Clear(); }
    static void Apply(Interaction ia, Loot loot, CondOwner actor) => AccessTools.Method(typeof(Interaction), "ApplyLootCT")
        .Invoke(ia, new object[] { loot, null, actor, null, 1f });

    internal static void Run(Action<bool, string> check) {
        var scene = new Harmony("ostrabus.tests.wreckbonus.scene");
        var production = new Harmony("ostrabus.tests.wreckbonus.production");
        var oldPlayer = CrewSim.coPlayer; var oldLogger = Ostrabus.OstrabusPlugin.Logger;
        var oldRelease = WreckBonusLog.Release; var oldCapture = WreckDamageLog.Current;
        var chances = AccessTools.Field(typeof(BeatManager), "dictEventChances"); var oldChances = chances.GetValue(null);
        var roll = AccessTools.Field(typeof(BeatManager), "fRoll"); var oldRoll = roll.GetValue(null);
        double oldEpoch = StarSystem.fEpoch;
        try {
            var release = AccessTools.Method(typeof(BeatManager), nameof(BeatManager.GenerateRelease));
            var interaction = AccessTools.Method(typeof(Interaction), "ApplyLootCT");
            WreckDamageLog.Enabled = false;
            production.CreateClassProcessor(typeof(WreckBonusReleasePatch)).Patch();
            production.CreateClassProcessor(typeof(WreckBonusInteractionPatch)).Patch();
            check(Harmony.GetPatchInfo(release)?.Owners.Contains(production.Id) != true &&
                Harmony.GetPatchInfo(interaction)?.Owners.Contains(production.Id) != true, "Default-off bonus logging installed hooks");

            Patch(scene, typeof(UnityEngine.Object), "op_Equality", nameof(Equal));
            Patch(scene, typeof(UnityEngine.Object), "op_Inequality", nameof(Unequal));
            Patch(scene, typeof(CrewSim), nameof(CrewSim.GetSelectedCrew), nameof(Selected));
            Patch(scene, typeof(CondOwner), nameof(CondOwner.GetCondAmount), nameof(Amount), new[] { typeof(string) });
            Patch(scene, typeof(CondOwner), nameof(CondOwner.HasCond), nameof(Has), new[] { typeof(string) });
            Patch(scene, typeof(CondOwner), nameof(CondOwner.AddCondAmount), nameof(Add));
            Patch(scene, typeof(CondOwner), nameof(CondOwner.ZeroCondAmount), nameof(Zero));
            Patch(scene, typeof(CondOwner), nameof(CondOwner.RememberEffects2), nameof(Quiet));
            Patch(scene, typeof(CondTrigger), nameof(CondTrigger.Triggered), nameof(Trigger));
            Patch(scene, typeof(DataHandler), nameof(DataHandler.GetCondTrigger), nameof(GetTrigger), new[] { typeof(string) });
            Patch(scene, typeof(PlotManager), nameof(PlotManager.CheckPlots), nameof(Plot));
            Patch(scene, typeof(BeatManager), "End79AUAdvanceSpecial", nameof(Quiet));
            Patch(scene, typeof(MathUtils), nameof(MathUtils.Rand), nameof(Rand), new[] { typeof(float), typeof(float), typeof(MathUtils.RandType), typeof(string) });
            var actor = Empty<CondOwner>(); var ship = Empty<Ship>(); ship.strRegID = "O-BONUS-TEST";
            Set(actor, "_objShip", ship); Set(actor, "_strID", "Bonus test pilot"); CrewSim.coPlayer = actor;
            var settings = JArray.Parse(File.ReadAllText(Path.Combine(InstallationPaths.Data, "plot_manager", "pm_settings.json")))[0];
            chances.SetValue(null, settings["dictEventChances"].ToObject<Dictionary<string, float>>());
            StarSystem.fEpoch = 12345;
            var logger = new ManualLogSource("Bonus log fixture"); logger.LogEvent += (_, e) => messages.Add(e.Data.ToString());
            Ostrabus.OstrabusPlugin.Logger = logger;

            Reset(); BeatManager.GenerateRelease();
            int baselineDraws = draws, baselineWrites = writes; double baselineCount = counts[WreckBonusLog.Condition];
            check(baselineCount == 1 && baselineDraws == 1 && messages.Count == 0, "Native negative-roll award control failed");
            WreckDamageLog.Enabled = true;
            production.CreateClassProcessor(typeof(WreckBonusReleasePatch)).Patch();
            production.CreateClassProcessor(typeof(WreckBonusInteractionPatch)).Patch();
            Reset(); BeatManager.GenerateRelease();
            check(draws == baselineDraws && writes == baselineWrites && counts[WreckBonusLog.Condition] == baselineCount,
                "Release diagnostics changed native draws, mutations or bonus count");
            check(messages.Count == 1 && messages[0].Contains("cause=BeatManager.BonusDerelict") &&
                messages[0].Contains("roll=-0.25; threshold=0.1; negative-roll=true") &&
                messages[0].Contains("plot=no-event(0.3->-0.25)") && messages[0].Contains("videotape=no-event(-0.25->-0.25)"),
                "Release log did not explain the native negative-roll fallback");
            check(!WreckBonusLog.Release.Active, "Release scope survived the completed call");
            counts.Remove("IsReleaseCooldown"); BeatManager.GenerateRelease();
            check(messages.Count == 2 && messages[1].Contains("count=1->2"), "Repeated pending awards were hidden or consumed");

            Reset(); draw = .6f; BeatManager.GenerateRelease();
            check(messages.Count == 1 && messages[0].Contains("roll=0.05; threshold=0.1; negative-roll=false"),
                "A normal positive-roll bonus was mislabeled as fallback");
            int countBeforeCooldown = messages.Count; int drawsBeforeCooldown = draws;
            BeatManager.GenerateRelease();
            check(messages.Count == countBeforeCooldown && draws == drawsBeforeCooldown,
                "An ineligible native cooldown call generated a log or random draw");

            EventHandler<LogEventArgs> brokenSink = (_, _) => throw new InvalidOperationException("fixture log failure");
            logger.LogEvent += brokenSink;
            Reset(); BeatManager.GenerateRelease();
            logger.LogEvent -= brokenSink;
            check(counts[WreckBonusLog.Condition] == 1 && !WreckBonusLog.Release.Active,
                "A logging failure changed the native award or leaked context");

            Reset(); draw = .8f; BeatManager.GenerateRelease();
            check(messages.Count == 0 && !counts.ContainsKey(WreckBonusLog.Condition), "Rejected native roll produced an award log");
            Reset(); failPlot = true;
            try { BeatManager.GenerateRelease(); check(false, "Native selection exception was swallowed"); }
            catch (InvalidOperationException e) { check(e.Message == "fixture plot failure", "Native selection exception changed"); }
            finally { failPlot = false; }
            check(!WreckBonusLog.Release.Active && messages.Count == 0, "Failed selection leaked context or fabricated an award");

            var lootJson = JArray.Parse(File.ReadAllText(Path.Combine(InstallationPaths.Data, "loot", "loot.json")))
                .Single(x => (string)x["strName"] == "CTACTPlayToyDinoBoth");
            var loot = Loot.CloneFromJSON(lootJson.ToObject<JsonLoot>());
            var ia = Empty<Interaction>(); ia.strName = "ACTPlayToyDinoBoth"; ia.bNoRemember = true;
            Reset(); WreckDamageLog.Enabled = false; Apply(ia, loot, actor);
            baselineDraws = draws; baselineWrites = writes; baselineCount = counts[WreckBonusLog.Condition];
            Reset(); WreckDamageLog.Enabled = true; Apply(ia, loot, actor);
            check(draws == baselineDraws && writes == baselineWrites && counts[WreckBonusLog.Condition] == baselineCount,
                "Interaction diagnostics changed native loot draws/application");
            check(messages.Count == 1 && messages[0].Contains("interaction=ACTPlayToyDinoBoth; loot=CTACTPlayToyDinoBoth; trigger=TUpIsDueBonusDerelict"),
                "Actual asset-trigger award lost its interaction/loot cause");

            ship.DMGStatus = Ship.Damage.Used; ship.fBreakInMultiplier = .4f;
            WreckSeverityPatch.Prefix(ship, true, out var capture);
            check(capture != null && !capture.DamageReport && WreckDamageLog.Current == null,
                "A bonus consumed by a non-derelict initialization was unobserved or became a wreck ray scope");
            actor.ZeroCondAmount(WreckBonusLog.Condition); ship.fBreakInMultiplier *= .75f;
            WreckSeverityPatch.Finalizer(ship, capture, null);
            check(messages.Count == 2 && messages[1].Contains("count=1->0") && messages[1].Contains("cause=Ship.BreakIn") &&
                messages[1].Contains("severity=0.4->0.3") && capture.BonusActor == null, "Consumption log lost count/severity or retained the actor");
            check(WreckBonusLog.Consumption(ship, 12345, 2, 2, .4f, .3f, null).Contains("count-cleared=false"),
                "An uncleared native bonus was reported as consumed");
            counts[WreckBonusLog.Condition] = 0; ship.fBreakInMultiplier = .4f;
            WreckSeverityPatch.Prefix(ship, false, out var zeroCount);
            check(zeroCount != null && zeroCount.BonusPresent && zeroCount.BonusBefore == 0,
                "Zero-count but present native bonus condition was hidden");
            ship.fBreakInMultiplier *= .75f; WreckSeverityPatch.Finalizer(ship, zeroCount, null);
            check(messages.Last().Contains("count=0->0") && messages.Last().Contains("condition-present-after=true"),
                "Diagnostic confused a zero count with removal of the native condition");
            check(messages.All(m => m.All(ch => ch <= 127)), "Bonus diagnostic introduced non-ASCII glyphs");

            var reader = typeof(PatchProcessor).GetMethods().Single(method => method.Name == "GetOriginalInstructions" &&
                method.GetParameters().Length == 2 && !method.GetParameters()[1].ParameterType.IsByRef);
            var original = ((IEnumerable<CodeInstruction>)reader.Invoke(null, new object[] { release, null })).ToList();
            var missing = original.Where(i => !i.Calls(AccessTools.Method(typeof(BeatManager), "BonusDerelict"))).ToList();
            var retained = WreckBonusReleasePatch.Transpiler(missing).ToList();
            check(retained.Count == missing.Count && retained.Select((i, n) => i.opcode == missing[n].opcode && Equals(i.operand, missing[n].operand)).All(x => x),
                "Unsupported release layout was partly rewritten");
            check(!original.Any(i => i.operand is MethodInfo m && m.DeclaringType == typeof(WreckBonusLog)),
                "Release observer changed the supplied original IL");
        } finally {
            production.UnpatchSelf(); scene.UnpatchSelf(); counts.Clear(); messages.Clear();
            CrewSim.coPlayer = oldPlayer; Ostrabus.OstrabusPlugin.Logger = oldLogger; chances.SetValue(null, oldChances); roll.SetValue(null, oldRoll);
            StarSystem.fEpoch = oldEpoch; WreckBonusLog.Release = oldRelease; WreckDamageLog.Current = oldCapture; WreckDamageLog.Enabled = false;
        }
    }
}
