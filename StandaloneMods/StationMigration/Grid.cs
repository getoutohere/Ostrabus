using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Ostrabus.StationMigration;

internal readonly struct Cell : IEquatable<Cell>
{
    internal readonly int X, Y;
    internal Cell(int x, int y) { X = x; Y = y; }
    public bool Equals(Cell other) => X == other.X && Y == other.Y;
    public override bool Equals(object obj) => obj is Cell other && Equals(other);
    public override int GetHashCode() => unchecked(X * 397 ^ Y);
}

internal sealed class Footprint
{
    internal int Width, Height;
    internal string[] Adds, Requires, Forbids;

    internal Footprint(JObject layout, float angle)
    {
        Width = (int?)layout["nCols"] ?? 1;
        Adds = Catalog.Strings(layout["aSocketAdds"]);
        Height = Width > 0 ? Adds.Length / Width : 0;
        if (Height <= 0) throw new InvalidOperationException("Invalid native item grid: " + layout["strName"]);
        Adds = Adds.Take(Width * Height).ToArray();
        Requires = Catalog.Strings(layout["aSocketReqs"]);
        Forbids = Catalog.Strings(layout["aSocketForbids"]);
        int turns = (bool?)layout["bHasSpriteSheet"] == true ? 0 : ((int)Math.Round(-angle / 90) % 4 + 4) % 4;
        for (int turn = 0; turn < turns; turn++)
        {
            Adds = Rotate(Adds, Width);
            Requires = Rotate(Requires, Width + 2);
            Forbids = Rotate(Forbids, Width + 2);
            (Width, Height) = (Height, Width);
        }
    }

    internal static string[] Rotate(string[] source, int width)
    {
        var result = new List<string>();
        for (int x = 0; x < width; x++)
            for (int y = source.Length / width - 1; y >= 0; y--)
                result.Add(source[y * width + x]);
        return result.ToArray();
    }

    internal Cell Origin(JObject part) => new Cell(
        (int)Math.Round((double)part["fX"] - (Width - 1) / 2d, MidpointRounding.AwayFromZero),
        (int)Math.Round((double)part["fY"] + (Height - 1) / 2d, MidpointRounding.AwayFromZero));
    internal IEnumerable<Cell> Cells(JObject part)
    {
        var origin = Origin(part);
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++) yield return new Cell(origin.X + x, origin.Y - y);
    }
    internal Cell SocketCell(JObject part, int index)
    {
        var origin = Origin(part);
        return new Cell(origin.X - 1 + index % (Width + 2), origin.Y + 1 - index / (Width + 2));
    }
}

internal sealed class Grid
{
    readonly Catalog assets;
    readonly Dictionary<Cell, HashSet<string>> flags = new Dictionary<Cell, HashSet<string>>();
    internal readonly Dictionary<Cell, List<JObject>> Contributors = new Dictionary<Cell, List<JObject>>();

    internal Grid(Catalog assets) { this.assets = assets; }
    internal HashSet<string> At(Cell cell) => flags.TryGetValue(cell, out var value) ? value : new HashSet<string>();
    internal void Add(JObject part)
    {
        var shape = Shape(part);
        int index = 0;
        foreach (var cell in shape.Cells(part))
        {
            if (!flags.TryGetValue(cell, out var value)) flags[cell] = value = new HashSet<string>();
            value.UnionWith(assets.Socket(shape.Adds[index++]));
            if (!Contributors.TryGetValue(cell, out var list)) Contributors[cell] = list = new List<JObject>();
            list.Add(part);
        }
    }
    internal Footprint Shape(JObject part) => assets.Shape((string)part["strName"], (float?)part["fRotation"] ?? 0);
    internal IEnumerable<Cell> Floors => flags.Where(pair => pair.Value.Contains("IsFloor") && !pair.Value.Contains("IsObstruction"))
        .Select(pair => pair.Key);
    internal IEnumerable<Cell> Failures(JObject part)
    {
        var shape = Shape(part);
        for (int i = 0; i < Math.Min(shape.Requires.Length, (shape.Width + 2) * (shape.Height + 2)); i++)
        {
            var cell = shape.SocketCell(part, i);
            var present = At(cell);
            if (!assets.Socket(shape.Requires[i]).IsSubsetOf(present) ||
                assets.Socket(shape.Forbids[i]).Overlaps(present)) yield return cell;
        }
    }
    internal HashSet<Cell> Clearance(JObject part)
    {
        var shape = Shape(part);
        var cells = shape.Cells(part).ToHashSet();
        for (int i = 0; i < shape.Forbids.Length; i++)
            if (assets.Socket(shape.Forbids[i]).Contains("IsItemTile")) cells.Add(shape.SocketCell(part, i));
        return cells;
    }
}
