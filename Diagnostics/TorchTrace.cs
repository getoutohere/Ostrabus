using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ostrabus.Inspection;
using Ostranauts.ShipGUIs.Utilities;
using UnityEngine.SceneManagement;

namespace Ostrabus.Diagnostics;

// No HarmonyPatch attribute: these observers exist only during an explicit trace.
internal static class TorchTrace {
    internal const string Owner = "ostrabus.diagnostics.torch";
    const int PoolSize = 32;
    static readonly Harmony harmony = new Harmony(Owner);
    static Session session;
    static volatile bool enabled;
    [ThreadStatic] static Frame currentRun, currentNav, currentFuel;
    internal static bool Enabled => enabled;

    internal struct Observation {
        public string Kind, Error;
        public double Epoch, ElapsedArgument, RequestedFuelBudget, AppliedFuelBudget, LastThrustCommandN, ObserverMs, CallWallMs;
        public double D2OChange, He3Change;
        public int ThrustCommands;
        public bool AppliedCaptured, FuelArgumentCaptured, FuelDeltaComplete, CallObserved;
        public TorchTraceData.State Before, Applied, After;
    }
    sealed class Frame {
        internal readonly FlightJournalData.Record Envelope = new FlightJournalData.Record();
        public Observation Observation;
        public TorchTraceData.Details Details;
    }
    sealed class Session {
        internal readonly ConcurrentStack<Frame> Pool = new ConcurrentStack<Frame>();
        internal readonly object Gate = new object();
        internal FlightJournalWriter Writer;
        internal Observation Latest;
        internal TorchTraceData.Details Deep;
        internal int Thread, World;
        internal string ShipId, Reason = "recording";
        internal long Errors, Exhausted;
        internal readonly long[] Next = new long[3];
        internal readonly double[] Epoch = new double[3];
        internal readonly int[] Plan = new int[3];
        internal readonly bool[] Torching = new bool[3];
        internal long NextDeep;
        internal Session(string path) {
            Thread = System.Threading.Thread.CurrentThread.ManagedThreadId;
            World = Identity(CrewSim.system);
            for (int i = 0; i < PoolSize; i++) {
                var frame = new Frame(); frame.Envelope.Diagnostic = frame; Pool.Push(frame);
            }
            Writer = new FlightJournalWriter(path, releaseRecord: Release, format:"ostrabus-torch-trace",
                units:"AU, seconds, N, G, MeV; tank quantities retain native condition units; raw/cache fields are explicitly named");
        }
        internal void Release(FlightJournalData.Record record) {
            var frame = (Frame)record.Diagnostic;
            frame.Observation = default; frame.Details = null;
            record.Utc = record.Kind = record.Error = record.PlayerShipId = null;
            Pool.Push(frame);
        }
    }
    internal struct Scope {
        internal object Frame, Session, Previous;
        internal Ship Ship;
        internal CondOwner Core;
        internal FusionIC Reactor;
        internal NavData Nav;
        internal long Started;
    }
    static int Identity(object value) => value == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
    static FusionIC Component(CondOwner core) => core.GetComponent<FusionIC>();

