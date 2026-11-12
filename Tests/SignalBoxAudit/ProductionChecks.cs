using System;
using System.Linq;
using Ostrabus.MiscBugFixes.SignalBoxes;

static partial class Program {
    static void CheckProductionLifecycle() {
        var source = Box();
        var target = Device();
        source.SetUpConnection(target.coSelf); Tick(source); Tick(target);
        Check(target.inputConnections.ContainsKey(source.coSelf.strID), "Production handshake failed.");
        target.CleanUp(true);
        Check(!source.outputConnections.ContainsKey(target.coSelf.strID), "Permanent removal kept reciprocal output.");
        Check(!Gpm(source)["outputConnections"].Contains(target.coSelf.strID), "Permanent removal was not saved on upstream box.");

        var upstream = Device(); var removedBox = Box();
        upstream.SetUpConnection(removedBox.coSelf); Tick(upstream); Tick(removedBox);
        registry.Remove(removedBox.coSelf.strID);
        upstream.QueueSignal(removedBox.coSelf.strID, Ostranauts.Electrical.SignalType.Off); Tick(upstream);
        Check(Queue(upstream, "sendQueue").Count == 0 && Gpm(upstream)["sendQueue"] == "",
            "Removing the last identifiable box left its upstream sender's delivered queue in saved state.");

        var restored = Box(); var destination = Device();
        restored.SetUpConnection(destination.coSelf);
        conditions[restored.coSelf.strID].Remove("IsPowered"); Tick(restored);
        var original = Queue(restored, "sendQueue").Select(signal => signal.ToString()).ToArray();
        Init(restored); Init(restored);
        Check(Queue(restored, "sendQueue").Select(signal => signal.ToString()).SequenceEqual(original), "Repeated initialization duplicated pending commands.");
        Check(Gpm(restored)["sendQueue"] != "", "Repeated initialization lost the durable queue snapshot.");

        var incomingSender = Box(); var incomingReceiver = Device();
        incomingSender.SetUpConnection(incomingReceiver.coSelf); Tick(incomingSender);
        int pendingIncoming = Queue(incomingReceiver, "signalQueue").Count;
        Check(pendingIncoming != 0, "Incoming restoration fixture has no pending messages.");
        Init(incomingReceiver); Init(incomingReceiver);
        Check(Queue(incomingReceiver, "signalQueue").Count == pendingIncoming && Gpm(incomingReceiver)["signalQueue"] != "",
            "Reinitialization lost or duplicated incoming messages before their receiver delay elapsed.");

        var ghostSource = Box(); var ghostReceiver = Box();
        ghostSource.SetUpConnection(ghostReceiver.coSelf); Tick(ghostSource); Tick(ghostReceiver);
        ghostSource.outputConnections.Remove(ghostReceiver.coSelf.strID); ghostSource.SetGPM();
        ghostReceiver.AuditConnections();
        Check(!ghostReceiver.inputConnections.ContainsKey(ghostSource.coSelf.strID), "Settled legacy ghost input survived reciprocal audit.");
        Check(!Gpm(ghostReceiver)["inputConnections"].Contains(ghostSource.coSelf.strID), "Ghost retirement was not saved.");

        var delayedSource = Box(); var delayedReceiver = Box();
        delayedSource.SetUpConnection(delayedReceiver.coSelf); Tick(delayedSource); Tick(delayedReceiver);
        delayedReceiver.SetSlider(.1f);
        delayedSource.RemoveConnection(delayedReceiver.coSelf.strID);
        delayedReceiver.AuditConnections();
        Check(delayedReceiver.inputConnections.ContainsKey(delayedSource.coSelf.strID), "Audit preempted sender's queued disconnect.");
        Tick(delayedSource); delayedReceiver.AuditConnections();
        Check(delayedReceiver.inputConnections.ContainsKey(delayedSource.coSelf.strID), "Audit preempted receiver's delayed disconnect.");
        StarSystem.fEpoch += 10; Tick(delayedReceiver);
        Check(!delayedReceiver.inputConnections.ContainsKey(delayedSource.coSelf.strID), "Delayed disconnect never completed.");

        var waiting = Box(); var receiver = Device();
        waiting.SetUpConnection(receiver.coSelf); Tick(waiting); Tick(receiver);
        CompletedElectricalLoad(waiting.coShip) = false;
        waiting.RemoveConnection(receiver.coSelf.strID); Tick(waiting);
        Check(Queue(waiting, "sendQueue").Count == 1, "Pending terminal message lost during reconstruction.");
        CompletedElectricalLoad(waiting.coShip) = true;
        SignalBoxRepair.Wake(waiting.coShip);
        StarSystem.fEpoch += 1;
        waiting.UpdateManual(); receiver.UpdateManual();
        StarSystem.fEpoch += 1;
        receiver.UpdateManual();
        Check(!receiver.inputConnections.ContainsKey(waiting.coSelf.strID), "Lifecycle wake failed to resume native delayed delivery.");

        var closing = Box(); var peer = Device();
        closing.SetUpConnection(peer.coSelf); Tick(closing); Tick(peer);
        closing.coShip.bDestroyed = true;
        closing.CleanUp(true);
        Check(peer.inputConnections.ContainsKey(closing.coSelf.strID), "Whole-ship teardown issued a permanent disconnect.");
        var mixed = Box(); var slow = Device(); var fast = Device();
        Set(slow.coSelf, "_objShip", mixed.coShip); slow.coShip = mixed.coShip;
        slow.isInitialising = true;
        mixed.SetUpConnection(slow.coSelf); mixed.SetUpConnection(fast.coSelf);
        int callbacks = 0;
        var outgoing = Queue(mixed, "sendQueue");
        for (int i = 0; i < outgoing.Count; i++) {
            var signal = outgoing[i];
            if (signal.originID == slow.coSelf.strID) { signal.callback = true; outgoing[i] = signal; callbacks++; }
        }
        var held = outgoing.Where(signal => signal.originID == slow.coSelf.strID).Select(signal => signal.ToString()).ToArray();
        Tick(mixed); Tick(fast);
        Check(fast.inputConnections.ContainsKey(mixed.coSelf.strID), "One unavailable endpoint blocked delivery to a ready endpoint.");
        Check(Queue(mixed, "sendQueue").Select(signal => signal.ToString()).SequenceEqual(held), "Retained native records lost epoch, callback, order or identity.");
        mixed.ToggleOverride(); Tick(fast);
        Check(fast.inputConnections[mixed.coSelf.strID].signalType == Ostranauts.Electrical.SignalType.Off, "Ready output stopped responding while another receiver was unavailable.");
        slow.isInitialising = false; Init(slow); Tick(mixed);
        Check(Queue(slow, "signalQueue").Count(signal => signal.callback) == callbacks, "Delayed dispatch lost native callbacks.");
        Tick(slow);
        Check(slow.inputConnections[mixed.coSelf.strID].signalType == Ostranauts.Electrical.SignalType.Off, "Delayed receiver ignored the source's later master-off command.");
        CheckProductionBoundaries();
    }
}
