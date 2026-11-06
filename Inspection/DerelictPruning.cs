using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Ostrabus.Inspection;

internal static class DerelictPruning {
    const double KmPerAU = 149597870.7;
    const double ScanBudgetSeconds = 0.002;
    static Dictionary<string, object> Map(params object[] pairs) { return SnapshotJson.Map(pairs); }
    static bool OverBudget(long start) { return Stopwatch.GetTimestamp() - start >= Stopwatch.Frequency * ScanBudgetSeconds; }

    static Ship FindStation(string stationId) {
        if (CrewSim.system == null || CrewSim.system.dictShips == null) { return null; }
        Ship station = CrewSim.system.GetShipByRegID(stationId);
        return station != null && !station.bDestroyed && station.objSS != null && station.IsStation() ? station : null;
    }

    static List<string> PlayerShips() {
        List<string> ids = CrewSim.coPlayer?.strID != null && CrewSim.system != null ? CrewSim.system.GetShipsForOwner(CrewSim.coPlayer.strID) : null;
        if (ids == null) ids = new List<string>();
        if (CrewSim.coPlayer?.ship?.strRegID != null && !ids.Contains(CrewSim.coPlayer.ship.strRegID)) ids.Add(CrewSim.coPlayer.ship.strRegID);
        return ids;
    }

    static HashSet<string> PlotShips() {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        List<string> plots = PlotManager.GetShipsInPlots();
        if (plots != null) for (int i = 0; i < plots.Count; i++) ids.Add(plots[i]);
        return ids;
    }

    static bool StructurallyEligible(Ship ship, List<string> playerShips, HashSet<string> plotShips) {
        if (ship == null || ship.bDestroyed || ship.objSS == null || !ship.IsDerelict() || ship.IsStation() || ship.IsStationHidden()) return false;
        if (ship.objSS.bBOLocked || ship.objSS.bIsBO || plotShips.Contains(ship.strRegID)) return false;
        if (playerShips.Contains(ship.strRegID) || ship.IsDockedWithPlayer(playerShips)) return false;
        return !ship.IsDocked();
    }

    static double DistanceKm(Ship ship, Ship station) { return ship.GetRangeTo(station) * KmPerAU; }

    internal static InspectionApi.Reply Preview(string stationId, double minDistanceKm, int offset, int limit, long start) {
        Ship station = FindStation(stationId);
        if (station == null) return new InspectionApi.Reply(404, Map("error", "Reference station was not found in the active simulation."));
        if (CrewSim.system?.dictShips == null) return new InspectionApi.Reply(503, Map("error", "No active ship registry."));

        var playerShips = PlayerShips();
        var plotShips = PlotShips();
        var items = new List<object>();
        int index = 0, scanned = 0;
        bool more = false;
        foreach (var pair in CrewSim.system.dictShips) {
            if (OverBudget(start)) { more = true; break; }
            if (index < offset) { index++; continue; }
            if (scanned >= limit) { more = true; break; }
            index++;
            scanned++;
            Ship ship = pair.Value;
            if (!StructurallyEligible(ship, playerShips, plotShips)) continue;
            double distanceKm = DistanceKm(ship, station);
            if (double.IsNaN(distanceKm) || double.IsInfinity(distanceKm) || distanceKm < minDistanceKm) continue;
            items.Add(Map("id", ship.strRegID, "name", ship.publicName, "distanceKm", distanceKm,
                "loadState", ship.LoadState.ToString(), "crewCount", ship.GetPeople(bAllowDocked: false)?.Count,
                "note", "POST rechecks docking, plot, player ownership and crew pruneability before removal."));
        }
        if (more && index < offset) return new InspectionApi.Reply(503, Map("error", "Offset scan exceeded budget; use a smaller offset."));
        return new InspectionApi.Reply(200, Map("referenceStationId", station.strRegID, "referenceStationName", station.publicName,
            "minimumDistanceKm", minDistanceKm, "items", items, "scanned", scanned, "nextOffset", more ? (object)index : null,
            "note", "Preview only. Lists unanchored derelicts outside plot/player protection; crew pruneability is checked only by the POST route."));
    }

