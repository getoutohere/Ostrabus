using System;
using System.Collections.Generic;
using System.Linq;
using Ostranauts.Core;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using Ostranauts.Ships;
using UnityEngine.SceneManagement;
using Ostrabus.Inspection;

namespace Ostrabus.ShipboardAction;

/// <summary>Shallow simulation for hostile AI crews fighting after docking.</summary>
internal static class ShipboardActionSimulation
{
    private const string Prefix = "ShipboardAction.";
    private const string EncounterKey = Prefix + "EncounterShips";
    private const string EncounterStartEpochKey = Prefix + "EncounterStartEpoch";
    private const string EncounterStartCrewKey = Prefix + "EncounterStartCrew";
    private const string NextEpochKey = Prefix + "NextEpoch";
    private const string RandomStateKey = Prefix + "RandomState";
    private const string ReturnKey = Prefix + "ReturnHome";
    private const string ReturnRouteQueuedKey = Prefix + "ReturnRouteQueued";
    private const double ScanInterval = 10.0;
    private const double MinCasualtyInterval = 90.0;
    private const double MaxCasualtyInterval = 300.0;
    private const double ReturnScanInterval = 5.0;
    private static double _nextScanEpoch;
    private static double _nextReturnScanEpoch;
    private static bool _running;
    private static WeakReference<StarSystem> _boundSystem;
    private static readonly List<AIShip> _aiScratch = new();
    private static readonly List<Ship> _groupScratch = new();
    private static readonly List<Ship> _encounterShipsScratch = new();
    private static readonly List<string> _idScratch = new();
    private static readonly HashSet<string> _visitedShips = new(StringComparer.Ordinal);
    private static readonly List<string> _factionScratch = new(1);
    private static readonly Dictionary<string, string[]> _encounterIdsCache = new(StringComparer.Ordinal);
    private static object _lastResolution;
    private static readonly List<CombatTeam> _teamScratch = new(3);
    private static readonly List<CombatTeam> _activeTeamScratch = new(3);
    private static readonly List<CombatTeam> _survivingTeamScratch = new(3);

    private sealed class CombatTeam
    {
        internal string Faction;
        internal readonly List<Ship> Ships = new();
        internal readonly List<CondOwner> Crew = new();
        internal readonly List<CondOwner> ActiveCrew = new();
        internal double WeaponScore;
        internal double Weight;
    }
    private static readonly HashSet<string> HostileFactions = new(StringComparer.OrdinalIgnoreCase) { "CCRE", "BeltPirates", "GalileanConfederacy" };

    internal static void Tick()
    {
        if (_running) return;
        if (_boundSystem == null || !_boundSystem.TryGetTarget(out StarSystem bound) || !ReferenceEquals(bound, CrewSim.system))
        {
            ResetRuntimeState();
            if (CrewSim.system != null) _boundSystem = new WeakReference<StarSystem>(CrewSim.system);
        }
        if (CrewSim.system == null || AIShipManager.dictAIs == null) return;
        _running = true;
        try
        {
            TickReturns();
            if (StarSystem.fEpoch < _nextScanEpoch) return;
            _nextScanEpoch = StarSystem.fEpoch + ScanInterval;
            ScanDockedGroups();
        }
        catch (Exception ex) { Ostrabus.OstrabusPlugin.Logger?.LogError($"[ShipboardAction] Simulation error: {ex}"); }
        finally
        {
            ClearScratchReferences();
            _running = false;
        }
    }

    internal static void OnSceneUnloaded(Scene scene) => ResetRuntimeState();

    internal static void ResetRuntimeState()
    {
        _running = false;
        _boundSystem = null;
        _nextScanEpoch = 0;
        _nextReturnScanEpoch = 0;
        _encounterIdsCache.Clear();
        _lastResolution = null;
        ClearScratchReferences();
    }

    private static void ClearScratchReferences()
    {
        _aiScratch.Clear();
        _groupScratch.Clear();
        _encounterShipsScratch.Clear();
        _idScratch.Clear();
        _visitedShips.Clear();
        _factionScratch.Clear();
        _activeTeamScratch.Clear();
        _survivingTeamScratch.Clear();
        for (int i = 0; i < _teamScratch.Count; i++)
        {
            CombatTeam team = _teamScratch[i];
            team.Ships.Clear();
            team.Crew.Clear();
            team.ActiveCrew.Clear();
            team.Faction = null;
        }
    }

