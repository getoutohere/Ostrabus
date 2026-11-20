using System;
using System.Collections.Generic;
using Ostrabus.NavConsole.Outlines;
using Ostrabus.Orbital;
using Ostranauts.ShipGUIs.Utilities;

namespace Ostrabus.ShipNavigation.Collisions;

// Confirmation of a broad AI warning, using the same immutable exterior cover
// as physical collisions. No steering, clearance grants or actor exemptions.
internal sealed class HullApproach : IApproachProbe {
    const double MetresPerAU = 1 / HullZoom.MetresToAU;
    const double ChordError = .25;
    const int MaxMembers = 32, MaxSweeps = 512, MaxVisits = 32768, MaxPairs = 8192;
    readonly struct Member {
        internal readonly Envelope Hull;
        internal readonly HullContact.Vec Offset;
        internal readonly double Angle, Radius;
        internal Member(Envelope hull, HullContact.Vec offset, double angle) {
            Hull = hull; Offset = offset; Angle = angle; Radius = hull.MapRadius;
        }
    }
    readonly List<Member> ours = new List<Member>();
    readonly List<Member> theirs = new List<Member>();
    readonly HullContact solver = new HullContact();
    double ourRadius, theirRadius;
    int sweeps, visits, pairs;
    bool orientationUnknown;
    [ThreadStatic] static HullApproach idle;

    internal static HullApproach Rent() {
        var value = idle ?? new HullApproach(); idle = null; return value;
    }
    internal static void Release(HullApproach value) {
        value.ours.Clear(); value.theirs.Clear(); value.ourRadius = value.theirRadius = 0;
        value.sweeps = value.visits = value.pairs = 0;
        value.orientationUnknown = false;
        if (idle == null) idle = value;
    }
    internal bool Prepare(Ship ship, Ship obstacle, NavData ourRoute = null, NavData theirRoute = null) =>
        Side(ship, ourRoute, ours, out ourRadius) && Side(obstacle, theirRoute, theirs, out theirRadius);

    bool Side(Ship owner, NavData route, List<Member> members, out double radius) {
        radius = 0;
        var group = owner.DockGroup;
        var anchor = group?.Anchor ?? owner.objSS;
        if (anchor == null || anchor.bIsBO || anchor.bBOLocked || anchor.bGrounded || anchor.bOrbitLocked) return false;
        bool planned = route?.GetNavDataPoints()?.Count >= 2;
        // The existing route model uses a fixed controller-to-anchor offset.
        // Do not prove a rotating flotilla clear from that approximation.
        if (planned && group?.DockedShips.Count > 1) return false;
        orientationUnknown |= !planned && anchor.fA != 0;
        if (group == null || group.DockedShips.Count == 0)
            return Add(owner, default, owner.objSS.fRot, members, ref radius);
        bool foundOwner = false;
        foreach (var member in group.DockedShips) {
            var hull = member?.Ship;
            if (hull == null || hull.bDestroyed) continue;
            if (members.Count == MaxMembers || !ReferenceEquals(hull.DockGroup, group) || member.IsMooringTarget ||
                hull.objSS == null || hull.objSS.bIsBO || hull.objSS.bBOLocked || hull.objSS.bOrbitLocked) return false;
            foundOwner |= ReferenceEquals(hull, owner);
            var local = hull.objSS._dockOffsetPos;
            var offset = Rotate(new HullContact.Vec(local.X * MetresPerAU, local.Y * MetresPerAU), anchor.fRot);
            if (!Add(hull, offset, anchor.fRot + hull.objSS._dockOffsetRot, members, ref radius)) return false;
        }
        return foundOwner && members.Count != 0;
    }
    static bool Add(Ship ship, HullContact.Vec offset, double rotation, List<Member> members, ref double radius) {
        if (!HullDocking.Ordinary(ship) || !HullDisplay.Geometry(ship, out var geometry) ||
            geometry?.Collision == null || !(geometry.MapRadius > 0) || !Finite(geometry.MapRadius + offset.X + offset.Y + rotation)) return false;
        members.Add(new Member(geometry, offset, rotation - ship.nGridRotation * Math.PI / 180));
        radius = Math.Max(radius, offset.Length + geometry.MapRadius);
        return true;
    }
    static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    static HullContact.Vec Rotate(HullContact.Vec value, double angle) {
        double c = Math.Cos(angle), s = Math.Sin(angle);
        return new HullContact.Vec(value.X * c - value.Y * s, value.X * s + value.Y * c);
    }
    static HullContact.Vec At(DVector p, DVector v, DVector a, double seconds) =>
        new HullContact.Vec(p.X + v.X * seconds + .5 * a.X * seconds * seconds,
            p.Y + v.Y * seconds + .5 * a.Y * seconds * seconds);

