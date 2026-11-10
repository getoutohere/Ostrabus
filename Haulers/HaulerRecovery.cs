using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using Ostrabus.Inspection;
using UnityEngine.SceneManagement;

namespace Ostrabus.Haulers;

internal static class HaulerRecovery {
    static readonly AccessTools.FieldRef<AIShip, Ship> RawShip = AccessTools.FieldRefAccess<AIShip, Ship>("_shipUs");
    static readonly AccessTools.FieldRef<AIShip, ICommand> Active = AccessTools.FieldRefAccess<AIShip, ICommand>("_activeCommand");
    static readonly AccessTools.FieldRef<AIShip, ICommand> Reaction = AccessTools.FieldRefAccess<AIShip, ICommand>("_activeReaction");
    static readonly AccessTools.FieldRef<Blackboard, Dictionary<string, object>> Board = AccessTools.FieldRefAccess<Blackboard, Dictionary<string, object>>("_data");
    static readonly AccessTools.FieldRef<QueuedCommandsNode, Queue<ICommand>> Queue = AccessTools.FieldRefAccess<QueuedCommandsNode, Queue<ICommand>>("commandQueue");
    static readonly AccessTools.FieldRef<QueuedCommandsNode, ICommand> QueuedActive = AccessTools.FieldRefAccess<QueuedCommandsNode, ICommand>("currentCommand");
    internal sealed class State {
        public State() {}
        internal bool PendingRelease;
        internal long Returns, Stops;
        internal double Epoch;
        internal string Reason;
    }
    static ConditionalWeakTable<Ship, State> states = new ConditionalWeakTable<Ship, State>();
    internal static bool Eligible(AIShip ai, Ship ship) {
        if (ai?.AICharacter == null || ship == null || ship.bDestroyed || ship.objSS == null || !ship.IsAIShip ||
            ship.HideFromSystem || ai.AIType != AIType.HaulerDeployer || !ReferenceEquals(RawShip(ai), ship) ||
            ReferenceEquals(CrewSim.coPlayer?.ship, ship) || ship.NavPlayerManned || ship.shipCombatTarget != null || Reaction(ai) != null) return false;
        var board = ai.Blackboard == null ? null : Board(ai.Blackboard);
        if (board == null) return true;
        if (board.TryGetValue("InCombat", out object value) && value is bool fighting && fighting) return false;
        return !board.TryGetValue("SurrenderedTo", out object surrendered) || surrendered == null;
    }
    internal static AIShip Controller(Ship ship) {
        if (ship == null || AIShipManager.dictAIs == null) return null;
        foreach (var region in AIShipManager.dictAIs.Values) {
            if (region == null) continue;
            foreach (var ai in region) if (ai != null && ReferenceEquals(RawShip(ai), ship) && Eligible(ai, ship)) return ai;
        }
        return null;
    }
    internal static bool EmptyAndUncontrolled(Ship ship) => ship?.objSS != null && !ship.HasDockGroup && !ship.IsDocked() &&
        ship.GetAllDockedShips().Count == 0 && ship.objSS.NavData == null && ship.objSS.vAccIn.x == 0 && ship.objSS.vAccIn.y == 0;
    internal static bool Targetless(Ship ship) => ship.shipScanTarget == null && ship.shipSituTarget == null;
    internal static void StopAbandonedManeuver(Ship ship, string reason) {
        var s = ship.objSS;
        if (s.vAccRCS.x == 0 && s.vAccRCS.y == 0 && s.fA == 0) return;
        // Native cleanup preserves position, velocity, angular velocity and fuel.
        ship.StopManeuver(ship.LoadState >= Ship.Loaded.Edit, false);
        State state = states.GetOrCreateValue(ship);
        state.Stops++; state.Epoch = StarSystem.fEpoch; state.Reason = reason;
        Ostrabus.OstrabusPlugin.Logger?.LogInfo($"[Hauler Recovery] Stopped abandoned maneuver for {ship.strRegID} ({reason}).");
    }
    internal static bool RestoreHomeTarget(Ship ship, AIShip ai, string reason) {
        if (!Eligible(ai, ship) || !EmptyAndUncontrolled(ship) || !Targetless(ship) || CrewSim.system == null || string.IsNullOrEmpty(ai.HomeStation)) return false;
        Ship destination = CrewSim.system.GetShipByRegID(ai.HomeStation);
        if (destination == null || ReferenceEquals(destination, ship) || destination.bDestroyed || destination.HideFromSystem || destination.objSS == null) return false;
        TargetData target = ai.AICharacter.GetTarget();
        if (target?.Ship != destination || target.Situ == null) return false;
        StopAbandonedManeuver(ship, reason);
        target.ApplyToShip(ship);
        State state = states.GetOrCreateValue(ship);
        state.Returns++; state.Epoch = StarSystem.fEpoch; state.Reason = reason;
        Ostrabus.OstrabusPlugin.Logger?.LogInfo($"[Hauler Recovery] Restored {ship.strRegID} return target {destination.strRegID} ({reason}).");
        return true;
    }
    internal static void AfterIdleAI(AIShip ai) {
        Ship ship = ai == null ? null : RawShip(ai);
        if (!Eligible(ai, ship) || ship.LoadState > Ship.Loaded.Shallow || !EmptyAndUncontrolled(ship) ||
            ship.objSS.bIsBO || ship.objSS.bBOLocked || ship.objSS.bOrbitLocked || Active(ai) != null) return;
        var queue = ai.AICharacter.PriorityQueue;
        if (queue != null && (QueuedActive(queue) != null || Queue(queue)?.Count > 0)) return;
        StopAbandonedManeuver(ship, "idle navigation");
        if (Targetless(ship)) RestoreHomeTarget(ship, ai, "idle return recovery");
    }
    internal static void MarkRelease(Ship ship) { states.GetOrCreateValue(ship).PendingRelease = true; }
    internal static bool PendingRelease(Ship ship) => ship != null && states.TryGetValue(ship, out State state) && state.PendingRelease;
    internal static void EndRelease(Ship ship) {
        if (ship != null && states.TryGetValue(ship, out State state)) state.PendingRelease = false;
    }
    internal static object Snapshot(Ship ship) {
        if (ship == null || !states.TryGetValue(ship, out State state)) return null;
        return SnapshotJson.Map("homeTargetsRestored", state.Returns, "abandonedManeuversStopped", state.Stops,
            "pendingRelease", state.PendingRelease, "lastEpoch", state.Epoch, "lastReason", state.Reason);
    }
    internal static void SceneUnloaded(Scene _) { states = new ConditionalWeakTable<Ship, State>(); }
    internal static void Fault(Exception error) { Ostrabus.OstrabusPlugin.Logger?.LogWarning("[Hauler Recovery] " + error.Message); }
}
