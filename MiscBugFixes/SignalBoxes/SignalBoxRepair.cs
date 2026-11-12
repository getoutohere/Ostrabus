using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using HarmonyLib;
using Ostranauts.Electrical;

namespace Ostrabus.MiscBugFixes.SignalBoxes;

// Native Electrical queues and gates remain authoritative. These helpers repair
// their mutation/persistence boundaries; no independent signal simulation runs.
internal static class SignalBoxRepair {
    internal sealed class DeviceState {
        internal readonly WeakReference<Electrical> Owner;
        internal readonly HashSet<string> Blocked = new HashSet<string>(StringComparer.Ordinal);
        internal readonly StringBuilder Text = new StringBuilder();
        internal readonly List<string> RetiredInputs = new List<string>();
        internal List<string> RemovedOutputs;
        internal WeakReference<Ship> WaitingOn;
        internal bool Waiting, WaitingAudit, Registered, Dirty = true;
        internal DeviceState(Electrical owner) { Owner = new WeakReference<Electrical>(owner); }
    }

    sealed class ShipState {
        internal readonly Dictionary<string, int> Switching = new Dictionary<string, int>(StringComparer.Ordinal);
        internal readonly List<WeakReference<Electrical>> Waiting = new List<WeakReference<Electrical>>();
    }

    internal sealed class DispatchScope {
        internal Electrical Owner;
        internal DeviceState State;
        internal int Index;
        internal readonly List<ElectricalSignal> Retained = new List<ElectricalSignal>();
        internal DispatchScope Previous, NextFree;
    }

    internal struct ModeScope {
        internal Ship Ship;
        internal string Id;
    }

    static readonly ConditionalWeakTable<Electrical, DeviceState> Devices = new ConditionalWeakTable<Electrical, DeviceState>();
    static readonly ConditionalWeakTable<Ship, ShipState> Ships = new ConditionalWeakTable<Ship, ShipState>();
    static readonly AccessTools.FieldRef<Ship, bool> DoneLoading = AccessTools.FieldRefAccess<Ship, bool>("_bDoneLoading");
    static readonly AccessTools.FieldRef<Electrical, bool> NeedsCheck = AccessTools.FieldRefAccess<Electrical, bool>("bNeedsCheck");
    static readonly AccessTools.FieldRef<Electrical, double> Deadline = AccessTools.FieldRefAccess<Electrical, double>("fTimeOfNextSignalCheck");
    static readonly AccessTools.FieldRef<Electrical, List<ElectricalSignal>> Outgoing = AccessTools.FieldRefAccess<Electrical, List<ElectricalSignal>>("sendQueue");
    static readonly AccessTools.FieldRef<Electrical, List<ElectricalSignal>> Incoming = AccessTools.FieldRefAccess<Electrical, List<ElectricalSignal>>("signalQueue");
    static readonly AccessTools.FieldRef<Electrical, Dictionary<string, string>> Properties = AccessTools.FieldRefAccess<Electrical, Dictionary<string, string>>("mapGPM");
    static readonly Func<Electrical, string, ElectricalSignal, bool> NativeSend = AccessTools.MethodDelegate<Func<Electrical, string, ElectricalSignal, bool>>(AccessTools.Method(typeof(Electrical), nameof(Electrical.SendSignal)));
    [ThreadStatic] static DispatchScope current;
    [ThreadStatic] static DispatchScope free;

    internal static bool DefinitionIsBox(string id) {
        if (id == null || DataHandler.dictCOs == null) return false;
        if (DataHandler.dictCOOverlays != null && DataHandler.dictCOOverlays.TryGetValue(id, out var overlay)) id = overlay.strCOBase;
        if (id == null || !DataHandler.dictCOs.TryGetValue(id, out var definition) || definition.aStartingConds == null) return false;
        foreach (string condition in definition.aStartingConds) if (condition != null && condition.StartsWith("IsElectricalBox=", StringComparison.Ordinal)) return true;
        return false;
    }

    internal static bool IsBox(Electrical owner) => owner != null && owner.coSelf != null &&
        ((owner.coSelf.mapConds != null && owner.coSelf.HasCond("IsElectricalBox")) || DefinitionIsBox(owner.coSelf.strCODef));

