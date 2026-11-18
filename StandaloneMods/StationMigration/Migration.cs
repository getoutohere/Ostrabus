using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Ostrabus.StationMigration;

internal sealed class StationPlan
{
    internal JObject Original, Updated;
    internal Dictionary<string, JObject> Records = new Dictionary<string, JObject>();
    internal HashSet<string> Retired = new HashSet<string>();
    internal int Added, Converted, Uninstalled, Moved, Stacked, SupportRestored;
    public override string ToString() => (string)Original["strRegID"] + ": " + Added + " added, " + Converted +
        " kiosk upgrades, " + SupportRestored + " missing support records restored, " + Uninstalled +
        " construction preserved loose, " + Moved + " moved (" + Stacked + " overlapping).";
}

internal static class Migration
{
    internal const string Marker = "1.1.0.1_Scrap_Kiosk_Fix";
    internal const string Fixed = "IsDebug1-1-0-1Fixed";
    internal static readonly string[] CorrectedTargets = { "JATL_SUB", "MSUZ_RB" };
    internal static bool Completed(IEnumerable<string> conditions) => (conditions ?? Array.Empty<string>())
        .Where(value => value.Split('=')[0] == Fixed)
        .Select(value => value.Contains("=") ? Catalog.Amount(value) : 1).LastOrDefault() > 0;
    internal static bool Root(JToken part) => string.IsNullOrEmpty((string)part["strParentID"]) &&
        string.IsNullOrEmpty((string)part["strSlotParentID"]);
    internal static JObject Clone(JObject value) => (JObject)value.DeepClone();
    internal static string Id(JObject part) => (string)part["strID"];
    internal static string Name(JObject part) => (string)part["strName"];
    static float Angle(JObject part) => (float?)part["fRotation"] ?? 0;
    static float X(JObject part) => (float)part["fX"];
    static float Y(JObject part) => (float)part["fY"];
    static bool SameSite(JObject a, JObject b) => Name(a) == Name(b) && Math.Abs(X(a) - X(b)) < .01 &&
        Math.Abs(Y(a) - Y(b)) < .01 && Math.Abs(((Angle(a) - Angle(b) + 540) % 360) - 180) < .01;

    internal static (float X, float Y, int Turns) Alignment(JObject station, JObject manifest)
    {
        int turns = (((int?)station["nGridRotation"] ?? 0) - ((int?)manifest["gridRotation"] ?? 0)) / 90;
        turns = (turns % 4 + 4) % 4;
        var roots = ((JArray)station["aItems"]).OfType<JObject>().Where(Root).GroupBy(Name)
            .Where(group => group.Count() == 1).ToDictionary(group => group.Key, group => group.Single());
        var offsets = ((JArray)manifest["anchors"]).OfType<JObject>().Where(anchor => roots.ContainsKey(Name(anchor)))
            .Select(anchor =>
            {
                var point = Rotate(X(anchor), Y(anchor), turns);
                var current = roots[Name(anchor)];
                return (X: Math.Round(X(current) - point.X, 3), Y: Math.Round(Y(current) - point.Y, 3));
            }).ToArray();
        var consensus = offsets.GroupBy(value => value).OrderByDescending(group => group.Count()).ToArray();
        if (consensus.Length == 0 || consensus[0].Count() < 3 || consensus[0].Count() <= offsets.Length / 2)
            throw new InvalidOperationException((string)station["strRegID"] + ": cannot establish a unique template-to-saved-grid alignment.");
        return ((float)consensus[0].Key.X, (float)consensus[0].Key.Y, turns);
    }

    static (float X, float Y) Rotate(float x, float y, int turns)
    {
        for (int turn = 0; turn < turns; turn++) (x, y) = (y, -x);
        return (x, y);
    }

    internal static JObject Transform(JObject source, (float X, float Y, int Turns) alignment, Catalog assets)
    {
        var result = Clone(source);
        var point = Rotate(X(source), Y(source), alignment.Turns);
        result["fX"] = point.X + alignment.X;
        result["fY"] = point.Y + alignment.Y;
        if ((bool?)assets.Item(Name(source))["bHasSpriteSheet"] != true)
            result["fRotation"] = (Angle(source) - 90 * alignment.Turns + 360) % 360;
        return result;
    }

