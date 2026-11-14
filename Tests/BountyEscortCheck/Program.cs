using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ostrabus.Development;
using Ostrabus.MiscBugFixes;
using UnityEngine;

static class Program {
    static int checks;
    static CondOwner actor, door;
    static Ship vessel;
    static JsonCompany company;
    static bool suffocating, pressureDifference;
    static readonly Dictionary<string, HashSet<string>> Conditions = new Dictionary<string, HashSet<string>>();
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Check(bool value, string why) { checks++; if (!value) throw new Exception(why); }
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = ReferenceEquals(__0, __1); return false; }
    static bool Unequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = !ReferenceEquals(__0, __1); return false; }
    static bool Has(CondOwner __instance, string strName, ref bool __result) { __result = Conditions[__instance.strID].Contains(strName); return false; }
    static bool Company(ref JsonCompany __result) { __result = company; return false; }
    static bool Vessel(ref Ship __result) { __result = vessel; return false; }
    static bool Emergency(ref bool __result) { __result = suffocating; return false; }
    static bool Position(ref Vector3 __result) { __result = Vector3.zero; return false; }
    static bool Pressure(ref bool __result) { __result = pressureDifference; return false; }
    static bool NoEvaGravity(ref bool __result) { __result = false; return false; }
    static CondOwner CO(string id) {
        var co = Empty<CondOwner>(); co.bAlive = true;
        AccessTools.Field(typeof(CondOwner), "_strID").SetValue(co, id);
        Conditions[id] = new HashSet<string>();
        return co;
    }
    static JObject Definition(string folder, string file, string id) => JArray.Parse(File.ReadAllText(Path.Combine(InstallationPaths.Data, folder, file)))
        .Cast<JObject>().Single(row => (string)row["strName"] == id);
    static void Assets() {
        foreach (string id in new[] { "ACTBountyArrestDemandAllowAI", "ACTBountyArrestDemandAllowAIScared" })
            Check((string)Definition("interactions", "interactions_combat.json", id)["LootCondsUs"] == "CONDBountyArrestAllowAIUs", "Arrest response no longer uses its authored custody loot");
        var arrest = Definition("loot", "loot.json", "CONDBountyArrestAllowAIUs");
        Check(arrest["aCOs"].Values<string>().Contains("IsTempDraftedLong=1.0x1"), "Arrest no longer grants long temporary drafting");
        var condition = Definition("conditions", "conditions.json", "IsTempDraftedLong");
        Check(condition["aPer"].Values<string>().Contains("CONDTempDraftedPer") && (double)condition["fDuration"] > 0,
            "Temporary custody duration/control policy changed");
        var per = Definition("loot", "loot.json", "CONDTempDraftedPer");
        foreach (string id in new[] { "IsDrafted=1.0x1", "IsPlayerCrew=1.0x1" })
            Check(per["aCOs"].Values<string>().Contains(id), "Custody no longer grants authored temporary crew control");
    }
    static void Main() {
        var boundaries = new Harmony("offline.bounty-escort.boundaries");
        var repair = new Harmony("offline.bounty-escort.repair");
        void Patch(System.Reflection.MethodBase target, string method) => boundaries.Patch(target, prefix: new HarmonyMethod(typeof(Program), method));
        Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Equality"), nameof(Equal));
        Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Inequality"), nameof(Unequal));
        foreach (var method in AccessTools.GetDeclaredMethods(typeof(CondOwner)).Where(m => m.Name == "HasCond" && m.GetParameters()[0].ParameterType == typeof(string))) Patch(method, nameof(Has));
        Patch(AccessTools.PropertyGetter(typeof(CondOwner), "Company"), nameof(Company));
        Patch(AccessTools.PropertyGetter(typeof(CondOwner), "ship"), nameof(Vessel));
        Patch(AccessTools.Method(typeof(CondTrigger), "Triggered"), nameof(Emergency));
        Patch(AccessTools.PropertyGetter(typeof(Transform), "position"), nameof(Position));
        Patch(AccessTools.Method(typeof(Pathfinder), "CheckDoorPressure"), nameof(Pressure));
        Patch(AccessTools.Method(typeof(Tile), "IsEvaTileWithGravitation"), nameof(NoEvaGravity));
        try {
            Assets();
            AccessTools.Field(typeof(CondOwner), "_ctSuffocatingManWalk").SetValue(null,
                new CondTrigger { strName = "TIsSuffocatingManWalkEmerg" });
            vessel = Empty<Ship>(); CrewSim.coPlayer = null;
            actor = CO("PRISONER"); door = CO("AIRLOCK");
            company = Empty<JsonCompany>();
            company.mapRoster = new Dictionary<string, JsonCompanyRules> { [actor.strID] = new JsonCompanyRules() };
            var rules = company.mapRoster[actor.strID];
            var flags = Conditions[actor.strID];
            flags.UnionWith(new[] { "IsTempDraftedLong", "IsDrafted", "IsPlayerCrew", "IsAIManual" });
            Check(actor.HasShoreLeave(), "Native custody did not override disembark permission");
            Check(!actor.HasAirlockPermission(true), "Baseline did not reproduce the roster airlock veto");
            Conditions[door.strID].UnionWith(new[] { "IsWall", "IsPortal" });
            var tile = Empty<Tile>(); tile.tf = Empty<Transform>(); tile.coProps = door;
            pressureDifference = true;
            var blocked = new PathResult(actor.HasAirlockPermission(true));
            Check(!tile.IsWalkable(actor, blocked) && blocked.bAirlockBlocked, "Native tile did not expose the airlock-blocked route");

            repair.CreateClassProcessor(typeof(BountyEscortAirlockPatch)).Patch();
            Check(actor.HasAirlockPermission(true), "Manual custody order remained roster-blocked");
            var allowed = new PathResult(actor.HasAirlockPermission(true));
            Check(tile.IsWalkable(actor, allowed) && !allowed.bAirlockBlocked, "Native tile remained blocked after custody permission repair");
            Check(!rules.bAirlockPermission && !rules.bShoreLeave, "Custody permission changed the faction roster");
            Check(!actor.HasAirlockPermission(false), "Manual escort exception leaked into autonomous airlock use");
            Conditions[door.strID].Add("IsPortalStuck");
            Check(!tile.IsWalkable(actor, new PathResult(true)), "Custody bypassed a stuck door");
            Conditions[door.strID].Remove("IsPortalStuck");
            Conditions[door.strID].Remove("IsPortal"); tile.coProps = door;
            Check(!tile.IsWalkable(actor, new PathResult(true)), "Custody bypassed a solid wall");
            Conditions[door.strID].Add("IsPortal"); tile.coProps = door;

            foreach (bool manual in new[] { false, true }) foreach (bool longDraft in new[] { false, true })
                foreach (bool shortDraft in new[] { false, true }) foreach (bool aiManual in new[] { false, true })
                foreach (bool airlock in new[] { false, true }) foreach (bool airtight in new[] { false, true })
                foreach (bool emergency in new[] { false, true }) foreach (bool gasping in new[] { false, true }) {
                flags.Clear();
                if (longDraft) flags.Add("IsTempDraftedLong");
                if (shortDraft) flags.Add("IsTempDrafted");
                if (aiManual) flags.Add("IsAIManual");
                if (airtight) flags.Add("IsAirtight");
                if (emergency) flags.Add("IsEmergencyOverride");
                rules.bAirlockPermission = airlock; suffocating = gasping;
                repair.UnpatchSelf();
                bool native = actor.HasAirlockPermission(manual);
                repair.CreateClassProcessor(typeof(BountyEscortAirlockPatch)).Patch();
                bool actual = actor.HasAirlockPermission(manual);
                Check(actual == (native || (manual && longDraft)), "Permission matrix changed a non-custody/native rule");
                Check(rules.bAirlockPermission == airlock, "Permission matrix mutated the roster");
            }
            flags.Clear(); flags.Add("IsTempDraftedLong"); suffocating = false; rules.bAirlockPermission = false;
            Check(actor.HasAirlockPermission(true), "Active custody lost permission");
            flags.Remove("IsTempDraftedLong");
            Check(!actor.HasAirlockPermission(true), "Expired/released custody retained airlock permission");
            company = null; flags.Add("IsTempDraftedLong");
            Check(actor.HasAirlockPermission(true), "Missing roster blocked manual custody");
            Check(!actor.HasAirlockPermission(false), "Missing roster granted autonomous custody movement");
            Console.WriteLine("Bounty escort: " + checks + " native permission, tile, asset and expiry checks passed.");
        } finally {
            repair.UnpatchSelf(); boundaries.UnpatchSelf(); Conditions.Clear(); CrewSim.coPlayer = null;
            AccessTools.Field(typeof(CondOwner), "_ctSuffocatingManWalk").SetValue(null, null);
        }
    }
}
