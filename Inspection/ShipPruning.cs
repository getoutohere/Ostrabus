using System;
using System.Collections.Generic;

namespace Ostrabus.Inspection;

internal static class ShipPruning {
    internal static bool IsRoute(string path) => path != null && path.Length >= 13 && path.StartsWith("/ships/", StringComparison.Ordinal) && path.EndsWith("/prune", StringComparison.Ordinal);
    internal static bool Parse(InspectionHttp http, out string id, out bool dryRun, out string error) {
        id = error = null; dryRun = false;
        string path = http.Url.AbsolutePath.TrimEnd('/');
        if (!IsRoute(path)) {
            if (http.QueryString["dryRun"] != null) { error = "dryRun is only valid on POST /ships/{callSign}/prune."; return false; }
            return true;
        }
        if (http.HttpMethod != "POST") { error = "Ship pruning requires POST."; return false; }
        try { id = Uri.UnescapeDataString(path.Substring(7, path.Length - 7 - 6)); }
        catch (UriFormatException) { error = "Invalid call sign."; return false; }
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64 || id.IndexOf('/') >= 0 || id.IndexOf('\\') >= 0) { error = "Expected one exact registry call sign, up to 64 characters."; return false; }
        foreach (char c in id) if (char.IsControl(c) || char.IsWhiteSpace(c)) { error = "Invalid call sign."; return false; }
        foreach (string key in http.QueryString.AllKeys) if (key != "confirm" && key != "dryRun") { error = "Ship pruning accepts only confirm and dryRun."; return false; }
        if (http.QueryString["dryRun"] != null && !bool.TryParse(http.QueryString["dryRun"], out dryRun)) { error = "dryRun must be true or false."; return false; }
        if (!dryRun && http.QueryString["confirm"] != "PRUNE") { error = "POST requires confirm=PRUNE, or dryRun=true for eligibility only."; return false; }
        return true;
    }
    static object Map(params object[] pairs) => SnapshotJson.Map(pairs);
    internal static InspectionApi.Reply Run(string id, bool dryRun, string confirm) {
        if (!dryRun && confirm != "PRUNE") return new InspectionApi.Reply(400, Map("error", "POST requires confirm=PRUNE."));
        var system = CrewSim.system;
        if (system?.dictShips == null || CrewSim.coPlayer == null || string.IsNullOrEmpty(CrewSim.coPlayer.strID) || PlotManager.dictPlotsActive == null)
            return new InspectionApi.Reply(503, Map("error", "Ship registry, player or plot state is unavailable."));
        if (!system.dictShips.TryGetValue(id, out Ship ship) || ship == null || ship.bDestroyed)
            return new InspectionApi.Reply(404, Map("error", "Exact registry call sign not found.", "id", id));
        string reason = null;
        if (ship.objSS == null) reason = "Ship has no physical state.";
        else if (ship.IsStation() || ship.IsStationHidden() || ship.objSS.bIsBO) reason = "Stations and body objects are protected.";
        var owned = system.GetShipsForOwner(CrewSim.coPlayer.strID);
        if (reason == null && (ReferenceEquals(CrewSim.coPlayer.ship, ship) || owned != null && owned.Contains(id))) reason = "Player-owned ships are protected.";
        if (reason == null && (ship.HasDockGroup || ship.IsDocked())) reason = "Docked or tethered ships must be disconnected before pruning.";
        if (reason == null) {
            var plots = PlotManager.GetShipsInPlots();
            if (plots == null) reason = "Plot protection could not be checked.";
            else if (plots.Contains(id)) reason = "Ship is involved in an active plot.";
        }
        List<CondOwner> crew = null;
        if (reason == null) {
            crew = ship.GetPeople(bAllowDocked: false);
            if (crew == null || ship.People != null && ship.People.Count != crew.Count) reason = "Crew could not be completely resolved.";
            else if (crew.Count == 0 && !ship.IsAIShip && !ship.IsDerelict()) reason = "Uncrewed hull is neither traffic AI nor a derelict.";
            else if (crew.Count > 0) {
                CondTrigger trigger = DataHandler.GetCondTrigger("TIsNPCPrunable");
                if (trigger == null) reason = "Native TIsNPCPrunable condition is unavailable.";
                else foreach (CondOwner person in crew) if (person == null || !trigger.Triggered(person, logOutcome: false)) {
                    reason = "Crew contains a non-pruneable NPC (TIsNPCPrunable failed)."; break;
                }
            }
        }
        if (reason != null) return new InspectionApi.Reply(dryRun ? 200 : 409, Map("id", id, "name", ship.publicName,
            "dryRun", dryRun, "eligible", false, "removed", false, "reason", reason));
        string name = ship.publicName;
        int count = crew.Count;
        if (dryRun) return new InspectionApi.Reply(200, Map("id", id, "name", name, "dryRun", true,
            "eligible", true, "removed", false, "pruneableCrew", count));
        // All crew and structural checks complete before the first destructive call.
        foreach (CondOwner person in crew) { person.RemoveFromCurrentHome(bForce: true); person.Destroy(); }
        var remaining = ship.GetPeople(bAllowDocked: false);
        if (remaining == null || remaining.Count != 0) return new InspectionApi.Reply(409, Map("id", id, "eligible", false,
            "removed", false, "removedCrew", count, "reason", "Crew remained after cleanup; hull retained."));
        ship.ToggleVis(bShow: false);
        // Native destruction unregisters AI, removes the hull, and cleans its COs.
        ship.Destroy(isDespawning: false);
        bool removed = !system.dictShips.TryGetValue(id, out Ship retained) || !ReferenceEquals(retained, ship);
        ConsoleToGUI.instance?.LogInfo($"[InspectionAPI] Pruned {id} ({count} pruneable crew).");
        return new InspectionApi.Reply(removed ? 200 : 500, Map("id", id, "name", name, "eligible", true,
            "dryRun", false, "removed", removed, "removedCrew", count));
    }
}
