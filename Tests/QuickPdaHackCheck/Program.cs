using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using HarmonyLib;
using Newtonsoft.Json;
using Ostrabus.Development;

static partial class Program {
    static int checks;
    static CondOwner selected;
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Check(bool ok, string why) { checks++; if (!ok) throw new Exception(why); }
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = ReferenceEquals(__0, __1); return false; }
    static bool Unequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = !ReferenceEquals(__0, __1); return false; }
    static bool Selected(ref CondOwner __result) { __result = selected; return false; }
    static bool Text(string strName, ref string __result) { __result = strName ?? ""; return false; }
    static bool Movement(ref bool __result) { __result = true; return false; }
    static bool Quiet() => false;
    static CondOwner CO(string id) {
        var co = Empty<CondOwner>();
        AccessTools.Field(typeof(CondOwner), "_strID").SetValue(co, id);
        co.mapConds = new Dictionary<string, Condition>();
        co.bAlive = true;
        DataHandler.mapCOs[id] = co;
        return co;
    }
    static void Flags(CondOwner co, params string[] names) {
        co.mapConds.Clear();
        foreach (string name in names) if (name != null) {
            var condition = Empty<Condition>();
            condition.fCount = 1;
            co.mapConds[name] = condition;
        }
    }
    static void Assets() {
        DataHandler.mapCOs = new Dictionary<string, CondOwner>();
        DataHandler.dictInteractions = JsonConvert.DeserializeObject<JsonInteraction[]>(File.ReadAllText(
            Path.Combine(InstallationPaths.Data, "interactions", "interactions_hacking.json"))).ToDictionary(x => x.strName);
        var definitions = JsonConvert.DeserializeObject<JsonCondTrigger[]>(File.ReadAllText(
            Path.Combine(InstallationPaths.Data, "condtrigs", "condtrigs.json")))
            .GroupBy(x => x.strName).ToDictionary(x => x.Key, x => x.Last());
        DataHandler.dictCTs = new Dictionary<string, CondTrigger> { ["Blank"] = new CondTrigger { strName = "Blank" } };
        void Add(string name) {
            if (DataHandler.dictCTs.ContainsKey(name)) return;
            var definition = definitions[name];
            var trigger = CondTrigger.CloneFromJSON(definition);
            DataHandler.dictCTs[name] = trigger;
            foreach (string child in (definition.aTriggers ?? Array.Empty<string>()).Concat(definition.aTriggersForbid ?? Array.Empty<string>())) Add(child);
            trigger.PostInit();
        }
        foreach (string name in new[] { "TIsHumanAwake", "TIsSkilledHacking", "TIsComputerPDAOnLocked", "TIsNotCarriedNotInstalled",
            "TIsJettisonableBody", "TIsJettisonableSignalBeacon" }) Add(name);
        DataHandler.dictLoot = new[] { "loot.json", "loot_self_reference.json" }
            .SelectMany(file => JsonConvert.DeserializeObject<JsonLoot[]>(File.ReadAllText(Path.Combine(InstallationPaths.Data, "loot", file))))
            .Where(x => x.strName == "CONDENCHackPDASkill" || x.strName == "ItmWristPDA01")
            .ToDictionary(x => x.strName, Loot.CloneFromJSON);
        DataHandler.dictConds = new Dictionary<string, JsonCond>();
        DataHandler.dictSocialStats = new Dictionary<string, SocialStats>();
    }
    static void Main() {
        var fixture = new Harmony("ostrabus.test.quick-pda.fixture");
        void Prefix(Type type, string method, string handler) => fixture.Patch(
            AccessTools.Method(type, method), prefix:new HarmonyMethod(typeof(Program), handler));
        Prefix(typeof(UnityEngine.Object), "op_Equality", nameof(Equal));
        Prefix(typeof(UnityEngine.Object), "op_Inequality", nameof(Unequal));
        Prefix(typeof(CrewSim), nameof(CrewSim.GetSelectedCrew), nameof(Selected));
        Prefix(typeof(DataHandler), nameof(DataHandler.GetString), nameof(Text));
        Prefix(typeof(CondOwner), nameof(CondOwner.ValidateParent), nameof(Quiet));
        Prefix(typeof(Interaction), "ValidateMovementSync", nameof(Movement));
        try { Assets(); NativeChecks(fixture); }
        finally { fixture.UnpatchSelf(); }
    }
}
