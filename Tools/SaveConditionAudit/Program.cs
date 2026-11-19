using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

try {
    if (args.Length < 3) throw new ArgumentException("Usage: SaveConditionAudit <save.zip> <actor-id> <condition> [condition...]");
    string archive = Path.GetFullPath(args[0]), actorId = args[1];
    string Hash() {
        using var file = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(file));
    }
    string before = Hash();
    JObject? actor = null;
    string? actorSource = null;
    string? playerId = null;
    double? epoch = null;
    using (var file = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
    using (var zip = new ZipArchive(file, ZipArchiveMode.Read)) {
        var entry = zip.Entries.Where(e => !e.FullName.StartsWith("ships/", StringComparison.Ordinal) &&
            e.FullName.EndsWith(".json", StringComparison.Ordinal)).OrderByDescending(e => e.Length).First();
        ReadEntry(entry, true);
        if (actor == null) {
            foreach (var ship in zip.Entries.Where(e => e.FullName.StartsWith("ships/", StringComparison.Ordinal) &&
                e.FullName.EndsWith(".json", StringComparison.Ordinal))) {
                ReadEntry(ship, false);
                if (actor != null) break;
            }
        }
    }
    void ReadEntry(ZipArchiveEntry entry, bool world) {
        using var text = new StreamReader(entry.Open());
        using var reader = new JsonTextReader(text) { DateParseHandling = DateParseHandling.None };
        if (!reader.Read()) throw new InvalidDataException("Empty save entry.");
        int propertyDepth = reader.TokenType == JsonToken.StartArray ? 2 : 1;
        while (reader.Read()) {
            if (reader.TokenType != JsonToken.PropertyName || reader.Depth != propertyDepth) continue;
            string name = (string)reader.Value!;
            if (!reader.Read()) throw new InvalidDataException("Missing root value.");
            if (name == "aCOs") {
                while (reader.Read() && reader.TokenType != JsonToken.EndArray) {
                    var record = JObject.Load(reader);
                    if ((string?)record["strID"] != actorId) continue;
                    if (actor != null) throw new InvalidDataException("Duplicate actor ID in root CO table.");
                    actor = record;
                    actorSource = entry.FullName;
                }
            } else if (world && name == "strPlayerCO") playerId = (string?)reader.Value;
            else if (world && name == "objSystem") epoch = (double?)JObject.Load(reader)["dfEpoch"];
            else reader.Skip();
        }
    }
    if (before != Hash()) throw new IOException("Archive changed during inspection; discard results and retry.");
    if (actor == null) throw new InvalidDataException("Actor not found in saved global or physical ship CO tables: " + actorId);
    var conditions = actor["aConds"]?.Values<string>().OfType<string>().ToArray() ?? [];
    double? Scalar(string equation) {
        int equals = equation.IndexOf('='), x = equation.LastIndexOf('x');
        if (equals < 0 || x < equals || equation[(equals + 1)..x] != "1.0") return null;
        return double.TryParse(equation[(x + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : null;
    }
    Console.WriteLine(JsonConvert.SerializeObject(new {
        archive, sourceHash = before, archiveUnchanged = true, epoch, playerId,
        actor = new { id = actorId, source = actorSource, definition = actor["strCODef"], alive = actor["bAlive"],
            lastShip = actor["strRegIDLast"], actorEpoch = actor["fEpoch"] },
        conditions = args.Skip(2).Select(name => {
            var matches = conditions.Where(c => c == name || c.StartsWith(name + "=", StringComparison.Ordinal)).ToArray();
            return new { name, explicitlyPresent = matches.Length != 0, equations = matches,
                explicitlyZeroed = actor["aCondZeroes"]?.Values<string>().Any(c => c == name) == true,
                scalar = matches.Length == 1 ? Scalar(matches[0]) : null };
        }),
        note = "Raw saved assignments only. Absence is not an explicit zero; DEFAULT definitions and runtime evaluators are not expanded."
    }, Formatting.Indented));
    return 0;
} catch (Exception error) {
    Console.Error.WriteLine("Saved condition audit failed: " + error.Message);
    return 1;
}
