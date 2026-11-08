using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

if (args.Length == 0) throw new ArgumentException("Usage: CharacterMassAudit <save.zip> [output-directory] [character-id;character-id] [graph.json]");
string save = Path.GetFullPath(args[0]), output = Path.GetFullPath(args.Length > 1 ? args[1] : "obj/CharacterMassAudit");
Directory.CreateDirectory(output);
string Hash() { using var stream = File.OpenRead(save); return Convert.ToHexString(SHA256.HashData(stream)); }
string before = Hash();
var definitions = Assets("condowners"); var loot = Assets("loot"); var conditions = Assets("conditions"); var overlays = Assets("cooverlays");
JObject? Definition(string name) => definitions.GetValueOrDefault(name) ??
    (overlays.TryGetValue(name, out var overlay) && (string?)overlay["strCOBase"] is string baseName ? definitions.GetValueOrDefault(baseName) : null);
Dictionary<string, JObject> Assets(string folder) {
    var result = new Dictionary<string, JObject>(StringComparer.Ordinal);
    foreach (string file in Directory.GetFiles(Path.Combine(Ostrabus.Development.InstallationPaths.Data, folder), "*.json").Order())
        foreach (JObject row in JArray.Parse(File.ReadAllText(file))) if ((string?)row["strName"] is string name) result[name] = row;
    return result;
}
double? Number(string equation) {
    int split = equation.IndexOf('x');
    return split >= 0 && double.TryParse(equation.AsSpan(split + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && double.IsFinite(value) ? value : null;
}
double? FixedMass(JObject definition) {
    var equations = (definition["aStartingConds"]?.Values<string>().OfType<string>() ?? []).Where(e => e.StartsWith("StatMass=", StringComparison.Ordinal)).ToArray();
    return equations.Length == 1 && equations[0].StartsWith("StatMass=1.0x", StringComparison.Ordinal) ? Number(equations[0]) : null;
}
var massLoot = loot.Values.Select(row => new { name = (string)row["strName"]!, equations = (row["aCOs"]?.Values<string>().OfType<string>() ?? []).Where(e => e.StartsWith("StatMass=", StringComparison.Ordinal) || e.StartsWith("-StatMass=", StringComparison.Ordinal)).ToArray() }).Where(x => x.equations.Length > 0).ToArray();
var massTraits = conditions.Values.Select(row => new { condition = (string)row["strName"]!, effects = (row["aPer"]?.Values<string>().OfType<string>() ?? []).Where(n => massLoot.Any(l => l.name == n)).ToArray() }).Where(x => x.effects.Length > 0).ToArray();
var defaultLimbs = loot["ItmCrewSlots"]["aCOs"]!.Values<string>().OfType<string>().Select(e => e.Split('=')[0]).Where(n => n.StartsWith("Body", StringComparison.Ordinal)).Select(n => new { definition = n, kg = definitions.TryGetValue(n, out var def) ? FixedMass(def) : null }).ToArray();
var nodes = new Dictionary<string, Node>(StringComparer.Ordinal);
void ReadNodes(JsonTextReader reader) {
    reader.Read();
    if (reader.TokenType == JsonToken.Null) return;
    if (reader.TokenType != JsonToken.StartArray) throw new Exception("Expected saved CO array");
    while (reader.Read() && reader.TokenType != JsonToken.EndArray) {
        var row = JObject.Load(reader); string id = (string)row["strID"]!, def = (string)row["strCODef"]!;
        string[] equations = row["aConds"]?.Values<string>().OfType<string>().ToArray() ?? [];
        bool unknownDefault = false;
        if (equations.Contains("DEFAULT")) {
            var defaults = Definition(def); unknownDefault = defaults == null;
            equations = equations.Where(e => e != "DEFAULT").Concat(defaults?["aStartingConds"]?.Values<string>().OfType<string>() ?? []).ToArray();
        }
        var values = new Dictionary<string, double>();
        foreach (string equation in equations) {
            int equals = equation.IndexOf('='); if (equals < 0 || Number(equation) is not double amount) continue;
            string name = equation[..equals]; if (name.StartsWith('-')) values[name[1..]] = 0; else values[name] = amount;
        }
        nodes[id] = new Node { Id = id, Definition = def, UnknownDefault = unknownDefault, Values = values, Stack = row["aStack"]?.Values<string>().OfType<string>().ToArray() ?? [], Lot = row["aLot"]?.Values<string>().OfType<string>().ToArray() ?? [] };
    }
}
using var zip = ZipFile.OpenRead(save);
// Identify the global record by structure instead of assuming the player's name.
var main = zip.Entries.Single(e => !e.FullName.Contains('/') && e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && e.Name != "saveInfo.json");
using (var text = new StreamReader(main.Open())) using (var reader = new JsonTextReader(text)) {
    reader.Read(); int rootPropertyDepth = reader.TokenType == JsonToken.StartArray ? 2 : 1;
    while (reader.Read()) {
        if (reader.TokenType != JsonToken.PropertyName || reader.Depth != rootPropertyDepth || (string?)reader.Value != "aCOs") continue;
        ReadNodes(reader);
        break;
    }
}
var layouts = new Dictionary<string, Layout>(StringComparer.Ordinal); var duplicate = new HashSet<string>();
foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith("ships/", StringComparison.Ordinal) && e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))) {
    using var text = new StreamReader(entry.Open()); using var reader = new JsonTextReader(text);
    while (reader.Read()) {
        if (reader.TokenType != JsonToken.PropertyName) continue;
        if ((string?)reader.Value == "aCOs") { ReadNodes(reader); continue; }
        if ((string?)reader.Value is not ("aItems" or "aCrew" or "aShallowPSpecs")) continue;
        reader.Read();
        while (reader.Read() && reader.TokenType != JsonToken.EndArray) {
            var row = JObject.Load(reader); if ((string?)row["strID"] is not string id) continue;
            if (layouts.ContainsKey(id)) duplicate.Add(id);
            layouts[id] = new Layout((string?)row["strParentID"] ?? (string?)row["strSlotParentID"], entry.FullName, (row["aCondOverrides"] as JArray)?.Any(o => (string?)o["CondName"] == "StatMass") == true);
        }
    }
}
var heads = new Dictionary<string, string>(); var lotMembers = nodes.Values.SelectMany(n => n.Lot).ToHashSet();
foreach (var node in nodes.Values) foreach (string member in node.Stack) { if (!heads.TryAdd(member, node.Id)) duplicate.Add(member); }
var children = new Dictionary<string, List<string>>();
foreach (var pair in layouts) if (pair.Value.Parent is string parent && !heads.ContainsKey(pair.Key) && !lotMembers.Contains(pair.Key)) {
    if (!children.TryGetValue(parent, out var list)) children[parent] = list = []; list.Add(pair.Key);
}
double Total(string id, HashSet<string> seen) {
    if (seen.Count > 10000 || !seen.Add(id)) throw new Exception("Cyclic/repeated mass reference: " + id);
    if (!nodes.TryGetValue(id, out var node)) throw new Exception("Missing saved CO: " + id);
    if (node.UnknownDefault) throw new Exception("Unresolved saved defaults: " + id);
    if (duplicate.Contains(id)) throw new Exception("Duplicate saved layout/stack membership: " + id);
    double mass = node.Values.GetValueOrDefault("StatMass");
    if (mass < 0) throw new Exception("Negative saved mass: " + id);
    foreach (string member in node.Stack) mass += Total(member, seen);
    return mass;
}
double RebuildAnatomy(string id, HashSet<string> seen, int depth = 0) {
    if (depth > 128 || !nodes.TryGetValue(id, out var node)) throw new Exception("Missing/deep anatomy graph");
    if (!node.Definition.StartsWith("Body", StringComparison.Ordinal)) return Total(id, seen);
    if (!seen.Add(id) || duplicate.Contains(id) || !layouts.TryGetValue(id, out var layout) || layout.MassOverride || Definition(node.Definition) is not JObject def || FixedMass(def) is not double tare)
        throw new Exception("Unreviewed anatomy definition/override/graph: " + id);
    double result = tare;
    foreach (string child in children.GetValueOrDefault(id) ?? []) result += RebuildAnatomy(child, seen, depth + 1);
    foreach (string member in node.Stack) result += RebuildAnatomy(member, seen, depth + 1);
    return result;
}
var rows = new List<object>(); int eligible = 0, different = 0;
foreach (var actor in nodes.Values.Where(n => n.Values.GetValueOrDefault("IsHuman") > 0 || n.Definition == "Crew01")) {
    try {
        if (!layouts.TryGetValue(actor.Id, out var layout)) throw new Exception("No saved layout (not an auditable physical instance)");
        if (layout.MassOverride) throw new Exception("Explicit layout mass override");
        if (actor.Definition != "Crew01" || !definitions.TryGetValue(actor.Definition, out var definition) || FixedMass(definition) is not double core) throw new Exception("Unreviewed character definition");
        if (!actor.Values.TryGetValue("StatMass", out double actual)) throw new Exception("No saved aggregate mass");
        double strong = actor.Values.GetValueOrDefault("IsStrong"), feeble = actor.Values.GetValueOrDefault("IsFeeble");
        if ((strong != 0 && strong != 1) || (feeble != 0 && feeble != 1) || strong + feeble > 1) throw new Exception("Invalid/noncanonical trait state");
        double trait = strong * 15 - feeble * 10, bodyRoots = 0, otherRoots = 0;
        var seen = new HashSet<string> { actor.Id }; var parts = new List<object>();
        foreach (string child in children.GetValueOrDefault(actor.Id) ?? []) {
            double total = Total(child, seen); var node = nodes[child];
            if (node.Definition.StartsWith("Body", StringComparison.Ordinal)) bodyRoots += total; else otherRoots += total;
            parts.Add(new { id = child, definition = node.Definition, kg = total, layout = layouts[child] });
        }
        double expected = core + trait + bodyRoots + otherRoots, delta = expected - actual;
        var rebuiltSeen = new HashSet<string> { actor.Id }; double rebuilt = core + trait;
        foreach (string child in children.GetValueOrDefault(actor.Id) ?? []) rebuilt += RebuildAnatomy(child, rebuiltSeen);
        eligible++; if (Math.Abs(delta) > 1e-6) different++;
        rows.Add(new { actor.Id, actor.Definition, layout.Ship, currentKg = actual, coreKg = core, traitKg = trait, anatomicalRootKg = bodyRoots, otherOwnedRootKg = otherRoots, inferredCoreKg = actual - bodyRoots - otherRoots, expectedKg = expected, deltaKg = delta, expectedWithRebuiltAnatomyKg = rebuilt, children = parts });
    } catch (Exception error) { rows.Add(new { actor.Id, actor.Definition, skip = error.Message }); }
}
var selectedIds = args.Length > 2 ? args[2].Split(';').ToHashSet(StringComparer.Ordinal) : [];
if (args.Length > 3) {
    var graph = new HashSet<string>();
    void Gather(string id) {
        if (!graph.Add(id) || !nodes.TryGetValue(id, out var node)) return;
        foreach (string child in children.GetValueOrDefault(id) ?? []) Gather(child);
        foreach (string member in node.Stack) Gather(member);
    }
    foreach (string id in selectedIds) Gather(id);
    string? Owner(string id) {
        var seen = new HashSet<string>();
        while (heads.TryGetValue(id, out string? head)) { if (!seen.Add(id)) throw new Exception("Cyclic stack graph"); id = head; }
        return layouts.GetValueOrDefault(id)?.Parent;
    }
    File.WriteAllText(args[3], JsonConvert.SerializeObject(new { sourceSha256 = before, roots = selectedIds, nodes = graph.Select(id => new {
        id, definition = nodes[id].Definition, values = nodes[id].Values, parent = Owner(id), stack = nodes[id].Stack,
        massOverride = layouts.GetValueOrDefault(id)?.MassOverride == true, unknownDefaults = nodes[id].UnknownDefault
    }) }, Formatting.Indented));
}
if (Hash() != before) throw new Exception("Source save changed during read-only audit");
var report = new { sourceUnchanged = true, sourceSha256 = before, savedRecords = nodes.Count, eligible, different,
    assumption = "Core and current traits are reconstructed; owned child masses are taken as stored, so corrupt cargo remains a separate issue.",
    defaultCoreKg = FixedMass(definitions["Crew01"]), defaultLimbs, defaultCompleteBodyKg = FixedMass(definitions["Crew01"]) + defaultLimbs.Sum(x => x.kg), massTraits, nonOverlayMassLoot = massLoot.Where(x => !x.name.StartsWith("CNDOL", StringComparison.Ordinal)), rows };
File.WriteAllText(Path.Combine(output, "report.json"), JsonConvert.SerializeObject(report, Formatting.Indented));
Console.WriteLine(JsonConvert.SerializeObject(new { report.sourceUnchanged, report.savedRecords, report.eligible, report.different, report.defaultCoreKg, report.defaultCompleteBodyKg, report.massTraits, report.nonOverlayMassLoot,
    definitions = nodes.Values.GroupBy(n => n.Definition).OrderByDescending(g => g.Count()).Take(8).Select(g => new { definition = g.Key, count = g.Count() }),
    selectedCharacter = rows.Where(o => selectedIds.Contains((string?)o.GetType().GetProperty("Id")?.GetValue(o) ?? "")) }, Formatting.Indented));

sealed class Node {
    public string Id = "", Definition = "";
    public bool UnknownDefault;
    public Dictionary<string, double> Values = [];
    public string[] Stack = [], Lot = [];
}
sealed record Layout(string? Parent, string Ship, bool MassOverride);
