using System.Diagnostics;
using System.Reflection;
using System.Runtime.Serialization;
using Ostrabus.Inspection;

int checks = 0;
void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
T Empty<T>() { return (T)FormatterServices.GetUninitializedObject(typeof(T)); }
void Field(string name, object value) { typeof(ExcursionInspection).GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, value); }
Dictionary<string, object> Json(object value) { return (Dictionary<string, object>)value; }
var ring = new ExcursionRing<int>(3);
Check(ring.Latest == 0 && !ring.TryRead(0, out _), "Empty ring has events");
for (int i = 1; i <= 5; i++) ring.Add(i * 2);
Check(ring.Latest == 5 && ring.Oldest == 3, "Ring bounds wrong");
Check(!ring.TryRead(2, out _) && ring.TryRead(3, out int v) && v == 6 && ring.TryRead(5, out v) && v == 10, "Ring overwrite corrupted retained events");
ring.Clear(); Check(ring.Latest == 0 && !ring.TryRead(3, out _), "Ring reset retained data");
var system = Empty<StarSystem>();
system.dictShips = new Dictionary<string, Ship>(); system.aBOs = new Dictionary<string, BodyOrbit>();
CrewSim.system = system; StarSystem.fEpoch = 1000;
var body = Empty<BodyOrbit>(); body.strName = "Venus"; body.dTimeCalcLast = 900; body.dVelCalcLast = 899; body.dXReal = .72; body.dVelX = .00001;
system.aBOs.Add("Venus", body);
Ship MakeShip(string id) {
    var ship = Empty<Ship>(); ship.strRegID = id; ship.publicName = id;
    var situation = Empty<ShipSitu>(); situation.strBOPORShip = "Venus"; situation.vPosx = .721; situation.vVelX = .00002;
    typeof(Ship).GetProperty("objSS")!.SetValue(ship, situation);
    system.dictShips.Add(id, ship); return ship;
}
var player = MakeShip("P"); var wreck = MakeShip("W");
ExcursionInspection.Reset();
Check(!ExcursionInspection.Begin("inactive", player).Active, "Inactive recorder captured");
Field("playerId", "P"); Field("threadId", Thread.CurrentThread.ManagedThreadId); Field("leaseUntil", Stopwatch.GetTimestamp() + 60 * Stopwatch.Frequency);
Check(!ExcursionInspection.Begin("other", wreck).Active, "Untracked actor captured");
Check(!Task.Run(() => ExcursionInspection.Begin("worker", player).Active).GetAwaiter().GetResult(), "Worker accessed live simulation");
var outer = ExcursionInspection.Begin("lock", player);
var nested = ExcursionInspection.Begin("lock-situation", null, player.objSS);
player.objSS.bOrbitLocked = true; player.objSS.vVelX += 1e-10;
ExcursionInspection.End(nested, null); ExcursionInspection.End(outer, null);
var reply = ExcursionInspection.Snapshot("W", 0, 50);
var snapshot = Json(reply.Body);
Check(reply.Status == 200 && (string)snapshot["playerShipId"] == "P" && (string)snapshot["watchedShipId"] == "W", "Snapshot pinning failed");
var events = (List<object>)snapshot["events"];
Check(events.Count == 1 && (long)snapshot["latestSequence"] == 1, "Nested transitions were duplicated or dropped");
var item = Json(events[0]);
Check((string)item["kind"] == "lock" && !(bool)Json(item["before"])["orbitLocked"] && (bool)Json(item["after"])["orbitLocked"], "Transition before/after lost");
Check(Math.Abs((double)item["velocityChangeMPerSecond"] - 14.95978707) < .00001, "Impulse conversion wrong");
var noChange = ExcursionInspection.Begin("noop", player); ExcursionInspection.End(noChange, null);
Check(((List<object>)Json(ExcursionInspection.Snapshot(null, 1, 50).Body)["events"]).Count == 0, "No-op or cursor replay emitted events");
Check(body.dTimeCalcLast == 900 && body.dVelCalcLast == 899 && body.dXReal == .72 && player.objSS.vPosx == .721, "Inspection advanced body/ship state");
var bodyJson = Json(Json(snapshot["bodies"])["Venus"]);
Check((double)bodyJson["positionCalculationEpoch"] == 900 && (double)bodyJson["velocityCalculationEpoch"] == 899, "Calculation timestamps omitted");
ExcursionInspection.Readout(player, "VREL ", "15 m/s");
ExcursionInspection.Readout(player, "VCRS ", "-3 m/s");
ExcursionInspection.Readout(player, "RNG ", "2 km");
var display = Json(Json(ExcursionInspection.Snapshot(null, 1, 50).Body)["navDisplay"]);
Check((string)display["vrelText"] == "15 m/s" && (string)display["vcrsText"] == "-3 m/s" && (double)display["vrelEpoch"] == 1000, "Readout strings/time lost");
ExcursionInspection.Readout(wreck, "VREL ", "wrong");
Check((string)Json(Json(ExcursionInspection.Snapshot(null, 1, 50).Body)["navDisplay"])["vrelText"] == "15 m/s", "Other ship display overwrote player");
ExcursionInspection.CaptureReadout(player);
ExcursionInspection.CaptureReadout(player);
var readoutEvents = (List<object>)Json(ExcursionInspection.Snapshot(null, 1, 50).Body)["events"];
Check(readoutEvents.Count == 1 && (string)Json(readoutEvents[0])["kind"] == "navDisplay", "Readout sampling/rate cap failed");
for (int i = 0; i < 260; i++) { var scope = ExcursionInspection.Begin("pulse", player); player.objSS.vVelX += 1e-12; ExcursionInspection.End(scope, null); }
var overrun = Json(ExcursionInspection.Snapshot(null, 0, 2).Body);
Check((bool)overrun["cursorOverrun"] && (long)overrun["overwritten"] == 6 && ((List<object>)overrun["events"]).Count == 2, "Bounded overrun/page reporting failed");
Check((long)overrun["nextSince"] == 8, "Page cursor skipped events");
Check((bool)Json(ExcursionInspection.Snapshot(null, 999, 2).Body)["cursorAhead"], "Ahead cursor not reported");
Field("leaseUntil", 0L); Check(!ExcursionInspection.Begin("expired", player).Active, "Expired lease still recording");
ExcursionInspection.Reset(); Check(!ExcursionInspection.Active && ((ExcursionRing<ExcursionInspection.Transition>)typeof(ExcursionInspection).GetField("Events", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!).Latest == 0, "Reset retained history");
foreach (var type in new[] { typeof(ExcursionInspection.Motion), typeof(ExcursionInspection.BodyState), typeof(ExcursionInspection.Transition) }) {
    Check(type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic).All(f => f.FieldType.IsValueType || f.FieldType == typeof(string)), "History retains reference objects");
}
Console.WriteLine($"Passed {checks} excursion checks against real game types on bundled Mono.");

namespace Ostrabus.Inspection {
    internal sealed class InspectionApi {
        internal sealed class Reply {
            internal int Status; internal object Body;
            internal Reply(int status, object body) { Status = status; Body = body; }
        }
    }
}
