using System;
using System.Collections.Generic;
using System.Diagnostics;
using HarmonyLib;
using Ostranauts.Ships.AIPilots;

namespace Ostrabus.Inspection;

internal static class InspectionSnapshots {
    static readonly AccessTools.FieldRef<Ship, double> ShallowMass = AccessTools.FieldRefAccess<Ship, double>("fShallowMass");
    static bool OverBudget(long start) { return Stopwatch.GetTimestamp() - start >= Stopwatch.Frequency / 1000; }
    static Dictionary<string, object> Map(params object[] pairs) { return SnapshotJson.Map(pairs); }
    static InspectionApi.Reply Reply(int status, object body) { return new InspectionApi.Reply(status, body); }

    internal static InspectionApi.Reply Query(string path, int offset, int limit, long start, double radiusKm = 500) {
        var ships = CrewSim.system.dictShips;
        if (path == "/ai/controllers") return ControllerInspection.Capture(start).Page(offset,limit);
        if (path.StartsWith("/ships/",StringComparison.Ordinal) && path.EndsWith("/controllers",StringComparison.Ordinal)) {
            if (path.Length<=19) return Reply(400,Map("error","Expected a ship registry ID."));
            string id=Uri.UnescapeDataString(path.Substring(7,path.Length-7-12));
            return ControllerInspection.Capture(start).Page(offset,limit,id);
        }
        if (path == "/player") { return Reply(200, PlayerInspection.Player()); }
        if (path == "/ui/scrollbacks") { return Reply(200, global::Ostrabus.NavConsole.Scrollbacks.Snapshot()); }
        if (path == "/ui/hulls") { return Reply(200, global::Ostrabus.NavConsole.Outlines.HullDisplay.Inspect()); }
        if (path == "/player/nearby") { return PlayerInspection.Nearby(offset, limit, radiusKm, start); }
        if (path == "/shipboard") { return Reply(200, global::Ostrabus.ShipboardAction.ShipboardActionSimulation.InspectionSnapshot()); }
        if (path == "/world") {
            return Reply(200, Map("ships", ships.Count, "conditionOwners", DataHandler.mapCOs?.Count,
                "aiRegions", AIShipManager.dictAIs?.Count, "paused", CrewSim.Paused, "player", PlayerInspection.Player()));
        }
        if (path == "/ships" || path == "/npcs") {
            var items = new List<object>();
            int index = 0;
            bool more = false;
            // Offset pagination reflects current dictionary order, not a persistent cursor.
            if (path == "/ships") {
                foreach (var pair in ships) {
                    if (OverBudget(start)) { more = true; break; }
                    if (index < offset) { index++; continue; }
                    if (items.Count >= limit) { more = true; break; }
                    items.Add(ShipSummary(pair.Key, pair.Value)); index++;
                }
            } else if (DataHandler.mapCOs != null) {
                foreach (var pair in DataHandler.mapCOs) {
                    if (OverBudget(start)) { more = true; break; }
                    if (index < offset) { index++; continue; }
                    if (items.Count >= limit) { more = true; break; }
                    var co = pair.Value;
                    // Scan cursor counts all COs, including non-person objects.
                    if (co != null && co.pspec != null) { items.Add(COSummary(pair.Key, co)); }
                    index++;
                    if (index - offset >= limit) { more = true; break; }
                }
            }
            if (more && index < offset) { return Reply(503, Map("error", "Offset scan exceeded the main-thread budget; use a direct ID lookup.")); }
            return Reply(200, Map("items", items, "scannedThrough", index, "nextOffset", more ? (object)index : null,
                "pagination", "Live dictionary order; mutations between requests can skip or repeat entries."));
        }
        if (path.StartsWith("/ships/", StringComparison.Ordinal)) {
            string idPath = path.Substring(7);
            Ship peerShip = null;
            bool commsOnly = false;
            int pairMarker = idPath.IndexOf("/comms/", StringComparison.Ordinal);
            if (pairMarker >= 0) {
                string peerId = Uri.UnescapeDataString(idPath.Substring(pairMarker + 7));
                idPath = idPath.Substring(0, pairMarker);
                if (!ships.TryGetValue(peerId, out peerShip) || peerShip == null) { return Reply(404, Map("error", "Comms peer ship not found.")); }
                commsOnly = true;
            } else if (idPath.EndsWith("/comms", StringComparison.Ordinal)) {
                idPath = idPath.Substring(0, idPath.Length - 6);
                commsOnly = true;
            }
            string id = Uri.UnescapeDataString(idPath);
            bool factionsOnly = id.EndsWith("/factions", StringComparison.Ordinal);
            if (factionsOnly) { id = id.Substring(0, id.Length - 9); }
            bool aiOnly = id.EndsWith("/ai", StringComparison.Ordinal);
            if (aiOnly) { id = id.Substring(0, id.Length - 3); }
            if (!ships.TryGetValue(id, out Ship ship) || ship == null) { return Reply(404, Map("error", "Ship not found.")); }
            if (factionsOnly) { return Reply(200, CombatInspection.Factions(ship, start)); }
            if (commsOnly) { return Reply(200, PlayerInspection.Comms(ship, peerShip, offset, limit, start)); }
            if (aiOnly) { return Reply(200, AI(ship, start)); }
            var data = ShipSummary(id, ship);
            data.Add("interiorGravityG", ship.Gravity);
            data.Add("needsGravityInitialization", ship.bNeedsGravSet);
            data.Add("storedShallowFuelKg", ship.fShallowRCSRemass);
            data.Add("storedShallowCapacityKg", ship.fShallowRCSRemassMax);
            data.Add("storedShallowMassKg", ShallowMass(ship));
            data.Add("stationDockedCollisionExempt", global::Ostrabus.ShipNavigation.StationDockedCollisions.IsExempt(ship));
            data.Add("fuelIsCurrent", ship.LoadState <= Ship.Loaded.Shallow);
            data.Add("fuelNote", "Raw stored fields. Full/Edit ship values may be stale; no canister scan or fallback capacity calculation performed.");
            data.Add("aiPauseTimer", ship.fAIPauseTimer);
            data.Add("aiDockingExpire", ship.fAIDockingExpire);
            data.Add("scanTarget", ship.shipScanTarget?.strRegID);
            data.Add("combatTarget", ship.shipCombatTarget?.strRegID);
            data.Add("navigation", NavigationInspection.ShipRoute(ship, start));
            data.Add("position", PlayerInspection.Position(ship));
            data.Add("relativeToSelectedCrew", PlayerInspection.Relative(PlayerInspection.SelectedShip(), ship));
            // Saved direct dock links only; avoid GetAllDockedShips recursive traversal.
            var docked = new List<object>();
            bool truncated = false;
            if (ship.json?.aDocked != null) {
                foreach (var pair in ship.json.aDocked) {
                    if (docked.Count >= 32 || OverBudget(start)) { truncated = true; break; }
                    docked.Add(Map("port", pair.Key, "ship", pair.Value));
                }
            }
            data.Add("savedDockLinks", docked);
            data.Add("savedDockLinksTruncated", truncated);
            data.Add("savedDockLinksNote", "Saved representation; may be stale for Full/Edit ships.");
            return Reply(200, data);
        }
        if (path.StartsWith("/npcs/", StringComparison.Ordinal) || path.StartsWith("/cos/", StringComparison.Ordinal)) {
            int prefix = path.StartsWith("/cos/", StringComparison.Ordinal) ? 5 : 6;
            string idPath = path.Substring(prefix);
            string npcView = null;
            foreach (string view in new[] { "conditions", "activity", "combat" }) {
                string suffix = "/" + view;
                if (idPath.EndsWith(suffix, StringComparison.Ordinal)) { idPath = idPath.Substring(0, idPath.Length - suffix.Length); npcView = view; break; }
            }
            string id = Uri.UnescapeDataString(idPath);
            if (DataHandler.mapCOs == null || !DataHandler.mapCOs.TryGetValue(id, out CondOwner co)) { return Reply(404, Map("error", "CO ID is absent from mapCOs.")); }
            if (co == null) { return Reply(200, Map("id", id, "resolved", false, "note", "Entry exists but CO is null or Unity-destroyed.")); }
            if (npcView == "conditions") { return Reply(200, NpcInspection.Conditions(co, offset, limit, start)); }
            if (npcView == "activity") { return Reply(200, NpcInspection.Activity(co, offset, limit, start)); }
            if (npcView == "combat") { return Reply(200, PersonnelCombatInspection.Snapshot(co)); }
            var data = COSummary(id, co);
            var conditions = new List<object>();
            bool truncated = false;
            if (co.mapConds != null) {
                foreach (var pair in co.mapConds) {
                    if (conditions.Count >= 32 || OverBudget(start)) { truncated = true; break; }
                    conditions.Add(Map("name", pair.Key, "amount", pair.Value == null ? null : (object)pair.Value.fCount));
                }
            }
            data.Add("conditions", conditions);
            data.Add("conditionCount", co.mapConds?.Count);
            data.Add("conditionsTruncated", truncated);
            data.Add("currentInteraction", co.GetInteractionCurrent()?.strName);
            data.Add("autoVent", QoL.AutoVents.AutoVentControl.Inspect(co));
            data.Add("factions", CombatInspection.PersonFactions(co, start));
            return Reply(200, data);
        }
        return Reply(404, Map("error", "Unknown endpoint.", "routes", "/health /logs /profiling/load /events/docking /events/ai /events/combat /shipboard /console /world /player /player/nearby /ships /ships/{id} /ships/{id}/ai GET|POST /ships/{id}/ai/restore GET|POST /ships/{id}/refuel GET|POST /ships/{id}/hauler/rescue GET|POST /ships/{id}/arrival[/{stationId}] /ships/{id}/factions /ships/{id}/comms /ships/{id}/comms/{peerId} POST /ships/{id}/prune /npcs /npcs/{id} /cos/{id}/conditions /cos/{id}/activity /cos/{id}/combat /combat/personnel/trace POST /combat/personnel/trace/start POST /combat/personnel/trace/stop POST /micrometeoroids/strike /derelicts /derelicts/prune"));
    }

