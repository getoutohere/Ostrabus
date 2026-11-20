using System.IO.Compression;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

try
{
    if (args.Length == 1 && args[0] == "--self-test")
    {
        StudyChecks.Run();
        return 0;
    }
    if (args.Length is not (2 or 3))
        throw new ArgumentException("Usage: SaveBloatStudy <save.zip> <report.json> [<game-data-directory>] | --self-test");
    string source = Path.GetFullPath(args[0]);
    string output = Path.GetFullPath(args[1]);
    if (source.Equals(output, StringComparison.OrdinalIgnoreCase))
        throw new ArgumentException("The report must not overwrite the source archive.");
    var result = new Study(args.Length == 3 ? new TrainingBaseline(args[2]) : null).Read(source);
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    File.WriteAllText(output, result.ToString(Formatting.Indented));
    Console.WriteLine($"Read-only study complete: {result["objects"]!["records"]} CO records; source hash unchanged.");
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine("Save bloat study failed: " + error);
    return 1;
}

internal sealed class Study(TrainingBaseline? training = null)
{
    private sealed record Owner(string Id, string Definition, string Entry, string? LastShip,
        bool Alive, bool Person, long Characters, string[] Children, int Messages, int Relationships,
        int PersonSpecs, int HistoryGroups, int HistoryInteractions);
    private sealed record LargeField(string id, string entry, string field, long compactCharacters);
    private sealed record LargeSocial(string id, string entry, int relationships, int specs, bool alive);
    private sealed record MissingParent(string Entry, string Item, string Parent);

    private readonly Dictionary<string, Owner> owners = new(StringComparer.Ordinal);
    private readonly List<object> duplicateOwners = new();
    private readonly HashSet<string> anchors = new(StringComparer.Ordinal);
    private readonly HashSet<string> ships = new(StringComparer.Ordinal);
    private readonly HashSet<string> deferred = new(StringComparer.Ordinal);
    private readonly HashSet<string> externalStrings = new(StringComparer.Ordinal);
    private readonly List<object> fields = new();
    private readonly List<JObject> markets = new();
    private readonly Dictionary<string, long> coFieldCharacters = new(StringComparer.Ordinal);
    private readonly List<LargeField> largestFields = new();
    private readonly List<LargeSocial> socialLargest = new();
    private readonly List<object> layoutDependencies = new();
    private readonly List<MissingParent> missingParents = new();
    private double? epoch;
    private string? version;
    private string? playerId;
    private long records;
    private long recordCharacters;
    private long deadRecordCharacters;
    private long messageCount;
    private long relationshipCount;
    private long personSpecCount;
    private long historyCount;
    private long historyInteractionCount;
    private long zeroIterationHistory;
    private long zeroTotalHistory;
    private long emptyScoreMaps;
    private long deadHistoryCharacters;
    private readonly Dictionary<string, long> historyNames = new(StringComparer.Ordinal);
    private long matchingTrainingEntries;
    private long matchingTrainingCharacters;

