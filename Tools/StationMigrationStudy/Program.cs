using System.IO.Compression;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Read-only feasibility study. No game assembly, hooks or save writes.
try
{
    if (args.Length < 3 || args.Length > 4)
        throw new ArgumentException("StationMigrationStudy OLD-DATA NEW-DATA REPORT.json [SAVE.zip]");

    string oldData = Path.GetFullPath(args[0]);
    string newData = Path.GetFullPath(args[1]);
    string output = Path.GetFullPath(args[2]);
    string[] templates =
    [
        "Port Mojave", "Port Mojave Xin", "Station_BCRS", "Station_BCRS_Titan",
        "Station_EJDR", "Station_HQCH", "Station_JATL", "Station_JFTS",
        "Station_MLAB", "Station_MSUZ_RB", "Station_SVIR"
    ];
    var overlaysBefore = ReadArray(Path.Combine(oldData, "cooverlays/cooverlays.json"))
        .ToDictionary(item => (string)item["strName"]!, StringComparer.Ordinal);
    var overlaysAfter = ReadArray(Path.Combine(newData, "cooverlays/cooverlays.json"))
        .ToDictionary(item => (string)item["strName"]!, StringComparer.Ordinal);
    var newKiosks = overlaysAfter.Keys.Where(name => name.StartsWith("ItmKiosk", StringComparison.Ordinal) &&
        name.EndsWith("Scrap01", StringComparison.Ordinal) && !overlaysBefore.ContainsKey(name))
        .ToHashSet(StringComparer.Ordinal);
    var defaults = ReadArray(Path.Combine(newData, "star_systems/star_system.json"))
        .Single(item => (string?)item["strName"] == "NewGame");
    var stationTemplates = ((JArray)defaults["aSpawnStations"]!).OfType<JObject>()
        .ToDictionary(item => (string)item["strName"]!, item => (string)item["strShipType"]!, StringComparer.Ordinal);

    var rows = new List<JObject>();
    foreach (string template in templates)
    {
        string oldFile = Path.Combine(oldData, "ships", template + ".json");
        string newFile = Path.Combine(newData, "ships", template + ".json");
        var oldShip = ReadArray(oldFile).Single();
        var newShip = ReadArray(newFile).Single();
        var oldItems = (JArray)oldShip["aItems"]!;
        var newItems = (JArray)newShip["aItems"]!;
        var replacements = new List<JObject>();
        foreach (var kiosk in newItems.OfType<JObject>().Where(item => newKiosks.Contains((string)item["strName"]!)))
        {
            var previous = oldItems.OfType<JObject>().Where(item => RootItem(item) && SamePosition(item, kiosk)).ToArray();
            replacements.Add(new JObject
            {
                ["newDefinition"] = kiosk["strName"],
                ["newKiosk"] = kiosk.DeepClone(),
                ["oldCandidatesAtLocation"] = new JArray(previous.Select(item => item.DeepClone())),
                ["sameTemplateItemId"] = previous.Length == 1 && (string?)previous[0]["strID"] == (string?)kiosk["strID"],
                ["overlay"] = overlaysAfter[(string)kiosk["strName"]!].DeepClone()
            });
        }
        var oldLayout = Layout(oldItems, newKiosks);
        var newLayout = Layout(newItems, newKiosks);
        var layoutChanges = oldLayout.Keys.Union(newLayout.Keys).Order(StringComparer.Ordinal)
            .Where(key => oldLayout.GetValueOrDefault(key) != newLayout.GetValueOrDefault(key))
            .Select(key => new JObject
            {
                ["key"] = key, ["before"] = oldLayout.GetValueOrDefault(key), ["after"] = newLayout.GetValueOrDefault(key)
            });
        rows.Add(new JObject
        {
            ["template"] = template,
            ["oldHash"] = Hash(oldFile), ["newHash"] = Hash(newFile),
            ["oldItemCount"] = oldItems.Count, ["newItemCount"] = newItems.Count,
            ["kiosks"] = new JArray(replacements), ["rootLayoutChanges"] = new JArray(layoutChanges)
        });
    }

    JObject? saved = null;
    if (args.Length == 4)
    {
        string archive = Path.GetFullPath(args[3]);
        if (archive.Equals(output, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Report must not overwrite the save.");
        string beforeHash = Hash(archive);
        using var stream = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var stations = new List<JObject>();
        var gridInputs = new List<GridProjection.Input>();
        foreach (var entry in zip.Entries.Where(entry => entry.FullName.StartsWith("ships/", StringComparison.Ordinal) &&
            entry.FullName.EndsWith(".json", StringComparison.Ordinal)))
        {
            string id = Path.GetFileNameWithoutExtension(entry.FullName);
            if (id is not ("BCER" or "BCRS" or "EJDR" or "HQCH" or "JATL" or "JFTS" or "MLAB" or "MSUZ" or "SVIR"))
                continue;
            using var text = new StreamReader(entry.Open());
            using var reader = new JsonTextReader(text) { DateParseHandling = DateParseHandling.None };
            var token = JToken.ReadFrom(reader);
            var ship = token is JArray array ? (JObject)array.Single() : (JObject)token;
            string savedName = (string)ship["strName"]!;
            string resolvedTemplate = stationTemplates.GetValueOrDefault(savedName, savedName);
            var row = rows.SingleOrDefault(row => (string?)row["template"] == resolvedTemplate);
            if (id is "BCER" or "BCRS")
                gridInputs.Add(new GridProjection.Input(id,
                    (JObject)ReadArray(Path.Combine(oldData, "ships", resolvedTemplate + ".json")).Single(),
                    (JObject)ReadArray(Path.Combine(newData, "ships", resolvedTemplate + ".json")).Single(), ship));
            var probes = new List<JObject>();
            foreach (var target in (JArray?)row?["kiosks"] ?? new JArray())
            {
                var kiosk = (JObject)target["newKiosk"]!;
                var matches = ((JArray)ship["aItems"]!).OfType<JObject>().Where(item => RootItem(item) && SamePosition(item, kiosk));
                probes.Add(new JObject { ["intendedDefinition"] = kiosk["strName"], ["existingAtLocation"] = new JArray(matches.Select(item => item.DeepClone())) });
            }
            stations.Add(new JObject
            {
                ["id"] = id, ["savedName"] = savedName, ["resolvedTemplate"] = resolvedTemplate,
                ["savedTemplateName"] = ship["strTemplateName"],
                ["hasReviewedKioskPlacement"] = row != null,
                ["itemCount"] = ((JArray)ship["aItems"]!).Count,
                ["shipConditions"] = ship["shipCO"]?["aConds"]?.DeepClone(),
                ["kioskProbes"] = new JArray(probes)
            });
        }
        var rootEntry = zip.Entries.Where(entry => !entry.FullName.StartsWith("ships/", StringComparison.Ordinal) &&
            entry.FullName.EndsWith(".json", StringComparison.Ordinal)).OrderByDescending(entry => entry.Length).First();
        var metadata = new JObject();
        var wantedIds = gridInputs.SelectMany(input => ((JArray)input.Saved["aItems"]!).OfType<JObject>())
            .Select(item => (string)item["strID"]!).ToHashSet(StringComparer.Ordinal);
        var savedStates = new Dictionary<string, JObject>(StringComparer.Ordinal);
        using (var text = new StreamReader(rootEntry.Open()))
        using (var reader = new JsonTextReader(text) { DateParseHandling = DateParseHandling.None })
        {
            while (reader.Read())
            {
                if (reader.TokenType != JsonToken.PropertyName || reader.Depth != 2)
                    continue;
                string name = (string)reader.Value!;
                if (!reader.Read())
                    throw new InvalidDataException("Missing saved root property value.");
                if (name is "strVersion" or "aPatchesApplied")
                    metadata[name] = JToken.ReadFrom(reader);
                else if (name == "aCOs")
                    ReadObjectStates(reader, wantedIds, savedStates);
                else
                    reader.Skip();
            }
        }
        string afterHash = Hash(archive);
        if (beforeHash != afterHash)
            throw new IOException("Save changed while being inspected; refusing a mixed report.");
        var projection = new GridProjection(newData, savedStates).Run(gridInputs);
        saved = new JObject
        {
            ["archive"] = archive, ["sha256"] = beforeHash, ["metadata"] = metadata,
            ["stations"] = new JArray(stations), ["gridProjection"] = projection
        };
    }

    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    var report = new JObject
    {
        ["scope"] = "Read-only template/local-placement feasibility. Root item counts are a spatial multiset, not a full native world simulation.",
        ["newDefinitions"] = new JArray(newKiosks.Order(StringComparer.Ordinal)),
        ["templates"] = new JArray(rows), ["savedWorld"] = saved
    };
    File.WriteAllText(output, report.ToString(Formatting.Indented) + "\n");
    Console.WriteLine($"Read {rows.Count} templates, {newKiosks.Count} new kiosk definitions; inspected {saved?["stations"]?.Count() ?? 0} saved stations. Report: {output}");
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error);
    return 1;
}

static JArray ReadArray(string path) => JArray.Parse(File.ReadAllText(path));
static string Hash(string path)
{
    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    return Convert.ToHexString(SHA256.HashData(stream));
}
static bool RootItem(JToken item)
{
    var parent = item["strParentID"];
    var slotParent = item["strSlotParentID"];
    return (parent == null || parent.Type == JTokenType.Null) &&
        (slotParent == null || slotParent.Type == JTokenType.Null);
}
static bool SamePosition(JToken a, JToken b) => Math.Abs((double)a["fX"]! - (double)b["fX"]!) < .001 &&
    Math.Abs((double)a["fY"]! - (double)b["fY"]!) < .001;
static Dictionary<string, int> Layout(JArray items, HashSet<string> kioskNames) => items.OfType<JObject>()
    .Where(RootItem).GroupBy(item => string.Join("|", kioskNames.Contains((string)item["strName"]!) ? "NEW-SCRAP-KIOSK" : (string)item["strName"]!,
        item["fX"]?.ToString(Formatting.None), item["fY"]?.ToString(Formatting.None), item["fRotation"]?.ToString(Formatting.None)))
    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

static void ReadObjectStates(JsonTextReader reader, HashSet<string> wanted, Dictionary<string, JObject> output)
{
    if (reader.TokenType != JsonToken.StartArray)
        throw new InvalidDataException("Saved CO table is not an array.");
    while (reader.Read() && reader.TokenType != JsonToken.EndArray)
    {
        if (reader.TokenType != JsonToken.StartObject)
            throw new InvalidDataException("Invalid saved CO entry.");
        int depth = reader.Depth;
        var fields = new JObject();
        string? id = null;
        while (reader.Read() && !(reader.TokenType == JsonToken.EndObject && reader.Depth == depth))
        {
            if (reader.TokenType != JsonToken.PropertyName || reader.Depth != depth + 1)
                continue;
            string name = (string)reader.Value!;
            if (!reader.Read())
                throw new InvalidDataException("Missing saved CO field value.");
            if (name == "strID")
            {
                id = (string?)reader.Value;
                fields[name] = id;
            }
            else if ((name is "strCODef" or "aConds") && (id == null || wanted.Contains(id)))
                fields[name] = JToken.ReadFrom(reader);
            else
                reader.Skip();
        }
        if (id != null && wanted.Contains(id))
            output[id] = fields;
    }
}
