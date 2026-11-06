using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.Ships.AIPilots;

namespace Ostrabus.Inspection;

internal static class InspectionEventHistory {
    const int Capacity = 512;
    static readonly object Gate = new object();
    static readonly Entry[] Ring = new Entry[Capacity];
    static long latest, dropped;

    sealed class Entry {
        internal long Sequence;
        internal DateTime Utc;
        internal double GameEpoch;
        internal string Type, ShipId;
        internal object Data;
    }

    internal static void Add(string type, string shipId, object data) {
        var entry = new Entry { Utc = DateTime.UtcNow, GameEpoch = StarSystem.fEpoch, Type = type, ShipId = shipId, Data = data };
        if (!Monitor.TryEnter(Gate)) { Interlocked.Increment(ref dropped); return; }
        try { entry.Sequence = ++latest; Ring[(int)((entry.Sequence - 1) % Capacity)] = entry; }
        finally { Monitor.Exit(Gate); }
    }

    internal static object Read(long since, int limit, string shipId = null, string type = null) {
        var entries = new List<Entry>(limit);
        long oldest, newest, next = since;
        lock (Gate) {
            oldest = Math.Max(1, latest - Capacity + 1);
            newest = latest;
            if (since == 0) {
                for (long seq = latest; seq >= oldest && entries.Count < limit; seq--) {
                    var entry = Ring[(int)((seq - 1) % Capacity)];
                    if (entry != null && entry.Sequence == seq && Matches(entry, shipId, type)) { entries.Add(entry); }
                }
                entries.Reverse();
                next = latest;
            } else {
                long first = since >= latest ? latest + 1 : Math.Max(oldest, since + 1);
                for (long seq = first; seq <= latest && entries.Count < limit; seq++) {
                    var entry = Ring[(int)((seq - 1) % Capacity)];
                    next = seq;
                    if (entry != null && entry.Sequence == seq && Matches(entry, shipId, type)) { entries.Add(entry); }
                }
                if (next < latest && entries.Count < limit) { next = latest; }
            }
        }
        var items = new List<object>(entries.Count);
        foreach (var entry in entries) {
            items.Add(SnapshotJson.Map("sequence", entry.Sequence, "utc", entry.Utc.ToString("O"), "gameEpoch", entry.GameEpoch,
                "type", entry.Type, "shipId", entry.ShipId, "data", entry.Data));
        }
        return SnapshotJson.Map("items", items, "nextSince", next, "latestSequence", newest,
            "oldestAvailableSequence", newest == 0 ? 0L : oldest, "overwritten", Math.Max(0, newest - Capacity),
            "cursorOverrun", since > 0 && since < oldest - 1, "droppedOnContention", Interlocked.Read(ref dropped),
            "note", "In-memory event history; only captured events since the current simulation/scene started are available. Damage events identify the damaged ship, not necessarily the attacker.");
    }

    static bool Matches(Entry entry, string shipId, string type) {
        bool typeMatch = type == null || entry.Type == type || (type == "ai" && entry.Type.StartsWith("ai", StringComparison.Ordinal)) ||
            (type == "combat" && (entry.Type == "shotFired" || entry.Type == "shipDamaged"));
        return typeMatch && (shipId == null || entry.ShipId == shipId || (entry.Data is Dictionary<string, object> data &&
            (data.TryGetValue("targetId", out var targetId) && targetId as string == shipId)));
    }

    internal static void Reset() {
        lock (Gate) { Array.Clear(Ring, 0, Ring.Length); latest = 0; Interlocked.Exchange(ref dropped, 0); }
    }
}

internal static class CombatEventInspection {
    internal static void ShipFired(string shipId, string weaponId) {
        Ship ship = CrewSim.system?.GetShipByRegID(shipId);
        string targetId = ship?.shipCombatTarget?.strRegID;
        InspectionEventHistory.Add("shotFired", shipId, SnapshotJson.Map("shooter", DockingInspection.ShipState(ship, null),
            "targetId", targetId, "weaponId", weaponId, "note", "Raised by the game's successful weapon-fire event."));
    }

    internal static void ShipDamaged(string shipId) {
        Ship ship = CrewSim.system?.GetShipByRegID(shipId);
        InspectionEventHistory.Add("shipDamaged", shipId, SnapshotJson.Map("target", DockingInspection.ShipState(ship, null),
            "note", "Raised by the game's ship damage event; the event does not identify the attacker."));
    }
}

