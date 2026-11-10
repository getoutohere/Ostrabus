using System;
using System.Collections.Generic;

namespace Ostrabus.Inspection;

internal static class ShipPruning {
    internal static bool IsRoute(string path) => path != null && path.Length >= 13 && path.StartsWith("/ships/", StringComparison.Ordinal) && path.EndsWith("/prune", StringComparison.Ordinal);
    internal static bool Parse(InspectionHttp http, out string id, out bool dryRun, out bool force, out string error) {
        id = error = null; dryRun = force = false;
        string path = http.Url.AbsolutePath.TrimEnd('/');
        if (!IsRoute(path)) {
            if (http.QueryString["dryRun"] != null || http.QueryString["force"] != null) { error = "dryRun and force are only valid on POST /ships/{callSign}/prune."; return false; }
            return true;
        }
        if (http.HttpMethod != "POST") { error = "Ship pruning requires POST."; return false; }
        try { id = Uri.UnescapeDataString(path.Substring(7, path.Length - 7 - 6)); }
        catch (UriFormatException) { error = "Invalid call sign."; return false; }
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64 || id.IndexOf('/') >= 0 || id.IndexOf('\\') >= 0) { error = "Expected one exact registry call sign, up to 64 characters."; return false; }
        foreach (char c in id) if (char.IsControl(c) || char.IsWhiteSpace(c)) { error = "Invalid call sign."; return false; }
        foreach (string key in http.QueryString.AllKeys) if (key != "confirm" && key != "dryRun" && key != "force") { error = "Ship pruning accepts only confirm, dryRun and force."; return false; }
        if (http.QueryString["dryRun"] != null && !bool.TryParse(http.QueryString["dryRun"], out dryRun)) { error = "dryRun must be true or false."; return false; }
        if (http.QueryString["force"] != null && !bool.TryParse(http.QueryString["force"], out force)) { error = "force must be true or false."; return false; }
        if (!dryRun && http.QueryString["confirm"] != "PRUNE") { error = "POST requires confirm=PRUNE, or dryRun=true for eligibility only."; return false; }
        return true;
    }
    static object Map(params object[] pairs) => SnapshotJson.Map(pairs);
    internal static InspectionApi.Reply Run(string id, bool dryRun, string confirm, bool force = false) {
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
        bool connected = ship.HasDockGroup || ship.IsDocked();
        if (reason == null && connected && !force) reason = "Docked or tethered ships must be disconnected before pruning, or use force=true.";
        List<string> plots = null;
        if (reason == null) {
            plots = PlotManager.GetShipsInPlots();
            if (plots == null) reason = "Plot protection could not be checked.";
            else if (plots.Contains(id)) reason = "Ship is involved in an active plot.";
        }
        List<Ship> group = null;
        Dictionary<string, Ship> links = null;
        if (reason == null && connected) reason = DetachPreflight(ship, system, owned, plots, out group, out links);
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
            "dryRun", dryRun, "force", force, "eligible", false, "removed", false, "reason", reason));
        string name = ship.publicName;
        int count = crew.Count;
        var detachFrom = new List<string>();
        if (links != null) foreach (var peer in links.Values) if (!detachFrom.Contains(peer.strRegID)) detachFrom.Add(peer.strRegID);
        if (dryRun) return new InspectionApi.Reply(200, Map("id", id, "name", name, "dryRun", true,
            "force", force, "eligible", true, "removed", false, "pruneableCrew", count, "detachFrom", detachFrom));
        if (links != null) {
            try {
                // Native calls own reciprocal links, mooring hardware and group
                // teardown. Never delete entries from these collections ourselves.
                foreach (var link in links) {
                    ship.ToggleTowBracesShallow(false, link.Value.strRegID);
                    if (link.Key.Contains("MP|")) ship.UnMoorFrom(link.Value);
                    else ship.Undock(link.Value);
                }
            } catch (Exception ex) {
                return DetachFailed(id, ex.GetType().Name + ": " + ex.Message, links, ship);
            }
            if (ship.HasDockGroup || ship.IsDocked()) return DetachFailed(id, "Native detachment was incomplete.", links, ship);
            foreach (var peer in group) {
                if (ReferenceEquals(peer, ship)) continue;
                foreach (var remainingPeer in peer.GetDockedShipsAndPortIDs().Values) if (ReferenceEquals(remainingPeer, ship))
                    return DetachFailed(id, "A reciprocal docking link remains.", links, ship);
                if (peer.HasDockGroup) foreach (var member in peer.DockGroup.DockedShips) if (member.RegId == id)
                    return DetachFailed(id, "A docking-group reference remains.", links, ship);
            }
            // Re-run every normal ownership/plot/crew check after native callbacks,
            // then use the single existing deletion path. Peers are not pruned.
            var reply = Run(id, false, confirm);
            var body = (Dictionary<string, object>)reply.Body;
            body["force"] = true; body["detachedFrom"] = detachFrom;
            return reply;
        }
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
            "dryRun", false, "force", force, "removed", removed, "removedCrew", count));
    }

    static string DetachPreflight(Ship ship, StarSystem system, List<string> owned, List<string> plots,
        out List<Ship> group, out Dictionary<string, Ship> direct) {
        group = new List<Ship> { ship }; direct = null;
        var seen = new HashSet<string>(StringComparer.Ordinal) { ship.strRegID };
        var allLinks = new Dictionary<string, Dictionary<string, Ship>>(StringComparer.Ordinal);
        int edges = 0;
        for (int i = 0; i < group.Count; i++) {
            var member = group[i];
            if (member.objSS == null || member.bDestroyed || member.LoadState != Ship.Loaded.Shallow) return "Forced detachment requires a fully resolved shallow ship group.";
            if (member.IsStation() || member.IsStationHidden() || member.objSS.bIsBO) return "Connected stations and body objects are protected.";
            if (ReferenceEquals(CrewSim.coPlayer.ship, member) || owned != null && owned.Contains(member.strRegID)) return "Connected player ships are protected.";
            if (plots.Contains(member.strRegID)) return "A connected ship is involved in an active plot.";
            var ports = member.GetDockedShipsAndPortIDs(); allLinks.Add(member.strRegID, ports); edges += ports.Count;
            if (ReferenceEquals(member, ship)) direct = ports;
            if (ports.Count == 0 && (member.IsDocked() || member.HasDockGroup)) return "Docking state has no resolved direct links.";
            foreach (var peer in ports.Values) {
                if (peer == null || peer.bDestroyed || string.IsNullOrEmpty(peer.strRegID) || ReferenceEquals(peer, member) ||
                    !system.dictShips.TryGetValue(peer.strRegID, out var registered) || !ReferenceEquals(peer, registered)) return "A connected ship reference cannot be resolved.";
                if (seen.Add(peer.strRegID)) {
                    if (group.Count >= 32) return "Connected group exceeds the 32-ship forced-detachment limit.";
                    group.Add(peer);
                }
            }
        }
        foreach (var member in group) {
            foreach (var peer in allLinks[member.strRegID].Values) if (!allLinks[peer.strRegID].ContainsValue(member)) return "Docking links are not reciprocal.";
            if (member.HasDockGroup) foreach (var entry in member.DockGroup.DockedShips) if (!seen.Contains(entry.RegId)) return "Docking group contains unresolved members.";
        }
        // Native ResetDockedCache recursively walks the graph before detaching;
        // cycles or duplicate connections can make that walk recurse indefinitely.
        if (edges != 2 * (group.Count - 1)) return "Cyclic or duplicate docking connections cannot be safely detached by native code.";
        return null;
    }
    static InspectionApi.Reply DetachFailed(string id, string reason, Dictionary<string, Ship> links, Ship ship) {
        var detached = new List<string>();
        var remaining = ship.GetDockedShipsAndPortIDs();
        foreach (var peer in links.Values) if (!remaining.ContainsValue(peer) && !detached.Contains(peer.strRegID)) detached.Add(peer.strRegID);
        return new InspectionApi.Reply(409, Map("id", id, "force", true, "eligible", false, "removed", false,
            "removedCrew", 0, "detachedFrom", detached, "reason", reason + " No crew or hull deletion performed."));
    }
}