    private static void ScanDockedGroups()
    {
        _visitedShips.Clear();
        SnapshotAIShips();
        for (int i = 0; i < _aiScratch.Count; i++)
        {
            AIShip ai = _aiScratch[i];
            Ship ship = ai?.Ship;
            if (ship == null || ship.bDestroyed || _visitedShips.Contains(ship.strRegID)) continue;
            _groupScratch.Clear();
            _groupScratch.Add(ship);
            IReadOnlyList<Ship> dockedShips = ship.GetAllDockedShips();
            for (int j = 0; j < dockedShips.Count; j++)
            {
                Ship docked = dockedShips[j];
                if (docked != null && !docked.bDestroyed) _groupScratch.Add(docked);
            }
            for (int j = 0; j < _groupScratch.Count; j++) _visitedShips.Add(_groupScratch[j].strRegID);
            AIShip coordinator = null;
            AIShip stateOwner = null;
            for (int j = 0; j < _groupScratch.Count; j++)
            {
                AIShip memberAI = AIShipManager.GetAIShipByRegID(_groupScratch[j].strRegID);
                if (memberAI?.Blackboard == null) continue;
                if (coordinator == null || string.CompareOrdinal(memberAI.Ship.strRegID, coordinator.Ship.strRegID) < 0) coordinator = memberAI;
                string activeEncounter = memberAI.Blackboard.Get<string>(EncounterKey);
                if (!string.IsNullOrEmpty(activeEncounter)) stateOwner = memberAI;
            }
            if (stateOwner != null) coordinator = stateOwner;
            if (coordinator == null) continue;
            if (coordinator.Blackboard.Get<string>(EncounterKey) is string active && active.Length > 0)
            {
                TickEncounter(coordinator, active);
                continue;
            }
            bool canFight = true;
            for (int j = 0; j < _groupScratch.Count; j++) if (_groupScratch[j].IsStation() || !_groupScratch[j].IsDocked()) { canFight = false; break; }
            if (!canFight || CountHostileFactions(_groupScratch) < 2) continue;
            StartEncounter(coordinator, _groupScratch);
        }
    }

    private static void SnapshotAIShips()
    {
        _aiScratch.Clear();
        foreach (List<AIShip> ships in AIShipManager.dictAIs.Values)
        {
            for (int i = 0; i < ships.Count; i++) _aiScratch.Add(ships[i]);
        }
    }

    private static int CountHostileFactions(IReadOnlyList<Ship> ships)
    {
        bool ccre = false, pirates = false, galcon = false;
        for (int i = 0; i < ships.Count; i++)
        {
            string faction = GetFaction(ships[i]);
            if (faction == "CCRE") ccre = true;
            else if (faction == "BeltPirates") pirates = true;
            else if (faction == "GalileanConfederacy") galcon = true;
        }
        return (ccre ? 1 : 0) + (pirates ? 1 : 0) + (galcon ? 1 : 0);
    }

    private static void StartEncounter(AIShip coordinator, List<Ship> group)
    {
        if (IsPlayerMaterialized(group)) return;
        _idScratch.Clear();
        for (int i = 0; i < group.Count; i++) _idScratch.Add(group[i].strRegID);
        _idScratch.Sort(StringComparer.Ordinal);
        coordinator.Blackboard.Set(EncounterKey, string.Join(";", _idScratch));
        coordinator.Blackboard.Set(EncounterStartEpochKey, StarSystem.fEpoch);
        string startCrew = BuildStartCrewSnapshot(group);
        coordinator.Blackboard.Set(EncounterStartCrewKey, startCrew);
        coordinator.Blackboard.Set(RandomStateKey, SeedFor(group));
        coordinator.Blackboard.Set(NextEpochKey, StarSystem.fEpoch + RandomRange(coordinator.Blackboard, MinCasualtyInterval, MaxCasualtyInterval));
        MirrorEncounterState(coordinator, group);
        Ostrabus.OstrabusPlugin.Logger?.LogInfo($"[ShipboardAction] Hostile docking encounter began at epoch {StarSystem.fEpoch}; initial crew readiness: {startCrew}");
    }

    private static void MirrorEncounterState(AIShip coordinator, List<Ship> ships)
    {
        if (coordinator?.Blackboard == null) return;
        string encoded = coordinator.Blackboard.Get<string>(EncounterKey);
        double startEpoch = coordinator.Blackboard.Get<double>(EncounterStartEpochKey);
        string startCrew = coordinator.Blackboard.Get<string>(EncounterStartCrewKey);
        double nextEpoch = coordinator.Blackboard.Get<double>(NextEpochKey);
        int randomState = coordinator.Blackboard.Get<int>(RandomStateKey);
        for (int i = 0; i < ships.Count; i++)
        {
            AIShip ai = AIShipManager.GetAIShipByRegID(ships[i].strRegID);
            if (ai?.Blackboard == null) continue;
            ai.Blackboard.Set(EncounterKey, encoded);
            ai.Blackboard.Set(EncounterStartEpochKey, startEpoch);
            ai.Blackboard.Set(EncounterStartCrewKey, startCrew);
            ai.Blackboard.Set(NextEpochKey, nextEpoch);
            ai.Blackboard.Set(RandomStateKey, randomState);
        }
    }

