using System;
using System.Collections.Generic;
using SolarSystem;

namespace Ostrabus.ShipNavigation.Towing;

internal static partial class DockedCoursePlayback {
    sealed class DepartureWalk {
        internal readonly List<Ship> Pending = new List<Ship>(8);
        internal readonly HashSet<Ship> Seen = new HashSet<Ship>();
        internal bool Busy;
    }
    [ThreadStatic] static DepartureWalk departureWalk;

    static bool MooringBoundary(DockGroupMember member) {
        var ship = member?.Ship;
        return member != null && (member.IsMooringTarget || (ship != null &&
            (ship.IsStation(bIgnoreDocks: true) || ship.IsSubStation() || ship.IsGroundStation())));
    }
    static DockGroupMember GroupMember(DockGroup group, Ship ship) {
        foreach (var member in group.DockedShips) if (ReferenceEquals(member?.Ship, ship)) return member;
        return null;
    }

    internal static double PredictionMass(Ship ship) {
        double physicalMass = InertialMass(ship);
        var group = ship.DockGroup;
        if (group == null) return physicalMass;
        bool moored = false;
        foreach (var member in group.DockedShips) if (MooringBoundary(member)) { moored = true; break; }
        // In free flight, even unbraced attached hulls still impose their mass
        // and native damaging loads. A moored preview instead describes the
        // intended departure after releasing ordinary station docking links.
        var owner = GroupMember(group, ship);
        if (!moored || owner == null || MooringBoundary(owner)) return physicalMass;
        var walk = departureWalk ?? (departureWalk = new DepartureWalk());
        if (walk.Busy) walk = new DepartureWalk();
        walk.Busy = true;
        try {
            walk.Pending.Add(ship); walk.Seen.Add(ship);
            double total = 0;
            for (int i = 0; i < walk.Pending.Count; i++) {
                var current = walk.Pending[i];
                double mass = current.Mass;
                if (mass > 0 && !double.IsInfinity(mass)) total += mass;
                var direct = Docked(current);
                if (direct != null) {
                    foreach (var peer in direct.Values) VisitDeparture(group, current, peer, walk);
                } else if (current.json?.aDocked != null && CrewSim.system?.dictShips != null) {
                    foreach (string id in current.json.aDocked.Values)
                        if (id != null && CrewSim.system.dictShips.TryGetValue(id, out var peer)) VisitDeparture(group, current, peer, walk);
                }
            }
            return total > 0 ? total : ship.Mass;
        } finally {
            // Retain capacity for the next UI/physics call, never live hulls.
            walk.Pending.Clear(); walk.Seen.Clear(); walk.Busy = false;
        }
    }

    static void VisitDeparture(DockGroup group, Ship parent, Ship peer, DepartureWalk walk) {
        if (peer == null || peer.bDestroyed || !ReferenceEquals(peer.DockGroup, group) || walk.Seen.Contains(peer)) return;
        var member = GroupMember(group, peer);
        if (member == null || MooringBoundary(member)) return;
        // Use native installed/secured brace state on either end of this direct
        // link. Avoid the native reverse lookup's missing-ship success fallback.
        if (!parent.TowBraceSecured(peer.strRegID, checkOtherSide: false) &&
            !peer.TowBraceSecured(parent.strRegID, checkOtherSide: false)) return;
        walk.Seen.Add(peer); walk.Pending.Add(peer);
    }
}
