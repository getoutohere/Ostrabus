using System;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.Objectives;

namespace Ostrabus.QoL.AutoVents;

internal static class AutoVentControl {
    internal static ConfigEntry<bool> Flag;
    internal static ConfigEntry<double> NormalCutoff, EmergencyCutoff, GraceSeconds;
    internal static bool Enabled => Flag?.Value == true;
    sealed class VentState {
        internal PressurePolicy A, B;
        internal double SampleEpoch = double.NaN;
        internal bool Trip, CloseQueued;
    }
    static ConditionalWeakTable<CondOwner, VentState> states = new ConditionalWeakTable<CondOwner, VentState>();
    static readonly AccessTools.FieldRef<GasPressureSense, CondOwner> Owner = AccessTools.FieldRefAccess<GasPressureSense, CondOwner>("co");
    static readonly AccessTools.FieldRef<GasPressureSense, string> Alarm = AccessTools.FieldRefAccess<GasPressureSense, string>("strInteractionAlarm");
    internal static void Initialize(ConfigFile config) {
        Flag = config.Bind("QoL.AutoVents", "Enabled", true, "Kang auto-vents tolerate brief low-O2 transients. Toggle in the PDA settings app. Does not configure devices remotely.");
        NormalCutoff = config.Bind("QoL.AutoVents", "NormalOxygenKPa", 20d, new ConfigDescription("Close after oxygen partial pressure stays below this cutoff for GraceSeconds. Other alarms and crew thresholds are unchanged.", new AcceptableValueRange<double>(0, 1000)));
        EmergencyCutoff = config.Bind("QoL.AutoVents", "EmergencyOxygenKPa", 17d, new ConfigDescription("Request immediate closure at or below this O2 partial pressure. Limited to NormalOxygenKPa at runtime.", new AcceptableValueRange<double>(0, 1000)));
        GraceSeconds = config.Bind("QoL.AutoVents", "GraceSeconds", 3d, new ConfigDescription("Consecutive observed low-pressure time, in game seconds. Vacuum/missing rooms and the emergency cutoff bypass this delay.", new AcceptableValueRange<double>(0, 60)));
        Flag.SettingChanged += Changed; NormalCutoff.SettingChanged += Changed;
        EmergencyCutoff.SettingChanged += Changed; GraceSeconds.SettingChanged += Changed;
    }
    static void Changed(object sender, EventArgs args) => Reset();
    internal static void SetEnabled(bool enabled) {
        if (Flag == null) throw new InvalidOperationException("Auto-vent settings are not initialized.");
        Flag.Value = enabled;
    }
    internal static void Reset() => states = new ConditionalWeakTable<CondOwner, VentState>();
    internal static object Inspect(CondOwner vent) {
        if (!states.TryGetValue(vent, out var state)) return null;
        double cutoff = Value(NormalCutoff, 20);
        return new Dictionary<string, object> {
            ["enabled"] = Enabled, ["normalOxygenKPa"] = cutoff,
            ["emergencyOxygenKPa"] = Math.Min(cutoff, Value(EmergencyCutoff, 17)), ["graceSeconds"] = Math.Min(60, Value(GraceSeconds, 3)),
            ["sampleEpoch"] = state.SampleEpoch, ["oxygenAKPa"] = state.A.Oxygen, ["oxygenBKPa"] = state.B.Oxygen,
            ["lowSecondsA"] = state.A.LowSeconds, ["lowSecondsB"] = state.B.LowSeconds,
            ["trip"] = state.Trip, ["closeQueued"] = state.CloseQueued
        };
    }
    internal static void SceneUnloaded(UnityEngine.SceneManagement.Scene scene) => Reset();
    internal static void Stop() {
        if (Flag != null) Flag.SettingChanged -= Changed;
        if (NormalCutoff != null) NormalCutoff.SettingChanged -= Changed;
        if (EmergencyCutoff != null) EmergencyCutoff.SettingChanged -= Changed;
        if (GraceSeconds != null) GraceSeconds.SettingChanged -= Changed;
        Reset();
    }
    internal static bool Handles(GasPressureSense sensor) => Enabled && Alarm(sensor) == "MSVent02CloseO2Allow";
    static double Value(ConfigEntry<double> entry, double fallback) {
        double value = entry?.Value ?? fallback;
        return double.IsNaN(value) || double.IsInfinity(value) || value < 0 ? fallback : value;
    }
    static bool Side(CondOwner vent, string point, ref PressurePolicy policy, double now, double cutoff, double emergency, double grace) {
        Room room = vent.ship.GetRoomAtWorldCoords1(vent.GetPos(point), bAllowDocked: true);
        CondOwner atmosphere = room?.CO;
        bool invalid = room == null || room.Void || atmosphere == null;
        return policy.Observe(invalid ? 0 : atmosphere.GetCondAmount("StatGasPpO2"), invalid, atmosphere?.strID, now, cutoff, emergency, grace);
    }
    static bool Evaluate(CondOwner vent, VentState state) {
        double now = vent.Clock.Epoch;
        if (state.SampleEpoch == now) return state.Trip;
        double cutoff = Value(NormalCutoff, 20), emergency = Math.Min(cutoff, Value(EmergencyCutoff, 17));
        double grace = Math.Min(60, Value(GraceSeconds, 3));
        // Evaluate both even if the first trips, so their independent histories
        // remain current. The two native sensors no longer compete for one tick.
        bool a = Side(vent, "RoomA", ref state.A, now, cutoff, emergency, grace);
        bool b = Side(vent, "RoomB", ref state.B, now, cutoff, emergency, grace);
        state.SampleEpoch = now;
        return state.Trip = a || b;
    }
    static void Close(CondOwner vent, VentState state, bool notify) {
        if (state.CloseQueued) return;
        Interaction close = DataHandler.GetInteraction("MSVent02CloseO2Allow");
        if (close == null || !close.CTTestUs.Triggered(vent)) return;
        if (!vent.QueueInteraction(vent, close)) return;
        state.CloseQueued = true;
        if (notify) ReportAlarm(vent, close);
    }
    internal static void ReportAlarm(CondOwner vent, Interaction close) {
        // Same native objective as GasPressureSense.Run, once per closure.
        try {
            var objective = new AlarmObjective(AlarmType.low_pressure, vent, close.strTitle, "TIsAlarmSafePressure", vent.ship.strRegID, close.strDesc);
            MonoSingleton<ObjectiveTracker>.Instance.AddObjective(objective);
        } catch (Exception error) { UnityEngine.Debug.LogWarning("[AutoVents] Could not report vent closure: " + error.Message); }
    }
    // Return true only to fall back to vanilla. No native objects are touched
    // while disabled or for ordinary vents and other pressure alarms.
    internal static bool Process(GasPressureSense sensor, bool initial) {
        if (!Handles(sensor)) return true;
        CondOwner vent = Owner(sensor);
        if (vent == null || vent.bDestroyed || vent.ship == null) return true;
        if (!initial && !vent.HasCond("IsReadyPressureSense")) return false;
        if (!vent.HasCond("IsPowered") || !vent.HasCond("IsOpen")) {
            states.Remove(vent); sensor.doNotExchange = false;
            return false;
        }
        try {
            var state = states.GetValue(vent, _ => new VentState());
            bool trip = Evaluate(vent, state);
            sensor.doNotExchange = trip;
            if (trip) Close(vent, state, !initial);
            if (!initial) {
                // Coalesce missed ticks: several pending signals at one epoch
                // are one present observation, not several seconds of history.
                double pending = vent.GetCondAmount("IsReadyPressureSense");
                if (pending > 0) vent.AddCondAmount("IsReadyPressureSense", -pending);
            }
            return false;
        } catch (Exception error) {
            states.Remove(vent);
            UnityEngine.Debug.LogWarning("[AutoVents] Sensor failed; using native behavior: " + error.Message);
            return true;
        }
    }
}