    private static string BuildStartCrewSnapshot(List<Ship> ships)
    {
        var entries = new List<string>(ships.Count);
        for (int i = 0; i < ships.Count; i++)
        {
            Ship ship = ships[i];
            int alive = 0, active = 0, ranged = 0, otherWeapon = 0, unarmed = 0;
            List<PersonSpec> people = ship.People;
            for (int j = 0; j < people.Count; j++)
            {
                CondOwner person = people[j]?.GetCO();
                if (person == null || !person.bAlive) continue;
                alive++;
                if (!IsCriticallyInjured(person)) active++;
                GetWeaponLoadout(person, out bool hasRanged, out bool hasWeapon, out _);
                if (hasRanged) ranged++;
                else if (hasWeapon) otherWeapon++;
                else unarmed++;
            }
            entries.Add($"{ship.strRegID}={alive}/{active}/r{ranged}/w{otherWeapon}/u{unarmed}");
        }
        return string.Join(", ", entries);
    }

    private static void TickEncounter(AIShip coordinator, string encodedShips)
    {
        if (!_encounterIdsCache.TryGetValue(encodedShips, out string[] ids)) _encounterIdsCache[encodedShips] = ids = encodedShips.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        _encounterShipsScratch.Clear();
        for (int i = 0; i < ids.Length; i++)
        {
            Ship ship = CrewSim.system.GetShipByRegID(ids[i]);
            if (ship != null && !ship.bDestroyed) _encounterShipsScratch.Add(ship);
        }
        List<Ship> group = _encounterShipsScratch;
        if (group.Count == 0) { ClearEncounter(coordinator); return; }
        if (IsPlayerMaterialized(group)) return;
        for (int i = 0; i < group.Count; i++) if (group[i].LoadState > Ship.Loaded.Shallow) return;
        BuildTeams(group);
        if (_activeTeamScratch.Count <= 1)
        {
            string winner = _activeTeamScratch.Count == 1 ? _activeTeamScratch[0].Faction : null;
            if (winner == null && _survivingTeamScratch.Count > 0)
            {
                int maxCrew = 0;
                for (int i = 0; i < _survivingTeamScratch.Count; i++) maxCrew = Math.Max(maxCrew, _survivingTeamScratch[i].Crew.Count);
                int tieCount = 0;
                for (int i = 0; i < _survivingTeamScratch.Count; i++) if (_survivingTeamScratch[i].Crew.Count == maxCrew) tieCount++;
                int choice = (int)(NextRandom(coordinator.Blackboard) * tieCount) % tieCount;
                for (int i = 0; i < _survivingTeamScratch.Count; i++)
                {
                    if (_survivingTeamScratch[i].Crew.Count != maxCrew) continue;
                    if (choice-- == 0) { winner = _survivingTeamScratch[i].Faction; break; }
                }
            }
            ResolveEncounter(coordinator, group, winner);
            return;
        }
        if (StarSystem.fEpoch < coordinator.Blackboard.Get<double>(NextEpochKey)) return;

        // Resolve one casualty per interval; crew count and weapon quality change which side is most exposed.
        double total = 0.0;
        for (int i = 0; i < _activeTeamScratch.Count; i++)
        {
            CombatTeam team = _activeTeamScratch[i];
            team.WeaponScore = AverageWeaponScore(team.ActiveCrew);
            team.Weight = 1.0 / (team.ActiveCrew.Count * (1.0 + team.WeaponScore));
            total += team.Weight;
        }
        double draw = NextRandom(coordinator.Blackboard) * total;
        CombatTeam victimTeam = _activeTeamScratch[0];
        for (int i = 0; i < _activeTeamScratch.Count; i++) { draw -= _activeTeamScratch[i].Weight; if (draw <= 0) { victimTeam = _activeTeamScratch[i]; break; } }
        CondOwner victim = victimTeam.ActiveCrew[(int)(NextRandom(coordinator.Blackboard) * victimTeam.ActiveCrew.Count) % victimTeam.ActiveCrew.Count];
        string victimName = victim.FriendlyName;
        string victimShip = victim.ship?.strRegID ?? "unknown ship";
        bool injured = NextRandom(coordinator.Blackboard) < 0.2;
        if (injured)
        {
            victim.AddCondAmount("Unconscious", 1.0);
            victim.AddCondAmount("CoughingBlood", 1.0);
        }
        else KillAndPrune(victim);
        coordinator.Blackboard.Set(NextEpochKey, StarSystem.fEpoch + RandomRange(coordinator.Blackboard, MinCasualtyInterval, MaxCasualtyInterval));
        MirrorEncounterState(coordinator, group);
        Ostrabus.OstrabusPlugin.Logger?.LogInfo($"[ShipboardAction] {victimName} {(injured ? "was critically injured" : "was killed")} aboard {victimShip}.");
    }

