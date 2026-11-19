using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Newtonsoft.Json;
using Ostrabus.Development;
using Ostrabus.MiscBugFixes;
using Ostranauts.Ships.AIPilots;

static partial class Program {
    static PersonSpec eligibleVolunteer;
    static string freshFactoryId;
    static bool captureFresh;
    static int moves;
    static int nameRolls;
    static string lastNameGender;
    static Queue<(string First, string Last)> nextNames;
    sealed class FreshCreationBoundary : Exception { }

    static void FreshFactory(string strName, JsonCondOwnerSave jcos) {
        if (!captureFresh || jcos != null || DataHandler.mapCOs.ContainsKey(strName) || DataHandler.dictCOSaves.ContainsKey(strName)) return;
        freshFactoryId = strName;
        // Stop only genuine scene-object allocation. Lookup and the entire
        // native recruiter/profile path have already executed by this point.
        throw new FreshCreationBoundary();
    }
    static bool MoveBoundary(CondOwner objCO, Ship objShipNew) {
        objCO.ship?.People.Remove(objCO.pspec);
        Field(objCO, "_objShip", objShipNew);
        objShipNew.People.Add(objCO.pspec);
        moves++;
        return false;
    }
    static void FreshAttempt(JsonPersonSpec spec, Ship ship, string expected, params (string First, string Last)[] draws) {
        freshFactoryId = null;
        nextNames = new Queue<(string, string)>(draws);
        int rolls = nameRolls;
        bool fresh = false;
        try { AIShip.AddCrew(spec.strName, ship, "BCRS", false); }
        catch (FreshCreationBoundary) { fresh = true; }
        Check(fresh && freshFactoryId == expected, "New recruit reached an occupied identity or unexpected name: " + freshFactoryId);
        Check(nameRolls == rolls + draws.Length && nextNames.Count == 0, "Unexpected native name-draw count.");
        Check(lastNameGender == spec.strGender, "Retry did not preserve the profile's native gender input.");
    }
    static void RepairedCases(Harmony environment, Ship former, Ship recruited, JsonPersonSpec spec, PersonSpec oldProfile, MethodInfo factory) {
        int baseline = checks;
        var production = new Harmony("offline.ai-crew-identity.production");
        try {
            spec.strGender = "IsFemale";
            existing.bAlive = true;
            existing.mapConds = new Dictionary<string, Condition> { ["StatAge"] = new Condition(null) { fCount = 28 } };
            Field(existing, "aMyShips", new HashSet<string> { former.strRegID });
            DataHandler.dictStrings = new Dictionary<string, string>();
            DataHandler.mapCOs[Name] = existing; DataHandler.dictCOSaves.Clear();
            environment.Patch(factory, prefix: new HarmonyMethod(typeof(Program), nameof(FreshFactory)));
            environment.Patch(AccessTools.Method(typeof(CrewSim), nameof(CrewSim.MoveCO)), prefix: new HarmonyMethod(typeof(Program), nameof(MoveBoundary)));
            production.CreateClassProcessor(typeof(AiCrewIdentityPatch)).Patch();
            captureFresh = true;

            FreshAttempt(spec, recruited, "Amina Haddad", ("Tomiwa", "Trejo"), ("Amina", "Haddad"));
            Check(ReferenceEquals(existing.ship, former) && ReferenceEquals(existing.pspec, oldProfile) && former.People.Count == 1 && moves == 0,
                "Fresh name collision moved or replaced the serving pilot.");
            Check(existing.bAlive && existing.mapConds["StatAge"].fCount == 28 && existing.OwnsShip(former.strRegID),
                "Fresh name collision changed the pilot's age/life/ownership.");
            Check(ReferenceEquals(DataHandler.mapCOs[Name], existing), "Fresh name allocation replaced the original registry entry.");

            // Saved-only identities and even null-valued occupied keys must
            // remain unavailable; the native factory dispatches by key.
            DataHandler.mapCOs.Clear();
            DataHandler.dictCOSaves[Name] = new JsonCondOwnerSave { strID = Name, strCODef = "Crew01", strCondID = Name };
            FreshAttempt(spec, recruited, "Amina Haddad", ("Tomiwa", "Trejo"), ("Amina", "Haddad"));
            Check(DataHandler.dictCOSaves.Count == 1 && DataHandler.dictCOSaves[Name].strID == Name, "Allocator modified the existing character save.");
            DataHandler.mapCOs["Amina Haddad"] = null;
            DataHandler.dictCOSaves["Mei Chen"] = new JsonCondOwnerSave();
            FreshAttempt(spec, recruited, "Lina Okafor", ("Tomiwa", "Trejo"), ("Amina", "Haddad"), ("Mei", "Chen"), ("Lina", "Okafor"));
            FreshAttempt(spec, recruited, "Lina Okafor", Enumerable.Repeat(("Tomiwa", "Trejo"), 32).Concat(new[] { ("Lina", "Okafor") }).ToArray());
            DataHandler.mapCOs.Clear(); DataHandler.dictCOSaves.Clear();
            FreshAttempt(spec, recruited, Name, ("Tomiwa", "Trejo"));
            Check(existing.mapConds["StatAge"].fCount == 28 && ReferenceEquals(existing.ship, former), "Collision-free generation affected an unrelated actor.");

            // Explicit restoration and existing-profile reuse are not renamed.
            DataHandler.mapCOs[Name] = existing;
            stopReuse = false;
            Check(ReferenceEquals(DataHandler.GetCondOwner("Crew01", Name, null, true), existing), "Production patch changed live actor lookup semantics.");
            Check(ReferenceEquals(oldProfile.MakeCondOwner(PersonSpec.StartShip.OLD), existing), "Production patch changed an existing profile's identity.");
            DataHandler.mapCOs.Clear(); DataHandler.dictCOSaves[Name] = new JsonCondOwnerSave { strID = Name, strCODef = "Crew01", strCondID = Name };
            bool restored = false;
            try { DataHandler.GetCondOwner("Crew01", Name, null, false); }
            catch (RehydratedExisting) { restored = true; }
            Check(restored, "Production patch changed explicit saved actor restoration.");

            // A valid station volunteer follows the unchanged native reuse and
            // boarding path; only scene positioning is a fixture boundary.
            DataHandler.mapCOs[Name] = existing; DataHandler.dictCOSaves.Clear();
            var station = Hull("BCRS"); CrewSim.system.dictShips[station.strRegID] = station;
            former.People.Clear(); station.People.Add(oldProfile); Field(existing, "_objShip", station);
            eligibleVolunteer = oldProfile; freshFactoryId = null; moves = 0;
            int beforeRecruitment = nameRolls;
            var hired = AIShip.AddCrew(spec.strName, recruited, station.strRegID, false);
            Check(ReferenceEquals(hired, existing) && freshFactoryId == null && moves == 1 && ReferenceEquals(existing.ship, recruited),
                "Eligible station recruit was renamed, replaced or failed to board.");
            Check(ReferenceEquals(existing.pspec, oldProfile) && existing.mapConds["StatAge"].fCount == 28 && existing.bAlive,
                "Intentional recruitment regenerated the existing person's attributes.");
            Check(existing.OwnsShip(recruited.strRegID), "Native recruitment no longer records the new ship claim.");
            Check(nameRolls == beforeRecruitment, "Intentional recruitment drew a new name.");

            // Separate native population defect: recruitment filters the actual
            // regional source registry returned by GetAllATCRecycleStations.
            var pools = AccessTools.Field(typeof(AIShipManager), "dictPilotSrcRegIDs");
            object previousPools = pools.GetValue(null);
            var sharedPool = new List<string> { "BCRS" };
            try {
                pools.SetValue(null, new Dictionary<string, List<string>> { ["BCRS"] = sharedPool });
                eligibleVolunteer = null;
                nextNames = new Queue<(string, string)>(new[] { ("Source", "Probe") });
                bool reachedFactory = false;
                try { AIShip.AddCrew(spec.strName, recruited, "BCRS", true); }
                catch (FreshCreationBoundary) { reachedFactory = true; }
                Check(reachedFactory && sharedPool.Count == 0,
                    "Native regional recruitment no longer removes underpopulated sources from the shared registry; re-audit the population defect.");

                production.CreateClassProcessor(typeof(AiCrewRecruitmentPatch)).Patch();
                sharedPool.Add("BCRS");
                for (int attempt = 0; attempt < 2; attempt++) {
                    nextNames = new Queue<(string, string)>(new[] { ("Source", "Probe") });
                    reachedFactory = false;
                    try { AIShip.AddCrew(spec.strName, recruited, "BCRS", true); }
                    catch (FreshCreationBoundary) { reachedFactory = true; }
                    Check(reachedFactory && sharedPool.SequenceEqual(new[] { "BCRS" }),
                        "Recruitment changed its shared sources or bypassed native fallback.");
                }
                // A recovering station remains available on the NEXT search.
                eligibleVolunteer = oldProfile;
                station.People.Add(oldProfile); recruited.People.Remove(oldProfile); Field(existing, "_objShip", station);
                beforeRecruitment = nameRolls;
                hired = AIShip.AddCrew(spec.strName, recruited, "BCRS", true);
                Check(ReferenceEquals(hired, existing) && ReferenceEquals(existing.ship, recruited) && nameRolls == beforeRecruitment,
                    "Recovered regional source failed native volunteer reuse.");
                Check(sharedPool.SequenceEqual(new[] { "BCRS" }), "Adding OKLG_UNK leaked into the shared registry.");
                var local = AiCrewRecruitment.Sources("BCRS"); local.Clear();
                Check(sharedPool.Count == 1, "Recruitment returned the registry list by reference.");
                eligibleVolunteer = null;
                try { AiCrewRecruitmentPatch.Transpiler(Array.Empty<CodeInstruction>()).ToArray(); throw new Exception("Accepted changed recruiter IL"); }
                catch (InvalidOperationException) { checks++; }
            } finally { pools.SetValue(null, previousPools); }

            RoleTemplates();
            ValidateInstructionScope();
            Console.WriteLine((checks - baseline) + " identity repair checks passed: live/saved collisions, repeated native name draws, gender, native reuse/restoration and IL scope.");
        } finally {
            nextNames = null;
            eligibleVolunteer = null; captureFresh = false; production.UnpatchSelf();
        }
    }
    static void RoleTemplates() {
        var templates = JsonConvert.DeserializeObject<JsonPersonSpec[]>(File.ReadAllText(Path.Combine(InstallationPaths.Data, "personspecs", "personspecs.json")))
            .Where(p => p.strName.StartsWith("CivilianPilot", StringComparison.Ordinal) || p.strName.StartsWith("CombatPilot", StringComparison.Ordinal) ||
                p.strName == "OKLGScavCrew" || p.strName == "OKLGLEO" || p.strName == "OKLGShipAIPirate").ToArray();
        Check(templates.Length >= 20, "Missing native AI recruitment asset coverage.");
        foreach (var template in templates) {
            Check(string.IsNullOrEmpty(template.strType) && template.strFirstName == "" && template.strLastName == "", "Native role changed its human/random-name contract: " + template.strName);
            nextNames = new Queue<(string, string)>(new[] { ("Tomiwa", "Trejo"), ("Tomiwa", "Trejo"), ("Amina", "Haddad") });
            var native = new PersonSpec(template, false);
            var corrected = AiCrewIdentity.Create(template, false);
            Check(native.FullName == Name && corrected.FullName == "Amina Haddad" && nextNames.Count == 0,
                "Role template did not obtain an unused name from the native generator: " + template.strName);
            Check(lastNameGender == corrected.strGender, "Role retry changed gender input: " + template.strName);
            Check(corrected.strCareerNow == native.strCareerNow && corrected.nAgeMin == native.nAgeMin && corrected.nAgeMax == native.nAgeMax &&
                corrected.strLoot == native.strLoot && corrected.strLootConds == native.strLootConds && corrected.aFactionAdds == native.aFactionAdds,
                "Identity correction changed authored age/career/loot/faction policy: " + template.strName);
        }
        Console.WriteLine("Native asset coverage: " + templates.Length + " civilian/combat/scav/police recruitment templates.");
    }
    static void ValidateInstructionScope() {
        var target = AccessTools.Method(typeof(AIShip), nameof(AIShip.AddCrew), new[] { typeof(string), typeof(Ship), typeof(string), typeof(bool) });
        var reader = typeof(PatchProcessor).GetMethods().Single(m => m.Name == "GetOriginalInstructions" && m.GetParameters().Length == 2 && !m.GetParameters()[1].ParameterType.IsByRef);
        var original = ((IEnumerable<CodeInstruction>)reader.Invoke(null, new object[] { target, null })).ToList();
        var transpiler = AccessTools.Method(typeof(AiCrewIdentityPatch), "Transpiler");
        List<CodeInstruction> Transform(List<CodeInstruction> code) => ((IEnumerable<CodeInstruction>)transpiler.Invoke(null, new object[] { code })).ToList();
        var patched = Transform(original.Select(c => new CodeInstruction(c)).ToList());
        Check(patched.Count == original.Count, "Identity repair inserted extra instructions or changed control-flow length.");
        int changed = 0;
        for (int i = 0; i < original.Count; i++) {
            if (patched[i].opcode == original[i].opcode && Equals(patched[i].operand, original[i].operand)) continue;
            changed++;
            Check(original[i].opcode == OpCodes.Newobj && patched[i].Calls(AccessTools.Method(typeof(AiCrewIdentity), nameof(AiCrewIdentity.Create))),
                "Identity repair rewrote native selection/boarding or a different call.");
        }
        Check(changed == 1, "Identity repair must change exactly one constructor call.");
        var altered = original.Select(c => new CodeInstruction(c)).ToList();
        int constructor = altered.FindIndex(c => c.opcode == OpCodes.Newobj && c.operand is ConstructorInfo ctor && ctor.DeclaringType == typeof(PersonSpec));
        altered[constructor - 1].opcode = OpCodes.Ldc_I4_1;
        bool refused = false;
        try { Transform(altered); } catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { refused = true; }
        Check(refused, "Changed native constructor semantics were silently accepted.");
    }
}