    internal static InspectionApi.Reply Prune(string stationId, double minDistanceKm, int maxShips, string confirm, long start) {
        if (confirm != "PRUNE") return new InspectionApi.Reply(400, Map("error", "POST requires confirm=PRUNE."));
        Ship station = FindStation(stationId);
        if (station == null) return new InspectionApi.Reply(404, Map("error", "Reference station was not found in the active simulation."));
        if (CrewSim.system?.dictShips == null) return new InspectionApi.Reply(503, Map("error", "No active ship registry."));

        var ships = new List<Ship>(CrewSim.system.dictShips.Values);
        var playerShips = PlayerShips();
        var plotShips = PlotShips();
        CondTrigger prunable = DataHandler.GetCondTrigger("TIsNPCPrunable");
        var removedIds = new List<string>();
        var skipped = new List<object>();
        int scanned = 0, matches = 0, removedCrew = 0;
        bool scanIncomplete = false;

        for (int i = 0; i < ships.Count; i++) {
            if (OverBudget(start)) { scanIncomplete = true; break; }
            scanned++;
            Ship ship = ships[i];
            if (!StructurallyEligible(ship, playerShips, plotShips)) continue;
            double distanceKm = DistanceKm(ship, station);
            if (double.IsNaN(distanceKm) || double.IsInfinity(distanceKm) || distanceKm < minDistanceKm) continue;
            matches++;
            List<CondOwner> crew = ship.GetPeople(bAllowDocked: false);
            bool allPrunable = crew != null;
            if (crew != null && prunable == null && crew.Count > 0) allPrunable = false;
            if (allPrunable && crew != null) {
                for (int j = 0; j < crew.Count; j++) {
                    CondOwner person = crew[j];
                    if (person == null || !prunable.Triggered(person, logOutcome: false)) { allPrunable = false; break; }
                }
            }
            if (!allPrunable) {
                if (skipped.Count < 25) skipped.Add(Map("id", ship.strRegID, "name", ship.publicName, "distanceKm", distanceKm, "reason", "Crew list unavailable or contains non-pruneable crew."));
                continue;
            }

            for (int j = 0; j < crew.Count; j++) {
                CondOwner person = crew[j];
                person.RemoveFromCurrentHome(bForce: true);
                person.Destroy();
                removedCrew++;
            }
            List<CondOwner> remaining = ship.GetPeople(bAllowDocked: false);
            if (remaining == null || remaining.Count != 0) {
                if (skipped.Count < 25) skipped.Add(Map("id", ship.strRegID, "name", ship.publicName, "distanceKm", distanceKm, "reason", "Crew remained after cleanup; hull kept."));
                continue;
            }

            string id = ship.strRegID;
            ship.ToggleVis(bShow: false);
            ship.Destroy(isDespawning: false);
            removedIds.Add(id);
            ConsoleToGUI.instance?.LogInfo($"[InspectionAPI] Pruned derelict {id} ({distanceKm:N0} km from {station.strRegID}).");
            if (removedIds.Count >= maxShips) { scanIncomplete = i + 1 < ships.Count; break; }
        }

        return new InspectionApi.Reply(200, Map("referenceStationId", station.strRegID, "minimumDistanceKm", minDistanceKm,
            "scanned", scanned, "matchingDerelicts", matches, "removedShips", removedIds.Count, "removedCrew", removedCrew,
            "removedIds", removedIds, "skipped", skipped, "scanIncomplete", scanIncomplete,
            "note", "Only unanchored non-plot derelicts were considered. Player-owned and player-docked ships, body-locked ships, and ships with non-pruneable crew were kept. Repeat POST to continue if scanIncomplete is true."));
    }
}
