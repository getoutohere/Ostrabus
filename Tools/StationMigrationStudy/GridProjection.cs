using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Detached projection of authored item/socket grids. This neither edits a save
// nor substitutes for final native placement/room/docking checks in Unity.
internal sealed class GridProjection
{
    internal sealed record Input(string Id, JObject Before, JObject After, JObject Saved);
    readonly record struct Cell(int X, int Y);
    sealed record Shape(int Width, int Height, string[] Adds, string[] Requires, string[] Forbids);
    sealed record Part(JObject Item, Shape Shape, bool Installed, bool SavedStateKnown)
    {
        internal string Id => (string)Item["strID"]!;
        internal string Definition => (string)Item["strName"]!;
    }

    readonly Dictionary<string, JObject> definitions;
    readonly Dictionary<string, JObject> overlays;
    readonly Dictionary<string, JObject> items;
    readonly Dictionary<string, JObject> loot;
    readonly Dictionary<string, HashSet<string>> socketCache = new(StringComparer.Ordinal);
    readonly Dictionary<string, JObject> states;
    int assertions;

    // Saved interiors may have been moved/rotated by native Ship.MoveShip /
    // RotateCW. Infer translation from several unique, unchanged installed
    // fixtures; never infer it from a bounding box altered by construction.
    (Input Aligned, JObject Evidence) Align(Input input)
    {
        var before = Parts(input.Before, false);
        var saved = Parts(input.Saved, true);
        int turns = (((int?)input.Saved["nGridRotation"] ?? 0) -
            ((int?)input.Before["nGridRotation"] ?? 0)) / 90;
        turns = (turns % 4 + 4) % 4;
        (double X, double Y) RotatePoint(double x, double y)
        {
            for (int n = 0; n < turns; n++)
                (x, y) = (y, -x);
            return (x, y);
        }
        var uniqueBefore = before.Where(part => part.Installed).GroupBy(part => part.Definition)
            .Where(group => group.Count() == 1).ToDictionary(group => group.Key, group => group.Single());
        var anchors = saved.Where(part => part.Installed).GroupBy(part => part.Definition)
            .Where(group => group.Count() == 1 && uniqueBefore.ContainsKey(group.Key))
            .Select(group =>
            {
                var old = uniqueBefore[group.Key];
                var now = group.Single();
                var rotated = RotatePoint((double)old.Item["fX"]!, (double)old.Item["fY"]!);
                return new { Name = group.Key, X = (double)now.Item["fX"]! - rotated.X,
                    Y = (double)now.Item["fY"]! - rotated.Y };
            }).ToArray();
        var consensus = anchors.GroupBy(anchor => (Math.Round(anchor.X, 3), Math.Round(anchor.Y, 3)))
            .OrderByDescending(group => group.Count()).First();
        Check(consensus.Count() >= 3 && consensus.Count() > anchors.Length / 2,
            input.Id + ": no unambiguous interior alignment; refusing to project placement.");
        double dx = consensus.Average(anchor => anchor.X), dy = consensus.Average(anchor => anchor.Y);
        JObject Transform(JObject template)
        {
            var copy = (JObject)template.DeepClone();
            foreach (var item in ((JArray)copy["aItems"]!).OfType<JObject>().Where(Root))
            {
                var point = RotatePoint((double)item["fX"]!, (double)item["fY"]!);
                item["fX"] = Math.Round(point.X + dx, 3);
                item["fY"] = Math.Round(point.Y + dy, 3);
                // Sprite-sheet items keep their native item rotation unchanged.
                var definition = Definition((string)item["strName"]!);
                if ((bool?)items[(string)definition["strItemDef"]!]["bHasSpriteSheet"] != true)
                    item["fRotation"] = (((double?)item["fRotation"] ?? 0) - 90 * turns + 360) % 360;
            }
            return copy;
        }
        return (input with { Before = Transform(input.Before), After = Transform(input.After) },
            new JObject { ["clockwiseQuarterTurns"] = turns, ["offsetX"] = dx, ["offsetY"] = dy,
                ["agreeingUniqueFixtures"] = consensus.Count(), ["uniqueFixturesCompared"] = anchors.Length });
    }