    static bool BoxId(string id) {
        if (id == null) return false;
        if (DataHandler.mapCOs != null && DataHandler.mapCOs.TryGetValue(id, out var live) && live != null &&
            ((live.mapConds != null && live.HasCond("IsElectricalBox")) || DefinitionIsBox(live.strCODef))) return true;
        // Saved type proves only relationship scope, never endpoint existence.
        return DataHandler.dictCOSaves != null && DataHandler.dictCOSaves.TryGetValue(id, out var saved) && DefinitionIsBox(saved.strCODef);
    }

    internal static bool Related(Electrical owner) {
        if (owner == null || owner.coSelf == null) return false;
        // Finish bookkeeping even after the last box is permanently removed
        // from runtime and saved registries. This weak association dies with
        // the component; it cannot keep the removed box or ship alive.
        if (Devices.TryGetValue(owner, out _)) return true;
        if (IsBox(owner)) return true;
        if (owner.inputConnections != null) foreach (string id in owner.inputConnections.Keys) if (BoxId(id)) return true;
        if (owner.outputConnections != null) foreach (string id in owner.outputConnections.Keys) if (BoxId(id)) return true;
        var queue = Outgoing(owner);
        if (queue != null) foreach (var signal in queue) if (BoxId(signal.originID)) return true;
        queue = Incoming(owner);
        if (queue != null) foreach (var signal in queue) if (BoxId(signal.originID)) return true;
        return false;
    }

    internal static bool Authoritative(Ship ship) => ship != null && !ship.bDestroyed && ship.LoadState == Ship.Loaded.Full && DoneLoading(ship) &&
        CrewSim.system?.dictShips != null && ship.strRegID != null && CrewSim.system.dictShips.TryGetValue(ship.strRegID, out var registered) && ReferenceEquals(registered, ship);

    static bool Switching(Ship ship, string id) => ship != null && id != null && Ships.TryGetValue(ship, out var state) && state.Switching.ContainsKey(id);
    static bool Ready(Electrical owner) => owner != null && owner.coSelf != null && !owner.isInitialising &&
        !owner.coSelf.bDestroyed && ReferenceEquals(owner.coSelf.Electrical, owner) && ReferenceEquals(owner.coSelf.ship, owner.coShip) &&
        Authoritative(owner.coShip) && DataHandler.mapCOs != null && DataHandler.mapCOs.TryGetValue(owner.coSelf.strID, out var registered) &&
        ReferenceEquals(registered, owner.coSelf) && !Switching(owner.coShip, owner.coSelf.strID);

    internal static bool EndpointTemporary(Ship ship, string id) => !Authoritative(ship) || Switching(ship, id);

    static DeviceState State(Electrical owner) => Devices.GetValue(owner, value => new DeviceState(value));

    internal static void Schedule(Electrical owner) {
        if (owner == null || owner.coSelf == null || owner.coSelf.Clock == null) return;
        double now = owner.coSelf.Clock.Epoch;
        if (!NeedsCheck(owner) || Deadline(owner) < now) Deadline(owner) = now + .1;
        NeedsCheck(owner) = true;
    }

    static void Wait(Electrical owner, DeviceState state, string destination = null, bool audit = false, Ship fallbackShip = null) {
        state.Waiting = true;
        state.WaitingAudit |= audit;
        if (destination != null) state.Blocked.Add(destination);
        Ship ship = owner.coSelf?.ship ?? owner.coShip ?? fallbackShip;
        if (ship == null) return;
        if (state.Registered && state.WaitingOn != null && state.WaitingOn.TryGetTarget(out var previous) && ReferenceEquals(previous, ship)) return;
        if (state.WaitingOn == null) state.WaitingOn = new WeakReference<Ship>(ship);
        else state.WaitingOn.SetTarget(ship);
        Ships.GetValue(ship, value => new ShipState()).Waiting.Add(state.Owner);
        state.Registered = true;
    }

