using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using Ionic.Zip;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ostrabus.BoneyardTurnover;
using Ostranauts.Core.Models;
using SolarSystem;
using UnityEngine;
using Object = UnityEngine.Object;
using BTField = Ostrabus.BoneyardTurnover.Field;
using BTRuntime = Ostrabus.BoneyardTurnover.Runtime;

internal static partial class Program
{
    private static readonly Dictionary<string, Dictionary<string, double>> fixtureConditions = new Dictionary<string, Dictionary<string, double>>();
    private static bool FixtureHasCond(CondOwner __instance, string __0, ref bool __result)
    {
        __result = fixtureConditions.TryGetValue(__instance.strID, out var conditions) && conditions.TryGetValue(__0, out var amount) && amount > 0;
        return false;
    }
    private static bool FixtureAmount(CondOwner __instance, string __0, ref double __result)
    {
        __result = fixtureConditions.TryGetValue(__instance.strID, out var conditions) && conditions.TryGetValue(__0, out var amount) ? amount : 0;
        return false;
    }
    private static UnityEngine.Random.State fixtureRandomState;
    private static bool GetRandomState(ref UnityEngine.Random.State __result) { __result = fixtureRandomState; return false; }
    private static bool SetRandomState(UnityEngine.Random.State __0) { fixtureRandomState = __0; return false; }
    private static bool InitRandomState(int __0)
    {
        // Unity engine RNG storage is unavailable outside the player. Supply
        // distinguishable storage, then execute the production try/finally.
        object boxed = new UnityEngine.Random.State();
        AccessTools.GetDeclaredFields(typeof(UnityEngine.Random.State)).First(x => !x.IsStatic).SetValue(boxed, __0);
        fixtureRandomState = (UnityEngine.Random.State)boxed;
        return false;
    }

    private static void ImplementationChecks(string work)
    {
        Check(Plugin.Supports(Plugin.NativeHash) && Plugin.Supports(Plugin.NativeHash1103) && Plugin.Supports(Plugin.NativeHash1104), "Reviewed 1.1.0.2/1.1.0.3/1.1.0.4 builds rejected");
        Check(!Plugin.Supports("unreviewed"), "Unreviewed turnover assembly admitted");
        var h = new Harmony("offline.boneyard-implementation");
        try
        {
            h.Patch(AccessTools.Method(typeof(Object), "op_Equality"), prefix: new HarmonyMethod(typeof(Program), nameof(Equal)));
            h.Patch(AccessTools.Method(typeof(Object), "op_Inequality"), prefix: new HarmonyMethod(typeof(Program), nameof(Unequal)));
            h.Patch(AccessTools.Method(typeof(Object), nameof(Object.DestroyImmediate), new[] { typeof(Object) }), prefix: new HarmonyMethod(typeof(Program), nameof(Quiet)));
            h.Patch(AccessTools.Method(typeof(Transform), nameof(Transform.GetEnumerator)), prefix: new HarmonyMethod(typeof(Program), nameof(EmptyChildren)));
            h.Patch(AccessTools.Method(typeof(CondOwner), nameof(CondOwner.ValidateParent)), prefix: new HarmonyMethod(typeof(Program), nameof(FixtureValidation)));
            h.Patch(AccessTools.Method(typeof(CondOwner), nameof(CondOwner.Destroy)), transpiler: new HarmonyMethod(typeof(Program), nameof(ReplaceUnityObjectLookup)));
            h.Patch(AccessTools.Method(typeof(CondOwner), nameof(CondOwner.HasCond), new[] { typeof(string) }), prefix: new HarmonyMethod(typeof(Program), nameof(FixtureHasCond)));
            h.Patch(AccessTools.Method(typeof(CondOwner), nameof(CondOwner.GetCondAmount), new[] { typeof(string), typeof(bool) }), prefix: new HarmonyMethod(typeof(Program), nameof(FixtureAmount)));
            h.Patch(AccessTools.PropertyGetter(typeof(UnityEngine.Random), nameof(UnityEngine.Random.state)), prefix: new HarmonyMethod(typeof(Program), nameof(GetRandomState)));
            h.Patch(AccessTools.PropertySetter(typeof(UnityEngine.Random), nameof(UnityEngine.Random.state)), prefix: new HarmonyMethod(typeof(Program), nameof(SetRandomState)));
            h.Patch(AccessTools.Method(typeof(UnityEngine.Random), nameof(UnityEngine.Random.InitState)), prefix: new HarmonyMethod(typeof(Program), nameof(InitRandomState)));
            LoadSpawnerDefinitions();
            ProductionCurves();
            ProductionEligibility(h);
            ProductionCommit();
            ProductionFailureStop(h);
            ProductionRandomScope();
            ProductionMetadata(h, work);
            FractionalTimestampChecks(h, work);
            ProductionCatalog();
            FieldCenterChecks();
            SavedLunaChecks();
            ProductionLifecycle(h);
            h.PatchAll(typeof(Plugin).Assembly);
            int hookCount = Harmony.GetAllPatchedMethods().Count(method => Harmony.GetPatchInfo(method).Owners.Contains(h.Id) &&
                Harmony.GetPatchInfo(method).Prefixes.Concat(Harmony.GetPatchInfo(method).Postfixes)
                    .Any(patch => patch.PatchMethod.DeclaringType.Assembly == typeof(Plugin).Assembly));
            Check(hookCount == 8, "Standalone native hook bindings changed: " + hookCount);
            Check(!typeof(Plugin).Assembly.GetReferencedAssemblies().Any(x => x.Name == "Ostrabus"), "Standalone references main plugin.");
            Console.WriteLine("Production implementation: curve/sampling, protection guards, native pair commits, RNG scope and native save-job hooks passed.");
        }
        finally { h.UnpatchSelf(); BTRuntime.Reset(); }
    }