    internal GridProjection(string data, Dictionary<string, JObject> states)
    {
        definitions = Load(data, "condowners/condowners.json");
        overlays = Load(data, "cooverlays/cooverlays.json");
        items = Load(data, "items/items.json");
        loot = Load(data, "loot/loot.json");
        this.states = states;
    }

    static Dictionary<string, JObject> Load(string root, string file) =>
        Directory.GetFiles(Path.Combine(root, Path.GetDirectoryName(file)!), "*.json")
            .Order(StringComparer.Ordinal)
            .SelectMany(path => JArray.Parse(File.ReadAllText(path)).OfType<JObject>())
            .GroupBy(row => (string)row["strName"]!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);

    JObject Definition(string name)
    {
        for (int depth = 0; depth < 32; depth++)
        {
            if (definitions.TryGetValue(name, out var result))
                return result;
            if (!overlays.TryGetValue(name, out var overlay))
                throw new InvalidDataException("Unknown CO definition: " + name);
            name = (string)overlay["strCOBase"]!;
        }
        throw new InvalidDataException("Overlay cycle: " + name);
    }

    static string[] Strings(JToken? token) => token is JArray array ? array.Values<string>().Select(value => value!).ToArray() : [];

    Shape Geometry(JObject item)
    {
        var definition = Definition((string)item["strName"]!);
        var layout = items[(string)definition["strItemDef"]!];
        int width = (int?)layout["nCols"] ?? 1;
        string[] adds = Strings(layout["aSocketAdds"]);
        int height = width > 0 ? adds.Length / width : 0;
        if (width <= 0 || height <= 0)
            throw new InvalidDataException("Invalid native item dimensions: " + item["strName"]);
        // Native Item.SetData uses integer division; RotateTilesCW likewise
        // ignores an incomplete final row (e.g. ItmToolBox02 has 3 adds/2 cols).
        adds = adds.Take(width * height).ToArray();
        string[] requires = Strings(layout["aSocketReqs"]);
        string[] forbids = Strings(layout["aSocketForbids"]);
        double angle = (double?)item["fRotation"] ?? 0;
        int turns = ((int)Math.Round(-angle / 90) % 4 + 4) % 4;
        if ((bool?)layout["bHasSpriteSheet"] == true)
            turns = 0;
        for (int turn = 0; turn < turns; turn++)
        {
            adds = Rotate(adds, width);
            requires = Rotate(requires, width + 2);
            forbids = Rotate(forbids, width + 2);
            (width, height) = (height, width);
        }
        return new Shape(width, height, adds, requires, forbids);
    }

    // Same row-major permutation as native TileUtils.RotateTilesCW<T>.
    static string[] Rotate(string[] source, int width)
    {
        if (source.Length == 0)
            return source;
        if (source.Length % width != 0)
            throw new InvalidDataException("Invalid socket-grid dimensions.");
        int height = source.Length / width;
        var result = new string[source.Length];
        int index = 0;
        for (int x = 0; x < width; x++)
            for (int y = height - 1; y >= 0; y--)
                result[index++] = source[y * width + x];
        return result;
    }

    HashSet<string> Socket(string name)
    {
        if (socketCache.TryGetValue(name, out var cached))
            return cached;
        var result = new HashSet<string>(StringComparer.Ordinal);
        // Add to the cache only once fully resolved; cyclic/random sockets are
        // unsupported rather than silently modeled as empty.
        ResolveSocket(name, result, new HashSet<string>(StringComparer.Ordinal));
        socketCache[name] = result;
        return result;
    }

    void ResolveSocket(string name, HashSet<string> output, HashSet<string> path)
    {
        if (!path.Add(name))
            throw new InvalidDataException("Socket loot cycle: " + name);
        var table = loot[name];
        foreach (string equation in Strings(table["aCOs"]))
        {
            var parts = equation.Split('=');
            if (parts.Length > 1 && parts[1] != "1.0x1" && parts[1] != "1.0x1.0")
                throw new InvalidDataException("Nontrivial socket equation: " + equation);
            output.Add(parts[0]);
        }
        foreach (string equation in Strings(table["aLoots"]))
        {
            var parts = equation.Split('=');
            if (parts.Length > 1 && parts[1] != "1.0x1" && parts[1] != "1.0x1.0")
                throw new InvalidDataException("Nontrivial nested socket equation: " + equation);
            ResolveSocket(parts[0], output, path);
        }
        path.Remove(name);
    }

