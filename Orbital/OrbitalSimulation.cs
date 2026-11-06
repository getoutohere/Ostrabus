using System;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using Ostrabus.Inspection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ostrabus.Orbital;

internal static class OrbitalSimulation {
    const double MetersPerAU = 149597870700d;
    sealed class FrameCache {
        public FrameCache() {}
        internal double Epoch;
        internal bool Initialized, Valid;
        internal OrbitAcceleration.Acceleration Acceleration;
        internal int Depth;
    }
    static ConditionalWeakTable<BodyOrbit, FrameCache> frames = new ConditionalWeakTable<BodyOrbit, FrameCache>();
    static WeakReference<StarSystem> system;
    internal static bool Enabled { get; private set; }
    internal static bool LaunchDefault { get; private set; }
    static long corrections, cacheHits, calculations, ignored, invalid, pathQueries;
    static long largeUpdates, superFastForwardUpdates;
    static double lastStep, maximumStep;
    static double lastEpoch, lastX, lastY, maximumMps2;
    static string lastBody;
    static int maximumDepth;

    internal static void Initialize(ConfigEntry<bool> launchFlag) {
        LaunchDefault = launchFlag.Value;
        Enabled = LaunchDefault;
        Reset();
    }
    internal static void SetEnabled(bool value) {
        Enabled = value;
        CoastIntegration.Reset();
        frames = new ConditionalWeakTable<BodyOrbit, FrameCache>();
        Log($"[Orbital] {(value ? "ON" : "OFF")}; launch default {(LaunchDefault ? "ON" : "OFF")} from [Orbital] Enabled. Toggle is session-only; positions, velocities and routes are preserved.");
    }
    static void Log(string message) {
        Ostrabus.OstrabusPlugin.Logger?.LogInfo(message);
        ConsoleToGUI.instance?.LogInfo(message);
    }
    internal static void SceneUnloaded(Scene _) { Reset(); }
    internal static void Reset() {
        CoastIntegration.Reset();
        SffSimulation.Reset();
        frames = new ConditionalWeakTable<BodyOrbit, FrameCache>(); system = null;
        corrections = cacheHits = calculations = ignored = invalid = pathQueries = 0;
        largeUpdates = superFastForwardUpdates = 0; lastStep = maximumStep = 0;
        lastEpoch = lastX = lastY = maximumMps2 = 0; lastBody = null; maximumDepth = 0;
    }
    internal static void Correct(StarSystem current, ShipSitu situation, BodyOrbit selected, ref Vector2 gravity) {
        if (!Enabled) return;
        EnsureSystem(current);
        if (situation == null || selected == null || situation.bIsBO || situation.bBOLocked || situation.bOrbitLocked || situation.bIgnoreGrav) { ignored++; return; }
        if (situation.NavData != null) pathQueries++; // Active playback normally bypasses numerical acceleration; invalid plans can fall through.
        double epoch = StarSystem.fEpoch;
        FrameCache cache = frames.GetOrCreateValue(selected);
        if (!cache.Initialized || cache.Epoch != epoch) {
            cache.Epoch = epoch; cache.Initialized = true;
            cache.Valid = OrbitAcceleration.TryFrame(selected, epoch, out cache.Acceleration, out cache.Depth);
            calculations++;
        } else cacheHits++;
        if (!cache.Valid) { invalid++; return; }
        var correction = cache.Acceleration;
        double x = gravity.x + correction.X, y = gravity.y + correction.Y;
        if (!OrbitAcceleration.Finite(x) || !OrbitAcceleration.Finite(y) || Math.Abs(x) > float.MaxValue || Math.Abs(y) > float.MaxValue) { invalid++; return; }
        if (correction.X == 0 && correction.Y == 0) return;
        gravity.x = (float)x; gravity.y = (float)y;
        corrections++; lastEpoch = epoch; lastBody = selected.strName;
        lastX = correction.X; lastY = correction.Y;
        maximumMps2 = Math.Max(maximumMps2, correction.Magnitude * MetersPerAU);
        maximumDepth = Math.Max(maximumDepth, cache.Depth);
    }
    static void EnsureSystem(StarSystem current) {
        if (system == null || !system.TryGetTarget(out StarSystem previous) || !ReferenceEquals(previous, current)) {
            Reset(); system = new WeakReference<StarSystem>(current);
        }
    }
    internal static void ObserveUpdate(StarSystem current, double seconds) {
        if (!Enabled || !OrbitAcceleration.Finite(seconds) || seconds <= 0) return;
        EnsureSystem(current);
        lastStep = seconds; maximumStep = Math.Max(maximumStep, seconds);
        if (seconds >= 60) largeUpdates++;
        if (GUIFFWD.Active) superFastForwardUpdates++;
    }
    internal static void Fault(Exception error) {
        Enabled = false;
        frames = new ConditionalWeakTable<BodyOrbit, FrameCache>();
        Log("[Orbital] Disabled after correction error; vanilla gravity retained: " + error.Message);
    }
    internal static object Snapshot() => SnapshotJson.Map("enabled", Enabled, "launchDefault", LaunchDefault,
        "correctionQueries", corrections, "cacheHits", cacheHits, "ephemerisCalculations", calculations,
        "ignoredQueries", ignored, "invalidQueries", invalid, "queriesWithNavData", pathQueries,
        "maximumHierarchyDepth", maximumDepth, "maximumCorrectionMps2", maximumMps2,
        "lastWorldStepSeconds", lastStep, "maximumWorldStepSeconds", maximumStep,
        "worldUpdatesAtLeast60Seconds", largeUpdates, "superFastForwardUpdates", superFastForwardUpdates,
        "sffEnabled", SffSimulation.Enabled, "lastSff", SffSimulation.Snapshot(),
        "coast", CoastIntegration.Snapshot(),
        "lastCorrection", SnapshotJson.Map("body", lastBody, "epoch", lastEpoch,
            "xMps2", lastX * MetersPerAU, "yMps2", lastY * MetersPerAU),
        "note", "Gravity-query counters, not counts of integrated objects. NavData playback can bypass numerical integration. No prediction, trigger evaluation or game-state mutation is performed by this endpoint.");
    internal static void Status() {
        Log($"[Orbital] {(Enabled ? "ON" : "OFF")}; coast {(CoastIntegration.Enabled ? "ON" : "OFF")}; SFF {(SffSimulation.Enabled ? "ON" : "OFF")}; launch default {(LaunchDefault ? "ON" : "OFF")}; correction queries {corrections}, ephemeris calculations {calculations}, invalid queries {invalid}, maximum correction {maximumMps2:F6} m/s². GET /orbital for details.");
    }
}
