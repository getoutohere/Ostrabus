using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Reflection.Emit;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ostrabus.MiscBugFixes;
using UnityEngine;

static class Program {
    static int checks, probes, scans;
    static CondOwner actor, seat, item;
    static Ship ship;
    static Pathfinder path;
    static JsonCompany company;
    static bool usable, throwProbe, owns = true;
    static readonly Dictionary<string, HashSet<string>> labels = new Dictionary<string, HashSet<string>>();
    static readonly List<CondOwner> candidates = new List<CondOwner>();
    static List<CondOwner> inventoryResult, worldResult;
    static Interaction fallback;
    static bool nativeSelection;
    static string queuedName, unavailableName;
    static readonly List<string> validated = new List<string>();
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Set(object obj, string name, object value) => AccessTools.Field(obj.GetType(), name).SetValue(obj, value);
    static void Check(bool ok, string reason) { checks++; if (!ok) throw new Exception(reason); }
    static CondOwner CO(string id, params string[] flags) {
        var co = Empty<CondOwner>(); Set(co, "_strID", id); co.tf = Empty<Transform>(); co.bAlive = true;
        labels[id] = new HashSet<string>(flags); Set(co, "_objShip", ship);
        return co;
    }
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = ReferenceEquals(__0, __1); return false; }
    static bool Unequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = !ReferenceEquals(__0, __1); return false; }
    static bool Has(CondOwner __instance, string strName, ref bool __result) { __result = labels.TryGetValue(__instance.strID, out var set) && set.Contains(strName); return false; }
    static bool Company(ref JsonCompany __result) { __result = company; return false; }
    static bool Owns(ref bool __result) { __result = owns; return false; }
    static bool PF(ref Pathfinder __result) { __result = path; return false; }
    static bool Pos(ref Vector2 __result) { __result = Vector2.zero; return false; }
    static bool New(CondOwner __instance, ref bool __result) { __result = labels[__instance.strID].Contains("pristine"); return false; }
    static bool Damage(ref double __result) { __result = 0.5; return false; }
    static bool Quiet() => false;
    static bool Inventory(ref List<CondOwner> __result) { __result = new List<CondOwner>(candidates); return false; }
    static IEnumerator World(List<CondOwner> results) { results.Clear(); yield return null; results.AddRange(candidates); }
    static bool WorldSearch(List<CondOwner> results, ref IEnumerator __result) { __result = World(results); return false; }
    static IEnumerator Choose(CondOwner co) {
        var filter = new CondTrigger { strName = "TIsBedFree" };
        inventoryResult = co.GetCOsSafe(true, filter);
        worldResult = new List<CondOwner>();
        var steps = ship.GetCosDeferred(filter, false, true, false, worldResult);
        try { while (steps.MoveNext()) yield return steps.Current; }
        finally { (steps as IDisposable)?.Dispose(); }
    }
    static bool Choice(CondOwner __instance, ref IEnumerator __result) { __result = Choose(__instance); return false; }
    static bool ShipItems(ref List<CondOwner> __result) { scans++; __result = new List<CondOwner>(candidates); return false; }
    static bool TriggerDefinition(string strName, ref CondTrigger __result) { __result = new CondTrigger { strName = strName }; return false; }
    static Interaction IA(string name, bool manual = false, CondOwner us = null) {
        var ia = new Interaction { strName = name, bManual = manual };
        Set(ia, "objUsTemp", us ?? actor); Set(ia, "objThemTemp", seat);
        return ia;
    }
    static bool InteractionDefinition(string strName, ref Interaction __result) { __result = IA(strName); return false; }
    static bool Validate(Interaction __instance, bool bCheckPath, bool bFetchItems, ref bool __result) {
        Check(nativeSelection || !bFetchItems, "Read-only seat probe requested material fetching");
        if (__instance.strName == "ACTChairSitEnd") { __result = true; return false; }
        Check(bCheckPath, "Restore probe skipped native path validation");
        probes++;
        validated.Add(__instance.strName);
        path.tilDest = Empty<Tile>(); path.fRangeGoal = 123; path.coDest = item;
        if (throwProbe) throw new InvalidOperationException("fixture validation failure");
        __result = usable && __instance.strName != unavailableName; return false;
    }
    static bool Queue(Interaction objInteraction, ref bool __result) { queuedName = objInteraction.strName; __result = true; return false; }
    static IEnumerable<CodeInstruction> ReplyCode() {
        yield return new CodeInstruction(OpCodes.Ldsfld, AccessTools.Field(typeof(Program), nameof(fallback)));
        yield return new CodeInstruction(OpCodes.Ret);
    }
    static void Patch(Harmony h, Type type, string name, string hook, Type[] args = null, int priority = Priority.Normal) =>
        h.Patch(AccessTools.Method(type, name, args), prefix: new HarmonyMethod(typeof(Program), hook) { priority = priority });
    static Interaction Renew(Interaction current) {
        fallback = IA(current.strName);
        return current.GetReply();
    }
    static void Next() { StarSystem.fEpoch += 31; probes = scans = 0; validated.Clear(); }
    static void SleepChecks() {
        var pack = CO("pack", "IsContainer"); var eva = CO("eva", "IsEVASuit");
        var bunk = CO("bunk", "IsBed01"); var med = CO("medical", "IsBedMedical"); var mod = CO("modded-bed");
        candidates.AddRange(new[] { pack, eva, bunk, med, mod });
        var method = AccessTools.Method(typeof(CondOwner), "GetMove2");
        IEnumerator Start() => (IEnumerator)method.Invoke(actor, null);
        var choice = Start();
        Check(choice.MoveNext(), "Deferred search fixture did not yield");
        Check(CrewSleepPatch.Choosing == null, "Crew scope leaked across coroutine yield");
        Check(inventoryResult.SequenceEqual(new[] { bunk, med, mod }), "Inventory bed filtering incorrect");
        while (choice.MoveNext()) Check(CrewSleepPatch.Choosing == null, "Scope leaked on resume");
        Check(worldResult.SequenceEqual(new[] { bunk, med, mod }), "Deferred world filtering incorrect");
        Check(CrewSleepPatch.Choosing == null, "Scope retained a crew reference after completion");
        foreach (string flag in new[] { "IsDrafted", "IsTempDrafted", "IsTempDraftedLong" }) {
            labels[actor.strID].Add(flag); choice = Start(); while (choice.MoveNext()) { }
            Check(inventoryResult.SequenceEqual(new[] { bunk, med, mod }) && worldResult.SequenceEqual(new[] { bunk, med, mod }), "Drafted autonomous sleep search bypassed container filter: " + flag);
            labels[actor.strID].Remove(flag);
        }
        choice = Start(); choice.MoveNext(); labels[actor.strID].Add("IsDrafted"); while (choice.MoveNext()) { }
        Check(worldResult.SequenceEqual(new[] { bunk, med, mod }), "Drafting during a yielded autonomous search disabled the sleep filter");
        labels[actor.strID].Remove("IsDrafted");
        Check(actor.GetCOsSafe(true, new CondTrigger { strName = "TIsBedFree" }).Count == 5, "Sleep filter changed direct/manual bed searches outside autonomous selection");
        labels[actor.strID].Add("IsAIManual"); choice = Start(); while (choice.MoveNext()) { }
        Check(inventoryResult.Count == 5 && worldResult.Count == 5, "Manual crew's sleep search changed");
        labels[actor.strID].Remove("IsAIManual");
        CrewSim.coPlayer = null; labels[actor.strID].Remove("IsPlayerCrew"); choice = Start(); while (choice.MoveNext()) { }
        Check(worldResult.Count == 5, "NPC sleep search changed");
        labels[actor.strID].Add("IsPlayerCrew"); CrewSim.coPlayer = actor;
        var trigger = new CondTrigger { strName = "TIsChairFree" };
        CrewSleepPatch.Choosing = actor;
        Check(actor.GetCOsSafe(true, trigger).Count == 5, "Non-sleep search changed");
        CrewSleepPatch.Choosing = null;
        choice = Start(); choice.MoveNext(); ((IDisposable)choice).Dispose();
        Check(CrewSleepPatch.Choosing == null && !choice.MoveNext(), "Cancelled choice retained scope or resumed");
        choice = Start(); choice.MoveNext(); labels[actor.strID].Add("IsAIManual"); while (choice.MoveNext()) { }
        Check(worldResult.Count == 5, "Deferred search ignored a switch to manual control"); labels[actor.strID].Remove("IsAIManual");
        foreach (string condition in new[] { "Unconscious", "IsAIManual" }) {
            labels[actor.strID].Add(condition); Check(!CrewRestPolicy.Autonomous(actor), "Ineligible actor accepted: " + condition); labels[actor.strID].Remove(condition);
        }
        candidates.Clear();
    }
    static void SeatChecks() {
        item = CO("repair-item"); Set(item, "bCanUndamage", true); item.aInteractions = new List<string> { "ACTItemUndamage" };
        candidates.Add(item);
        var originalDestination = Empty<Tile>(); path.tilDest = originalDestination; path.coDest = seat; path.fRangeGoal = 0;
        usable = true; Next();
        var result = Renew(IA("ACTChairSitAllow"));
        Check(result.strName == "ACTChairSitEnd", "Usable restore work did not end idle sitting: scans=" + scans + ", probes=" + probes + ", autonomous=" + CrewRestPolicy.Autonomous(actor) + ", shift=" + actor.jsShiftLast.nID);
        Check(path.tilDest == originalDestination && path.coDest == seat && path.fRangeGoal == 0, "Probe changed active path state");
        Check(probes == 1 && scans == 1, "Restore probe did unexpected work");
        int before = scans; result = Renew(IA("ACTChairSitAllow"));
        Check(ReferenceEquals(result, fallback) && scans == before, "Seat loop ignored probe throttle");
        usable = false; Next(); result = Renew(IA("ACTChairSitAllow"));
        Check(ReferenceEquals(result, fallback), "Unreachable or tool-ineligible work forced standing");
        Check(path.tilDest == originalDestination && path.coDest == seat, "Failed probe changed path state");
        foreach (string name in new[] { "ACTChairSitSecureAllow", "GUINavStationSit", "GUINAVSitEnd", "SeekSleepSimpleCont" }) {
            usable = true; Next(); result = Renew(IA(name));
            Check(ReferenceEquals(result, fallback) && scans == 0, "Protected action interrupted: " + name);
        }
        foreach (string flag in new[] { "IsAIManual", "Unconscious", "IsInCombat", "InSocialCombat", "IsEmergencyOverride", "IsTempDrafted", "IsTempDraftedLong", "IsDrafted", "IsSubdued" }) {
            labels[actor.strID].Add(flag); Next(); result = Renew(IA("ACTChairSitAllow"));
            Check(ReferenceEquals(result, fallback) && scans == 0, "Actor protection ignored: " + flag); labels[actor.strID].Remove(flag);
        }
        Next(); result = Renew(IA("ACTChairSitAllow", true));
        Check(ReferenceEquals(result, fallback) && result.bManual && scans == 0, "Manual seating was interrupted or lost its flag");
        foreach (string name in new[] { "ACTChairSit", "ACTChairSitShim", "ACTChairSitSecureAllow", "GUINavStationSit", "GUINAVSitCancel" }) {
            Next(); result = Renew(IA(name, true)); Check(result.bManual, "Manual intent lost in seating chain: " + name);
            Check(new JsonInteractionSave(result).bManual, "Manual seating marker was not captured by native save data");
        }
        var manual = IA("ACTChairSitAllow", true); fallback = IA("SeekSleepSimple"); manual.GetReply();
        Check(!fallback.bManual, "Manual flag leaked to a different action family");
        company.mapRoster[actor.strID].bRestorePermission = false; Next(); result = Renew(IA("ACTChairSitAllow"));
        Check(scans == 0 && ReferenceEquals(result, fallback), "Restore permission ignored"); company.mapRoster[actor.strID].bRestorePermission = true;
        actor.jsShiftLast.nID = 1; Next(); Renew(IA("ACTChairSitAllow")); Check(scans == 0, "Sleep shift interrupted for work"); actor.jsShiftLast.nID = 2;
        owns = false; Next(); Renew(IA("ACTChairSitAllow")); Check(scans == 0, "Unowned ship interrupted idle seating"); owns = true;
        labels[item.strID].Add("pristine"); Next(); Renew(IA("ACTChairSitAllow")); Check(probes == 0, "Like-new item triggered work"); labels[item.strID].Remove("pristine");
        var tried = (Dictionary<string, double>)AccessTools.Field(typeof(CondOwner), "dictRecentlyTried").GetValue(actor);
        tried[item.strID + "ACTItemUndamage"] = StarSystem.fEpoch; Next(); Renew(IA("ACTChairSitAllow")); Check(probes == 0, "Native recently-tried exclusion ignored"); tried.Clear();
        throwProbe = true; Next(); result = Renew(IA("ACTChairSitAllow"));
        throwProbe = false;
        Check(ReferenceEquals(result, fallback) && path.tilDest == originalDestination && path.coDest == seat && path.fRangeGoal == 0, "Exceptional probe leaked path state or failed to retain native behavior");
        for (int i = 0; i < 3; i++) {
            var extra = CO("extra" + i); Set(extra, "bCanUndamage", true); extra.aInteractions = new List<string> { "ACTItemUndamage" }; candidates.Add(extra);
        }
        usable = false; Next(); Renew(IA("ACTChairSitAllow"));
        Check(probes == 2, "Probe exceeded the native two-candidate limit");
    }
    static void AssetChecks() {
        string root = Ostrabus.Development.InstallationPaths.Data;
        var owners = JArray.Parse(File.ReadAllText(root + "condowners/condowners.json"));
        JObject Named(JArray rows, string name) => (JObject)rows.Single(r => (string)r["strName"] == name);
        foreach (string id in new[] { "ItmBackpack01", "OutfitEVA01", "ItmBed01", "ItmBed02", "ItmBedMedical01" }) {
            var values = Named(owners, id)["aStartingConds"].Values<string>().Select(s => s.Split('=')[0]).ToArray();
            var target = CO(id, values);
            Check(CrewRestPolicy.Bed(target) == (id.StartsWith("ItmBed")), "Native asset misclassified: " + id);
        }
        var interactions = JArray.Parse(File.ReadAllText(root + "interactions/interactions.json"));
        var end = Named(interactions, "ACTChairSitEnd");
        Check((string)end["LootCondsUs"] == "CONDSitEnd" && (string)end["LootCondsThem"] == "CONDSitEndThem", "Native stand-up cleanup changed");
        var loot = JArray.Parse(File.ReadAllText(root + "loot/loot.json"));
        Check(Named(loot, "CONDSitEnd")["aCOs"].Values<string>().Contains("-Sitting=1.0x1"), "Stand-up does not clear sitting");
        Check(Named(loot, "CONDSitEndThem")["aCOs"].Values<string>().Contains("-IsOccupied=1.0x1"), "Stand-up does not release seat occupancy");
        var triggers = JArray.Parse(File.ReadAllText(root + "condtrigs/condtrigs.json"));
        Check(Named(triggers, "TNoDiscomfortsSitting")["aForbids"].Values<string>().Contains("DcOxygen03"), "Native oxygen-related seat exit changed");
        Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(PledgeSurviveO2), "IsEmergency")) == null, "Oxygen emergency handling patched");
    }
    static void WorkCategoryChecks() {
        candidates.Clear();
        var repair = CO("repair-only", "pristine"); Set(repair, "bCanRepair", true);
        repair.aInteractions = new List<string> { "ACTItemRepair" }; candidates.Add(repair);
        usable = true; Next();
        Check(Renew(IA("ACTChairSitAllow")).strName == "ACTChairSitEnd", "Repair-only work did not release autonomous sitting");
        candidates.Clear();
        var patch = CO("patch-only", "pristine"); Set(patch, "bCanPatch", true);
        patch.aInteractions = new List<string> { "ACTScrapPatch", "ACTItemPatch" }; candidates.Add(patch);
        Next();
        Check(Renew(IA("ACTChairSitAllow")).strName == "ACTChairSitEnd", "Patch-only work did not release autonomous sitting");
        Check(validated.SequenceEqual(new[] { "ACTItemPatch" }), "Scrap patch was treated as automatic patch work");
        candidates.Clear(); Next();
        Check(ReferenceEquals(Renew(IA("ACTChairSitAllow")), fallback), "Empty ship made idle crew get up");
        var restore = CO("restore-only"); Set(restore, "bCanUndamage", true); restore.aInteractions = new List<string> { "ACTItemUndamage" };
        candidates.AddRange(new[] { patch, repair, restore });
        actor.Clock = new Condowner.Utils.Clock();
        var recent = (Dictionary<string, double>)AccessTools.Field(typeof(CondOwner), "dictRecentlyTried").GetValue(actor);
        var native = AccessTools.Method(typeof(CondOwner), "ProcessAutoTasks");
        string NativeSelection() {
            nativeSelection = true; queuedName = null; recent.Clear();
            try { Check((bool)native.Invoke(actor, null), "Native work selection failed in priority oracle"); return queuedName; }
            finally { recent.Clear(); nativeSelection = false; }
        }
        var rules = company.mapRoster[actor.strID];
        List<AutoTask> Buffer(string name) => (List<AutoTask>)AccessTools.Field(typeof(CondOwner), name).GetValue(actor);
        foreach (string duty in new[] { "Patch", "Repair", "Restore" }) rules.aDutyLvls[Array.IndexOf(JsonCompanyRules.aDutiesNew, duty)] = 0;
        string vanillaFirst = NativeSelection(); unavailableName = vanillaFirst;
        string vanillaSecond = NativeSelection();
        Check(vanillaFirst == "ACTItemPatch" && vanillaSecond == "ACTItemUndamage", "Native repeated-scan priority corruption not reproduced");
        Check(ReferenceEquals(Buffer("aATsPatch"), Buffer("aATsRestore")), "Native repeated scan did not alias scratch fields");
        unavailableName = null;
        var scratchHooks = new Harmony("offline.crewrest.scratch");
        scratchHooks.CreateClassProcessor(typeof(AutoTaskScratchPatch)).Patch();
        try {
        NativeSelection();
        var patchBuffer = Buffer("aATsPatch"); var repairBuffer = Buffer("aATsRepair"); var restoreBuffer = Buffer("aATsRestore");
        // Compare against the shipped full ProcessAutoTasks method, rather than
        // predicting the production merge order with another copy of its rules.
        for (int p = 0; p < 3; p++) for (int r = 0; r < 3; r++) for (int s = 0; s < 3; s++) {
            rules.aDutyLvls[Array.IndexOf(JsonCompanyRules.aDutiesNew, "Patch")] = p;
            rules.aDutyLvls[Array.IndexOf(JsonCompanyRules.aDutiesNew, "Repair")] = r;
            rules.aDutyLvls[Array.IndexOf(JsonCompanyRules.aDutiesNew, "Restore")] = s;
            unavailableName = null;
            string first = NativeSelection();
            Next();
            Check(Renew(IA("ACTChairSitAllow")).strName == "ACTChairSitEnd" && validated[0] == first, "Seat probe disagrees with native first choice for duties " + p + "," + r + "," + s);
            unavailableName = first;
            string second = NativeSelection();
            Next();
            Check(Renew(IA("ACTChairSitAllow")).strName == "ACTChairSitEnd" && validated.SequenceEqual(new[] { first, second }), "Seat probe disagrees with native second choice for duties " + p + "," + r + "," + s + ": native=" + first + "," + second + "; probe=" + string.Join(",", validated));
        }
        unavailableName = null;
        Check(ReferenceEquals(patchBuffer, Buffer("aATsPatch")) && ReferenceEquals(repairBuffer, Buffer("aATsRepair")) && ReferenceEquals(restoreBuffer, Buffer("aATsRestore")), "Native scratch buffers were replaced after warmup");
        Check(!ReferenceEquals(patchBuffer, repairBuffer) && !ReferenceEquals(patchBuffer, restoreBuffer) && !ReferenceEquals(repairBuffer, restoreBuffer), "Native scratch category alias survived correction");
        Check(patchBuffer.Count == 0 && repairBuffer.Count == 0 && restoreBuffer.Count == 0, "Native scratch buffers retain candidate references after dispatch");
        throwProbe = true;
        bool threw = false;
        try { NativeSelection(); } catch (System.Reflection.TargetInvocationException error) { threw = error.InnerException is InvalidOperationException; }
        finally { throwProbe = false; }
        Check(threw && ReferenceEquals(patchBuffer, Buffer("aATsPatch")) && patchBuffer.Count == 0 && repairBuffer.Count == 0 && restoreBuffer.Count == 0, "Native selection exception swallowed or scratch state not restored");
        candidates.Clear(); candidates.Add(repair);
        rules.bRestorePermission = false; Next();
        Check(ReferenceEquals(Renew(IA("ACTChairSitAllow")), fallback) && scans == 0, "Repair bypassed restore permission");
        rules.bRestorePermission = true;
        throwProbe = true; Next(); Renew(IA("ACTChairSitAllow")); throwProbe = false;
        object table = AccessTools.Field(typeof(CrewIdleSeatPatch), "Probes").GetValue(null);
        object[] arguments = { actor, null };
        Check((bool)AccessTools.Method(table.GetType(), "TryGetValue").Invoke(table, arguments), "Probe state unavailable for lifetime check");
        foreach (var field in arguments[1].GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic))
            if (field.GetValue(arguments[1]) is ICollection list) Check(list.Count == 0, "Probe retains work targets after exceptional validation: " + field.Name);
        } finally { scratchHooks.UnpatchSelf(); }
    }
    static int Main() {
        var h = new Harmony("offline.crewrest.fixture");
        Patch(h, typeof(Debug), "LogWarning", nameof(Quiet), new[] { typeof(object) });
        Patch(h, typeof(UnityEngine.Object), "op_Equality", nameof(Equal)); Patch(h, typeof(UnityEngine.Object), "op_Inequality", nameof(Unequal));
        Patch(h, typeof(CondOwner), "HasCond", nameof(Has), new[] { typeof(string) });
        h.Patch(AccessTools.PropertyGetter(typeof(CondOwner), "Company"), prefix: new HarmonyMethod(typeof(Program), nameof(Company)));
        h.Patch(AccessTools.PropertyGetter(typeof(CondOwner), "Pathfinder"), prefix: new HarmonyMethod(typeof(Program), nameof(PF)));
        Patch(h, typeof(CondOwner), "OwnsShip", nameof(Owns)); Patch(h, typeof(CondOwner), "GetPos", nameof(Pos));
        Patch(h, typeof(CondOwner), "GetIsLikeNew", nameof(New)); Patch(h, typeof(CondOwner), "GetDamageState", nameof(Damage));
        Patch(h, typeof(CondOwner), "GetCOsSafe", nameof(Inventory)); Patch(h, typeof(CondOwner), "GetMove2", nameof(Choice));
        Patch(h, typeof(Ship), "GetCosDeferred", nameof(WorldSearch));
        Patch(h, typeof(Ship), "GetCOs", nameof(ShipItems), new[] { typeof(CondTrigger), typeof(bool), typeof(bool), typeof(bool) });
        Patch(h, typeof(DataHandler), "GetCondTrigger", nameof(TriggerDefinition), new[] { typeof(string) });
        Patch(h, typeof(DataHandler), "GetInteraction", nameof(InteractionDefinition), new[] { typeof(string), typeof(JsonInteractionSave), typeof(bool) });
        Patch(h, typeof(Interaction), "Triggered", nameof(Validate), new[] { typeof(CondOwner), typeof(CondOwner), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(List<string>) });
        Patch(h, typeof(CondOwner), "QueueInteraction", nameof(Queue), new[] { typeof(CondOwner), typeof(Interaction), typeof(bool), typeof(bool) });
        h.Patch(AccessTools.Method(typeof(Interaction), "GetReply"), transpiler: new HarmonyMethod(typeof(Program), nameof(ReplyCode)));
        h.CreateClassProcessor(typeof(CrewSleepPatch)).Patch(); h.CreateClassProcessor(typeof(CrewSleepInventoryPatch)).Patch();
        h.CreateClassProcessor(typeof(CrewSleepShipPatch)).Patch(); h.CreateClassProcessor(typeof(CrewIdleSeatPatch)).Patch();
        ship = Empty<Ship>(); ship.strRegID = "ship"; actor = CO("crew", "IsPlayerCrew"); seat = CO("chair");
        CrewSim.coPlayer = actor; path = Empty<Pathfinder>(); company = new JsonCompany();
        company.mapRoster[actor.strID] = new JsonCompanyRules { bRestorePermission = true };
        actor.jsShiftLast = Empty<JsonShift>(); actor.jsShiftLast.nID = 2;
        Set(actor, "dictRecentlyTried", new Dictionary<string, double>());
        SleepChecks(); SeatChecks(); WorkCategoryChecks(); AssetChecks();
        Console.WriteLine(checks + " crew sleep/idle-seat checks passed.");
        return 0;
    }
}
