using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

if (args.Length < 2) throw new ArgumentException("Usage: InventoryLoadAudit <save.zip> <output.json> [ship IDs...]");
string save = Path.GetFullPath(args[0]);
string output = Path.GetFullPath(args[1]);
string[] ships = args.Length > 2 ? args[2..] : ["OKLG_UNK", "H-3UTJ"];
string Hash() {
    using var file = File.OpenRead(save);
    return Convert.ToHexString(SHA256.HashData(file));
}
string before = Hash();
string data = Ostrabus.Development.InstallationPaths.Data;
Dictionary<string, JObject> ReadTable(string folder) {
    var result = new Dictionary<string, JObject>(StringComparer.Ordinal);
    foreach (string file in Directory.GetFiles(Path.Combine(data, folder), "*.json").Order()) {
        foreach (JObject entry in JArray.Parse(File.ReadAllText(file))) {
            if ((string?)entry["strName"] is string name) result[name] = entry;
        }
    }
    return result;
}
var definitions = ReadTable("condowners");
var overlays = ReadTable("cooverlays");
var loots = ReadTable("loot");
IEnumerable<string> LootConditions(string name, int depth = 0) {
    if (depth > 32 || !loots.TryGetValue(name, out var loot)) yield break;
    foreach (string condition in (loot["aCOs"]?.Values<string>() ?? []).OfType<string>()) yield return condition;
    foreach (string nested in (loot["aLoots"]?.Values<string>() ?? []).OfType<string>()) {
        foreach (string condition in LootConditions(nested.Split('=')[0], depth + 1)) yield return condition;
    }
}
double Equation(string text) {
    int index = text.LastIndexOf('x');
    return index >= 0 && float.TryParse(text[(index + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : 0;
}
double? LooseHealth(string name) {
    overlays.TryGetValue(name, out var overlay);
    string baseName = (string?)overlay?["strCOBase"] ?? name;
    if (!definitions.TryGetValue(baseName, out var definition)) return null;
    string[] conditions = (definition["aStartingConds"]?.Values<string>() ?? []).OfType<string>().ToArray();
    if (conditions.Any(value => value.StartsWith("IsInstalled=", StringComparison.Ordinal))) return null;
    double health = conditions.Where(value => value.StartsWith("StatDamageMax=", StringComparison.Ordinal)).Select(Equation).FirstOrDefault();
    if ((string?)overlay?["strCondLoot"] is string loot) {
        foreach (string condition in LootConditions(loot)) {
            if (condition.StartsWith("StatDamageMax=", StringComparison.Ordinal)) health += Equation(condition);
            if (condition.StartsWith("-StatDamageMax=", StringComparison.Ordinal)) health -= Equation(condition);
        }
    }
    return health;
}

using var zip = ZipFile.OpenRead(save);
var layouts = new Dictionary<string, Dictionary<string, Item>>();
foreach (string ship in ships) {
    var entry = zip.GetEntry("ships/" + ship + ".json");
    if (entry == null) { Console.WriteLine("Absent layout: " + ship); continue; }
    var items = new Dictionary<string, Item>(StringComparer.Ordinal);
    using var text = new StreamReader(entry.Open());
    using var reader = new JsonTextReader(text);
    while (reader.Read()) {
        if (reader.TokenType != JsonToken.PropertyName || reader.Value is not string key || key is not ("aItems" or "aCrew")) continue;
        reader.Read();
        if (reader.TokenType != JsonToken.StartArray) continue;
        while (reader.Read() && reader.TokenType != JsonToken.EndArray) {
            var record = JObject.Load(reader);
            string? id = (string?)record["strID"], name = (string?)record["strName"];
            if (id == null || name == null) continue;
            var damage = (record["aCondOverrides"] as JArray)?.OfType<JObject>().FirstOrDefault(condition => (string?)condition["CondName"] == "StatDamage");
            double? amount = (double?)damage?["Amount"];
            if ((bool?)damage?["NegativeValue"] == true) amount = -amount;
            double? health = LooseHealth(name);
            items[id] = new Item(id, name, (string?)record["strParentID"] ?? (string?)record["strSlotParentID"], amount, health);
        }
    }
    layouts[ship] = items;
    Console.WriteLine("Read {0}: {1} layout records.", ship, items.Count);
}

var rows = new List<object>();
var selected = new HashSet<string>(StringComparer.Ordinal);
foreach (var pair in layouts) {
    var items = pair.Value;
    var available = items.Values.Where(item => item.Parent == null && !item.Destroyed).Select(item => item.Id).ToHashSet();
    var pending = items.Values.Where(item => item.Parent != null).ToList();
    int progress;
    do {
        progress = 0;
        for (int index = pending.Count - 1; index >= 0; index--) {
            var item = pending[index];
            if (!available.Contains(item.Parent!)) continue;
            pending.RemoveAt(index);
            if (!item.Destroyed) available.Add(item.Id);
            progress++;
        }
    } while (progress != 0);
    var unresolved = new List<object>();
    foreach (var item in pending) {
        var chain = new List<Item>();
        var seen = new HashSet<string>();
        Item current = item;
        string reason;
        while (true) {
            chain.Add(current);
            selected.Add(current.Id);
            if (!seen.Add(current.Id)) { reason = "parent-cycle"; break; }
            if (current.Destroyed) { reason = "destroyed-item-or-ancestor"; break; }
            if (current.Parent == null) { reason = "unmodeled-native-spawn-or-slot-failure"; break; }
            if (!items.TryGetValue(current.Parent, out var parent)) { reason = "parent-absent-from-layout"; break; }
            current = parent;
        }
        unresolved.Add(new { item.Id, item.Name, item.Parent, reason, ancestors = chain.Select(node => new { node.Id, node.Name, node.Damage, node.Health }) });
    }
    rows.Add(new { ship = pair.Key, layoutItems = items.Count, unresolvedCount = pending.Count, unresolved });
    Console.WriteLine("Modeled unresolved {0}: {1}", pair.Key, pending.Count);
}

// Stream the large main save one CO at a time. Keep only the selected records.
var saved = new Dictionary<string, JObject>();
var main = zip.GetEntry("Ayden Kelly.json") ?? throw new Exception("Main save JSON missing.");
using (var text = new StreamReader(main.Open())) using (var reader = new JsonTextReader(text)) {
    while (reader.Read()) {
        if (reader.TokenType != JsonToken.PropertyName || (string?)reader.Value != "aCOs") continue;
        reader.Read();
        while (reader.Read() && reader.TokenType != JsonToken.EndArray) {
            var co = JObject.Load(reader);
            if ((string?)co["strID"] is string id && selected.Contains(id)) saved[id] = co;
        }
        break;
    }
}
string after = Hash();
if (before != after) throw new Exception("Save changed during read-only audit; discard results and retry.");
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output, JsonConvert.SerializeObject(new { saveName = Path.GetFileName(save), sourceHash = before, rows, saved }, Formatting.Indented));
Console.WriteLine("Retained {0} matching saved COs; source hash unchanged. Report: {1}", saved.Count, output);

sealed record Item(string Id, string Name, string? Parent, double? Damage, double? Health) {
    public bool Destroyed => Damage.HasValue && Health.HasValue && Damage.Value >= Health.Value;
}
