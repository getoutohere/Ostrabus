using System;
using System.Collections.Generic;
using System.Diagnostics;
using Ostrabus.Orbital;

namespace Ostrabus.Inspection;

// Called by the existing Unity log listener, before native Ship.Destroy removes
// its registry entry. Capture even actors born and removed within one world step.
// No gameplay hook, polling, file writer, retained game reference or trace toggle.
internal static class ShipDestructionEvidence {
    const string Prefix = "Destroying ship ";
    static object NearbyBodies(StarSystem world, ShipSitu situation) {
        var nearest = new List<(double gap, BodyOrbit body)>(4);
        if (situation != null && world.aBOs != null) foreach (var body in world.aBOs.Values) {
            if (body == null) continue;
            double dx = situation.vPosx - body.dXReal, dy = situation.vPosy - body.dYReal;
            double gap = Math.Sqrt(dx * dx + dy * dy) - body.fRadius;
            if (double.IsNaN(gap) || double.IsInfinity(gap)) continue;
            int index = nearest.FindIndex(item => gap < item.gap);
            if (index < 0) index = nearest.Count;
            if (index >= 3) continue;
            nearest.Insert(index, (gap, body));
            if (nearest.Count > 3) nearest.RemoveAt(3);
        }
        var result = new List<object>(nearest.Count);
        foreach (var item in nearest) result.Add(SnapshotJson.Map("id", item.body.strName,
            "type", item.body.GetType().Name, "cachedEpoch", item.body.dTimeCalcLast,
            "xAU", item.body.dXReal, "yAU", item.body.dYReal, "radiusAU", item.body.fRadius,
            "centerToSurfaceGapKm", item.gap * 149597870.7));
        return result;
    }
    static string Command(Ostranauts.Ships.AIPilots.AIShip ai) {
        // A newly constructed or retiring controller may have no queue yet.
        try { return ai?.ActiveCommandName; } catch (NullReferenceException) { return null; }
    }
    internal static void Observe(string message) {
        if (!JumpBoundary.Active || message == null || !message.StartsWith(Prefix, StringComparison.Ordinal) ||
            !message.EndsWith(".", StringComparison.Ordinal)) return;
        string id = message.Substring(Prefix.Length, message.Length - Prefix.Length - 1);
        var world = CrewSim.system;
        if (world?.dictShips == null || !world.dictShips.TryGetValue(id, out var ship) || ship == null || ship.bDestroyed ||
            ship.People == null || ship.People.Count == 0) return;
        var crew = new List<object>();
        foreach (var person in ship.People) {
            if (crew.Count == 32) break;
            if (person == null) continue;
            CondOwner cached = null;
            if (person.strCO != null) DataHandler.mapCOs?.TryGetValue(person.strCO, out cached);
            crew.Add(SnapshotJson.Map("id", person.strCO, "alive", cached == null ? person.bAlive : cached.bAlive,
                "cachedOwnerPresent", cached != null, "cachedOwnerShip", cached?.ship?.strRegID));
        }
        var situation = ship.objSS;
        var route = situation?.NavData;
        var registry = ControllerInspection.Capture(Stopwatch.GetTimestamp());
        var ai = registry.Single(id, ship);
        string stack = new StackTrace(1, false).ToString();
        if (stack.Length > 8192) stack = stack.Substring(0, 8192);
        InspectionEventHistory.Add("aiShipDestroyingDuringJump", id, SnapshotJson.Map(
            "source", JumpBoundary.Current.Source, "name", ship.publicName,
            "owner", world.GetShipOwner(id), "classification", ship.Classification.ToString(),
            "loadState", ship.LoadState.ToString(), "hidden", ship.HideFromSystem, "removeFlag", ship.bRemove,
            "aiRole", ai?.AICharacter?.AIType.ToString(), "command", Command(ai),
            "controllerScanComplete", registry.Complete, "targetId", ship.targetRegID,
            "crewCount", ship.People.Count, "crew", crew, "crewTruncated", ship.People.Count > 32,
            "motion", situation == null ? null : SnapshotJson.Map("xAU", situation.vPosx, "yAU", situation.vPosy,
                "vxAUPerSecond", situation.vVelX, "vyAUPerSecond", situation.vVelY,
                "bodyReference", situation.strBOPORShip, "bodyLocked", situation.bBOLocked,
                "orbitLocked", situation.bOrbitLocked, "isBody", situation.bIsBO,
                "inputAcceleration", SnapshotJson.Map("x", situation.vAccIn.x, "y", situation.vAccIn.y),
                "rcsAcceleration", SnapshotJson.Map("x", situation.vAccRCS.x, "y", situation.vAccRCS.y)),
            "routeEndEpoch", route?.Destination?.ArrivalTime, "rcsFuelKgRaw", ship.fShallowRCSRemass,
            "nearestStoredBodySurfaces", NearbyBodies(world, situation),
            "stack", stack,
            "note", "Pre-destruction evidence, not a death verdict. Native unload/replacement also destroys old instances. No actor or navigation methods were executed."));
    }
}
