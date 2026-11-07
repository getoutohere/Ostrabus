using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace Ostrabus.Inspection;

// Explicit dynamic registration only: no HarmonyPatch attribute or startup hook.
internal static class FlightJournal {
    internal const string Owner = "com.ostranauts.template.inspection.flight";
    static readonly Harmony harmony = new Harmony(Owner);
    static FlightJournalWriter session;
    static volatile bool active;
    static bool capturing, pendingSettled, pendingFollowup;
    static long captureErrors;
    static long nextTransition, pendingTransition;
    static double lastEpoch;
    static double followupEpoch;
    static LogListener logListener;
    static int mainThread;
    internal static bool Active => active;
    internal struct Step {
        internal bool Capture;
        internal object PreviousReport;
        internal long Id;
    }
    internal static bool IsControl(string path) => path == "/flight/start" || path == "/flight/mark" || path == "/flight/stop";
    internal static InspectionApi.Reply Control(string path) {
        try {
            if (path == "/flight/start") Start();
            else if (path == "/flight/stop") Stop();
            else if (!Live()) return new InspectionApi.Reply(409, SnapshotJson.Map("error", "Flight journal is not recording."));
            else Capture("checkpoint");
            return new InspectionApi.Reply(200, Snapshot());
        } catch (InvalidOperationException ex) { return new InspectionApi.Reply(409, SnapshotJson.Map("error", ex.Message)); }
    }
    internal static void Start(string outputPath = null) {
        if (active) return;
        if (session != null && !session.Finished) throw new InvalidOperationException("Previous flight journal is still draining.");
        string path = outputPath ?? Path.Combine(BepInEx.Paths.BepInExRootPath, "FlightJournals", "flight-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".jsonl");
        session = new FlightJournalWriter(path); captureErrors = nextTransition = pendingTransition = 0; pendingSettled = pendingFollowup = false; mainThread = Thread.CurrentThread.ManagedThreadId;
        try {
            harmony.Patch(AccessTools.Method(typeof(StarSystem), nameof(StarSystem.Update)),
                prefix: new HarmonyMethod(typeof(FlightJournal), nameof(WorldBegin)) { priority = Priority.First },
                finalizer: new HarmonyMethod(typeof(FlightJournal), nameof(WorldEnd)) { priority = Priority.Last, after = new[] { PluginInfo.PLUGIN_GUID } });
            harmony.Patch(AccessTools.Method(typeof(AIShipManager), "RegionCleanup"),
                prefix: new HarmonyMethod(typeof(FlightJournal), nameof(RegionBegin)) { priority = Priority.First },
                finalizer: new HarmonyMethod(typeof(FlightJournal), nameof(RegionEnd)));
            harmony.Patch(AccessTools.Method(typeof(AIShipManager), nameof(AIShipManager.Update)),
                finalizer: new HarmonyMethod(typeof(FlightJournal), nameof(AiEnd)) { priority = Priority.Last });
            logListener = new LogListener();
            BepInEx.Logging.Logger.Listeners.Add(logListener);
            Application.logMessageReceivedThreaded += UnityLog;
            active = true;
            Capture("baseline");
        } catch { Stop(false); throw; }
    }
    internal static void Stop(bool finalSnapshot = true) {
        try { if (active && finalSnapshot) Capture("final"); }
        finally {
            active = false; pendingSettled = pendingFollowup = false;
            harmony.UnpatchSelf();
            Application.logMessageReceivedThreaded -= UnityLog;
            if (logListener != null) { BepInEx.Logging.Logger.Listeners.Remove(logListener); logListener = null; }
            session?.Complete();
        }
    }
    static bool Live() {
        if (!active) return false;
        if (session?.Error != null || session == null || !session.Accepting) { Stop(false); return false; }
        return true;
    }
    internal static void Capture(string kind, double skip = 0, object previousReport = null, Exception error = null, long transition = 0) {
        if (!Live() || capturing) return;
        capturing = true;
        long start = Stopwatch.GetTimestamp();
        try {
            var record = FlightJournalData.Capture(kind);
            record.Epoch = StarSystem.fEpoch; Volatile.Write(ref lastEpoch, record.Epoch);
            record.Utc = DateTime.UtcNow.ToString("O"); record.SkipSeconds = skip; record.Error = Clip(error?.ToString(), 4096);
            record.TransitionId = transition;
            record.NewSffReport = kind == "sff-world-after" && !ReferenceEquals(previousReport, global::Ostrabus.Orbital.SffSimulation.Snapshot());
            record.CaptureMs = (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
            session.Enqueue(record);
        } catch (Exception) { Interlocked.Increment(ref captureErrors); }
        finally { capturing = false; }
    }
    static void WorldBegin(double fTimeDelta, out Step __state) {
        __state = default;
        if (!Live() || !GUIFFWD.Active || fTimeDelta < 60) return;
        __state.Capture = true; __state.PreviousReport = global::Ostrabus.Orbital.SffSimulation.Snapshot();
        __state.Id = ++nextTransition;
        Capture("sff-world-before", fTimeDelta, transition: __state.Id);
    }
    static Exception WorldEnd(double fTimeDelta, Exception __exception, Step __state) {
        if (__state.Capture) Capture("sff-world-after", fTimeDelta, __state.PreviousReport, __exception, __state.Id);
        return __exception;
    }
    static void RegionBegin(out Step __state) {
        __state = default;
        if (Live()) { __state.Capture = true; __state.Id = ++nextTransition; Capture("atc-before", transition: __state.Id); }
    }
    static Exception RegionEnd(Exception __exception, Step __state) {
        if (Live() && __state.Capture) {
            Capture(__exception == null ? "atc-after" : "atc-failed", error: __exception, transition: __state.Id);
            pendingSettled = __exception == null; pendingTransition = __state.Id;
            pendingFollowup = pendingSettled; followupEpoch = StarSystem.fEpoch + 60;
        }
        return __exception;
    }
    static Exception AiEnd(Exception __exception) {
        if (Live() && pendingSettled) { pendingSettled = false; Capture("atc-populated", error: __exception, transition: pendingTransition); }
        if (Live() && pendingFollowup && StarSystem.fEpoch >= followupEpoch) {
            pendingFollowup = false; Capture("atc-follow-up", error: __exception, transition: pendingTransition);
        }
        return __exception;
    }
    static string Clip(string text, int limit) => text == null || text.Length <= limit ? text : text.Substring(0, limit);
    static void Log(string source, string level, string message, string stack = null) {
        if (!active) return;
        var writer = session;
        if (writer == null || !writer.Accepting) return;
        bool approximate = Thread.CurrentThread.ManagedThreadId != mainThread;
        writer.Enqueue(new FlightJournalData.Record { Kind = "log", Utc = DateTime.UtcNow.ToString("O"), Epoch = approximate ? Volatile.Read(ref lastEpoch) : StarSystem.fEpoch, EpochApproximate = approximate,
            Source = Clip(source, 128), Level = level, Message = Clip(message, 4096), Error = Clip(stack, 4096),
            LogTruncated = (message?.Length ?? 0) > 4096 || (stack?.Length ?? 0) > 4096 });
    }
    static void UnityLog(string message, string stack, LogType type) {
        if (type == LogType.Warning || type == LogType.Error || type == LogType.Exception || type == LogType.Assert) Log("Unity", type.ToString(), message, stack);
    }
    sealed class LogListener : ILogListener {
        public void LogEvent(object sender, LogEventArgs args) {
            if (args.Source?.SourceName == "Unity Log" || (args.Level & (LogLevel.Warning | LogLevel.Error | LogLevel.Fatal)) == 0) return;
            Log(args.Source?.SourceName, args.Level.ToString(), args.Data as string ?? "[Non-string payload]");
        }
        public void Dispose() { }
    }
    internal static object Snapshot() => session == null ? SnapshotJson.Map("active", false, "state", "No flight journal started.") : session.Summary(active, Interlocked.Read(ref captureErrors));
}
