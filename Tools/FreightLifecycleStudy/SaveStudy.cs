using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static class SaveStudy
{
    internal static JObject Ship(JToken value) => value is JArray array ? (JObject)array[0] : (JObject)value;

    internal static int Summarize(string[] args)
    {
        var rows = JArray.Parse(File.ReadAllText(args[1]));
        string id = args[2];
        foreach (var row in rows)
        {
            var record = row["ships"]?[id];
            if ((bool?)record?["present"] != true || record["ship"] == null) continue;
            var ship = Ship(record["ship"]);
            var report = new JObject { ["save"] = row["save"], ["id"] = id };
            foreach (string key in new[] { "publicName", "origin", "strScanTargetID",
                "fShallowRCSRemass", "fAIDockingExpire", "fAIPauseTimer" })
                report[key] = ship[key];
            report["crew"] = new JArray(((JArray)ship["aCrew"] ?? new JArray()).Select(member => member["strID"]));
            var situ = ship["objSS"];
            report["motion"] = situ == null ? null : new JObject(new[] {
                "vPosx", "vPosy", "vVelX", "vVelY", "bBOLocked", "bOrbitLocked", "bIgnoreGrav", "vAccIn", "vAccRCS"
            }.Select(key => new JProperty(key, situ[key])));
            var points = situ?["jnd"]?["aPoints"];
            report["navSummary"] = points is JArray array ? new JObject { ["count"] = array.Count,
                ["firstArrival"] = array.First?["ArrivalTime"], ["lastArrival"] = array.Last?["ArrivalTime"] } : null;
            report["log"] = ship["aLog"] is JArray log ? new JArray(log.TakeLast(12)) : null;
            Console.WriteLine(report.ToString(Formatting.None));
        }
        return 0;
    }

    internal static int Root(string[] args)
    {
        if (args.Length < 4) throw new ArgumentException("root OUTPUT_DIRECTORY SAVE.zip SHIP_ID [...]");
        string output = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(output);
        string path = args[2];
        var ids = new HashSet<string>(args.Skip(3).Where(arg => !arg.StartsWith("--")), StringComparer.Ordinal);
        var people = new HashSet<string>(args.Skip(3).Where(arg => arg.StartsWith("--person="))
            .Select(arg => arg.Substring("--person=".Length)), StringComparer.Ordinal);
        var marketIds = new HashSet<string>(ids, StringComparer.Ordinal);
        bool everywhere = args.Contains("--people-everywhere");
        bool allStations = args.Contains("--all-stations");
        bool stationLifecycle = args.Contains("--station-lifecycle");
        marketIds.UnionWith(args.Skip(3).Where(arg => arg.StartsWith("--market="))
            .Select(arg => arg.Substring("--market=".Length)));
        var ships = new JObject();
        using var zip = ZipFile.OpenRead(path);
        foreach (string id in ids)
        {
            var entry = zip.GetEntry("ships/" + id + ".json");
            if (entry == null) { ships[id] = null; continue; }
            using var stream = new StreamReader(entry.Open());
            using var reader = new JsonTextReader(stream);
            var ship = Ship(JToken.ReadFrom(reader));
            ships[id] = ship;
            foreach (string key in new[] { "origin", "strScanTargetID" })
                if ((string)ship[key] is string station && station.Length > 0) marketIds.Add(station);
            foreach (string key in new[] { "aCrew", "aShallowPSpecs" })
                if (ship[key] is JArray crew)
                    foreach (var member in crew)
                        if ((string)member["strID"] is string coId) people.Add(coId);
        }
        var report = new JObject
        {
            ["save"] = Path.GetFileNameWithoutExtension(path),
            ["source"] = Path.GetFullPath(path),
            ["ships"] = ships,
            ["peopleRequested"] = new JArray(people.Order()),
            ["people"] = new JArray(),
            ["pilots"] = new JArray(),
            ["markets"] = new JObject(),
            ["stationMotion"] = new JObject(),
            ["stationSpecs"] = new JObject()
        };
        foreach (var pair in ships.Properties())
            if (pair.Value is JObject ship && ship["aCOs"] is JArray cos)
                foreach (var co in cos.Where(co => people.Contains((string)co["strID"])))
                    ((JArray)report["people"]).Add(co);
        var rootEntry = zip.Entries.First(entry => !entry.FullName.StartsWith("ships/") &&
            entry.FullName != "saveInfo.json" && entry.FullName.EndsWith(".json"));
        using (var stream = new StreamReader(rootEntry.Open()))
        using (var reader = new JsonTextReader(stream))
        {
            while (reader.Read())
            {
                if (reader.TokenType != JsonToken.PropertyName || reader.Depth != 2) continue;
                string key = (string)reader.Value;
                reader.Read();
                switch (key)
                {
                    case "aCOs":
                        ReadPeople(reader, people, (JArray)report["people"]);
                        break;
                    case "objMarketSave":
                        var marketSave = JObject.Load(reader);
                        var map = (JObject)marketSave["mapMarket"];
                        foreach (string id in marketIds) report["markets"][id] = map?[id];
                        report["marketIds"] = new JArray(map?.Properties().Select(property => property.Name) ?? Array.Empty<string>());
                        break;
                    case "objAIShipManager":
                        var manager = JObject.Load(reader);
                        report["pilots"] = new JArray(((JArray)manager["aAIShips"])
                            .Where(pilot => ids.Contains((string)pilot["strRegId"])));
                        break;
                    case "objSystem":
                        var system = JObject.Load(reader);
                        report["epoch"] = system["dfEpoch"];
                        report["bodies"] = system["aBOs"];
                        report["hierarchy"] = system["dictBOHierarchy"];
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }
        }
        report["peopleSearch"] = everywhere ? "main root and all ship entries" : "main root, selected hulls and candidate stations";
        // Stream records instead of loading every hull into memory. A complete
        // people search can establish absence, while the default is a quick
        // search of likely disembarkation locations.
        foreach (var entry in zip.Entries.Where(entry => entry.FullName.StartsWith("ships/") &&
            entry.FullName.EndsWith(".json")))
        {
            string stationId = Path.GetFileNameWithoutExtension(entry.FullName);
            bool candidate = stationId == "OKLG_UNK" || marketIds.Any(id =>
                stationId == id || stationId.StartsWith(id + "_", StringComparison.Ordinal));
            if (ids.Contains(stationId) || !(everywhere || candidate || allStations || stationLifecycle)) continue;
            var station = new JObject();
            using var stationStream = new StreamReader(entry.Open());
            using var stationReader = new JsonTextReader(stationStream);
            while (stationReader.Read())
            {
                if (stationReader.TokenType != JsonToken.PropertyName || stationReader.Depth != 2) continue;
                string key = (string)stationReader.Value;
                stationReader.Read();
                if (key == "aCOs" && (everywhere || candidate)) ReadPeople(stationReader, people, (JArray)report["people"]);
                else if (stationLifecycle && (key == "shipCO" || key == "aFactions")) station[key] = JToken.ReadFrom(stationReader);
                else if (stationLifecycle && key == "aCrew") {
                    int count = 0;
                    if (stationReader.TokenType == JsonToken.StartArray) while (stationReader.Read() && stationReader.TokenType != JsonToken.EndArray) {
                        count++; stationReader.Skip();
                    }
                    station["savedCrewCount"] = count;
                }
                else if (key == "objSS") {
                    station[key] = JToken.ReadFrom(stationReader);
                    if (candidate) report["stationMotion"][stationId] = station[key];
                }
                else if (new[] { "aDockingPorts", "ShipType", "bShipHidden", "bNoCollisions" }.Contains(key)) station[key] = JToken.ReadFrom(stationReader);
                else stationReader.Skip();
            }
            if ((bool?)station["objSS"]?["bIsBO"] == true &&
                (station["aDockingPorts"] is JArray ports && ports.Count > 0 || stationLifecycle && (int?)station["ShipType"] != 7)) { // Native Waypoint = 7.
                report["stationMotion"][stationId] = station["objSS"];
                report["stationSpecs"][stationId] = station;
            }
        }
        string file = Path.Combine(output, Path.GetFileNameWithoutExtension(path) + "-root.json");
        File.WriteAllText(file, report.ToString(Formatting.Indented));
        Console.WriteLine(new JObject
        {
            ["save"] = report["save"], ["epoch"] = report["epoch"],
            ["ships"] = new JObject(ships.Properties().Select(property =>
                new JProperty(property.Name, property.Value.Type != JTokenType.Null))),
            ["peopleFound"] = new JArray(((JArray)report["people"]).Select(person => person["strID"])),
            ["pilotsFound"] = ((JArray)report["pilots"]).Count,
            ["report"] = file
        }.ToString(Formatting.None));
        return 0;
    }

    internal static int Report(string[] args)
    {
        var report = JObject.Parse(File.ReadAllText(args[1]));
        var markets = new JObject();
        foreach (var market in ((JObject)report["markets"]).Properties())
        {
            if (market.Value.Type == JTokenType.Null) { markets[market.Name] = null; continue; }
            var configs = (JObject)market.Value["configMap"];
            var first = configs?.Properties().FirstOrDefault()?.Value;
            markets[market.Name] = new JObject
            {
                ["actors"] = configs?.Count,
                ["stockRows"] = configs?.Properties().Sum(actor => (actor.Value["aStock"] as JArray)?.Count ?? 0),
                ["quantities"] = SavedStock(configs),
                ["firstActorFields"] = first is JObject obj ? new JArray(obj.Properties().Select(property => property.Name)) : null,
                ["stockExample"] = (first?["aStock"] as JArray)?.First
            };
        }
        Console.WriteLine(new JObject
        {
            ["save"] = report["save"], ["epoch"] = report["epoch"],
            ["markets"] = markets,
            ["people"] = new JArray(((JArray)report["people"]).Select(person => new JObject
            {
                ["id"] = person["strID"], ["alive"] = person["bAlive"],
                ["ship"] = person["strRegIDLast"],
                ["conditions"] = new JArray(((JArray)person["aConds"] ?? new JArray())
                    .Where(value => new[] { "Stale", "Dead", "Health", "Damage", "Ship", "NPC", "Human" }
                        .Any(word => ((string)value).Contains(word))))
            })),
            ["pilots"] = report["pilots"]
        }.ToString(Formatting.None));
        return 0;
    }

    internal static int Compare(string[] args)
    {
        var before = JObject.Parse(File.ReadAllText(args[1]));
        var after = JObject.Parse(File.ReadAllText(args[2]));
        var rows = new JArray();
        foreach (string id in args.Skip(3))
        {
            var a = before["markets"]?[id]?["configMap"] as JObject;
            var b = after["markets"]?[id]?["configMap"] as JObject;
            string Fingerprint(JObject configs)
            {
                if (configs == null) return null;
                var stock = configs.Properties().SelectMany(actor =>
                    (actor.Value["aStock"] as JArray ?? new JArray()).Select(item => actor.Name + "\t" + (string)item))
                    .Order(StringComparer.Ordinal);
                return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", stock))));
            }
            var crew = ((JArray)before["ships"]?[id]?["aCrew"] ?? new JArray()).Select(person => (string)person["strID"]).ToHashSet();
            rows.Add(new JObject
            {
                ["id"] = id,
                ["hullBefore"] = before["ships"]?[id]?.Type == JTokenType.Object,
                ["hullAfter"] = after["ships"]?[id]?.Type == JTokenType.Object,
                ["cargoBefore"] = SavedStock(a), ["cargoAfter"] = SavedStock(b),
                ["stockFingerprintEqual"] = a != null && b != null && Fingerprint(a) == Fingerprint(b),
                ["stockFingerprintBefore"] = Fingerprint(a), ["stockFingerprintAfter"] = Fingerprint(b),
                ["crewIdsBefore"] = new JArray(crew),
                ["crewFoundAfter"] = new JArray(((JArray)after["people"]).Where(person => crew.Contains((string)person["strID"]))),
                ["peopleSearchAfter"] = after["peopleSearch"]
            });
        }
        Console.WriteLine(new JObject
        {
            ["before"] = before["save"], ["after"] = after["save"],
            ["elapsedSeconds"] = (double)after["epoch"] - (double)before["epoch"], ["ships"] = rows
        }.ToString(Formatting.None));
        return 0;
    }

    internal static int Fixture(string[] args)
    {
        var source = JObject.Parse(File.ReadAllText(args[2]));
        var ships = new JObject();
        foreach (string id in args.Skip(3))
        {
            var ship = (JObject)source["ships"][id];
            ships[id] = new JObject(new[] {
                "strRegID", "origin", "strScanTargetID", "objSS", "objSituScanTarget",
                "fShallowMass", "fShallowRCSRemass", "fShallowRCSRemassMax", "fShallowFusionRemain",
                "fFusionThrustMax", "fFusionPelletMax", "nRCSCount", "nRCSCountRaw", "nRCSDistroCount", "nRCSExhaustV",
                "fShallowRotorStrength", "bFusionTorch", "ShipType", "bAIShip", "aDockingPorts"
            }.Where(key => ship[key] != null).Select(key => new JProperty(key, ship[key].DeepClone())));
        }
        var output = new JObject {
            ["save"] = source["save"], ["epoch"] = source["epoch"],
            ["bodies"] = source["bodies"], ["hierarchy"] = source["hierarchy"],
            ["ships"] = ships, ["stationMotion"] = source["stationMotion"], ["stationSpecs"] = source["stationSpecs"],
            ["note"] = "Saved physics inputs only; no people, inventories or personal filesystem paths."
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
        File.WriteAllText(args[1], output.ToString(Formatting.Indented));
        Console.WriteLine("Extracted " + ships.Count + " compact freight physics fixtures.");
        return 0;
    }

    private static JObject SavedStock(JObject configs)
    {
        var result = new JObject();
        if (configs == null) return result;
        foreach (var config in configs.Properties())
        foreach (string item in config.Value["aStock"] as JArray ?? new JArray())
        {
            int categoryEnd = item.IndexOf('|');
            int amountStart = item.LastIndexOf('=');
            if (categoryEnd < 0 || amountStart < 0) throw new InvalidDataException("Unknown saved stock shape.");
            string category = item.Substring(0, categoryEnd);
            long count = long.Parse(item.Substring(amountStart + 1), System.Globalization.CultureInfo.InvariantCulture);
            result[category] = ((long?)result[category] ?? 0) + count;
        }
        return result;
    }

    private static void ReadPeople(JsonTextReader reader, HashSet<string> selected, JArray output)
    {
        if (reader.TokenType == JsonToken.Null) return;
        if (reader.TokenType != JsonToken.StartArray) throw new InvalidDataException("Unexpected aCOs container.");
        while (reader.Read() && reader.TokenType != JsonToken.EndArray)
        {
            if (reader.TokenType != JsonToken.StartObject) throw new InvalidDataException("Unexpected CO record.");
            int depth = reader.Depth;
            reader.Read();
            if (reader.TokenType != JsonToken.PropertyName || (string)reader.Value != "strID")
                throw new InvalidDataException("CO ID is no longer its first saved property; update the streaming extractor.");
            reader.Read();
            string id = (string)reader.Value;
            bool keep = selected.Contains(id);
            var person = keep ? new JObject { ["strID"] = id } : null;
            while (reader.Read() && !(reader.TokenType == JsonToken.EndObject && reader.Depth == depth))
            {
                if (keep && reader.TokenType == JsonToken.PropertyName)
                {
                    string field = (string)reader.Value;
                    reader.Read();
                    person[field] = JToken.ReadFrom(reader);
                }
                else reader.Skip();
            }
            if (keep) output.Add(person);
        }
    }
}