    static bool Root(JToken item) => item["strParentID"]?.Type is null or JTokenType.Null &&
        item["strSlotParentID"]?.Type is null or JTokenType.Null;

    Part[] Parts(JObject ship, bool saved)
    {
        return ((JArray)ship["aItems"]!).OfType<JObject>().Where(Root).Select(item =>
        {
            var definition = Definition((string)item["strName"]!);
            JObject? state = null;
            bool known = !saved || states.TryGetValue((string)item["strID"]!, out state);
            var conditions = saved && state != null ? Strings(state["aConds"]).ToList() : Strings(definition["aStartingConds"]).ToList();
            if (conditions.Remove("DEFAULT"))
                conditions.AddRange(Strings(definition["aStartingConds"]));
            double installed = 0;
            foreach (string equation in conditions)
            {
                if (equation == "IsInstalled")
                    installed++;
                else if (equation.StartsWith("IsInstalled=", StringComparison.Ordinal))
                {
                    string amount = equation[(equation.LastIndexOf('x') + 1)..];
                    if (!double.TryParse(amount, NumberStyles.Float, CultureInfo.InvariantCulture, out installed))
                        throw new InvalidDataException("Unsupported installed-state equation: " + equation);
                }
            }
            return new Part(item, Geometry(item), installed > 0, known);
        }).ToArray();
    }

    static string Key(Part part) => string.Join("|", part.Definition,
        Math.Round((double?)part.Item["fX"] ?? 0, 3), Math.Round((double?)part.Item["fY"] ?? 0, 3),
        Math.Round(((double?)part.Item["fRotation"] ?? 0) % 360, 3));
    static Cell Origin(Part part) => new(
        (int)Math.Round((double)part.Item["fX"]! - (part.Shape.Width - 1) / 2d, MidpointRounding.AwayFromZero),
        (int)Math.Round((double)part.Item["fY"]! + (part.Shape.Height - 1) / 2d, MidpointRounding.AwayFromZero));
    static IEnumerable<Cell> Cells(Part part)
    {
        var start = Origin(part);
        for (int y = 0; y < part.Shape.Height; y++)
            for (int x = 0; x < part.Shape.Width; x++)
                yield return new Cell(start.X + x, start.Y - y);
    }

    Dictionary<Cell, HashSet<string>> Grid(IEnumerable<Part> parts)
    {
        var grid = new Dictionary<Cell, HashSet<string>>();
        foreach (var part in parts.Where(part => part.Installed))
        {
            int index = 0;
            foreach (var cell in Cells(part))
            {
                if (!grid.TryGetValue(cell, out var flags))
                    grid[cell] = flags = new HashSet<string>(StringComparer.Ordinal);
                flags.UnionWith(Socket(part.Shape.Adds[index++]));
            }
        }
        return grid;
    }

    List<JObject> Fit(Part part, Dictionary<Cell, HashSet<string>> grid)
    {
        var failures = new List<JObject>();
        var origin = Origin(part);
        for (int i = 0; i < part.Shape.Requires.Length; i++)
        {
            int width = part.Shape.Width + 2;
            var cell = new Cell(origin.X - 1 + i % width, origin.Y + 1 - i / width);
            var requires = Socket(part.Shape.Requires[i]);
            var forbids = Socket(part.Shape.Forbids[i]);
            var flags = grid.GetValueOrDefault(cell) ?? [];
            if (!requires.IsSubsetOf(flags) || forbids.Overlaps(flags))
                failures.Add(new JObject
                {
                    ["x"] = cell.X, ["y"] = cell.Y,
                    ["missing"] = new JArray(requires.Except(flags)),
                    ["forbidden"] = new JArray(forbids.Intersect(flags))
                });
        }
        return failures;
    }

    HashSet<Cell> Clearance(Part kiosk)
    {
        var cells = Cells(kiosk).ToHashSet();
        var origin = Origin(kiosk);
        for (int i = 0; i < kiosk.Shape.Forbids.Length; i++)
            if (Socket(kiosk.Shape.Forbids[i]).Contains("IsItemTile"))
                cells.Add(new Cell(origin.X - 1 + i % (kiosk.Shape.Width + 2), origin.Y + 1 - i / (kiosk.Shape.Width + 2)));
        return cells;
    }

