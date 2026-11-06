using System;
using System.Collections.Generic;
using System.Threading;
using HarmonyLib;
using Ostranauts.Ships.AIPilots;

namespace Ostrabus.Inspection;

internal static class DockingInspection {
    const int Capacity = 256;
    static readonly object Gate = new object();
    static readonly Entry[] Ring = new Entry[Capacity];
    static readonly AccessTools.FieldRef<Ship, Dictionary<string, Ship>> Docked = AccessTools.FieldRefAccess<Ship, Dictionary<string, Ship>>("aDocked");
    static readonly AccessTools.FieldRef<AIShip, Ship> AIShipRaw = AccessTools.FieldRefAccess<AIShip, Ship>("_shipUs");
    static long latest, dropped;
    [ThreadStatic] static int depth;

    sealed class Entry {
        internal long Sequence;
        internal DateTime Utc;
        internal double GameEpoch;
        internal string Type;
        internal object Actor, Target;
    }

    internal sealed class Scope {
        internal bool Root, WasLinked, Record;
        internal string Type, ActorPort, TargetPort;
        internal Ship Actor, Target;
    }

    internal static Scope Begin(string type, Ship actor, Ship target, bool enabled = true, string actorPort = null, string targetPort = null) {
        bool root = ++depth == 1;
        return new Scope { Root = root, Record = root && enabled, WasLinked = Linked(actor, target), Type = type,
            Actor = actor, Target = target, ActorPort = actorPort, TargetPort = targetPort };
    }

    internal static void End(Scope scope, Exception exception) {
        if (depth > 0) { depth--; }
        if (scope == null || !scope.Record || exception != null) { return; }
        bool linked = Linked(scope.Actor, scope.Target);
        if ((scope.Type == "dock" || scope.Type == "moor") ? (!scope.WasLinked && linked) : (scope.WasLinked && !linked)) { Record(scope); }
    }

    static bool Linked(Ship actor, Ship target) {
        if (actor == null || target == null) { return false; }
        try {
            var docked = Docked(actor);
            if (docked != null) { foreach (var pair in docked) { if (ReferenceEquals(pair.Value, target)) { return true; } } }
        } catch (Exception) { }
        try {
            var saved = actor.json?.aDocked;
            if (saved != null) { foreach (var pair in saved) { if (pair.Value == target.strRegID) { return true; } } }
        } catch (Exception) { }
        return false;
    }

    static void Record(Scope scope) {
        var entry = new Entry { Utc = DateTime.UtcNow, GameEpoch = StarSystem.fEpoch, Type = scope.Type,
            Actor = ShipState(scope.Actor, scope.ActorPort), Target = ShipState(scope.Target, scope.TargetPort) };
        if (!Monitor.TryEnter(Gate)) { Interlocked.Increment(ref dropped); return; }
        try { entry.Sequence = ++latest; Ring[(int)((entry.Sequence - 1) % Capacity)] = entry; }
        finally { Monitor.Exit(Gate); }
    }

    internal static object ShipState(Ship ship, string port) {
        if (ship == null) { return SnapshotJson.Map("id", null); }
        string owner = null, aiType = null, command = null, combatTarget = null;
        var factions = new List<string>();
        try {
            owner = CrewSim.system?.GetShipOwner(ship.strRegID);
            var shipFactions = ship.GetShipFactions();
            if (shipFactions != null) { for (int i = 0; i < shipFactions.Count && i < 32; i++) { if (shipFactions[i] != null) { factions.Add(shipFactions[i].strName); } } }
        } catch (Exception) { }
        try {
            combatTarget = ship.shipCombatTarget?.strRegID;
            int examined = 0;
            if (AIShipManager.dictAIs != null) {
                foreach (var region in AIShipManager.dictAIs) {
                    if (region.Value == null) { continue; }
                    foreach (var ai in region.Value) {
                        if (++examined > 512) { break; }
                        if (ai == null || !ReferenceEquals(AIShipRaw(ai), ship)) { continue; }
                        var character = ai.AICharacter;
                        aiType = character?.AIType.ToString();
                        command = ai.ActiveCommandName;
                        break;
                    }
                    if (aiType != null || command != null || examined > 512) { break; }
                }
            }
        } catch (Exception) { }
        return SnapshotJson.Map("id", ship.strRegID, "name", ship.publicName, "ownerId", owner,
            "shipFactions", factions, "aiType", aiType, "activeCommand", command, "combatTargetId", combatTarget,
            "damageState", ship.DMGStatus.ToString(), "destroyed", ship.bDestroyed,
            "latestStatus", ship.LatestStatus.ToString(), "timeSinceLatestStatus", ship.fTimeSinceLatestStatus, "portId", port);
    }

