using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BepInEx.Configuration;
using HarmonyLib;
using Ionic.Zip;
using LitJson;
using Newtonsoft.Json.Linq;
using Ostranauts.Core.Models;
using SolarSystem;

internal static partial class Program
{
    static int checks;
    const string MetadataEntry = "boneyard-turnover.state.json";
    static void AddMetadata(ZipFile zip) => zip.AddEntry(MetadataEntry, "{\"schemaVersion\":1,\"studyOnly\":true}");
    static void Check(bool valid, string message) { checks++; if (!valid) throw new InvalidOperationException(message); }
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static bool Quiet() => false;
    static bool RejectPosition(ShipSitu objSS, ref bool __result)
    {
        objSS.vPosx = .8;
        objSS.vPosy = .9;
        __result = false;
        return false;
    }

    static int Main()
    {
        var harmony = new Harmony("offline.boneyard-turnover-study");
        string work = Path.Combine(Path.GetTempPath(), "ostrabus-boneyard-study-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            CurveChecks(work);
            AssetChecks();
            NativeRecordsAndSaving(harmony, work);
            NativePlacementFailure(harmony);
            NativeRiskChecks(work);
#if IMPLEMENTATION_CHECKS
            ImplementationChecks(work);
#endif
            Console.WriteLine("Boneyard turnover study: " + checks + " checks passed.");
            Console.WriteLine("Offline only: no plugin registered, live APIs called, installed assets edited or player saves changed.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { harmony.UnpatchSelf(); }
    }

    static double Probability(double age, double days, double halfLife, double shape)
    {
        Check(CurvePrototype.TryProbability(age, days, halfLife, shape, out double value), "Valid curve input rejected.");
        return value;
    }

    static void CurveChecks(string work)
    {
        string file = Path.Combine(work, "curve.cfg");
        AccessTools.Field(typeof(BepInEx.Paths), "<BepInExConfigPath>k__BackingField").SetValue(null, Path.Combine(work, "BepInEx.cfg"));
        var config = new ConfigFile(file, false);
        var model = new CurvePrototype(config);
        Check(model.HalfLifeDays.Value == 7 && model.CurveShape.Value == 1, "BepInEx defaults differ from the proposal.");
        model.HalfLifeDays.Value = 10;
        model.CurveShape.Value = 2;
        config.Save();
        var restored = new CurvePrototype(new ConfigFile(file, false));
        Check(restored.HalfLifeDays.Value == 10 && restored.CurveShape.Value == 2, "Actual BepInEx CFG did not roundtrip curve settings.");
        foreach (double shape in new[] { .5, 1, 2 })
        {
            Check(Math.Abs(Probability(0, 7, 7, shape) - .5) < 1e-12, "Shape changed the meaning of half-life.");
            double previous = 0;
            foreach (double day in new[] { 0d, 1, 3.5, 7, 14, 21, 28, 365 })
            {
                double value = Probability(0, day, 7, shape);
                Check(value >= previous && value <= 1, "Curve is not monotonic/bounded.");
                previous = value;
            }
            double survive = 1;
            for (int day = 0; day < 14; day++) survive *= 1 - Probability(day, 1, 7, shape);
            Check(Math.Abs(survive - (1 - Probability(0, 14, 7, shape))) < 1e-12,
                "Accumulated-absence conditional probabilities permit trip splitting to alter survival.");
        }
        Check(Math.Abs(Probability(0, 14, 7, 1) - .75) < 1e-12, "Fourteen-day exponential default changed.");
        Check(Probability(100, 0, 7, 2) == 0, "A reload/no absence granted replacements.");
        Check(Probability(0, 1, 7, .5) > Probability(0, 1, 7, 1) && Probability(0, 1, 7, 2) < Probability(0, 1, 7, 1),
            "Config shape descriptions do not match the calculation.");
        foreach (double invalid in new[] { 0d, -1, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Check(!CurvePrototype.Valid(invalid), "Invalid positive configuration value admitted.");
            Check(!CurvePrototype.TryProbability(0, 1, invalid, 1, out _), "Invalid half-life was used.");
            Check(!CurvePrototype.TryProbability(0, 1, 7, invalid, out _), "Invalid shape was used.");
        }
        Check(!CurvePrototype.TryProbability(-1, 1, 7, 1, out _) && !CurvePrototype.TryProbability(0, -1, 7, 1, out _),
            "Negative or rolled-back absence was admitted.");
        var probabilities = new[] { .1, .5, .9, 0, 1 };
        var order = new[] { 3, 0, 4, 2, 1 };
        var hits = new int[probabilities.Length];
        bool bounded = true;
        for (int i = 0; i < 1000; i++)
        {
            var chosen = CurvePrototype.Select(probabilities, order, (i + .5) / 1000);
            int count = chosen.Count(value => value);
            bounded &= count == 2 || count == 3;
            for (int j = 0; j < chosen.Length; j++) if (chosen[j]) hits[j]++;
        }
        Check(bounded, "Mixed-age count escaped floor/ceil of expected count.");
        for (int j = 0; j < probabilities.Length; j++)
            Check(Math.Abs(hits[j] / 1000d - probabilities[j]) < .001, "Mixed-age selection changed individual probability.");
        Check(CurvePrototype.Select(probabilities, order, .123).SequenceEqual(CurvePrototype.Select(probabilities, order, .123)),
            "Replaying the same selection inputs rerolled candidates.");
        Console.WriteLine("CFG/curve: real BepInEx save/load, defaults, shape, bounds and partition-independent absent-age survival pass.");
    }

    static void AssetChecks()
    {
        string assets = Path.Combine(Ostrabus.Development.InstallationPaths.Game, "Ostranauts_Data", "StreamingAssets", "data");
        var maps = JArray.Parse(File.ReadAllText(Path.Combine(assets, "blueprints", "environments", "environments.json")))
            .OfType<JObject>().ToDictionary(map => (int)map["strName"]);
        var allowed = new[] { 100, 1000, 1002, 10020, 1005, 1008, 1006, 1007, 102, 1020, 103 };
        var pools = new HashSet<string>();
        foreach (int id in allowed)
        {
            var rings = (JArray)maps[id]["aSpawnDerelictRings"];
            Check(rings != null && rings.Count == 1, "Reviewed field ring layout changed: " + id);
            var ring = rings[0];
            pools.Add((string)ring["strLootShipType"]);
            Check((bool)ring["bIsBodyLocked"] && (double)ring["fSpawnRadiusMaxKM"] > (double)ring["fSpawnRadiusMinKM"],
                "Reviewed deferred field bounds/attachment changed: " + id);
        }
        Check(pools.Count == 7, "Expected seven distinct regional field pools.");
        Check(!allowed.Contains(800) && !allowed.Contains(801) && !allowed.Contains(802), "Earth/active-yard environments entered the allowlist.");
        foreach (string pool in pools)
        {
            bool found = Directory.GetFiles(Path.Combine(assets, "loot"), "*.json").Any(path =>
                JArray.Parse(File.ReadAllText(path)).OfType<JObject>().Any(row => (string)row["strName"] == pool));
            Check(found, "Native template pool not found: " + pool);
        }
        Console.WriteLine("Assets: eleven reviewed environment variants share seven static field pools; Earth/OKLG/VORB map IDs excluded.");
    }

    static void NativeRecordsAndSaving(Harmony harmony, string work)
    {
        var initial = new JsonStellarDerelictSave
        {
            strID = "BT-STUDY-ONLY", strLoot = "native-study-template", strOwner = "study-owner", nMapID = 100,
            strParent = "manufactured-orbital-reference", nSize = 100, fRot = .25f,
            vBOOffsetx = 1e-6, vBOOffsety = -2e-6, fOrbitW = .0002, fOrbitTimeLast = 1000,
            fBreakInMultiplier = .6f, aFactions = new[] { "study-faction" }
        };
        var native = new StellarDerelict(initial);
        var saved = new JsonStellarDerelictSave(native);
        var game = new JsonGameSave { strName = "study", objSystem = new JsonStarSystemSave { dfEpoch = 1000, aStellarDerelicts = new[] { saved } } };
        string folder = Path.Combine(work, "slot") + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(folder);
        var dto = new SaveDto { filepath = folder, persistenpath = work };
        dto.AddGameSave(game, folder + "study.json");
        dto.AddSaveInfo(new JsonSaveInfo { strName = "slot", playerName = "study", seedId = "study-world" }, folder + "saveInfo.json");
        var job = new SavingJob { SaveDto = dto };
        job.StartWithoutThreading();
        Check(job.IsDone && !job.WasCancelled && job.Exception == null, "Native synchronous SavingJob failed: " + job.Exception);
        string archive = Path.Combine(folder, "slot.zip");
        Check(File.Exists(archive), "Native SavingJob did not commit its expected zip path.");
        using (var zip = ZipFile.Read(archive))
        using (var stream = new MemoryStream())
        {
            zip["study.json"].Extract(stream);
            string json = System.Text.Encoding.UTF8.GetString(stream.ToArray());
            var loaded = JsonMapper.ToObject<JsonGameSave[]>(json)[0];
            var restored = new StellarDerelict(loaded.objSystem.aStellarDerelicts[0]);
            var again = new JsonStellarDerelictSave(restored);
            Check(again.strID == initial.strID && again.strLoot == initial.strLoot && again.nMapID == initial.nMapID,
                "Native identity/template/map did not survive actual zip serialization.");
            Check(again.strParent == initial.strParent && again.fOrbitW == initial.fOrbitW && again.vBOOffsetx == initial.vBOOffsetx,
                "Native reference/circular attachment did not survive actual zip serialization.");
            Check(again.strOwner == initial.strOwner && again.aFactions.SequenceEqual(initial.aFactions) &&
                Math.Abs(again.fBreakInMultiplier - initial.fBreakInMultiplier) < 1e-6, "Native ownership/salvage fields changed.");
            Check(!json.Contains("$type") && !json.Contains("BoneyardTurnover"), "Plugin type metadata entered the native save.");
        }
        string threadedFolder = Path.Combine(work, "threaded") + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(threadedFolder);
        var threadedDto = new SaveDto { filepath = threadedFolder, persistenpath = work };
        threadedDto.AddGameSave(game, threadedFolder + "study.json");
        threadedDto.AddSaveInfo(new JsonSaveInfo { strName = "threaded", playerName = "study" }, threadedFolder + "saveInfo.json");
        var threaded = new SavingJob { SaveDto = threadedDto };
        threaded.Start();
        threaded.JoinThread();
        Check(threaded.IsDone && !threaded.WasCancelled && threaded.Exception == null &&
            File.Exists(Path.Combine(threadedFolder, "threaded.zip")), "Native asynchronous SavingJob did not commit successfully.");

        // Deliberately make the destination parent a FILE in this private temp
        // fixture. Native SavingJob must report failure without committing data.
        string blocked = Path.Combine(work, "blocked");
        File.WriteAllText(blocked, "offline fixture");
        var badDto = new SaveDto { filepath = blocked + Path.DirectorySeparatorChar, persistenpath = work };
        badDto.AddGameSave(game, badDto.filepath + "study.json");
        badDto.AddSaveInfo(new JsonSaveInfo(), badDto.filepath + "saveInfo.json");
        var failed = new SavingJob { SaveDto = badDto };
        failed.StartWithoutThreading();
        Check(failed.IsDone && !failed.WasCancelled && failed.Exception != null,
            "Native failed save did not expose its completion/error distinction.");
        Check(File.ReadAllText(blocked) == "offline fixture", "Failed native save damaged its fixture parent.");

        var writeImages = AccessTools.Method(typeof(SavingJob), "WriteSaveImagesToZip");
        harmony.Patch(writeImages, postfix: new HarmonyMethod(typeof(Program), nameof(AddMetadata)));
        job.StartWithoutThreading();
        Check(job.Exception == null, "Optional archive metadata prevented native save completion.");
        var files = new Dictionary<string, byte[]>();
        using (var zip = ZipFile.Read(archive))
        {
            foreach (ZipEntry entry in zip)
            {
                using var bytes = new MemoryStream();
                entry.Extract(bytes);
                files.Add(entry.FileName, bytes.ToArray());
            }
        }
        Check(files.ContainsKey(MetadataEntry) && !MetadataEntry.Contains("ships/"), "Metadata is absent or enters the native ship-file selector.");
        harmony.Patch(AccessTools.Method(typeof(UnityEngine.Debug), nameof(UnityEngine.Debug.Log), new[] { typeof(object) }),
            prefix: new HarmonyMethod(typeof(Program), nameof(Quiet)));
        var loadedWithExtra = DataHandler.LoadSaveFile("study.json", files);
        Check(loadedWithExtra != null && loadedWithExtra.objSystem.aStellarDerelicts.Length == 1,
            "Actual native LoadSaveFile failed with the optional archive entry present.");
        files[MetadataEntry] = System.Text.Encoding.UTF8.GetBytes("invalid optional JSON");
        Check(DataHandler.LoadSaveFile("study.json", files)?.objSystem.aStellarDerelicts.Length == 1,
            "Corrupt optional metadata affected native save parsing.");
        harmony.Unpatch(writeImages, HarmonyPatchType.Postfix, harmony.Id);
        job.StartWithoutThreading();
        Check(job.Exception == null, "Native resave after metadata-hook removal failed.");
        using (var zip = ZipFile.Read(archive))
            Check(zip[MetadataEntry] == null && zip["study.json"] != null, "Native resave retained optional metadata or lost native data.");
        Console.WriteLine("Native save: actual SavingJob zip output -> LitJson DTO -> native StellarDerelict roundtrip passes without a registered turnover plugin.");
        Console.WriteLine("Native saving jobs: worker-thread commit passes; failed destination still completes with Exception set. IsDone alone is insufficient for sidecar commit.");
        Console.WriteLine("Optional archive entry: native save injection + native LoadSaveFile succeeds; removing the injection and resaving drops metadata. Complete Unity scene loading remains untested.");
    }

    static void NativePlacementFailure(Harmony harmony)
    {
        CrewSim.system = Empty<StarSystem>();
        StarSystem.fEpoch = 1000;
        var body = Empty<BodyOrbit>();
        body.strName = "placement-study";
        body.dTimeCalcLast = body.dVelCalcLast = 1000;
        harmony.Patch(AccessTools.Method(typeof(StarSystem), nameof(StarSystem.SetSituToRandomSafeCoords)),
            prefix: new HarmonyMethod(typeof(Program), nameof(RejectPosition)));
        harmony.Patch(AccessTools.Method(typeof(UnityEngine.Debug), nameof(UnityEngine.Debug.LogWarning), new[] { typeof(object) }),
            prefix: new HarmonyMethod(typeof(Program), nameof(Quiet)));
        var situation = new ShipSitu(false);
        var ring = new JsonSpawnDerelict { fSpawnRadiusMinKM = 100, fSpawnRadiusMaxKM = 150, bIsBodyLocked = true };
        var method = AccessTools.Method(typeof(EnvironmentEntities), "SetupDerelictPosition");
        double radius = (double)method.Invoke(null, new object[] { situation, body, 0d, 0d, ring, false });
        Check(radius >= 100 && radius <= 150 && situation.bBOLocked, "Native failed-placement continuation no longer reproduces.");
        Check(Math.Abs(situation.vPosx) > .1, "Native helper rejected/cleared the failed placement unexpectedly.");
        harmony.Unpatch(AccessTools.Method(typeof(StarSystem), nameof(StarSystem.SetSituToRandomSafeCoords)), HarmonyPatchType.Prefix, harmony.Id);
        Console.WriteLine("Native placement counterexample: supplied safe-coordinate failure still returns a radius and locks an out-of-bounds position. Replacement needs an explicit success check.");
    }
}
