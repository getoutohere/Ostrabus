using System;
using System.Collections.Generic;

namespace Ostrabus.NavConsole.Outlines;

// Worker-facing geometry deliberately has no Unity or game-object dependencies.
internal readonly struct HullPoint : IEquatable<HullPoint> {
    internal readonly double X, Y;
    internal HullPoint(double x, double y) { X = x; Y = y; }
    public bool Equals(HullPoint p) => X == p.X && Y == p.Y;
    internal bool Finite => !double.IsNaN(X) && !double.IsInfinity(X) && !double.IsNaN(Y) && !double.IsInfinity(Y);
}

internal sealed class Envelope {
    readonly HullPoint[] points;
    readonly int[] ends;
    readonly double centerX, centerY, radius;
    internal readonly CollisionShape Collision;
    internal readonly string CollisionError;
    internal int Count => points.Length;
    internal int Loops => ends.Length;
    internal Envelope(HullPoint[] source, int count) {
        points = new HullPoint[count]; Array.Copy(source, points, count);
        ends = count >= 3 ? new[] { count } : Array.Empty<int>();
        Normalize(source, count, out centerX, out centerY, out radius);
    }
    internal Envelope(List<HullPoint> source, List<int> loopEnds, CollisionShape.Builder collision = null) {
        points = source.ToArray(); ends = loopEnds.ToArray();
        Normalize(points, points.Length, out centerX, out centerY, out radius);
        if(collision!=null)try{Collision=collision.Build(this);}catch(InvalidOperationException error){CollisionError=error.Message;}
    }
    static void Normalize(HullPoint[] source, int count, out double centerX, out double centerY, out double radius) {
        centerX = centerY = radius = 0;
        if (count == 0) return;
        double minX = source[0].X, maxX = minX, minY = source[0].Y, maxY = minY;
        for (int i = 1; i < count; i++) {
            minX = Math.Min(minX, source[i].X); maxX = Math.Max(maxX, source[i].X);
            minY = Math.Min(minY, source[i].Y); maxY = Math.Max(maxY, source[i].Y);
        }
        centerX = (minX + maxX) * .5; centerY = (minY + maxY) * .5;
        double squared = 0;
        for (int i = 0; i < count; i++) {
            double x = source[i].X - centerX, y = source[i].Y - centerY;
            squared = Math.Max(squared, x * x + y * y);
        }
        radius = Math.Sqrt(squared);
    }
    internal HullPoint Raw(int i) => points[i];
    internal int Start(int loop) => loop == 0 ? 0 : ends[loop - 1];
    internal int End(int loop) => ends[loop];
    internal HullPoint Unit(int i) => Unit(points[i].X,points[i].Y);
    internal HullPoint Unit(double x,double y) => radius > 0 ? new HullPoint((x-centerX)/radius,(y-centerY)/radius) : default;
    internal bool SameRaw(List<HullPoint> other, List<int> loopEnds) {
        if (other.Count != Count || loopEnds.Count != Loops) return false;
        for (int i = 0; i < Count; i++) if (!points[i].Equals(other[i])) return false;
        for (int i = 0; i < Loops; i++) if (ends[i] != loopEnds[i]) return false;
        return true;
    }
    internal bool SameDrawing(Envelope other) {
        if (other == null || other.Count != Count || other.Loops != Loops) return false;
        for (int i = 0; i < Loops; i++) if (ends[i] != other.ends[i]) return false;
        for (int i = 0; i < Count; i++) {
            var a = Unit(i); var b = other.Unit(i);
            if (Math.Abs(a.X - b.X) > 1e-12 || Math.Abs(a.Y - b.Y) > 1e-12) return false;
        }
        return true;
    }
    internal bool Contains(double x, double y, bool strict) {
        for (int loop = 0; loop < Loops; loop++) {
            bool inside = false; int start = Start(loop), end = End(loop);
            for (int i = start; i < end; i++) {
                var a = points[i]; var b = points[i + 1 == end ? start : i + 1];
                double cross = (b.X - a.X) * (y - a.Y) - (b.Y - a.Y) * (x - a.X);
                double tolerance = 1e-8 * Math.Max(1, Math.Abs(b.X - a.X) + Math.Abs(b.Y - a.Y));
                if (Math.Abs(cross) <= tolerance && x >= Math.Min(a.X,b.X) - 1e-8 && x <= Math.Max(a.X,b.X) + 1e-8 &&
                    y >= Math.Min(a.Y,b.Y) - 1e-8 && y <= Math.Max(a.Y,b.Y) + 1e-8) return !strict;
                if ((a.Y > y) != (b.Y > y) && x < (b.X - a.X) * (y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
            }
            if (inside) return true;
        }
        return false;
    }
    internal bool ContainsBox(double minX, double minY, double maxX, double maxY, bool strict) {
        if (!Contains(minX,minY,strict) || !Contains(maxX,minY,strict) || !Contains(maxX,maxY,strict) || !Contains(minX,maxY,strict)) return false;
        // Four contained corners do not suffice for a concave hull: its notch
        // can run through the box. Reject any perimeter inside/touching the box.
        double margin = strict ? 1e-8 : -1e-8;
        for (int loop = 0; loop < Loops; loop++) for (int i = Start(loop); i < End(loop); i++) {
            var a = points[i]; var b = points[i + 1 == End(loop) ? Start(loop) : i + 1];
            if (Math.Max(a.X,b.X) >= minX - margin && Math.Min(a.X,b.X) <= maxX + margin &&
                Math.Max(a.Y,b.Y) >= minY - margin && Math.Min(a.Y,b.Y) <= maxY + margin) return false;
        }
        return true;
    }
}