    static bool ClearFloor(Cell cell, Dictionary<Cell, HashSet<string>> grid) =>
        grid.TryGetValue(cell, out var flags) && flags.Contains("IsFloor") && !flags.Contains("IsObstruction");

    static HashSet<Cell> ConnectedFloor(Part part, Dictionary<Cell, HashSet<string>> grid)
    {
        var found = Cells(part).Where(cell => ClearFloor(cell, grid)).ToHashSet();
        var pending = new Queue<Cell>(found);
        while (pending.TryDequeue(out var cell))
            foreach (var neighbor in new[] { new Cell(cell.X - 1, cell.Y), new Cell(cell.X + 1, cell.Y),
                new Cell(cell.X, cell.Y - 1), new Cell(cell.X, cell.Y + 1) })
                if (ClearFloor(neighbor, grid) && found.Add(neighbor))
                    pending.Enqueue(neighbor);
        return found;
    }

    JObject Analyze(Input input, bool injectLoose, bool injectWall)
    {
        var current = (JObject)input.Saved.DeepClone();
        string beforeJson = input.Saved.ToString(Formatting.None);
        var before = Parts(input.Before, false);
        var updated = Parts(input.After, false);
        var newKiosk = updated.Single(part => part.Definition == "ItmKiosk" + input.Id + "Scrap01");
        var currentParts = Parts(current, true);
        var updatedKeys = updated.Select(Key).ToHashSet(StringComparer.Ordinal);
        var removedKeys = before.Where(part => !updatedKeys.Contains(Key(part))).Select(Key).ToHashSet(StringComparer.Ordinal);
        var retired = currentParts.Where(part => part.Installed && removedKeys.Contains(Key(part))).ToArray();
        var clearance = Clearance(newKiosk);
        var kioskCell = Cells(newKiosk).First();
        if (injectLoose)
        {
            var source = currentParts.First(part => !part.Installed && part.SavedStateKnown && part.Shape.Width == 1 && part.Shape.Height == 1);
            var clone = (JObject)source.Item.DeepClone();
            clone["strID"] = "MODEL-LOOSE";
            clone["fX"] = kioskCell.X;
            clone["fY"] = kioskCell.Y;
            states["MODEL-LOOSE"] = (JObject)states[source.Id].DeepClone();
            ((JArray)current["aItems"]!).Add(clone);
            currentParts = Parts(current, true);
        }
        if (injectWall)
        {
            var source = before.First(part => part.Installed && part.Shape.Width == 1 && part.Shape.Height == 1 && Socket(part.Shape.Adds[0]).Contains("IsWall"));
            var clone = (JObject)source.Item.DeepClone();
            clone["strID"] = "MODEL-PLAYER-WALL";
            // Use the center of the fixture, not one of BCER's authored removals.
            clone["fX"] = (double)newKiosk.Item["fX"]!;
            clone["fY"] = (double)newKiosk.Item["fY"]!;
            states["MODEL-PLAYER-WALL"] = new JObject { ["aConds"] = new JArray("IsInstalled=1.0x1") };
            ((JArray)current["aItems"]!).Add(clone);
            currentParts = Parts(current, true);
        }

        var remaining = currentParts.Where(part => !retired.Any(old => old.Id == part.Id)).ToArray();
        var grid = Grid(remaining);
        var failures = Fit(newKiosk, grid);
        var controlGrid = Grid(updated.Where(part => part.Id != newKiosk.Id));
        var controlFailures = Fit(newKiosk, controlGrid);
        Check(controlFailures.Count == 0, input.Id + ": projection rejects the authored updated template: " + new JArray(controlFailures));

        var obstructingLoose = remaining.Where(part => !part.Installed && Cells(part).Any(clearance.Contains)).ToArray();
        var moves = new JArray();
        if (failures.Count == 0)
        {
            var occupied = remaining.Where(part => !part.Installed && !obstructingLoose.Contains(part)).SelectMany(Cells).ToHashSet();
            var reserved = clearance.ToHashSet();
            foreach (var part in obstructingLoose)
            {
                var connected = ConnectedFloor(part, grid);
                var candidates = connected
                    .OrderBy(cell => Math.Abs(cell.X - (double)part.Item["fX"]!) + Math.Abs(cell.Y - (double)part.Item["fY"]!))
                    .ThenBy(cell => cell.Y).ThenBy(cell => cell.X);
                JObject? destination = null;
                foreach (var cell in candidates)
                {
                    var item = (JObject)part.Item.DeepClone();
                    item["fX"] = cell.X + (part.Shape.Width % 2 == 0 ? .5 : 0);
                    item["fY"] = cell.Y + (part.Shape.Height % 2 == 0 ? .5 : 0);
                    var proposed = part with { Item = item };
                    var cells = Cells(proposed).ToArray();
                    if (cells.Any(reserved.Contains) || cells.Any(occupied.Contains) ||
                        cells.Any(position => !connected.Contains(position)) || Fit(proposed, grid).Count != 0)
                        continue;
                    reserved.UnionWith(cells);
                    destination = new JObject { ["x"] = item["fX"], ["y"] = item["fY"] };
                    break;
                }
                moves.Add(new JObject
                {
                    ["id"] = part.Id, ["definition"] = part.Definition,
                    ["from"] = new JObject { ["x"] = part.Item["fX"], ["y"] = part.Item["fY"] },
                    ["to"] = destination
                });
            }
        }
        Check(input.Saved.ToString(Formatting.None) == beforeJson, "Analysis modified saved layout input.");
        return new JObject
        {
            ["station"] = input.Id, ["fixture"] = newKiosk.Definition,
            ["physicalCells"] = new JArray(Cells(newKiosk).Select(cell => new JObject { ["x"] = cell.X, ["y"] = cell.Y })),
            ["missingSavedStates"] = currentParts.Count(part => !part.SavedStateKnown),
            ["authoredRetirementsPresent"] = new JArray(retired.Select(part => new JObject { ["id"] = part.Id, ["definition"] = part.Definition })),
            ["fitFailuresAfterAuthoredRetirements"] = new JArray(failures),
            ["intersectingLooseCount"] = obstructingLoose.Length,
            ["looseMoves"] = moves,
            ["status"] = currentParts.Any(part => !part.SavedStateKnown) ? "unknown-saved-state" :
                failures.Count > 0 ? "structural-conflict" : moves.OfType<JObject>().Any(move => move["to"]!.Type == JTokenType.Null) ? "no-clear-relocation" : "projected-fit"
        };
    }