    internal static void SetEnabled(bool value) { if (value) Start(); else Stop(); }
    internal static void Start(string path = null) {
        if (enabled) return;
        if (session?.Writer != null && !session.Writer.Finished) throw new InvalidOperationException("Previous torch trace is still draining.");
        path = path ?? Path.Combine(BepInEx.Paths.BepInExRootPath,"TorchTraces","torch-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".jsonl");
        session = new Session(path);
        try {
            harmony.Patch(AccessTools.Method(typeof(FusionIC),"Run"),
                prefix: Hook(nameof(RunBegin),Priority.First), finalizer: Hook(nameof(RunEnd),Priority.Last));
            harmony.Patch(AccessTools.Method(typeof(FusionIC),"Fusion"), prefix: Hook(nameof(FusionBegin),Priority.Last));
            harmony.Patch(AccessTools.Method(typeof(NavData),nameof(NavData.TimeAdvance)),
                prefix: Hook(nameof(NavBegin),Priority.First), finalizer: Hook(nameof(NavEnd),Priority.Last));
            var fuel = AccessTools.Method(typeof(FusionIC),nameof(FusionIC.SetReactants));
            harmony.Patch(fuel, prefix: Hook(nameof(FuelBegin),Priority.First), finalizer: Hook(nameof(FuelEnd),Priority.Last));
            harmony.Patch(fuel, prefix: Hook(nameof(FuelApplied),Priority.Last));
            harmony.Patch(AccessTools.Method(typeof(Ship),nameof(Ship.SetThrust)), postfix: Hook(nameof(Thrust),Priority.Last));
            SceneManager.sceneUnloaded += SceneUnloaded;
            enabled = true;
            Mark("baseline");
        } catch { Stop("start failed"); throw; }
    }
    static HarmonyMethod Hook(string name, int priority) => new HarmonyMethod(typeof(TorchTrace),name) { priority = priority };
    internal static void Stop(string reason = "switched off") {
        if (enabled && reason == "switched off") Mark("final");
        enabled = false;
        harmony.UnpatchSelf();
        SceneManager.sceneUnloaded -= SceneUnloaded;
        currentRun = currentNav = currentFuel = null;
        if (session == null) return;
        session.Reason = reason;
        session.Writer.Complete();
    }
    static void SceneUnloaded(Scene scene) => Stop("scene unloaded");
    static void Mark(string kind) {
        var owner = session;
        Ship ship = null;
        if (owner?.ShipId != null) CrewSim.system?.dictShips?.TryGetValue(owner.ShipId,out ship);
        else ship = CrewSim.coPlayer?.ship;
        var core = TorchTraceData.RawCore(ship);
        if (owner == null || core == null) return;
        long next = owner.Next[0]; double epoch = owner.Epoch[0]; int plan = owner.Plan[0]; bool torching = owner.Torching[0];
        owner.Next[0] = 0;
        var scope = Begin(0,ship,core);
        owner.Next[0] = next; owner.Epoch[0] = epoch; owner.Plan[0] = plan; owner.Torching[0] = torching;
        if (scope.Frame is Frame frame) {
            frame.Observation.Kind = kind; frame.Observation.CallObserved = false;
            long start = Stopwatch.GetTimestamp();
            try { frame.Details = frame.Details ?? TorchTraceData.Deep(ship,core,scope.Reactor); }
            catch { owner.Errors++; }
            frame.Observation.ObserverMs += (Stopwatch.GetTimestamp()-start)*1000d/Stopwatch.Frequency;
        }
        Finish(scope,null);
    }
    static bool Live() {
        if (!enabled || session == null || session.Thread != Thread.CurrentThread.ManagedThreadId) return false;
        if (!session.Writer.Accepting || session.Writer.Error != null) { Stop("writer stopped: " + session.Writer.Error); return false; }
        if (session.World != Identity(CrewSim.system)) { Stop("simulation changed"); return false; }
        return true;
    }
    static Scope Begin(int kind, Ship ship, CondOwner core, FusionIC reactor = null, NavData nav = null, double argument = 0) {
        var scope = default(Scope);
        if (!Live() || ship == null || ship.bDestroyed || core == null || ship.LoadState != Ship.Loaded.Full || ship.objSS == null) return scope;
        if (session.ShipId == null) {
            if (!ReferenceEquals(CrewSim.coPlayer?.ship,ship) || ship.IsAIShip) return scope;
            session.ShipId = ship.strRegID;
        }
        if (ship.strRegID != session.ShipId || CrewSim.system?.dictShips == null ||
            !CrewSim.system.dictShips.TryGetValue(ship.strRegID,out var actual) || !ReferenceEquals(actual,ship)) return scope;
        long now = Stopwatch.GetTimestamp();
        int plan = Identity(nav ?? ship.objSS.NavData);
        bool torching = (nav ?? ship.objSS.NavData)?.IsTorching == true;
        bool transition = plan != session.Plan[kind] || torching != session.Torching[kind] ||
            Math.Abs(StarSystem.fEpoch-session.Epoch[kind]) >= 60;
        if (now < session.Next[kind] && !transition) return scope;
        session.Next[kind] = now + Stopwatch.Frequency/4;
        session.Epoch[kind] = StarSystem.fEpoch; session.Plan[kind] = plan; session.Torching[kind] = torching;
        if (!session.Pool.TryPop(out var frame)) { session.Exhausted++; return scope; }
        scope = new Scope { Frame = frame, Session = session, Ship = ship, Core = core, Reactor = reactor, Nav = nav, Started = now };
        try {
            if (reactor == null) scope.Reactor = Component(core);
            frame.Observation = new Observation { Kind = kind == 0 ? "reactor-run" : kind == 1 ? "course-playback" : "fuel-writeback",
                CallObserved = true,
                Epoch = StarSystem.fEpoch, ElapsedArgument = kind == 0 ? argument : 0,
                RequestedFuelBudget = kind == 2 ? argument : double.NaN, AppliedFuelBudget = double.NaN,
                Before = TorchTraceData.Read(ship,core,scope.Reactor,nav) };
            if (now >= session.NextDeep) {
                frame.Details = TorchTraceData.Deep(ship,core,scope.Reactor);
                session.NextDeep = now + Stopwatch.Frequency;
            }
            frame.Observation.ObserverMs = (Stopwatch.GetTimestamp()-now)*1000d/Stopwatch.Frequency;
            return scope;
        } catch {
            session.Errors++; session.Release(frame.Envelope); return default;
        }
    }
    static void Finish(Scope scope, Exception error) {
        if (!(scope.Frame is Frame frame) || !(scope.Session is Session owner)) return;
        bool handedOff = false;
        try {
            if (!enabled || !ReferenceEquals(session,owner)) { owner.Release(frame.Envelope); return; }
            long start = Stopwatch.GetTimestamp();
            if (scope.Ship != null && !scope.Ship.bDestroyed && scope.Ship.objSS != null)
                frame.Observation.After = TorchTraceData.Read(scope.Ship,scope.Core,scope.Reactor,scope.Nav);
            if (frame.Details == null && (frame.Observation.Before.PelletCapacity != frame.Observation.After.PelletCapacity ||
                frame.Observation.Before.TheoryCapacity != frame.Observation.After.TheoryCapacity || frame.Observation.Before.Running != frame.Observation.After.Running))
                frame.Details = TorchTraceData.Deep(scope.Ship,scope.Core,scope.Reactor);
            string message = error?.Message;
            frame.Observation.Error = error == null ? null : error.GetType().Name + ": " +
                (message == null || message.Length <= 1024 ? message : message.Substring(0,1024));
            frame.Observation.FuelDeltaComplete = frame.Observation.Before.FuelScanAvailable && frame.Observation.After.FuelScanAvailable &&
                !frame.Observation.Before.FuelScanTruncated && !frame.Observation.After.FuelScanTruncated;
            frame.Observation.D2OChange = frame.Observation.Before.D2OTotal-frame.Observation.After.D2OTotal;
            frame.Observation.He3Change = frame.Observation.Before.He3Total-frame.Observation.After.He3Total;
            frame.Observation.CallWallMs = (start-scope.Started)*1000d/Stopwatch.Frequency;
            frame.Observation.ObserverMs += (Stopwatch.GetTimestamp()-start)*1000d/Stopwatch.Frequency;
            lock (owner.Gate) {
                owner.Latest = frame.Observation;
                if (frame.Details != null) owner.Deep = frame.Details;
            }
            var record = frame.Envelope;
            record.Kind = "torch-" + frame.Observation.Kind;
            record.Utc = DateTime.UtcNow.ToString("O"); record.Epoch = frame.Observation.Epoch;
            record.PlayerShipId = frame.Observation.Before.ShipId; record.CaptureMs = frame.Observation.ObserverMs;
            handedOff = true;
            owner.Writer.Enqueue(record); // Writer owns/releases the detached frame, including on a drop.
        } catch { owner.Errors++; if (!handedOff) owner.Release(frame.Envelope); }
    }
    static void RunBegin(FusionIC __instance, double fDeltaTime, out Scope __state) {
        var core = TorchTraceData.RawCore(__instance);
        __state = Begin(0,core?.ship,core,__instance,argument:fDeltaTime);
        __state.Previous = currentRun; currentRun = __state.Frame as Frame;
    }
    static Exception RunEnd(Exception __exception, Scope __state) {
        try { Finish(__state,__exception); } finally { currentRun = __state.Previous as Frame; }
        return __exception;
    }
    static void FusionBegin(FusionIC __instance) {
        try {
            var frame = currentRun; var core = TorchTraceData.RawCore(__instance);
            if (frame == null || core == null || TorchTraceData.RawId(core) != frame.Observation.Before.ReactorId) return;
            long start = Stopwatch.GetTimestamp();
            frame.Observation.Applied = TorchTraceData.Read(core.ship,core,__instance);
            frame.Observation.AppliedCaptured = true;
            frame.Observation.ObserverMs += (Stopwatch.GetTimestamp()-start)*1000d/Stopwatch.Frequency;
        } catch { if (session != null) session.Errors++; }
    }
    static void NavBegin(NavData __instance, out Scope __state) {
        var ship = TorchTraceData.CourseShip(__instance);
        __state = Begin(1,ship,TorchTraceData.RawCore(ship),nav:__instance);
        __state.Previous = currentNav; currentNav = __state.Frame as Frame;
    }
    static Exception NavEnd(Exception __exception, Scope __state) {
        try { Finish(__state,__exception); } finally { currentNav = __state.Previous as Frame; }
        return __exception;
    }
    static void FuelBegin(FusionIC __instance, double fShallowFusionRemain, out Scope __state) {
        var core = TorchTraceData.RawCore(__instance);
        __state = Begin(2,core?.ship,core,__instance,argument:fShallowFusionRemain);
        __state.Previous = currentFuel; currentFuel = __state.Frame as Frame;
    }
    static void FuelApplied(double fShallowFusionRemain) {
        if (currentFuel == null) return;
        currentFuel.Observation.AppliedFuelBudget = fShallowFusionRemain;
        currentFuel.Observation.FuelArgumentCaptured = true;
    }
    static Exception FuelEnd(Exception __exception, Scope __state) {
        try { Finish(__state,__exception); } finally { currentFuel = __state.Previous as Frame; }
        return __exception;
    }
    static void Thrust(Ship __instance, double fAmount) {
        var frame = currentRun ?? currentNav;
        if (frame == null || __instance?.strRegID != frame.Observation.Before.ShipId) return;
        frame.Observation.LastThrustCommandN = fAmount;
        frame.Observation.ThrustCommands++;
    }
    internal static object Snapshot() {
        var owner = session;
        if (owner == null) return SnapshotJson.Map("active",false,"state","No torch trace started.");
        Observation latest; TorchTraceData.Details details;
        lock (owner.Gate) { latest = owner.Latest; details = owner.Deep; }
        return SnapshotJson.Map("active",enabled,"shipId",owner.ShipId,"stopReason",owner.Reason,
            "writer",owner.Writer.Summary(enabled,owner.Errors),"poolDrops",owner.Exhausted,"latest",ApiValue(JToken.FromObject(latest)),
            "reactor",details == null ? null : ApiValue(JToken.FromObject(details)));
    }
    // The HTTP transport intentionally accepts primitives/maps/lists only.
    // Reflect over detached trace DTOs here, never extend it to game objects.
    static object ApiValue(JToken token) {
        if (token is JObject map) {
            var result = new Dictionary<string,object>();
            foreach (var pair in map) result[pair.Key] = ApiValue(pair.Value);
            return result;
        }
        if (token is JArray array) {
            var result = new List<object>();
            foreach (var item in array) result.Add(ApiValue(item));
            return result;
        }
        if (token is JValue value) return value.Value;
        return null;
    }
    internal static object Inspect(string id) {
        Ship ship = null;
        if (id != null) CrewSim.system?.dictShips?.TryGetValue(id,out ship);
        else if (session?.ShipId != null) CrewSim.system?.dictShips?.TryGetValue(session.ShipId,out ship);
        else ship = CrewSim.coPlayer?.ship;
        var core = TorchTraceData.RawCore(ship);
        if (ship == null || ship.bDestroyed || core == null) return SnapshotJson.Map("resolved",false,"shipId",id,"note","No materialized reactor found.");
        var reactor = Component(core);
        return SnapshotJson.Map("resolved",true,"state",ApiValue(JToken.FromObject(TorchTraceData.Read(ship,core,reactor))),
            "reactor",ApiValue(JToken.FromObject(TorchTraceData.Deep(ship,core,reactor))));
    }
}
