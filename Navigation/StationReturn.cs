using System;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;

namespace Ostrabus.ShipNavigation;

// Persist intent, not an unserializable queued command tree. Native leaves still
// own planning, fuel, clearance and DockAndDespawn. No world scan or custom hull motion.
internal static class StationReturn {
    // Persisted wire identifiers intentionally survive the project rename.
    internal const string DestinationKey = "TemplateExample.StationReturn.Destination";
    internal const string DockingKey = "TemplateExample.StationReturn.Docking";
    static readonly AccessTools.FieldRef<AIShip, ICommand> Active = AccessTools.FieldRefAccess<AIShip, ICommand>("_activeCommand");
    static readonly AccessTools.FieldRef<AIShip, ICommand> Reaction = AccessTools.FieldRefAccess<AIShip, ICommand>("_activeReaction");
    static readonly AccessTools.FieldRef<QueuedCommandsNode, Queue<ICommand>> Queue = AccessTools.FieldRefAccess<QueuedCommandsNode, Queue<ICommand>>("commandQueue");
    static readonly AccessTools.FieldRef<QueuedCommandsNode, ICommand> Current = AccessTools.FieldRefAccess<QueuedCommandsNode, ICommand>("currentCommand");
    static readonly AccessTools.FieldRef<BaseCommand, string> NodeName = AccessTools.FieldRefAccess<BaseCommand, string>("_nodeName");
    sealed class Commands {
        internal readonly FlyTo Fly;
        internal readonly Dock Dock;
        internal Commands(AIShip ai) { Fly = new FlyTo(ai.AICharacter); Dock = new Dock(ai.AICharacter); }
    }
    static readonly ConditionalWeakTable<AIShip, Commands> commands = new();
    internal static bool Pending(AIShip ai) => !string.IsNullOrEmpty(ai?.Blackboard?.Get<string>(DestinationKey));
    internal static bool YieldToOtherControl(AIShip ai) => ai.Ship == null || ReferenceEquals(CrewSim.coPlayer?.ship, ai.Ship) ||
        ai.Ship.NavPlayerManned || Reaction(ai) != null ||
        (ai.Ship.HasDockGroup || ai.Ship.IsDocked()) && ai.Ship.IsDockedWithPlayer();
    internal static void Request(AIShip ai, Ship home) {
        Ship ship = ai?.Ship;
        if (ship?.objSS == null || ai.AICharacter == null || ai.Blackboard == null || home?.objSS == null || home.bDestroyed || home == ship) return;
        if (ai.Blackboard.Get<string>(DestinationKey) == home.strRegID) return;
        Bind(ship, home);
        ai.Blackboard.Set("NeedsRepairs", true);
        ai.Blackboard.Set(DestinationKey, home.strRegID);
        ai.Blackboard.Remove(DockingKey);
        NavigationFuel.ClearPrediction(ship);
        RemoveLegacyQueue(ai);
        commands.Remove(ai);
    }
    static bool Bind(Ship ship, Ship home) {
        var before = new NavigationTargetKey(ship);
        new TargetData(home).ApplyToShip(ship);
        bool changed = !before.Same(new NavigationTargetKey(ship));
        if (changed) { NavigationRoute.Stop(ship); NavigationFuel.ClearPrediction(ship); }
        return changed;
    }
    static bool Legacy(ICommand command) => command is StickySequenceCommand node &&
        (NodeName(node) == "CeresNavigationReturn" || NodeName(node) == "ShipboardActionReturn");
    static void RemoveLegacyQueue(AIShip ai) {
        var node = ai.AICharacter.PriorityQueue;
        if (node == null) return;
        if (Legacy(Current(node))) { Current(node).OnExit(ai.Blackboard); Current(node) = null; }
        var queue = Queue(node);
        for (int i = queue.Count; i > 0; i--) {
            var command = queue.Dequeue();
            if (!Legacy(command)) queue.Enqueue(command);
        }
    }
    internal static void Cancel(AIShip ai) {
        ai.Blackboard.Remove(DestinationKey); ai.Blackboard.Remove(DockingKey);
        commands.Remove(ai);
    }
    internal static bool Run(AIShip ai) {
        if (!Pending(ai)) return false;
        Ship ship = ai.Ship;
        if (ship == null || ship.bDestroyed || ship.objSS == null || !ship.IsAIShip || ai.AICharacter == null) { Cancel(ai); return false; }
        if (YieldToOtherControl(ai)) return false;
        Ship home = CrewSim.system?.GetShipByRegID(ai.Blackboard.Get<string>(DestinationKey));
        if (home?.objSS == null || home.bDestroyed || home.HideFromSystem) { Cancel(ai); return false; }
        if (ship.IsDockedWith(home)) {
            if (home.objSS.bIsBO) CrewSim.DockAndDespawn(ship, home);
            Cancel(ai); return true;
        }
        // Never steer a docked/tethered group on behalf of this one ship.
        if (ship.IsDocked() || ship.HasDockGroup) return true;
        bool sameTarget = ship.targetRegID == home.strRegID && ReferenceEquals(ship.shipSituTarget, home.objSS);
        if (!sameTarget) {
            if (Bind(ship, home)) { ai.Blackboard.Remove(DockingKey); commands.Remove(ai); }
        }
        var state = commands.GetValue(ai, key => new Commands(key));
        bool docking = ai.Blackboard.Get<bool>(DockingKey);
        ICommand leaf = docking ? (ICommand)state.Dock : state.Fly;
        // FlyTo.CanExecute can skip a leg already within native docking range.
        if (!docking && !leaf.CanExecute(ai.Blackboard)) {
            ai.Blackboard.Set(DockingKey, true); leaf = state.Dock; docking = true;
        }
        CommandStatus result = leaf.RunCommand(ai.Blackboard);
        Active(ai) = result.Status == CommandCode.Failure ? null : result.ActiveCommand ?? leaf;
        if (result.Status == CommandCode.Running) return true;
        leaf.OnExit(ai.Blackboard);
        if (!docking && result.Status == CommandCode.Finished) ai.Blackboard.Set(DockingKey, true);
        else if (docking && (result.Status == CommandCode.Finished || ship.bDestroyed || ship.HideFromSystem)) Cancel(ai);
        else {
            ai.Blackboard.Remove(DockingKey);
            NavigationRoute.Stop(ship);
        }
        return true;
    }
}

