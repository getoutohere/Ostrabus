using System;
using System.Collections.Generic;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using Ostrabus.ShipNavigation;

namespace Ostrabus.Inspection;

// Operator-requested repair of station-local haulers, not a simulation rule.
// Native return then transfers eligible crew home instead of pruning them.
internal static class HaulerRescue {
    const string Suffix = "/hauler/rescue";
    const double KmPerAU = 149597870.7, ApproachKm = 100;
    static readonly AccessTools.FieldRef<PersonSpec, CondOwner> CachedCrew = AccessTools.FieldRefAccess<PersonSpec, CondOwner>("co");
    static readonly AccessTools.FieldRef<AIShip, ICommand> Reaction = AccessTools.FieldRefAccess<AIShip, ICommand>("_activeReaction");
    static readonly AccessTools.FieldRef<Ship, double> Capacity = AccessTools.FieldRefAccess<Ship, double>("fShallowRCSRemassMax");
    internal static bool IsRoute(string path) => path != null && path.StartsWith("/ships/", StringComparison.Ordinal) && path.EndsWith(Suffix, StringComparison.Ordinal);
    static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);
    static InspectionApi.Reply Reply(int status, params object[] pairs) => new InspectionApi.Reply(status, SnapshotJson.Map(pairs));
    internal static InspectionApi.Reply Run(string path, bool apply) {
        string id;
        try { id = Uri.UnescapeDataString(path.Substring(7, path.Length-7-Suffix.Length)); }
        catch (Exception error) when (error is UriFormatException || error is ArgumentOutOfRangeException) { return Reply(400, "error", "Invalid registry ID."); }
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64 || id.IndexOf('/') >= 0 || id.IndexOf('\\') >= 0) return Reply(400, "error", "Expected one exact registry ID.");
        foreach (char c in id) if (char.IsControl(c) || char.IsWhiteSpace(c)) return Reply(400, "error", "Invalid registry ID.");
        var system = CrewSim.system;
        var player = CrewSim.coPlayer;
        if (system?.dictShips == null || player == null || string.IsNullOrEmpty(player.strID) || AIShipManager.dictAIs == null || PlotManager.dictPlotsActive == null)
            return Reply(503, "error", "Simulation, ownership or plot state is unavailable.");
        if (!system.dictShips.TryGetValue(id, out var ship) || ship == null || ship.bDestroyed) return Reply(404, "error", "Ship not found.");
        var ai = AIShipManager.GetAIShipByRegID(id);
        string reason = null;
        if (ai?.AICharacter == null || ai.Blackboard == null || !ReferenceEquals(ai.Ship, ship) ||
            (ai.AIType != AIType.HaulerRetriever && ai.AIType != AIType.HaulerDeployer)) reason = "A registered station-local retriever/deployer controller is required; cargo routes are excluded.";
        else if (ship.objSS == null || ship.LoadState != Ship.Loaded.Shallow || !ship.IsAIShip || ship.HideFromSystem || ship.IsStation() || ship.objSS.bIsBO ||
            ship.objSS.bBOLocked || ship.objSS.bOrbitLocked) reason = "Only visible, free shallow haulers can be rescued.";
        else if (ReferenceEquals(player.ship, ship) || system.GetShipOwner(id) == player.strID) reason = "Player ships are protected.";
        else if (ship.shipCombatTarget != null || ai.Blackboard.Get<bool>("InCombat") || Reaction(ai) != null) reason = "Active combat/reactions are protected.";
        else if (StationReturn.Pending(ai)) reason = "Ship already has a station return request; no repeat relocation performed.";
        else if (ai.AICharacter.PriorityQueue?.CanExecute(ai.Blackboard) == true) reason = "Queued/current priority work must finish first.";
        var home = reason == null && !string.IsNullOrEmpty(ai.HomeStation) ? system.GetShipByRegID(ai.HomeStation) : null;
        if (reason == null && (home?.objSS == null || home.bDestroyed || home.HideFromSystem || !home.IsStation() || ReferenceEquals(home, ship) || ai.HomeStation != AIShipManager.strATCLast))
            reason = "Native home station must be available in the current ATC region.";
        var plots = reason == null ? PlotManager.GetShipsInPlots() : null;
        if (reason == null && (plots == null || plots.Contains(id))) reason = "Active plot ships are protected.";
        var tows = new List<Ship>();
        if (reason == null) foreach (var tow in ship.GetAllDockedShips()) {
            if (tow?.objSS == null || tow.bDestroyed || tow.LoadState != Ship.Loaded.Shallow || !tow.IsDerelict() || tow.IsStation() || tow.objSS.bIsBO ||
                ReferenceEquals(tow, player.ship) || system.GetShipOwner(tow.strRegID) == player.strID || plots.Contains(tow.strRegID) ||
                (tow.People?.Count ?? 0) != 0 || AIShipManager.GetAIShipByRegID(tow.strRegID) != null) {
                reason = "Connected hulls must be uncrewed, non-player, non-plot shallow wrecks without controllers."; break;
            }
            tows.Add(tow);
        }
        var crew = new List<CondOwner>();
        bool conscious = false;
        var dropoff = reason == null ? DataHandler.GetCondTrigger("TIsValidStationDropOff") : null;
        if (reason == null && dropoff == null) reason = "Native station drop-off eligibility is unavailable.";
        if (reason == null) {
            var factions = home.GetShipFactions();
            if (factions == null) reason = "Home station factions are unavailable.";
            else {
                var names = new List<string>(factions.Count);
                foreach (var faction in factions) names.Add(faction.strName);
                if (ship.People != null) foreach (var spec in ship.People) {
                    var co = spec == null ? null : CachedCrew(spec);
                    if (co == null || co.bDestroyed || !co.bAlive || !ReferenceEquals(co.ship, ship) || ReferenceEquals(co, player) || player.Company?.mapRoster?.ContainsKey(co.strID) == true ||
                        !dropoff.Triggered(co, logOutcome:false) || JsonFaction.GetReputation(co.GetFactionScore(names)) == JsonFaction.Reputation.Dislikes) {
                        reason = "All crew must be resolved, alive, non-player and eligible for native home-station transfer without deletion."; break;
                    }
                    crew.Add(co);
                    if (!co.HasCond("Unconscious", isThreshold:false)) conscious = true;
                }
            }
            if (reason == null && (crew.Count == 0 || !conscious)) reason = "A living conscious pilot is required; no crew will be created.";
        }
        // GetRCSMax lazily writes an unknown shallow capacity. Preview must only
        // read the existing value; refuse unknown capacity rather than invent it.
        double fuel = reason == null ? Capacity(ship) : 0;
        if (reason == null && (!Finite(fuel) || fuel <= 0)) reason = "No usable native RCS capacity; rescue cannot restore propulsion.";
        double px = 0, py = 0;
        BodyOrbit approachBody = null;
        if (reason == null) {
            var body = system.GetNearestBO(home.objSS, StarSystem.fEpoch, bIncludePlaceholders:false);
            double dx = body == null ? 0 : home.objSS.vPosx-body.dXReal, dy = body == null ? 0 : home.objSS.vPosy-body.dYReal;
            double length = Math.Sqrt(dx*dx+dy*dy);
            if (body == null || string.IsNullOrEmpty(body.strName) || !Finite(length) || length <= 0 || !Finite(home.objSS.vVelX) || !Finite(home.objSS.vVelY)) reason = "A finite outward approach from the home station's physical body cannot be established.";
            else { approachBody = body; px = home.objSS.vPosx+dx/length*ApproachKm/KmPerAU; py = home.objSS.vPosy+dy/length*ApproachKm/KmPerAU; }
        }
        if (reason != null) return Reply(apply ? 409 : 200, "id", id, "eligible", false, "rescued", false, "reason", reason);
        var towIds = new List<string>(tows.Count); foreach (var tow in tows) towIds.Add(tow.strRegID);
        var crewIds = new List<string>(crew.Count); foreach (var co in crew) crewIds.Add(co.strID);
        var plan = SnapshotJson.Map("homeStation", home.strRegID, "crew", crewIds, "releaseWrecks", towIds, "pruneWrecks", false,
            "relocate", true, "approachDistanceKm", ApproachKm, "positionAU", SnapshotJson.Map("x", px, "y", py),
            "matchHomeVelocity", true, "clearSpinAndControls", true, "fuelBeforeKg", ship.GetRCSRemain(), "fuelAfterKg", fuel,
            "returnBehavior", "Native FlyTo/Dock via existing StationReturn; eligible crew transfer at arrival.");
        if (!apply) return Reply(200, "id", id, "eligible", true, "rescued", false, "plan", plan);
        // Every hull/crew/home check precedes mutations. Native undocking owns
        // reciprocal links and DockGroup teardown; never edit those collections.
        NavigationRoute.Stop(ship);
        foreach (var tow in tows) { ship.ToggleTowBracesShallow(false, tow.strRegID); ship.Undock(tow); }
        if (ship.IsDocked() || ship.HasDockGroup) return Reply(409, "id", id, "rescued", false, "reason", "Native detachment was incomplete; no relocation or crew transfer performed.");
        var motion = ship.objSS;
        motion.ResetNavData(); motion.ResetAccelerations(); motion.bIgnoreGrav = false;
        motion.BOClosest = null; motion.BOGreatestGrav = null; motion.strBOPORShip = approachBody.strName;
        motion.vPosx = px; motion.vPosy = py; motion.vVelX = home.objSS.vVelX; motion.vVelY = home.objSS.vVelY;
        motion.fA = 0; motion.fW = 0; motion.fRot = Finite(motion.fRot) ? (float)(motion.fRot % (Math.PI*2)) : 0;
        ship.AIRefuel();
        StationReturn.Cancel(ai); StationReturn.Request(ai, home);
        return Reply(200, "id", id, "eligible", true, "rescued", true, "returnRequested", StationReturn.Pending(ai), "plan", plan);
    }
}
