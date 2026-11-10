using System;
using System.Collections.Generic;
using UnityEngine;

// Investigation-only reference: edges of the union of half-tile cells.
// Keeps disconnected pieces and holes separate by returning independent edges.
// No Harmony patch, renderer, collision change or persistent cache is installed.
static class ReferenceEdges {
    internal readonly struct Cell : IEquatable<Cell> {
        internal readonly int X, Y;
        internal Cell(int x, int y) { X = x; Y = y; }
        public bool Equals(Cell other) => X == other.X && Y == other.Y;
        public override bool Equals(object other) => other is Cell cell && Equals(cell);
        public override int GetHashCode() => unchecked(X * 73856093 ^ Y * 19349663);
    }
    internal readonly struct Edge : IEquatable<Edge> {
        internal readonly Cell A, B;
        internal Edge(int x, int y, int dx, int dy) { A = new Cell(x, y); B = new Cell(x + dx, y + dy); }
        public bool Equals(Edge other) => A.Equals(other.A) && B.Equals(other.B);
        public override bool Equals(object other) => other is Edge edge && Equals(edge);
        public override int GetHashCode() => unchecked(A.GetHashCode() * 397 ^ B.GetHashCode());
    }
    internal static HashSet<Cell> Occupied(List<Vector2> plan) {
        var cells = new HashSet<Cell>();
        if (plan == null) return cells;
        foreach (var point in plan) {
            double x = point.x * 2d, y = point.y * 2d;
            if (double.IsNaN(x) || double.IsInfinity(x) || double.IsNaN(y) || double.IsInfinity(y) ||
                Math.Abs(x - Math.Round(x)) > .001 || Math.Abs(y - Math.Round(y)) > .001)
                throw new InvalidOperationException("Reference supports half-tile-aligned footprints only.");
            int cx = checked((int)Math.Round(x)) - 1, cy = checked((int)Math.Round(y)) - 1;
            cells.Add(new Cell(cx, cy)); cells.Add(new Cell(cx + 1, cy));
            cells.Add(new Cell(cx, cy + 1)); cells.Add(new Cell(cx + 1, cy + 1));
        }
        return cells;
    }
    internal static HashSet<Edge> Build(List<Vector2> plan) {
        var cells = Occupied(plan); var edges = new HashSet<Edge>();
        foreach (var cell in cells) {
            int x = cell.X, y = cell.Y;
            if (!cells.Contains(new Cell(x, y - 1))) edges.Add(new Edge(x, y, 1, 0));
            if (!cells.Contains(new Cell(x + 1, y))) edges.Add(new Edge(x + 1, y, 0, 1));
            if (!cells.Contains(new Cell(x, y + 1))) edges.Add(new Edge(x + 1, y + 1, -1, 0));
            if (!cells.Contains(new Cell(x - 1, y))) edges.Add(new Edge(x, y + 1, 0, -1));
        }
        return edges;
    }
}
