using System;
using System.Diagnostics;
using HarmonyLib;
using Ostrabus.ShipNavigation;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using Ostranauts.Ships;
using Ostranauts.ShipGUIs.Utilities;
using System.Globalization;
using System.Collections.Generic;

namespace Ostrabus.Inspection;

// Explicit debug intervention, not a background navigation policy or a loiter controller.
internal static class ShipVelocityRecovery {
    const string Suffix = "/match-velocity";
    const string ReleaseSuffix = "/release-hold";
    const string HoldMarker = "Ostrabus.VelocityRecovery.Hold";
    const double KmPerAU = 149597872;
    static readonly AccessTools.FieldRef<AIShip, Ostranauts.Ships.Commands.ICommand> Active = AccessTools.FieldRefAccess<AIShip, Ostranauts.Ships.Commands.ICommand>("_activeCommand");
    static readonly AccessTools.FieldRef<AIShip, Ostranauts.Ships.Commands.ICommand> Reaction = AccessTools.FieldRefAccess<AIShip, Ostranauts.Ships.Commands.ICommand>("_activeReaction");
    static readonly AccessTools.FieldRef<QueuedCommandsNode, Queue<ICommand>> Queue = AccessTools.FieldRefAccess<QueuedCommandsNode, Queue<ICommand>>("commandQueue");
    static readonly AccessTools.FieldRef<QueuedCommandsNode, ICommand> Current = AccessTools.FieldRefAccess<QueuedCommandsNode, ICommand>("currentCommand");
    static readonly AccessTools.FieldRef<SelectorCommand, List<ICommand>> Choices = AccessTools.FieldRefAccess<SelectorCommand, List<ICommand>>("_childCommands");
    static readonly System.Reflection.MethodInfo FindTarget = AccessTools.Method(typeof(TargetNewIfNone), "FindTarget");
    static readonly System.Reflection.MethodInfo GeneratePlan = AccessTools.Method(typeof(TargetNewIfNone), "GenerateNavData");
    static readonly System.Reflection.MethodInfo NeededFuel = AccessTools.Method(typeof(TargetNewIfNone), "GetNeededFuel");
    static readonly Type CandidateType = AccessTools.Inner(typeof(TargetNewIfNone), "TargetDataDTO");
    static readonly AccessTools.FieldRef<object, TargetData> CandidateTarget = AccessTools.FieldRefAccess<object, TargetData>(AccessTools.Field(CandidateType, "TargetData"));
    static readonly AccessTools.FieldRef<object, NavData> CandidatePlan = AccessTools.FieldRefAccess<object, NavData>(AccessTools.Field(CandidateType, "NavData"));
    static readonly AccessTools.FieldRef<object, double> CandidateFuel = AccessTools.FieldRefAccess<object, double>(AccessTools.Field(CandidateType, "FuelCostRCS"));
    static bool LocalMission(AIShip ai) => ai.AIType == AIType.Navy || ai.AIType == AIType.Police || ai.AIType == AIType.Pirate || ai.AIType == AIType.Scav || ai.AIType == AIType.Civilian;
    static bool NextTrip(AIShip ai, out TargetData target, out NavData plan, out double fuel) {
        target = null; plan = null; fuel = -1;
        var selection = new TargetNewIfNone(ai.AICharacter);
        if (LocalMission(ai)) {
            object candidate = FindTarget.Invoke(selection, null);
            if (candidate == null) return false;
            target = CandidateTarget(candidate); plan = CandidatePlan(candidate); fuel = CandidateFuel(candidate);
        } else {
            // Retain a hauler's existing delivery goal rather than choose a new job.
            var ship = ai.Ship;
            target = ship.shipScanTarget != null ? new TargetData(ship.shipScanTarget) : ship.shipSituTarget != null ? new TargetData(ship.shipSituTarget) : null;
            if (target == null) return false;
            if (target.TargetIsBoLocked) {
                plan = (NavData)GeneratePlan.Invoke(selection, new object[] { target });
                if (plan?.Origin == null || plan.Destination == null) return false;
                fuel = plan.Origin.FuelLevel - plan.Destination.FuelLevel;
            } else fuel = (double)NeededFuel.Invoke(selection, new object[] { target });
        }
        return target?.HasValidTarget == true && (!target.TargetIsBoLocked || plan?.Destination != null) && Finite(fuel) && fuel >= 0;
    }
    internal static bool IsRoute(string path) => path != null && path.StartsWith("/ships/", StringComparison.Ordinal) &&
        (path.EndsWith(Suffix, StringComparison.Ordinal) || path.EndsWith(ReleaseSuffix, StringComparison.Ordinal));
    static bool Marked(ICommand command) => command is Hold && command.SaveData?.Length == 1 && command.SaveData[0] == HoldMarker;
    internal static bool RecoveryHold(AIShip ai) {
        var queue = ai?.AICharacter?.PriorityQueue;
        var ship = ai?.Ship;
        if (queue == null || ship == null || ship.bDestroyed || !double.IsPositiveInfinity(ship.fAIDockingExpire)) return false;
        return Marked(Current(queue)) && Queue(queue).Count == 0 || Current(queue) == null && Queue(queue).Count == 1 && Marked(Queue(queue).Peek());
    }
    static bool ValidId(string id) {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64) return false;
        foreach (char c in id) if (char.IsControl(c) || char.IsWhiteSpace(c) || c == '/' || c == '\\') return false;
        return true;
    }
    internal static InspectionApi.Reply Run(string path, string referenceId, bool apply) {
        string id;
        bool release = path.EndsWith(ReleaseSuffix, StringComparison.Ordinal);
        try { id = Uri.UnescapeDataString(path.Substring(7, path.Length - 7 - (release ? ReleaseSuffix.Length : Suffix.Length))); }
        catch (Exception ex) when (ex is UriFormatException || ex is ArgumentOutOfRangeException) { return Error(400, "Invalid registry ID."); }
        if (!ValidId(id) || !release && !ValidId(referenceId)) return Error(400, "Specify one exact ship ID and referenceId (ship or celestial body).");
        var system = CrewSim.system;
        if (system?.dictShips == null || AIShipManager.dictAIs == null || CrewSim.coPlayer == null) return Error(503, "Simulation is not ready.");
        if (!system.dictShips.TryGetValue(id, out var ship) || ship == null || ship.bDestroyed) return Error(404, "Ship not found.");
        if (ship.bRemove) return Error(409, "Ship is already scheduled for removal; recovery cannot safely cancel an in-progress deletion.");
        if (referenceId == id) return Error(400, "Reference must be a different object.");
        double vx = 0, vy = 0;
        string referenceKind;
        if (release) referenceKind = null;
        else if (system.dictShips.TryGetValue(referenceId, out var reference)) {
            if (reference == null || reference.bDestroyed || reference.HideFromSystem || reference.objSS == null) return Error(404, "Reference ship is unavailable or hidden.");
            vx = reference.objSS.vVelX; vy = reference.objSS.vVelY; referenceKind = "ship";
        } else {
            var body = system.GetBO(referenceId);
            if (body == null || body.IsPlaceholder()) return Error(404, "Visible reference ship or celestial body not found.");
            vx = body.dVelX; vy = body.dVelY; referenceKind = "body";
        }
        var controllers = ControllerInspection.Capture(Stopwatch.GetTimestamp());
        if (!controllers.Complete) return Error(503, "Controller scan incomplete; retry.");
        var ai = controllers.Single(id, ship);
        bool held = ai != null && RecoveryHold(ai);
        string reason = null;
        if (!ship.IsAIShip || ship.HideFromSystem || ship.objSS == null || ship.IsStation() || ship.objSS.bIsBO || ship.objSS.bBOLocked || ship.objSS.bOrbitLocked || ship.objSS.bGrounded)
            reason = "Only visible free AI vessels are supported.";
        else if (ReferenceEquals(ship, CrewSim.coPlayer.ship) || ship.NavPlayerManned || system.GetShipOwner(id) == CrewSim.coPlayer.strID)
            reason = "Player ships are protected.";
        else if (ship.IsDocked() || ship.HasDockGroup) reason = "Connected ships must be detached first; group motion is not changed.";
        else if (ai?.AICharacter?.BehaviorTree == null || ai.Blackboard == null) reason = "Exactly one live controller with a native behavior tree is required.";
        else if (ai.AIType == AIType.Auto || ai.AIType == AIType.Missile || ai.AIType == AIType.Station) reason = "Autopilot, projectile and station controllers are outside AI vessel recovery.";
        else if (ship.shipCombatTarget != null || ai.Blackboard.Get<bool>("InCombat") || Reaction(ai) != null) reason = "Active combat/reactions are protected.";
        else if (ai.AICharacter.PriorityQueue == null) reason = "Native priority queue is unavailable.";
        else if (!(ai.AICharacter.BehaviorTree is SelectorCommand root) || Choices(root).Count == 0 || !ReferenceEquals(Choices(root)[0], ai.AICharacter.PriorityQueue))
            reason = "Native priority queue is not the first behavior branch; a safe Hold cannot be established.";
        else if (ai.AICharacter.PriorityQueue.CanExecute(ai.Blackboard) && !held) reason = "A priority order is active; it is not discarded by velocity recovery.";
        else if (!release && (!Finite(vx) || !Finite(vy) || !Finite(ship.objSS.vVelX) || !Finite(ship.objSS.vVelY))) reason = "Velocity is not finite.";
        if (reason != null) return new InspectionApi.Reply(apply ? 409 : 200, SnapshotJson.Map("id", id, "eligible", false, "applied", false, "reason", reason));
        StickySequenceCommand mission = null;
        if (LocalMission(ai)) {
            var branches = Choices((SelectorCommand)ai.AICharacter.BehaviorTree);
            mission = branches[branches.Count - 1] as StickySequenceCommand;
            if (mission?.FindCommand(typeof(TargetNewIfNone)) == null) return Error(409, "Native local mission branch is unavailable; recovery did not change it.");
        }
        if (release) {
            if (!held) return Error(409, "This vessel has no isolated velocity-recovery Hold to release.");
            if (apply) {
                // Ask native planning without entering RunCommand's unregister/
                // cleanup continuation. Failure leaves the Hold authoritative.
                if (!NextTrip(ai, out var target, out var plan, out double fuel))
                    return new InspectionApi.Reply(409, SnapshotJson.Map("id", id, "eligible", false, "applied", false, "held", true,
                        "reason", "Native planning found no feasible next trip; the recovery Hold was retained."));
                target.ApplyToShip(ship);
                ship.objSS.NavData = plan;
                ai.Blackboard.Set("TripFuelPrediction", "VelocityRecovery." + StarSystem.fEpoch.ToString("R", CultureInfo.InvariantCulture) + "&" + fuel.ToString("R", CultureInfo.InvariantCulture));
                var queue = ai.AICharacter.PriorityQueue;
                Current(queue)?.OnExit(ai.Blackboard);
                Current(queue) = null; Queue(queue).Clear();
                Active(ai) = null;
                ship.fAIDockingExpire = double.NegativeInfinity;
                ship.fAIPauseTimer = 0;
                ship.objSS.bIgnoreGrav = false;
            }
            return new InspectionApi.Reply(200, SnapshotJson.Map("id", id, "eligible", true, "applied", apply, "held", !apply,
                "note", "POST releases only after native planning accepts a next trip; failed planning retains the Hold. GET checks the Hold without planning or changing game state."));
        }
        double beforeX = ship.objSS.vVelX, beforeY = ship.objSS.vVelY;
        double dx = (vx - beforeX) * KmPerAU, dy = (vy - beforeY) * KmPerAU;
        if (!Finite(dx) || !Finite(dy) || !Finite(dx * dx + dy * dy)) return Error(409, "Velocity difference is not usable.");
        bool freshMission = LocalMission(ai);
        if (apply) {
            // Clear corrupt local intent, but do not run an unproven new mission.
            // A native queued Hold keeps this controller registered until release.
            // Haulers retain delivery state behind that Hold.
            if (freshMission) {
                // Selector.RunCommand does not update its saved current-child
                // index reliably. Exit the verified native Main directly.
                mission.OnExit(ai.Blackboard);
                Active(ai) = null;
                ship.ClearShipTarget();
                if (ship.json != null) {
                    ship.json.strScanTargetID = null;
                    ship.json.strTargetRegID = null;
                    ship.json.objSituScanTarget = null;
                    ship.json.strStationKeepingTargetID = null;
                }
                ship.shipStationKeepingTarget = null;
                ship.fAIPauseTimer = 0;
                ship.fAIDockingExpire = double.NegativeInfinity;
            }
            NavigationRoute.Stop(ship);
            NavigationFuel.ClearPrediction(ship);
            ai.Blackboard.Remove("RejectedPaths");
            ship.objSS.vVelX = vx; ship.objSS.vVelY = vy;
            ship.objSS.fW = ship.objSS.fA = 0;
            ship.objSS.bIgnoreGrav = false;
            if (!held) ai.AddCommand(new Hold(ai.AICharacter) { SaveData = new[] { HoldMarker } });
            ship.fAIPauseTimer = 0;
            ship.fAIDockingExpire = double.PositiveInfinity;
            ship.ShipCO?.ZeroCondAmount("IsStale");
            // Prevent an SFF between this request and the next AI tick from
            // replaying the cancelled flight leaf instead of the priority Hold.
            Active(ai) = ai.AICharacter.PriorityQueue;
        }
        return new InspectionApi.Reply(200, SnapshotJson.Map("id", id, "eligible", true, "applied", apply, "referenceId", referenceId,
            "freshMission", freshMission, "held", apply || held, "wouldHold", true, "retainedDestinationId", freshMission ? null : ship.targetRegID,
            "referenceKind", referenceKind, "velocityBeforeKmPerSecond", SnapshotJson.Map("x", beforeX * KmPerAU, "y", beforeY * KmPerAU),
            "velocityAfterKmPerSecond", SnapshotJson.Map("x", vx * KmPerAU, "y", vy * KmPerAU), "deltaVKmPerSecond", Math.Sqrt(dx * dx + dy * dy),
            "note", "Debug velocity match without changing position, fuel, crew, ownership or controller. A native queued Hold prevents new mission selection and retirement until explicit release-hold. Haulers retain delivery state. This is a hold order, not continuous velocity matching or an orbit lock; native priority orders suspend ordinary ship mission/combat decisions."));
    }
    static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    static InspectionApi.Reply Error(int code, string message) => new(code, SnapshotJson.Map("error", message));
}

// Some native role callbacks (notably PolicePilot.FFWD) relocate a vessel even
// when its current command is a priority queue. An operator's recovery Hold
// must survive that boundary, including after a save is loaded in a new process.
[HarmonyPatch(typeof(AIShip), nameof(AIShip.FFWD))]
internal static class ShipVelocityRecoverySffPatch {
    static bool Prefix(AIShip __instance) => !ShipVelocityRecovery.RecoveryHold(__instance);
}
