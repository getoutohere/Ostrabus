using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Ionic.Zip;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ostrabus.BoneyardTurnover;
using SolarSystem;

internal static partial class Program
{
    private const string MercuryKind = "mercury-original-attachment-v1";
    private static readonly string[] MercuryIds = { "E-DZKX", "V-6BM", "E-XMH", "H-QS10", "O-267Z", "V-PH2F",
        "H-E8D3", "J-ZEK", "J-PYN2", "E-8UEN", "B-D26N", "E-H8KX" };

    private static ZipEntry Root(ZipFile zip) => zip.Entries.Where(x => !x.FileName.StartsWith("ships/", StringComparison.Ordinal) &&
        x.FileName.EndsWith(".json", StringComparison.Ordinal)).OrderByDescending(x => x.UncompressedSize).First();

    private static StarSystem MercuryWorld(JsonStarSystemSave saved)
    {
        var world = new StarSystem();
        CrewSim.system = world;
        StarSystem.fEpoch = saved.dfEpoch;
        foreach (var body in saved.aBOs) world.aBOs.Add(body.strName, new BodyOrbit(body));
        world.boStar = world.aBOs["Sol"];
        foreach (var body in world.aBOs.Values) if (body != world.boStar) body.boParent = world.boStar;
        foreach (string link in saved.dictBOHierarchy)
        {
            var p = link.Split('=');
            if (p.Length == 2 && world.aBOs.TryGetValue(p[0], out var child) && world.aBOs.TryGetValue(p[1], out var parent)) child.boParent = parent;
        }
        return world;
    }

    private static object MercuryField(StarSystem world, JsonSpawnDerelict ring)
    {
        var type = typeof(Plugin).Assembly.GetType("Ostrabus.BoneyardTurnover.Field", true);
        var field = Activator.CreateInstance(type, true);
        var anchor = world.GetBO("HQCH_DET");
        if (anchor == null || anchor.boParent?.strName != "Mercury") throw new InvalidDataException("Original Mercury yard reference is missing or changed.");
        anchor.UpdateTime(StarSystem.fEpoch);
        void Set(string key, object value) => type.GetField(key, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(field, value);
        Set("Anchor", anchor); Set("Center", anchor.vPos);
        Set("InnerKm", anchor.fRadiusKM + ring.fSpawnRadiusMinKM); Set("OuterKm", anchor.fRadiusKM + ring.fSpawnRadiusMaxKM);
        return field;
    }

    private static bool MercuryContains(object field, ShipSitu pose) => (bool)field.GetType().GetMethod("Contains",
        BindingFlags.Instance | BindingFlags.NonPublic).Invoke(field, new object[] { pose });

    private static JsonSpawnDerelict MercuryRing()
    {
        string data = Ostrabus.Development.InstallationPaths.Data;
        var ring = JsonConvert.DeserializeObject<JsonEnvironmentMap[]>(File.ReadAllText(Path.Combine(data,
            "blueprints", "environments", "environments.json"))).Single(x => x.strName == "100").aSpawnDerelictRings.Single();
        if (ring.strLootShipType != "RandomDerelictHQCH" || ring.strOwner != "HQCHCiv" || !ring.bIsBodyLocked || ring.bOrbitBO ||
            ring.fAltSpawnDistanceKM != 0 || ring.fSpawnRadiusMinKM != 400 || ring.fSpawnRadiusMaxKM != 700)
            throw new InvalidDataException("Reviewed Mercury definition changed.");
        return ring;
    }

    private static void AddStrings(JToken token, HashSet<string> into)
    {
        if (token is JValue value) { if (value.Type == JTokenType.String) into.Add((string)value); return; }
        if (token is JContainer container) foreach (var item in container.Children()) AddStrings(item, into);
    }