    private static void ProductionCurves()
    {
        foreach (double shape in new[] { .5, 1d, 2d })
        {
            Check(Curve.TryProbability(0, 7, 7, shape, out var half) && Math.Abs(half - .5) < 1e-12, "Production half life differs.");
            Curve.TryProbability(0, 3, 7, shape, out double first);
            Curve.TryProbability(3, 4, 7, shape, out double second);
            Check(Math.Abs((1 - first) * (1 - second) - .5) < 1e-12, "Short visits alter survivor probability.");
        }
        Check(!Curve.TryProbability(0, 1, 0, 1, out _), "Invalid half life admitted.");
        Check(!Curve.TryProbability(double.NaN, 1, 7, 1, out _), "Invalid survivor age admitted.");
        double[] p = { .05, .2, .5, .9, 1, 0 };
        int[] hits = new int[p.Length];
        for (int step = 0; step < 10000; ++step)
        {
            var picked = Curve.Select(p, new[] { 4, 2, 1, 5, 0, 3 }, (step + .5) / 10000);
            int count = picked.Count(x => x);
            Check(count == 2 || count == 3, "Production sample count not randomized rounding.");
            for (int i = 0; i < p.Length; i++) if (picked[i]) hits[i]++;
        }
        Check(p.Select((x, i) => Math.Abs(hits[i] / 10000d - x) < .00011).All(x => x), "Production marginal probabilities changed.");
        var permutation = Curve.Shuffle(30, new System.Random(42));
        Check(permutation.Distinct().Count() == 30, "Shuffle repeated a candidate.");
    }

    private static BTField ProductionField()
    {
        var world = RiskWorld();
        StarSystem.fEpoch = 1000;
        PlotManager.dictPlotsActive = new Dictionary<string, JsonPlotSave>();
        fixtureConditions.Clear();
        var anchor = Empty<BodyOrbit>();
        anchor.strName = "study-anchor";
        anchor.dTimeCalcLast = anchor.dVelCalcLast = 1000;
        anchor.fPeriod = 31556926;
        world.aBOs[anchor.strName] = anchor;
        return new BTField
        {
            Key = "yard", Region = "region", Map = 100, Definition = "definition", Anchor = anchor,
            Center = new Ostranauts.Utils.Models.Point(0, 0), InnerKm = 400, OuterKm = 700,
            Ring = new JsonSpawnDerelict { strOwner = "study-yard", strLootShipType = "study-pool", bIsBodyLocked = true }
        };
    }

    private static Candidate ProductionHull(string id)
    {
        var hull = RiskHull(id);
        hull.objSS.vBOOffsetx = 500 / BTField.AuKm;
        hull.objSS.vPosx = hull.objSS.vBOOffsetx;
        ((Dictionary<string, string>)AccessTools.Field(typeof(StarSystem), "dictShipOwners").GetValue(CrewSim.system))[id] = "study-yard";
        fixtureConditions[hull.ShipCO.strID] = new Dictionary<string, double> { ["EnvironmentMapID"] = 100, ["IsEnvironmentEntity"] = 1 };
        return new Candidate { Id = id, Hull = hull };
    }

    private static Candidate ProductionDeferred(string id)
    {
        var wreck = new StellarDerelict(new JsonStellarDerelictSave
        {
            strID = id, strLoot = "study-template", strOwner = "study-yard", nMapID = 100,
            strParent = "study-anchor", vBOOffsetx = 500 / BTField.AuKm, nSize = 100
        });
        CrewSim.system.dictStellarObjects.Add(id, wreck);
        return new Candidate { Id = id, Deferred = wreck };
    }