    internal static void Wake(Ship ship) {
        if (!Authoritative(ship) || !Ships.TryGetValue(ship, out var waiting) || waiting.Switching.Count != 0) return;
        int kept = 0;
        for (int i = 0; i < waiting.Waiting.Count; i++) {
            var weak = waiting.Waiting[i];
            if (!weak.TryGetTarget(out var owner) || owner == null || !Devices.TryGetValue(owner, out var state)) continue;
            if (owner.coSelf == null) { waiting.Waiting[kept++] = weak; continue; }
            if (!ReferenceEquals(owner.coSelf.ship, ship)) {
                if (state.WaitingOn != null && state.WaitingOn.TryGetTarget(out var previous) && ReferenceEquals(previous, ship)) state.Registered = false;
                continue;
            }
            if (!Ready(owner) || !CanSerialize(owner)) { waiting.Waiting[kept++] = weak; continue; }
            if (state.RemovedOutputs?.Count > 0) SaveRemovedOutputs(owner, state);
            state.Registered = state.Waiting = false;
            state.WaitingOn?.SetTarget(null);
            state.Blocked.Clear();
            if (state.WaitingAudit) owner.fTimeToNextAudit = 0;
            state.WaitingAudit = false;
            if (Outgoing(owner).Count != 0 || Incoming(owner).Count != 0) Schedule(owner);
        }
        if (kept < waiting.Waiting.Count) waiting.Waiting.RemoveRange(kept, waiting.Waiting.Count - kept);
    }

    internal static bool BeforeAudit(Electrical owner, out int oldInputs) {
        oldInputs = -1;
        if (!Related(owner)) return true;
        oldInputs = owner.inputConnections.Count;
        bool ready = Ready(owner);
        if (ready) {
            foreach (string id in owner.inputConnections.Keys) if (Switching(owner.coShip, id)) { ready = false; break; }
            if (ready) foreach (string id in owner.outputConnections.Keys) if (Switching(owner.coShip, id)) { ready = false; break; }
        }
        if (ready) return true;
        owner.fTimeToNextAudit = double.PositiveInfinity;
        Wait(owner, State(owner), audit: true);
        oldInputs = -1;
        return false;
    }

    internal static void AfterAudit(Electrical owner, int oldInputs) {
        if (oldInputs >= 0 && Ready(owner)) RetireGhostInputs(owner);
        if (oldInputs >= 0 && owner.inputConnections.Count != oldInputs) Schedule(owner);
    }

    static bool Pending(List<ElectricalSignal> queue, string id) {
        foreach (var signal in queue) if (signal.originID == id) return true;
        return false;
    }

    static void RetireGhostInputs(Electrical owner) {
        var retired = State(owner).RetiredInputs;
        try {
            foreach (string id in owner.inputConnections.Keys) {
                if (Switching(owner.coShip, id)) continue;
                var source = owner.coShip.GetCOByID(id);
                var electrical = source == null ? null : source.Electrical;
                if (electrical == null || !Ready(electrical) || electrical.outputConnections.ContainsKey(owner.coSelf.strID)) continue;
                // A real disconnect/handshake must retain its native delivery and
                // receiver delay. Only settled nonreciprocal inputs are ghosts.
                if (Pending(Outgoing(electrical), owner.coSelf.strID) || Pending(Incoming(owner), id)) continue;
                retired.Add(id);
            }
            foreach (string id in retired) owner.inputConnections.Remove(id);
            if (retired.Count != 0) owner.SetGPM();
        } finally { retired.Clear(); }
    }

    internal static DispatchScope BeginDispatch(Electrical owner) {
        var scope = free;
        if (scope == null) scope = new DispatchScope();
        else free = scope.NextFree;
        scope.NextFree = null;
        scope.Previous = current;
        scope.Owner = owner;
        scope.State = Related(owner) ? State(owner) : null;
        scope.Index = 0;
        current = scope;
        return scope;
    }

    internal static void EndDispatch(DispatchScope scope, bool completed) {
        if (scope == null) return;
        try {
            if (completed && scope.State != null && scope.Retained.Count != 0 && Incoming(scope.Owner).Count == 0) NeedsCheck(scope.Owner) = false;
        } finally {
            current = scope.Previous;
            scope.Owner = null;
            scope.State = null;
            scope.Previous = null;
            scope.Retained.Clear();
            scope.NextFree = free;
            free = scope;
        }
    }

