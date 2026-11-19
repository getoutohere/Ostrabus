using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Ionic.Zip;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ostrabus.BoneyardTurnover;

internal static partial class Program
{
    private const string Entry = "boneyard-turnover.state.json";
    private static readonly BindingFlags StaticInternal = BindingFlags.Static | BindingFlags.NonPublic;

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length > 0 && args[0] == "study-departure") DepartureStudy.Run(args.Skip(1).ToArray());
            else if (args.Length > 0 && args[0] == "study-fields") FieldStudy.Run(args.Skip(1).ToArray());
            else if (args.Length > 0 && args[0] == "test-field-model") SavedFieldModelStudy.Run(args.Skip(1).ToArray());
            else if (args.Length > 0 && args[0] == "assign-phobos") AssignPhobos(args.Skip(1).ToArray());
            else if (args.Length > 0 && args[0] == "plan-mercury") PlanMercury(args.Skip(1).ToArray());
            else if (args.Length > 0 && args[0] == "verify-mercury") VerifyMercury(args.Skip(1).ToArray());
            else Run(args);
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void Run(string[] args)
    {
        if (args.Length != 3 && args.Length != 4)
            throw new ArgumentException("Usage: BoneyardTurnoverRepair <first-outside.zip> <current.zip> <new-copy.zip> [<departure-study.json>]");
        string outsidePath = Path.GetFullPath(args[0]), currentPath = Path.GetFullPath(args[1]);
        string output = Path.GetFullPath(args[2]);
        if (File.Exists(output) || output.Equals(currentPath, StringComparison.OrdinalIgnoreCase) ||
            output.Equals(outsidePath, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Output must be a new copy; existing saves cannot be overwritten.");
        var plugin = typeof(Plugin).Assembly;
        string nativeHash = Hash(typeof(CrewSim).Assembly.Location);
        if (!(bool)plugin.GetType("Ostrabus.BoneyardTurnover.Plugin")
            .GetMethod("Supports", StaticInternal).Invoke(null, new object[] { nativeHash }))
            throw new InvalidOperationException("Unreviewed native assembly.");
        string outsideHash = Hash(outsidePath), currentHash = Hash(currentPath);
        string data = Ostrabus.Development.InstallationPaths.Data;
        DataHandler.dictCOs = JsonConvert.DeserializeObject<JsonCondOwner[]>(File.ReadAllText(
            Path.Combine(data, "condowners", "condowners.json"))).ToDictionary(x => x.strName);
        DataHandler.dictCOOverlays = JsonConvert.DeserializeObject<JsonCOOverlay[]>(File.ReadAllText(
            Path.Combine(data, "cooverlays", "cooverlays.json"))).ToDictionary(x => x.strName);
        using var outside = ZipFile.Read(outsidePath);
        using var current = ZipFile.Read(currentPath);
        var oldGame = ReadGame(outside);
        var game = ReadGame(current);
        var oldState = ReadObject(outside[Entry]);
        var state = ReadObject(current[Entry]);
        string seed = (string)ReadArray(current["saveInfo.json"])[0]["seedId"];
        string oldSeed = (string)ReadArray(outside["saveInfo.json"])[0]["seedId"];
        Validate(oldState, oldGame, oldSeed);
        Validate(state, game, seed);
        if (seed != oldSeed || (string)state["World"] != (string)oldState["World"] ||
            (string)state["Player"] != (string)oldState["Player"] ||
            game.objSystem.dfEpochGameStart != oldGame.objSystem.dfEpochGameStart ||
            game.objSystem.dfEpoch < oldGame.objSystem.dfEpoch ||
            oldGame.objAIShipManager.strATCLast == "EJDR" || game.objAIShipManager.strATCLast == "EJDR")
            throw new InvalidDataException("Saves do not establish the same ongoing absence from EJDR.");
        if (((JArray)state["Fields"]).Any(x => (string)x["Region"] == "EJDR"))
            throw new InvalidDataException("Current save already has EJDR history; refusing to replace it.");

        var owners = Owners(oldGame.objSystem.dictShipOwners);
        var currentOwners = Owners(game.objSystem.dictShipOwners);
        var ids = new List<string>();
        foreach (var entry in outside.Entries.Where(x => x.FileName.StartsWith("ships/", StringComparison.Ordinal) &&
            x.FileName.EndsWith(".json", StringComparison.Ordinal)))
        {
            var hull = ReadHull(entry);
            string id = (string)hull["strRegID"];
            if (Condition(hull["shipCO"], "EnvironmentMapID") != 1000 ||
                (int?)hull["ShipType"] != 0 || !owners.TryGetValue(id, out string owner) || owner != "EJDRCiv") continue;
            if (!Retainable(hull)) throw new InvalidDataException("Uncertain outside-save wreck: " + id);
            var now = current["ships/" + id + ".json"];
            if (now == null || !currentOwners.TryGetValue(id, out owner) || owner != "EJDRCiv" ||
                !Retainable(ReadHull(now)))
                throw new InvalidDataException("Candidate changed, disappeared or was claimed: " + id);
            ids.Add(id);
        }
        // This is a narrow evidence-based repair of the audited Luna cohort,
        // not a generic command for granting absence to arbitrary wrecks.
        if (ids.Count != 19 || ids.Distinct(StringComparer.Ordinal).Count() != ids.Count)
            throw new InvalidDataException("Expected exactly the 19 audited Luna wrecks.");

        string assets = Path.Combine(Ostrabus.Development.InstallationPaths.Data, "blueprints", "environments", "environments.json");
        var map = JsonConvert.DeserializeObject<JsonEnvironmentMap[]>(File.ReadAllText(assets))
            .Single(x => x.strName == "1000");
        var ring = map.aSpawnDerelictRings.Single();
        if (ring.strLootShipType != "RandomDerelictEJDR_GEO" || ring.strSpawnAroundBody != "EJDR_GEO" ||
            ring.strOwner != "EJDRCiv" || !ring.bIsBodyLocked || ring.bOrbitBO)
            throw new InvalidDataException("Luna field definition changed.");
        string definition = (string)plugin.GetType("Ostrabus.BoneyardTurnover.Field")
            .GetMethod("Hash", StaticInternal).Invoke(null, new object[]
            { "1000:EJDR_GEO:" + JsonConvert.SerializeObject(ring) });
        double start = oldGame.objSystem.dfEpoch;
        string basis = "Conservative first-proven-outside timestamp; exact exit time is unknown.";
        if (args.Length == 4)
        {
            var study = JObject.Parse(File.ReadAllText(args[3]));
            double exit = (double)study["exitEpoch"];
            if ((string)study["archiveSHA256"] != outsideHash ||
                (string)study["nativeGameMvid"] != typeof(CrewSim).Module.ModuleVersionId.ToString("D") ||
                (double)study["stationReplayErrorKm"] > .001 || (double)study["shipReplayErrorKm"] > .001 ||
                exit < (double)study["sixHourCaptureEpoch"] || exit > start ||
                (double)study["routeEndEpoch"] < start || double.IsNaN(exit) || double.IsInfinity(exit))
                throw new InvalidDataException("Departure reconstruction does not validate against this source save and assembly.");
            start = exit;
            basis = "Reconstructed physical EJDR range crossing from the saved native route and body hierarchy, validated against six-hour live positions; exact RegionCleanup callback time is not retained.";
        }
        var ages = new JObject();
        foreach (string id in ids.OrderBy(x => x, StringComparer.Ordinal)) ages[id] = 0d;
        ((JArray)state["Fields"]).Add(new JObject
        {
            ["Key"] = "EJDR|EJDR|RandomDerelictEJDR_GEO|EJDR_GEO",
            ["Region"] = "EJDR", ["Definition"] = definition, ["Away"] = true,
            ["Departure"] = start, ["HalfLife"] = 7d, ["Shape"] = 1d,
            ["Seed"] = Guid.NewGuid().GetHashCode(), ["Ages"] = ages
        });
        byte[] bytes = Encoding.UTF8.GetBytes(state.ToString(Formatting.None));
        Validate(state, game, seed);
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        // Update the optional entry in a new archive. Native JSON is untouched.
        current.UpdateEntry(Entry, bytes);
        using (var destination = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            current.Save(destination);
        using var repaired = ZipFile.Read(output);
        Validate(ReadObject(repaired[Entry]), game, seed);
        // Compare decompressed content, not just ZIP names or file lengths.
        using var original = ZipFile.Read(currentPath);
        int checkedEntries = 0;
        foreach (var entry in original.Entries.Where(x => x.FileName != Entry && !x.IsDirectory))
        {
            var copy = repaired[entry.FileName] ?? throw new InvalidDataException("Native entry missing from copy.");
            using var a = entry.OpenReader();
            using var b = copy.OpenReader();
            if (StreamHash(a) != StreamHash(b)) throw new InvalidDataException("Native payload changed: " + entry.FileName);
            checkedEntries++;
        }
        if (original.Entries.Count != repaired.Entries.Count || Hash(currentPath) != currentHash || Hash(outsidePath) != outsideHash)
            throw new IOException("Source changed during repair or archive membership changed.");
        var report = new JObject
        {
            ["outsideSourceSHA256"] = outsideHash, ["currentSourceSHA256"] = currentHash,
            ["repairedSHA256"] = Hash(output), ["nativeAssemblySHA256"] = nativeHash,
            ["departureEpoch"] = start, ["currentEpoch"] = game.objSystem.dfEpoch,
            ["creditedHours"] = (game.objSystem.dfEpoch - start) / 3600,
            ["departureBasis"] = basis,
            ["halfLifeDays"] = 7, ["curveShape"] = 1, ["candidateIds"] = new JArray(ids),
            ["fieldDefinition"] = definition, ["nativePayloadsHashVerified"] = checkedEntries,
            ["productionReaderAccepted"] = true, ["originalsUnchanged"] = true,
            ["installed"] = false, ["changedEntry"] = Entry,
            ["application"] = "Pending until a later native return to EJDR; all runtime protections are rechecked."
        };
        File.WriteAllText(output + ".report.json", report.ToString(Formatting.Indented));
        Console.WriteLine(report.ToString(Formatting.Indented));
    }

    private static bool Retainable(JObject hull)
    {
        var situ = hull["objSS"];
        return (int?)hull["ShipType"] == 0 && (bool?)hull["bAIShip"] != true &&
            (bool?)situ?["bBOLocked"] == true && (string)situ?["boPORShip"] == "EJDR_GEO" &&
            Condition(hull["shipCO"], "EnvironmentMapID") == 1000 &&
            Condition(hull["shipCO"], "IsEnvironmentEntity") > 0 &&
            !(hull["aCrew"] as JArray ?? new JArray()).Any() &&
            (hull["aShallowPSpecs"] as JArray ?? new JArray()).All(x =>
                DataHandler.GetCondOwnerDef((string)x["strName"])?.strName == "SysLootSpawner") &&
            hull["aDocked"]?.HasValues != true;
    }

    private static double Condition(JToken co, string name)
    {
        foreach (string text in co?["aConds"]?.Values<string>() ?? Enumerable.Empty<string>())
            if (text.StartsWith(name + "=", StringComparison.Ordinal))
                return double.Parse(text.Substring(text.LastIndexOf('x') + 1), System.Globalization.CultureInfo.InvariantCulture);
        return 0;
    }

    private static Dictionary<string, string> Owners(string[] pairs)
    {
        if (pairs == null || pairs.Length % 2 != 0) throw new InvalidDataException("Invalid owner table.");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < pairs.Length; i += 2) result.Add(pairs[i], pairs[i + 1]);
        return result;
    }

    private static JsonGameSave ReadGame(ZipFile zip)
    {
        var entry = zip.Entries.Where(x => !x.FileName.StartsWith("ships/", StringComparison.Ordinal) &&
            x.FileName.EndsWith(".json", StringComparison.Ordinal)).OrderByDescending(x => x.UncompressedSize).First();
        return ReadFields(entry, "strPlayerCO", "objAIShipManager", "objSystem").ToObject<JsonGameSave>();
    }

    private static JObject ReadHull(ZipEntry entry) => ReadFields(entry, "strRegID", "ShipType", "shipCO", "objSS",
        "aCrew", "aShallowPSpecs", "aDocked", "bAIShip");

    private static JObject ReadFields(ZipEntry entry, params string[] names)
    {
        using var stream = entry.OpenReader();
        using var text = new StreamReader(stream);
        using var reader = new JsonTextReader(text) { DateParseHandling = DateParseHandling.None };
        var result = new JObject();
        while (reader.Read())
        {
            if (reader.TokenType != JsonToken.PropertyName || reader.Depth != 2) continue;
            string name = (string)reader.Value;
            if (!reader.Read()) throw new InvalidDataException("Truncated native JSON.");
            if (names.Contains(name, StringComparer.Ordinal)) result[name] = JToken.ReadFrom(reader);
            else reader.Skip();
        }
        return result;
    }

    private static JObject ReadObject(ZipEntry entry)
    {
        if (entry == null) throw new InvalidDataException("Missing turnover metadata.");
        using var stream = entry.OpenReader();
        using var text = new StreamReader(stream);
        return JObject.Parse(text.ReadToEnd());
    }

    private static JArray ReadArray(ZipEntry entry)
    {
        using var stream = entry.OpenReader();
        using var text = new StreamReader(stream);
        return JArray.Parse(text.ReadToEnd());
    }

    private static void Validate(JObject state, JsonGameSave game, string seed)
    {
        var codec = typeof(Plugin).Assembly.GetType("Ostrabus.BoneyardTurnover.StateCodec");
        var value = codec.GetMethod("Read", StaticInternal).Invoke(null, new object[]
        { Encoding.UTF8.GetBytes(state.ToString(Formatting.None)), game, seed });
        if (value == null) throw new InvalidDataException("Production turnover reader rejected the metadata.");
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return StreamHash(stream);
    }

    private static string StreamHash(Stream stream)
    {
        using var hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "");
    }
}
