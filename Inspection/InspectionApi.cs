using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ostranauts.Ships;
using UnityEngine.SceneManagement;

namespace Ostrabus.Inspection;

internal sealed class InspectionApi : IDisposable {
    const int MaxClients = 4;
    const int TimeoutMs = 2000;
    readonly TcpListener listener;
    readonly int port;
    readonly ConcurrentQueue<Request> queue = new ConcurrentQueue<Request>();
    readonly ConcurrentDictionary<TcpClient, byte> clients = new ConcurrentDictionary<TcpClient, byte>();
    readonly CancellationTokenSource stop = new CancellationTokenSource();
    int active, generation, pending;
    volatile bool disposed;
    StarSystem system;

    internal sealed class Reply {
        internal int Status;
        internal object Body;
        internal Reply(int status, object body) { Status = status; Body = body; }
    }

    sealed class Request {
        internal string Path;
        internal string ShipId;
        internal string CoId;
        internal int TraceSeconds;
        internal long Since;
        internal string Method, StationId, Confirm;
        internal int Offset, Limit, Generation;
        internal int MaxShips;
        internal bool DryRun, Force;
        internal double RadiusKm, MinDistanceKm;
        internal double? FuelTargetKg;
        internal long Deadline;
        internal readonly TaskCompletionSource<Reply> Completion = new TaskCompletionSource<Reply>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    internal InspectionApi(int port) {
        this.port = port;
        listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start(16);
        SceneManager.activeSceneChanged += SceneChanged;
        SceneManager.sceneUnloaded += SceneUnloaded;
        WeaponsSystem.ShipFired.AddListener(CombatEventInspection.ShipFired);
        DamageSystem.OnShipTookDamageEvent.AddListener(CombatEventInspection.ShipDamaged);
        _ = Task.Run(AcceptLoop);
    }

    static Reply Error(int status, string message) { return new Reply(status, SnapshotJson.Map("error", message)); }
    static bool TorchTraceControl(string path) => path == "/torch/trace/start" || path == "/torch/trace/stop";
    static Reply ControlTorchTrace(string path) {
        try {
            global::Ostrabus.Diagnostics.TorchTrace.SetEnabled(path.EndsWith("/start",StringComparison.Ordinal));
            return new Reply(200,global::Ostrabus.Diagnostics.TorchTrace.Snapshot());
        } catch (InvalidOperationException ex) { return Error(409,ex.Message); }
    }
    static long Now { get { return Stopwatch.GetTimestamp(); } }
    void SceneChanged(Scene oldScene, Scene newScene) { Invalidate(); }
    void SceneUnloaded(Scene scene) { Invalidate(); }
    void Invalidate() {
        global::Ostrabus.Optimizations.SocialsProfiling.Stop("Scene or simulation changed");
        Interlocked.Increment(ref generation);
        DockingInspection.Reset();
        InspectionEventHistory.Reset();
        AIBehaviorHistory.Reset();
        ExcursionInspection.Reset();
        PersonnelCombatInspection.Reset();
        while (Dequeue(out var request)) { request.Completion.TrySetResult(Error(503, "Scene or simulation changed; retry.")); }
    }

    bool Dequeue(out Request request) {
        if (!queue.TryDequeue(out request)) { return false; }
        Interlocked.Decrement(ref pending);
        return true;
    }

    async Task AcceptLoop() {
        while (!disposed) {
            TcpClient context;
            try { context = await listener.AcceptTcpClientAsync().ConfigureAwait(false); }
            catch (Exception ex) {
                if (!disposed) { InspectionLog.Add("InspectionApi", "Error", ex.Message, ex.StackTrace); DisposeTransport(); }
                return;
            }
            if (Interlocked.Increment(ref active) > MaxClients) {
                Interlocked.Decrement(ref active);
                try {
                    context.SendTimeout = 50;
                    byte[] bytes = Encoding.ASCII.GetBytes("HTTP/1.1 429 Too Many Requests\r\nConnection: close\r\nContent-Length: 0\r\n\r\n");
                    context.GetStream().Write(bytes, 0, bytes.Length);
                } catch (Exception) { }
                context.Close();
                continue;
            }
            clients.TryAdd(context, 0);
            if (disposed) { context.Close(); }
            _ = Serve(context);
        }
    }

