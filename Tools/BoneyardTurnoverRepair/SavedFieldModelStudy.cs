using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Runtime.Serialization;
using System.Globalization;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ostrabus.BoneyardTurnover;
using Ostranauts.Utils.Models;
using SolarSystem;

// Read-only model experiment. No ZIP writer, live API, patch installation or
// plugin initialization is used. Original Mercury placements exist only in RAM.
internal static class SavedFieldModelStudy
{
    private const double AuKm = 149597872d;
    private static int checks;
    private static double largestPoseErrorKm, largestVelocityError;
    private static readonly Type FieldType = typeof(Plugin).Assembly.GetType("Ostrabus.BoneyardTurnover.Field", true);
    private static readonly BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Dictionary<string, Dictionary<string, double>> nativeConditions = new Dictionary<string, Dictionary<string, double>>();
    private static bool UnityEqual(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result)
    { __result = ReferenceEquals(__0, __1); return false; }
    private static bool UnityUnequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result)
    { __result = !ReferenceEquals(__0, __1); return false; }

    private static bool HasCondition(CondOwner __instance, string __0, ref bool __result)
    {
        __result = nativeConditions.TryGetValue(__instance.strID, out var values) && values.TryGetValue(__0, out double n) && n > 0;
        return false;
    }
    private static bool ConditionAmount(CondOwner __instance, string __0, ref double __result)
    {
        __result = nativeConditions.TryGetValue(__instance.strID, out var values) && values.TryGetValue(__0, out double n) ? n : 0;
        return false;
    }

    private static void ProductionFields(StarSystem world, JsonStarSystemSave saved, JObject content, JsonEnvironmentMap[] maps)
    {
        var fixture = new Harmony("offline.saved-field-model.conditions");
        try
        {
            fixture.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Equality"), prefix: new HarmonyMethod(typeof(SavedFieldModelStudy), nameof(UnityEqual)));
            fixture.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Inequality"), prefix: new HarmonyMethod(typeof(SavedFieldModelStudy), nameof(UnityUnequal)));
            fixture.Patch(AccessTools.Method(typeof(CondOwner), nameof(CondOwner.HasCond), new[] { typeof(string) }),
                prefix: new HarmonyMethod(typeof(SavedFieldModelStudy), nameof(HasCondition)));
            fixture.Patch(AccessTools.Method(typeof(CondOwner), nameof(CondOwner.GetCondAmount), new[] { typeof(string), typeof(bool) }),
                prefix: new HarmonyMethod(typeof(SavedFieldModelStudy), nameof(ConditionAmount)));
            DataHandler.dictEnvironmentMaps = maps.ToDictionary(x => x.strName);
            var owners = new Dictionary<string, string>();
            for (int i = 0; i < saved.dictShipOwners.Length; i += 2) owners.Add(saved.dictShipOwners[i], saved.dictShipOwners[i + 1]);
            AccessTools.Field(typeof(StarSystem), "dictShipOwners").SetValue(world, owners);
            AccessTools.Field(typeof(AIShipManager), "dictSpawnRegIDs").SetValue(null, new Dictionary<string, List<string>>());
            foreach (var row in content["physical"])
            {
                string id = (string)row["id"];
                var hull = (Ship)FormatterServices.GetUninitializedObject(typeof(Ship));
                var co = (CondOwner)FormatterServices.GetUninitializedObject(typeof(CondOwner));
                AccessTools.Field(typeof(CondOwner), "_strID").SetValue(co, id + "-field-reader-fixture");
                var values = new Dictionary<string, double>();
                foreach (string condition in row["shipConditions"]?.Values<string>() ?? Enumerable.Empty<string>())
                {
                    int equal = condition.IndexOf('='), multiplier = condition.LastIndexOf('x');
                    if (equal > 0 && multiplier > equal) values[condition.Substring(0, equal)] = double.Parse(condition.Substring(multiplier + 1), CultureInfo.InvariantCulture);
                }
                nativeConditions[co.strID] = values;
                hull.strRegID = id;
                hull.json = new JsonShip { strName = (string)row["template"] };
                hull.Classification = (Ship.TypeClassification)(int)row["classification"];
                AccessTools.Field(typeof(Ship), "<ShipCO>k__BackingField").SetValue(hull, co);
                AccessTools.Field(typeof(Ship), "<objSS>k__BackingField").SetValue(hull, new ShipSitu(row["situation"].ToObject<JsonShipSitu>()));
                world.dictShips.Add(id, hull);
            }
            foreach (var item in saved.aStellarShips) world.dictStellarObjects.Add(item.strID, new StellarShip(item));
            foreach (var item in saved.aStellarDerelicts) world.dictStellarObjects.Add(item.strID, new StellarDerelict(item));
            foreach (var expected in new Dictionary<string, int> { ["HQCH"] = 0, ["EJDR"] = 19, ["MTRS"] = 10,
                ["JFTS"] = 15, ["SVIR"] = 13, ["MSUZ"] = 14, ["JPTN"] = 27 })
            foreach (double elapsed in new[] { 0d, 7200d, 21600d, 5 * 86400d, 8 * 86400d, 14 * 86400d })
            {
                StarSystem.fEpoch = saved.dfEpoch + elapsed;
                var fields = ((System.Collections.IEnumerable)FieldType.GetMethod("ForRegion", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { expected.Key })).Cast<object>().ToArray();
                Check(fields.Length == 1, "Full production reader rejected saved field " + expected.Key + " at " + elapsed + " seconds.");
                int map = (int)FieldType.GetField("Map", Instance).GetValue(fields[0]);
                string owner = ((JsonSpawnDerelict)FieldType.GetField("Ring", Instance).GetValue(fields[0])).strOwner;
                var poses = content["physical"].Where(x => (int?)x["mapID"] == map && (int?)x["classification"] == 0 && (string)x["owner"] == owner)
                    .Select(x => new ShipSitu(x["situation"].ToObject<JsonShipSitu>())).ToList();
                poses.AddRange(saved.aStellarDerelicts.Where(x => x.nMapID == map && x.strOwner == owner).Select(x => new StellarDerelict(x).objSS));
                Check(poses.Count(x => Contains(fields[0], x)) == expected.Value, "Full production reader's saved membership differs for " + expected.Key);
            }
            Console.WriteLine("Full production field reader: all seven real saved hierarchies pass through fourteen days, including physical Luna and deferred Ganymede centres.");
        }
        finally { fixture.UnpatchSelf(); nativeConditions.Clear(); }
    }

    private sealed class Specimen
    {
        internal string Id;
        internal ShipSitu Pose;
    }

    private static void Check(bool valid, string message)
    {
        checks++;
        if (!valid) throw new InvalidDataException(message);
    }

    private static string Hash(string path)
    {
        using var input = File.OpenRead(path);
        using var hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(input)).Replace("-", "");
    }

    private static void Set(object field, string name, object value) => FieldType.GetField(name, Instance).SetValue(field, value);
    private static bool Contains(object field, ShipSitu pose) =>
        (bool)FieldType.GetMethod("Contains", Instance).Invoke(field, new object[] { pose });

    private static ShipSitu Clone(ShipSitu pose) => new ShipSitu(LitJson.JsonMapper.ToObject<JsonShipSitu>(
        LitJson.JsonMapper.ToJson(pose.GetJSON())));

    private static void SamePose(ShipSitu a, ShipSitu b, string context)
    {
        double error = (a.vPos - b.vPos).magnitude * AuKm;
        double velocity = (a.vVel - b.vVel).magnitude * AuKm * 1000;
        largestPoseErrorKm = Math.Max(largestPoseErrorKm, error);
        largestVelocityError = Math.Max(largestVelocityError, velocity);
        Check(!double.IsNaN(error) && !double.IsInfinity(error) && error < .001, context + ": position differs by >=1 metre.");
        Check(!double.IsNaN(velocity) && !double.IsInfinity(velocity) && velocity < .00001, context + ": velocity differs by >=0.00001 m/s.");
        Check(a.bBOLocked == b.bBOLocked && a.strBOPORShip == b.strBOPORShip && a.fOrbitW == b.fOrbitW,
            context + ": attachment or orbital rate changed.");
    }

    internal static void Run(string[] args)
    {
        if (args.Length != 4) throw new ArgumentException(
            "test-field-model <current-world-audit.json> <current-content-audit.json> <patch-world-audit.json> <new-report.json>");
        if (File.Exists(args[3])) throw new IOException("Report must be a new file.");
        var inputHashes = args.Take(3).Select(Hash).ToArray();
        var root = JObject.Parse(File.ReadAllText(args[0]));
        var content = JObject.Parse(File.ReadAllText(args[1]));
        var reference = JObject.Parse(File.ReadAllText(args[2]));
        Check((string)root["archiveSHA256"] == (string)content["archiveSHA256"], "Current audits describe different saves.");
        var saved = root["fields"]["objSystem"].ToObject<JsonStarSystemSave>();
        var old = reference["fields"]["objSystem"].ToObject<JsonStarSystemSave>();
        Check(saved.dfEpochGameStart == old.dfEpochGameStart && saved.dfEpoch > old.dfEpoch, "Reference is not an earlier snapshot of this world.");
        var previousWorld = CrewSim.system;
        double previousEpoch = StarSystem.fEpoch;
        try
        {
            var world = new StarSystem();
            CrewSim.system = world;
            StarSystem.fEpoch = saved.dfEpoch;
            foreach (var body in saved.aBOs) world.aBOs.Add(body.strName, new BodyOrbit(body));
            world.boStar = world.aBOs["Sol"];
            foreach (var body in world.aBOs.Values) if (body != world.boStar) body.boParent = world.boStar;
            foreach (string text in saved.dictBOHierarchy)
            {
                var pair = text.Split('=');
                Check(pair.Length == 2, "Malformed saved hierarchy entry.");
                // Native hierarchy also contains generated asteroids, serialized
                // separately from aBOs. This study advances the field reference
                // hierarchy, not that unrelated asteroid population.
                if (!world.aBOs.TryGetValue(pair[0], out var child)) continue;
                Check(world.aBOs.ContainsKey(pair[1]), "A field-reference ancestor is absent.");
                child.boParent = world.aBOs[pair[1]];
            }
            var maps = JsonConvert.DeserializeObject<JsonEnvironmentMap[]>(File.ReadAllText(Path.Combine(
                Ostrabus.Development.InstallationPaths.Data, "blueprints", "environments", "environments.json")));
            ProductionFields(world, saved, content, maps);
            var rows = new JArray();
            var expected = new Dictionary<string, int> { ["HQCH"] = 0, ["EJDR"] = 19, ["MTRS"] = 10,
                ["JFTS"] = 0, ["SVIR"] = 13, ["MSUZ"] = 14, ["JPTN"] = 27 };
            foreach (var entry in expected)
            {
                string region = entry.Key;
                StarSystem.fEpoch = saved.dfEpoch;
                var station = content["physical"].Single(x => (string)x["id"] == region);
                var definition = maps.Single(x => x.strName == ((int)station["mapID"]).ToString());
                var ring = definition.aSpawnDerelictRings.Single();
                var stationPose = new ShipSitu(station["situation"].ToObject<JsonShipSitu>());
                stationPose.UpdateTime(StarSystem.fEpoch);
                bool ground = (int)station["classification"] == (int)Ship.TypeClassification.GroundStation ||
                    (int)station["classification"] == (int)Ship.TypeClassification.GroundStationUnfinished;
                var anchor = string.IsNullOrEmpty(ring.strSpawnAroundBody)
                    ? world.GetNearestBO(stationPose, StarSystem.fEpoch, !ground) : world.GetBO(ring.strSpawnAroundBody);
                Check(anchor != null, region + ": no native anchor.");
                anchor.UpdateTime(StarSystem.fEpoch);
                var field = Activator.CreateInstance(FieldType, true);
                Set(field, "Anchor", anchor);
                double bodyRadius = ring.fAltSpawnDistanceKM > 0 ? 0 : anchor.fRadiusKM;
                Set(field, "InnerKm", bodyRadius + ring.fSpawnRadiusMinKM);
                Set(field, "OuterKm", bodyRadius + ring.fSpawnRadiusMaxKM);
                ShipSitu centerReference = null, savedCenter = null;
                BodyOrbit centerParent = null;
                if (ring.fAltSpawnDistanceKM > 0)
                {
                    var centerHull = content["physical"].FirstOrDefault(x => (string)x["id"] == anchor.strName) ?? station;
                    centerReference = new ShipSitu(centerHull["situation"].ToObject<JsonShipSitu>());
                    centerReference.UpdateTime(StarSystem.fEpoch);
                    bool centerGround = (int)centerHull["classification"] == (int)Ship.TypeClassification.GroundStation ||
                        (int)centerHull["classification"] == (int)Ship.TypeClassification.GroundStationUnfinished;
                    centerParent = world.GetNearestBO(centerReference, StarSystem.fEpoch, !centerGround);
                    var marker = definition.aEntities.Where(x => x.bIsStation && x.fDistanceKM == ring.fAltSpawnDistanceKM &&
                        x.fAngle == ring.fAltSpawnAngle).Single();
                    string markerId = (string)typeof(EnvironmentEntities).GetMethod("GenerateRegID", BindingFlags.Static | BindingFlags.NonPublic)
                        .Invoke(null, new object[] { marker, region });
                    var markerSaved = saved.aStellarShips.SingleOrDefault(x => x.strID == markerId);
                    savedCenter = markerSaved != null ? new StellarShip(markerSaved).objSS :
                        new ShipSitu(content["physical"].Single(x => (string)x["id"] == markerId)["situation"].ToObject<JsonShipSitu>());
                }
                Point Center(bool useSavedCenter, double time)
                {
                    anchor.UpdateTime(time);
                    if (centerReference == null) return anchor.vPos;
                    if (useSavedCenter) { savedCenter.UpdateTime(time); return savedCenter.vPos; }
                    centerReference.UpdateTime(time); centerParent.UpdateTime(time);
                    return (Point)typeof(EnvironmentEntities).GetMethod("CalculateEntityPosition", BindingFlags.Static | BindingFlags.NonPublic)
                        .Invoke(null, new object[] { centerParent.vPos, centerReference.vPos, (double)ring.fAltSpawnDistanceKM, ring.fAltSpawnAngle });
                }
                var specimens = content["physical"].Where(x => (int?)x["mapID"] == (int)station["mapID"] &&
                    (int?)x["classification"] == 0 && (string)x["owner"] == ring.strOwner)
                    .Select(x => new Specimen { Id = (string)x["id"], Pose = new ShipSitu(x["situation"].ToObject<JsonShipSitu>()) }).ToList();
                specimens.AddRange(saved.aStellarDerelicts.Where(x => x.nMapID == (int)station["mapID"] && x.strOwner == ring.strOwner)
                    .Select(x => new Specimen { Id = x.strID, Pose = new StellarDerelict(x).objSS }));
                Check(specimens.Select(x => x.Id).Distinct().Count() == specimens.Count, region + ": duplicate representations.");
                Set(field, "Center", Center(false, saved.dfEpoch));
                int currentCount = specimens.Count(x => Contains(field, x.Pose));
                Check(currentCount == entry.Value, region + ": current spatial result changed: " + currentCount);
                var row = new JObject { ["region"] = region, ["anchor"] = anchor.strName,
                    ["specimens"] = specimens.Count, ["currentSpatialMatches"] = currentCount };
                if (region == "HQCH")
                {
                    var original = old.aStellarDerelicts.Where(x => specimens.Any(s => s.Id == x.strID)).ToArray();
                    Check(original.Length == 12 && original.All(x => x.strParent == "HQCH_DET" && x.fOrbitW == 0 && x.nMapID == 100),
                        "Original Mercury cohort is incomplete or has different motion.");
                    Check(anchor.strName == "HQCH_DET", "Mercury field adopted the planet instead of its native yard reference.");
                    specimens = original.Select(x => new Specimen { Id = x.strID, Pose = new StellarDerelict(x).objSS }).ToList();
                    row["inMemoryOriginalPlacementMatches"] = specimens.Count(x => Contains(field, x.Pose));
                    Check((int)row["inMemoryOriginalPlacementMatches"] == 12, "Original Mercury placements do not fit the unchanged field.");
                    foreach (var source in original)
                    {
                        var wreck = new StellarDerelict(source);
                        wreck.objSS.UpdateTime(saved.dfEpoch);
                        var copy = new StellarDerelict(LitJson.JsonMapper.ToObject<JsonStellarDerelictSave>(
                            LitJson.JsonMapper.ToJson(new JsonStellarDerelictSave(wreck))));
                        copy.objSS.UpdateTime(saved.dfEpoch);
                        SamePose(wreck.objSS, copy.objSS, "Mercury deferred round trip " + source.strID);
                        Check(copy.strLoot == source.strLoot && copy.strOwner == source.strOwner &&
                            copy.BreakInMultiplier == source.fBreakInMultiplier, "Deferred round trip changed authored contents.");
                    }
                }
                bool candidateCenter = savedCenter != null;
                if (candidateCenter)
                {
                    row["savedCenterDifferenceKm"] = (Center(false, saved.dfEpoch) - Center(true, saved.dfEpoch)).magnitude * AuKm;
                    Set(field, "Center", Center(true, saved.dfEpoch));
                    int corrected = specimens.Count(x => Contains(field, x.Pose));
                    row["savedCenterSpatialMatches"] = corrected;
                    Check(corrected == (region == "JFTS" ? 15 : entry.Value), region + ": saved native centre gives unexpected membership.");
                }
                int admitted = specimens.Count(x => Contains(field, x.Pose));
                int circular = specimens.Count(x => x.Pose.fOrbitW != 0);
                foreach (var specimen in specimens)
                {
                    StarSystem.fEpoch = saved.dfEpoch;
                    Set(field, "Center", Center(candidateCenter, StarSystem.fEpoch));
                    specimen.Pose.UpdateTime(StarSystem.fEpoch);
                    bool initiallyInside = Contains(field, specimen.Pose);
                    var initial = Clone(specimen.Pose);
                    SamePose(specimen.Pose, initial, region + " physical round trip " + specimen.Id);
                    foreach (double duration in new[] { 3600d, 7200d, 21600d, 5 * 86400d, 8 * 86400d, 14 * 86400d })
                    {
                        double end = saved.dfEpoch + duration;
                        var direct = Clone(initial);
                        StarSystem.fEpoch = end;
                        Set(field, "Center", Center(candidateCenter, end));
                        direct.UpdateTime(end);
                        Check(Contains(field, direct) == initiallyInside, region + ": membership changed with native motion at " + duration);
                        var stepped = Clone(initial);
                        double time = saved.dfEpoch;
                        while (time < end)
                        {
                            time = Math.Min(end, time + 3600);
                            StarSystem.fEpoch = time;
                            stepped.UpdateTime(time);
                        }
                        SamePose(direct, stepped, region + " direct/chunked " + specimen.Id + "/" + duration);
                        var reloaded = Clone(direct);
                        reloaded.UpdateTime(end);
                        SamePose(direct, reloaded, region + " reload " + specimen.Id);
                    }
                    StarSystem.fEpoch = saved.dfEpoch;
                    Set(field, "Center", Center(candidateCenter, StarSystem.fEpoch));
                    if (initiallyInside)
                    {
                        var free = Clone(initial); free.bBOLocked = false;
                        Check(!Contains(field, free), region + ": free movement bypasses membership guard.");
                        var docked = Clone(initial); docked._dockGroup = new DockGroup();
                        Check(!Contains(field, docked), region + ": docked group bypasses membership guard.");
                        var moved = Clone(initial); moved.vBOOffsetx += .1;
                        Check(!Contains(field, moved), region + ": moved-away hull bypasses field bounds.");
                    }
                }
                row["testedSpatialMatches"] = admitted;
                row["circularMembersPreserved"] = circular;
                row["timeHorizonsHours"] = new JArray(1, 2, 6, 120, 192, 336);
                rows.Add(row);
                Console.WriteLine(region + ": " + currentCount + " current; " + admitted + " in-memory model; native motion/round trips pass.");
            }
            for (int i = 0; i < inputHashes.Length; i++) Check(Hash(args[i]) == inputHashes[i], "Input audit was modified.");
            var report = new JObject { ["checks"] = checks, ["currentSnapshotSHA256"] = root["archiveSHA256"],
                ["referenceSnapshotSHA256"] = reference["archiveSHA256"], ["snapshotEpoch"] = saved.dfEpoch,
                ["nativeGameMvid"] = typeof(CrewSim).Module.ModuleVersionId.ToString("D"),
                ["productionTurnoverSHA256"] = Hash(typeof(Plugin).Assembly.Location),
                ["largestPoseErrorKm"] = largestPoseErrorKm, ["largestVelocityErrorMetersPerSecond"] = largestVelocityError,
                ["fields"] = rows, ["savesWritten"] = false, ["runtimeChangesInstalled"] = false,
                ["limits"] = "Detached saved-world motion, production spatial checks and native serialization; no AI lifecycle, market, collision or full GUI/PASS orchestration in this experiment. Actor/claim protections are exercised separately in the production turnover suite." };
            File.WriteAllText(args[3], report.ToString());
            Console.WriteLine(checks + " saved-field model checks passed; no saves or plugins modified.");
        }
        finally { CrewSim.system = previousWorld; StarSystem.fEpoch = previousEpoch; }
    }
}
