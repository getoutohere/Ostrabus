using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Ostrabus.MiscBugFixes.SignalBoxes;
using Ostranauts.Electrical;
using UnityEngine;

static partial class Program {
    static Action modeBody;
    static string restoredLabel;
    static bool ModeBodyFixture() { modeBody(); return false; }
    static bool ShipReadyFixture(Ship __instance) { CompletedElectricalLoad(__instance) = true; return false; }
    static bool LabelFixture(string input) { restoredLabel = input; return false; }

    static void CheckProductionBoundaries() {
        var fixture = new Harmony("offline.signalbox.production.boundaries");
        try {
            fixture.Patch(AccessTools.Method(typeof(CondOwner), "ValidateParentRecursive"), prefix: new HarmonyMethod(typeof(Program), nameof(Skip)));
            fixture.Patch(AccessTools.Method(typeof(CondOwner), "ModeSwitchInternal"), prefix: new HarmonyMethod(typeof(Program), nameof(ModeBodyFixture)));
            var snapshots = AccessTools.TypeByName("Ostranauts.Pathing.NavigationSnapshot");
            fixture.Patch(AccessTools.Method(snapshots, "BeginModeSwitch"), prefix: new HarmonyMethod(typeof(Program), nameof(Skip)));
            fixture.Patch(AccessTools.Method(snapshots, "CompleteModeSwitch"), prefix: new HarmonyMethod(typeof(Program), nameof(Skip)));
            fixture.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.InitShip)), prefix: new HarmonyMethod(typeof(Program), nameof(ShipReadyFixture)));
            fixture.Patch(AccessTools.Method(typeof(TMPro.TMP_InputField), nameof(TMPro.TMP_InputField.SetTextWithoutNotify)), prefix: new HarmonyMethod(typeof(Program), nameof(LabelFixture)));

            foreach (bool fail in new[] { false, true }) {
                var source = Box(); var old = Box(); var next = Device();
                Set(old.coSelf, "_objShip", source.coShip); old.coShip = source.coShip;
                Set(next.coSelf, "_objShip", source.coShip); next.coShip = source.coShip;
                source.SetUpConnection(old.coSelf); Tick(source); Tick(old);
                string id = old.coSelf.strID;
                modeBody = () => {
                    old.CleanUp(false);
                    registry.Remove(id);
                    try {
                        source.AuditConnections();
                        source.QueueSignal(id, SignalType.Off); Tick(source);
                        Check(source.outputConnections.ContainsKey(id) && Queue(source, "sendQueue").Count != 0, "Actual mode-switch entry hook failed to protect the gap.");
                        if (fail) throw new InvalidOperationException("fixture conversion failure");
                        registry.Remove(next.coSelf.strID);
                        Set(next.coSelf, "_strID", id);
                        Set(next, "mapGPM", Gpm(old)); next.coSelf.mapGUIPropMaps["Electrical"] = Gpm(old);
                        registry[id] = next.coSelf;
                        Init(next);
                    } finally { if (fail) registry[id] = old.coSelf; }
                };
                try { old.coSelf.ModeSwitch(next.coSelf, default); Check(!fail, "Expected conversion exception was swallowed."); }
                catch (InvalidOperationException error) when (error.Message == "fixture conversion failure") { Check(fail, "Unexpected conversion fixture failure."); }
                Check(!SignalBoxRepair.EndpointTemporary(source.coShip, id), "Mode-switch finalizer leaked a scope after completion/exception.");
                Check(Get<bool>(source, "bNeedsCheck"), "Mode-switch completion failed to wake the native sender.");
                Tick(source); Tick(fail ? old : next);
                Check(Queue(source, "sendQueue").Count == 0, "Woken conversion queue did not settle.");
            }
            modeBody = null;

            // Live November 11 regression: the receiver is ready to convert,
            // while an upstream component exists before its GPM binding finishes.
            // Native cleanup must remain safe before binding, and initialization
            // must not resurrect the output from its older saved snapshot.
            foreach (int incomplete in new[] { 0, 1, 2 }) {
                var unready = Box(); var converting = Box();
                Set(unready.coSelf, "_objShip", converting.coShip);
                unready.coShip = converting.coShip;
                unready.SetUpConnection(converting.coSelf); Tick(unready); Tick(converting);
                var unrelated = Device();
                unready.SetUpConnection(unrelated.coSelf); Tick(unready); Tick(unrelated);
                var alsoRemoved = Box();
                unready.SetUpConnection(alsoRemoved.coSelf); Tick(unready); Tick(alsoRemoved);
                var boundCo = unready.coSelf;
                var ship = unready.coShip;
                var saved = new Dictionary<string, string>(Gpm(unready));
                string removedId = converting.coSelf.strID;
                boundCo.mapGUIPropMaps["Electrical"] = saved;
                unready.isInitialising = true;
                if (incomplete == 1) { unready.coSelf = null; unready.coShip = null; }
                else if (incomplete == 0) Set(unready, "mapGPM", null);
                bool threw = false;
                try { converting.CleanUp(true); }
                catch (NullReferenceException) { threw = true; }
                Check(!threw, "Cleanup still serializes an uninitialized upstream component.");
                alsoRemoved.CleanUp(true);
                alsoRemoved.CleanUp(true);
                Check(!unready.outputConnections.ContainsKey(removedId), "Native reciprocal removal was skipped.");
                Check(saved["outputConnections"].Contains(removedId), "Incomplete component was prematurely serialized.");
                SignalBoxRepair.Wake(ship); // Must retain a still-unbound waiter.
                unready.coSelf = boundCo;
                unready.coShip = ship;
                Set(unready, "mapGPM", saved);
                unready.isInitialising = false; // Native CheckforGPM sets this before InitGPMValues.
                if (incomplete == 2) CompletedElectricalLoad(ship) = false;
                Init(unready);
                Check(!unready.outputConnections.ContainsKey(removedId) && !Gpm(unready)["outputConnections"].Contains(removedId),
                    "Native initialization resurrected the removed connection.");
                Check(unready.outputConnections.ContainsKey(unrelated.coSelf.strID) && Gpm(unready)["outputConnections"].Contains(unrelated.coSelf.strID),
                    "Deferred cleanup removed an unrelated output.");
                Check(ReferenceEquals(boundCo.mapGUIPropMaps["Electrical"], saved), "Cleanup replaced the native property map.");
                var state = (SignalBoxRepair.DeviceState)AccessTools.Method(typeof(SignalBoxRepair), "State").Invoke(null, new object[] { unready });
                Check(state.RemovedOutputs.Count == 0, "Successful publication retained removed endpoint IDs.");
                Check(!unready.outputConnections.ContainsKey(alsoRemoved.coSelf.strID) && !Gpm(unready)["outputConnections"].Contains(alsoRemoved.coSelf.strID),
                    "Multiple/repeated cleanup lost a deferred removal.");
                CompletedElectricalLoad(ship) = true;
                SignalBoxRepair.Wake(ship);
                Check(!state.Registered && !state.Waiting, "Initialized component retained a stale lifecycle waiter.");
                Init(unready);
                Check(!unready.outputConnections.ContainsKey(removedId), "Repeated initialization resurrected the removed output.");
                unready.SetUpConnection(converting.coSelf); Tick(unready); Tick(converting);
                Check(unready.outputConnections.ContainsKey(removedId) && Gpm(unready)["outputConnections"].Contains(removedId),
                    "Completed cleanup prevented a later explicit reconnection.");
                Check(!unready.outputConnections.ContainsKey(alsoRemoved.coSelf.strID), "Reconnection restored a separately removed output.");
            }

            CheckDeferredCleanupConversion();

            var awaiting = Box(); var endpoint = Device();
            awaiting.SetUpConnection(endpoint.coSelf); Tick(awaiting); Tick(endpoint);
            CompletedElectricalLoad(awaiting.coShip) = false;
            awaiting.RemoveConnection(endpoint.coSelf.strID); Tick(awaiting);
            Check(!Get<bool>(awaiting, "bNeedsCheck"), "Sender did not park while reconstruction was incomplete.");
            awaiting.coShip.InitShip(false, Ship.Loaded.Full);
            Check(Get<bool>(awaiting, "bNeedsCheck"), "Actual Ship.InitShip postfix failed to wake parked delivery.");
            Tick(awaiting); Tick(endpoint);
            Check(!endpoint.inputConnections.ContainsKey(awaiting.coSelf.strID), "Reconstruction wake lost disconnect delivery.");

            var retiring = Box(); var initializing = Device();
            Set(initializing.coSelf, "_objShip", retiring.coShip); initializing.coShip = retiring.coShip;
            retiring.SetUpConnection(initializing.coSelf); Tick(retiring); Tick(initializing);
            initializing.isInitialising = true;
            retiring.RemoveConnection(initializing.coSelf.strID); Tick(retiring);
            Check(!Get<bool>(retiring, "bNeedsCheck"), "Initializing receiver did not park sender.");
            initializing.CleanUp(true); registry.Remove(initializing.coSelf.strID);
            Check(Get<bool>(retiring, "bNeedsCheck"), "Removing an initializing receiver did not wake pending retirement.");
            Tick(retiring);
            Check(Queue(retiring, "sendQueue").Count == 0, "Removed initializing receiver retained a terminal command forever.");

            var labels = Box(); var output = Device(); labels.SetUpConnection(output.coSelf);
            labels.RenameConnection(output.coSelf.strID, "Fuel backup");
            var ui = Empty<GUIOutput>(); ui.elec = labels; ui.destinationID = output.coSelf.strID; ui.label = Empty<TMPro.TMP_InputField>();
            Set(ui, "nickName", "Fuel backup");
            ui.ChangeName("Fuel, bad#label");
            Check(restoredLabel == "Fuel backup" && Get<string>(ui, "nickName") == "Fuel backup", "Invalid label was not visibly restored without changing saved nickname.");

            var weak = UnrootedWaitingBox();
            for (int i = 0; i < 3 && weak.IsAlive; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
            Check(!weak.IsAlive, "Weak signal-box/lifecycle state retained an otherwise unreachable device.");
            var unboundWeak = UnrootedCleanupSource();
            for (int i = 0; i < 3 && unboundWeak.IsAlive; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
            Check(!unboundWeak.IsAlive, "Deferred cleanup retained an unbound source through a live ship's waiter list.");
            Check(AccessTools.Field(typeof(SignalBoxRepair), "current").GetValue(null) == null, "Dispatch context leaked outside native resolution.");
            Console.WriteLine("Production lifecycle: mode-switch success/exception, reconstruction, endpoint retirement, GUI label and weak-lifetime checks passed.");
        } finally { modeBody = null; restoredLabel = null; fixture.UnpatchSelf(); }
    }

    static void CheckDeferredCleanupConversion() {
        var old = Box(); var removed = Device(); var next = Device();
        old.SetUpConnection(removed.coSelf); Tick(old); Tick(removed);
        string id = old.coSelf.strID;
        var ship = old.coShip;
        var saved = new Dictionary<string, string>(Gpm(old));
        old.coSelf.mapGUIPropMaps["Electrical"] = saved;
        old.isInitialising = true;
        Set(old, "mapGPM", null);
        removed.CleanUp(true);
        Set(next.coSelf, "_objShip", ship); next.coShip = ship;
        modeBody = () => {
            old.CleanUp(false);
            registry.Remove(id);
            registry.Remove(next.coSelf.strID);
            Set(next.coSelf, "_strID", id);
            Set(next, "mapGPM", saved);
            next.coSelf.mapGUIPropMaps["Electrical"] = saved;
            registry[id] = next.coSelf;
            Init(next);
        };
        try { old.coSelf.ModeSwitch(next.coSelf, default); }
        finally { modeBody = null; }
        Check(!next.outputConnections.ContainsKey(removed.coSelf.strID) && !Gpm(next)["outputConnections"].Contains(removed.coSelf.strID),
            "Preserving Electrical conversion lost deferred reciprocal cleanup.");
        Check(!SignalBoxRepair.EndpointTemporary(ship, id), "Deferred-cleanup conversion leaked its scope.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference UnrootedCleanupSource() {
        var source = Box(); var removed = Device();
        source.SetUpConnection(removed.coSelf); Tick(source); Tick(removed);
        string id = source.coSelf.strID;
        var ship = source.coShip;
        source.coSelf = null; source.coShip = null; source.isInitialising = true;
        removed.CleanUp(true);
        var weak = new WeakReference(source);
        registry.Remove(id);
        candidateBoxes.Remove(source);
        GC.KeepAlive(ship); // Its registered ship may outlive the discarded component.
        return weak;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference UnrootedWaitingBox() {
        var box = Box(); var other = Device();
        CompletedElectricalLoad(box.coShip) = false;
        box.SetUpConnection(other.coSelf); Tick(box);
        var weak = new WeakReference(box);
        registry.Remove(box.coSelf.strID);
        registry.Remove(other.coSelf.strID);
        CrewSim.system.dictShips.Remove(box.coShip.strRegID);
        CrewSim.system.dictShips.Remove(other.coShip.strRegID);
        candidateBoxes.Remove(box);
        return weak;
    }
}