    internal static bool Dispatch(Electrical owner, string destination, ElectricalSignal signal) {
        var scope = current;
        if (scope == null || !ReferenceEquals(scope.Owner, owner) || scope.State == null) return NativeSend(owner, destination, signal);
        var queue = Outgoing(owner);
        int index = scope.Index++;
        bool mapped = owner.outputConnections.ContainsKey(destination);
        if (!mapped && signal.signalType != SignalType.Disconnect) return false;
        bool temporary = !Ready(owner) || Switching(owner.coShip, destination) || scope.State.Blocked.Contains(destination);
        CondOwner target = temporary ? null : owner.coShip.GetCOByID(destination);
        if (target != null && target.Electrical != null && target.Electrical.isInitialising) temporary = true;
        if (temporary) {
            // Copy the original native queue record, preserving its epoch/callback.
            if (index < 0 || index >= queue.Count) throw new InvalidOperationException("Electrical dispatch queue order changed.");
            scope.Retained.Add(queue[index]);
            Wait(owner, scope.State, destination);
            return false;
        }
        if (signal.signalType == SignalType.Disconnect && !mapped) {
            var receiver = target == null ? null : target.Electrical;
            if (receiver != null && receiver.inputConnections.ContainsKey(owner.coSelf.strID)) receiver.AddSignal(signal);
            return receiver != null;
        }
        // Settled missing/non-electrical endpoints follow the native removal path;
        // they are not retried forever merely because an old saved ID exists.
        return NativeSend(owner, destination, signal);
    }

    internal static void ClearDelivered(List<ElectricalSignal> queue, Electrical owner) {
        var scope = current;
        queue.Clear();
        if (scope != null && ReferenceEquals(scope.Owner, owner) && scope.State != null) {
            queue.AddRange(scope.Retained);
            scope.State.Dirty = true;
        }
    }

    internal static void Persist(Electrical owner, bool initializing = false) {
        if (owner == null || (!Devices.TryGetValue(owner, out var state) && !Related(owner))) return;
        state = state ?? State(owner);
        if (initializing) state.Dirty = true;
        var map = Properties(owner);
        if (map == null || !state.Dirty) return;
        var queue = Outgoing(owner);
        if (queue == null) return;
        if (queue.Count == 0) map["sendQueue"] = "";
        else {
            var text = state.Text;
            text.Clear();
            foreach (var signal in queue) {
                if (text.Length != 0) text.Append(',');
                text.Append(signal.ToString());
            }
            map["sendQueue"] = text.ToString();
            text.Clear();
        }
        state.Dirty = false;
    }

    internal static void Queued(Electrical owner) {
        // QueueSignal already serialized the current queue in the native format.
        if (Devices.TryGetValue(owner, out var state)) state.Dirty = false;
    }

    internal static void Normalize(Electrical owner, string destination, ref SignalType type) {
        if (type == SignalType.On && IsBox(owner) && (!owner.status || !owner.bOverride ||
            !owner.outputConnections.TryGetValue(destination, out var branch) || !branch.switchStatus)) type = SignalType.Off;
    }

    internal static void Initialized(Electrical owner) {
        if (Related(owner)) {
            State(owner).Dirty = true;
            // Native initialization clears serialized incoming AND outgoing
            // queues after reading them. Publish their still-pending snapshots.
            SaveRemovedOutputs(owner, State(owner));
            if (IsBox(owner)) {
                if (owner.status) owner.coSelf.ZeroCondAmount("IsSignalOff");
                if (owner.bOverride) owner.coSelf.ZeroCondAmount("IsOverrideOff");
            }
            if (Outgoing(owner).Count != 0 || Incoming(owner).Count != 0) Schedule(owner);
        }
        Wake(owner.coSelf?.ship);
    }

    internal static void Initializing(Electrical owner) {
        if (!Related(owner)) return;
        // Serialized queues are the restoration snapshot. Reinitializing this
        // component must not append the same pending records a second time.
        Outgoing(owner).Clear();
        Incoming(owner).Clear();
    }

