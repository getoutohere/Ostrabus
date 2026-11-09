using System;
using Ostranauts.Electrical;

static partial class Program {
    static bool Truth(GateMode gate, bool a, bool b, bool oneInput = false) {
        bool any = a || (!oneInput && b), all = a && (oneInput || b);
        return gate == GateMode.OR ? any : gate == GateMode.AND ? all : gate == GateMode.NOR ? !any : !all;
    }
    static void CheckCandidateNetwork() {
        var a = Box(); var b = Box(); var c = Box();
        bool sensorA = false, sensorB = true, sensorC = false, connected = true;
        Input(a, "sensor-a", sensorA); Input(a, "sensor-b", sensorB); Input(c, "sensor-c", sensorC);
        Tick(a); a.SetUpConnection(b.coSelf); Tick(a); Tick(b);
        b.SetUpConnection(c.coSelf); Tick(b); Tick(c);
        var random = new Random(7305);
        for (int eventIndex = 0; eventIndex < 300; eventIndex++) {
            switch (random.Next(10)) {
                case 0: sensorA = !sensorA; a.AddSignal(new ElectricalSignal("sensor-a", sensorA ? SignalType.On : SignalType.Off)); break;
                case 1: sensorB = !sensorB; a.AddSignal(new ElectricalSignal("sensor-b", sensorB ? SignalType.On : SignalType.Off)); break;
                case 2: a.ToggleOverride(); break;
                case 3: if (connected) a.ToggleConnection(b.coSelf.strID); break;
                case 4: b.SetGateMode(random.Next(4)); break;
                case 5: b.ToggleOverride(); break;
                case 6: sensorC = !sensorC; c.AddSignal(new ElectricalSignal("sensor-c", sensorC ? SignalType.On : SignalType.Off)); break;
                case 7:
                    conditions[a.coSelf.strID].Remove("IsPowered"); sensorA = !sensorA;
                    a.AddSignal(new ElectricalSignal("sensor-a", sensorA ? SignalType.On : SignalType.Off)); Tick(a);
                    conditions[a.coSelf.strID].Add("IsPowered"); break;
                case 8:
                    if (connected) a.RemoveConnection(b.coSelf.strID); else a.SetUpConnection(b.coSelf);
                    connected = !connected; break;
                case 9: c.SetGateMode(random.Next(4)); break;
            }
            for (int pass = 0; pass < 6; pass++) { Tick(a); Tick(b); Tick(c); }
            bool aGate = Truth(a.gateMode, sensorA, sensorB);
            bool aOutput = aGate && a.bOverride && connected && a.outputConnections[b.coSelf.strID].switchStatus;
            bool bGate = !connected || Truth(b.gateMode, aOutput, false, true); // Native no-input fallback is On.
            bool bOutput = bGate && b.bOverride;
            bool cGate = Truth(c.gateMode, bOutput, sensorC);
            Check(a.status == aGate, "Stateful network root matches independent truth model at event " + eventIndex);
            Check(b.status == bGate, "Stateful network relay matches independent truth model at event " + eventIndex);
            Check(c.status == cGate, "Stateful network sink matches independent truth model at event " + eventIndex);
            Check(b.inputConnections.ContainsKey(a.coSelf.strID) == connected, "Stateful disconnect/reconnect keeps reciprocal membership");
            Check(Queue(a, "sendQueue").Count == 0 && Gpm(a)["sendQueue"] == "", "Settled root queue has no saved replay");
        }
        var deferred = Box(); Input(deferred, "hi", true); Input(deferred, "lo", false); Tick(deferred);
        Set(deferred, "bNeedsCheck", true); Set(deferred, "fTimeOfNextSignalCheck", StarSystem.fEpoch + 10);
        double deadline = Get<double>(deferred, "fTimeOfNextSignalCheck"); deferred.SetGateMode((int)GateMode.AND);
        Check(Get<double>(deferred, "fTimeOfNextSignalCheck") == deadline, "Configuration change does not shorten a pending native deadline");
    }
}