    public JObject Read(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        string before = Hash(file);
        using var zip = new ZipArchive(file, ZipArchiveMode.Read, true);
        if (zip.Entries.GroupBy(e => e.FullName, StringComparer.Ordinal).Any(g => g.Count() > 1))
            throw new InvalidDataException("Duplicate ZIP entry names.");
        var root = zip.Entries.Where(e => !e.FullName.Contains('/') && e.FullName.EndsWith(".json", StringComparison.Ordinal)
            && e.FullName != "saveInfo.json").OrderByDescending(e => e.Length).First();
        foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith("ships/", StringComparison.Ordinal)
            && e.FullName.EndsWith(".json", StringComparison.Ordinal)))
            ReadEntry(entry, false);
        ReadEntry(root, true);

        var reachable = new HashSet<string>(anchors, StringComparer.Ordinal);
        var queue = new Queue<string>(anchors);
        while (queue.TryDequeue(out string? id))
            if (owners.TryGetValue(id, out var owner))
                foreach (string child in owner.Children)
                    if (reachable.Add(child)) queue.Enqueue(child);

        var unanchored = owners.Values.Where(x => !reachable.Contains(x.Id)).ToArray();
        string[] missingState = anchors.Where(id => !owners.ContainsKey(id) && !id.Contains("MP|", StringComparison.Ordinal)).ToArray();
        if (epoch == null || playerId == null)
            throw new InvalidDataException("Missing world/player metadata; the selected root entry is not a supported native world save.");
        foreach (var market in markets)
        {
            string id = (string)market["id"]!;
            market["physicalHullPresent"] = ships.Contains(id);
            market["deferredWorldObjectPresent"] = deferred.Contains(id);
        }
        if (before != Hash(file)) throw new IOException("Source archive changed during inspection; discard this result.");
        return JObject.FromObject(new
        {
            archiveSHA256 = before,
            archiveUnchanged = true,
            sourceFileName = Path.GetFileName(path),
            epoch, version, playerId,
            savedVersionNote = "strVersion is the original save version, not necessarily the executable that last saved it.",
            archive = new
            {
                bytes = file.Length,
                entries = zip.Entries.Count,
                uncompressedBytes = zip.Entries.Sum(e => e.Length),
                groups = zip.Entries.GroupBy(EntryGroup).Select(g => new
                {
                    kind = g.Key, count = g.Count(), compressedBytes = g.Sum(e => e.CompressedLength),
                    uncompressedBytes = g.Sum(e => e.Length)
                }).OrderByDescending(g => g.uncompressedBytes),
                largestEntries = zip.Entries.OrderByDescending(e => e.Length).Take(20).Select(e => new
                {
                    name = e.FullName, uncompressedBytes = e.Length, compressedBytes = e.CompressedLength
                })
            },
            physicalShips = ships.Count,
            deferredWorldObjects = deferred.Count,
            objects = new
            {
                records, uniqueIds = owners.Count, duplicateOwners,
                rawRecordCharacters = recordCharacters,
                deadRecords = owners.Values.Count(x => !x.Alive), deadRecordCharacters,
                persons = owners.Values.Count(x => x.Person),
                deadPersons = owners.Values.Count(x => x.Person && !x.Alive),
                messages = messageCount, relationships = relationshipCount, personSpecCopies = personSpecCount,
                historyGroups = historyCount, historyInteractions = historyInteractionCount,
                historyReview = new
                {
                    zeroIterationHistory, zeroTotalHistory, emptyScoreMaps, deadHistoryCompactCharacters = deadHistoryCharacters,
                    assetTrainingComparisonPerformed = training != null,
                    assetTrainingSources = training?.Sources,
                    matchingTrainingEntries, matchingTrainingCompactCharacters = matchingTrainingCharacters,
                    mostRepeatedNames = historyNames.OrderByDescending(x => x.Value).Take(20),
                    note = "Training and learned preferences use the same history format. Zero totals/iterations or empty score maps alone do not prove a record is removable."
                },
                byEntry = owners.Values.GroupBy(x => x.Entry).Select(g => new
                {
                    entry = g.Key, count = g.Count(), rawCharacters = g.Sum(x => x.Characters),
                    dead = g.Count(x => !x.Alive), persons = g.Count(x => x.Person)
                }).OrderByDescending(x => x.rawCharacters),
                byDefinition = owners.Values.GroupBy(x => x.Definition).Select(g => new
                {
                    definition = g.Key, count = g.Count(), rawCharacters = g.Sum(x => x.Characters)
                }).OrderByDescending(x => x.rawCharacters).Take(30),
                largest = owners.Values.OrderByDescending(x => x.Characters).Take(25).Select(Describe),
                fieldCompactCharacters = coFieldCharacters.OrderByDescending(x => x.Value),
                largestSingleFields = largestFields.OrderByDescending(x => x.compactCharacters).Take(20),
                largestSocials = socialLargest.OrderByDescending(x => x.relationships).Take(20)
            },
            inventoryReachability = new
            {
                anchorIds = anchors.Count, reachableIds = reachable.Count,
                unanchoredRecords = unanchored.Length,
                unanchoredRawCharacters = unanchored.Sum(x => x.Characters),
                unanchoredWithOtherExactStringReferences = unanchored.Count(x => externalStrings.Contains(x.Id)),
                layoutIdsWithoutExplicitState = missingState.Length,
                layoutIdsWithoutExplicitStateExamples = missingState.Take(30),
                byDefinition = unanchored.GroupBy(x => x.Definition).Select(g => new
                {
                    definition = g.Key, count = g.Count(), rawCharacters = g.Sum(x => x.Characters)
                }).OrderByDescending(x => x.rawCharacters).Take(30),
                largest = unanchored.OrderByDescending(x => x.Characters).Take(50).Select(x => new
                {
                    record = Describe(x), lastShipExists = x.LastShip != null && ships.Contains(x.LastShip),
                    otherExactStringReference = externalStrings.Contains(x.Id)
                }),
                note = "Anchors are saved JsonItems, crew entries, aShallowPSpecs spawner entries and ship COs; traversal follows aStack/aLot. Unanchored is a review candidate, NOT proof that an object is irrelevant. Indirect asset/condition references and future native queries are not exhaustively modeled."
            },
            fields,
            layoutDependencies,
            missingParentReview = missingParents.GroupBy(x => new { x.Entry, x.Parent }).Select(g => new
            {
                entry = g.Key.Entry, parent = g.Key.Parent, children = g.Count(),
                parentHasSavedState = owners.ContainsKey(g.Key.Parent),
                parentAppearsInAnyLayout = anchors.Contains(g.Key.Parent),
                childStateCharacters = g.Sum(x => owners.TryGetValue(x.Item, out var state) ? state.Characters : 0),
                childExamples = g.Take(10).Select(x => x.Item)
            }),
            markets,
            notes = new[]
            {
                "Read-only offline analysis; no game assembly execution, no save rewriting, no deletion recommendations derived solely from age/death/absence.",
                "Raw characters are UTF-16 character spans in native JSON; they are not compressed ZIP bytes or exact UTF-8 byte counts. Field spans include spacing after the property colon.",
                "CO field character totals use compact JSON serialization and are comparable estimates, not raw native spans. Archive entry sizes are exact ZIP metadata.",
                "Social person-spec counts include repeated embedded copies; death does not make a relationship or history irrelevant.",
                "Absent-hull markets can retain undelivered cargo and must not be treated as disposable solely on that basis."
            }
        });
    }

    private static object Describe(Owner x) => new
    {
        x.Id, x.Definition, x.Entry, x.LastShip, x.Alive, x.Person, rawCharacters = x.Characters,
        x.Messages, x.Relationships, x.PersonSpecs, x.HistoryGroups, x.HistoryInteractions
    };

    private static string Hash(Stream input)
    {
        input.Position = 0;
        string hash = Convert.ToHexString(SHA256.HashData(input));
        input.Position = 0;
        return hash;
    }

    private static string EntryGroup(ZipArchiveEntry entry) =>
        entry.FullName.StartsWith("ships/", StringComparison.Ordinal)
            ? entry.FullName.EndsWith(".json", StringComparison.Ordinal) ? "ship-json" : "ship-images-and-other"
            : entry.FullName.EndsWith(".json", StringComparison.Ordinal) ? "root-json" : "other";

    private void ReadEntry(ZipArchiveEntry entry, bool root)
    {
        var parents = new Dictionary<string, string?>(StringComparer.Ordinal);
        using var text = new PositionTextReader(new StreamReader(entry.Open()));
        using var json = new JsonTextReader(text) { DateParseHandling = DateParseHandling.None };
        if (!json.Read()) throw new InvalidDataException("Empty JSON entry: " + entry.FullName);
        int depth = json.TokenType == JsonToken.StartArray ? 2 : 1;
        while (json.Read())
        {
            if (json.TokenType != JsonToken.PropertyName || json.Depth != depth) continue;
            string field = (string)json.Value!;
            long start = text.Position(json);
            if (!json.Read()) throw new InvalidDataException("Missing value: " + field);
            int? count = null;
            if (field == "aCOs" && json.TokenType == JsonToken.StartArray)
            {
                count = ReadArray(json, value => ReadOwner(value, entry.FullName, false), text);
            }
            else if (!root && field == "shipCO" && json.TokenType == JsonToken.StartObject)
            {
                long recordStart = text.Position(json) - 1;
                var value = JObject.Load(json);
                AddOwner(value, entry.FullName, text.Position(json) - recordStart, true);
            }
            else if (!root && field is "aItems" or "aCrew" or "aShallowPSpecs" && json.TokenType == JsonToken.StartArray)
            {
                count = ReadArray(json, value =>
                {
                    string? id = (string?)value["strID"];
                    if (!string.IsNullOrEmpty(id)) anchors.Add(id);
                    if (!string.IsNullOrEmpty(id)) parents.TryAdd(id, (string?)value["strParentID"] ?? (string?)value["strSlotParentID"]);
                    RememberStrings(value);
                }, text);
            }
            else
            {
                JToken value = JToken.ReadFrom(json);
                if (value is JArray array) count = array.Count;
                if (root && field == "strVersion") version = (string?)value;
                if (root && field == "strPlayerCO") playerId = (string?)value;
                if (!root && field == "strRegID" && value.Type == JTokenType.String) ships.Add((string)value!);
                if (root && field == "objSystem")
                {
                    epoch = (double?)value["dfEpoch"];
                    foreach (string name in new[] { "aStellarShips", "aStellarDerelicts" })
                        foreach (var item in value[name] as JArray ?? new JArray())
                            if ((string?)item["strID"] is string id) deferred.Add(id);
                }
                if (root && field == "objMarketSave") ReadMarkets(value);
                RememberStrings(value);
            }
            fields.Add(new { entry = entry.FullName, field, rawCharacters = text.Position(json) - start, count });

            void ReadOwner(JObject value, string name, bool anchor) =>
                AddOwner(value, name, text.LastRecordCharacters, anchor);
        }
        if (!root) ReadParentDependencies(entry.FullName, parents);
    }

    private void ReadParentDependencies(string entry, Dictionary<string, string?> parents)
    {
        // Structural reachability only: damage, slots and native mode switches
        // belong to the existing InventoryLoadAudit/native loader checks.
        var status = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (string id in parents.Keys)
        {
            var path = new HashSet<string>(StringComparer.Ordinal);
            string? current = id;
            while (current != null && parents.ContainsKey(current) && !status.ContainsKey(current) && path.Add(current))
                current = parents[current];
            bool reachesRoot = current == null || status.GetValueOrDefault(current);
            foreach (string visited in path) status[visited] = reachesRoot;
        }
        var missing = parents.Where(p => p.Value != null && !parents.ContainsKey(p.Value)).ToArray();
        missingParents.AddRange(missing.Select(p => new MissingParent(entry, p.Key, p.Value!)));
        layoutDependencies.Add(new
        {
            entry, items = parents.Count, directMissingParents = missing.Length,
            unreachableFromParentlessItem = status.Count(p => !p.Value),
            missingParentExamples = missing.Take(10),
            note = "Same-hull layout graph only. Missing/cyclic parents require native and cross-hull reference review; this is not a deletion list."
        });
    }

    private static int ReadArray(JsonTextReader json, Action<JObject> consume, PositionTextReader text)
    {
        int count = 0;
        while (json.Read() && json.TokenType != JsonToken.EndArray)
        {
            if (json.TokenType != JsonToken.StartObject) throw new InvalidDataException("Expected saved record object.");
            long start = text.Position(json) - 1;
            var item = JObject.Load(json);
            text.LastRecordCharacters = text.Position(json) - start;
            consume(item);
            count++;
        }
        return count;
    }

    private void AddOwner(JObject value, string entry, long characters, bool anchor)
    {
        string id = (string?)value["strID"] ?? throw new InvalidDataException("CO without ID.");
        string definition = (string?)value["strCODef"] ?? "";
        bool person = value["social"] is JObject || value["strBodyType"]?.Type == JTokenType.String;
        int messages = (value["aMessages2"] as JArray)?.Count ?? (value["aMessages"] as JArray)?.Count ?? 0;
        int relationships = (value["social"]?["aRelationships2"] as JArray)?.Count ?? 0;
        int specs = (value["social"]?["aPSpecsValues"] as JArray)?.Count ?? 0;
        int histories = (value["mapIAHist2"] as JArray)?.Count ?? 0;
        int interactions = value["mapIAHist2"]?.Sum(x => (x["mapInteractions"] as JArray)?.Count ?? 0) ?? 0;
        foreach (var group in value["mapIAHist2"] as JArray ?? new JArray())
            foreach (var interaction in group["mapInteractions"] as JArray ?? new JArray())
            {
                string[] parts = ((string?)interaction["strValues"] ?? "").Split('|');
                if (parts.Length != 3) continue;
                if (parts[1] == "0") zeroIterationHistory++;
                if (float.TryParse(parts[2], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float score) && score == 0) zeroTotalHistory++;
                if ((interaction["mapScores"] as JArray)?.Count is null or 0) emptyScoreMaps++;
                if (training?.Matches((string?)group["strCondName"] ?? "", parts, interaction) == true)
                {
                    matchingTrainingEntries++;
                    matchingTrainingCharacters += interaction.ToString(Formatting.None).Length;
                }
                historyNames[parts[0]] = historyNames.GetValueOrDefault(parts[0]) + 1;
            }
        var children = new[] { "aStack", "aLot" }.SelectMany(name =>
            (value[name] as JArray ?? new JArray()).Values<string>().OfType<string>()).ToArray();
        var owner = new Owner(id, definition, entry, (string?)value["strRegIDLast"],
            (bool?)value["bAlive"] ?? true, person, characters, children, messages, relationships, specs, histories, interactions);
        if (owners.TryGetValue(id, out var earlier)) duplicateOwners.Add(new { id, earlier = earlier.Entry, later = entry });
        else owners.Add(id, owner);
        records++;
        recordCharacters += characters;
        if (!owner.Alive) deadRecordCharacters += characters;
        messageCount += messages;
        relationshipCount += relationships;
        personSpecCount += specs;
        historyCount += histories;
        historyInteractionCount += interactions;
        if (anchor) anchors.Add(id);
        foreach (var property in value.Properties())
        {
            long size = property.Value.ToString(Formatting.None).Length;
            coFieldCharacters[property.Name] = coFieldCharacters.GetValueOrDefault(property.Name) + size;
            if (!owner.Alive && property.Name == "mapIAHist2") deadHistoryCharacters += size;
            if (size > 100_000) largestFields.Add(new LargeField(id, entry, property.Name, size));
            if (property.Name is not ("strID" or "aConds" or "aCondAges" or "aCondRules" or "aCondZeroes" or "mapIAHist2"))
                RememberStrings(property.Value);
        }
        if (relationships > 100) socialLargest.Add(new LargeSocial(id, entry, relationships, specs, owner.Alive));
    }

    private void ReadMarkets(JToken value)
    {
        if (value["mapMarket"] is not JObject map) return;
        foreach (var market in map.Properties())
        {
            var configs = market.Value["configMap"] as JObject;
            markets.Add(new JObject
            {
                ["id"] = market.Name,
                ["storedId"] = market.Value["strShipRegId"],
                ["actors"] = configs?.Count ?? 0,
                ["actorsWithStock"] = configs?.Properties().Count(p => (p.Value["aStock"] as JArray)?.Count > 0) ?? 0,
                ["stockEntries"] = configs?.Properties().Sum(p => (p.Value["aStock"] as JArray)?.Count ?? 0) ?? 0,
                ["compactCharacters"] = market.Value.ToString(Formatting.None).Length,
                ["configFields"] = new JArray(configs?.Properties().FirstOrDefault()?.Value.Children<JProperty>().Select(p => p.Name) ?? [])
            });
        }
    }

    private void RememberStrings(JToken token)
    {
        if (token is JValue value)
        {
            if (value.Type == JTokenType.String && (string?)value is string text) externalStrings.Add(text);
            return;
        }
        if (token is JObject obj)
        {
            foreach (var property in obj.Properties())
            {
                externalStrings.Add(property.Name);
                RememberStrings(property.Value);
            }
            return;
        }
        foreach (var child in token.Children()) RememberStrings(child);
    }
}

// JsonTextReader buffers ahead. Its line/column, rather than the underlying
// stream cursor, locates the current token. Discard old line offsets after use.
internal sealed class PositionTextReader(TextReader input) : TextReader
{
    private readonly Dictionary<int, long> lineStarts = new() { [1] = 0 };
    private long characters;
    private int line = 1;
    private int firstRetainedLine = 1;
    private bool previousWasCr;
    public long LastRecordCharacters { get; set; }

    public long Position(JsonTextReader reader)
    {
        int current = Math.Max(reader.LineNumber, 1);
        long result = lineStarts[current] + reader.LinePosition;
        while (firstRetainedLine < current) lineStarts.Remove(firstRetainedLine++);
        return result;
    }

    public override int Read(char[] buffer, int index, int count)
    {
        int read = input.Read(buffer, index, count);
        for (int i = index; i < index + read; i++)
        {
            char c = buffer[i];
            characters++;
            if (c == '\r') lineStarts[++line] = characters;
            else if (c == '\n')
            {
                if (!previousWasCr) line++;
                lineStarts[line] = characters;
            }
            previousWasCr = c == '\r';
        }
        return read;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) input.Dispose();
        base.Dispose(disposing);
    }
}
