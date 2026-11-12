using System;
using System.Linq;
using HarmonyLib;
using Ostranauts.Electrical;
using Ostrabus.MiscBugFixes.SignalBoxes;

static partial class Program {
    static void CheckCandidateFixes(bool production = false) {
        int before = checks;
        var oldSystem = CrewSim.system;
        var oldMap = DataHandler.mapCOs;
        productionTesting = production;
        if (production) DataHandler.mapCOs = registry;
        CrewSim.system = Empty<StarSystem>(); CrewSim.system.dictShips = new System.Collections.Generic.Dictionary<string, Ship>();
        var patch = new Harmony("offline.signalbox.candidates.only");
        void Hook(string method, string prefix = null, string postfix = null) => patch.Patch(AccessTools.Method(typeof(Electrical), method),
            prefix: prefix == null ? null : new HarmonyMethod(typeof(Program), prefix),
            postfix: postfix == null ? null : new HarmonyMethod(typeof(Program), postfix));
        try {
            if (production) {
                patch.Patch(AccessTools.Method(typeof(UnityEngine.Debug), "Log", new[] { typeof(object) }), prefix: new HarmonyMethod(typeof(Program), nameof(CaptureDiagnostic)));
                patch.CreateClassProcessor(typeof(Ostrabus.MiscBugFixes.ElectricalAuditLogging)).Patch();
                patch.CreateClassProcessor(typeof(Ostrabus.MiscBugFixes.ElectricalAuditConsoleFilter)).Patch();
                foreach (Type type in new[] {
                    typeof(SignalBoxGatePatch), typeof(SignalBoxAuditPatch), typeof(SignalBoxQueuePatch),
                    typeof(SignalBoxDispatchPatch), typeof(SignalBoxPersistencePatch), typeof(SignalBoxLabelPatch), typeof(SignalBoxLabelInputPatch),
                    typeof(SignalBoxShipReadyPatch), typeof(SignalBoxOwnerPatch), typeof(SignalBoxModeSwitchPatch), typeof(SignalBoxCleanupPatch)
                }) patch.CreateClassProcessor(type).Patch();
            } else {
                Hook(nameof(Electrical.SetGateMode), nameof(CandidateGateBefore), nameof(CandidateGateAfter));
                patch.Patch(AccessTools.PropertySetter(typeof(Electrical), nameof(Electrical.GateMode)),
                    prefix: new HarmonyMethod(typeof(Program), nameof(CandidateGateBefore)), postfix: new HarmonyMethod(typeof(Program), nameof(CandidateGateAfter)));
                Hook(nameof(Electrical.AuditConnections), nameof(CandidateAuditBefore), nameof(CandidateAuditAfter));
                patch.Patch(AccessTools.Method(typeof(Electrical), nameof(Electrical.AuditConnections)), prefix: new HarmonyMethod(typeof(Program), nameof(CandidateAuditAllowed)));
                Hook(nameof(Electrical.QueueSignal), nameof(CandidateQueue));
                Hook(nameof(Electrical.SendSignal), nameof(CandidateSend));
                Hook(nameof(Electrical.ResolveSignalQueue), postfix: nameof(CandidateResolved));
                Hook(nameof(Electrical.SetGPM), postfix: nameof(CandidatePersist));
                Hook("InitGPMValues", postfix: nameof(CandidateInitialised));
                Hook(nameof(Electrical.RenameConnection), nameof(CandidateRename));
            }

            // Full effective-output matrix; native gate math and delivery still run.
            foreach (GateMode gate in Enum.GetValues(typeof(GateMode))) for (int mask = 0; mask < 4; mask++)
                foreach (bool master in new[] { false, true }) foreach (bool branch in new[] { false, true }) foreach (bool powered in new[] { false, true }) {
                var source = Box(); var target = Device();
                source.gateMode = gate; Input(source, "upstream-a", (mask & 1) != 0); Input(source, "upstream-b", (mask & 2) != 0); Tick(source);
                bool any = mask != 0, all = mask == 3;
                bool gateValue = gate == GateMode.OR ? any : gate == GateMode.AND ? all : gate == GateMode.NOR ? !any : !all;
                if (!master) source.ToggleOverride();
                if (!powered) conditions[source.coSelf.strID].Remove("IsPowered");
                source.SetUpConnection(target.coSelf);
                if (!branch) source.ToggleConnection(target.coSelf.strID);
                Tick(source); Tick(target);
                if (!powered) {
                    Check(!target.inputConnections.ContainsKey(source.coSelf.strID), "Unpowered source cannot deliver a new connection");
                    Check(Queue(source, "sendQueue").Count > 0 && Gpm(source)["sendQueue"].Length > 0, "Unpowered outgoing commands remain durable");
                    conditions[source.coSelf.strID].Add("IsPowered"); Tick(source); Tick(target);
                }
                Check(target.inputConnections[source.coSelf.strID].signalType == (gateValue && master && branch ? SignalType.On : SignalType.Off), "Effective initial output respects gate/master/branch");
                Check(source.status == gateValue, "Master/branch policy does not overwrite gate truth value");
                Check(Queue(source, "sendQueue").Count == 0 && Gpm(source)["sendQueue"] == "", "Delivered queue is cleared in runtime and saved GPM");
                if (!branch) { source.ToggleConnection(target.coSelf.strID); Tick(source); Tick(target); }
                Check(target.inputConnections[source.coSelf.strID].signalType == (gateValue && master ? SignalType.On : SignalType.Off), "Reenabled output respects current effective source state");
            }
            foreach (bool property in new[] { false, true }) {
                var box = Box(); Input(box, "high", true); Input(box, "low", false); Tick(box);
                if (property) box.GateMode = GateMode.AND; else box.SetGateMode((int)GateMode.AND);
                Check(Get<bool>(box, "bNeedsCheck") && box.status, "Gate changes schedule rather than synchronously force output");
                box.UpdateManual(); Check(box.status, "Gate change retains native processing delay");
                StarSystem.fEpoch += .2; box.UpdateManual(); Check(!box.status, "Gate reevaluates on normal scheduled update");
            }
            var audited = Box(); var high = Device(); var low = Device();
            audited.gateMode = GateMode.AND; Input(audited, high.coSelf.strID, true); Input(audited, low.coSelf.strID, false); Tick(audited);
            registry.Remove(low.coSelf.strID); audited.AuditConnections();
            Check(!audited.status && Get<bool>(audited, "bNeedsCheck"), "Changed audited input set schedules reevaluation");
            StarSystem.fEpoch += .2; audited.UpdateManual(); Check(audited.status, "Audited gate result refreshes through native update");

            foreach (bool powered in new[] { false, true }) foreach (bool present in new[] { false, true }) {
                var source = Box(); var target = Device(); source.SetUpConnection(target.coSelf); Tick(source); Tick(target);
                if (!powered) conditions[source.coSelf.strID].Remove("IsPowered");
                if (!present) { registry.Remove(target.coSelf.strID); CompletedElectricalLoad(source.coShip) = false; }
                source.RemoveConnection(target.coSelf.strID); Tick(source); Tick(target);
                if (!powered || !present) {
                    Check(target.inputConnections.ContainsKey(source.coSelf.strID), "Unavailable delivery does not synchronously mutate receiver");
                    Check(Queue(source, "sendQueue").Any(s => s.signalType == SignalType.Disconnect), "Deferred disconnect survives in sender queue");
                    // Fresh native initialization consumes the persisted pending queue.
                    string pending = Gpm(source)["sendQueue"];
                    Queue(source, "sendQueue").Clear(); Init(source);
                    Check(Gpm(source)["sendQueue"] != "" && Queue(source, "sendQueue").Count > 0, "Pending disconnect survives reinitialization");
                    registry[target.coSelf.strID] = target.coSelf;
                    CompletedElectricalLoad(source.coShip) = true;
                    WakeCandidate(source); // Reconstruction-completion boundary.
                    conditions[source.coSelf.strID].Add("IsPowered"); Tick(source); Tick(target);
                }
                Check(!target.inputConnections.ContainsKey(source.coSelf.strID), "Terminal disconnect removes ghost input");
                Check(Gpm(source)["sendQueue"] == "", "Delivered disconnect will not replay after reload");
                source.QueueSignal(target.coSelf.strID, SignalType.On); source.QueueSignal(target.coSelf.strID, SignalType.Connect); Tick(source); Tick(target);
                Check(!target.inputConnections.ContainsKey(source.coSelf.strID), "Nonterminal signals cannot recreate a removed wire");
            }
            var delayedSource = Box(); var delayedTarget = Device(); delayedTarget.sliderPercent = .05f;
            delayedSource.SetUpConnection(delayedTarget.coSelf); Tick(delayedSource); Tick(delayedTarget);
            Check(delayedTarget.inputConnections.Count == 0, "Receiver's configured delay remains effective");
            StarSystem.fEpoch += 4; delayedTarget.ResolveSignalQueue(); Check(delayedTarget.inputConnections.Count == 1, "Delayed handshake completes natively");
            delayedSource.RemoveConnection(delayedTarget.coSelf.strID); Tick(delayedSource); Tick(delayedTarget);
            Check(delayedTarget.inputConnections.Count == 1, "Disconnect respects receiver delay");
            StarSystem.fEpoch += 4; delayedTarget.ResolveSignalQueue(); Check(delayedTarget.inputConnections.Count == 0, "Delayed disconnect completes");

            var label = Box(); var endpoint = Device(); label.SetUpConnection(endpoint.coSelf);
            label.RenameConnection(endpoint.coSelf.strID, "Fuel backup — α");
            foreach (string invalid in new[] { "Fuel#backup", "Fuel, backup" }) {
                label.RenameConnection(endpoint.coSelf.strID, invalid);
                Check(label.outputConnections[endpoint.coSelf.strID].nickName == "Fuel backup — α", "Candidate label validation prevents native delimiter corruption");
            }
            label.SetGPM(); label.outputConnections.Clear(); Init(label);
            Check(label.outputConnections.Count == 1 && label.outputConnections[endpoint.coSelf.strID].nickName == "Fuel backup — α", "Allowed label round trips in unmodified native format");

            var ordinary = Device(); Input(ordinary, "a", true); Input(ordinary, "b", false); Tick(ordinary); ordinary.SetGateMode((int)GateMode.AND);
            Check(!Get<bool>(ordinary, "bNeedsCheck") && ordinary.status, "Unmarked non-signal-box devices retain vanilla behavior");
            CheckCandidateNetwork();
            CheckEndpointAuthority();
            if (production) CheckProductionLifecycle();
            Console.WriteLine((production ? "Production signal-box repairs: " : "Offline candidates: ") + (checks-before) + " checks passed.");
        } finally {
            patch.UnpatchSelf(); candidateBoxes.Clear(); candidateRetries.Clear(); candidateSwitches.Clear(); candidateDormant.Clear();
            CrewSim.system = oldSystem; DataHandler.mapCOs = oldMap; productionTesting = false;
        }
    }
}
