using System;
using System.Collections.Generic;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using Ostrabus.ShipNavigation;

namespace Ostrabus.Inspection;

// Explicit completion of a native station arrival. No new population, physics,
// AI scheduler or saved-state mechanism is introduced.
internal static class ShipArrival {
    static readonly AccessTools.FieldRef<PersonSpec, CondOwner> CachedCrew = AccessTools.FieldRefAccess<PersonSpec, CondOwner>("co");
    static readonly Action<BaseCommand, ShipSitu> Place = AccessTools.MethodDelegate<Action<BaseCommand, ShipSitu>>(AccessTools.Method(typeof(BaseCommand), "PlaceWithinDockingRange"));
    internal static bool IsRoute(string path) {
        if (path == null || !path.StartsWith("/ships/", StringComparison.Ordinal)) return false;
        int at = path.IndexOf("/arrival", 7, StringComparison.Ordinal);
        return at > 7 && (at+8 == path.Length || path[at+8] == '/');
    }
    static bool ValidId(string id) {
        if (string.IsNullOrEmpty(id) || id.Length > 64) return false;
        foreach (char c in id) if (char.IsControl(c) || char.IsWhiteSpace(c) || c == '/' || c == '\\') return false;
        return true;
    }
    static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    static InspectionApi.Reply Reply(int status, params object[] pairs) => new InspectionApi.Reply(status, SnapshotJson.Map(pairs));
    internal static InspectionApi.Reply Run(string path, bool apply) {
        var parts = path.Split('/');
        if ((parts.Length != 4 && parts.Length != 5) || parts[1] != "ships" || parts[3] != "arrival") return Reply(400, "error", "Expected /ships/{id}/arrival[/{stationId}].");
        string id, destination;
        try { id = Uri.UnescapeDataString(parts[2]); destination = parts.Length == 5 ? Uri.UnescapeDataString(parts[4]) : null; }
        catch (UriFormatException) { return Reply(400, "error", "Invalid registry ID."); }
        if (!ValidId(id) || destination != null && !ValidId(destination)) return Reply(400, "error", "Expected exact ship/station registry IDs.");
        var system = CrewSim.system; var player = CrewSim.coPlayer;
        if (system?.dictShips == null || AIShipManager.dictAIs == null || player == null || string.IsNullOrEmpty(player.strID) || PlotManager.dictPlotsActive == null)
            return Reply(503, "error", "Simulation, ownership or plot state unavailable.");
        if (!system.dictShips.TryGetValue(id, out var ship) || ship == null || ship.bDestroyed) return Reply(404, "error", "Ship not found.");
        var ai = AIShipManager.GetAIShipByRegID(id);
        string reason = null;
        bool hauler = ai != null && (ai.AIType == AIType.HaulerRetriever || ai.AIType == AIType.HaulerDeployer);
        if (ai?.AICharacter == null || ai.Blackboard == null || !ReferenceEquals(ai.Ship, ship) || (!hauler && ai.AIType != AIType.Police && ai.AIType != AIType.Scav))
            reason = "Only registered local haulers, police and salvagers are supported; cargo/passenger and combat roles are excluded.";
        else if (ship.objSS == null || ship.bRemove || ship.LoadState != Ship.Loaded.Shallow || !ship.IsAIShip || ship.HideFromSystem || ship.objSS.bIsBO || ship.IsStation() || ship.IsStationHidden())
            reason = "Source must be a visible shallow traffic ship.";
        else if (ReferenceEquals(player.ship, ship) || system.GetShipOwner(id) == player.strID) reason = "Player ships are protected.";
        destination = destination ?? ai?.HomeStation;
        var station = reason == null && ValidId(destination) ? system.GetShipByRegID(destination) : null;
        if (reason == null && (station?.objSS == null || station.bDestroyed || station.bRemove || station.HideFromSystem || !station.objSS.bIsBO || !station.IsStation() || ReferenceEquals(ship, station) ||
            !Finite(station.objSS.vPosx) || !Finite(station.objSS.vPosy) || !Finite(station.objSS.vVelX) || !Finite(station.objSS.vVelY)))
            reason = "Destination must be an available native body/station; specify its registry ID if native home is unavailable.";
        var plots = reason == null ? PlotManager.GetShipsInPlots() : null;
        if (reason == null && (plots == null || plots.Contains(id))) reason = "Active plot ships are protected.";
        var tows = new List<Ship>();
        if (reason == null) foreach (var tow in ship.GetAllDockedShips()) {
            if (tow?.objSS == null || tow.bDestroyed || tow.LoadState != Ship.Loaded.Shallow || !tow.IsDerelict() || tow.objSS.bIsBO || tow.IsStation() ||
                ReferenceEquals(tow, player.ship) || system.GetShipOwner(tow.strRegID) == player.strID || plots.Contains(tow.strRegID) ||
                (tow.People?.Count ?? 0) != 0 || AIShipManager.GetAIShipByRegID(tow.strRegID) != null || !tow.IsDockedWith(ship)) {
                reason = "Connected hulls must be directly attached, uncrewed non-player/non-plot shallow wrecks."; break;
            }
            tows.Add(tow);
        }
        if (reason == null && tows.Count == 0 && (ship.IsDocked() || ship.HasDockGroup)) reason = "Unresolved docking group; arrival refused.";
        var crew = new List<CondOwner>();
        if (reason == null && ship.People != null) foreach (var spec in ship.People) {
            var co = spec == null ? null : CachedCrew(spec);
            if (co == null || co.bDestroyed || !ReferenceEquals(co.ship, ship) || ReferenceEquals(co, player) || player.Company?.mapRoster?.ContainsKey(co.strID) == true) {
                reason = "Crew must be completely resolved and exclude player crew."; break;
            }
            crew.Add(co);
        }
        if (reason == null && crew.Count == 0) reason = "No existing crew to return; no NPCs will be created.";
        var dropoff = reason == null ? DataHandler.GetCondTrigger("TIsValidStationDropOff") : null;
        var factions = reason == null ? station.GetShipFactions() : null;
        if (reason == null && (dropoff == null || factions == null || factions.Count == 0)) reason = "Native crew drop-off or destination faction data unavailable.";
        if (reason == null) {
            var names = new List<string>(factions.Count); foreach (var faction in factions) names.Add(faction.strName);
            // Native retirement catches crew up before deciding whether to transfer
            // or destroy them. POST validates that same up-to-date state first.
            if (apply) foreach (var co in crew) co.CatchUp();
            foreach (var co in crew) if (co.bDestroyed || !co.bAlive || !ReferenceEquals(co.ship, ship) || !dropoff.Triggered(co, logOutcome:false) ||
                JsonFaction.GetReputation(co.GetFactionScore(names)) == JsonFaction.Reputation.Dislikes) {
                reason = "Native station arrival would reject/delete a crew member; no arrival performed."; break;
            }
        }
        if (reason == null && hauler && tows.Count > 0 && DataHandler.GetLoot("ShipRemoveHelpRequest") == null) reason = "Native tow-delivery cleanup is unavailable.";
        if (reason != null) return Reply(apply ? 409 : 200, "id", id, "eligible", false, "arrived", false, "reason", reason);
        var crewIds = new List<string>(crew.Count); foreach (var co in crew) crewIds.Add(co.strID);
        var towIds = new List<string>(tows.Count); foreach (var tow in tows) towIds.Add(tow.strRegID);
        var plan = SnapshotJson.Map("stationId", station.strRegID, "crewToTransfer", crewIds,
            "wrecksDeliveredAndDestroyed", hauler ? towIds : new List<string>(), "wrecksReleased", hauler ? new List<string>() : towIds,
            "nativeCommand", hauler ? "DockCargoAndDespawn" : "Dock", "instantArrival", true, "hullHiddenForNativeReuseOrCleanup", true,
            "crewPreserved", true, "refuel", false, "previewChecksCurrentState", true);
        if (!apply) return Reply(200, "id", id, "eligible", true, "arrived", false, "plan", plan);
        string nativeStatus = "NotStarted", failure = null;
        bool finished = false;
        try {
            StationReturn.Cancel(ai);
            NavigationRoute.Stop(ship);
            if (!hauler) {
                foreach (var tow in tows) { ship.ToggleTowBracesShallow(false, tow.strRegID); ship.Undock(tow); }
                if (ship.IsDocked() || ship.HasDockGroup) return Reply(409, "id", id, "arrived", false, "reason", "Native detachment incomplete; crew and hull have not been retired.", "plan", plan);
            }
            new TargetData(station).ApplyToShip(ship);
            BaseCommand command = hauler ? (BaseCommand)new DockCargoAndDespawn(ai.AICharacter) : new Dock(ai.AICharacter);
            Place(command, station.objSS);
            nativeStatus = "Executing";
            var outcome = command.ResolveInstantly(ai.Blackboard);
            nativeStatus = outcome.Status.ToString(); finished = outcome.Status == CommandCode.Finished;
        } catch (Exception error) {
            failure = error.GetType().Name+": "+error.Message;
            OstrabusPlugin.Logger?.LogWarning("[Station Arrival] "+id+" -> "+station.strRegID+": "+error);
        }
        bool preserved = true; foreach (var co in crew) if (co.bDestroyed || !co.bAlive || !ReferenceEquals(co.ship, station)) preserved = false;
        bool done = failure == null && finished && preserved && !ship.bDestroyed &&
            system.dictShips.TryGetValue(id, out var retained) && ReferenceEquals(retained, ship) &&
            ship.HideFromSystem && AIShipManager.GetAIShipByRegID(id) == null && ship.strAIDespawnedAt == station.strRegID;
        var wreckResults = new List<object>(tows.Count); foreach (var tow in tows) {
            wreckResults.Add(SnapshotJson.Map("id", tow.strRegID, "destroyed", tow.bDestroyed));
            if (hauler ? !tow.bDestroyed : tow.bDestroyed) done = false;
        }
        return Reply(done ? 200 : 500, "id", id, "eligible", true, "arrived", done, "crewPreservedAtStation", preserved,
            "nativeStatus", nativeStatus, "error", failure, "hullHidden", ship.HideFromSystem, "wrecks", wreckResults, "plan", plan,
            "note", done ? "Native arrival completed; original crew are station residents. Hull follows native reuse/cleanup policy." : "Native operation did not reach all expected postconditions; inspect current state before retrying.");
    }
}
