using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Ostranauts.Core.Models;
using Ostranauts.Systems;
using SolarSystem;

namespace Ostrabus.BoneyardTurnover
{
    internal sealed class Candidate
    {
        internal string Id;
        internal Ship Hull;
        internal StellarDerelict Deferred;
        internal ShipSitu Situ => Hull?.objSS ?? Deferred?.objSS;
    }

    internal sealed class Eligibility
    {
        private readonly HashSet<string> protectedIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<ShipSitu> targetedSituations = new HashSet<ShipSitu>();
        private static readonly AccessTools.FieldRef<Ship, DictionaryArray<string, CondOwner>> Items =
            AccessTools.FieldRefAccess<Ship, DictionaryArray<string, CondOwner>>("mapICOs");

        internal Eligibility()
        {
            foreach (var ship in CrewSim.system.dictShips.Values)
            {
                Add(ship.shipScanTarget?.strRegID); Add(ship.shipCombatTarget?.strRegID);
                Add(ship.shipStationKeepingTarget?.strRegID); Add(ship.shipUndock?.strRegID);
                Add(ship.targetRegID); Add(ship.TargetData?.TargetRegId); Add(ship.TargetData?.Ship?.strRegID);
                if (ship.shipSituTarget != null) targetedSituations.Add(ship.shipSituTarget);
                if (ship.TargetData?.Situ != null) targetedSituations.Add(ship.TargetData.Situ);
                var json = ship.json;
                if (json == null) continue;
                Add(json.strScanTargetID); Add(json.strCombatTargetID); Add(json.strStationKeepingTargetID);
                Add(json.strUndockID); Add(json.strTargetRegID);
            }
            foreach (var id in PlotManager.GetShipsInPlots()) Add(id);
            // Native plot lookup only resolves live COs. Resolve saved tokens too.
            foreach (var plot in PlotManager.dictPlotsActive.Values)
                if (plot?.dictCOTokens != null)
                    foreach (var token in plot.dictCOTokens.Values)
                    {
                        Add(token);
                        if (DataHandler.dictCOSaves.TryGetValue(token, out var saved)) Add(saved.strRegIDLast);
                    }
            foreach (var bounty in BountyManager.Bounties)
            {
                Add(bounty.strTargetRegId); Add(bounty.strTargetShip);
                foreach (string id in CrewSim.system.GetShipsForOwner(bounty.strTargetID)) Add(id);
                if (!string.IsNullOrEmpty(bounty.strTargetID) && DataHandler.dictCOSaves.TryGetValue(bounty.strTargetID, out var saved))
                    Add(saved.strRegIDLast);
            }
            // Registered player claims and crew-owned ships are protected even
            // if no character currently occupies them.
            var company = CrewSim.coPlayer?.Company;
            if (company?.mapRoster != null)
                foreach (string member in company.mapRoster.Keys)
                    foreach (string id in CrewSim.system.GetShipsForOwner(member)) Add(id);
        }

        private void Add(string id) { if (!string.IsNullOrEmpty(id)) protectedIds.Add(id); }

        internal bool References(string id) => protectedIds.Contains(id);

        internal List<Candidate> List(Field field)
        {
            var candidates = new List<Candidate>();
            foreach (var item in CrewSim.system.dictStellarObjects)
                if (item is StellarDerelict wreck && wreck.MapID == field.Map)
                {
                    var candidate = new Candidate { Id = wreck.strID, Deferred = wreck };
                    if (Allows(field, candidate)) candidates.Add(candidate);
                }
            foreach (var hull in CrewSim.system.dictShips.Values)
            {
                var candidate = new Candidate { Id = hull.strRegID, Hull = hull };
                if (Allows(field, candidate)) candidates.Add(candidate);
            }
            return candidates.OrderBy(x => x.Id, StringComparer.Ordinal).ToList();
        }

        internal bool Allows(Field field, Candidate candidate)
        {
            if (protectedIds.Contains(candidate.Id) || targetedSituations.Contains(candidate.Situ) ||
                !field.Contains(candidate.Situ)) return false;
            string owner = CrewSim.system.GetShipOwner(candidate.Id);
            string deferredOwner = candidate.Deferred?.strOwner;
            string player = CrewSim.coPlayer?.strID;
            if (!string.IsNullOrEmpty(player) && (owner == player || deferredOwner == player)) return false;
            // A changed registry/owner is a claim or another actor's property.
            string expected = field.Ring.strOwner;
            if (candidate.Deferred != null)
                return deferredOwner == expected && (owner == "UNREGISTERED" || owner == expected) &&
                    !CrewSim.system.dictShips.ContainsKey(candidate.Id) &&
                    CrewSim.system.dictStellarObjects.TryGetValue(candidate.Id, out var current) && ReferenceEquals(current, candidate.Deferred);

            var hull = candidate.Hull;
            if (hull == null || hull.bDestroyed || hull.ShipCO == null || hull.json == null ||
                hull.Classification != Ship.TypeClassification.Default || hull.LoadState != Ship.Loaded.Shallow ||
                hull.IsAIShip || AIShipManager.GetAIShipByRegID(candidate.Id) != null ||
                hull.HasDockGroup || hull.IsDocked() || owner != expected ||
                hull.People.Count != 0 || hull.json.aCrew?.Length > 0 ||
                !OnlySpawnMarkers(hull) ||
                EnvironmentEntities.GetMapID(hull) != field.Map || !hull.ShipCO.HasCond("IsEnvironmentEntity") ||
                PlotManager.IsActivePlotCO(hull.ShipCO) ||
                !CrewSim.system.dictShips.TryGetValue(candidate.Id, out var currentHull) || !ReferenceEquals(hull, currentHull)) return false;
            return !CrewSim.system.dictStellarObjects.ContainsKey(candidate.Id);
        }

        private static bool OnlySpawnMarkers(Ship hull)
        {
            // Despite its name, SaveCOs fills aShallowPSpecs with NPC loot
            // spawners, not people. Shallow loading instantiates these markers.
            // Permit only native, root-owned markers; retain the orphan-item
            // guard for every other loaded object and every uncertain record.
            if (hull.json.aShallowPSpecs != null && hull.json.aShallowPSpecs.Any(x =>
                x == null || !IsSpawnDefinition(x.strName) || string.IsNullOrEmpty(x.strID))) return false;
            var items = Items(hull);
            if (items == null) return false;
            return items.Values.All(x => x != null && !x.bDestroyed && IsSpawnDefinition(x.strCODef) &&
                x.HasCond("IsLootSpawner") && x.Crew == null && x.pspec == null && !x.IsHumanOrRobot &&
                x.objCOParent == null && ReferenceEquals(x.ship, hull) &&
                DataHandler.mapCOs.TryGetValue(x.strID, out var current) && ReferenceEquals(current, x));
        }

        private static bool IsSpawnDefinition(string name)
        {
            if (string.IsNullOrEmpty(name) || DataHandler.dictCOs == null || DataHandler.dictCOOverlays == null ||
                !DataHandler.dictCOs.ContainsKey(name) && !DataHandler.dictCOOverlays.ContainsKey(name)) return false;
            // Native overlays retain their distinct saved names. Resolve the
            // authored base through the same mechanism as native CO creation.
            return DataHandler.GetCondOwnerDef(name)?.strName == "SysLootSpawner";
        }
    }
}
