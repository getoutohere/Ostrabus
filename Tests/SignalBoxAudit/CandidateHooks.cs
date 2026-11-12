// Offline prototypes only. This directory is excluded from the plugin build.
using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using Ostranauts.Electrical;
using Ostrabus.MiscBugFixes.SignalBoxes;

static partial class Program {
    static bool productionTesting;
    static readonly HashSet<Electrical> candidateBoxes = new HashSet<Electrical>();
    static readonly Dictionary<Electrical, List<ElectricalSignal>> candidateRetries = new Dictionary<Electrical, List<ElectricalSignal>>();
    static readonly HashSet<Electrical> candidateDormant = new HashSet<Electrical>();
    static readonly Dictionary<(Ship ship, string id), int> candidateSwitches = new Dictionary<(Ship, string), int>();
    static readonly AccessTools.FieldRef<Ship, bool> CompletedElectricalLoad = AccessTools.FieldRefAccess<Ship, bool>("_bDoneLoading");
    static bool Authoritative(Ship ship) => ship != null && !ship.bDestroyed && ship.LoadState == Ship.Loaded.Full && CompletedElectricalLoad(ship) &&
        CrewSim.system?.dictShips != null && ship.strRegID != null && CrewSim.system.dictShips.TryGetValue(ship.strRegID, out var current) && ReferenceEquals(current, ship);
    static bool EndpointTemporary(Ship ship, string id) => productionTesting ? SignalBoxRepair.EndpointTemporary(ship, id) : !Authoritative(ship) || candidateSwitches.ContainsKey((ship, id));
    static bool EndpointTemporary(Electrical owner, string id) => owner.isInitialising ||
        !ReferenceEquals(owner.coSelf.ship, owner.coShip) || EndpointTemporary(owner.coShip, id);
    static void SwitchBegin(Ship ship, string id) {
        if (productionTesting) { SignalBoxRepair.TrackSwitch(ship, id); return; }
        candidateSwitches.TryGetValue((ship, id), out int depth); candidateSwitches[(ship, id)] = depth + 1;
    }
    static void SwitchEnd(Ship ship, string id) {
        if (productionTesting) { SignalBoxRepair.EndModeSwitch(new SignalBoxRepair.ModeScope { Ship = ship, Id = id }); return; }
        if (candidateSwitches[(ship, id)] <= 1) candidateSwitches.Remove((ship, id)); else candidateSwitches[(ship, id)]--;
    }
    static bool Candidate(Electrical e) => candidateBoxes.Contains(e);
    static Electrical Box() {
        var e = Device(); candidateBoxes.Add(e);
        if (productionTesting) { e.coSelf.strCODef = "ItmElectricalBox01"; conditions[e.coSelf.strID].Add("IsElectricalBox"); }
        Set(e.coShip, "nLoadState", Ship.Loaded.Full);
        CompletedElectricalLoad(e.coShip) = true;
        e.coShip.strRegID = "ship-" + e.coSelf.strID;
        CrewSim.system.dictShips[e.coShip.strRegID] = e.coShip;
        return e;
    }
    static void ScheduleCandidate(Electrical e) {
        if (!Candidate(e)) return;
        bool pending = Get<bool>(e, "bNeedsCheck");
        Set(e, "bNeedsCheck", true);
        if (!pending || Get<double>(e, "fTimeOfNextSignalCheck") < StarSystem.fEpoch)
            Set(e, "fTimeOfNextSignalCheck", StarSystem.fEpoch + .1);
    }
    static void CandidateGateBefore(Electrical __instance, out GateMode __state) { __state = __instance.gateMode; }
    static void CandidateGateAfter(Electrical __instance, GateMode __state) {
        if (__instance.gateMode != __state) ScheduleCandidate(__instance);
    }
    static void CandidateAuditBefore(Electrical __instance, out int __state) { __state = __instance.inputConnections.Count; }
    static void CandidateAuditAfter(Electrical __instance, int __state) {
        if (CandidateAuditAllowed(__instance) && __instance.inputConnections.Count != __state) ScheduleCandidate(__instance);
    }
    static bool CandidateAuditAllowed(Electrical __instance) {
        if (!Candidate(__instance)) return true;
        if (__instance.isInitialising || !ReferenceEquals(__instance.coSelf.ship, __instance.coShip) || !Authoritative(__instance.coShip)) { __instance.fTimeToNextAudit = 1; return false; }
        foreach (var link in __instance.inputConnections.Values) if (candidateSwitches.ContainsKey((__instance.coShip, link.originID))) return false;
        foreach (var link in __instance.outputConnections.Values) if (candidateSwitches.ContainsKey((__instance.coShip, link.originID))) return false;
        return true;
    }
    static void CandidateQueue(Electrical __instance, string destination, ref SignalType signalType) {
        if (!Candidate(__instance) || signalType != SignalType.On) return;
        if (!__instance.status || !__instance.bOverride ||
            !__instance.outputConnections.TryGetValue(destination, out var branch) || !branch.switchStatus) signalType = SignalType.Off;
    }
    static bool CandidateSend(Electrical __instance, string destination, ElectricalSignal signal, ref bool __result) {
        if (!Candidate(__instance)) return true;
        if (EndpointTemporary(__instance, destination)) {
            if (signal.signalType == SignalType.Disconnect || __instance.outputConnections.ContainsKey(destination)) RetryCandidate(__instance, destination, signal);
            __result = false; return false;
        }
        if (signal.signalType != SignalType.Disconnect || __instance.outputConnections.ContainsKey(destination)) return true;
        var co = __instance.coShip.GetCOByID(destination);
        if (co?.Electrical != null) {
            if (co.Electrical.isInitialising) { RetryCandidate(__instance, destination, signal); __result = false; return false; }
            // Terminal delivery can remove an existing input, never recreate one.
            if (co.Electrical.inputConnections.ContainsKey(__instance.coSelf.strID)) co.Electrical.AddSignal(signal);
            __result = true;
        } else __result = true; // Settled runtime map proves there is no receiver in this routing domain.
        return false;
    }
    static void RetryCandidate(Electrical owner, string destination, ElectricalSignal signal) {
        if (!candidateRetries.TryGetValue(owner, out var retry)) candidateRetries[owner] = retry = new List<ElectricalSignal>();
        retry.Add(new ElectricalSignal(destination, signal.signalType, signal.callback));
    }
    static void CandidateResolved(Electrical __instance) {
        if (!Candidate(__instance)) return;
        if (candidateRetries.TryGetValue(__instance, out var retry) && retry.Count != 0) {
            foreach (var signal in retry) __instance.QueueSignal(signal.originID, signal.signalType);
            retry.Clear();
            Set(__instance, "fTimeOfNextSignalCheck", StarSystem.fEpoch + .1);
            Set(__instance, "bNeedsCheck", false);
            candidateDormant.Add(__instance); // Lifecycle completion wakes it; do not poll missing endpoints.
        }
        CandidatePersist(__instance);
    }
    static void WakeCandidate(Electrical owner) {
        if (productionTesting) { SignalBoxRepair.Wake(owner.coShip); return; }
        if (candidateDormant.Remove(owner)) ScheduleCandidate(owner);
    }
    static void CandidatePersist(Electrical __instance) {
        if (!Candidate(__instance)) return;
        var queue = Queue(__instance, "sendQueue");
        if (queue.Count == 0) { Gpm(__instance)["sendQueue"] = ""; return; }
        var text = new StringBuilder();
        foreach (var signal in queue) { if (text.Length > 0) text.Append(','); text.Append(signal.ToString()); }
        Gpm(__instance)["sendQueue"] = text.ToString();
    }
    static void CandidateInitialised(Electrical __instance) {
        if (!Candidate(__instance)) return;
        if (Queue(__instance, "sendQueue").Count != 0 || Queue(__instance, "signalQueue").Count != 0) ScheduleCandidate(__instance);
        CandidatePersist(__instance);
    }
    static bool CandidateRename(Electrical __instance, string newName) => !Candidate(__instance) ||
        newName == null || (newName.IndexOf('#') < 0 && newName.IndexOf(',') < 0);
}
