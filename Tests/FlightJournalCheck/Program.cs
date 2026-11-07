using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using BepInEx.Logging;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ostrabus.Inspection;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;
using Ostranauts.ShipGUIs.Utilities;

static class Program {
    static int checks, reports;
    static bool sff, fail, switchRegion = true;
    static readonly Exception nativeError = new InvalidOperationException("native-world-fixture");
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Field(object obj, string name, object value) => AccessTools.Field(obj.GetType(), name).SetValue(obj, value);
    static void Check(bool ok, string why) { checks++; if (!ok) throw new Exception(why); }
    static int Owned() => Harmony.GetAllPatchedMethods().Count(m => Harmony.GetPatchInfo(m).Owners.Contains(FlightJournal.Owner));
    static int LogListeners() => BepInEx.Logging.Logger.Listeners.Count(l => l.GetType().DeclaringType == typeof(FlightJournal));
    static int UnityListeners() => typeof(UnityEngine.Application).GetFields(BindingFlags.Static | BindingFlags.NonPublic)
        .Where(f => typeof(Delegate).IsAssignableFrom(f.FieldType)).Select(f => f.GetValue(null) as Delegate)
        .Where(d => d != null).Sum(d => d.GetInvocationList().Count(c => c.Method.DeclaringType == typeof(FlightJournal)));
    static bool Active(ref bool __result) { __result = sff; return false; }
    static bool World(double fTimeDelta) { if (fail) throw nativeError; StarSystem.fEpoch += fTimeDelta; return false; }
    static Exception OrbitalEnd(Exception __exception) { Ostrabus.Orbital.SffSimulation.Last = SnapshotJson.Map("fixtureReport", ++reports); return __exception; }
    static bool Region() { CrewSim.system.dictShips.Remove("OLD"); AIShipManager.dictAIs.Clear(); AIShipManager.strATCLast = "VORB"; return false; }
    static bool Ai() {
        if (switchRegion) { AccessTools.Method(typeof(AIShipManager), "RegionCleanup").Invoke(null, null); Hull("NEW"); switchRegion = false; }
        return false;
    }
    static bool Shader(ref int __result) { __result = 1; return false; }
    static bool Quiet() => false;
    static Ship Hull(string id) {
        var ship = Empty<Ship>(); ship.strRegID = id;
        var situation = Empty<ShipSitu>(); situation.vPosx = 1; situation.vPosy = 2; Field(situation, "_vVelX", 3d); Field(situation, "_vVelY", 4d);
        Field(ship, "<objSS>k__BackingField", situation); Field(ship, "aDocked", new Dictionary<string, Ship>());
        CrewSim.system.dictShips[id] = ship;
        return ship;
    }
    static void WorldSetup() {
        CrewSim.system = Empty<StarSystem>(); CrewSim.system.dictShips = new Dictionary<string, Ship>(); CrewSim.system.aBOs = new Dictionary<string, BodyOrbit>();
        DataHandler.mapCOs = new Dictionary<string, CondOwner>(); AIShipManager.dictAIs = new Dictionary<string, List<AIShip>>();
        AIShipManager.strATCLast = "OKLG"; CollisionManager.strATCClosest = "VORB";
        var player = Hull("PLAYER"); Hull("OLD"); CrewSim.coPlayer = Empty<CondOwner>(); Field(CrewSim.coPlayer, "_objShip", player);
        var target = Hull("TARGET"); player.TargetData = new TargetData(target); player.shipSituTarget = target.objSS;
        var course = Empty<NavData>(); Field(course, "_navPoints", new List<NavDataPoint>());
        course.AddNavDataPoint(new NavDataPoint(100, target.objSS, 50, 1, 100)); player.objSS.NavData = course;
        var ai = Empty<AIShip>(); Field(ai, "_shipUs", target); Field(ai, "<HomeStation>k__BackingField", "OKLG");
        AIShipManager.dictAIs["OKLG"] = new List<AIShip> { ai };
        var body = Empty<BodyOrbit>(); body.strName = "VENUS"; body.fMass = 1; body.dTimeCalcLast = 123;
        CrewSim.system.aBOs[body.strName] = body; StarSystem.fEpoch = 1000;
    }
    static void WaitForWriter() {
        var writer = (FlightJournalWriter)AccessTools.Field(typeof(FlightJournal), "session").GetValue(null);
        Check(writer.Completion.Wait(10000), "Writer failed to finish");
        Check(writer.Error == null, "Writer failed: " + writer.Error);
    }
    static JObject[] Read(string path) => File.ReadLines(path).Select(JObject.Parse).ToArray();
    static void Main() {
        var fixture = new Harmony("offline.flight.fixture"); var orbital = new Harmony(Ostrabus.PluginInfo.PLUGIN_GUID);
        string folder = Path.GetFullPath("obj/flight-journal-tests"); Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".jsonl");
        try {
            fixture.Patch(AccessTools.Method(typeof(UnityEngine.Shader), "PropertyToID", new[] { typeof(string) }), prefix: new HarmonyMethod(typeof(Program), nameof(Shader)));
            fixture.Patch(AccessTools.Method(typeof(UnityEngine.Application), "SetLogCallbackDefined"), prefix: new HarmonyMethod(typeof(Program), nameof(Quiet)));
            fixture.Patch(AccessTools.PropertyGetter(typeof(GUIFFWD), nameof(GUIFFWD.Active)), prefix: new HarmonyMethod(typeof(Program), nameof(Active)));
            fixture.Patch(AccessTools.Method(typeof(StarSystem), nameof(StarSystem.Update)), prefix: new HarmonyMethod(typeof(Program), nameof(World)));
            fixture.Patch(AccessTools.Method(typeof(AIShipManager), "RegionCleanup"), prefix: new HarmonyMethod(typeof(Program), nameof(Region)));
            fixture.Patch(AccessTools.Method(typeof(AIShipManager), nameof(AIShipManager.Update)), prefix: new HarmonyMethod(typeof(Program), nameof(Ai)));
            orbital.Patch(AccessTools.Method(typeof(StarSystem), nameof(StarSystem.Update)), finalizer: new HarmonyMethod(typeof(Program), nameof(OrbitalEnd)));
            Check(Owned() == 0 && !FlightJournal.Active, "Flight hooks installed at startup");
            Check(LogListeners() == 0 && UnityListeners() == 0, "Log observers installed before start");
            FlightJournal.Snapshot(); Check(Owned() == 0, "GET installed hooks");
            WorldSetup();
            var detached = FlightJournalData.Capture("fixture");
            Check(detached.Count == 3 && detached.PlayerCourse.Length == 1, "Baseline omitted ships/course");
            CrewSim.coPlayer.ship.objSS.vPosx = 99;
            Check(detached.Buffer[0].Motion.XAU == 1, "Snapshot retained live state");
            FlightJournalData.Release(detached); Check(detached.Buffer == null && detached.Count == 0, "Buffer was not released");
            Check(((System.Collections.IDictionary)AccessTools.Field(typeof(FlightJournalData), "pilots").GetValue(null)).Count == 0, "Capture retained AI references");
            FlightJournal.Start(file); Check(Owned() == 3 && FlightJournal.Active, "Start did not install exactly three hooks");
            Check(LogListeners() == 1 && UnityListeners() == 1, "Temporary log observers not installed exactly once");
            FlightJournal.Start(file); Check(Owned() == 3, "Repeated start duplicated hooks");
            sff = false; CrewSim.system.Update(1);
            sff = true; CrewSim.system.Update(3600);
            AIShipManager.Update();
            sff = false; CrewSim.system.Update(61); AIShipManager.Update(); sff = true;
            using (var log = new ManualLogSource("flight-fixture")) {
                BepInEx.Logging.Logger.Sources.Add(log); log.LogWarning("journal-warning"); BepInEx.Logging.Logger.Sources.Remove(log);
            }
            fail = true;
            Exception caught = null; try { CrewSim.system.Update(3600); } catch (Exception error) { caught = error; }
            Check(ReferenceEquals(caught, nativeError), "Diagnostic finalizer swallowed/replaced native exception"); fail = false;
            FlightJournal.Control("/flight/mark");
            FlightJournal.Stop(); Check(!FlightJournal.Active && Owned() == 0, "Stop left hooks installed");
            Check(LogListeners() == 0 && UnityListeners() == 0, "Stop retained log listeners");
            WaitForWriter();
            var rows = Read(file); Check((string)rows[0]["format"] == "ostrabus-flight-journal", "Missing header");
            Check((string)rows.Last()["kind"] == "journal-end", "Missing successful-close footer");
            var events = rows.Where(r => r["Kind"] != null).ToArray();
            Check((string)events.First()["Kind"] == "baseline" && (string)events.Last()["Kind"] == "final", "Session boundary missing");
            Check(events.Count(r => (string)r["Kind"] == "sff-world-before") == 2 && events.Count(r => (string)r["Kind"] == "sff-world-after") == 2, "SFF observation missing or recorded normal update");
            Check(events.Where(r => (string)r["Kind"] == "sff-world-after").All(r => (bool)r["NewSffReport"]), "SFF snapshot ran before orbital finalization");
            Check(events.Where(r => (string)r["Kind"] == "sff-world-after").Last()["Error"].Type == JTokenType.String, "Native error missing from journal");
            var before = events.Single(r => (string)r["Kind"] == "atc-before");
            var after = events.Single(r => (string)r["Kind"] == "atc-after");
            var populated = events.Single(r => (string)r["Kind"] == "atc-populated");
            Check(before["Ships"].Any(r => (string)r["Id"] == "OLD") && !after["Ships"].Any(r => (string)r["Id"] == "OLD"), "ATC pre-cleanup state was lost");
            Check(!after["Ships"].Any(r => (string)r["Id"] == "NEW") && populated["Ships"].Any(r => (string)r["Id"] == "NEW"), "Populated-region capture ran too early");
            Check((long)before["TransitionId"] == (long)populated["TransitionId"], "Transition correlation changed");
            Check(events.Count(r => (string)r["Kind"] == "atc-follow-up") == 1, "One-shot region follow-up was not recorded");
            Check(events.Any(r => (string)r["Kind"] == "log" && (string)r["Message"] == "journal-warning"), "Relevant warning was not captured");
            FlightJournal.Stop(); Check(Owned() == 0, "Repeated stop changed hooks");
            var snapshot = FlightJournalData.Capture("limit");
            FlightJournalData.Release(snapshot);
            for (int i = 0; i < FlightJournalData.MaxShips + 2; i++) Hull("many-" + i);
            snapshot = FlightJournalData.Capture("limit");
            Check(snapshot.Count == FlightJournalData.MaxShips && snapshot.ShipsTruncated && snapshot.Buffer[0].Id == "PLAYER", "Ship cap lost player or failed to report truncation");
            FlightJournalData.Release(snapshot);
            for (int i = 0; i < FlightJournalData.MaxBodies + 2; i++) {
                var body = Empty<BodyOrbit>(); body.strName = "placeholder-" + i; body.nDrawFlagsBody = 1; CrewSim.system.aBOs[body.strName] = body;
            }
            CrewSim.coPlayer.ship.objSS.strBOPORShip = "VENUS";
            snapshot = FlightJournalData.Capture("body-limit");
            Check(snapshot.Bodies.Length == FlightJournalData.MaxBodies && snapshot.BodiesTruncated && snapshot.Bodies[0].Id == "VENUS", "Body cap omitted player's reference body");
            FlightJournalData.Release(snapshot);
            WriterChecks(folder);
            string second = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".jsonl");
            FlightJournal.Start(second); FlightJournal.Stop(false); WaitForWriter(); Check(Owned() == 0, "Restart leaked hooks");
            string blocked = Path.Combine(folder, "blocked-" + Guid.NewGuid().ToString("N")); File.WriteAllText(blocked, "occupied");
            FlightJournal.Start(Path.Combine(blocked, "session.jsonl"));
            var broken = (FlightJournalWriter)AccessTools.Field(typeof(FlightJournal), "session").GetValue(null);
            Check(broken.Completion.Wait(10000) && broken.Error != null, "Journal disk failure fixture did not fail");
            sff = false; CrewSim.system.Update(1);
            Check(!FlightJournal.Active && Owned() == 0 && LogListeners() == 0 && UnityListeners() == 0, "Disk failure left observers installed");
            Console.WriteLine(checks + " flight journal capture/order/finalizer/writer/bounds/lifecycle checks passed.");
        } finally { FlightJournal.Stop(false); fixture.UnpatchSelf(); orbital.UnpatchSelf(); }
    }
    static void WriterChecks(string folder) {
        string path = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".jsonl");
        int releases = 0, releaseThread = 0;
        var writer = new FlightJournalWriter(path, releaseRecord: r => { Interlocked.Increment(ref releases); releaseThread = Thread.CurrentThread.ManagedThreadId; });
        writer.Enqueue(new FlightJournalData.Record { Kind = "writer-test", Message = "detached" }); writer.Complete();
        Check(writer.Completion.Wait(10000) && writer.Error == null && releases == 1, "Writer did not drain/release record");
        Check(releaseThread != Thread.CurrentThread.ManagedThreadId, "Writer ran on producer thread");
        Check(Read(path).Length == 3, "Writer JSONL/header/footer invalid"); writer.Complete();
        string collision = Path.Combine(folder, Guid.NewGuid().ToString("N")); File.WriteAllText(collision, "occupied");
        var failed = new FlightJournalWriter(Path.Combine(collision, "blocked.jsonl"));
        Check(failed.Completion.Wait(10000) && failed.Error != null && !failed.Accepting, "Disk error was not surfaced");
        string limited = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".jsonl");
        var cap = new FlightJournalWriter(limited, 4096); cap.Enqueue(new FlightJournalData.Record { Kind = "oversized", Message = new string('x', 10000) }); cap.Complete();
        Check(cap.Completion.Wait(10000) && cap.Error != null && new FileInfo(limited).Length <= 4096, "File-size bound failed");
        using (var entered = new ManualResetEventSlim()) using (var resume = new ManualResetEventSlim()) {
            int returned = 0;
            var bounded = new FlightJournalWriter(System.IO.Path.Combine(folder, Guid.NewGuid().ToString("N") + ".jsonl"), releaseRecord: r => {
                if (Interlocked.Increment(ref returned) == 1) { entered.Set(); resume.Wait(10000); }
            });
            bounded.Enqueue(new FlightJournalData.Record { Kind = "block-writer" });
            Check(entered.Wait(10000), "Writer did not reach backpressure fixture");
            try {
                for (int i = 0; i < FlightJournalWriter.QueueCapacity; i++) Check(bounded.Enqueue(new FlightJournalData.Record { Kind = "queued" }), "Queue filled too early");
                Check(!bounded.Enqueue(new FlightJournalData.Record { Kind = "overflow" }), "Queue bound failed");
            } finally { bounded.Complete(); resume.Set(); }
            Check(bounded.Completion.Wait(10000) && returned == FlightJournalWriter.QueueCapacity + 2, "Queue drop/drain retained records");
            Check((long)((Dictionary<string, object>)bounded.Summary(false, 0))["dropped"] == 1, "Dropped records were not reported");
        }
    }
}
namespace Ostrabus { internal static class PluginInfo { internal const string PLUGIN_GUID = "com.ostranauts.template"; } }
namespace Ostrabus.Orbital {
    internal static class SffSimulation { internal static object Last = new object(); internal static object Snapshot() => Last; }
    internal static class OrbitalSimulation { internal static object Snapshot() => SnapshotJson.Map("lastSff", SffSimulation.Snapshot()); }
}
namespace Ostrabus.Inspection {
    internal static class InspectionApi { internal sealed class Reply { internal int Status; internal object Body; internal Reply(int status, object body) { Status = status; Body = body; } } }
}
