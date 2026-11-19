using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

try
{
    if (args.Length == 4 && args[0] == "--prepare-mercury")
    {
        ExactNativeRepair.Prepare(args[1], args[2], args[3]);
        return 0;
    }
    if (args.Length == 1 && args[0] == "--test-exact-repair")
    {
        ExactNativeRepair.Test();
        return 0;
    }
    if (args.Length == 4 && args[0] == "--verify-native-copy")
    {
        VerifyNativeCopy(args[1], args[2], args[3]);
        return 0;
    }
    Run(args);
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine("Content audit failed: " + error.Message);
    return 1;
}

static void VerifyNativeCopy(string originalPath, string repairedPath, string reportPath)
{
    const string optional = "boneyard-turnover.state.json";
    using var originalFile = File.OpenRead(originalPath);
    using var repairedFile = File.OpenRead(repairedPath);
    string FileHash(Stream stream)
    {
        stream.Position = 0;
        string result = Convert.ToHexString(SHA256.HashData(stream));
        stream.Position = 0;
        return result;
    }
    string originalHash = FileHash(originalFile), repairedHash = FileHash(repairedFile);
    using var original = new ZipArchive(originalFile, ZipArchiveMode.Read, true);
    using var repaired = new ZipArchive(repairedFile, ZipArchiveMode.Read, true);
    var report = JObject.Parse(File.ReadAllText(reportPath));
    if ((string?)report["currentSourceSHA256"] != originalHash || (string?)report["repairedSHA256"] != repairedHash ||
        original.Entries.Count != repaired.Entries.Count ||
        original.Entries.Select(x => x.FullName).Distinct().Count() != original.Entries.Count ||
        repaired.Entries.Select(x => x.FullName).Distinct().Count() != repaired.Entries.Count)
        throw new InvalidDataException("Archive hashes or membership differ from the prepared repair.");
    int verified = 0;
    foreach (var entry in original.Entries.Where(x => x.FullName != optional))
    {
        using var a = entry.Open();
        using var b = (repaired.GetEntry(entry.FullName) ?? throw new InvalidDataException("Missing native entry.")).Open();
        if (!SHA256.HashData(a).SequenceEqual(SHA256.HashData(b)))
            throw new InvalidDataException("Native payload changed: " + entry.FullName);
        verified++;
    }
    if (FileHash(originalFile) != originalHash || FileHash(repairedFile) != repairedHash)
        throw new IOException("An archive changed during verification.");
    report["nativePayloadsHashVerified"] = verified;
    report["originalsUnchanged"] = true;
    File.WriteAllText(reportPath, report.ToString(Formatting.Indented));
    Console.WriteLine($"Verified {verified} identical native payloads; only optional turnover metadata differs.");
}
static void Run(string[] args)
{
    if (args.Length != 2 && args.Length != 4)
        throw new ArgumentException("Usage: SaveContentAudit <save.zip> <output.json> [<entry> <comma-separated-fields>]");
    string archive = Path.GetFullPath(args[0]);
    string output = Path.GetFullPath(args[1]);
    if (archive.Equals(output, StringComparison.OrdinalIgnoreCase))
        throw new ArgumentException("The output must not overwrite the source archive.");
    string Hash()
    {
        using var file = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(file));
    }

    string before = Hash();
    using var archiveStream = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    using var zip = new ZipArchive(archiveStream, ZipArchiveMode.Read);
    if (args.Length == 4)
    {
        var entry = zip.GetEntry(args[2]) ?? throw new InvalidDataException("Requested entry is absent.");
        var names = new HashSet<string>(args[3].Split(','), StringComparer.Ordinal);
        var fields = new JObject();
        using var input = new StreamReader(entry.Open());
        using var json = new JsonTextReader(input) { DateParseHandling = DateParseHandling.None };
        while (json.Read())
        {
            if (json.TokenType != JsonToken.PropertyName || json.Depth != 2) continue;
            string name = (string)json.Value!;
            if (!json.Read()) throw new InvalidDataException("Truncated entry.");
            if (names.Contains(name)) fields[name] = JToken.ReadFrom(json);
            else json.Skip();
        }
        if (Hash() != before) throw new IOException("Source changed during extraction.");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, new JObject { ["archiveSHA256"] = before,
            ["entry"] = entry.FullName, ["fields"] = fields }.ToString(Formatting.Indented));
        Console.WriteLine($"Read-only entry audit: {fields.Count} fields; archive hash unchanged.");
        return;
    }
    var root = zip.Entries.Where(entry => !entry.FullName.StartsWith("ships/", StringComparison.Ordinal) && entry.FullName.EndsWith(".json", StringComparison.Ordinal)).OrderByDescending(entry => entry.Length).First();
    var metadata = new JObject();
    using (var stream = new StreamReader(root.Open()))
    using (var reader = new JsonTextReader(stream)
    {
        DateParseHandling = DateParseHandling.None
    }

    )
    {
        // Stream past the potentially very large global CO table. No whole-save DOM.
        while (reader.Read())
        {
            if (reader.TokenType != JsonToken.PropertyName || reader.Depth != 2)
                continue;
            string name = (string)reader.Value!;
            if (!reader.Read())
                throw new InvalidDataException("Missing root value.");
            if (name is "strVersion" or "aPatchesApplied" or "objSystem" or "aPlots" or "aPlotsOld")
                metadata[name] = JToken.ReadFrom(reader);
            else
                reader.Skip();
        }
    }

    var world = (JObject? )metadata["objSystem"] ?? throw new InvalidDataException("Missing saved system.");
    var owners = new Dictionary<string, string>(StringComparer.Ordinal);
    var ownerList = (JArray? )world["dictShipOwners"] ?? new JArray();
    if (ownerList.Count % 2 != 0)
        throw new InvalidDataException("Odd ship-owner list.");
    for (int i = 0; i < ownerList.Count; i += 2)
        owners[(string)ownerList[i]!] = (string)ownerList[i + 1]!;
    double Condition(JToken? co, string name)
    {
        double result = 0;
        foreach (string? text in co?["aConds"]?.Values<string>() ?? [])
        {
            if (text == null || !text.StartsWith(name + "=", StringComparison.Ordinal))
                continue;
            int split = text.LastIndexOf('x');
            if (split < 0 || !double.TryParse(text[(split + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out result))
                throw new InvalidDataException("Unsupported saved condition: " + text);
        }

        return result;
    }

    var ships = new JArray();
    foreach (var entry in zip.Entries.Where(entry => entry.FullName.StartsWith("ships/", StringComparison.Ordinal) && entry.FullName.EndsWith(".json", StringComparison.Ordinal)))
    {
        using var stream = new StreamReader(entry.Open());
        using var reader = new JsonTextReader(stream)
        {
            DateParseHandling = DateParseHandling.None
        };
        var kept = new JObject();
        while (reader.Read())
        {
            if (reader.TokenType != JsonToken.PropertyName || reader.Depth != 2)
                continue;
            string name = (string)reader.Value!;
            if (!reader.Read())
                throw new InvalidDataException("Missing ship value.");
            if (name is "strRegID" or "strName" or "strTemplateName" or "publicName" or "shipCO" or "objSS" or "DMGStatus" or "ShipType" or "fFirstVisit" or "aDocked" or "bAIShip" or "aCrew" or "aShallowPSpecs")
                kept[name] = JToken.ReadFrom(reader);
            else
                reader.Skip();
        }

        string id = (string? )kept["strRegID"] ?? throw new InvalidDataException("Missing ship ID.");
        var co = kept["shipCO"];
        var situation = kept["objSS"];
        ships.Add(new JObject
        {
            ["id"] = id,
            ["template"] = kept["strTemplateName"] ?? kept["strName"],
            ["name"] = kept["publicName"],
            ["owner"] = owners.TryGetValue(id, out var owner) ? owner : null,
            ["damage"] = kept["DMGStatus"],
            ["classification"] = kept["ShipType"],
            ["firstVisit"] = kept["fFirstVisit"],
            ["ai"] = kept["bAIShip"],
            ["dockingLinks"] = (kept["aDocked"] as JObject)?.Count ?? 0,
            ["parent"] = situation?["boPORShip"],
            ["region"] = situation?["bIsRegion"] ?? false,
            ["bodyLocked"] = situation?["bBOLocked"] ?? false,
            ["mapID"] = Condition(co, "EnvironmentMapID"),
            ["environment"] = Condition(co, "IsEnvironmentEntity") > 0,
            ["legacyDerelict"] = Condition(co, "IsDerelict") > 0,
            ["orbitRate"] = situation?["fOrbitW"] ?? 0,
            ["situation"] = situation,
            ["shipConditions"] = co?["aConds"],
            ["crewDefinitions"] = (kept["aCrew"] as JArray)?.Count ?? 0,
            ["shallowSpawnMarkers"] = (kept["aShallowPSpecs"] as JArray)?.Count ?? 0,
            ["shallowSpawnDefinitions"] = new JArray(kept["aShallowPSpecs"]?.Select(x => x["strName"]) ?? [])
        });
    }

    var deferred = new JArray();
    foreach (string key in new[]
    {
        "aStellarShips",
        "aStellarDerelicts"
    }

    )
        foreach (var item in world[key] as JArray ?? new JArray())
            deferred.Add(new JObject { ["id"] = item["strID"], ["template"] = item["strLoot"], ["parent"] = item["strParent"], ["owner"] = item["strOwner"], ["mapID"] = item["nMapID"], ["wreck"] = key == "aStellarDerelicts" });
    var bodyMaps = new JArray((world["aBOs"] as JArray ?? new JArray()).Select(body => new JObject
    {
        ["id"] = body["strName"], ["mapID"] = body["nEnvironmentMapID"] ?? 0,
        ["atmospheres"] = body["aAtmospheres"],
        ["savedOrbit"] = body.DeepClone()
    }));
    var populations = new JArray(deferred.Concat(ships.Where(ship => (bool? )ship["environment"] == true && (double? )ship["mapID"] > 0)).GroupBy(item => (int? )item["mapID"] ?? 0).OrderBy(group => group.Key).Select(group => new JObject { ["mapID"] = group.Key, ["deferred"] = group.Count(item => item["wreck"] != null), ["physical"] = group.Count(item => item["wreck"] == null), ["deferredWrecks"] = group.Count(item => (bool? )item["wreck"] == true) }));
    if (Hash() != before)
        throw new IOException("Source archive changed during read; retry when saving finishes.");
    var result = new JObject
    {
        ["archiveSHA256"] = before,
        ["epoch"] = world["dfEpoch"],
        ["version"] = metadata["strVersion"],
        ["patches"] = metadata["aPatchesApplied"],
        ["plotsActive"] = metadata["aPlots"] ?? new JArray(),
        ["plotsHistorical"] = metadata["aPlotsOld"] ?? new JArray(),
        ["bodies"] = bodyMaps,
        ["companies"] = world["aComps"],
        ["stellarArraysPresent"] = world["aStellarShips"]?.Type == JTokenType.Array || world["aStellarDerelicts"]?.Type == JTokenType.Array,
        ["physical"] = ships,
        ["deferred"] = deferred,
        ["populations"] = populations
    };
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    File.WriteAllText(output, result.ToString(Formatting.Indented));
    Console.WriteLine($"Read-only content audit: {ships.Count} physical hulls, {deferred.Count} deferred objects; archive hash unchanged.");
}
