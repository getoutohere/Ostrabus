using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ostrabus.MiscBugFixes;

if (args.Length == 0) throw new ArgumentException("Usage: MassAudit <save.zip> [output-directory]");
string save = Path.GetFullPath(args[0]);
string output = Path.GetFullPath(args.Length > 1 ? args[1] : "obj/MASSFIX-COVERAGE");
Directory.CreateDirectory(output);
string Hash() { using var stream = File.OpenRead(save); return Convert.ToHexString(SHA256.HashData(stream)); }
string beforeHash = Hash();
var definitions = new Dictionary<string, JObject>();
string assets = Ostrabus.Development.InstallationPaths.Data + "condowners";
foreach (string path in Directory.GetFiles(assets, "*.json").Order()) foreach (JObject definition in JArray.Parse(File.ReadAllText(path)))
    definitions[(string)definition["strName"]!] = definition;
var nodes = new Dictionary<string, Node>();
using var zip = ZipFile.OpenRead(save);
var main = zip.GetEntry("Ayden Kelly.json") ?? throw new Exception("Main save entry missing");
using (var text = new StreamReader(main.Open())) using (var reader = new JsonTextReader(text)) {
    while (reader.Read()) {
        if (reader.TokenType != JsonToken.PropertyName || (string?)reader.Value != "aCOs") continue;
        reader.Read();
        while (reader.Read() && reader.TokenType != JsonToken.EndArray) {
            var co = JObject.Load(reader);
            string id = (string)co["strID"]!, def = (string)co["strCODef"]!;
            string[] conditions = co["aConds"]?.ToObject<string[]>() ?? [];
            if (conditions.Contains("DEFAULT") && definitions.TryGetValue(def, out var defaults))
                conditions = conditions.Where(x => x != "DEFAULT").Concat(defaults["aStartingConds"]?.ToObject<string[]>() ?? []).ToArray();
            double? mass = null;
            foreach (string equation in conditions.Where(x => x.StartsWith("StatMass=", StringComparison.Ordinal))) {
                int x = equation.IndexOf('x');
                if (x >= 0 && double.TryParse(equation[(x + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out double value)) mass = value;
            }
            nodes[id] = new Node { Id = id, Def = def, Mass = mass, Original = mass,
                Conditions = conditions.Select(x => x.Split('=')[0]).ToArray(),
                Stack = co["aStack"]?.ToObject<string[]>() ?? [], Lot = co["aLot"]?.ToObject<string[]>() ?? [] };
        }
        break;
    }
}
var layouts = new Dictionary<string, Layout>();
var duplicates = new HashSet<string>();
foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith("ships/") && e.FullName.EndsWith(".json"))) {
    using var text = new StreamReader(entry.Open()); using var reader = new JsonTextReader(text);
    while (reader.Read()) {
        if (reader.TokenType != JsonToken.PropertyName || (string?)reader.Value != "aItems") continue;
        reader.Read();
        while (reader.Read() && reader.TokenType != JsonToken.EndArray) {
            var item = JObject.Load(reader);
            string? id = (string?)item["strID"];
            if (id == null) continue;
            if (layouts.ContainsKey(id)) duplicates.Add(id);
            bool massOverride = item["aCondOverrides"] is JArray overrides && overrides.Any(x => (string?)x["CondName"] == "StatMass");
            layouts[id] = new Layout((string?)item["strParentID"] ?? (string?)item["strSlotParentID"], entry.FullName, massOverride);
        }
        break;
    }
}
var children = new Dictionary<string, List<string>>();
foreach (var item in layouts) if (item.Value.Parent is string parent) {
    if (!children.TryGetValue(parent, out var list)) children[parent] = list = [];
    list.Add(item.Key);
}
var stackHeads = new Dictionary<string, string>();
var lotMembers = new HashSet<string>();
foreach (var node in nodes.Values) {
    foreach (string member in node.Stack) stackHeads[member] = node.Id;
    foreach (string member in node.Lot) lotMembers.Add(member);
}
string? Parent(string id) {
    // Stack members share the head's actual owner; layout parent IDs instead encode the stack.
    int count = 0;
    while (stackHeads.TryGetValue(id, out string? head)) { id = head; if (++count > 128) throw new Exception("cyclic stack owners"); }
    return layouts.TryGetValue(id, out var layout) ? layout.Parent : null;
}
List<Node> Ancestors(Node node) {
    var result = new List<Node>(); var seen = new HashSet<string>(); string? id = node.Id;
    while (id != null) {
        if (seen.Count >= 128 || !seen.Add(id) || !nodes.TryGetValue(id, out var current)) throw new Exception("missing or cyclic parent graph");
        result.Add(current); id = Parent(id);
    }
    return result;
}
double Cargo(string id, HashSet<string> seen, int depth = 0) {
    if (depth > 128 || seen.Count >= 10000 || !seen.Add(id) || !nodes.TryGetValue(id, out var node) || node.Mass is not double mass || !MassRepairRules.Finite(mass) || mass < 0)
        throw new Exception("missing/invalid cargo mass or stack graph");
    foreach (string member in node.Stack) mass += Cargo(member, seen, depth + 1);
    return mass;
}
var skipped = new Dictionary<string, string>();
var eligible = new Dictionary<string, double>();
int candidates = 0;
foreach (var node in nodes.Values) {
    if (!node.Conditions.Contains("IsContainer")) continue;
    candidates++;
    string? error = null;
    if (layouts.TryGetValue(node.Id, out var layout) && layout.MassOverride) error = "explicit layout mass override";
    else if (!definitions.TryGetValue(node.Def, out var def)) error = "missing definition or unaudited overlay";
    else if (string.IsNullOrEmpty((string?)def["strContainerCT"])) error = "no inventory container component in definition";
    else if (MassRepairRules.TryTare((string?)def["strType"], (string?)def["strLoot"], def["aStartingConds"]?.ToObject<string[]>(), node.Conditions, out double tare, out error)) {
        eligible[node.Id] = tare;
    }
    if (error != null) skipped[node.Id] = error;
}
var ordered = eligible.Keys.Select(id => {
    try { return (Id: id, Depth: Ancestors(nodes[id]).Count); } catch (Exception e) { skipped[id] = e.Message; return (Id: id, Depth: -1); }
}).OrderByDescending(x => x.Depth).ToArray();
int directChanges = 0;
foreach (var pair in ordered.Where(x => x.Depth >= 0)) {
    var node = nodes[pair.Id];
    try {
        if (!layouts.ContainsKey(node.Id) || duplicates.Contains(node.Id)) throw new Exception("missing or duplicate layout instance");
        double expected = eligible[node.Id]; var seen = new HashSet<string> { node.Id };
        if (children.TryGetValue(node.Id, out var cargo)) foreach (string id in cargo) {
            if (lotMembers.Contains(id) || stackHeads.ContainsKey(id) || seen.Contains(id)) continue;
            expected += Cargo(id, seen);
        }
        if (node.Mass is not double current || !MassRepairRules.Finite(current) || !MassRepairRules.Finite(expected)) throw new Exception("nonfinite/missing container mass");
        double delta = expected - current;
        var ancestors = Ancestors(node);
        if (ancestors.Any(a => a.Mass is not double m || !MassRepairRules.Finite(m + delta) || m + delta < 0)) throw new Exception("invalid resulting parent mass");
        if (Math.Abs(delta) < 0.000001) continue;
        foreach (var ancestor in ancestors) ancestor.Mass += delta;
        directChanges++;
    } catch (Exception error) { skipped[node.Id] = error.Message; }
}
var covered = eligible.Keys.Where(id => !skipped.ContainsKey(id)).ToArray();
var changes = covered.Where(id => Math.Abs(nodes[id].Mass!.Value - nodes[id].Original!.Value) >= 0.000001).Select(id => new {
    id, definition = nodes[id].Def, ship = layouts[id].Ship, beforeKg = nodes[id].Original, afterKg = nodes[id].Mass
}).ToArray();
string afterHash = Hash();
if (beforeHash != afterHash) throw new Exception("Source save changed during read-only audit");
var report = new {
    save, sourceSha256 = beforeHash, sourceUnchanged = true, savedCOs = nodes.Count, candidates,
    covered = covered.Length, skipped = skipped.Count, finalMassChanges = changes.Length, directAdjustments = directChanges,
    scope = "All saved containers, including unloaded ships; predicts a normal loaded-state repair. Live massfix only touches instantiated world containers.",
    skipReasons = skipped.GroupBy(p => p.Value).Select(g => new { reason = g.Key, count = g.Count() }).OrderByDescending(x => x.count).ToArray(),
    skipTypes = skipped.GroupBy(p => (nodes[p.Key].Def, p.Value)).Select(g => new { definition = g.Key.Def, reason = g.Key.Value, count = g.Count() }).OrderByDescending(x => x.count).ToArray(),
    coverageTypes = covered.GroupBy(id => nodes[id].Def).Select(g => new { definition = g.Key, count = g.Count() }).OrderByDescending(x => x.count).ToArray(),
    changes
};
File.WriteAllText(output + "/report.json", JsonConvert.SerializeObject(report, Formatting.Indented));
var lines = new List<string> { "# massfix offline save coverage", "", $"Save: `{save}`", "", "Read-only audit of all saved containers, including unloaded inventories. Live command coverage is limited to instantiated world objects.", "", $"- Saved COs: {nodes.Count}", $"- Container candidates: {candidates}", $"- Covered: {covered.Length}", $"- Skipped: {skipped.Count}", $"- Covered containers with changed final mass: {changes.Length}", $"- Direct adjustments in simulated deepest-first order: {directChanges}", "", "## Skips", "" };
foreach (var reason in report.skipReasons) lines.Add($"- {reason.count}: {reason.reason}");
lines.AddRange(["", "Full per-type counts and changes: `report.json`.", "", $"Source SHA256 unchanged: `{beforeHash}`."]);
File.WriteAllLines(output + "/README.md", lines);
Console.WriteLine(JsonConvert.SerializeObject(new { candidates, covered = covered.Length, skipped = skipped.Count, finalMassChanges = changes.Length, sourceUnchanged = true, report.skipReasons }, Formatting.Indented));

sealed class Node {
    public string Id = "", Def = "";
    public double? Mass, Original;
    public string[] Conditions = [], Stack = [], Lot = [];
}
sealed record Layout(string? Parent, string Ship, bool MassOverride);
