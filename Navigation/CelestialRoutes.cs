using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Ostrabus.Orbital;
using Ostranauts.ShipGUIs.Utilities;
using Ostranauts.Ships.Commands;

namespace Ostrabus.ShipNavigation;

// Geometry of the path NavData actually plays, including body motion between
// waypoints. No world clock/cache writes, actor scan, saved marker or timer.
internal static class CelestialRoutes {
    internal readonly struct Obstruction {
        internal readonly BodyOrbit Body;
        internal readonly double Epoch;
        internal readonly bool Uncertain;
        internal Obstruction(BodyOrbit body, double epoch, bool uncertain = false) {
            Body = body; Epoch = epoch; Uncertain = uncertain;
        }
    }
    sealed class Receipt {
        internal WeakReference<StarSystem> World;
        internal int Bodies;
        internal double From, Radius;
        internal PointKey[] Points;
    }
    readonly struct PointKey {
        readonly double epoch, rate, phase;
        readonly DVector position;
        readonly string body;
        readonly bool attached;
        internal PointKey(NavDataPoint point) {
            epoch = point.ArrivalTime; rate = point.ObjSS.fOrbitW; phase = point.ObjSS.fOrbitTimeLast;
            position = Key(point.ObjSS); attached = Attached(point.ObjSS); body = attached ? point.ObjSS.strBOPORShip : null;
        }
        internal bool Matches(NavDataPoint point) => point?.ObjSS != null && epoch == point.ArrivalTime &&
            rate == point.ObjSS.fOrbitW && (!attached || rate == 0 || phase == point.ObjSS.fOrbitTimeLast) &&
            attached == Attached(point.ObjSS) && body == (attached ? point.ObjSS.strBOPORShip : null) &&
            (position - Key(point.ObjSS)).Length == 0;
    }
    sealed class Memo { internal Receipt Value; }
    static readonly ConditionalWeakTable<NavData, Memo> clear = new ConditionalWeakTable<NavData, Memo>();
    [ThreadStatic] static bool detouring;
    const int SampleBudget = 32768;
    const double KmAU = OrbitalMotion.KmPerAU;