    async Task Serve(TcpClient context) {
        try {
            Reply reply;
            context.NoDelay = true;
            var read = InspectionHttp.Read(context.GetStream(), port);
            if (!await WaitForIO(read).ConfigureAwait(false)) { return; }
            var http = await read.ConfigureAwait(false);
            // Reject browser cross-origin requests; this API is for local tooling.
            if (http.Error != null) { reply = Error(http.ErrorStatus, http.Error); }
            else if (http.Headers["Origin"] != null) { reply = Error(403, "Browser origins are not accepted."); }
            else if (MemoryInspection.IsControl(http.Url?.AbsolutePath.TrimEnd('/')) && http.HttpMethod != "POST") { reply = Error(405, "Memory diagnostic controls require POST."); }
            else if (TransitProfileInspection.IsControl(http.Url?.AbsolutePath.TrimEnd('/')) && http.HttpMethod != "POST") { reply = Error(405, "Transit profiling controls require POST."); }
            else if (SocialsProfileInspection.IsControl(http.Url?.AbsolutePath.TrimEnd('/')) && http.HttpMethod != "POST") { reply = Error(405, "Socials profiling controls require POST."); }
            else if (FlightJournal.IsControl(http.Url?.AbsolutePath.TrimEnd('/')) && http.HttpMethod != "POST") { reply = Error(405, "Flight journal controls require POST."); }
            else if (TorchTraceControl(http.Url?.AbsolutePath.TrimEnd('/')) && http.HttpMethod != "POST") { reply = Error(405, "Torch trace controls require POST."); }
            else if (http.Url?.AbsolutePath.TrimEnd('/') == global::Ostrabus.Micrometeoroids.MeteorTest.Route && http.HttpMethod != "POST") { reply = Error(405, "Micrometeoroid test strikes require POST."); }
            else if (PersonnelCombatRequest.IsControl(http.Url?.AbsolutePath.TrimEnd('/')) && http.HttpMethod != "POST") { reply = Error(405, "Personnel trace controls require POST."); }
            else if (ShipPruning.IsRoute(http.Url?.AbsolutePath.TrimEnd('/')) && http.HttpMethod != "POST") { reply = Error(405, "Ship pruning requires POST."); }
            else if (http.HttpMethod != "GET" && !(http.HttpMethod == "POST" && (http.Url?.AbsolutePath.TrimEnd('/') == "/derelicts/prune" || http.Url?.AbsolutePath.TrimEnd('/') == global::Ostrabus.Micrometeoroids.MeteorTest.Route || MemoryInspection.IsControl(http.Url?.AbsolutePath.TrimEnd('/')) || TransitProfileInspection.IsControl(http.Url?.AbsolutePath.TrimEnd('/')) || SocialsProfileInspection.IsControl(http.Url?.AbsolutePath.TrimEnd('/')) || ShipPruning.IsRoute(http.Url?.AbsolutePath.TrimEnd('/')) || ShipAIRecovery.IsRoute(http.Url?.AbsolutePath.TrimEnd('/')) || ShipRefueling.IsRoute(http.Url?.AbsolutePath.TrimEnd('/')) || HaulerRescue.IsRoute(http.Url?.AbsolutePath.TrimEnd('/')) || ShipArrival.IsRoute(http.Url?.AbsolutePath.TrimEnd('/')) || FreightRecovery.IsRoute(http.Url?.AbsolutePath.TrimEnd('/')) || PersonnelCombatRequest.IsControl(http.Url?.AbsolutePath.TrimEnd('/')) || FlightJournal.IsControl(http.Url?.AbsolutePath.TrimEnd('/')) || TorchTraceControl(http.Url?.AbsolutePath.TrimEnd('/'))))) { reply = Error(405, "GET is required except explicit pruning, recovery and diagnostic control POST routes."); }
            else if (http.Url.AbsolutePath.Length > 256) { reply = Error(400, "Path too long."); }
            else if (!ParsePage(http.QueryString["offset"], 0, http.Url.AbsolutePath.TrimEnd('/') == "/console" || http.Url.AbsolutePath.StartsWith("/economy/", StringComparison.Ordinal) ? 1048576 : 10000, out int offset) ||
                     !ParsePage(http.QueryString["limit"], 25, 50, out int limit) || limit == 0 ||
                     !ParseRadius(http.QueryString["radiusKm"], out double radius) ||
                     !ParseSince(http.QueryString["since"], out long since) ||
                     !ParseShipFilter(http.QueryString["shipId"], out string shipFilter)) { reply = Error(400, "offset: 0..10000; limit: 1..50; radiusKm: finite 0..100000; since: nonnegative integer; shipId: URL-encoded ID up to 64 characters."); }
            else if (http.Url.AbsolutePath.TrimEnd('/') == "/logs") { reply = new Reply(200, InspectionLog.Read(since, limit)); }
            else if (http.Url.AbsolutePath.TrimEnd('/') == "/memory") { reply = new Reply(200, MemoryDiagnostics.Read(since, limit)); }
            else if (http.Url.AbsolutePath.TrimEnd('/') == "/profiling/load") { reply = new Reply(200, global::Ostrabus.Optimizations.LoadProfiling.Snapshot()); }
            else if (http.Url.AbsolutePath.TrimEnd('/') == "/profiling/transit") { reply = new Reply(200, global::Ostrabus.Optimizations.TransitProfiling.Snapshot()); }
            else if (http.Url.AbsolutePath.TrimEnd('/') == "/profiling/socials") { reply = new Reply(200, global::Ostrabus.Optimizations.SocialsProfiling.Snapshot()); }
            else if (http.Url.AbsolutePath.TrimEnd('/') == "/flight") { reply = new Reply(200, FlightJournal.Snapshot()); }
            else if (http.Url.AbsolutePath.TrimEnd('/') == "/torch/trace") { reply = new Reply(200, global::Ostrabus.Diagnostics.TorchTrace.Snapshot()); }
            else if (http.Url.AbsolutePath.TrimEnd('/') == "/events/docking") { reply = new Reply(200, DockingInspection.Read(since, limit)); }
            else if (http.Url.AbsolutePath.TrimEnd('/') == "/events/ai") { reply = new Reply(200, InspectionEventHistory.Read(since, limit, shipFilter, "ai")); }
            else if (http.Url.AbsolutePath.TrimEnd('/') == "/events/combat") { reply = new Reply(200, InspectionEventHistory.Read(since, limit, shipFilter, "combat")); }
            else if (!ShipPruning.Parse(http, out string pruneId, out bool dryRun, out bool force, out string pruneError)) { reply = Error(400, pruneError); }
            else if (!ShipRefueling.Parse(http, out double? fuelTarget, out string fuelError)) { reply = Error(400, fuelError); }
            else if (!ParseDerelictRequest(http, out string stationId, out double minDistanceKm, out int maxShips, out string confirm, out string derelictError)) { reply = Error(400, derelictError); }
            else if (!PersonnelCombatRequest.Parse(http, out string coId, out int traceSeconds)) { reply = Error(400, "coId: URL-encoded ID up to 64 characters; seconds: integer 1..300 (default 120)."); }
            else {
                var request = new Request {
                    Path = http.Url.AbsolutePath.TrimEnd('/'), Method = http.HttpMethod, Offset = offset, Limit = limit, RadiusKm = radius,
                    StationId = stationId, MinDistanceKm = minDistanceKm, MaxShips = maxShips, Confirm = pruneId != null || FreightRecovery.IsRoute(http.Url.AbsolutePath.TrimEnd('/')) ? http.QueryString["confirm"] : confirm,
                    ShipId = pruneId ?? shipFilter, Since = since, DryRun = dryRun, Force = force,
                    CoId = coId, TraceSeconds = traceSeconds,
                    FuelTargetKg = fuelTarget,
                    Generation = Volatile.Read(ref generation), Deadline = Now + Stopwatch.Frequency * TimeoutMs / 1000
                };
                // Timed-out work can remain queued while Unity is stalled; bound that too.
                if (Interlocked.Increment(ref pending) > 16) {
                    Interlocked.Decrement(ref pending);
                    request.Completion.TrySetResult(Error(429, "Inspection queue is full."));
                } else {
                    queue.Enqueue(request);
                    using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop.Token)) {
                        var timeout = Task.Delay(TimeoutMs, deadline.Token);
                        if (await Task.WhenAny(request.Completion.Task, timeout).ConfigureAwait(false) != request.Completion.Task) {
                            request.Completion.TrySetResult(Error(504, "Main-thread inspection timed out."));
                        }
                        deadline.Cancel();
                    }
                }
                reply = await request.Completion.Task.ConfigureAwait(false);
            }
            // Body consists only of detached primitives/collections; no game objects.
            byte[] bytes = Encoding.UTF8.GetBytes(SnapshotJson.Encode(reply.Body));
            var write = InspectionHttp.Write(context.GetStream(), reply.Status, bytes);
            await WaitForIO(write).ConfigureAwait(false);
        } catch (Exception ex) {
            // Expected transport failures are quiet; internal failures remain inspectable.
            if (!disposed && !(ex is IOException) && !(ex is SocketException) && !(ex is ObjectDisposedException)) {
                InspectionLog.Add("InspectionApi", "Error", ex.Message, ex.StackTrace);
            }
        } finally {
            context.Close();
            clients.TryRemove(context, out _);
            Interlocked.Decrement(ref active);
        }
    }

    async Task<bool> WaitForIO(Task operation) {
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop.Token)) {
            if (await Task.WhenAny(operation, Task.Delay(TimeoutMs, deadline.Token)).ConfigureAwait(false) == operation) {
                deadline.Cancel();
                await operation.ConfigureAwait(false);
                return true;
            }
            // Closing the socket in Serve will complete the outstanding I/O with an error.
            _ = operation.ContinueWith(task => { var observed = task.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return false;
        }
    }

    static bool ParsePage(string text, int fallback, int max, out int value) {
        value = fallback;
        return text == null || (int.TryParse(text, out value) && value >= 0 && value <= max);
    }

    static bool ParseRadius(string text, out double value) {
        value = 500;
        return text == null || (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
            !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0 && value <= 100000);
    }

    static bool ParseSince(string text, out long value) {
        value = 0;
        return text == null || (long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= 0);
    }

    static bool ParseShipFilter(string text, out string value) {
        value = null;
        if (text == null) { return true; }
        try { value = Uri.UnescapeDataString(text); }
        catch (UriFormatException) { return false; }
        return value.Length > 0 && value.Length <= 64;
    }

    static bool ParseDerelictRequest(InspectionHttp http, out string stationId, out double minDistanceKm, out int maxShips, out string confirm, out string error) {
        stationId = confirm = error = null;
        minDistanceKm = 0;
        maxShips = 5;
        string path = http.Url.AbsolutePath.TrimEnd('/');
        if (ShipPruning.IsRoute(path) || FreightRecovery.IsRoute(path)) return true;
        bool preview = path == "/derelicts" && http.HttpMethod == "GET";
        bool prune = path == "/derelicts/prune" && http.HttpMethod == "POST";
        if (!preview && !prune) {
            if (http.Url.Query.Contains("stationId=") || http.Url.Query.Contains("minDistanceKm=") || http.Url.Query.Contains("confirm=") || http.Url.Query.Contains("maxShips=")) {
                error = "Derelict query parameters are only valid on /derelicts or POST /derelicts/prune.";
                return false;
            }
            return true;
        }
        string rawStationId = http.QueryString["stationId"];
        string rawDistance = http.QueryString["minDistanceKm"];
        if (rawStationId == null || rawDistance == null || rawStationId.Length == 0 || rawStationId.Length > 64 ||
            !double.TryParse(rawDistance, NumberStyles.Float, CultureInfo.InvariantCulture, out minDistanceKm) ||
            double.IsNaN(minDistanceKm) || double.IsInfinity(minDistanceKm) || minDistanceKm < 0) {
            error = "stationId and minDistanceKm are required; distance must be finite and nonnegative.";
            return false;
        }
        try { stationId = Uri.UnescapeDataString(rawStationId); }
        catch (UriFormatException) { error = "Invalid stationId."; return false; }
        if (stationId.Length == 0 || stationId.Length > 64) { error = "Invalid stationId."; return false; }
        if (http.QueryString["maxShips"] != null && (!int.TryParse(http.QueryString["maxShips"], out maxShips) || maxShips < 1 || maxShips > 10)) {
            error = "maxShips must be from 1 to 10.";
            return false;
        }
        confirm = http.QueryString["confirm"];
        if (prune && confirm != "PRUNE") { error = "POST requires confirm=PRUNE."; return false; }
        if (preview && confirm != null) { error = "The preview route does not accept confirm."; return false; }
        return true;
    }

    // Called only by the plugin's Unity Update. At most one query per frame.
    internal void Pump() {
        if (disposed) { return; }
        if (!ReferenceEquals(system, CrewSim.system)) { system = CrewSim.system; Invalidate(); }
        PersonnelCombatInspection.Tick();
        long start = Now;
        try { AIBehaviorHistory.Capture(StarSystem.fEpoch); }
        catch (Exception ex) { InspectionLog.Add("AIHistory", "Error", ex.Message, ex.StackTrace); }
        int discarded = 0;
        while (discarded++ < MaxClients && Dequeue(out var request)) {
            if (request.Completion.Task.IsCompleted) { continue; }
            if (request.Deadline <= Now) { request.Completion.TrySetResult(Error(504, "Expired before inspection.")); continue; }
            if (request.Generation != generation) { request.Completion.TrySetResult(Error(503, "Simulation changed; retry.")); continue; }
            try {
                bool ready = CrewSim.objInstance != null && CrewSim.objInstance.FinishedLoading && system?.dictShips != null;
                if (request.Path == "/health") {
                    request.Completion.TrySetResult(new Reply(200, SnapshotJson.Map("ready", ready, "generation", generation)));
                } else if (request.Path == "/console") {
                    request.Completion.TrySetResult(ConsoleInspection.Read(request.Offset));
                } else if (request.Path == "/memory/stop" && request.Method == "POST") {
                    request.Completion.TrySetResult(MemoryInspection.Control(request.Path));
                } else if (request.Path == "/profiling/transit/stop" && request.Method == "POST") {
                    request.Completion.TrySetResult(TransitProfileInspection.Control(request.Path));
                } else if (request.Path == "/profiling/socials/stop" && request.Method == "POST") {
                    request.Completion.TrySetResult(SocialsProfileInspection.Control(request.Path));
                } else if (request.Path == "/flight/stop" && request.Method == "POST") {
                    request.Completion.TrySetResult(FlightJournal.Control(request.Path));
                } else if (request.Path == "/torch/trace/stop" && request.Method == "POST") {
                    request.Completion.TrySetResult(ControlTorchTrace(request.Path));
                } else if (!ready) { request.Completion.TrySetResult(Error(503, "Game is not ready.")); }
                else {
                    InspectionApi.Reply reply;
                    if (request.Path == "/orbital") {
                        reply = new Reply(200, global::Ostrabus.Orbital.OrbitalSimulation.Snapshot());
                    } else if (FreightRecovery.IsRoute(request.Path)) {
                        reply = FreightRecovery.Run(request.Path,request.Method == "POST",request.Confirm);
                    } else if (request.Path == "/economy" || request.Path.StartsWith("/economy/", StringComparison.Ordinal)) {
                        reply = EconomyInspection.Query(request.Path, request.Offset, request.Limit, start);
                    } else if (FlightJournal.IsControl(request.Path) && request.Method == "POST") {
                        reply = FlightJournal.Control(request.Path);
                    } else if (TorchTraceControl(request.Path) && request.Method == "POST") {
                        reply = ControlTorchTrace(request.Path);
                    } else if (request.Path == "/torch/reactor") {
                        reply = new Reply(200,global::Ostrabus.Diagnostics.TorchTrace.Inspect(request.ShipId));
                    } else if (MemoryInspection.IsControl(request.Path) && request.Method == "POST") {
                        reply = MemoryInspection.Control(request.Path);
                    } else if (TransitProfileInspection.IsControl(request.Path) && request.Method == "POST") {
                        reply = TransitProfileInspection.Control(request.Path);
                    } else if (SocialsProfileInspection.IsControl(request.Path) && request.Method == "POST") {
                        reply = SocialsProfileInspection.Control(request.Path);
                    } else if (request.Path == global::Ostrabus.Micrometeoroids.MeteorTest.Route && request.Method == "POST") {
                        reply = MicrometeoroidInspection.Strike();
                    } else if (request.Path == "/combat/personnel/trace/start") {
                        reply = PersonnelCombatInspection.Start(request.CoId, request.TraceSeconds);
                    } else if (request.Path == "/combat/personnel/trace/stop") {
                        PersonnelCombatInspection.Stop();
                        reply = new Reply(200, PersonnelCombatInspection.Read(request.Since, request.Limit));
                    } else if (request.Path == "/combat/personnel/trace") {
                        reply = new Reply(200, PersonnelCombatInspection.Read(request.Since, request.Limit));
                    } else if (request.Path == "/workorders/trace") {
                        reply = new Reply(200, global::Ostrabus.Diagnostics.WorkTrace.Snapshot());
                    } else if (request.Path == "/workorders/scheduler") {
                        reply = new Reply(200, WorkSchedulerInspection.Snapshot());
                    } else if (request.Path == "/workorders") {
                        reply = new Reply(200, global::Ostrabus.AlternativeWork.AlternativeWorkSystem.Snapshot());
                    } else if (request.Path == "/excursion") {
                        reply = ExcursionInspection.Snapshot(request.ShipId, request.Since, request.Limit);
                    } else if (request.Path == "/derelicts" && request.Method == "GET") {
                        reply = DerelictPruning.Preview(request.StationId, request.MinDistanceKm, request.Offset, request.Limit, start);
                    } else if (request.Path == "/derelicts/prune" && request.Method == "POST") {
                        reply = DerelictPruning.Prune(request.StationId, request.MinDistanceKm, request.MaxShips, request.Confirm, start);
                    } else if (ShipArrival.IsRoute(request.Path)) {
                        reply = ShipArrival.Run(request.Path, request.Method == "POST");
                    } else if (HaulerRescue.IsRoute(request.Path)) {
                        reply = HaulerRescue.Run(request.Path, request.Method == "POST");
                    } else if (ShipAIRecovery.IsRoute(request.Path)) {
                        reply = ShipAIRecovery.Run(request.Path, request.Method == "POST");
                    } else if (ShipRefueling.IsRoute(request.Path)) {
                        reply = ShipRefueling.Run(request.Path, request.Method == "POST", request.FuelTargetKg);
                    } else if (ShipPruning.IsRoute(request.Path) && request.Method == "POST") {
                        reply = ShipPruning.Run(request.ShipId, request.DryRun, request.Confirm, request.Force);
                    } else {
                        reply = InspectionSnapshots.Query(request.Path, request.Offset, request.Limit, start, request.RadiusKm);
                    }
                    request.Completion.TrySetResult(new Reply(reply.Status, SnapshotJson.Map(
                        "generation", generation, "epoch", StarSystem.fEpoch,
                        "capturedUtc", DateTime.UtcNow.ToString("O"),
                        "inspectionMs", (Now - start) * 1000d / Stopwatch.Frequency, "data", reply.Body)));
                }
            } catch (Exception ex) {
                InspectionLog.Add("InspectionApi", "Error", ex.Message, ex.StackTrace);
                request.Completion.TrySetResult(Error(500, "Inspection failed: " + ex.GetType().Name));
            }
            break;
        }
    }

    void DisposeTransport() {
        disposed = true;
        stop.Cancel();
        listener.Stop();
        foreach (var context in clients.Keys) { context.Close(); }
        while (Dequeue(out var request)) { request.Completion.TrySetResult(Error(503, "API stopped.")); }
    }

    public void Dispose() {
        global::Ostrabus.Diagnostics.TorchTrace.Stop("inspection stopped");
        FlightJournal.Stop(false);
        global::Ostrabus.Optimizations.TransitProfiling.Stop();
        global::Ostrabus.Optimizations.SocialsProfiling.Stop();
        MemoryDiagnostics.Stop();
        ExcursionInspection.Reset();
        PersonnelCombatInspection.Reset();
        SceneManager.activeSceneChanged -= SceneChanged;
        SceneManager.sceneUnloaded -= SceneUnloaded;
        WeaponsSystem.ShipFired.RemoveListener(CombatEventInspection.ShipFired);
        DamageSystem.OnShipTookDamageEvent.RemoveListener(CombatEventInspection.ShipDamaged);
        DisposeTransport();
    }
}
