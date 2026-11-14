using System;

namespace Ostrabus.ShipNavigation;

// Only callers creating local patrol/wait/encounter coordinates use this resolver.
// A nearby physical body remains the reference when it belongs to the station's
// authored hierarchy. Otherwise the station's own moving frame is authoritative.
internal static class LocalNavigationFrame {
    internal static BodyOrbit StationOrbit(Ship station) {
        var world = CrewSim.system;
        var situ = station?.objSS;
        if (station == null || station.bDestroyed || station.bRemove || situ == null ||
            !(situ.bIsBO || situ.bBOLocked || situ.bOrbitLocked) || world?.aBOs == null ||
            string.IsNullOrEmpty(situ.strBOPORShip)) return null;
        return world.aBOs.TryGetValue(situ.strBOPORShip, out var body) ? body : null;
    }
    internal static BodyOrbit Select(Ship station, BodyOrbit natural) {
        BodyOrbit frame = StationOrbit(station);
        if (frame == null) return natural;
        // Keep Mercury/Venus/Ceres/etc. for ordinary planetary activities. Sol
        // is not a useful stationary frame for a station in heliocentric orbit.
        BodyOrbit parent = frame;
        for (int depth = 0; parent != null && depth < 32; depth++, parent = parent.boParent)
            if (ReferenceEquals(parent, natural) && parent.strName != "Sol") return natural;
        return frame;
    }
    internal static BodyOrbit At(ShipSitu point, BodyOrbit natural) {
        var world = CrewSim.system;
        if (world?.dictShips == null || point == null || double.IsNaN(point.vPosx) || double.IsNaN(point.vPosy) ||
            double.IsInfinity(point.vPosx) || double.IsInfinity(point.vPosy)) return natural;
        if (Dominates(point, natural)) return natural;
        // Same regional ownership lookup used by native local target selection;
        // no fixed distance cutoff and no registry retained between calls.
        return Select(world.GetNearestStationRegional(point.vPosx, point.vPosy), natural);
    }
    static bool Dominates(ShipSitu point, BodyOrbit natural) {
        if (natural == null || natural.strName == "Sol") return false;
        if (!CrewSim.system.aBOs.TryGetValue("Sol", out var sol) || sol == null) return true;
        double dx = point.vPosx - natural.dXReal, dy = point.vPosy - natural.dYReal;
        double sx = point.vPosx - sol.dXReal, sy = point.vPosy - sol.dYReal;
        // Compare G*M/r^2 without square roots or a gravity/situation update.
        // A real local gravity well takes precedence over a distant station.
        return natural.fMass > 0 && natural.fMass * (sx * sx + sy * sy) > sol.fMass * (dx * dx + dy * dy);
    }
    internal static BodyOrbit Nearest(StarSystem world, ShipSitu point, double epoch, bool includePlaceholder) =>
        At(point, world.GetNearestBO(point, epoch, includePlaceholder));
    internal static void Bind(ShipSitu point, double epoch, bool includePlaceholder) {
        if (epoch < 0) epoch = StarSystem.fEpoch;
        point.LockToBO(Nearest(CrewSim.system, point, epoch, includePlaceholder), epoch);
    }
    internal static void BindExplicit(ShipSitu point, BodyOrbit natural, double epoch) => point.LockToBO(At(point, natural), epoch);
}
