using System;
using HarmonyLib;
using Ostranauts.Ships.AIPilots;

namespace Ostrabus.Inspection;

// Explicit recovery of the stranded inspection-patrol state, not an AI-role
// guessing API or a background sweep of intentionally dormant ships.
internal static class ShipAIRecovery {
    const string Suffix = "/ai/restore";
    static readonly AccessTools.FieldRef<PersonSpec, CondOwner> CachedCrew = AccessTools.FieldRefAccess<PersonSpec, CondOwner>("co");
    internal static bool IsRoute(string path) => path != null && path.StartsWith("/ships/", StringComparison.Ordinal) && path.EndsWith(Suffix, StringComparison.Ordinal);
    internal static InspectionApi.Reply Run(string path, bool apply) {
        string id;
        try { id = Uri.UnescapeDataString(path.Substring(7, path.Length-7-Suffix.Length)); }
        catch (Exception ex) when (ex is UriFormatException || ex is ArgumentOutOfRangeException) { return Error(400, "Invalid registry ID."); }
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64 || id.IndexOf('/') >= 0 || id.IndexOf('\\') >= 0) return Error(400, "Expected one exact registry ID.");
        foreach (char c in id) if (char.IsControl(c) || char.IsWhiteSpace(c)) return Error(400, "Invalid registry ID.");
        var system = CrewSim.system;
        if (system?.dictShips == null || AIShipManager.dictAIs == null || CrewSim.coPlayer == null) return Error(503, "Simulation is not ready.");
        if (!system.dictShips.TryGetValue(id, out var ship) || ship == null || ship.bDestroyed) return Error(404, "Ship not found.");
        string reason = null, homeId = null;
        CondOwner officer = null;
        var existing = AIShipManager.GetAIShipByRegID(id);
        if (existing != null) reason = "Ship already has a controller; no controller or route was replaced.";
        else if (!ship.IsAIShip || !ship.IsLocalAuthority || ship.HideFromSystem || ship.objSS == null || ship.objSS.bIsBO || ship.IsStation() || ship.IsStationHidden()) reason = "Only visible orphaned local-police patrols are supported.";
        else if (ship.IsDocked() || ship.HasDockGroup) reason = "Ship is still connected; finish the boarding encounter first.";
        else if (ship.ShipCO == null || ship.ShipCO.HasCond("IsStale", isThreshold:false)) reason = "Retired/stale ships are not revived.";
        else if (ship.objSS.NavData != null || ship.shipScanTarget != null || ship.shipCombatTarget != null || ship.targetRegID != null || ship.shipSituTarget != null ||
            ship.objSS.vAccIn.magnitude != 0 || ship.objSS.vAccRCS.magnitude != 0) reason = "Ship has active navigation or thrust; recovery refused.";
        var owned = system.GetShipsForOwner(CrewSim.coPlayer.strID);
        if (reason == null && (ReferenceEquals(CrewSim.coPlayer.ship, ship) || owned?.Contains(id) == true)) reason = "Player ships are protected.";
        if (reason == null) {
            string owner = system.GetShipOwner(id);
            if (owner == null || !owner.EndsWith("LEO", StringComparison.Ordinal)) reason = "Cannot infer a police home from native ownership.";
            else {
                homeId = owner.Substring(0, owner.Length-3);
                if (homeId != AIShipManager.strATCLast || !system.dictShips.TryGetValue(homeId, out var home) || home == null || home.bDestroyed || !home.IsStation())
                    reason = "Native police home is unavailable or outside the current ATC region.";
            }
        }
        if (reason == null && ship.People != null) foreach (var person in ship.People) {
            var co = person == null ? null : CachedCrew(person);
            if (co != null && !co.bDestroyed && co.bAlive && ReferenceEquals(co.ship, ship) && !co.HasCond("Unconscious", isThreshold:false) &&
                co.HasCond("IsShakedownModeActive", isThreshold:false) && CrewSim.coPlayer.Company?.mapRoster?.ContainsKey(co.strID) != true) { officer = co; break; }
        }
        if (reason == null && officer == null) reason = "No living conscious inspection officer is aboard; role/state recovery is not justified.";
        var cleanup = reason == null ? DataHandler.GetLoot("CONDRemoveLEOAccuseOKLG") : null;
        if (reason == null && cleanup == null) reason = "Native inspection cleanup definition is unavailable.";
        if (reason != null) return new InspectionApi.Reply(apply ? 409 : 200, SnapshotJson.Map("id", id, "eligible", false, "restored", false, "reason", reason));
        if (!apply) return new InspectionApi.Reply(200, SnapshotJson.Map("id", id, "eligible", true, "restored", false, "aiType", "Police", "homeStation", homeId, "officer", officer.strID));
        var restored = AIShipManager.AddAIToShip(ship, AIType.Police, homeId);
        if (restored == null || !ReferenceEquals(restored.Ship, ship) || AIShipManager.GetAIShipByRegID(id) != restored)
            return Error(409, "Native registration did not retain a controller; no pilot was created or moved.");
        cleanup.ApplyCondLoot(officer, 1);
        officer.SetCondAmount("IsPledgeUndockDone", 1);
        return new InspectionApi.Reply(200, SnapshotJson.Map("id", id, "eligible", true, "restored", true,
            "aiType", restored.AIType.ToString(), "homeStation", restored.HomeStation, "officer", officer.strID));
    }
    static InspectionApi.Reply Error(int status, string message) => new InspectionApi.Reply(status, SnapshotJson.Map("error", message));
}