    void Check(bool condition, string message)
    {
        assertions++;
        if (!condition)
            throw new InvalidOperationException(message);
    }

    internal JObject Run(IEnumerable<Input> inputs)
    {
        var results = new JArray();
        foreach (var original in inputs)
        {
            var (input, alignment) = Align(original);
            var actual = Analyze(input, false, false);
            actual["alignment"] = alignment;
            results.Add(actual);
            var withWall = Analyze(input, false, true);
            Check((string?)withWall["status"] == "structural-conflict", "Player-added wall was overwritten by the projected migration.");
            // Inject loose items into the authored pre-upgrade layout so an
            // unrelated real player edit cannot hide the relocation branch.
            var pristine = input with { Saved = (JObject)input.Before.DeepClone() };
            foreach (var item in ((JArray)pristine.Saved["aItems"]!).OfType<JObject>().Where(Root))
                states[(string)item["strID"]!] = new JObject { ["aConds"] = new JArray("DEFAULT") };
            var withLoose = Analyze(pristine, true, false);
            Check((string?)withLoose["status"] == "projected-fit", "Loose items could not be relocated in the authored original station.");
            Check(((JArray)withLoose["looseMoves"]!).OfType<JObject>().Any(move => (string?)move["id"] == "MODEL-LOOSE"),
                "Injected loose item did not receive a relocation plan.");
            results.Add(new JObject { ["station"] = input.Id, ["syntheticLooseRelocation"] = withLoose, ["syntheticStructuralConflict"] = withWall });
        }
        return new JObject
        {
            ["scope"] = "Detached deterministic socket projection. Final native placement, dynamic damage, room, docking-boundary and item-conversion validation still required. Moves never mutate the input ZIP.",
            ["assertions"] = assertions, ["results"] = results
        };
    }
}
