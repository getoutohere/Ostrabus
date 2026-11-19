using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ostrabus.Development;

static partial class Program {
    static readonly Dictionary<CondOwner, GUIChargenStack> PopulationStacks = new Dictionary<CondOwner, GUIChargenStack>();
    static CondTrigger populationTraffic;
    static bool PopulationComponent(UnityEngine.Component __instance, ref GUIChargenStack __result) {
        PopulationStacks.TryGetValue((CondOwner)__instance, out __result); return false;
    }
    static bool PopulationTrigger(string __0, ref CondTrigger __result) {
        if (__0 != "TIsTrafficAIScav") throw new Exception("Unexpected recruitment trigger: " + __0);
        __result = populationTraffic; return false;
    }
    static CondOwner PopulationActor(JObject row) {
        var co = Empty<CondOwner>(); Field(co, "_strID", (string)row["strID"]); co.strName = (string)row["strID"]; co.bAlive = (bool)row["bAlive"];
        co.mapConds = new Dictionary<string, Condition> { ["IsHuman"] = new Condition(null) { fCount = 1 } };
        foreach (string text in row["aConds"].Values<string>()) {
            if (!text.Contains("=")) continue;
            string name = text.Split('=')[0];
            co.mapConds[name] = new Condition(null) { strName = name, fCount = 1 };
        }
        var cgs = Empty<GUIChargenStack>();
        Field(cgs, "jsh", new JsonHomeworld { strName = (string)row["cgs"]["strJSH"], strATCCode = (string)row["cgs"]["strJSH"] });
        cgs.aCareers = new List<CareerChosen>();
        foreach (var saved in (JArray)row["cgs"]["aCareers"]) {
            var career = Empty<CareerChosen>(); career.strJC = (string)saved["strJC"];
            Field(career, "objJC", new JsonCareer { strName = career.strJC }); cgs.aCareers.Add(career);
        }
        PopulationStacks[co] = cgs; return co;
    }
    static int PopulationRun(string path) {
        var h = new Harmony("offline.population.recruitment");
        try {
            h.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Equality"), prefix: new HarmonyMethod(typeof(Program), nameof(Equal)));
            h.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Inequality"), prefix: new HarmonyMethod(typeof(Program), nameof(Unequal)));
            var component = typeof(UnityEngine.Component).GetMethods().Single(m => m.Name == "GetComponent" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0)
                .MakeGenericMethod(typeof(GUIChargenStack));
            h.Patch(component, prefix: new HarmonyMethod(typeof(Program), nameof(PopulationComponent)));
            h.Patch(AccessTools.Method(typeof(DataHandler), "GetCondTrigger", new[] { typeof(string), typeof(bool) }), prefix: new HarmonyMethod(typeof(Program), nameof(PopulationTrigger)));
            var definitions = JArray.Parse(File.ReadAllText(Path.Combine(InstallationPaths.Data, "personspecs", "personspecs.json")));
            var profile = definitions.Single(d => (string)d["strName"] == "CivilianPilotEJDR").ToObject<JsonPersonSpec>();
            var triggerRow = JArray.Parse(File.ReadAllText(Path.Combine(InstallationPaths.Data, "condtrigs", "condtrigs.json"))).Single(d => (string)d["strName"] == profile.strCT);
            populationTraffic = new CondTrigger(profile.strCT, triggerRow["aReqs"].Values<string>().ToArray(), triggerRow["aForbids"].Values<string>().ToArray(),
                triggerRow["aTriggers"].Values<string>().ToArray(), null) { bAND = (bool)triggerRow["bAND"], logReason = false };
            var root = JObject.Parse(File.ReadAllText(path)); var results = new JArray();
            foreach (JObject row in (JArray)root["people"]) {
                if (!(row["cgs"] is JObject)) continue;
                var co = PopulationActor(row);
                results.Add(new JObject { ["id"] = co.strID, ["savedShip"] = row["strRegIDLast"], ["homeworld"] = row["cgs"]["strJSH"],
                    ["career"] = row["cgs"]["aCareers"].Last?["strJC"], ["nativeProfileMatch"] = profile.Matches(co),
                    ["nativeTrafficTrigger"] = populationTraffic.Triggered(co), ["alive"] = co.bAlive });
            }
            // Positive and negative controls use the same native Matches body.
            var eligible = PopulationStacks.Keys.First(co => co.strID == "Jay Farmer");
            Check(!profile.Matches(eligible) && populationTraffic.Triggered(eligible), "Saved non-Earth shipbreaker control changed");
            Field(PopulationStacks[eligible], "jsh", new JsonHomeworld { strName = "EJDR", strATCCode = "EJDR" });
            Check(profile.Matches(eligible), "Eligible Earth-born shipbreaker was rejected by the fixture");
            eligible.mapConds["IsNotTrafficAI"] = new Condition(null) { fCount = 1 };
            Check(!profile.Matches(eligible), "Native authored non-traffic protection was bypassed");
            var pools = AccessTools.Field(typeof(AIShipManager), "dictPilotSrcRegIDs");
            var previous = pools.GetValue(null); var shared = new List<string> { "EJDR", "EJDR_GEO" };
            try {
                pools.SetValue(null, new Dictionary<string, List<string>> { ["EJDR"] = shared });
                Check(ReferenceEquals(AIShipManager.GetAllATCRecycleStations("EJDR"), shared), "Native recruitment source list no longer aliases the regional registry");
            } finally { pools.SetValue(null, previous); }
            Console.WriteLine(new JObject { ["nativeMvid"] = typeof(CondOwner).Assembly.ManifestModule.ModuleVersionId.ToString(),
                ["profile"] = profile.strName, ["requiredHomeworld"] = profile.strHomeworldFind, ["requiredCareer"] = profile.strCareerNow,
                ["actors"] = results, ["controlsPassed"] = checks, ["scope"] = "Native Matches/traffic-trigger execution with saved chargen identity and active-condition presence; no scene, pilot creation or lifecycle policy patch." }.ToString());
            return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { h.UnpatchSelf(); PopulationStacks.Clear(); }
    }
}
