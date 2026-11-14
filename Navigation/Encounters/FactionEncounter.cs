using System;
using System.Collections.Generic;
using HarmonyLib;
using Ostrabus.Orbital;
using Ostranauts.Core;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;

namespace Ostrabus.ShipNavigation.Encounters;

// Intentional staging policy, not a controller or navigation governor. Nothing
// retains ships between calls, changes native fleet caps or repairs old actors.
internal static class FactionEncounter {
    internal const double MaxRelativeKmS = 1, SpawnMarginKm = 550, NativeDepotSpawnKm = 70;
    const double KmPerAU = 149597872;
    static readonly AccessTools.FieldRef<AIShip, ICommand> Active = AccessTools.FieldRefAccess<AIShip, ICommand>("_activeCommand");
    static readonly AccessTools.FieldRef<AIShip, ICommand> Reaction = AccessTools.FieldRefAccess<AIShip, ICommand>("_activeReaction");
    static readonly AccessTools.FieldRef<QueuedCommandsNode, Queue<ICommand>> Waiting = AccessTools.FieldRefAccess<QueuedCommandsNode, Queue<ICommand>>("commandQueue");
    internal static Ship Player => CrewSim.GetSelectedCrew()?.ship;
    static bool Finite(double number) => !double.IsNaN(number) && !double.IsInfinity(number);
    internal static bool State(Ship station, out MotionState result) {
        result = default;
        var situ = station?.objSS;
        var world = CrewSim.system;
        if (station == null || station.bDestroyed || station.bRemove || situ == null || !situ.bIsBO || world?.aBOs == null ||
            string.IsNullOrEmpty(situ.strBOPORShip) || !world.aBOs.TryGetValue(situ.strBOPORShip, out var body) ||
            !NativeEphemeris.State(body, StarSystem.fEpoch, out var position, out var velocity)) return false;
        result = new MotionState(position * KmPerAU, velocity * KmPerAU);
        return result.Finite;
    }
    static MotionState Motion(Ship ship) => new MotionState(
        new DVector(ship.objSS.vPosx * KmPerAU, ship.objSS.vPosy * KmPerAU),
        new DVector(ship.objSS.vVelX * KmPerAU, ship.objSS.vVelY * KmPerAU));
    internal static bool SlowEnough(Ship player, Ship home) => player?.objSS != null && State(home, out var state) &&
        (Motion(player).Velocity - state.Velocity).Length <= MaxRelativeKmS + .000001;
    static bool Torching(Ship player) {
        if (player.IsUsingTorchDrive) return true;
        var group = player.DockGroup;
        if (group != null) {
            foreach (var member in group.DockedShips) if (member?.Ship != null && member.Ship.IsUsingTorchDrive) return true;
        } else if (player.IsDocked()) {
            foreach (var peer in player.GetAllDockedShips()) if (peer != null && peer.IsUsingTorchDrive) return true;
        }
        return false;
    }
    internal static bool Area(string atc, out MotionState center, out double radiusKm) {
        center = default; radiusKm = 0;
        var world = CrewSim.system;
        if (world?.dictShips == null || string.IsNullOrEmpty(atc)) return false;
        var primary = world?.GetShipByRegID(atc);
        if (!State(primary, out center)) return false;
        // Enclose authored station patrol disks and anchored local POIs in a
        // regional disk. This includes the space between opposing depots rather
        // than leaving an artificial gap between their patrol-radius circles.
        foreach (string id in AIShipManager.GetAllStationsInATCRegion(atc)) {
            var station = world.GetShipByRegID(id);
            if (!State(station, out var position)) continue;
            double influence = station.ShipCO?.GetCondAmount("StationInfluenceRange") ?? 0;
            if (!Finite(influence) || influence < 0) continue;
            radiusKm = Math.Max(radiusKm, (position.Position - center.Position).Length + Math.Max(NativeDepotSpawnKm, influence));
            foreach (var poi in AIShipManager.GetPOIs(station, atc)) {
                if (!State(poi, out var point)) continue;
                radiusKm = Math.Max(radiusKm, (point.Position - center.Position).Length + NativeDepotSpawnKm);
            }
        }
        radiusKm += SpawnMarginKm;
        return Finite(radiusKm) && radiusKm > SpawnMarginKm;
    }
    internal static bool CanStage() {
        var player = Player;
        if (player == null || player.bDestroyed || player.objSS == null || CrewSim.system == null || Torching(player)) return false;
        var motion = Motion(player);
        string atc = AIShipManager.strATCLast;
        if (!motion.Finite || !Area(atc, out var center, out double radius) || (motion.Position - center.Position).Length > radius) return false;
        // There must also be some compatible local infrastructure velocity.
        // Ordinary FactionShip filters each actual spawning depot separately.
        foreach (string id in AIShipManager.GetAllStationsInATCRegion(atc))
            if (SlowEnough(player, CrewSim.system.GetShipByRegID(id))) return true;
        return false;
    }
    internal static List<string> Stations(string atc) {
        var result = AIShipManager.GetAllStationsInATCRegion(atc);
        var player = Player;
        result.RemoveAll(id => player == null || !SlowEnough(player, CrewSim.system.GetShipByRegID(id)));
        return result;
    }
    internal static bool Recruitable(AIShip ai) {
        var ship = ai?.Ship;
        var pilot = ai?.AICharacter;
        var player = Player;
        if (ship == null || ship.bDestroyed || ship.bRemove || ship.HideFromSystem || ship.objSS == null ||
            pilot == null || player?.objSS == null || ReferenceEquals(player, ship) || ship.LoadState != Ship.Loaded.Shallow ||
            ship.HasDockGroup || ship.IsDocked() || ship.NavPlayerManned || ship.shipScanTarget != null || ship.shipCombatTarget != null || Reaction(ai) != null) return false;
        if (ship.objSS.bIsBO || ship.objSS.bBOLocked || ship.objSS.bOrbitLocked || ship.fAIPauseTimer > StarSystem.fEpoch ||
            CrewSim.system.GetShipOwner(ship.strRegID) == CrewSim.coPlayer?.strID) return false;
        var board = ai.Blackboard;
        if (board == null || board.Get<bool>("InCombat") || board.Get<bool>("NeedsRepairs") ||
            !string.IsNullOrEmpty(board.Get<string>(StationReturn.DestinationKey)) || pilot.CaptainUs?.HasCond("IsBountyTarget") == true) return false;
        if (Active(ai) != null && !(Active(ai) is FlyToPatrol)) return false;
        if (pilot.PriorityQueue != null && (Waiting(pilot.PriorityQueue) == null || Waiting(pilot.PriorityQueue).Count > 0)) return false;
        double distance = (Motion(ship).Position - Motion(player).Position).Length;
        double speed = (Motion(ship).Velocity - Motion(player).Velocity).Length;
        return Finite(distance) && Finite(speed) && distance <= SpawnMarginKm && speed <= MaxRelativeKmS &&
            !AIShipManager.LowFuel(ship, pilot.MaxSpeed(null));
    }
    internal static void Consider(List<AIShip> candidates, AIShip ai) {
        if (Recruitable(ai)) candidates.Add(ai);
        // Native num3-- still follows this call, so excluded busy ships continue
        // to count against fleet caps. Filtering the entire fleet would not.
    }
    internal static bool Fresh(AIShip ai) {
        if (ai == null || AIShipManager.dictAIs == null) return false;
        foreach (var region in AIShipManager.dictAIs.Values) {
            if (region == null) continue;
            foreach (var existing in region) if (ReferenceEquals(existing, ai) || existing != null && ReferenceEquals(existing.Ship, ai.Ship)) return false;
        }
        return true;
    }
}