    public bool Collides(DVector position, DVector velocity, DVector acceleration, double elapsed, double seconds,
        double ourStart, double ourEnd, double theirStart, double theirEnd) {
        if (!(seconds >= 0) || !Finite(seconds + elapsed + ourStart + ourEnd + theirStart + theirEnd) ||
            !position.Finite || !velocity.Finite || !acceleration.Finite || ours.Count == 0 || theirs.Count == 0) return true;
        // All orientations and rigid group offsets fit these unpadded circles.
        // Most inflated-circle false positives end here without a shape query.
        double radius = ourRadius + theirRadius;
        if (ConstantAccelerationApproach.MinimumSquared(position, velocity, acceleration, seconds) > radius * radius) return false;
        // Native angular acceleration can stop a spin within a time step. If
        // its future orientation is unresolved, retain the actual enclosing
        // circle warning rather than invent a clear exterior sweep.
        if (orientationUnknown) return true;
        foreach (var a in ours) foreach (var b in theirs) {
            double wa = seconds > 0 ? (ourEnd - ourStart) / seconds : 0;
            double wb = seconds > 0 ? (theirEnd - theirStart) / seconds : 0;
            // The straight chord deviates at most |p''|*dt^2/8. Include
            // rotating group-centre offsets, not just translational thrust.
            double curvature = acceleration.Length + wa * wa * a.Offset.Length + wb * wb * b.Offset.Length;
            double required = Math.Ceiling(seconds * Math.Sqrt(curvature / (8 * ChordError)));
            if (!Finite(required) || required > MaxSweeps - sweeps) return true;
            int count = Math.Max(1, (int)required);
            for (int i = 0; i < count; i++) {
                if (++sweeps > MaxSweeps || visits >= MaxVisits || pairs >= MaxPairs) return true;
                double start = seconds * i / count, end = seconds * (i + 1) / count;
                double a0 = ourStart + wa * start, a1 = ourStart + wa * end;
                double b0 = theirStart + wb * start, b1 = theirStart + wb * end;
                var p = At(position, velocity, acceleration, start) + Rotate(a.Offset, a0) - Rotate(b.Offset, b0);
                var q = At(position, velocity, acceleration, end) + Rotate(a.Offset, a1) - Rotate(b.Offset, b1);
                double bound = a.Radius + b.Radius + ChordError + .01;
                var delta = q - p; double length = delta.Dot(delta);
                double closest = length > 0 ? Math.Max(0, Math.Min(1, -p.Dot(delta) / length)) : 0;
                var near = p + delta * closest;
                if (near.Dot(near) > bound * bound) continue;
                var path = new HullContact.Motion(p, q, a.Angle + a0, a.Angle + a1,
                    b.Angle + b0, b.Angle + b1, a.Radius, b.Radius, true);
                bool resolved = solver.TryHit(a.Hull.Collision, b.Hull.Collision, path, ChordError + .01, out bool hit,
                    MaxVisits - visits, MaxPairs - pairs);
                visits += solver.Visits; pairs += solver.Pairs;
                if (!resolved || hit) return true;
            }
        }
        return false;
    }
}
