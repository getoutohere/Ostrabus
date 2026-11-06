using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using BepInEx.Configuration;
using Condowner.Utils;
using HarmonyLib;
using Ostrabus.QoL.AutoVents;
using UnityEngine;

static class Program {
    static int checks, roomReads, nativeLookups, queues, objectives;
    static double now;
    static CondOwner vent, airA, airB;
    static Room roomA, roomB;
    static GasPressureSense sensorA, sensorB;
    static readonly Dictionary<string, Dictionary<string, double>> amounts = new Dictionary<string, Dictionary<string, double>>();
    static bool queueAccepted = true, powerGate = true;
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Field(object obj, string name, object value) => AccessTools.Field(obj.GetType(), name).SetValue(obj, value);
    static void Check(bool ok, string why) { checks++; if (!ok) throw new Exception(why); }
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = ReferenceEquals(__0, __1); return false; }
    static bool Unequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = !ReferenceEquals(__0, __1); return false; }
    static bool Implicit(UnityEngine.Object __0, ref bool __result) { __result = !ReferenceEquals(__0, null); return false; }
    static bool Read(CondOwner __instance, string strName, ref double __result) { amounts[__instance.strID].TryGetValue(strName, out __result); return false; }
    static bool Has(CondOwner __instance, string strName, ref bool __result) { __result = amounts[__instance.strID].TryGetValue(strName, out double value) && value > 0; return false; }
    static bool Add(CondOwner __instance, string strName, double fAmount) { var data = amounts[__instance.strID]; data.TryGetValue(strName, out double value); data[strName] = value + fAmount; return false; }
    static bool Epoch(ref double __result) { __result = now; return false; }
    static bool Pos(string strPointName, ref Vector2 __result) { __result = strPointName == "RoomA" ? new Vector2(1, 0) : new Vector2(-1, 0); return false; }
    static bool RoomAt(Vector2 vPos, ref Room __result) { roomReads++; __result = vPos.x > 0 ? roomA : roomB; return false; }
    static bool LegacyLookup(Vector2 vPos, List<CondOwner> aOut) { nativeLookups++; var air = vPos.x > 0 ? airA : airB; if (amounts[air.strID]["StatGasPpO2"] >= 20) aOut.Add(air); return false; }
    static bool Trigger(ref bool __result) { __result = powerGate; return false; }
    static bool Definition(string strName, ref Interaction __result) { __result = strName == null ? null : new Interaction { strName = strName, fDuration = 0.0002, CTTestUs = new CondTrigger() }; return false; }
    static bool TriggerDefinition(string strName, ref CondTrigger __result) { __result = new CondTrigger { strName = strName }; return false; }
    static bool Queue(CondOwner objTarget, Interaction objInteraction, ref bool __result) { Check(ReferenceEquals(objTarget, vent) && objInteraction.strName == "MSVent02CloseO2Allow", "Non-native close queued"); Check(objInteraction.fDuration == 0.0002, "Closing duration changed"); queues++; __result = queueAccepted; return false; }
    static bool Report() { objectives++; return false; }
    static bool Quiet() => false;
    static bool Component(ref GasPressureSense __result) { __result = sensorB; return false; }
    static CondOwner CO(string id) { var co = Empty<CondOwner>(); Field(co, "_strID", id); amounts[id] = new Dictionary<string, double>(); return co; }
    static GasPressureSense Sensor(string point, string alarm = "MSVent02CloseO2Allow") {
        var sense = Empty<GasPressureSense>(); Field(sense, "co", vent);
        sense.SetData(new Dictionary<string, string> { ["strPoint"] = point, ["strSignalCond"] = "IsReadyPressureSense", ["strInteractionAlarm"] = alarm, ["strInteractionClear"] = null, ["strCTClear"] = "TIsGasPressureSafeO2" });
        return sense;
    }
    static void Fresh(double a = 21, double b = 21) {
        AutoVentControl.Reset(); now = 100; roomReads = nativeLookups = queues = objectives = 0;
        roomA = Empty<Room>(); roomB = Empty<Room>();
        Field(roomA, "coRoom", airA); Field(roomB, "coRoom", airB);
        amounts[airA.strID]["StatGasPpO2"] = a; amounts[airB.strID]["StatGasPpO2"] = b;
        amounts[vent.strID]["IsPowered"] = 1; amounts[vent.strID]["IsOpen"] = 1; amounts[vent.strID]["IsReadyPressureSense"] = 0;
        sensorA.doNotExchange = sensorB.doNotExchange = false; queueAccepted = powerGate = true;
    }
    static void Tick(double elapsed = 1) {
        now += elapsed; amounts[vent.strID]["IsReadyPressureSense"] = 1;
        sensorB.Run(); sensorA.Run();
    }
    static void ScalarChecks() {
        bool Observe(ref PressurePolicy p, double oxygen, double t, string room = "a", bool invalid = false, double cutoff = 20, double emergency = 17, double grace = 3) => p.Observe(oxygen, invalid, room, t, cutoff, emergency, grace);
        var p = new PressurePolicy();
        Check(!Observe(ref p, 20, 0), "Exact normal threshold tripped");
        Check(!Observe(ref p, 19, 1) && !Observe(ref p, 19, 2) && !Observe(ref p, 19, 3), "Grace elapsed too early");
        Check(Observe(ref p, 19, 4), "Sustained low pressure did not trip at three seconds");
        p = new PressurePolicy();
        Check(!Observe(ref p, 19, 1) && !Observe(ref p, 21, 3) && !Observe(ref p, 19, 4) && !Observe(ref p, 19, 5), "Recovery failed to cancel timer");
        foreach (double bad in new[] { 17d, 0d, -1d, double.NaN, double.PositiveInfinity }) { p = new PressurePolicy(); Check(Observe(ref p, bad, 0), "Emergency/invalid pressure did not trip"); }
        p = new PressurePolicy(); Check(Observe(ref p, 21, 0, invalid: true), "Missing/void room did not trip");
        p = new PressurePolicy(); Observe(ref p, 19, 1); Check(!Observe(ref p, 19, 100), "Skipped time credited as observed pressure");
        Check(!Observe(ref p, 19, 99), "Clock reversal did not reset dwell");
        p = new PressurePolicy(); Observe(ref p, 19, 1); Observe(ref p, 19, 3); Check(!Observe(ref p, 19, 4, "new-room"), "Different room inherited dwell time");
        p = new PressurePolicy(); Check(Observe(ref p, 19, 0, grace: 0), "Zero grace did not trip immediately");
        p = new PressurePolicy(); Check(!Observe(ref p, 18, 0, cutoff: 18, emergency: 16), "Configured normal threshold ignored");
        // Independent one-second series oracle: count elapsed consecutive low
        // samples, with emergency taking priority and recovery clearing history.
        var random = new System.Random(819); p = new PressurePolicy(); double? since = null;
        for (int i = 0; i < 2000; i++) {
            double oxygen = random.Next(16, 23);
            if (oxygen >= 20) since = null;
            else if (!since.HasValue) since = i;
            bool expected = oxygen <= 17 || oxygen < 20 && i - since.Value >= 3;
            bool actual = Observe(ref p, oxygen, i);
            Check(actual == expected, "Pressure-series policy differs from oracle");
            if (oxygen <= 17) since = null;
        }
    }
    static void IntegrationChecks() {
        Fresh(19, 21); sensorB.FixForVentSensors();
        Check(!sensorB.doNotExchange && queues == 0 && roomReads == 2, "Initial marginal pressure bypassed grace or checked only one side");
        var exchange = Empty<GasExchange>();
        AccessTools.Method(typeof(GasExchange), "FixForVentSensors").Invoke(exchange, null);
        Check(!(bool)AccessTools.Field(typeof(GasExchange), "doNotExchange").GetValue(exchange), "Native GasExchange cached a premature flow block");
        int before = roomReads; for (int i = 0; i < 1000; i++) { sensorA.Run(); sensorB.Run(); }
        Check(roomReads == before, "No-signal updates performed room searches");
        Tick(); Check(roomReads == before + 2 && queues == 0, "Two sensors did not share one sample");
        Tick(); amounts[airA.strID]["StatGasPpO2"] = 20; Tick();
        Check(queues == 0, "Airlock-like dip/recovery closed the vent");
        Fresh(21, 19); sensorB.FixForVentSensors(); Tick(); Tick(); Tick();
        Check(queues == 1 && objectives == 1, "RoomB sustained low pressure did not queue exactly one native close/objective");
        Tick(); sensorA.FixForVentSensors(); Check(queues == 1, "Close was queued more than once");
        Fresh(17, 21); sensorB.FixForVentSensors();
        Check(queues == 1 && sensorB.doNotExchange && objectives == 0, "Initial emergency was delayed or changed native startup reporting");
        exchange = Empty<GasExchange>(); AccessTools.Method(typeof(GasExchange), "FixForVentSensors").Invoke(exchange, null);
        Check((bool)AccessTools.Field(typeof(GasExchange), "doNotExchange").GetValue(exchange), "Native emergency flow block lost");
        Fresh(21, 17); Tick(); Check(queues == 1, "Second sensing point emergency ignored");
        Fresh(); roomA = null; Tick(); Check(queues == 1, "Missing room did not close");
        Fresh(); Field(roomB, "bVoid", true); Tick(); Check(queues == 1, "Void room did not close");
        Fresh(19, 21); Tick(); Tick(100); Check(queues == 0, "SFF-like gap advanced low timer");
        Tick(); Tick(); Tick(); Check(queues == 1, "Normal timing did not resume after gap");
        Fresh(19, 21); Tick(); amounts[vent.strID]["IsReadyPressureSense"] = 100; sensorA.Run(); sensorB.Run();
        Check(queues == 0 && amounts[vent.strID]["IsReadyPressureSense"] == 0, "Pending signals became fake low-pressure history");
        Fresh(19, 21); Tick(); amounts[vent.strID]["IsPowered"] = 0; amounts[vent.strID]["IsReadyPressureSense"] = 1; sensorA.Run();
        Check(AutoVentControl.Inspect(vent) == null, "Power loss retained timer");
        amounts[vent.strID]["IsPowered"] = 1; Tick(); Check(queues == 0, "Power restoration inherited old timer");
        Fresh(19, 21); Tick(); now += 1; Field(roomA, "coRoom", CO("replacement")); amounts["replacement"]["StatGasPpO2"] = 19;
        Tick(); Check(queues == 0, "Room topology change inherited timer");
        Fresh(19, 21); Tick(); AutoVentControl.SceneUnloaded(default); Tick(); Check(queues == 0, "Scene change inherited timer");
        Fresh(); Tick(); before = roomReads; Check(AutoVentControl.Inspect(vent) != null && roomReads == before, "Inspection sampled or created game state");
        AutoVentControl.SetEnabled(false); Check(!AutoVentControl.Enabled && AutoVentControl.Inspect(vent) == null, "Live toggle retained state");
        Fresh(); amounts[vent.strID]["IsReadyPressureSense"] = 1; sensorB.Run();
        Check(nativeLookups == 1 && roomReads == 0, "Disabled feature did not use native sensor");
        AutoVentControl.SetEnabled(true); Fresh(); var other = Sensor("RoomB", "MSAlarmO2SwitchRedAllow"); amounts[vent.strID]["IsReadyPressureSense"] = 1; other.Run();
        Check(nativeLookups == 1 && roomReads == 0, "Ordinary alarm was patched");
        Fresh(17, 21); queueAccepted = false; Tick(); Tick(); Check(queues == 2 && objectives == 0, "Rejected queue was treated as completed closure");
        Fresh(17, 21); powerGate = false; Tick(); Check(queues == 0, "Native powered/installed trigger bypassed");
        Fresh(19, 21); Tick(); AutoVentControl.GraceSeconds.Value = 0; Tick(); Check(queues == 1, "Config change did not reset/apply policy"); AutoVentControl.GraceSeconds.Value = 3;
        Fresh(19, 21); AutoVentControl.NormalCutoff.Value = 18; AutoVentControl.EmergencyCutoff.Value = 19; Tick(); Check(queues == 0, "Emergency cutoff exceeded normal cutoff");
        AutoVentControl.NormalCutoff.Value = 20; AutoVentControl.EmergencyCutoff.Value = 17;
    }
    static void Patch(Harmony h, Type type, string method, string hook, Type[] args = null) => h.Patch(AccessTools.Method(type, method, args), prefix: new HarmonyMethod(typeof(Program), hook));
    static int Main() {
        ScalarChecks();
        string configPath = Path.Combine(Path.GetTempPath(), "codex-autovents-" + Guid.NewGuid().ToString("N") + ".cfg");
        try {
            AccessTools.Field(typeof(BepInEx.Paths), "<BepInExConfigPath>k__BackingField").SetValue(null, configPath + ".core");
            AutoVentControl.Initialize(new ConfigFile(configPath, false));
            Check(AutoVentControl.Enabled && AutoVentControl.NormalCutoff.Value == 20 && AutoVentControl.EmergencyCutoff.Value == 17 && AutoVentControl.GraceSeconds.Value == 3, "Config defaults incorrect");
            var h = new Harmony("offline.autovents.fixture");
            Patch(h, typeof(UnityEngine.Object), "op_Equality", nameof(Equal)); Patch(h, typeof(UnityEngine.Object), "op_Inequality", nameof(Unequal)); Patch(h, typeof(UnityEngine.Object), "op_Implicit", nameof(Implicit));
            Patch(h, typeof(CondOwner), "GetCondAmount", nameof(Read), new[] { typeof(string) }); Patch(h, typeof(CondOwner), "HasCond", nameof(Has), new[] { typeof(string) });
            Patch(h, typeof(CondOwner), "AddCondAmount", nameof(Add)); Patch(h, typeof(CondOwner), "GetPos", nameof(Pos));
            h.Patch(AccessTools.PropertyGetter(typeof(Clock), "Epoch"), prefix: new HarmonyMethod(typeof(Program), nameof(Epoch)));
            Patch(h, typeof(Ship), "GetRoomAtWorldCoords1", nameof(RoomAt)); Patch(h, typeof(Ship), "GetCOsAtWorldCoords1", nameof(LegacyLookup));
            Patch(h, typeof(CondTrigger), "Triggered", nameof(Trigger), new[] { typeof(CondOwner), typeof(string), typeof(bool) });
            Patch(h, typeof(DataHandler), "GetInteraction", nameof(Definition), new[] { typeof(string), typeof(JsonInteractionSave), typeof(bool) });
            Patch(h, typeof(DataHandler), "GetCondTrigger", nameof(TriggerDefinition), new[] { typeof(string) });
            Patch(h, typeof(CondOwner), "QueueInteraction", nameof(Queue)); Patch(h, typeof(AutoVentControl), "ReportAlarm", nameof(Report));
            Patch(h, typeof(Debug), "LogWarning", nameof(Quiet), new[] { typeof(object) });
            var component = typeof(UnityEngine.Component).GetMethods().Single(m => m.Name == "GetComponent" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0).MakeGenericMethod(typeof(GasPressureSense));
            h.Patch(component, prefix: new HarmonyMethod(typeof(Program), nameof(Component)));
            h.CreateClassProcessor(typeof(AutoVentSensePatch)).Patch(); h.CreateClassProcessor(typeof(AutoVentInitialPatch)).Patch();
            vent = CO("vent"); airA = CO("airA"); airB = CO("airB"); vent.Clock = Empty<Clock>(); Field(vent, "_objShip", Empty<Ship>());
            sensorA = Sensor("RoomA"); sensorB = Sensor("RoomB");
            IntegrationChecks();
            Check(File.Exists(configPath) && File.ReadAllText(configPath).Contains("[QoL.AutoVents]"), "Configuration did not persist");
            Console.WriteLine(checks + " auto-vent policy/native integration checks passed.");
            return 0;
        } finally { AutoVentControl.Stop(); if (File.Exists(configPath)) File.Delete(configPath); if (File.Exists(configPath + ".core")) File.Delete(configPath + ".core"); }
    }
}