[HarmonyPatch(typeof(AIShip), nameof(AIShip.RunAI))]
internal static class StationReturnPatch {
    [HarmonyPrefix]
    internal static bool Prefix(AIShip __instance) {
        try { return !StationReturn.Run(__instance); }
        catch (Exception error) {
            Ostrabus.OstrabusPlugin.Logger?.LogWarning("[Station Return] " + error);
            return true;
        }
    }
}

// AIShip.FFWD normally queues a one-shot copy of its current native leaf. This
// controller already owns that leaf across the skip; do not leave a second copy
// waiting to run after docking. ShipSitu/NavData motion advancement is unchanged;
// the return resumes its native leaf at the next AI update, without a pilot's
// instant-placement callback. Manual control and reactions retain native FFWD.
[HarmonyPatch(typeof(AIShip), nameof(AIShip.FFWD))]
internal static class StationReturnFastForwardPatch {
    [HarmonyPrefix]
    internal static bool Prefix(AIShip __instance) => !StationReturn.Pending(__instance) || StationReturn.YieldToOtherControl(__instance);
}

// The old queued custom nodes cannot be recreated by AIShip.CreateCommand. Native
// loading otherwise flattens their child payloads into stray queued FlyTo leaves.
// Their persisted module intent is migrated by the two callers instead.
[HarmonyPatch(typeof(QueuedCommandsNode), nameof(QueuedCommandsNode.LoadSaveData))]
internal static class LegacyReturnLoadPatch {
    static bool Legacy(JsonCommandPayload payload) {
        string path = payload?.strPath;
        return path != null && (path.Contains("/CeresNavigationReturn/") || path.EndsWith("/CeresNavigationReturn", StringComparison.Ordinal) ||
            path.Contains("/ShipboardActionReturn/") || path.EndsWith("/ShipboardActionReturn", StringComparison.Ordinal));
    }
    [HarmonyPrefix]
    internal static void Prefix(ref JsonCommandPayload[] saveData) {
        if (saveData == null) return;
        for (int i = 0; i < saveData.Length; i++) {
            if (!Legacy(saveData[i])) continue;
            var keep = new List<JsonCommandPayload>(saveData.Length);
            foreach (var payload in saveData) if (!Legacy(payload)) keep.Add(payload);
            saveData = keep.ToArray(); return;
        }
    }
}

