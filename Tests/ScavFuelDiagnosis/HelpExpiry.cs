using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Condowner.Utils;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ostranauts.Core;
using Ostranauts.Ships.Commands;
using Ostranauts.Ships.Comms;

static partial class Program {
    static int helpRemovals;
    static CondOwner cooldownCaptain;
    static CondTrigger cooldownTrigger;
    static bool CooldownCaptain(ref CondOwner captain, ref bool __result) { captain = cooldownCaptain; __result = true; return false; }
    static bool CooldownTrigger(string __0, ref CondTrigger __result) {
        __result = __0 == "TIsHasRequestedHelp" ? cooldownTrigger : new CondTrigger();
        return false;
    }
    static bool ExpiryRemoval(CondOwner __instance, string strName) {
        helpRemovals++;
        __instance.mapConds.Remove(strName);
        var timers = (List<JsonTicker>)AccessTools.Field(typeof(CondOwner), "aTickers").GetValue(__instance);
        timers.RemoveAll(t => t.strCondUpdate == strName);
        return false; // UI/scheduler side effects are outside this condition-expiry fixture.
    }
    static void CheckHelpExpiry(Harmony boundary) {
        var expire = AccessTools.Method(typeof(Ostrabus.OstrabusPlugin).Assembly.GetType("Ostrabus.MiscBugFixes.AiHoldClock", true), "ExpireHelp");
        boundary.Patch(AccessTools.Method(typeof(CondOwner), nameof(CondOwner.ZeroCondAmount)), prefix: new HarmonyMethod(typeof(Program), nameof(ExpiryRemoval)));
        string path = Path.Combine(Ostrabus.Development.InstallationPaths.Game, "Ostranauts_Data/StreamingAssets/data/conditions/conditions.json");
        var definition = JArray.Parse(File.ReadAllText(path)).OfType<JObject>().Single(j => (string)j["strName"] == "ShipRequestedHelp").ToObject<JsonCond>();
        foreach (string scenario in new[] { "before", "at-end", "after", "saved", "sff", "full", "other-condition", "repeating", "missing-ticker" }) {
            double start = 65633393023;
            var co = Empty<CondOwner>(); co.ship = HelpShip(); co.Clock = new Clock(); co.Clock.Epoch = start;
            var condition = new Condition(definition) { fCount = 1 };
            var other = new Condition(new JsonCond { strName = "UnrelatedHealth", fDuration = .01f }) { fCount = 1 };
            co.mapConds = new Dictionary<string, Condition> { [condition.strName] = condition, [other.strName] = other };
            var timer = new JsonTicker { strName = condition.strName, strCondUpdate = condition.strName, fEpochStart = start, fPeriod = condition.fDuration };
            if (scenario == "saved") timer = JsonConvert.DeserializeObject<JsonTicker>(JsonConvert.SerializeObject(timer));
            if (scenario == "full") Set(co.ship, "nLoadState", Ship.Loaded.Full);
            if (scenario == "other-condition") timer.strCondUpdate = "UnrelatedHealth";
            if (scenario == "repeating") timer.bRepeat = true;
            var timers = new List<JsonTicker>(); if (scenario != "missing-ticker") timers.Add(timer);
            Set(co, "aTickers", timers);
            StarSystem.fEpoch = timer.GetEpochEndS() + (scenario == "before" ? -1 : scenario == "at-end" ? 0 : scenario == "sff" ? 21600 : 1);
            int removed = helpRemovals;
            double clock = co.Clock.Epoch;
            expire.Invoke(null, new object[] { co });
            bool due = scenario == "at-end" || scenario == "after" || scenario == "saved" || scenario == "sff";
            Check(co.mapConds.ContainsKey("ShipRequestedHelp") != due && helpRemovals == removed + (due ? 1 : 0), "Authored help expiry failed: " + scenario);
            Check(other.GetAge() == 0 && co.Clock.Epoch == clock && co.mapConds.ContainsKey(other.strName), "Help expiry advanced unrelated NPC state: " + scenario);
            expire.Invoke(null, new object[] { co });
            Check(helpRemovals == removed + (due ? 1 : 0), "Help expiry repeated effects: " + scenario);
        }
        // Integrate real condition evaluation with the actual selection prefix:
        // no boolean help fixture supplies the cooldown result in this case.
        boundary.Unpatch(AccessTools.Method(typeof(CondTrigger), nameof(CondTrigger.Triggered)), HarmonyPatchType.Prefix, boundary.Id);
        boundary.Unpatch(AccessTools.Method(typeof(Comms), nameof(Comms.GetCaptain)), HarmonyPatchType.Prefix, boundary.Id);
        boundary.Patch(AccessTools.Method(typeof(Comms), nameof(Comms.GetCaptain)), prefix: new HarmonyMethod(typeof(Program), nameof(CooldownCaptain)));
        var getTrigger = AccessTools.Method(typeof(DataHandler), nameof(DataHandler.GetCondTrigger), new[] { typeof(string) });
        boundary.Unpatch(getTrigger, HarmonyPatchType.Prefix, boundary.Id);
        boundary.Patch(getTrigger, prefix: new HarmonyMethod(typeof(Program), nameof(CooldownTrigger)));
        boundary.Patch(AccessTools.Method(typeof(CondOwner), nameof(CondOwner.ValidateParent)), prefix: new HarmonyMethod(typeof(Program), nameof(Quiet)));
        cooldownTrigger = new CondTrigger("TIsHasRequestedHelp", new[] { "ShipRequestedHelp" }, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()) { bAND = false };
        DataHandler.dictConds = new Dictionary<string, JsonCond> { [definition.strName] = definition };
        StarSystem.fEpoch = 65633393023;
        cooldownCaptain = Empty<CondOwner>(); cooldownCaptain.ship = HelpShip(); cooldownCaptain.Clock = new Clock();
        cooldownCaptain.ship.fShallowRCSRemass = 1873;
        cooldownCaptain.mapConds = new Dictionary<string, Condition> { [definition.strName] = new Condition(definition) { fCount = 1 } };
        var active = new JsonTicker { strName = definition.strName, strCondUpdate = definition.strName, fPeriod = definition.fDuration, fEpochStart = StarSystem.fEpoch };
        Set(cooldownCaptain, "aTickers", new List<JsonTicker> { active });
        var pilot = new HelpPilot(cooldownCaptain.ship);
        var select = new TargetNewIfNone(pilot);
        int previousSends = sends;
        Check(select.RunCommand(pilot.Blackboard).Status == CommandCode.Running && sends == previousSends, "Native active-help evaluator did not suppress a request.");
        StarSystem.fEpoch = active.GetEpochEndS() + 1;
        Check(select.RunCommand(pilot.Blackboard).Status == CommandCode.Running && sends == previousSends + 1, "Native expired-help evaluation did not permit a throttled retry.");
        Check(!cooldownCaptain.mapConds.ContainsKey(definition.strName), "Selection did not process the actual expired help condition.");
    }
}
