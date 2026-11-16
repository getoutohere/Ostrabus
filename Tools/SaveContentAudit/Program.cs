using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

try
{
    Run(args);
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine("Content audit failed: " + error.Message);
    return 1;
}

static void Run(string[] args)
{
    if (args.Length != 2)
        throw new ArgumentException("Usage: SaveContentAudit <save.zip> <output.json>");
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
            if (name is "strRegID" or "strName" or "strTemplateName" or "publicName" or "shipCO" or "objSS" or "DMGStatus" or "ShipType" or "fFirstVisit" or "aDocked" or "bAIShip")
                kept[name] = JToken.ReadFrom(reader);
            else
                reader.Skip();
        }

        string id = (string? )kept["strRegID"] ?? throw new InvalidDataException("Missing ship ID.");
        var co = kept["shipCO"];
        var situation = kept["objSS"];
        ships.Add(new JObject { ["id"] = id, ["template"] = kept["strTemplateName"] ?? kept["strName"], ["name"] = kept["publicName"], ["owner"] = owners.TryGetValue(id, out var owner) ? owner : null, ["damage"] = kept["DMGStatus"], ["classification"] = kept["ShipType"], ["firstVisit"] = kept["fFirstVisit"], ["ai"] = kept["bAIShip"], ["dockingLinks"] = (kept["aDocked"] as JObject)?.Count ?? 0, ["parent"] = situation?["boPORShip"], ["region"] = situation?["bIsRegion"] ?? false, ["bodyLocked"] = situation?["bBOLocked"] ?? false, ["mapID"] = Condition(co, "EnvironmentMapID"), ["environment"] = Condition(co, "IsEnvironmentEntity") > 0, ["legacyDerelict"] = Condition(co, "IsDerelict") > 0, ["orbitRate"] = situation?["fOrbitW"] ?? 0 });
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
    var bodyMaps = new JArray((world["aBOs"] as JArray ?? new JArray()).Select(body => new JObject { ["id"] = body["strName"], ["mapID"] = body["nEnvironmentMapID"] ?? 0 }));
    var populations = new JArray(deferred.Concat(ships.Where(ship => (bool? )ship["environment"] == true && (double? )ship["mapID"] > 0)).GroupBy(item => (int? )item["mapID"] ?? 0).OrderBy(group => group.Key).Select(group => new JObject { ["mapID"] = group.Key, ["deferred"] = group.Count(item => item["wreck"] != null), ["physical"] = group.Count(item => item["wreck"] == null), ["deferredWrecks"] = group.Count(item => (bool? )item["wreck"] == true) }));
    if (Hash() != before)
        throw new IOException("Source archive changed during read; retry when saving finishes.");
    var result = new JObject
    {
        ["archiveSHA256"] = before,
        ["version"] = metadata["strVersion"],
        ["patches"] = metadata["aPatchesApplied"],
        ["plotsActive"] = metadata["aPlots"] ?? new JArray(),
        ["plotsHistorical"] = metadata["aPlotsOld"] ?? new JArray(),
        ["bodies"] = bodyMaps,
        ["stellarArraysPresent"] = world["aStellarShips"]?.Type == JTokenType.Array || world["aStellarDerelicts"]?.Type == JTokenType.Array,
        ["physical"] = ships,
        ["deferred"] = deferred,
        ["populations"] = populations
    };
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    File.WriteAllText(output, result.ToString(Formatting.Indented));
    Console.WriteLine($"Read-only content audit: {ships.Count} physical hulls, {deferred.Count} deferred objects; archive hash unchanged.");
}
