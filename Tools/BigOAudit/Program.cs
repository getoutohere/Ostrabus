using System.IO.Compression;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

if (args.Length != 1) throw new ArgumentException("Usage: BigOAudit <save.zip>");
using var zip = ZipFile.OpenRead(args[0]);
var rows = new List<object>();
foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith("ships/") && e.FullName.EndsWith(".json"))) {
    using var text = new StreamReader(entry.Open()); using var reader = new JsonTextReader(text);
    var root = JToken.Load(reader);
    foreach (var ship in root is JArray array ? array.OfType<JObject>() : new[] { (JObject)root }) rows.Add(Analyze(ship, entry.FullName));
}
Console.WriteLine(JsonConvert.SerializeObject(rows, Formatting.Indented));

static object Analyze(JObject ship, string entryName) {
    var items = (ship["aItems"] as JArray)?.OfType<JObject>().ToList() ?? new();
    var roots = items.Where(i => i["strParentID"]?.Type != JTokenType.String && i["strSlotParentID"]?.Type != JTokenType.String).ToList();
    var pending = items.Except(roots).ToList();
    int contained = pending.Count, passes = 0;
    long attempts = 0, comparisons = 0, shifts = 0;
    var available = new HashSet<string>(roots.Select(i => (string)i["strID"]!).Where(i => i != null));
    // Source-faithful parent readiness/list bookkeeping, assuming successful creation
    // and slotting. This counts operations, not engine time or malformed-data retries.
    while (pending.Count > 0) {
        passes++; int progress = 0;
        for (int i = pending.Count - 1; i >= 0; i--) {
            attempts++;
            var item = pending[i]; string? parent = (string?)item["strParentID"] ?? (string?)item["strSlotParentID"];
            if (parent == null || !available.Contains(parent)) continue;
            comparisons += i + 1; shifts += pending.Count - i - 1;
            available.Add((string)item["strID"]!); pending.RemoveAt(i); progress++;
        }
        if (progress == 0) break;
    }
    return new { ship = (string?)ship["strRegID"] ?? entryName, items = items.Count, savedCos = (ship["aCOs"] as JArray)?.Count ?? 0, contained, dependencyPasses = passes,
        parentAttempts = attempts, listRemoveReferenceComparisons = comparisons, listRemoveShiftedReferences = shifts, unresolved = pending.Count,
        allMatchedWritebackIdComparisons = (long)items.Count * (items.Count + 1) / 2 };
}