    private static void ProductionEligibility(Harmony h)
    {
        var field = ProductionField();
        var old = ProductionHull("BT-PRODUCTION");
        Check(new Eligibility().Allows(field, old), "Production excludes visited saved-only shallow wreck.");
        var other = ProductionHull("BT-OTHER");
        foreach (string member in new[] { "shipScanTarget", "shipCombatTarget", "shipStationKeepingTarget", "shipUndock" })
        {
            Field(other.Hull, member, old.Hull);
            Check(!new Eligibility().Allows(field, old), "Production admitted target: " + member);
            Field(other.Hull, member, null);
        }
        other.Hull.shipSituTarget = old.Situ;
        Check(!new Eligibility().Allows(field, old), "Production admitted targeted situation.");
        other.Hull.shipSituTarget = null;
        foreach (string member in new[] { "strScanTargetID", "strCombatTargetID", "strStationKeepingTargetID", "strUndockID", "strTargetRegID" })
        {
            typeof(JsonShip).GetProperty(member).SetValue(other.Hull.json, old.Id);
            Check(!new Eligibility().Allows(field, old), "Production admitted saved target: " + member);
            typeof(JsonShip).GetProperty(member).SetValue(other.Hull.json, null);
        }
        old.Hull.People.Add(Empty<PersonSpec>());
        Check(!new Eligibility().Allows(field, old), "Unresolved person was unprotected.");
        old.Hull.People.Clear();
        old.Hull.json.aCrew = new[] { new JsonItem { strID = "crew" } };
        Check(!new Eligibility().Allows(field, old), "Saved crew was unprotected.");
        old.Hull.json.aCrew = null;
        old.Hull.json.aShallowPSpecs = new[] { new JsonItem { strID = "crew" } };
        Check(!new Eligibility().Allows(field, old), "Saved shallow crew was unprotected.");
        old.Hull.json.aShallowPSpecs = null;
        var marker = ProductionMarker(old.Hull, "BT-SPAWNER");
        old.Hull.json.aShallowPSpecs = new[] { new JsonItem { strID = marker.strID, strName = "SysLootSpawner" } };
        Check(new Eligibility().Allows(field, old), "Native shallow NPC spawn marker was mistaken for an occupant.");
        marker.pspec = Empty<PersonSpec>();
        Check(!new Eligibility().Allows(field, old), "Person attached to a spawn marker was unprotected.");
        marker.pspec = null;
        fixtureConditions[marker.strID]["IsLootSpawner"] = 0;
        Check(!new Eligibility().Allows(field, old), "A renamed ordinary loaded object was admitted as a marker.");
        fixtureConditions[marker.strID]["IsLootSpawner"] = 1;
        DataHandler.mapCOs.Remove(marker.strID);
        Check(!new Eligibility().Allows(field, old), "Unregistered loaded marker was admitted.");
        DataHandler.mapCOs[marker.strID] = marker;
        Field(marker, "_objCOParent", RiskCO("BT-EXTERNAL-PARENT"));
        Check(!new Eligibility().Allows(field, old), "Orphan spawn marker was admitted.");
        Field(marker, "_objCOParent", null);
        var markerNext = new StellarDerelict(new JsonStellarDerelictSave
            { strID = "BT-MARKER-NEXT", strLoot = "study-template", strOwner = "study-yard", nMapID = 100, strParent = "study-anchor" });
        // Real native Destroy must dispose of instantiated markers too.
        var markerHull = ProductionHull("BT-MARKER-HULL");
        var disposable = ProductionMarker(markerHull.Hull, "BT-DISPOSABLE-SPAWNER");
        h.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.RemoveCO)), prefix:
            new HarmonyMethod(typeof(Program), nameof(RemoveMarkerWithoutScene)));
        try { Replacement.Commit(markerHull, markerNext); }
        finally { h.Unpatch(AccessTools.Method(typeof(Ship), nameof(Ship.RemoveCO)), HarmonyPatchType.All, h.Id); }
        Check(disposable.bDestroyed && !DataHandler.mapCOs.ContainsKey(disposable.strID), "Native removal left a shallow spawn marker alive.");
        ((DictionaryArray<string, CondOwner>)AccessTools.Field(typeof(Ship), "mapICOs").GetValue(old.Hull)).Clear();
        DataHandler.mapCOs.Remove(marker.strID);
        old.Hull.json.aShallowPSpecs = null;
        Field(old.Situ, "_dockGroup", new DockGroup());
        Check(!new Eligibility().Allows(field, old), "Dock group was unprotected.");
        Field(old.Situ, "_dockGroup", null);
        Field(old.Hull, "nLoadState", Ship.Loaded.Full);
        Check(!new Eligibility().Allows(field, old), "Full hull was unprotected.");
        Field(old.Hull, "nLoadState", Ship.Loaded.Shallow);
        old.Hull.IsAIShip = true;
        Check(!new Eligibility().Allows(field, old), "AI hull was unprotected.");
        old.Hull.IsAIShip = false;
        PlotManager.dictPlotsActive["plot"] = new JsonPlotSave { dictCOTokens = new Dictionary<string, string> { ["target"] = old.Id } };
        Check(!new Eligibility().Allows(field, old), "Plot token was unprotected.");
        PlotManager.dictPlotsActive.Clear();
        var deferred = ProductionDeferred("BT-DEFERRED");
        Check(new Eligibility().Allows(field, deferred), "Production excludes valid deferred wreck.");
        deferred.Deferred.strOwner = "claimed";
        Check(!new Eligibility().Allows(field, deferred), "Claimed deferred wreck was admitted.");
        deferred.Deferred.strOwner = "study-yard";
        deferred.Situ.vBOOffsetx = 900 / BTField.AuKm;
        deferred.Situ.vPosx = deferred.Situ.vBOOffsetx;
        Check(!new Eligibility().Allows(field, deferred), "Moved wreck was admitted.");
        Check(new Eligibility().Allows(field, old), "Temporary guards permanently excluded a survivor.");
    }

    private static void ProductionCommit()
    {
        ProductionField();
        var old = ProductionHull("BT-OLD-VISITED");
        var next = new StellarDerelict(new JsonStellarDerelictSave { strID = "BT-NEXT", strLoot = "study-template", strOwner = "study-yard", nMapID = 100, strParent = "study-anchor" });
        Replacement.Commit(old, next);
        Check(old.Hull.bDestroyed && !CrewSim.system.dictShips.ContainsKey(old.Id) && CrewSim.system.dictStellarObjects.ContainsKey(next.strID), "Production pair commit failed.");
        var saved = CrewSim.system.GetJSONSave();
        Check(saved.aStellarDerelicts.Any(x => x.strID == next.strID) && !saved.dictShips.Contains(old.Id), "Native world save did not persist replacement independently.");
        var oldDeferred = ProductionDeferred("BT-OLD-DEFERRED");
        next = new StellarDerelict(new JsonStellarDerelictSave { strID = "BT-NEXT-DEFERRED", strLoot = "study-template", strOwner = "study-yard", nMapID = 100, strParent = "study-anchor" });
        Replacement.Commit(oldDeferred, next);
        Check(!CrewSim.system.dictStellarObjects.ContainsKey(oldDeferred.Id) && CrewSim.system.dictStellarObjects.ContainsKey(next.strID), "Deferred pair commit failed.");
    }

    private static void LoadSpawnerDefinitions()
    {
        string data = Ostrabus.Development.InstallationPaths.Data;
        var definition = JsonConvert.DeserializeObject<JsonCondOwner[]>(File.ReadAllText(
            Path.Combine(data, "condowners", "condowners.json"))).Single(x => x.strName == "SysLootSpawner");
        DataHandler.dictCOs = new Dictionary<string, JsonCondOwner> { [definition.strName] = definition };
        DataHandler.dictCOOverlays = JsonConvert.DeserializeObject<JsonCOOverlay[]>(File.ReadAllText(
            Path.Combine(data, "cooverlays", "cooverlays.json"))).Where(x => x.strCOBase == definition.strName).ToDictionary(x => x.strName);
        Check(DataHandler.GetCondOwnerDef("SysLootSpawnerBoarding") == definition &&
            DataHandler.GetCondOwnerDef("SysLootSpawnerNotBoarding") == definition,
            "Authored NPC spawner overlays were not resolved by the native resolver.");
    }

    private static CondOwner ProductionMarker(Ship hull, string id, string definition = "SysLootSpawner")
    {
        var marker = RiskCO(id);
        Field(marker, "bTriedGettingCrewRef", true);
        marker.strCODef = definition;
        fixtureConditions[id] = new Dictionary<string, double> { ["IsLootSpawner"] = 1 };
        Field(marker, "_objShip", hull);
        ((DictionaryArray<string, CondOwner>)AccessTools.Field(typeof(Ship), "mapICOs").GetValue(hull)).Add(id, marker);
        return marker;
    }

    private static bool RemoveMarkerWithoutScene(Ship __instance, CondOwner objCO)
    {
        // The RemoveCO wrapper needs real Unity tiles and UI managers. Supply
        // that scene boundary only: native RemoveInternal, Ship.Destroy and
        // CondOwner.Destroy still perform the registry/ownership cleanup.
        AccessTools.Method(typeof(Ship), "RemoveInternal").Invoke(__instance, new object[] { null, objCO });
        return false;
    }

    private static void ProductionRandomScope()
    {
        InitRandomState(17);
        var initial = fixtureRandomState;
        Check(Replacement.WithNativeRandom(123, () => !fixtureRandomState.Equals(initial)), "RNG scope was not seeded.");
        Check(fixtureRandomState.Equals(initial), "RNG scope changed global state.");
        try { Replacement.WithNativeRandom<int>(123, () => throw new IOException("Supplied native failure.")); }
        catch (IOException) { }
        Check(fixtureRandomState.Equals(initial), "RNG exception changed global state.");
    }

    private static void ProductionFailureStop(Harmony h)
    {
        var field = ProductionField();
        var first = ProductionHull("BT-FAIL-FIRST");
        var second = ProductionHull("BT-FAIL-SECOND");
        // Supply only staging; the production batch, protection recheck,
        // irreversible native destruction and failure-stop logic execute.
        h.Patch(AccessTools.Method(typeof(Replacement), nameof(Replacement.Prepare)), prefix: new HarmonyMethod(typeof(Program), nameof(FixtureReplacement)));
        h.Patch(AccessTools.Method(typeof(CondOwner), nameof(CondOwner.Destroy)), prefix: new HarmonyMethod(typeof(Program), nameof(InjectOwnerFailure)));
        failedConditionOwner = first.Hull.ShipCO.strID;
        var record = new FieldState
        {
            Key = field.Key, Region = field.Region, Definition = field.Definition,
            Departure = StarSystem.fEpoch - 1e9, HalfLife = 7, Shape = 1,
            Ages = new Dictionary<string, double> { [first.Id] = 0, [second.Id] = 0 }
        };
        bool finished = BTRuntime.Apply(field, record);
        Check(!finished && first.Hull.bDestroyed && !second.Hull.bDestroyed, "Native cleanup failure did not stop later deletions.");
        Check(CrewSim.system.dictStellarObjects.Count == 0, "Failure published a replacement for incomplete native cleanup.");
        Check(!CrewSim.system.dictShips.ContainsKey(first.Id), "Failure pretended to roll back native destruction.");
        failedConditionOwner = null;
        h.Unpatch(AccessTools.Method(typeof(Replacement), nameof(Replacement.Prepare)), HarmonyPatchType.All, h.Id);
        h.Unpatch(AccessTools.Method(typeof(CondOwner), nameof(CondOwner.Destroy)), HarmonyPatchType.Prefix, h.Id);
    }

    private static bool FixtureReplacement(ref StellarDerelict __result)
    {
        __result = new StellarDerelict(new JsonStellarDerelictSave
            { strID = "BT-FAIL-NEXT", strLoot = "study-template", strParent = "study-anchor", nMapID = 100, strOwner = "study-yard" });
        return false;
    }

    private static void ProductionMetadata(Harmony h, string work)
    {
        var cfg = new ConfigFile(Path.Combine(work, "production.cfg"), false);
        Plugin.EnableSetting = cfg.Bind("Turnover", "Enabled", true);
        Plugin.HalfLifeSetting = cfg.Bind("Turnover", "HalfLifeDays", 7d);
        Plugin.ShapeSetting = cfg.Bind("Turnover", "CurveShape", 1d);
        ProductionField();
        AIShipManager.strATCLast = "region";
        BTRuntime.Loaded(null);
        BTRuntime.State.Fields.Add(new FieldState
        {
            Key = "yard", Region = "other-region", Definition = "definition", Away = true,
            Departure = 900, HalfLife = 7, Shape = 2, Seed = 99,
            Ages = new Dictionary<string, double> { ["wreck"] = 4 }
        });
        var dto = RiskSave(work, "production-slot");
        dto.jGameSave.Item2.objAIShipManager = new JsonAIShipManagerSave { strATCLast = "region" };
        dto.jGameSave.Item2.strPlayerCO = "player";
        h.CreateClassProcessor(typeof(SaveStartHook)).Patch();
        h.CreateClassProcessor(typeof(SaveWriteHook)).Patch();
        var job = new SavingJob { SaveDto = dto };
        job.StartWithoutThreading();
        Check(job.Exception == null, "Production metadata broke native saving: " + job.Exception);
        string archive = RiskArchive(dto);
        byte[] bytes = RiskMetadataBytes(archive);
        Check(bytes != null, "Production hook failed to write optional archive entry.");
        var read = StateCodec.Read(bytes, dto.jGameSave.Item2, "risk-world");
        Check(read?.Fields.Count == 1 && read.Fields[0].Ages["wreck"] == 4, "Production state did not round trip.");
        Check(StateCodec.Read(bytes, dto.jGameSave.Item2, "other-world") == null, "Cross-world metadata admitted.");
        Check(StateCodec.Read(new byte[StateCodec.MaxBytes + 1], dto.jGameSave.Item2, "risk-world") == null, "Oversize metadata admitted.");
        Check(StateCodec.Read(System.Text.Encoding.UTF8.GetBytes("bad json"), dto.jGameSave.Item2, "risk-world") == null, "Malformed metadata admitted.");
        var corrupt = JObject.Parse(System.Text.Encoding.UTF8.GetString(bytes));
        corrupt.Remove("Schema");
        Check(StateCodec.Read(System.Text.Encoding.UTF8.GetBytes(corrupt.ToString()), dto.jGameSave.Item2, "risk-world") == null, "Missing schema admitted.");
        corrupt = JObject.Parse(System.Text.Encoding.UTF8.GetString(bytes));
        corrupt["Fields"][0]["Ages"]["wreck"] = -1;
        Check(StateCodec.Read(System.Text.Encoding.UTF8.GetBytes(corrupt.ToString()), dto.jGameSave.Item2, "risk-world") == null, "Negative survivor age admitted.");
        corrupt = JObject.Parse(System.Text.Encoding.UTF8.GetString(bytes));
        corrupt["Epoch"] = 999;
        Check(StateCodec.Read(System.Text.Encoding.UTF8.GetBytes(corrupt.ToString()), dto.jGameSave.Item2, "risk-world") == null, "Different snapshot epoch admitted.");
        BTRuntime.State.Fields[0].Ages["wreck"] = 99;
        using var zip = new ZipFile();
        Persistence.Write(job, zip);
        using var memory = new MemoryStream();
        zip.Save(memory);
        memory.Position = 0;
        using var frozenZip = ZipFile.Read(memory);
        using var frozenBytes = new MemoryStream();
        frozenZip[StateCodec.Entry].Extract(frozenBytes);
        Check(StateCodec.Read(frozenBytes.ToArray(), dto.jGameSave.Item2, "risk-world").Fields[0].Ages["wreck"] == 4, "Worker metadata was not frozen.");
        var files = new Dictionary<string, byte[]>
        {
            [StateCodec.Entry] = bytes,
            ["saveInfo.json"] = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new[] { dto.jSaveInfo.Item2 }))
        };
        Persistence.ResetLoad();
        Persistence.Read("risk.json", files, dto.jGameSave.Item2);
        BTRuntime.Loaded(Persistence.TakeLoaded());
        Check(BTRuntime.State.Fields.Count == 1, "Memory load lost metadata.");
        BTRuntime.Enter("other-region");
        Check(BTRuntime.State.Fields[0].Away, "Reload was treated as a region return.");
        h.Unpatch(AccessTools.Method(typeof(SavingJob), nameof(SavingJob.StartWithoutThreading)), HarmonyPatchType.All, h.Id);
        h.Unpatch(AccessTools.Method(typeof(SavingJob), "WriteSaveImagesToZip"), HarmonyPatchType.All, h.Id);
        dto = RiskSave(work, "production-without-plugin");
        job = new SavingJob { SaveDto = dto };
        job.StartWithoutThreading();
        Check(job.Exception == null && RiskMetadataBytes(RiskArchive(dto)) == null,
            "Native resave without plugin retained optional metadata.");
    }

    private static void ProductionCatalog()
    {
        string assets = Path.Combine(Ostrabus.Development.InstallationPaths.Game, "Ostranauts_Data", "StreamingAssets", "data");
        var maps = JsonConvert.DeserializeObject<JsonEnvironmentMap[]>(File.ReadAllText(Path.Combine(assets, "blueprints", "environments", "environments.json")));
        foreach (int map in new[] { 100, 1000, 1002, 10020, 1005, 1008, 1006, 1007, 102, 1020, 103, 800, 801, 802 })
        {
            ProductionField();
            DataHandler.dictEnvironmentMaps = maps.ToDictionary(x => x.strName);
            var station = ProductionHull("BT-STATION").Hull;
            station.Classification = Ship.TypeClassification.OrbitalStation;
            station.objSS.bIsRegion = true;
            station.objSS.bIsBO = true;
            Field(station, "aDockingPorts", new List<string> { "fixture-dock" });
            fixtureConditions[station.ShipCO.strID]["EnvironmentMapID"] = map;
            FieldStatic(typeof(AIShipManager), "dictSpawnRegIDs", new Dictionary<string, List<string>>());
            // Explicit authored references must exist separately from the
            // station. The default reference remains the manufactured anchor.
            var ring = DataHandler.dictEnvironmentMaps[map.ToString()].aSpawnDerelictRings?.FirstOrDefault();
            if (!string.IsNullOrEmpty(ring?.strSpawnAroundBody))
            {
                var body = Empty<BodyOrbit>();
                body.strName = ring.strSpawnAroundBody;
                body.fPeriod = 31556926;
                body.dTimeCalcLast = body.dVelCalcLast = StarSystem.fEpoch;
                CrewSim.system.aBOs[body.strName] = body;
                CrewSim.system.aBOs.Remove("study-anchor");
                station.objSS.strBOPORShip = body.strName;
            }
            if (ring?.fAltSpawnDistanceKM > 0) SeedCatalogCenter(station, DataHandler.dictEnvironmentMaps[map.ToString()], ring);
            var fields = BTField.ForRegion(station.strRegID);
            Check(fields.Count == (map == 800 || map == 801 || map == 802 ? 0 : 1), "Production catalog wrong for map " + map);
            if (fields.Count == 0) continue;
            var buoy = ProductionHull("BT-BUOY").Hull;
            buoy.Classification = Ship.TypeClassification.Outpost;
            fixtureConditions[buoy.ShipCO.strID]["EnvironmentMapID"] = map;
            FieldStatic(typeof(AIShipManager), "dictSpawnRegIDs", new Dictionary<string, List<string>>
                { [station.strRegID] = new List<string> { station.strRegID, buoy.strRegID } });
            Check(BTField.ForRegion(station.strRegID).Count == 1, "Inherited substation map ID created duplicate field: " + map);
            var field = fields[0];
            Check(field.Anchor.strName == (ring.strSpawnAroundBody ?? "study-anchor"), "Field collapsed its authored reference: " + map);
            Check(Math.Abs(field.OuterKm - field.InnerKm - (ring.fSpawnRadiusMaxKM - ring.fSpawnRadiusMinKM)) < 1e-8, "Field annulus changed: " + map);
            fixtureConditions[station.ShipCO.strID]["StationIsDerelictSpawner"] = 1;
            Check(BTField.ForRegion(station.strRegID).Count == 0, "Active hauler capability was ignored.");
        }
        Console.WriteLine("Production catalog: all eleven native field variants resolved; Earth/OKLG/VORB and authored hauler capabilities excluded.");
    }

    private static int generatedIds;
    private static bool FixtureId(ref string __result) { __result = "BT-NEW-" + (++generatedIds); return false; }
    private static bool RejectCrewPresence(ref bool __result) { __result = true; return false; }

    private static void ProductionLifecycle(Harmony h)
    {
        var field = ProductionField();
        var station = ProductionHull("BT-STATION").Hull;
        station.Classification = Ship.TypeClassification.OrbitalStation;
        station.objSS.bIsRegion = true;
        station.objSS.bIsBO = true;
        Field(station, "aDockingPorts", new List<string> { "fixture-dock" });
        var definition = new JsonEnvironmentMap
        {
            strName = "100", aSpawnDerelictRings = new[] { new JsonSpawnDerelict
            {
                strLootShipType = "RandomDerelictHQCH", strOwner = "study-yard", bIsBodyLocked = true,
                fSpawnRadiusMinKM = 400, fSpawnRadiusMaxKM = 700, aFactions = new[] { "study-faction" }
            } }
        };
        DataHandler.dictEnvironmentMaps = new Dictionary<string, JsonEnvironmentMap> { ["100"] = definition };
        DataHandler.dictLoot = new Dictionary<string, Loot>
        {
            ["RandomDerelictHQCH"] = Loot.CloneFromJSON(new JsonLoot { strName = "RandomDerelictHQCH", strType = "ship", aCOs = new[] { "study-template=1x1" } })
        };
        DataHandler.dictShips = new Dictionary<string, JsonShip>
        {
            ["study-template"] = new JsonShip { strName = "study-template", objSS = new JsonShipSitu { size = 100 } }
        };
        FieldStatic(typeof(AIShipManager), "dictSpawnRegIDs", new Dictionary<string, List<string>>());
        AIShipManager.strATCLast = station.strRegID;
        CollisionManager.strATCClosest = station.strRegID;
        BTRuntime.Loaded(null);
        var old = ProductionDeferred("BT-TRIP-OLD");
        Check(BTRuntime.BeforeDeparture() == null, "Install/reload was counted as departure.");
        CollisionManager.strATCClosest = "BT-AWAY";
        CrewSim.coPlayer = RiskCO("BT-PLAYER");
        var company = new JsonCompany();
        company.mapRoster["BT-MISSING-CREW"] = new JsonCompanyRules();
        Field(CrewSim.coPlayer, "objCompany", company);
        Check(BTRuntime.BeforeDeparture().Fields.Count == 0, "Unresolved crew permitted region absence.");
        CrewSim.coPlayer = null;
        h.Patch(AccessTools.Method(typeof(EnvironmentEntities), "IsCrewPresentInRegion"), prefix: new HarmonyMethod(typeof(Program), nameof(RejectCrewPresence)));
        var blocked = BTRuntime.BeforeDeparture();
        Check(blocked != null && blocked.Fields.Count == 0, "Crew remaining in region did not block absence.");
        h.Unpatch(AccessTools.Method(typeof(EnvironmentEntities), "IsCrewPresentInRegion"), HarmonyPatchType.All, h.Id);
        var departure = BTRuntime.BeforeDeparture();
        Check(departure?.Fields.Count == 1 && departure.Fields[0].Ages.ContainsKey(old.Id), "Departure did not capture native field cohort.");
        BTRuntime.AfterDeparture(departure);
        Check(BTRuntime.State.Fields.Count == 0, "Failed native region transition committed departure.");
        AIShipManager.strATCLast = "BT-AWAY";
        BTRuntime.AfterDeparture(departure);
        Check(BTRuntime.State.Fields.Count == 1 && BTRuntime.State.Fields[0].Away, "Successful departure did not commit.");
        Plugin.HalfLifeSetting.Value = 14;
        Check(BTRuntime.State.Fields[0].HalfLife == 7, "CFG edit changed already captured absence.");
        Plugin.HalfLifeSetting.Value = 7;
        StarSystem.fEpoch += 7 * 86400;
        field.Anchor.dTimeCalcLast = field.Anchor.dVelCalcLast = StarSystem.fEpoch;
        CollisionManager.strATCClosest = station.strRegID;
        departure = BTRuntime.BeforeDeparture();
        AIShipManager.strATCLast = station.strRegID;
        BTRuntime.AfterDeparture(departure);
        // An invalid template prevents removal, but must still consume the trip.
        DataHandler.dictShips.Clear();
        BTRuntime.Enter(station.strRegID);
        var record = BTRuntime.State.Fields.Single();
        Check(!record.Away && CrewSim.system.dictStellarObjects.ContainsKey(old.Id), "Failed staging deleted old hull or left trip rerollable.");
        Check(record.Ages[old.Id] == 7, "Survivor absent age was not accumulated.");
        BTRuntime.Enter(station.strRegID);
        Check(record.Ages[old.Id] == 7, "Repeated load aged survivor twice.");
        DataHandler.dictShips["study-template"] = new JsonShip { strName = "study-template", objSS = new JsonShipSitu { size = 100 } };
        h.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.GenerateID)), prefix: new HarmonyMethod(typeof(Program), nameof(FixtureId)));
        var candidate = new Eligibility().List(BTField.ForRegion(station.strRegID).Single()).Single();
        var actualField = BTField.ForRegion(station.strRegID).Single();
        int previousIdCounter = generatedIds;
        var prepared = Replacement.Prepare(actualField, candidate, 123, false);
        Check(prepared != null && !CrewSim.system.dictStellarObjects.ContainsKey(prepared.strID) && actualField.Contains(prepared.objSS), "Native loot/placement staging failed or published early.");
        Check(prepared.MapID == 100 && prepared.strOwner == "study-yard" && prepared.Factions.Single() == "study-faction", "Native replacement attributes changed.");
        DataHandler.dictCOSaves["historical-item"] = new JsonCondOwnerSave { strID = "historical-item", strRegIDLast = prepared.strID };
        generatedIds = previousIdCounter;
        Check(Replacement.Prepare(actualField, candidate, 123, false) == null, "Replacement reused an ID retained by saved native data.");
        DataHandler.dictCOSaves.Remove("historical-item");
        var obstacle = new StellarDerelict(new JsonStellarDerelictSave(prepared) { strID = "BT-OBSTACLE" });
        CrewSim.system.dictStellarObjects.Add(obstacle.strID, obstacle);
        Check(Replacement.Prepare(actualField, candidate, 123, false) == null, "Production allowed overlapping deferred wreck.");
        CrewSim.system.dictStellarObjects.Remove(obstacle.strID);
        h.Patch(AccessTools.Method(typeof(StarSystem), nameof(StarSystem.SetSituToRandomSafeCoords)), prefix: new HarmonyMethod(typeof(Program), nameof(RejectPosition)));
        Check(Replacement.Prepare(actualField, candidate, 123, false) == null && CrewSim.system.dictStellarObjects.ContainsKey(old.Id), "Failed native placement removed predecessor.");
        h.Unpatch(AccessTools.Method(typeof(StarSystem), nameof(StarSystem.SetSituToRandomSafeCoords)), HarmonyPatchType.All, h.Id);
        record.Away = false; record.Departure = StarSystem.fEpoch - 1e9; record.HalfLife = .001;
        BTRuntime.Apply(actualField, record);
        Check(!CrewSim.system.dictStellarObjects.ContainsKey(old.Id) && record.Ages.Count == 1 && record.Ages.Values.Single() == 0,
            "Production replacement failed or retained predecessor age.");
        string nextId = record.Ages.Keys.Single();
        var queue = (Queue<string>)AccessTools.Field(typeof(EnvironmentEntities), "_derelictSpawnQueue").GetValue(null);
        EnvironmentEntities.ClearDerelictSpawnQueue();
        EnvironmentEntities.LoadEnvironmentForRegion(station.strRegID);
        Check(queue.Count == 1 && queue.Peek() == nextId, "Native loading queued predecessor instead of committed replacement.");
        EnvironmentEntities.ClearDerelictSpawnQueue();
        // A story/mod edit to the current field invalidates the departed
        // definition; the old plan cannot restore or reroll that environment.
        CollisionManager.strATCClosest = "BT-AWAY";
        departure = BTRuntime.BeforeDeparture();
        AIShipManager.strATCLast = "BT-AWAY";
        BTRuntime.AfterDeparture(departure);
        definition.aSpawnDerelictRings[0].fSpawnRadiusMaxKM += 1;
        CollisionManager.strATCClosest = station.strRegID;
        departure = BTRuntime.BeforeDeparture();
        AIShipManager.strATCLast = station.strRegID;
        BTRuntime.AfterDeparture(departure);
        BTRuntime.Enter(station.strRegID);
        Check(BTRuntime.State.Fields.Count == 0 && CrewSim.system.dictStellarObjects.ContainsKey(nextId), "Changed field definition applied an obsolete turnover plan.");
        Console.WriteLine("Production lifecycle: departure/return, crew absence, captured CFG, failed staging, one-roll consumption, native placement, deferred clearance, replacement aging and native entry queue passed.");
    }
}