    private static void PlanMercury(string[] args)
    {
        if (args.Length != 3 && args.Length != 4) throw new ArgumentException("plan-mercury <patch.zip> <current.zip> <new-plan.json> [<previous-plan.json>]");
        if (File.Exists(args[2])) throw new IOException("Plan must be a new file.");
        string oldHash = Hash(args[0]), sourceHash = Hash(args[1]);
        if (oldHash != "95635C61DCEB0BFBADCB244C5FB515D3AA4694DC07BA2AD08DF3AAF90F072849")
            throw new InvalidDataException("Reference is not the audited patch archive.");
        string nativeHash = Hash(typeof(CrewSim).Assembly.Location);
        if (!(bool)typeof(Plugin).GetMethod("Supports", StaticInternal).Invoke(null, new object[] { nativeHash }))
            throw new InvalidDataException("Native assembly is not reviewed.");
        using var original = ZipFile.Read(args[0]);
        using var current = ZipFile.Read(args[1]);
        var oldGame = ReadGame(original);
        var rootEntry = Root(current);
        var header = ReadFields(rootEntry, "strPlayerCO", "objSystem", "objAIShipManager", "aPlots", "aBounties", "aJobs", "aTasksUnclaimed", "subscribedShips");
        var game = header.ToObject<JsonGameSave>();
        if (game.strPlayerCO != oldGame.strPlayerCO || game.objSystem.dfEpochGameStart != oldGame.objSystem.dfEpochGameStart ||
            game.objSystem.dfEpoch <= oldGame.objSystem.dfEpoch || game.objAIShipManager.strATCLast == "HQCH")
            throw new InvalidDataException("World/player identity differs or the player is in the repair region.");
        string seed = (string)ReadArray(current["saveInfo.json"])[0]["seedId"];
        var state = ReadObject(current[Entry]);
        Validate(state, game, seed);
        double assignmentEpoch = game.objSystem.dfEpoch;
        if (args.Length == 4)
        {
            var previousPlan = JObject.Parse(File.ReadAllText(args[3]));
            if ((string)previousPlan["repairKind"] != MercuryKind || (string)previousPlan["referenceSourceSHA256"] != oldHash ||
                (string)previousPlan["turnoverState"]?["World"] != (string)state["World"] ||
                (string)previousPlan["turnoverState"]?["Player"] != (string)state["Player"] ||
                (double?)previousPlan["assignedDays"] != 5)
                throw new InvalidDataException("Previous assignment belongs to a different repair/world.");
            assignmentEpoch = (double?)(previousPlan["assignmentEpoch"] ?? previousPlan["epoch"]) ?? double.NaN;
            if (double.IsNaN(assignmentEpoch) || double.IsInfinity(assignmentEpoch) || assignmentEpoch > game.objSystem.dfEpoch ||
                assignmentEpoch < oldGame.objSystem.dfEpoch ||
                Math.Abs((double)previousPlan["turnoverState"]["Fields"].Last["Departure"] - (assignmentEpoch - 5 * 86400d)) > .001)
                throw new InvalidDataException("Previous absence clock is invalid or later than this save.");
        }
        var fields = (JArray)state["Fields"];
        if (fields.Any(x => (string)x["Region"] == "HQCH")) throw new InvalidDataException("Mercury tracking already exists; inspect rather than overwrite it.");
        var preserved = fields.DeepClone();
        var ring = MercuryRing();
        var world = MercuryWorld(game.objSystem);
        var field = MercuryField(world, ring);
        var owners = Owners(game.objSystem.dictShipOwners);
        var protectedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in new[] { "objAIShipManager", "aPlots", "aBounties", "aJobs", "aTasksUnclaimed", "subscribedShips" })
            AddStrings(header[name], protectedIds);
        var hulls = new Dictionary<string, JObject>(StringComparer.Ordinal);
        foreach (var entry in current.Entries.Where(x => x.FileName.StartsWith("ships/", StringComparison.Ordinal) && x.FileName.EndsWith(".json", StringComparison.Ordinal)))
        {
            var hull = ReadFields(entry, "strRegID", "strName", "shipCO", "objSS", "ShipType", "DMGStatus", "bAIShip", "aCrew", "aShallowPSpecs", "aDocked",
                "strScanTargetID", "strCombatTargetID", "strStationKeepingTargetID", "strUndockID", "strTargetRegID");
            hulls.Add((string)hull["strRegID"], hull);
            foreach (string key in new[] { "strScanTargetID", "strCombatTargetID", "strStationKeepingTargetID", "strUndockID", "strTargetRegID" })
                AddStrings(hull[key], protectedIds);
            AddStrings(hull["objSS"]?["jnd"], protectedIds);
        }
        string data = Ostrabus.Development.InstallationPaths.Data;
        DataHandler.dictCOs = JsonConvert.DeserializeObject<JsonCondOwner[]>(File.ReadAllText(Path.Combine(data, "condowners", "condowners.json"))).ToDictionary(x => x.strName);
        DataHandler.dictCOOverlays = JsonConvert.DeserializeObject<JsonCOOverlay[]>(File.ReadAllText(Path.Combine(data, "cooverlays", "cooverlays.json"))).ToDictionary(x => x.strName);
        var changes = new JArray();
        var candidates = new JArray();
        var excluded = new JArray();
        var worldJson = (JObject)header["objSystem"];
        foreach (string id in MercuryIds)
        {
            var old = oldGame.objSystem.aStellarDerelicts.Single(x => x.strID == id);
            if (old.strParent != "HQCH_DET" || old.fOrbitW != 0 || old.strOwner != ring.strOwner || old.nMapID != 100)
                throw new InvalidDataException("Original record differs: " + id);
            hulls.TryGetValue(id, out var hull);
            var deferredRecords = ((JArray)worldJson["aStellarDerelicts"]).Select((item, index) => new { item, index }).Where(x => (string)x.item["strID"] == id).ToArray();
            if ((hull == null) == (deferredRecords.Length == 0) || deferredRecords.Length > 1)
                throw new InvalidDataException("Candidate is missing or has duplicate physical/deferred representations: " + id);
            if (protectedIds.Contains(id) || hull != null && protectedIds.Contains((string)hull["shipCO"]?["strID"]))
            { excluded.Add(new JObject { ["id"] = id, ["reason"] = "Active target, plot, bounty, job, controller or subscription reference" }); continue; }
            if (owners.TryGetValue(id, out string owner) && owner != ring.strOwner)
            { excluded.Add(new JObject { ["id"] = id, ["reason"] = "Changed ownership" }); continue; }
            ShipSitu pose;
            JToken record;
            string entryName, prefix;
            if (hull != null)
            {
                bool safe = owners.TryGetValue(id, out owner) && owner == ring.strOwner && (int?)hull["ShipType"] == 0 &&
                    (int?)hull["DMGStatus"] == (int)Ship.Damage.Derelict && (bool?)hull["bAIShip"] != true &&
                    Condition(hull["shipCO"], "EnvironmentMapID") == 100 && Condition(hull["shipCO"], "IsEnvironmentEntity") > 0 &&
                    !(hull["aCrew"] as JArray ?? new JArray()).Any() && hull["aDocked"]?.HasValues != true &&
                    (hull["aShallowPSpecs"] as JArray ?? new JArray()).All(x => DataHandler.GetCondOwnerDef((string)x["strName"])?.strName == "SysLootSpawner");
                if (!safe) { excluded.Add(new JObject { ["id"] = id, ["reason"] = "Physical ownership, occupancy, docking or environment guard" }); continue; }
                record = hull["objSS"];
                pose = new ShipSitu(record.ToObject<JsonShipSitu>());
                entryName = "ships/" + id + ".json"; prefix = "/0/objSS/";
            }
            else
            {
                record = deferredRecords[0].item;
                var dto = record.ToObject<JsonStellarDerelictSave>();
                if (dto.strOwner != ring.strOwner || dto.nMapID != 100 || dto.strLoot != old.strLoot)
                { excluded.Add(new JObject { ["id"] = id, ["reason"] = "Deferred template, owner or map changed" }); continue; }
                pose = new StellarDerelict(dto).objSS;
                entryName = rootEntry.FileName; prefix = "/0/objSystem/aStellarDerelicts/" + deferredRecords[0].index + "/";
            }
            if (!pose.bBOLocked || pose.bIsBO || pose.bOrbitLocked || pose.strBOPORShip != "Mercury" ||
                pose.fOrbitW == 0 || pose.HasNavData() || pose.IsAccelerating || pose.fA != 0)
            { excluded.Add(new JObject { ["id"] = id, ["reason"] = "Not the audited passive circular Mercury relock signature" }); continue; }
            void Change(string key, JToken next)
            {
                var previous = record[key] ?? throw new InvalidDataException("Required native property is missing: " + id + "/" + key);
                if (!JToken.DeepEquals(previous, next)) changes.Add(new JObject { ["entry"] = entryName, ["path"] = prefix + key,
                    ["old"] = previous.DeepClone(), ["new"] = next, ["id"] = id });
            }
            pose.strBOPORShip = "HQCH_DET"; pose.boReference = null;
            pose.vBOOffsetx = old.vBOOffsetx; pose.vBOOffsety = old.vBOOffsety;
            pose.fOrbitW = 0; pose.fOrbitTimeLast = StarSystem.fEpoch;
            pose.aPathRecent?.Clear();
            pose.UpdateTime(StarSystem.fEpoch);
            if (!MercuryContains(field, pose)) throw new InvalidDataException("Original placement fails production field bounds: " + id);
            if (hull == null)
            {
                Change("strParent", "HQCH_DET"); Change("vBOOffsetx", old.vBOOffsetx); Change("vBOOffsety", old.vBOOffsety);
                Change("fOrbitW", 0d); Change("fOrbitTimeLast", StarSystem.fEpoch);
            }
            else
            {
                Change("boPORShip", "HQCH_DET"); Change("vBOOffsetx", old.vBOOffsetx); Change("vBOOffsety", old.vBOOffsety);
                Change("fOrbitW", 0d); Change("fOrbitTimeLast", StarSystem.fEpoch);
                Change("vPosx", pose.vPosx); Change("vPosy", pose.vPosy); Change("vVelX", pose.vVelX); Change("vVelY", pose.vVelY);
                foreach (string key in new[] { "aPathRecentT", "aPathRecentX", "aPathRecentY" })
                    if (record[key] != null) Change(key, new JArray());
                var roundtrip = new ShipSitu(LitJson.JsonMapper.ToObject<JsonShipSitu>(LitJson.JsonMapper.ToJson(pose.GetJSON())));
                roundtrip.UpdateTime(StarSystem.fEpoch);
                if (!MercuryContains(field, roundtrip) || (roundtrip.vPos - pose.vPos).magnitude * 149597872d >= .001)
                    throw new InvalidDataException("Native repaired pose round trip failed: " + id);
            }
            candidates.Add(id);
        }
        if (candidates.Count == 0) throw new InvalidDataException("No unprotected members can be repaired.");
        string definition = (string)typeof(Plugin).Assembly.GetType("Ostrabus.BoneyardTurnover.Field", true)
            .GetMethod("Hash", StaticInternal).Invoke(null, new object[] { "100:HQCH_DET:" + JsonConvert.SerializeObject(ring) });
        var ages = new JObject();
        foreach (string id in candidates.Values<string>().OrderBy(x => x, StringComparer.Ordinal)) ages[id] = 0d;
        fields.Add(new JObject { ["Key"] = "HQCH|HQCH|RandomDerelictHQCH|HQCH_DET", ["Region"] = "HQCH", ["Definition"] = definition,
            ["Away"] = true, ["Departure"] = assignmentEpoch - 5 * 86400d, ["HalfLife"] = 7d, ["Shape"] = 1d,
            ["Seed"] = Guid.NewGuid().GetHashCode(), ["Ages"] = ages });
        Validate(state, game, seed);
        if (!JToken.DeepEquals(new JArray(fields.Take(fields.Count - 1)), preserved)) throw new InvalidDataException("Existing turnover history changed.");
        var plan = new JObject { ["repairKind"] = MercuryKind, ["currentSourceSHA256"] = sourceHash, ["referenceSourceSHA256"] = oldHash,
            ["nativeAssemblySHA256"] = nativeHash, ["requiredTurnoverSHA256"] = Hash(typeof(Plugin).Assembly.Location),
            ["epoch"] = game.objSystem.dfEpoch, ["rootEntry"] = rootEntry.FileName, ["candidateIds"] = candidates,
            ["excluded"] = excluded, ["creditedHours"] = (game.objSystem.dfEpoch - assignmentEpoch) / 3600d + 120,
            ["assignedDays"] = 5, ["assignmentEpoch"] = assignmentEpoch, ["changes"] = changes,
            ["turnoverState"] = state, ["productionPlanAccepted"] = true, ["installed"] = false,
            ["repairBasis"] = "Exact original HQCH_DET offsets from patch; preserve current contents and repair only the audited, currently unprotected circular-relock cohort. Five days is user-assigned absence, not reconstructed history." };
        if (Hash(args[0]) != oldHash || Hash(args[1]) != sourceHash) throw new IOException("A source save changed during planning.");
        File.WriteAllText(args[2], plan.ToString());
        Console.WriteLine("Mercury plan only: " + candidates.Count + " candidates, " + excluded.Count + " protected exclusions, " + changes.Count + " native property edits. No save written.");
    }

