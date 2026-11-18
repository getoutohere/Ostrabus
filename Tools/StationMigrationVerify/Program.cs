using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

try
{
    if (args.Length != 4) throw new ArgumentException("BASELINE.zip MIGRATED.zip MANIFEST.json REPORT.json");
    var manifests = (JArray)JObject.Parse(File.ReadAllText(args[2]))["templates"]!;
    var ids = manifests.OfType<JObject>().Select(row => (string)row["station"]!).ToHashSet(StringComparer.Ordinal);
    var baseline = Snapshot.Read(args[0], ids);
    var migrated = Snapshot.Read(args[1], ids);
    var rows = new JArray();
    foreach (string id in ids.Order(StringComparer.Ordinal))
    {
        if (!baseline.Stations.TryGetValue(id, out var before) || !migrated.Stations.TryGetValue(id, out var after))
            throw new InvalidDataException("Missing baseline or migrated physical section: " + id);
        var oldItems = Snapshot.Items(before);
        var newItems = Snapshot.Items(after);
        var lost = oldItems.Keys.Except(newItems.Keys).Select(key => oldItems[key]).ToArray();
        var added = newItems.Keys.Except(oldItems.Keys).Select(key => newItems[key]).ToArray();
        var changes = new JArray();
        int linkedChildren = 0, inventoryRecords = 0;
        var inventoryFailures = new JArray();
        var settingsDifferences = new JArray();
        foreach (var pair in oldItems.Where(pair => newItems.ContainsKey(pair.Key)))
        {
            var next = newItems[pair.Key];
            var fields = new JArray();
            foreach (string field in new[] { "strName", "strParentID", "strSlotParentID" })
                if (((string?)pair.Value[field] ?? "") != ((string?)next[field] ?? "")) fields.Add(field);
            foreach (string field in new[] { "fX", "fY", "fRotation" })
                if (Math.Abs(((double?)pair.Value[field] ?? 0) - ((double?)next[field] ?? 0)) > .001) fields.Add(field);
            if (fields.Count > 0)
                changes.Add(new JObject { ["id"] = pair.Key, ["before"] = Compact(pair.Value), ["after"] = Compact(next), ["fields"] = fields });
            foreach (string field in new[] { "aGPMSettings", "aCondOverrides" })
                if (!Equivalent(pair.Value[field], next[field]))
                    settingsDifferences.Add(new JObject { ["id"] = pair.Key, ["definition"] = next["strName"], ["field"] = field,
                        ["before"] = pair.Value[field]?.DeepClone(), ["after"] = next[field]?.DeepClone() });
            if (!string.IsNullOrEmpty((string?)pair.Value["strParentID"]) || !string.IsNullOrEmpty((string?)pair.Value["strSlotParentID"])) linkedChildren++;
            var oldState = baseline.Record(before, pair.Key);
            var newState = migrated.Record(after, pair.Key);
            if (oldState == null) continue;
            if (newState == null)
            {
                inventoryFailures.Add(new JObject { ["id"] = pair.Key, ["field"] = "missing-saved-CO" });
                continue;
            }
            foreach (string field in new[] { "aLot", "aStack", "aMyShips", "aFactions", "strPersistentCO", "strPersistentCT", "strSlotName", "inventoryX", "inventoryY" })
                if (!Equivalent(oldState[field], newState[field]))
                    inventoryFailures.Add(new JObject { ["id"] = pair.Key, ["field"] = field,
                        ["before"] = oldState[field]?.DeepClone(), ["after"] = newState[field]?.DeepClone() });
            inventoryRecords++;
        }
        string[] wanted = manifests.OfType<JObject>().Where(row => (string)row["station"]! == id && row["kiosk"] is JObject)
            .Select(row => (string)row["kiosk"]!["strName"]!).Distinct().ToArray();
        var kiosks = new JArray();
        foreach (var item in newItems.Values.Where(item => wanted.Contains((string)item["strName"]!)))
        {
            var state = migrated.Record(after, (string)item["strID"]!);
            kiosks.Add(new JObject { ["id"] = item["strID"], ["definition"] = item["strName"],
                ["x"] = item["fX"], ["y"] = item["fY"], ["savedDefinition"] = state?["strCODef"],
                ["lastUpdate"] = state?["fLastICOUpdate"] });
        }
        rows.Add(new JObject
        {
            ["station"] = id, ["beforeItems"] = oldItems.Count, ["afterItems"] = newItems.Count,
            ["retainedIds"] = oldItems.Keys.Intersect(newItems.Keys).Count(),
            ["lost"] = new JArray(lost.Select(Compact)), ["added"] = new JArray(added.Select(Compact)),
            ["changed"] = changes, ["nestedItemsChecked"] = linkedChildren,
            ["inventoryRecordsChecked"] = inventoryRecords, ["inventoryDifferences"] = inventoryFailures,
            ["settingsDifferences"] = settingsDifferences,
            ["nativeStationFlag"] = Conditions(after["shipCO"]).Contains("IsDebug1-1-0-1Fixed"),
            ["wantedKioskCount"] = wanted.Length == 0 ? 0 : 1, ["kiosks"] = kiosks,
            ["dockingUnchanged"] = Equivalent(before["aDocked"], after["aDocked"]),
            ["towBracesUnchanged"] = Equivalent(before["aSecuredTowBraces"], after["aSecuredTowBraces"])
        });
    }
    baseline.VerifyHash();
    migrated.VerifyHash();
    var report = new JObject
    {
        ["scope"] = "Read-only saved station identity/layout and inventory-link comparison. Does not execute native scene or trade UI.",
        ["baseline"] = baseline.Path, ["baselineSHA256"] = baseline.Hash,
        ["migrated"] = migrated.Path, ["migratedSHA256"] = migrated.Hash,
        ["baselineEpoch"] = baseline.Epoch, ["migratedEpoch"] = migrated.Epoch,
        ["globalMigrationMarker"] = migrated.Markers.Contains("1.1.0.1_Scrap_Kiosk_Fix"),
        ["stations"] = rows
    };
    string output = System.IO.Path.GetFullPath(args[3]);
    if (output.Equals(baseline.Path, StringComparison.OrdinalIgnoreCase) || output.Equals(migrated.Path, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Report must not overwrite a source archive.");
    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(output)!);
    File.WriteAllText(output, report.ToString(Formatting.Indented) + "\n");
    foreach (var row in rows)
        Console.WriteLine($"{row["station"]}: {row["retainedIds"]} retained; {row["lost"]!.Count()} lost; {row["added"]!.Count()} added; " +
            $"{row["changed"]!.Count()} changed; {row["inventoryDifferences"]!.Count()} inventory differences; flag={row["nativeStationFlag"]}; kiosks={row["kiosks"]!.Count()}");
    Console.WriteLine($"Read-only comparison complete. Epoch {baseline.Epoch} -> {migrated.Epoch}; global marker={report["globalMigrationMarker"]}. {output}");
    return 0;
}
catch (Exception error) { Console.Error.WriteLine(error); return 1; }

static JObject Compact(JObject item)
{
    var result = new JObject();
    foreach (string field in new[] { "strID", "strName", "fX", "fY", "fRotation", "strParentID", "strSlotParentID" })
        if (item[field] != null) result[field] = item[field]!.DeepClone();
    return result;
}
static bool Equivalent(JToken? a, JToken? b)
{
    bool Empty(JToken? token) => token == null || token.Type == JTokenType.Null ||
        token is JContainer container && container.Count == 0 || token.Type == JTokenType.String && token.Value<string>() == "";
    return Empty(a) && Empty(b) || JToken.DeepEquals(a, b);
}
static HashSet<string> Conditions(JToken? state)
{
    var names = new HashSet<string>(StringComparer.Ordinal);
    foreach (string? value in (state?["aConds"] as JArray ?? new JArray()).Values<string>())
    {
        if (string.IsNullOrEmpty(value)) continue;
        int split = value.IndexOf('=');
        if (split < 0) { names.Add(value); continue; }
        int amountStart = value.LastIndexOf('x') + 1;
        if (amountStart <= split || !double.TryParse(value[amountStart..], NumberStyles.Float, CultureInfo.InvariantCulture, out double amount))
            throw new InvalidDataException("Unsupported saved condition: " + value);
        if (amount > 0) names.Add(value[..split]);
    }
    return names;
}

sealed class Snapshot
{
    internal string Path = "", Hash = "";
    internal double Epoch;
    internal string[] Markers = [];
    internal Dictionary<string, JObject> Stations = new(StringComparer.Ordinal);
    readonly Dictionary<string, JObject> records = new(StringComparer.Ordinal);
    readonly Dictionary<string, Dictionary<string, JObject>> localRecords = new(StringComparer.Ordinal);
    internal static Dictionary<string, JObject> Items(JObject station) => ((JArray)station["aItems"]!).OfType<JObject>()
        .ToDictionary(row => (string)row["strID"]!, StringComparer.Ordinal);
    internal JObject? Record(JObject station, string id)
    {
        var local = localRecords[(string)station["strRegID"]!];
        return local.GetValueOrDefault(id) ?? records.GetValueOrDefault(id);
    }
    static string HashFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
    internal void VerifyHash()
    {
        if (HashFile(Path) != Hash) throw new IOException("Source changed during inspection: " + Path);
    }
    internal static Snapshot Read(string path, HashSet<string> wantedStations)
    {
        var result = new Snapshot { Path = System.IO.Path.GetFullPath(path), Hash = HashFile(path) };
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var zip = new ZipArchive(input, ZipArchiveMode.Read);
        foreach (var entry in zip.Entries.Where(entry => wantedStations.Any(id => entry.FullName == "ships/" + id + ".json")))
        {
            using var text = new StreamReader(entry.Open());
            var station = (JObject)JArray.Parse(text.ReadToEnd()).Single();
            string id = (string)station["strRegID"]!;
            result.Stations[id] = station;
            result.localRecords[id] = (station["aCOs"] as JArray ?? new JArray()).OfType<JObject>()
                .ToDictionary(row => (string)row["strID"]!, StringComparer.Ordinal);
        }
        var wanted = result.Stations.Values.SelectMany(station => Items(station).Keys).ToHashSet(StringComparer.Ordinal);
        var root = zip.Entries.Where(entry => !entry.FullName.StartsWith("ships/", StringComparison.Ordinal) && entry.FullName.EndsWith(".json", StringComparison.Ordinal))
            .OrderByDescending(entry => entry.Length).First();
        using var rootText = new StreamReader(root.Open());
        using var reader = new JsonTextReader(rootText) { DateParseHandling = DateParseHandling.None };
        while (reader.Read())
        {
            if (reader.TokenType != JsonToken.PropertyName || reader.Depth != 2) continue;
            string field = (string)reader.Value!;
            reader.Read();
            if (field == "aPatchesApplied") result.Markers = JArray.Load(reader).Values<string>().Select(value => value!).ToArray();
            else if (field == "objSystem")
            {
                int depth = reader.Depth;
                while (reader.Read() && !(reader.TokenType == JsonToken.EndObject && reader.Depth == depth))
                {
                    if (reader.TokenType != JsonToken.PropertyName || reader.Depth != depth + 1) continue;
                    string name = (string)reader.Value!;
                    reader.Read();
                    if (name == "dfEpoch") result.Epoch = Convert.ToDouble(reader.Value, CultureInfo.InvariantCulture);
                    else reader.Skip();
                }
            }
            else if (field == "aCOs")
            {
                while (reader.Read() && reader.TokenType != JsonToken.EndArray)
                {
                    int depth = reader.Depth;
                    string? id = null;
                    var kept = new JObject();
                    while (reader.Read() && !(reader.TokenType == JsonToken.EndObject && reader.Depth == depth))
                    {
                        if (reader.TokenType != JsonToken.PropertyName || reader.Depth != depth + 1) continue;
                        string name = (string)reader.Value!;
                        reader.Read();
                        if (name == "strID") { id = (string?)reader.Value; kept[name] = id; }
                        else if (id == null || wanted.Contains(id)) kept[name] = JToken.ReadFrom(reader);
                        else reader.Skip();
                    }
                    if (id != null && wanted.Contains(id)) result.records[id] = kept;
                }
            }
            else reader.Skip();
        }
        result.VerifyHash();
        return result;
    }
}