    internal static StationPlan Plan(JObject station, JObject manifest, Catalog assets, Func<string, JObject> saved, double epoch = 0)
    {
        var plan = new StationPlan { Original = station, Updated = Clone(station) };
        if (epoch <= 0) epoch = (double?)station["shipCO"]?["fEpoch"] ?? (double?)station["shipCO"]?["fLastICOUpdate"] ?? 0;
        if (manifest["kiosk"] == null || manifest["kiosk"].Type == JTokenType.Null)
        {
            Mark(plan.Updated, assets);
            return plan;
        }
        var parts = ((JArray)plan.Updated["aItems"]).OfType<JObject>().ToList();
        var local = (station["aCOs"] as JArray ?? new JArray()).OfType<JObject>()
            .ToDictionary(part => (string)part["strID"]);
        var cache = new Dictionary<string, JObject>();
        JObject OriginalRecord(string id) => local.TryGetValue(id, out var value) ? value : saved(id);
        // Ordinary native SpawnItems skips layout entries lacking a saved CO.
        // Preserve those entries, but do not invent objects or treat their
        // absent geometry as an obstruction to this unrelated migration.
        var roots = parts.Where(Root).Where(part => !assets.Destroyed(part) &&
            (OriginalRecord(Id(part)) != null || Id(part).Contains("MP|"))).ToList();
        JObject Record(JObject part)
        {
            string id = Id(part);
            if (!cache.TryGetValue(id, out var state))
            {
                var original = OriginalRecord(id);
                if (original == null && id.Contains("MP|")) original = new JObject
                {
                    ["strID"] = id, ["strCondID"] = id, ["strCODef"] = Name(part), ["aConds"] = new JArray("DEFAULT"),
                    ["bAlive"] = true, ["strRegIDLast"] = station["strRegID"].DeepClone(),
                    ["fEpoch"] = epoch, ["fEpochRate"] = 1, ["fLastICOUpdate"] = epoch
                };
                if (original == null) throw new InvalidOperationException("Missing saved object " + id + " on " + station["strRegID"]);
                state = Clone(original);
                cache[id] = state;
            }
            if ((string)state["strCODef"] != Name(part))
                throw new InvalidOperationException("Saved object/layout definition mismatch: " + id);
            return state;
        }
        bool Installed(JObject part) => assets.Installed(Name(part), Record(part));
        JObject DefaultRecord(JObject part) => new JObject
        {
            ["strID"] = Id(part), ["strCondID"] = Id(part), ["strCODef"] = Name(part),
            ["bAlive"] = true, ["aConds"] = new JArray("DEFAULT"),
            ["aCondRules"] = new JArray("DEFAULT"), ["strRegIDLast"] = station["strRegID"].DeepClone(),
            ["fEpoch"] = epoch, ["fEpochRate"] = 1, ["fLastICOUpdate"] = epoch
        };
        var alignment = Alignment(station, manifest);
        JObject NewPart(JObject blueprint)
        {
            var part = Clone(blueprint);
            part["strID"] = Guid.NewGuid().ToString();
            part.Remove("strParentID");
            part.Remove("strSlotParentID");
            var state = DefaultRecord(part);
            plan.Records.Add(Id(part), state);
            cache.Add(Id(part), state);
            parts.Add(part);
            roots.Add(part);
            plan.Added++;
            return part;
        }
        Grid StructuralGrid()
        {
            var result = new Grid(assets);
            foreach (var part in roots.Where(Installed)) result.Add(part);
            return result;
        }

        var kiosk = Transform((JObject)manifest["kiosk"], alignment, assets);
        string wanted = Name(kiosk);
        var existing = roots.Where(part => Name(part) == wanted && Installed(part)).ToArray();
        // Respect an already present regional kiosk, including one deliberately
        // relocated by another mod. Never duplicate its inventory or stock.
        if (existing.Length > 0)
        {
            Mark(plan.Updated, assets);
            return plan;
        }

        var replacementSites = ((JArray)manifest["replace"]).OfType<JObject>()
            .Select(source => Transform(source, alignment, assets)).ToArray();
        var oldKiosk = roots.FirstOrDefault(part => Installed(part) && replacementSites.Any(site => SameSite(part, site)));
        if (oldKiosk != null)
        {
            var state = Record(oldKiosk);
            plan.Records[Id(oldKiosk)] = state;
            assets.Convert(state, Name(oldKiosk), wanted);
            oldKiosk["strName"] = wanted;
            oldKiosk["fX"] = kiosk["fX"];
            oldKiosk["fY"] = kiosk["fY"];
            oldKiosk["fRotation"] = kiosk["fRotation"];
            // Native overlay supplies the new Trader map. Preserve unrelated
            // overrides, but discard old trade-table and cache overrides.
            if (oldKiosk["aGPMSettings"] is JArray maps)
                foreach (var map in maps.OfType<JObject>().Where(map => (string)map["strName"] == "Trader").ToArray()) map.Remove();
            kiosk = oldKiosk;
            plan.Converted++;
        }
        else kiosk = NewPart(kiosk);

        var move = new HashSet<JObject>();
        void Uninstall(JObject part)
        {
            var record = Record(part);
            plan.Records[Id(part)] = record;
            string oldName = Name(part), loose = assets.LooseDefinition(oldName);
            assets.Convert(record, oldName, loose);
            part["strName"] = loose;
            if (part["aCondOverrides"] is JArray overrides)
                foreach (var entry in overrides.OfType<JObject>().Where(entry => (string)entry["CondName"] == "IsInstalled").ToArray()) entry.Remove();
            if (assets.Installed(loose, record)) throw new InvalidOperationException("Loose conversion remained installed: " + loose);
            move.Add(part);
            plan.Uninstalled++;
        }

        // These are the exact original rock-wall removals in the new template,
        // not arbitrary construction in the footprint. Modified/replaced
        // construction is handled by the native loose-form conversion below.
        foreach (var source in ((JArray)manifest["remove"]).OfType<JObject>())
        {
            var site = Transform(source, alignment, assets);
            foreach (var part in roots.Where(part => part != kiosk && Installed(part) && SameSite(part, site)).ToArray())
            {
                if (parts.Any(child => (string)child["strParentID"] == Id(part) || (string)child["strSlotParentID"] == Id(part)) ||
                    Catalog.Strings(Record(part)["aLot"]).Length > 0)
                    throw new InvalidOperationException("Authored wall retirement unexpectedly contains items: " + Id(part));
                roots.Remove(part);
                parts.Remove(part);
                plan.Records.Remove(Id(part));
                plan.Retired.Add(Id(part));
            }
        }

        var grid = StructuralGrid();
        var clearance = grid.Clearance(kiosk);
        // Remove only actual incompatible structural contributors. Compatible
        // floors, conduits, lamps and other layers are retained.
        for (int pass = 0; pass < roots.Count + 1; pass++)
        {
            var withoutKiosk = new Grid(assets);
            foreach (var part in roots.Where(part => part != kiosk && Installed(part))) withoutKiosk.Add(part);
            var failures = withoutKiosk.Failures(kiosk).ToArray();
            if (failures.Length == 0) { grid = withoutKiosk; break; }
            var shape = withoutKiosk.Shape(kiosk);
            var blockers = new HashSet<JObject>();
            for (int i = 0; i < shape.Forbids.Length; i++)
            {
                var cell = shape.SocketCell(kiosk, i);
                var forbidden = assets.Socket(shape.Forbids[i]);
                if (!withoutKiosk.Contributors.TryGetValue(cell, out var contributors)) continue;
                foreach (var part in contributors)
                {
                    var pshape = withoutKiosk.Shape(part);
                    int index = Array.IndexOf(pshape.Cells(part).ToArray(), cell);
                    if (forbidden.Overlaps(assets.Socket(pshape.Adds[index]))) blockers.Add(part);
                }
            }
            if (blockers.Count > 0)
            {
                foreach (var blocker in blockers) Uninstall(blocker);
                continue;
            }
            // Restore only the small authored floor/support region necessary
            // for this kiosk if the player removed or replaced its support.
            var supports = ((JArray)manifest["support"]).OfType<JObject>()
                .Select(source => Transform(source, alignment, assets))
                .Where(source =>
                {
                    var supportShape = withoutKiosk.Shape(source);
                    var supportCells = supportShape.Cells(source).ToArray();
                    for (int index = 0; index < shape.Requires.Length; index++)
                    {
                        var cell = shape.SocketCell(kiosk, index);
                        var missing = assets.Socket(shape.Requires[index]).Except(withoutKiosk.At(cell)).ToHashSet();
                        int supportIndex = Array.IndexOf(supportCells, cell);
                        if (supportIndex >= 0 && missing.Overlaps(assets.Socket(supportShape.Adds[supportIndex]))) return true;
                    }
                    return false;
                }).ToArray();
            if (supports.Length == 0) throw new InvalidOperationException("Cannot satisfy authored kiosk support on " + station["strRegID"]);
            foreach (var support in supports)
            {
                var unmaterialized = parts.FirstOrDefault(part => Root(part) && !roots.Contains(part) &&
                    !assets.Destroyed(part) && SameSite(part, support));
                if (unmaterialized == null) { NewPart(support); continue; }
                // Keep an existing layout identity when only its saved record
                // is absent, instead of creating a duplicate at the same site.
                var state = DefaultRecord(unmaterialized);
                cache[Id(unmaterialized)] = state;
                plan.Records[Id(unmaterialized)] = state;
                roots.Add(unmaterialized);
                plan.SupportRestored++;
            }
        }
        if (grid.Failures(kiosk).Any()) throw new InvalidOperationException("Kiosk preflight did not converge on " + station["strRegID"]);

        foreach (var part in roots.Where(part => part != kiosk && !Installed(part)))
            if (grid.Shape(part).Cells(part).Any(clearance.Contains)) move.Add(part);
        // Structural floors are compatible with loose items. Only loose roots
        // reserve footprint occupancy; sockets check structural obstructions.
        var occupied = roots.Where(part => !Installed(part) && !move.Contains(part))
            .SelectMany(part => grid.Shape(part).Cells(part)).ToHashSet();
        foreach (var part in move.OrderBy(Id))
        {
            var shape = grid.Shape(part);
            var candidates = grid.Floors.Where(cell => Math.Abs(cell.X - X(part)) + Math.Abs(cell.Y - Y(part)) <= 8)
                .OrderBy(cell => Math.Abs(cell.X - X(part)) + Math.Abs(cell.Y - Y(part)))
                .ThenBy(cell => cell.Y).ThenBy(cell => cell.X).ToArray();
            bool placed = false;
            // Retry allowing overlap with loose items. Structural obstacles,
            // kiosk/access cells and missing floor remain excluded.
            foreach (bool stack in new[] { false, true })
            {
                foreach (var cell in candidates)
                {
                    var candidate = Clone(part);
                    candidate["fX"] = cell.X + (shape.Width % 2 == 0 ? .5 : 0);
                    candidate["fY"] = cell.Y + (shape.Height % 2 == 0 ? .5 : 0);
                    var cells = shape.Cells(candidate).ToArray();
                    if (cells.Any(clearance.Contains) || !stack && cells.Any(occupied.Contains) ||
                        cells.Any(position => !grid.At(position).Contains("IsFloor") || grid.At(position).Contains("IsObstruction")) ||
                        grid.Failures(candidate).Any()) continue;
                    part["fX"] = candidate["fX"];
                    part["fY"] = candidate["fY"];
                    occupied.UnionWith(cells);
                    plan.Moved++;
                    if (stack) plan.Stacked++;
                    placed = true;
                    break;
                }
                if (placed) break;
            }
            if (!placed) throw new InvalidOperationException("No floor-backed destination even with overlapping loose items on " + station["strRegID"]);
        }
        plan.Updated["aItems"] = new JArray(parts);
        if (plan.Updated["aCOs"] is JArray localRecords)
            plan.Updated["aCOs"] = new JArray(localRecords.OfType<JObject>().Where(record => !plan.Retired.Contains((string)record["strID"]))
                .Select(record => plan.Records.TryGetValue((string)record["strID"], out var updated) ? Clone(updated) : record));
        Mark(plan.Updated, assets);
        return plan;
    }

    static void Mark(JObject station, Catalog assets)
    {
        var state = (JObject)station["shipCO"] ?? throw new InvalidOperationException("Missing station CO");
        var conditions = Catalog.Strings(state["aConds"]).Where(value => value.Split('=')[0] != Fixed).ToList();
        conditions.Add(Fixed + "=1.0x1");
        state["aConds"] = new JArray(conditions);
    }
}