internal static class AIBehaviorHistory {
    const double SampleSeconds = 1.0;
    const int MaxShips = 512;
    static readonly AccessTools.FieldRef<AIShip, Ship> AIShipRaw = AccessTools.FieldRefAccess<AIShip, Ship>("_shipUs");
    static readonly AccessTools.FieldRef<Blackboard, Dictionary<string, object>> BoardData = AccessTools.FieldRefAccess<Blackboard, Dictionary<string, object>>("_data");
    static readonly AccessTools.FieldRef<Ship, Dictionary<string, Ship>> Docked = AccessTools.FieldRefAccess<Ship, Dictionary<string, Ship>>("aDocked");
    sealed class State { internal string Signature; internal object Data; }
    static readonly Dictionary<string, State> Previous = new Dictionary<string, State>();
    static long lastSample;

    internal static void Capture(double epoch) {
        long now = Stopwatch.GetTimestamp();
        if (now - lastSample < Stopwatch.Frequency * SampleSeconds || CrewSim.Paused || double.IsNaN(epoch) || double.IsInfinity(epoch) || AIShipManager.dictAIs == null) { return; }
        lastSample = now;
        int examined = 0;
        var seen = new HashSet<string>();
        foreach (var region in AIShipManager.dictAIs) {
            if (region.Value == null) { continue; }
            foreach (var ai in region.Value) {
                if (++examined > MaxShips) { return; }
                if (ai == null) { continue; }
                Ship ship = AIShipRaw(ai);
                if (ship == null || string.IsNullOrEmpty(ship.strRegID)) { continue; }
                seen.Add(ship.strRegID);
                State current = ReadState(ship, ai);
                if (Previous.TryGetValue(ship.strRegID, out var previous)) {
                    if (previous.Signature != current.Signature) {
                        InspectionEventHistory.Add("aiStateChanged", ship.strRegID,
                            SnapshotJson.Map("previous", previous.Data, "current", current.Data));
                    }
                }
                Previous[ship.strRegID] = current;
            }
        }
        var missing = new List<string>();
        foreach (var pair in Previous) { if (!seen.Contains(pair.Key)) { missing.Add(pair.Key); } }
        foreach (string id in missing) {
            InspectionEventHistory.Add("aiUnregistered", id, SnapshotJson.Map("previous", Previous[id].Data));
            Previous.Remove(id);
        }
    }

    static State ReadState(Ship ship, AIShip ai) {
        var docked = new List<string>();
        var rawDocked = Docked(ship);
        if (rawDocked != null) {
            foreach (var pair in rawDocked) { if (pair.Value != null && docked.Count < 32) { docked.Add(pair.Value.strRegID); } }
        }
        docked.Sort(StringComparer.Ordinal);
        string command = ai.ActiveCommandName;
        string scanTarget = ship.shipScanTarget?.strRegID;
        string combatTarget = ship.shipCombatTarget?.strRegID;
        var board = ai.Blackboard;
        var data = board == null ? null : BoardData(board);
        object combatValue = null;
        bool inCombatPresent = data != null && data.TryGetValue("InCombat", out combatValue) && combatValue is bool;
        object inCombat = inCombatPresent ? combatValue : null;
        var enemies = ShipIds(data, "Enemies");
        var combatants = ShipIds(data, "Combatants");
        string type = ai.AICharacter?.AIType.ToString();
        var snapshot = SnapshotJson.Map("aiType", type, "activeCommand", command, "scanTargetId", scanTarget,
            "combatTargetId", combatTarget, "inCombat", inCombat, "inCombatPresent", inCombatPresent,
            "enemies", enemies, "combatants", combatants, "dockedShipIds", docked);
        return new State { Data = snapshot, Signature = SnapshotJson.Encode(snapshot) };
    }

    static List<string> ShipIds(Dictionary<string, object> data, string key) {
        var result = new List<string>();
        if (data != null && data.TryGetValue(key, out var value) && value is List<Ship> ships) {
            foreach (var ship in ships) { if (ship != null && result.Count < 32) { result.Add(ship.strRegID); } }
        }
        result.Sort(StringComparer.Ordinal);
        return result;
    }

    internal static void Reset() { Previous.Clear(); lastSample = 0; }
}
