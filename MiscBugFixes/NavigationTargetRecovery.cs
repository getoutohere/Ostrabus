using System;
using HarmonyLib;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;

namespace Ostrabus.MiscBugFixes;

// Explicit recovery for damage already saved before ShipReplacementTargetsPatch.
// No recurring scan, call-sign allowlist or travel-distance/duration threshold.
internal static class NavigationTargetRecovery {
    static readonly AccessTools.FieldRef<AIShip, Ship> PilotShip = AccessTools.FieldRefAccess<AIShip, Ship>("_shipUs");
    static bool HasPilot(Ship ship) {
        if (AIShipManager.dictAIs == null) return false;
        foreach (var pilots in AIShipManager.dictAIs.Values) {
            if (pilots == null) continue;
            foreach (AIShip pilot in pilots) if (pilot != null && ReferenceEquals(PilotShip(pilot), ship) && pilot.AICharacter != null) return true;
        }
        return false;
    }
    internal static bool HasCorruptReference(Ship ship) => ship?.shipSituTarget != null &&
        ship.shipSituTarget.bIsBO && ship.shipSituTarget.strBOPORShip == null;

    internal static bool Candidate(Ship ship, StarSystem system, out Ship destination, out string reason) {
        destination = null;
        reason = null;
        if (!HasCorruptReference(ship)) { reason = "no cleared orbital-body target reference"; return false; }
        if (system == null || ship.bDestroyed || ship.objSS == null || !ship.IsAIShip || ship.HideFromSystem) {
            reason = "not an active AI vessel"; return false;
        }
        if (ship.objSS.bIsBO || ship.objSS.bBOLocked || ship.objSS.bOrbitLocked || ship.HasDockGroup || ship.IsDocked()) {
            reason = "vessel is anchored or connected"; return false;
        }
        if (ship.NavPlayerManned || ReferenceEquals(CrewSim.coPlayer?.ship, ship)) { reason = "player-controlled vessel"; return false; }
        if (ship.shipCombatTarget != null) { reason = "combat target is active"; return false; }
        string id = ship.targetRegID;
        if (string.IsNullOrEmpty(id) || ship.shipScanTarget == null || ship.shipScanTarget.strRegID != id ||
            (ship.TargetData?.TargetRegId != null && ship.TargetData.TargetRegId != id)) {
            reason = "destination identity is missing or inconsistent"; return false;
        }
        destination = system.GetShipByRegID(id);
        ShipSitu target = destination?.objSS;
        if (destination == null || ReferenceEquals(destination, ship) || destination.bDestroyed || destination.HideFromSystem ||
            target == null || !target.bIsBO || string.IsNullOrEmpty(target.strBOPORShip) || system.GetBO(target.strBOPORShip) == null) {
            reason = "named destination has no valid orbital-body situation"; return false;
        }
        ShipSitu stale = ship.shipSituTarget;
        if (stale.IsRegionBorderPoint || stale.vBOOffsetx != target.vBOOffsetx || stale.vBOOffsety != target.vBOOffsety) {
            reason = "independent offset/border waypoint requires separate diagnosis"; return false;
        }
        if (!Finite(target.vPosx) || !Finite(target.vPosy) || !Finite(target.vVelX) || !Finite(target.vVelY)) {
            reason = "named destination has non-finite motion"; return false;
        }
        if (!Finite(ship.objSS.vPosx) || !Finite(ship.objSS.vPosy) || !Finite(ship.objSS.vVelX) || !Finite(ship.objSS.vVelY)) {
            reason = "vessel has non-finite motion"; return false;
        }
        if (!HasPilot(ship)) { reason = "no registered AI pilot to resume navigation"; return false; }
        return true;
    }
    static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    internal static bool Recover(Ship ship, StarSystem system, out string reason) {
        if (!Candidate(ship, system, out Ship destination, out reason)) return false;
        // Stop the old route's controls without changing position, velocity or fuel.
        global::Ostrabus.ShipNavigation.NavigationRoute.Stop(ship);
        new TargetData(destination).ApplyToShip(ship);
        return true;
    }

    internal static void Run(bool apply) {
        StarSystem system = CrewSim.system;
        var console = ConsoleToGUI.instance;
        if (system == null) { console?.LogInfo("[NavFix] No loaded world."); return; }
        int found = 0, eligible = 0, recovered = 0, skipped = 0;
        foreach (Ship ship in system.dictShips.Values) {
            if (!HasCorruptReference(ship)) continue;
            found++;
            try {
                if (!Candidate(ship, system, out Ship destination, out string reason)) {
                    skipped++; console?.LogInfo($"[NavFix] {ship.strRegID}: skipped ({reason})."); continue;
                }
                eligible++;
                if (apply && !Recover(ship, system, out reason)) {
                    skipped++; console?.LogInfo($"[NavFix] {ship.strRegID}: skipped ({reason})."); continue;
                }
                if (apply) recovered++;
                console?.LogInfo($"[NavFix] {ship.strRegID} -> {destination.strRegID}: {(apply ? "target rebound; old route cleared; native AI will replan" : "eligible; no changes made")}.");
                if (ship.LoadState <= Ship.Loaded.Shallow && ship.fShallowRCSRemass <= 0)
                    console?.LogInfo($"[NavFix] {ship.strRegID}: no stored RCS fuel; target recovery does not make a return flight feasible.");
            } catch (Exception ex) {
                skipped++;
                console?.LogInfo($"[NavFix] {ship.strRegID}: recovery failed ({ex.Message}).");
                Ostrabus.OstrabusPlugin.Logger?.LogWarning($"[NavFix] {ship.strRegID}: {ex}");
            }
        }
        console?.LogInfo($"[NavFix] {(apply ? "Apply" : "Dry run")}: {found} corrupt targets, {eligible} eligible, {recovered} recovered, {skipped} skipped.{(apply ? "" : " Use navfix apply to recover eligible ships.")}");
    }
}

[HarmonyPatch(typeof(ConsoleResolver), "ResolveString")]
internal static class NavigationTargetRecoveryCommand {
    [HarmonyPrefix]
    internal static bool Prefix(string strInput) {
        string input = strInput?.Trim();
        if (string.Equals(input, "navfix", StringComparison.OrdinalIgnoreCase) || string.Equals(input, "navfix dryrun", StringComparison.OrdinalIgnoreCase)) {
            NavigationTargetRecovery.Run(false); return false;
        }
        if (string.Equals(input, "navfix apply", StringComparison.OrdinalIgnoreCase)) {
            NavigationTargetRecovery.Run(true); return false;
        }
        return true;
    }
}
