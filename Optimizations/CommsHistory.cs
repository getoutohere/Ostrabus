using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.Ships.Comms;

namespace Ostrabus.Optimizations;

// History retention is a policy change, separate from fixing the oldest-100 UI.
// Run only at lifecycle boundaries; do not interfere with live message delivery.
internal static class CommsHistory {
    internal const double RetentionSeconds = 7 * 24 * 60 * 60;
    static readonly AccessTools.FieldRef<Comms, List<ShipMessage>> Log = AccessTools.FieldRefAccess<Comms, List<ShipMessage>>("_messageLog");
    static readonly AccessTools.FieldRef<StarSystem, Dictionary<string, ShipMessage>> EnRoute = AccessTools.FieldRefAccess<StarSystem, Dictionary<string, ShipMessage>>("_messagesEnRoute");
    static readonly Dictionary<string, int> latest = new Dictionary<string, int>(StringComparer.Ordinal);
    static readonly HashSet<MessageKey> pending = new HashSet<MessageKey>();
    static bool sweeping;
    static int loadGeneration;

    // Same components as native ShipMessage.ID, without allocating concatenated
    // IDs for every log entry. Restored queue/log objects need not share identity.
    internal readonly struct MessageKey : IEquatable<MessageKey> {
        readonly string sender, receiver, interaction;
        readonly double time;
        internal MessageKey(ShipMessage message) {
            sender = message.SenderRegId; receiver = message.ReceiverRegId;
            interaction = message.Interaction?.strName ?? ""; time = message.AvailableTime;
        }
        public bool Equals(MessageKey other) => sender == other.sender && receiver == other.receiver && interaction == other.interaction && time.Equals(other.time);
        public override bool Equals(object obj) => obj is MessageKey key && Equals(key);
        public override int GetHashCode() {
            unchecked { return (((sender?.GetHashCode() ?? 0) * 397 ^ (receiver?.GetHashCode() ?? 0)) * 397 ^ (interaction?.GetHashCode() ?? 0)) * 397 ^ time.GetHashCode(); }
        }
    }
    static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    static string Peer(ShipMessage message, string owner) {
        if (message == null) return null;
        if (message.SenderRegId == owner) return message.ReceiverRegId;
        return message.ReceiverRegId == owner ? message.SenderRegId : null;
    }
    internal static int Prune(List<ShipMessage> messages, string owner, double now, HashSet<MessageKey> inFlight, Dictionary<string, int> lastByPeer) {
        if (messages == null || string.IsNullOrEmpty(owner) || !Finite(now) || inFlight == null) return 0;
        lastByPeer.Clear();
        try {
            for (int i = 0; i < messages.Count; i++) {
                string peer = Peer(messages[i], owner);
                if (!string.IsNullOrEmpty(peer)) lastByPeer[peer] = i;
            }
            double cutoff = now - RetentionSeconds;
            int write = 0, before = messages.Count;
            for (int read = 0; read < before; read++) {
                ShipMessage message = messages[read];
                string peer = Peer(message, owner);
                bool remove = message != null && !string.IsNullOrEmpty(peer) && Finite(message.AvailableTime) && message.AvailableTime < cutoff &&
                    lastByPeer[peer] != read && !(message.ReceiverRegId == owner && !message.Read) && !inFlight.Contains(new MessageKey(message));
                if (!remove) messages[write++] = message;
            }
            if (write < before) messages.RemoveRange(write, before - write);
            return before - write;
        } finally { lastByPeer.Clear(); }
    }
    internal static int Sweep(StarSystem system, double now) {
        if (sweeping || system?.dictShips == null || !Finite(now)) return 0;
        var queue = EnRoute(system);
        if (queue == null) return 0; // Cannot establish which records are in flight.
        sweeping = true;
        try {
            foreach (ShipMessage message in queue.Values) {
                // Unknown identities cannot safely be matched against history.
                if (message == null || string.IsNullOrEmpty(message.SenderRegId) || string.IsNullOrEmpty(message.ReceiverRegId) || !Finite(message.AvailableTime)) return 0;
                pending.Add(new MessageKey(message));
            }
            int removed = 0;
            foreach (Ship ship in system.dictShips.Values) {
                if (ship == null || ship.bDestroyed || ship.Comms == null) continue;
                removed += Prune(Log(ship.Comms), ship.strRegID, now, pending, latest);
            }
            return removed;
        } finally { pending.Clear(); latest.Clear(); sweeping = false; }
    }
    internal static void Run(string phase) {
        var sim = CrewSim.objInstance;
        if (sim == null || !sim.FinishedLoading || CrewSim.bShipEdit) return;
        try {
            int removed = Sweep(CrewSim.system, StarSystem.fEpoch);
            OstrabusPlugin.Logger?.LogInfo("[CommsHistory] " + phase + ": removed " + removed + " old records; unread, pending and latest conversation entries retained.");
        } catch (Exception error) {
            // Optional cleanup must not prevent loading or taking a native save.
            OstrabusPlugin.Logger?.LogWarning("[CommsHistory] " + phase + " cleanup failed: " + error);
        }
    }
    internal static void Loaded() {
        var sim = CrewSim.objInstance;
        if (sim == null || CrewSim.bShipEdit) return;
        sim.StartCoroutine(AfterLoad(sim, ++loadGeneration));
    }
    internal static IEnumerator AfterLoad(CrewSim sim, int generation) {
        while (generation == loadGeneration && sim != null && sim == CrewSim.objInstance && !sim.FinishedLoading) yield return null;
        if (generation == loadGeneration && sim != null && sim == CrewSim.objInstance) Run("after load");
    }
    internal static void CancelLoad() { loadGeneration++; }
}

// SaveGame cancels/joins its previous writer before this main-thread snapshot.
// Never edit the archive or DTO once the background SavingJob owns it.
[HarmonyPatch(typeof(LoadManager), "SaveGameData")]
internal static class CommsHistorySavePatch {
    static void Prefix() => CommsHistory.Run("before save snapshot");
}