    static Dictionary<string, object> ShipSummary(string id, Ship ship) {
        if (ship == null) { return Map("id", id, "resolved", false); }
        return Map("id", id, "name", ship.publicName, "loadState", ship.LoadState.ToString(), "destroyed", ship.bDestroyed, "isAI", ship.IsAIShip);
    }

    static Dictionary<string, object> COSummary(string id, CondOwner co) {
        return Map("id", id, "name", co.strName, "resolved", true, "destroyed", co.bDestroyed,
            "alive", co.bAlive, "hasPersonSpec", co.pspec != null, "ship", co.ship?.strRegID);
    }

    static object AI(Ship ship, long start) {
        var index=ControllerInspection.Capture(start);
        var data=index.Snapshot(ship.strRegID,ship);
        var ai=index.Single(ship.strRegID,ship);
        data["region"]=index.Region(ai);
        data["homeStation"]=ai?.HomeStation;
        data["activeCommand"]=null;
        if (ai != null) try { data["activeCommand"]=ai.ActiveCommandName; } catch (NullReferenceException) { }
        data["incomplete"]=!index.Complete;data["examined"]=index.Scanned;
        data["aiPauseTimer"]=ship.fAIPauseTimer;data["aiDockingExpire"]=ship.fAIDockingExpire;
        data["navigation"]=NavigationInspection.ShipRoute(ship,start,index);
        data["commands"]=NavigationInspection.Commands(ai,start);
        data["haulerRecovery"]=Ostrabus.Haulers.HaulerRecovery.Snapshot(ship);
        data["combat"]=ai == null ? null : CombatInspection.AI(ship,ai,start);
        return data;
    }
}
