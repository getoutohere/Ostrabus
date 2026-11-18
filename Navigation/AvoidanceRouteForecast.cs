using System;
using System.Collections.Generic;
using Ostrabus.Orbital;
using Ostranauts.ShipGUIs.Utilities;

namespace Ostrabus.ShipNavigation;

// Read the route native NavData playback actually executes. Its interpolation
// is piecewise linear; extending its current acceleration ignores upcoming
// braking/turning points and can alternate safe evasion with unsafe replanning.
internal static class AvoidanceRouteForecast
{
    const double MetresPerAU = OrbitalMotion.KmPerAU * 1000;
    struct Motion
    {
        internal readonly ShipSitu Situation;
        internal readonly List<NavDataPoint> Points;
        internal readonly DVector Offset;
        internal int Index;
        internal Motion(ShipSitu situation, NavData route, ShipSitu origin)
        {
            Situation = situation; Points = route?.GetNavDataPoints(); Index = 0;
            // A native route belongs to the controlling hull; the collision
            // circle may instead be centred on its docking-group anchor.
            Offset = origin == null ? default : new DVector(situation.vPosx - origin.vPosx, situation.vPosy - origin.vPosy);
            if (Points == null || Points.Count < 2) Points = null;
        }
        internal double Next(double epoch, double end)
        {
            if (Points == null) return end;
            while (Index + 1 < Points.Count && Points[Index + 1].ArrivalTime <= epoch) Index++;
            if (epoch < Points[0].ArrivalTime) return Math.Min(end, Points[0].ArrivalTime);
            return Index + 1 < Points.Count ? Math.Min(end, Points[Index + 1].ArrivalTime) : end;
        }
        internal bool At(double epoch, double elapsed, out DVector p, out DVector v)
        {
            if (Points == null)
            {
                var a = Acceleration(Situation); double burn = Math.Min(elapsed, AvoidancePrediction.AccelerationSeconds);
                p = new DVector(Situation.vPosx, Situation.vPosy) + new DVector(Situation.vVelX, Situation.vVelY) * elapsed + a * (burn * (elapsed - .5 * burn));
                v = new DVector(Situation.vVelX, Situation.vVelY) + a * burn; return p.Finite && v.Finite;
            }
            while (Index + 1 < Points.Count && Points[Index + 1].ArrivalTime < epoch) Index++;
            var first = Points[Index]; var last = Index + 1 < Points.Count ? Points[Index + 1] : first;
            if (first?.ObjSS == null || last?.ObjSS == null || !Point(first.ObjSS, epoch, out var p0, out var v0) || !Point(last.ObjSS, epoch, out var p1, out var v1)) { p = v = default; return false; }
            if (ReferenceEquals(first, last))
            {
                double after = Math.Max(0, epoch - first.ArrivalTime);
                bool attached = first.ObjSS.bBOLocked || first.ObjSS.bIsBO || first.ObjSS.bOrbitLocked;
                p = (attached ? p0 : p0 + v0 * after) + Offset; v = v0; return p.Finite && v.Finite;
            }
            double duration = last.ArrivalTime - first.ArrivalTime;
            if (!(duration > 0) || !OrbitAcceleration.Finite(duration)) { p = v = default; return false; }
            // Native InverseLerp receives float elapsed/duration separately.
            double t = Math.Max(0, Math.Min(1, (float)(epoch - first.ArrivalTime) / (float)duration));
            p = p0 + (p1 - p0) * t + Offset; v = v0 + (v1 - v0) * t; return p.Finite && v.Finite;
        }
    }
    internal static bool TryCollision(ShipSitu us, ShipSitu them, double seconds, NavData ours, NavData theirs, out bool hit,
        ShipSitu ourOrigin = null, ShipSitu theirOrigin = null)
    {
        hit = false; var a = new Motion(us, ours, ourOrigin); var b = new Motion(them, theirs, theirOrigin);
        if (a.Points == null && b.Points == null) return false;
        double now = StarSystem.fEpoch, window = AvoidancePrediction.AccelerationSeconds;
        // Inspect a newly generated course before admitting it. Ordinary flight
        // checks retain bounded local lookahead; they do not rescan a whole trip.
        double radius = (double)CollisionManager.GetCollisionDistanceAU(us, them) * MetresPerAU;
        if (!OrbitAcceleration.Finite(radius) || radius < 0) return false;
        if (AvoidanceQuery.Admission && a.Points != null && LocalEncounter(us, them, seconds, radius))
            window = Math.Max(window, a.Points[a.Points.Count - 1].ArrivalTime - now);
        double end = now + window, step = Math.Max(1, window / 256);
        double squared = radius * radius;
        if (!a.At(now, 0, out var ap, out _) || !b.At(now, 0, out var bp, out _)) return false;
        double t = now;
        // Retain every route breakpoint and subdivide long intervals for locked
        // endpoint curvature, without advancing live situation/body caches.
        int limit = 512 + (a.Points?.Count ?? 0) + (b.Points?.Count ?? 0);
        for (int count = 0; t < end && count < limit; count++)
        {
            double next = Math.Min(end, t + step); next = a.Next(t, next); next = b.Next(t, next);
            if (t < now + AvoidancePrediction.AccelerationSeconds) next = Math.Min(next, now + AvoidancePrediction.AccelerationSeconds);
            if (!(next > t)) return false;
            if (!a.At(next, next - now, out var aq, out var av) || !b.At(next, next - now, out var bq, out var bv)) return false;
            double dt = next - t; var p = (ap - bp) * MetresPerAU; var q = (aq - bq) * MetresPerAU;
            var acc = (t < now + AvoidancePrediction.AccelerationSeconds ? ((a.Points == null ? Acceleration(us) : default) - (b.Points == null ? Acceleration(them) : default)) : default) * MetresPerAU;
            var v = (q - p) * (1 / dt) - acc * (.5 * dt);
            if (ConstantAccelerationApproach.MinimumSquared(p, v, acc, dt) <= squared) { hit = true; return true; }
            t = next; ap = aq; bp = bq;
            if (t >= end && seconds > window)
            {
                var velocity = (av - bv) * MetresPerAU; double speed = velocity.Dot(velocity), rest = seconds - window;
                double closest = speed > 0 ? Math.Max(0, Math.Min(rest, -q.Dot(velocity) / speed)) : 0;
                var point = q + velocity * closest; hit = point.Dot(point) <= squared; return true;
            }
        }
        return t >= end;
    }
    static bool LocalEncounter(ShipSitu us, ShipSitu them, double seconds, double radius) {
        // Whole-course admission is for the nearby encounter being negotiated,
        // not speculative meetings hours later with currently distant traffic.
        // Use the native reaction window and physical maneuver reach, including
        // hull clearance; unlike the removed gate this admits starts from rest.
        double window = Math.Max(AvoidancePrediction.AccelerationSeconds, seconds);
        double acceleration = Math.Min(AvoidanceQuery.Owner?.RCSAccelMax ?? 0, 1.9016982118751132E-10) * MetresPerAU;
        var p = new DVector(us.vPosx - them.vPosx, us.vPosy - them.vPosy) * MetresPerAU;
        var v = new DVector(us.vVelX - them.vVelX, us.vVelY - them.vVelY) * MetresPerAU;
        double reach = radius + v.Length * window + .5 * (acceleration + Acceleration(them).Length * MetresPerAU) * window * window;
        return p.Length <= reach;
    }
    static DVector Acceleration(ShipSitu s) => new DVector(
        (double)s.vAccRCS.x + s.vAccIn.x + s.vAccEx.x + s.vAccLift.x + s.vAccDrag.x,
        (double)s.vAccRCS.y + s.vAccIn.y + s.vAccEx.y + s.vAccLift.y + s.vAccDrag.y);
    static bool Point(ShipSitu s, double epoch, out DVector position, out DVector velocity)
    {
        if (!(s.bBOLocked || s.bOrbitLocked || s.bIsBO))
        {
            position = new DVector(s.vPosx, s.vPosy); velocity = new DVector(s.vVelX, s.vVelY); return position.Finite && velocity.Finite;
        }
        position = velocity = default;
        if (CrewSim.system?.aBOs == null || s.strBOPORShip == null || !CrewSim.system.aBOs.TryGetValue(s.strBOPORShip, out var body) ||
            !NativeEphemeris.State(body, epoch, out var p, out var v)) return false;
        if (s.bIsBO) { position = p; velocity = v; return true; }
        var state = new BodyLockedOffset(s).At(new MotionState(p * OrbitalMotion.KmPerAU, v * OrbitalMotion.KmPerAU), epoch);
        position = state.Position * (1 / OrbitalMotion.KmPerAU); velocity = state.Velocity * (1 / OrbitalMotion.KmPerAU); return position.Finite && velocity.Finite;
    }
}
