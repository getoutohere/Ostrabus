using System;
using System.Collections.Generic;
using HarmonyLib;
using BepInEx.Configuration;

namespace Ostrabus.ShipNavigation;

// Simulation policy: a hull parked at a station, including through other hulls,
// is not a ship-traffic obstacle. Do not set bNoCollisions: it also affects
// planetary/projectile handling. Never retain live hulls between queries.
internal static class StationDockedCollisions {
    internal static ConfigEntry<bool> Flag;
    internal static bool Enabled => Flag?.Value ?? true;
    internal static void SetEnabled(bool enabled) {
        if (Flag == null) throw new InvalidOperationException("Station collision policy is not initialized.");
        Flag.Value = enabled;
    }
    static readonly AccessTools.FieldRef<Ship, Dictionary<string, Ship>> Direct = AccessTools.FieldRefAccess<Ship, Dictionary<string, Ship>>("aDocked");
    [ThreadStatic] static List<Ship> pending;
    [ThreadStatic] static HashSet<string> visited;
    [ThreadStatic] static bool evaluating;

    internal static bool ActualStation(Ship ship) => !ReferenceEquals(ship, null) && ship.objSS != null &&
        ship.Classification != Ship.TypeClassification.Waypoint && ship.Classification != Ship.TypeClassification.Asteroid &&
        ship.Classification != Ship.TypeClassification.Projectile && ship.Classification != Ship.TypeClassification.Buoy &&
        ship.Classification != Ship.TypeClassification.SignalBeacon &&
        (ship.IsStation(bIgnoreDocks:true) || ship.IsSubStation() || ship.IsGroundStation());

    internal static bool IsExempt(Ship ship) {
        if (!Enabled) return false;
        var registry = CrewSim.system?.dictShips;
        if (ReferenceEquals(ship, null) || ship.bDestroyed || ship.objSS == null || ActualStation(ship) ||
            string.IsNullOrEmpty(ship.strRegID) || registry == null || !ship.IsDocked() || evaluating) return false;
        if (!registry.TryGetValue(ship.strRegID, out Ship current) || !ReferenceEquals(current, ship)) return false;
        if (pending == null) pending = new List<Ship>(16);
        if (visited == null) visited = new HashSet<string>(StringComparer.Ordinal);
        evaluating = true;
        try {
            pending.Add(ship); visited.Add(ship.strRegID);
            for (int i = 0; i < pending.Count; i++) {
                Ship member = pending[i];
                var direct = Direct(member);
                if (direct != null) {
                    foreach (var peer in direct.Values) if (Visit(member, peer, registry)) return true;
                } else if (member.json?.aDocked != null) {
                    foreach (string id in member.json.aDocked.Values)
                        if (id != null && registry.TryGetValue(id, out Ship peer) && Visit(member, peer, registry)) return true;
                }
            }
            return false;
        } finally {
            pending.Clear(); visited.Clear(); evaluating = false;
        }
    }
    static bool Visit(Ship parent, Ship ship, Dictionary<string, Ship> registry) {
        if (ReferenceEquals(ship, null) || ship.bDestroyed || ship.objSS == null || string.IsNullOrEmpty(ship.strRegID) ||
            !registry.TryGetValue(ship.strRegID, out Ship current) || !ReferenceEquals(current, ship) || !ReturnsTo(ship, parent) || !visited.Add(ship.strRegID)) return false;
        if (ActualStation(ship)) return true;
        pending.Add(ship);
        return false;
    }
    static bool ReturnsTo(Ship ship, Ship parent) {
        var links = Direct(ship);
        if (links != null) {
            foreach (var peer in links.Values) if (ReferenceEquals(peer, parent)) return true;
        } else if (ship.json?.aDocked != null) {
            foreach (string id in ship.json.aDocked.Values) if (id == parent.strRegID) return true;
        }
        return false;
    }
    internal static bool IgnoreAsObstacle(Ship ship) => ship.HideFromSystem || IsExempt(ship);
    internal static bool ShipCollision(Ship a, Ship b) {
        // Keep weapon/projectile hits on parked ships. Planetary collision uses
        // the separate Ship/BodyOrbit overload and never enters this predicate.
        if (ReferenceEquals(a, null) || ReferenceEquals(b, null) ||
            a.Classification == Ship.TypeClassification.Projectile || b.Classification == Ship.TypeClassification.Projectile) return true;
        return !IsExempt(a) && !IsExempt(b);
    }
}

[HarmonyPatch(typeof(CollisionManager), "ProcessCollision", new[]{typeof(Ship), typeof(Ship), typeof(double)})]
internal static class StationDockedCollisionPatch {
    static bool Prefix(Ship a, Ship b) => StationDockedCollisions.ShipCollision(a, b);
}