    internal static ModeScope BeginModeSwitch(CondOwner old, CondOwner replacement) {
        if (old == null || replacement == null || old.ship == null || old.strID == null || old.Electrical == null || replacement.Electrical == null) return default;
        if (Devices.TryGetValue(old.Electrical, out var previous) && previous.RemovedOutputs?.Count > 0) {
            var next = State(replacement.Electrical);
            if (next.RemovedOutputs == null) next.RemovedOutputs = new List<string>();
            foreach (string id in previous.RemovedOutputs) if (!next.RemovedOutputs.Contains(id)) next.RemovedOutputs.Add(id);
        }
        return TrackSwitch(old.ship, old.strID);
    }

    internal static ModeScope TrackSwitch(Ship ship, string id) {
        var state = Ships.GetValue(ship, value => new ShipState());
        state.Switching.TryGetValue(id, out int depth);
        state.Switching[id] = depth + 1;
        return new ModeScope { Ship = ship, Id = id };
    }

    internal static void EndModeSwitch(ModeScope scope) {
        if (scope.Ship == null || scope.Id == null || !Ships.TryGetValue(scope.Ship, out var state)) return;
        if (state.Switching.TryGetValue(scope.Id, out int depth) && depth > 1) state.Switching[scope.Id] = depth - 1;
        else state.Switching.Remove(scope.Id);
        Wake(scope.Ship);
    }

    internal static void Added(Ship ship, CondOwner co) {
        var electrical = co == null ? null : co.Electrical;
        if (electrical != null && Related(electrical) && ReferenceEquals(co.ship, ship)) {
            bool moved = !ReferenceEquals(electrical.coShip, ship);
            electrical.coShip = ship;
            if (moved && Devices.TryGetValue(electrical, out var state)) {
                state.Registered = false;
                if (state.Waiting) Wait(electrical, state);
            }
        }
        if (Ships.TryGetValue(ship, out _)) Wake(ship);
    }

    internal static bool Cleanup(Electrical owner, ref bool disconnect) {
        if (!Related(owner)) return false;
        Persist(owner, initializing: true);
        if (disconnect && !Authoritative(owner.coShip)) disconnect = false;
        return disconnect;
    }

    static bool CanSerialize(Electrical owner) => owner != null && !owner.isInitialising && owner.coSelf != null &&
        !owner.coSelf.bDestroyed && ReferenceEquals(owner.coSelf.Electrical, owner) &&
        Properties(owner) != null && owner.coSelf.mapGUIPropMaps != null;

    static void SaveRemovedOutputs(Electrical owner, DeviceState state) {
        // InitGPMValues may have just restored an older connection snapshot.
        // Reapply native cleanup before publishing, preserving every other ID.
        if (state.RemovedOutputs != null)
            foreach (string id in state.RemovedOutputs) owner.outputConnections.Remove(id);
        owner.SetGPM();
        state.RemovedOutputs?.Clear(); // Keep pending IDs if serialization throws.
    }

    internal static void Cleaned(Electrical owner, bool disconnected) {
        if (disconnected && Authoritative(owner.coShip)) {
            // Native cleanup directly removes reciprocal outputs. Publish those
            // mutations without changing its disconnect/transfer policy.
            foreach (string id in owner.inputConnections.Keys) {
                var source = owner.coShip.GetCOByID(id);
                var electrical = source == null ? null : source.Electrical;
                if (electrical == null) continue;
                var state = State(electrical);
                if (CanSerialize(electrical)) SaveRemovedOutputs(electrical, state);
                else {
                    if (state.RemovedOutputs == null) state.RemovedOutputs = new List<string>();
                    if (!state.RemovedOutputs.Contains(owner.coSelf.strID)) state.RemovedOutputs.Add(owner.coSelf.strID);
                    Wait(electrical, state, fallbackShip: source.ship);
                }
            }
        }
        // A receiver can be removed before completing initialization. Wake its
        // waiting senders so their next native tick can retire the missing ID.
        Wake(owner.coSelf?.ship ?? owner.coShip);
    }

    internal static bool ValidLabel(Electrical owner, string label) => !IsBox(owner) || label == null || (label.IndexOf('#') < 0 && label.IndexOf(',') < 0);
}