    internal static object Read(long since, int limit) {
        var entries = new List<Entry>(limit);
        long oldest, newest, next = since;
        lock (Gate) {
            oldest = Math.Max(1, latest - Capacity + 1);
            newest = latest;
            long first = since == 0 ? Math.Max(oldest, latest - limit + 1) : (since >= latest ? latest + 1 : Math.Max(oldest, since + 1));
            for (long seq = first; seq <= latest && entries.Count < limit; seq++) { entries.Add(Ring[(int)((seq - 1) % Capacity)]); next = seq; }
        }
        var items = new List<object>(entries.Count);
        foreach (var entry in entries) {
            items.Add(SnapshotJson.Map("sequence", entry.Sequence, "utc", entry.Utc.ToString("O"), "gameEpoch", entry.GameEpoch,
                "type", entry.Type, "actor", entry.Actor, "target", entry.Target));
        }
        return SnapshotJson.Map("items", items, "nextSince", next, "latestSequence", newest,
            "oldestAvailableSequence", newest == 0 ? 0L : oldest, "overwritten", Math.Max(0, newest - Capacity),
            "cursorOverrun", since > 0 && since < oldest - 1, "droppedOnContention", Interlocked.Read(ref dropped),
            "note", "Successful dock/moor and undock/unmoor transitions observed after plugin startup. actor is the Ship instance whose game method initiated the transition; history resets when the simulation/scene changes or on restart.");
    }

    internal static void Reset() {
        lock (Gate) {
            Array.Clear(Ring, 0, Ring.Length);
            latest = 0;
            Interlocked.Exchange(ref dropped, 0);
        }
    }
}

[HarmonyPatch(typeof(Ship), nameof(Ship.Dock), new Type[] { typeof(Ship), typeof(bool), typeof(string), typeof(string) })]
internal static class DockTransitionPatch {
    static void Prefix(Ship __instance, Ship dockingTarget, bool bSyncOnly, string dockingPortTarget, string dockingPortUs, out DockingInspection.Scope __state) {
        __state = DockingInspection.Begin("dock", __instance, dockingTarget, !bSyncOnly, dockingPortUs, dockingPortTarget);
    }
    static Exception Finalizer(Exception __exception, DockingInspection.Scope __state) { DockingInspection.End(__state, __exception); return __exception; }
}

[HarmonyPatch(typeof(Ship), nameof(Ship.MoorTo), new Type[] { typeof(Ship), typeof(string), typeof(string), typeof(bool) })]
internal static class MoorTransitionPatch {
    static void Prefix(Ship __instance, Ship mooringTarget, string dockingPortOtherShip, string dockingPortUs, bool bSyncOnly, out DockingInspection.Scope __state) {
        __state = DockingInspection.Begin("moor", __instance, mooringTarget, !bSyncOnly, dockingPortUs, dockingPortOtherShip);
    }
    static Exception Finalizer(Exception __exception, DockingInspection.Scope __state) { DockingInspection.End(__state, __exception); return __exception; }
}

[HarmonyPatch(typeof(Ship), nameof(Ship.Undock), new Type[] { typeof(Ship) })]
internal static class UndockTransitionPatch {
    static void Prefix(Ship __instance, Ship objShip, out DockingInspection.Scope __state) { __state = DockingInspection.Begin("undock", __instance, objShip); }
    static Exception Finalizer(Exception __exception, DockingInspection.Scope __state) { DockingInspection.End(__state, __exception); return __exception; }
}

[HarmonyPatch(typeof(Ship), nameof(Ship.UnMoorFrom), new Type[] { typeof(Ship) })]
internal static class UnmoorTransitionPatch {
    static void Prefix(Ship __instance, Ship oldMooringTarget, out DockingInspection.Scope __state) { __state = DockingInspection.Begin("unmoor", __instance, oldMooringTarget); }
    static Exception Finalizer(Exception __exception, DockingInspection.Scope __state) { DockingInspection.End(__state, __exception); return __exception; }
}