    private static void VerifyMercury(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("verify-mercury <original.zip> <prepared.zip> <plan.json>");
        var plan = JObject.Parse(File.ReadAllText(args[2]));
        var report = JObject.Parse(File.ReadAllText(args[1] + ".report.json"));
        if ((string)plan["repairKind"] != MercuryKind || (string)report["repairKind"] != MercuryKind ||
            (string)plan["currentSourceSHA256"] != Hash(args[0]) || (string)report["repairedSHA256"] != Hash(args[1]) ||
            (string)report["planSHA256"] != Hash(args[2]) || (string)plan["nativeAssemblySHA256"] != Hash(typeof(CrewSim).Assembly.Location) ||
            (bool?)report["exactNativeChangesVerified"] != true || (string)plan["requiredTurnoverSHA256"] != Hash(typeof(Plugin).Assembly.Location))
            throw new InvalidDataException("Preparation has not passed exact native change verification or its inputs changed.");
        using var zip = ZipFile.Read(args[1]);
        var game = ReadGame(zip);
        var ring = MercuryRing();
        var world = MercuryWorld(game.objSystem);
        var field = MercuryField(world, ring);
        int passed = 0;
        foreach (string id in plan["candidateIds"].Values<string>())
        {
            ShipSitu pose;
            var entry = zip["ships/" + id + ".json"];
            if (entry != null) pose = new ShipSitu(ReadHull(entry)["objSS"].ToObject<JsonShipSitu>());
            else pose = new StellarDerelict(game.objSystem.aStellarDerelicts.Single(x => x.strID == id)).objSS;
            pose.UpdateTime(StarSystem.fEpoch);
            if (!MercuryContains(field, pose) || pose.strBOPORShip != "HQCH_DET" || pose.fOrbitW != 0)
                throw new InvalidDataException("Native prepared-save field check failed: " + id);
            var roundTrip = new ShipSitu(LitJson.JsonMapper.ToObject<JsonShipSitu>(LitJson.JsonMapper.ToJson(pose.GetJSON())));
            roundTrip.UpdateTime(StarSystem.fEpoch);
            if (!MercuryContains(field, roundTrip) || (pose.vPos - roundTrip.vPos).magnitude * 149597872d >= .001)
                throw new InvalidDataException("Native prepared-save round trip failed: " + id);
            passed++;
        }
        string seed = (string)ReadArray(zip["saveInfo.json"])[0]["seedId"];
        var afterState = ReadObject(zip[Entry]);
        Validate(afterState, game, seed);
        using var before = ZipFile.Read(args[0]);
        var beforeState = ReadObject(before[Entry]);
        var afterFields = (JArray)afterState["Fields"];
        var beforeFields = (JArray)beforeState["Fields"];
        if (afterFields.Count != beforeFields.Count + 1 || !JToken.DeepEquals(beforeFields, new JArray(afterFields.Take(beforeFields.Count))))
            throw new InvalidDataException("Existing Luna/Phobos or other turnover records changed.");
        foreach (var property in beforeState.Properties().Where(x => x.Name != "Fields"))
            if (!JToken.DeepEquals(property.Value, afterState[property.Name])) throw new InvalidDataException("Turnover world/header state changed.");
        var newField = afterFields.Last;
        if ((string)newField["Key"] != "HQCH|HQCH|RandomDerelictHQCH|HQCH_DET" || (string)newField["Region"] != "HQCH" ||
            (bool?)newField["Away"] != true || Math.Abs((double)newField["Departure"] -
                ((double)(plan["assignmentEpoch"] ?? plan["epoch"]) - 5 * 86400d)) > .001 ||
            !((JObject)newField["Ages"]).Properties().Select(x => x.Name).OrderBy(x => x).SequenceEqual(plan["candidateIds"].Values<string>().OrderBy(x => x)) ||
            ((JObject)newField["Ages"]).Properties().Any(x => (double)x.Value != 0))
            throw new InvalidDataException("Mercury age or membership differs from the approved plan.");
        report["productionReaderAccepted"] = true;
        report["nativePoseRoundTripsVerified"] = passed;
        File.WriteAllText(args[1] + ".report.json", report.ToString());
        Console.WriteLine("Native prepared-copy verification passed for " + passed + " wrecks and all turnover metadata. Installed save unchanged.");
    }
}