    static DVector Position(ShipSitu point) => new DVector(point.vPosx, point.vPosy);
    static bool Attached(ShipSitu point) => point.bIsBO || point.bBOLocked || point.bOrbitLocked;
    static DVector Key(ShipSitu point) => Attached(point) ? new DVector(point.vBOOffsetx, point.vBOOffsety) : Position(point);
    internal static double Radius(Ship ship) {
        var anchor = ship.HasDockGroup ? ship.DockGroup.Anchor : null;
        return anchor == null ? ship.objSS.GetRadiusAU() : anchor.GetRadiusAU() + (Position(anchor) - Position(ship.objSS)).Length;
    }
    internal static bool PointAt(ShipSitu point, double epoch, out DVector position) {
        position = default;
        if (point == null) return false;
        if (!Attached(point)) { position = Position(point); return position.Finite; }
        if (point.strBOPORShip == null || CrewSim.system?.aBOs == null ||
            !CrewSim.system.aBOs.TryGetValue(point.strBOPORShip, out var body) ||
            !NativeEphemeris.Position(body, epoch, out position)) return false;
        // Native bIsBO still carries vBOOffset (including the planner's 300 km
        // stand-off). Do not replace manufactured station references with planets.
        double angle = point.fOrbitW * (epoch - point.fOrbitTimeLast);
        double cos = Math.Cos(angle), sin = Math.Sin(angle);
        position += new DVector(point.vBOOffsetx * cos - point.vBOOffsety * sin,
            point.vBOOffsetx * sin + point.vBOOffsety * cos);
        return position.Finite;
    }
    internal static bool Sample(NavDataPoint a, NavDataPoint b, double epoch, out DVector position) {
        position = default;
        if (!PointAt(a?.ObjSS, epoch, out var p) || !PointAt(b?.ObjSS, epoch, out var q)) return false;
        double span = b.ArrivalTime - a.ArrivalTime;
        if (!(span > 0)) { position = q; return span == 0; }
        double blend = Math.Max(0, Math.Min(1, (float)(epoch - a.ArrivalTime) / (float)span));
        position = p + (q - p) * blend;
        return position.Finite;
    }
    internal static bool DockingPoint(ShipSitu origin, BodyOrbit body, ref double x, ref double y) {
        bool clear = FlightCPU.GetDockingNavPoint(origin, body, ref x, ref y);
        if (!clear || body == null || origin == null || body.IsPlaceholder() || body.IsAsteroidField ||
            !(body.fRadius > 0) || origin.bGrounded) return clear;
        var p = Position(origin) - new DVector(body.dXReal, body.dYReal);
        var q = new DVector(x - body.dXReal, y - body.dYReal);
        // Native returns true both for clear LOS and when all three side
        // candidates fail. Only the latter needs the missing near-side exit.
        if (!(p.Length > body.fRadius) || ChordMinimum(p, q) > body.fRadius) return clear;
        var exit = p * (2 * body.fRadius / p.Length);
        x = body.dXReal + exit.X; y = body.dYReal + exit.Y;
        return false;
    }
    static double AccelerationBound(BodyOrbit body) {
        double sum = 0;
        int depth = 0;
        for (; body != null; body = body.boParent) {
            if (++depth > 32) return double.PositiveInfinity;
            double axis = body.fAxis1 * .5;
            if (axis == 0 && body.fAxis2 == 0) continue;
            if (!(axis > 0) || body.fPeriod == 0 || !(body.fPerh > 0)) return double.PositiveInfinity;
            double n = 2 * Math.PI / body.fPeriod;
            sum += n * n * axis * axis * axis / (body.fPerh * body.fPerh);
        }
        return sum;
    }
    static double EndpointAcceleration(ShipSitu point) {
        if (!Attached(point)) return 0;
        if (point.strBOPORShip == null || !CrewSim.system.aBOs.TryGetValue(point.strBOPORShip, out var body)) return double.PositiveInfinity;
        double radius = Math.Sqrt(point.vBOOffsetx * point.vBOOffsetx + point.vBOOffsety * point.vBOOffsety);
        return AccelerationBound(body) + point.fOrbitW * point.fOrbitW * radius;
    }
    static double EndpointSpeed(ShipSitu point) {
        if (!Attached(point)) return 0;
        if (point.strBOPORShip == null || !CrewSim.system.aBOs.TryGetValue(point.strBOPORShip, out var body)) return double.PositiveInfinity;
        double speed = Math.Abs(point.fOrbitW) * new DVector(point.vBOOffsetx, point.vBOOffsety).Length;
        int depth = 0;
        for (; body != null; body = body.boParent) {
            if (++depth > 32) return double.PositiveInfinity;
            double axis = body.fAxis1 * .5;
            if (axis == 0 && body.fAxis2 == 0) continue;
            if (!(axis > 0) || body.fPeriod == 0 || !(body.fPerh > 0)) return double.PositiveInfinity;
            speed += Math.Abs(2 * Math.PI / body.fPeriod) * axis * axis / body.fPerh;
        }
        return speed;
    }
    static bool Unchanged(Receipt receipt, List<NavDataPoint> points) {
        if (receipt.Points == null || receipt.Points.Length != points.Count) return false;
        for (int i = 0; i < points.Count; i++) if (!receipt.Points[i].Matches(points[i])) return false;
        return true;
    }
    static double ChordMinimum(DVector p, DVector q) {
        var d = q - p;
        double length = d.Dot(d), t = length > 0 ? Math.Max(0, Math.Min(1, -p.Dot(d) / length)) : 0;
        return (p + d * t).Length;
    }
    static bool Relative(NavDataPoint a, NavDataPoint b, BodyOrbit body, double epoch, ref int budget, out DVector p) {
        p = default;
        if (--budget < 0 || !Sample(a, b, epoch, out var ship) || !NativeEphemeris.Position(body, epoch, out var center)) return false;
        p = ship - center;
        return p.Finite;
    }
    static bool Segment(NavDataPoint a, NavDataPoint b, BodyOrbit body, double radius, double acceleration,
        double first, double last, DVector p, DVector q, ref int budget, int depth, out Obstruction obstruction) {
        obstruction = default;
        if (p.Length <= radius || q.Length <= radius) {
            obstruction = new Obstruction(body, p.Length <= radius ? first : last); return false;
        }
        double duration = last - first;
        // A bounded acceleration limits curvature away from a straight chord.
        // Include float InverseLerp and AU-coordinate rounding in admission.
        double rounding = Math.Max(.001 / KmAU, (Position(b.ObjSS) - Position(a.ObjSS)).Length * 1.2e-7);
        if (ChordMinimum(p, q) > radius + acceleration * duration * duration / 8 + rounding) return true;
        double middle = first + duration * .5;
        if (depth >= 24 || duration <= .025 || !(middle > first && middle < last) ||
            !Relative(a, b, body, middle, ref budget, out var m)) {
            obstruction = new Obstruction(body, first, true); return false;
        }
        return Segment(a, b, body, radius, acceleration, first, middle, p, m, ref budget, depth + 1, out obstruction) &&
            Segment(a, b, body, radius, acceleration, middle, last, m, q, ref budget, depth + 1, out obstruction);
    }
    internal static bool Clear(NavData route, double radiusAU, double from, out Obstruction obstruction) {
        obstruction = default;
        var world = CrewSim.system;
        var points = route?.GetNavDataPoints();
        if (world?.aBOs == null || points == null || points.Count < 2 || points.Count > 4096 ||
            !OrbitAcceleration.Finite(radiusAU) || radiusAU < 0 || !OrbitAcceleration.Finite(from)) return false;
        var firstPoint = points[0]; var lastPoint = points[points.Count - 1];
        if (firstPoint?.ObjSS == null || lastPoint?.ObjSS == null) return false;
        var memo = clear.GetValue(route, _ => new Memo());
        var receipt = Volatile.Read(ref memo.Value);
        if (receipt != null && receipt.World.TryGetTarget(out var priorWorld) && ReferenceEquals(priorWorld, world) &&
            receipt.Bodies == world.aBOs.Count && receipt.From <= from && receipt.Radius == radiusAU && Unchanged(receipt, points)) return true;
        int budget = SampleBudget;
        foreach (var body in world.aBOs.Values) {
            if (body == null || body.IsPlaceholder() || body.IsAsteroidField || !(body.fRadius > 0)) continue;
            double bodyAcceleration = AccelerationBound(body);
            for (int i = 1; i < points.Count; i++) {
                var a = points[i - 1]; var b = points[i];
                if (a?.ObjSS == null || b?.ObjSS == null || !OrbitAcceleration.Finite(a.ArrivalTime) ||
                    !OrbitAcceleration.Finite(b.ArrivalTime) || b.ArrivalTime < a.ArrivalTime) return false;
                if (b.ArrivalTime < from) continue;
                double begin = Math.Max(from, a.ArrivalTime), end = b.ArrivalTime;
                if (!Relative(a, b, body, begin, ref budget, out var p) || !Relative(a, b, body, end, ref budget, out var q)) {
                    obstruction = new Obstruction(body, begin, true); return false;
                }
                double curvature = bodyAcceleration + EndpointAcceleration(a.ObjSS) + EndpointAcceleration(b.ObjSS);
                if (Attached(a.ObjSS) || Attached(b.ObjSS)) {
                    // Moving endpoint interpolation has a velocity cross term.
                    curvature += 2 * (EndpointSpeed(a.ObjSS) + EndpointSpeed(b.ObjSS)) / Math.Max(.001, b.ArrivalTime - a.ArrivalTime);
                }
                if (!Segment(a, b, body, body.fRadius + radiusAU, curvature, begin, end, p, q, ref budget, 0, out obstruction)) return false;
            }
        }
        var keys = new PointKey[points.Count];
        for (int i = 0; i < points.Count; i++) keys[i] = new PointKey(points[i]);
        Volatile.Write(ref memo.Value, new Receipt { World = new WeakReference<StarSystem>(world), Bodies = world.aBOs.Count,
            From = from, Radius = radiusAU, Points = keys });
        return true;
    }
    internal static bool TryDetour(FlyToPath planner, NavDataPoint origin, float limiter, float coast,
        Obstruction obstruction, out NavData route) {
        route = null;
        var body = obstruction.Body;
        if (detouring || body == null || origin?.ObjSS == null ||
            !NativeEphemeris.State(body, origin.ArrivalTime, out var center, out var velocity)) return false;
        var relative = Position(origin.ObjSS) - center;
        if (!(relative.Length > body.fRadius)) return false;
        var radial = relative * (1 / relative.Length);
        // Native FlightCPU's side candidates are at two body radii. Ask that
        // same geometry routine about the blocked side; add the missing near-side
        // radial exit when neither perpendicular candidate can clear a low orbit.
        var shadow = new BodyOrbit(new JsonBodyOrbitSave { strName = body.strName, fRadius = body.fRadius, fPeriod = 1, nOrbitDirection = 1 });
        var source = new ShipSitu(false) { vPosx = relative.X, vPosy = relative.Y };
        double x = -radial.X * body.fRadius, y = -radial.Y * body.fRadius;
        bool nativeClear = FlightCPU.GetDockingNavPoint(source, shadow, ref x, ref y);
        var perpendicular = new DVector(-radial.Y, radial.X);
        var candidates = new List<DVector>();
        if (!nativeClear) candidates.Add(new DVector(x, y));
        candidates.Add(radial * (2 * body.fRadius));
        candidates.Add(perpendicular * (2 * body.fRadius));
        candidates.Add(perpendicular * (-2 * body.fRadius));
        candidates.Add(radial * (4 * body.fRadius));
        int recursion = planner.nRecursion;
        detouring = true;
        try {
            foreach (var offset in candidates) {
                var point = new ShipSitu(false) { bBOLocked = true, strBOPORShip = body.strName,
                    vPosx = center.X + offset.X, vPosy = center.Y + offset.Y,
                    vBOOffsetx = offset.X, vBOOffsety = offset.Y, vVelX = velocity.X, vVelY = velocity.Y };
                var target = new NavDataPoint(origin.ArrivalTime, point, origin.FuelLevel, 0, origin.TorchFuelLevel, origin.RCSCount);
                planner.nRecursion = 0;
                var candidate = planner.PlanTrip4(origin, target, limiter, coast);
                if (candidate == null || !Clear(candidate, Radius(planner.ShipUs), origin.ArrivalTime, out _)) continue;
                route = candidate;
                return true;
            }
            return false;
        } finally { planner.nRecursion = recursion; detouring = false; }
    }
}