    private static double AverageWeaponScore(List<CondOwner> crew)
    {
        if (crew.Count == 0) return 0;
        double score = 0.0;
        for (int i = 0; i < crew.Count; i++)
        {
            CondOwner person = crew[i];
            GetWeaponLoadout(person, out bool ranged, out bool armed, out bool wieldingRanged);
            score += wieldingRanged ? 1.0 : ranged ? 0.6 : armed ? 0.25 : 0.0;
        }
        return score / crew.Count;
    }

    private static void GetWeaponLoadout(CondOwner person, out bool ranged, out bool armed, out bool wieldingRanged)
    {
        wieldingRanged = person.HasCond("IsWieldingRanged");
        ranged = wieldingRanged;
        armed = ranged;
        if (ranged) return;
        List<CondOwner> items = person.GetCOs(bAllowLocked: true);
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].HasCond("IsWeaponRanged")) { ranged = true; armed = true; break; }
            if (items[i].HasCond("IsWeapon")) armed = true;
        }
    }

    private static void BuildTeams(List<Ship> ships)
    {
        for (int i = 0; i < _teamScratch.Count; i++)
        {
            _teamScratch[i].Ships.Clear();
            _teamScratch[i].Crew.Clear();
            _teamScratch[i].ActiveCrew.Clear();
        }
        _activeTeamScratch.Clear();
        _survivingTeamScratch.Clear();
        for (int i = 0; i < ships.Count; i++)
        {
            Ship ship = ships[i];
            string faction = GetFaction(ship);
            if (faction == null) continue;
            CombatTeam team = null;
            for (int j = 0; j < _teamScratch.Count; j++) if (string.Equals(_teamScratch[j].Faction, faction, StringComparison.OrdinalIgnoreCase)) { team = _teamScratch[j]; break; }
            if (team == null)
            {
                for (int j = 0; j < _teamScratch.Count; j++) if (_teamScratch[j].Ships.Count == 0) { team = _teamScratch[j]; break; }
                if (team == null) { team = new CombatTeam(); _teamScratch.Add(team); }
                team.Faction = faction;
            }
            team.Ships.Add(ship);
            List<PersonSpec> people = ship.People;
            for (int j = 0; j < people.Count; j++)
            {
                CondOwner person = people[j]?.GetCO();
                if (person == null || !person.bAlive) continue;
                team.Crew.Add(person);
                if (!IsCriticallyInjured(person)) team.ActiveCrew.Add(person);
            }
        }
        for (int i = 0; i < _teamScratch.Count; i++)
        {
            CombatTeam team = _teamScratch[i];
            if (team.Crew.Count > 0) _survivingTeamScratch.Add(team);
            if (team.ActiveCrew.Count > 0) _activeTeamScratch.Add(team);
        }
    }

    private static void ResolveEncounter(AIShip coordinator, List<Ship> group, string winnerFaction)
    {
        var losingShips = winnerFaction == null ? new List<Ship>() : group.Where(x => GetFaction(x) != winnerFaction && !x.IsStation()).ToList();
        var winnerCrew = winnerFaction == null ? new List<CondOwner>() : group.Where(x => GetFaction(x) == winnerFaction).SelectMany(x => x.GetPeople(false)).Where(p => p != null && p.bAlive).ToList();
        var winnerCaptainIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (Ship ship in group.Where(x => GetFaction(x) == winnerFaction))
        {
            CondOwner captain = AIShipManager.GetAIShipByRegID(ship.strRegID)?.AICharacter?.CaptainUs;
            if (captain != null) winnerCaptainIds.Add(captain.strID);
        }
        var viablePilots = winnerCrew.Where(p => IsHealthyPilot(p) && !winnerCaptainIds.Contains(p.strID)).ToList();
        var prizeChecks = new List<string>();
        var prizeTaken = false;
        string prizeShipId = null, prizePilotId = null;
        Ship pendingPrize = null, pendingHome = null, pilotFormerShip = null;
        CondOwner pendingPilot = null;
        string formerOwner = null, formerFaction = null;
        AIType formerAIType = AIType.NA;
        if (winnerFaction == null) prizeChecks.Add("No surviving combat team.");
        else if (winnerCrew.Count <= 1) prizeChecks.Add("No prize: the winning side had one or fewer survivors.");
        else if (viablePilots.Count == 0) prizeChecks.Add("No prize: no healthy non-captain winner crew member was available to pilot it.");
        else
        {
            foreach (Ship prize in losingShips)
            {
                if (prizeTaken) break;
                if (prize.DMGStatus == Ship.Damage.Derelict) { prizeChecks.Add(prize.strRegID + ": hull is derelict."); continue; }
                Ship home = FindHomeForFaction(prize, winnerFaction);
                if (home == null) { prizeChecks.Add(prize.strRegID + ": no station found for the winning faction."); continue; }
                if (AIShipManager.BingoFuelCheck(prize, home, AIShip.CalculateMaxSpeed(prize, null))) { prizeChecks.Add(prize.strRegID + ": insufficient RCS delta-v to reach " + home.strRegID + "."); continue; }
                CondOwner pilot = viablePilots[(int)(NextRandom(coordinator.Blackboard) * viablePilots.Count) % viablePilots.Count];
                pendingPrize = prize;
                pendingHome = home;
                pendingPilot = pilot;
                pilotFormerShip = pilot.ship;
                formerOwner = CrewSim.system.GetShipOwner(prize.strRegID);
                formerFaction = GetFaction(prize);
                AIShip oldAI = AIShipManager.GetAIShipByRegID(prize.strRegID);
                formerAIType = oldAI?.AIType ?? (formerFaction == "BeltPirates" ? AIType.Pirate : AIType.Navy);
                prizeChecks.Add(prize.strRegID + ": hull passed nav and fuel checks; selected " + pilot.strID + ".");
                break;
            }
        }
        if (!prizeTaken && winnerFaction != null && losingShips.Count == 0) prizeChecks.Add("No eligible losing ship was available as a prize.");

        // Preserve the losing ship's NavAIManned result until after prize eligibility is checked.
        if (winnerFaction != null)
        {
            foreach (CondOwner loser in group.Where(x => GetFaction(x) != winnerFaction).SelectMany(x => x.GetPeople(false)).Where(p => p != null && p.bAlive).ToArray()) KillAndPrune(loser);
        }
        if (pendingPrize != null && pendingPilot != null)
        {
            CrewSim.MoveCO(pendingPilot, pendingPrize, bPlayerShip: false);
            pendingPilot.ClaimShip(pendingPrize.strRegID);
            CrewSim.system.RegisterShipOwner(pendingPrize.strRegID, winnerFaction);
            JsonFaction faction = CrewSim.system.GetFaction(winnerFaction);
            if (faction != null) pendingPrize.SetFactions(new List<JsonFaction> { faction }, bRemoveOld: true);
            AIShipManager.UnregisterShip(pendingPrize);
            AIShip prizeAI = AIShipManager.AddAIToShip(pendingPrize, AIType.Navy, pendingHome.strRegID);
            if (prizeAI != null)
            {
                ClearAlliedCombatReferences(pendingPrize, winnerFaction);
                QueueReturnHome(pendingPrize);
                prizeShipId = pendingPrize.strRegID;
                prizePilotId = pendingPilot.strID;
                prizeTaken = true;
                prizeChecks.Add(pendingPrize.strRegID + ": captured by " + pendingPilot.strID + "; returning to " + pendingHome.strRegID + ".");
            }
            else
            {
                prizeChecks.Add(pendingPrize.strRegID + ": pilot transfer occurred but the game's AI controller could not be created.");
                CrewSim.MoveCO(pendingPilot, pilotFormerShip, bPlayerShip: false);
                pendingPilot.ClaimShip(pilotFormerShip.strRegID);
                CrewSim.system.RegisterShipOwner(pendingPrize.strRegID, formerOwner);
                JsonFaction oldFaction = CrewSim.system.GetFaction(formerFaction);
                if (oldFaction != null) pendingPrize.SetFactions(new List<JsonFaction> { oldFaction }, bRemoveOld: true);
                AIShipManager.AddAIToShip(pendingPrize, formerAIType, pendingHome.strRegID);
            }
        }
        foreach (Ship ship in group)
        {
            if (!ship.IsDocked()) continue;
            foreach (Ship docked in ship.GetAllDockedShips().Where(x => x != null && group.Any(g => g.strRegID == x.strRegID)).ToArray()) ship.Undock(docked);
        }
        if (winnerFaction != null)
        {
            foreach (Ship ship in group.Where(x => GetFaction(x) == winnerFaction))
            {
                List<CondOwner> crew = ship.GetPeople(false).Where(p => p != null && p.bAlive).ToList();
                bool injured = crew.Any(IsCriticallyInjured);
                if (crew.Count == 0) continue;
                Ship home = FindHome(ship);
                double maxSpeed = AIShip.CalculateMaxSpeed(ship, null);
                bool lowFuel = AIShipManager.LowFuel(ship, maxSpeed) || (home != null && AIShipManager.BingoFuelCheck(ship, home, maxSpeed));
                if (injured || crew.Count < 2 || lowFuel) QueueReturnHome(ship);
            }
        }
        var participantIds = new List<string>(group.Count);
        for (int i = 0; i < group.Count; i++) participantIds.Add(group[i].strRegID);
        _lastResolution = SnapshotJson.Map("epoch", StarSystem.fEpoch, "winnerFaction", winnerFaction, "participantShips", participantIds,
            "winnerSurvivors", winnerCrew.Count, "healthyPilotCandidates", viablePilots.Count, "captainsKeptAboard", winnerCaptainIds.Count, "prizeCaptured", prizeTaken,
            "prizeShip", prizeShipId, "prizePilot", prizePilotId, "prizeChecks", prizeChecks);
        Ostrabus.OstrabusPlugin.Logger?.LogInfo($"[ShipboardAction] Prize result: {(prizeTaken ? prizeShipId + " captured by " + prizePilotId : string.Join("; ", prizeChecks))}");
        Ostrabus.OstrabusPlugin.Logger?.LogInfo($"[ShipboardAction] Encounter ended; winner: {winnerFaction ?? "none"}. Ships undocked.");
        ClearEncounter(coordinator);
    }

    internal static bool IsCriticallyInjured(CondOwner person)
    {
        if (person == null) return true;
        if (person.HasCond("Unconscious") || person.HasCond("Stunned") || person.HasCond("Recovering") || person.HasCond("CoughingBlood") || person.HasCond("BleedingInternal") || person.HasCond("PulmonaryHemorrhage") || person.HasCond("DcBlood03") || person.HasCond("DcBlood04") || person.HasCond("DcWoundBlood04")) return true;
        List<CondOwner> items = person.GetCOs(bAllowLocked: true);
        if (items == null) return false;
        for (int i = 0; i < items.Count; i++)
        {
            CondOwner item = items[i];
            if (item.HasCond("DcBlood03") || item.HasCond("DcBlood04") || item.HasCond("DcWoundBlood04") || item.HasCond("BleedingInternal") || item.HasCond("PulmonaryHemorrhage")) return true;
            // IsWoundVital marks anatomy, not an injury. Count it only when the wound itself has reached the game's stun threshold.
            if (item.HasCond("IsWoundVital") && item.GetCondAmount("StatWoundBlunt", isThreshold: false) + item.GetCondAmount("StatWoundCut", isThreshold: false) >= 0.5) return true;
        }
        return false;
    }
    private static bool IsHealthyPilot(CondOwner person) => person != null && person.bAlive && !IsCriticallyInjured(person);

    private static void KillAndPrune(CondOwner person)
    {
        person.Kill = true;
        if (!DataHandler.GetCondTrigger("TIsNPCPrunable").Triggered(person, logOutcome: false)) return;
        person.RemoveFromCurrentHome(bForce: true);
        person.Destroy();
    }

    private static void QueueReturnHome(Ship ship)
    {
        AIShip ai = AIShipManager.GetAIShipByRegID(ship.strRegID);
        Ship home = FindHome(ship);
        if (ai == null || home == null) return;
        ai.Blackboard.Set("NeedsRepairs", true);
        ai.Blackboard.Set(ReturnKey, true);
        global::Ostrabus.ShipNavigation.StationReturn.Request(ai, home);
        ai.Blackboard.Remove(ReturnRouteQueuedKey);
    }

    private static void ClearAlliedCombatReferences(Ship prize, string winnerFaction)
    {
        if (prize == null || AIShipManager.dictAIs == null) return;
        string prizeId = prize.strRegID;
        int clearedShips = 0;
        foreach (List<AIShip> region in AIShipManager.dictAIs.Values)
        {
            for (int i = 0; i < region.Count; i++)
            {
                AIShip ai = region[i];
                Ship ship = ai?.Ship;
                if (ship == null || ai.Blackboard == null || CrewSim.system.GetShipOwner(ship.strRegID) != winnerFaction) continue;
                bool cleared = false;
                List<Ship> enemies = ai.Blackboard.Get<List<Ship>>("Enemies");
                if (RemoveShipReference(enemies, prizeId)) cleared = true;
                List<Ship> combatants = ai.Blackboard.Get<List<Ship>>("Combatants");
                if (RemoveShipReference(combatants, prizeId)) cleared = true;
                if (ai.Blackboard.Get<Ship>("ClosestEnemy")?.strRegID == prizeId)
                {
                    ai.Blackboard.Remove("ClosestEnemy");
                    cleared = true;
                }
                if (ship.shipCombatTarget?.strRegID == prizeId)
                {
                    ship.shipCombatTarget = null;
                    cleared = true;
                }
                if (cleared)
                {
                    bool hasEnemies = enemies != null && enemies.Count > 0;
                    bool hasCombatants = combatants != null && combatants.Count > 0;
                    if (!hasEnemies && !hasCombatants) ai.Blackboard.Set("InCombat", false);
                    clearedShips++;
                }
            }
        }
        Ostrabus.OstrabusPlugin.Logger?.LogInfo($"[ShipboardAction] Cleared {prizeId} from combat state on {clearedShips} {winnerFaction} AI ships.");
    }

    private static bool RemoveShipReference(List<Ship> ships, string shipId)
    {
        if (ships == null) return false;
        bool removed = false;
        for (int i = ships.Count - 1; i >= 0; i--)
        {
            if (ships[i]?.strRegID != shipId) continue;
            ships.RemoveAt(i);
            removed = true;
        }
        return removed;
    }

    private static void TickReturns()
    {
        if (StarSystem.fEpoch < _nextReturnScanEpoch) return;
        _nextReturnScanEpoch = StarSystem.fEpoch + ReturnScanInterval;
        SnapshotAIShips();
        for (int i = 0; i < _aiScratch.Count; i++)
        {
            AIShip ai = _aiScratch[i];
            if (ai?.Blackboard?.Get<bool>(ReturnKey) != true || ai.Ship == null) continue;
            Ship ship = ai.Ship;
            Ship home = FindHome(ship);
            if (home == null) continue;
            if (!global::Ostrabus.ShipNavigation.StationReturn.Pending(ai)) global::Ostrabus.ShipNavigation.StationReturn.Request(ai, home);
            ai.Blackboard.Remove(ReturnRouteQueuedKey);
            bool dockedAtHome = false;
            if (ship.IsDocked())
            {
                IReadOnlyList<Ship> dockedShips = ship.GetAllDockedShips();
                for (int j = 0; j < dockedShips.Count; j++) if (dockedShips[j].strRegID == home.strRegID) { dockedAtHome = true; break; }
            }
            if (dockedAtHome)
            {
                CrewSim.DockAndDespawn(ship, home);
                ai.Blackboard.Remove(ReturnKey);
                ai.Blackboard.Remove(ReturnRouteQueuedKey);
                continue;
            }
            // Pending intent is enough. The shared ordinary-mission branch
            // binds home only after native urgent/combat decisions have run.
        }
    }

    private static Ship FindHome(Ship ship)
    {
        string owner = CrewSim.system.GetShipOwner(ship.strRegID);
        _factionScratch.Clear();
        _factionScratch.Add(owner);
        return CrewSim.system.GetNearestStation(ship.objSS.vPosx, ship.objSS.vPosy, excludeOutposts: false, _factionScratch);
    }

    private static Ship FindHomeForFaction(Ship ship, string faction)
    {
        _factionScratch.Clear();
        _factionScratch.Add(faction);
        return CrewSim.system.GetNearestStation(ship.objSS.vPosx, ship.objSS.vPosy, excludeOutposts: false, _factionScratch);
    }

    internal static string GetFaction(Ship ship)
    {
        List<JsonFaction> factions = ship.GetShipFactions();
        for (int i = 0; i < factions.Count; i++) if (factions[i] != null && HostileFactions.Contains(factions[i].strName)) return factions[i].strName;
        return null;
    }

    private static bool IsPlayerMaterialized(List<Ship> group)
    {
        Ship playerShip = CrewSim.coPlayer?.ship;
        if (playerShip == null) return false;
        for (int i = 0; i < group.Count; i++)
        {
            Ship ship = group[i];
            if (ship.strRegID == playerShip.strRegID) return true;
            IReadOnlyList<Ship> dockedShips = ship.GetAllDockedShips();
            for (int j = 0; j < dockedShips.Count; j++) if (dockedShips[j].strRegID == playerShip.strRegID) return true;
        }
        return false;
    }

    private static int SeedFor(IEnumerable<Ship> ships) => Math.Abs(string.Join("", ships.Select(x => x.strRegID).OrderBy(x => x, StringComparer.Ordinal)).GetHashCode()) + 1;
    private static double NextRandom(Blackboard board)
    {
        uint state = unchecked((uint)board.Get<int>(RandomStateKey));
        if (state == 0) state = 2463534242;
        state ^= state << 13; state ^= state >> 17; state ^= state << 5;
        board.Set(RandomStateKey, unchecked((int)state));
        return state / (double)uint.MaxValue;
    }
    private static double RandomRange(Blackboard board, double min, double max) => min + NextRandom(board) * (max - min);
    private static void ClearEncounter(AIShip coordinator)
    {
        string encoded = coordinator.Blackboard.Get<string>(EncounterKey);
        if (string.IsNullOrEmpty(encoded)) return;
        _encounterIdsCache.Remove(encoded);
        string[] ids = encoded.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < ids.Length; i++)
        {
            AIShip ai = AIShipManager.GetAIShipByRegID(ids[i]);
            if (ai?.Blackboard == null) continue;
            ai.Blackboard.Remove(EncounterKey);
            ai.Blackboard.Remove(EncounterStartEpochKey);
            ai.Blackboard.Remove(EncounterStartCrewKey);
            ai.Blackboard.Remove(NextEpochKey);
            ai.Blackboard.Remove(RandomStateKey);
        }
    }

    internal static object InspectionSnapshot()
    {
        var encounters = new List<object>();
        var returning = new List<object>();
        var reportedEncounters = new HashSet<string>(StringComparer.Ordinal);
        if (AIShipManager.dictAIs != null)
        {
            foreach (List<AIShip> region in AIShipManager.dictAIs.Values)
            {
                for (int i = 0; i < region.Count; i++)
                {
                    AIShip ai = region[i];
                    Ship ship = ai?.Ship;
                    if (ship == null || ai.Blackboard == null) continue;
                    string encoded = ai.Blackboard.Get<string>(EncounterKey);
                    if (!string.IsNullOrEmpty(encoded) && reportedEncounters.Add(encoded))
                    {
                        var participants = new List<object>();
                        string[] ids = encoded.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                        string startCrew = ai.Blackboard.Get<string>(EncounterStartCrewKey);
                        double startEpoch = ai.Blackboard.Get<double>(EncounterStartEpochKey);
                        bool playerMaterialized = false, loaded = false;
                        for (int j = 0; j < ids.Length; j++)
                        {
                            Ship participant = CrewSim.system.GetShipByRegID(ids[j]);
                            if (participant == null) { participants.Add(SnapshotJson.Map("id", ids[j], "resolved", false)); continue; }
                            int alive = 0, injured = 0;
                            List<PersonSpec> people = participant.People;
                            for (int k = 0; k < people.Count; k++)
                            {
                                CondOwner person = people[k]?.GetCO();
                                if (person == null || !person.bAlive) continue;
                                alive++;
                                if (IsCriticallyInjured(person)) injured++;
                            }
                            participants.Add(SnapshotJson.Map("id", participant.strRegID, "faction", GetFaction(participant),
                                "loadState", participant.LoadState.ToString(), "aliveCrew", alive, "criticallyInjuredCrew", injured,
                                "startCrewReadiness", FindStartCrewEntry(startCrew, participant.strRegID)));
                            if (participant.LoadState > Ship.Loaded.Shallow) loaded = true;
                            Ship playerShip = CrewSim.coPlayer?.ship;
                            if (playerShip != null)
                            {
                                if (playerShip.strRegID == participant.strRegID) playerMaterialized = true;
                                else
                                {
                                    IReadOnlyList<Ship> docked = participant.GetAllDockedShips();
                                    for (int k = 0; k < docked.Count; k++) if (docked[k].strRegID == playerShip.strRegID) { playerMaterialized = true; break; }
                                }
                            }
                        }
                        double nextEpoch = ai.Blackboard.Get<double>(NextEpochKey);
                        encounters.Add(SnapshotJson.Map("ships", participants, "state", playerMaterialized ? "paused-player" : loaded ? "paused-materialized" : "simulating",
                            "startEpoch", startEpoch, "startCrewReadiness", startCrew,
                            "nextCasualtyEpoch", nextEpoch, "secondsUntilNextCasualty", Math.Max(0, nextEpoch - StarSystem.fEpoch)));
                    }
                    if (ai.Blackboard.Get<bool>(ReturnKey))
                    {
                        returning.Add(SnapshotJson.Map("ship", ship.strRegID, "owner", CrewSim.system.GetShipOwner(ship.strRegID),
                            "target", ship.shipScanTarget?.strRegID, "activeCommand", ai.ActiveCommandName,
                            "routeQueued", global::Ostrabus.ShipNavigation.StationReturn.Pending(ai)));
                    }
                }
            }
        }
        return SnapshotJson.Map("epoch", StarSystem.fEpoch, "activeEncounters", encounters, "returningShips", returning, "lastResolution", _lastResolution);
    }

    private static string FindStartCrewEntry(string snapshot, string shipId)
    {
        if (string.IsNullOrEmpty(snapshot)) return null;
        string prefix = shipId + "=";
        string[] entries = snapshot.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < entries.Length; i++) if (entries[i].StartsWith(prefix, StringComparison.Ordinal)) return entries[i].Substring(prefix.Length);
        return null;
    }
}

