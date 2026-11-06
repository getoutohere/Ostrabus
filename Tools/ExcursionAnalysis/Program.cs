using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
if (args.Length == 1 && args[0] == "--self-test") { Analysis.SelfTest(); return; }
string input = args.Length > 0 ? args[0] : "EXCURSION-E-H6GR.jsonl";
string output = args.Length > 1 ? args[1] : "EXCURSION-ANALYSIS";
Directory.CreateDirectory(output);
var analysis = new Analysis();
int lineNumber = 0;
foreach (string line in File.ReadLines(input)) {
    lineNumber++;
    try { analysis.Read(JObject.Parse(line), lineNumber); }
    catch (Exception ex) { analysis.Errors.Add(new { Line = lineNumber, Error = ex.Message }); }
}
analysis.Finish(output, input, lineNumber);

sealed class Body {
    public string Id = "", Parent = "";
    public double X, Y, Vx, Vy, PositionEpoch, VelocityEpoch;
    public JsonBodyOrbitSave Elements = new();
    public static Body? Parse(JToken? j) {
        if (j == null || j.Type == JTokenType.Null) return null;
        return new Body { Id = U.S(j, "id"), Parent = U.S(j, "parentId"), X = U.D(j, "xAU"), Y = U.D(j, "yAU"), Vx = U.D(j, "vxAUPerSecond"), Vy = U.D(j, "vyAUPerSecond"),
            PositionEpoch = U.D(j, "positionCalculationEpoch"), VelocityEpoch = U.D(j, "velocityCalculationEpoch"),
            Elements = new JsonBodyOrbitSave { strName = U.S(j, "id"), fPerh = U.D(j, "periapsisAU"), fAph = U.D(j, "apoapsisAU"), fAxis1 = U.D(j, "majorAxisAU"), fAxis2 = U.D(j, "minorAxisAU"),
                fEcc = U.D(j, "eccentricity"), fAngle = U.D(j, "angleDegrees"), fPeriod = U.D(j, "periodSeconds"), fPeriodShift = U.D(j, "phaseSeconds"), fMass = U.D(j, "massKg"), fRadius = U.D(j, "radiusAU"), nOrbitDirection = (int)U.D(j, "direction") } };
    }
}
sealed class Motion {
    public string Id = "", Reference = "";
    public double Epoch, X, Y, Vx, Vy, OffsetX, OffsetY, Ax, Ay;
    public bool Orbit, Locked;
    public Body? Body;
    public static Motion? Parse(JToken? j) {
        if (j == null || j.Type == JTokenType.Null) return null;
        return new Motion { Id = U.S(j, "id"), Reference = U.S(j, "referenceBodyId"), Epoch = U.D(j, "captureEpoch"), X = U.D(j, "xAU"), Y = U.D(j, "yAU"),
            Vx = U.D(j, "vxAUPerSecond"), Vy = U.D(j, "vyAUPerSecond"), OffsetX = U.D(j, "bodyOffsetXAU"), OffsetY = U.D(j, "bodyOffsetYAU"), Orbit = U.B(j, "orbitLocked"), Locked = U.B(j, "bodyOrbitLocked"), Body = Body.Parse(j["referenceBody"]),
            Ax = U.D(j["rcsAcceleration"], "x") + U.D(j["inputAcceleration"], "x") + U.D(j["externalAcceleration"], "x"),
            Ay = U.D(j["rcsAcceleration"], "y") + U.D(j["inputAcceleration"], "y") + U.D(j["externalAcceleration"], "y") };
    }
}
sealed class Event {
    public int Generation, Session, Frame, Line;
    public long Sequence;
    public string Kind = "", Target = "", Label = "", VrelText = "", VcrsText = "";
    public Motion? Before, After;
    public double Vrel = double.NaN, VrelEpoch, VcrsEpoch;
    public double DeltaV => Before == null || After == null ? 0 : U.Mag(After.Vx - Before.Vx, After.Vy - Before.Vy) * U.Meters;
}
static class U {
    public const double Meters = 149597870700d;
    public static double D(JToken? j, string key, double fallback = 0) {
        var v = (j as JObject)?[key];
        if (v == null || v.Type == JTokenType.Null) return fallback;
        return double.TryParse(v.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double n) ? n : fallback;
    }
    public static string S(JToken? j, string key) { return (j as JObject)?[key]?.ToString() ?? ""; }
    public static bool B(JToken? j, string key) { return (bool?)(j as JObject)?[key] ?? false; }
    public static double Mag(double x, double y) { return Math.Sqrt(x * x + y * y); }
    public static void Json(string path, object value) { File.WriteAllText(path, JsonConvert.SerializeObject(value, Formatting.Indented)); }
}
sealed class Analysis {
    internal static void SelfTest() {
        if (U.D(JValue.CreateNull(),"value",17) != 17 || U.S(JValue.CreateNull(),"value") != "" || U.B(JValue.CreateNull(),"value")) throw new Exception("Null-token handling regression");
        if (Motion.Parse(JValue.CreateNull()) != null) throw new Exception("Missing motion parsed");
        var a = new Analysis();
        var row = JObject.Parse("{\"generation\":1,\"epoch\":100,\"data\":{\"session\":1,\"playerShipId\":\"P\",\"watchedShipId\":\"W\",\"ships\":[],\"bodies\":{},\"events\":[{\"sequence\":5,\"kind\":\"navDisplay\",\"after\":{\"id\":\"P\",\"captureEpoch\":100},\"navDisplay\":{\"targetId\":\"W\",\"vrelMPerSecond\":3}},{\"sequence\":6,\"kind\":\"UnlockFromOrbit\",\"navDisplay\":null,\"before\":{\"id\":\"P\",\"vxAUPerSecond\":1e-11},\"after\":{\"id\":\"P\",\"vxAUPerSecond\":2e-11}}]}}");
        a.Read(row,1); a.Read(row,2);
        if (a.events.Count != 2 || a.duplicates != 2 || a.gaps.Count != 1 || a.events[(1,1,5)].Kind != "navDisplay" || a.events[(1,1,6)].Kind != "UnlockFromOrbit") throw new Exception("Mixed event/null display/dedup/gap regression");
        if (Math.Abs(a.events[(1,1,6)].DeltaV-1.495978707) > 1e-9) throw new Exception("Velocity units regression");
        Console.WriteLine("Analyzer regression checks passed: null tokens, mixed events, deduplication, gaps and AU/s conversion.");
    }
    public List<object> Errors = new();
    readonly List<object> transportErrors = new();
    readonly Dictionary<(int, int, long), Event> events = new();
    readonly Dictionary<(int, int), long> lastSequences = new();
    readonly Dictionary<string, Body> bodies = new();
    readonly List<object> gaps = new(), bodyErrors = new(), timeline = new(), fitChecks = new(), cacheChecks = new();
    readonly List<double> inspectionTimes = new();
    readonly List<(double Epoch, string Id, double Radius, double Speed)> orbitRows = new();
    int snapshots, duplicates, overrunReplies, paused, firstGeneration;
    double firstEpoch = double.NaN, lastEpoch;
    string firstUtc = "", lastUtc = "", player = "", watched = "";
    public void Read(JObject root, int line) {
        var data = root["data"];
        if (data == null) { transportErrors.Add(new { Line = line, Error = root.ToString(Formatting.None) }); return; }
        snapshots++;
        int gen = (int)U.D(root, "generation"), session = (int)U.D(data, "session");
        double epoch = U.D(root, "epoch");
        if (double.IsNaN(firstEpoch)) { firstEpoch = epoch; firstGeneration = gen; firstUtc = U.S(root, "capturedUtc"); }
        lastEpoch = epoch; lastUtc = U.S(root, "capturedUtc");
        player = U.S(data, "playerShipId"); watched = U.S(data, "watchedShipId");
        inspectionTimes.Add(U.D(root, "inspectionMs"));
        if (U.B(data, "paused")) paused++;
        if (U.B(data, "cursorOverrun")) overrunReplies++;
        if (data["bodies"] is JObject bs) foreach (var property in bs.Properties()) {
            var body = Body.Parse(property.Value);
            if (body != null) bodies[body.Id] = body;
        }
        foreach (var body in bodies.Values.Where(b => b.Id is "Venus" or "VORB" or "VORB|Aux").ToArray()) CheckBody(body, gen, session, epoch, line);
        var ships = data["ships"] as JArray;
        if (ships != null) foreach (var ship in ships) {
            Motion? m = Motion.Parse(ship["motion"]);
            if (m == null) continue;
            timeline.Add(new { Line = line, Generation = gen, Session = session, Epoch = epoch, Id = m.Id, m.Orbit, m.Locked, m.Reference, m.X, m.Y, m.Vx, m.Vy, m.Ax, m.Ay, Load = U.S(ship, "loadState") });
        }
        if (ships != null) {
            var pm = Motion.Parse(ships.FirstOrDefault(s=>U.S(s,"id")==player)?["motion"]);
            var tm = Motion.Parse(ships.FirstOrDefault(s=>U.S(s,"id")==watched)?["motion"]);
            if (pm?.Locked == true && tm?.Locked == true && pm.Body != null && tm.Body != null && pm.Reference == "VORB" && tm.Reference == "VORB|Aux") {
                var a = Model(pm.Body); var b = Model(tm.Body);
                a.UpdateTime(epoch); b.UpdateTime(epoch);
                cacheChecks.Add(new { Line=line, Epoch=epoch, GameSeconds=epoch-firstEpoch,
                    DisplayedVrel=U.D(data["navDisplay"],"vrelMPerSecond",double.NaN),
                    RawShipVrel=U.Mag(pm.Vx-tm.Vx,pm.Vy-tm.Vy)*U.Meters,
                    CachedBodyVrel=U.Mag(pm.Body.Vx-tm.Body.Vx,pm.Body.Vy-tm.Body.Vy)*U.Meters,
                    SynchronizedModelVrel=U.Mag(a.dVelX-b.dVelX,a.dVelY-b.dVelY)*U.Meters,
                    PlayerBodyVelocityEpoch=pm.Body.VelocityEpoch, TargetBodyVelocityEpoch=tm.Body.VelocityEpoch,
                    CacheEpochSeparationSeconds=Math.Abs(pm.Body.VelocityEpoch-tm.Body.VelocityEpoch),
                    PlayerVsOwnCachedBodyMPerSecond=U.Mag(pm.Vx-pm.Body.Vx,pm.Vy-pm.Body.Vy)*U.Meters,
                    TargetVsOwnCachedBodyMPerSecond=U.Mag(tm.Vx-tm.Body.Vx,tm.Vy-tm.Body.Vy)*U.Meters });
            }
        }
        foreach (var token in (data["events"] as JArray ?? new JArray())) {
            long seq = (long)U.D(token, "sequence");
            var key = (gen, session, seq);
            if (events.ContainsKey(key)) { duplicates++; continue; }
            var stream = (gen, session);
            long previous = lastSequences.GetValueOrDefault(stream);
            if (seq > previous + 1) gaps.Add(new { Generation = gen, Session = session, From = previous + 1, Through = seq - 1, Count = seq - previous - 1, Bootstrap = previous == 0, Line = line });
            lastSequences[stream] = Math.Max(previous, seq);
            var display = token["navDisplay"];
            var e = new Event { Generation = gen, Session = session, Sequence = seq, Frame = (int)U.D(token, "frame"), Line = line, Kind = U.S(token, "kind"), Before = Motion.Parse(token["before"]), After = Motion.Parse(token["after"]),
                Target = U.S(display, "targetId"), Label = U.S(display, "vrelLabel"), VrelText = U.S(display, "vrelText"), VcrsText = U.S(display, "vcrsText"), Vrel = U.D(display, "vrelMPerSecond", double.NaN), VrelEpoch = U.D(display, "vrelEpoch"), VcrsEpoch = U.D(display, "vcrsEpoch") };
            events.Add(key, e);
            if (e.Kind != "navDisplay" && e.After?.Orbit == true && e.Before != null && e.After.Body != null) CheckFit(e);
        }
    }
    void CheckFit(Event e) {
        try {
            var m = e.After!; var before = e.Before!; var model = Model(m.Body!);
            double low = m.Epoch - .05, high = m.Epoch + .05;
            model.UpdateTime(low,true,false); double lx = model.dXReal, ly = model.dYReal;
            model.UpdateTime(high,true,false);
            double instantX = (model.dXReal-lx)/(high-low), instantY = (model.dYReal-ly)/(high-low);
            model.UpdateTime(m.Epoch-1,true,false); double bx=model.dXReal, by=model.dYReal;
            model.UpdateTime(m.Epoch,true,false); double avgX=model.dXReal-bx, avgY=model.dYReal-by;
            fitChecks.Add(new { e.Sequence, Epoch=m.Epoch, ActualJumpMPerSecond=e.DeltaV,
                InstantaneousFitErrorMPerSecond=U.Mag(instantX-before.Vx,instantY-before.Vy)*U.Meters,
                BackwardAverageMatchesAfterErrorMPerSecond=U.Mag(avgX-m.Vx,avgY-m.Vy)*U.Meters,
                BackwardVersusInstantaneousMPerSecond=U.Mag(avgX-instantX,avgY-instantY)*U.Meters,
                JumpX=(m.Vx-before.Vx)*U.Meters,JumpY=(m.Vy-before.Vy)*U.Meters,
                BiasX=(avgX-instantX)*U.Meters,BiasY=(avgY-instantY)*U.Meters });
        } catch(Exception ex) { Errors.Add(new { Event=e.Sequence, Error=ex.Message }); }
    }
    BodyOrbit Model(Body source, HashSet<string>? path = null) {
        path ??= new();
        if (!path.Add(source.Id)) throw new Exception("Orbit parent cycle " + source.Id);
        var model = new BodyOrbit(source.Elements);
        if (!string.IsNullOrEmpty(source.Parent)) {
            if (!bodies.TryGetValue(source.Parent, out var parent)) throw new Exception("Missing parent " + source.Parent);
            model.boParent = Model(parent, path);
        }
        return model;
    }
    void CheckBody(Body body, int gen, int session, double epoch, int line) {
        try {
            var model = Model(body);
            model.UpdateTime(body.PositionEpoch, true, false);
            double positionError = U.Mag(model.dXReal - body.X, model.dYReal - body.Y) * U.Meters;
            model.UpdateTime(body.VelocityEpoch - 1, true, false);
            double x = model.dXReal, y = model.dYReal;
            model.UpdateTime(body.VelocityEpoch, true, false);
            double velocityError = U.Mag((model.dXReal - x) - body.Vx, (model.dYReal - y) - body.Vy) * U.Meters;
            bodyErrors.Add(new { Line = line, Generation = gen, Session = session, CaptureEpoch = epoch, Body = body.Id, PositionErrorM = positionError, VelocityErrorMPerSecond = velocityError,
                PositionAgeSeconds = epoch - body.PositionEpoch, VelocityAgeSeconds = epoch - body.VelocityEpoch });
            if (body.Id == "VORB" && bodies.TryGetValue("Venus", out var venus)) {
                var parent = Model(venus);
                parent.UpdateTime(body.PositionEpoch, true, false);
                var vm = Model(body); vm.UpdateTime(body.PositionEpoch, true, false);
                double radius = U.Mag(vm.dXReal - parent.dXReal, vm.dYReal - parent.dYReal) * U.Meters / 1000;
                parent.UpdateTime(body.VelocityEpoch - 1, true, false); double px = parent.dXReal, py = parent.dYReal;
                parent.UpdateTime(body.VelocityEpoch, true, false);
                double speed = U.Mag(body.Vx - (parent.dXReal - px), body.Vy - (parent.dYReal - py)) * U.Meters / 1000;
                orbitRows.Add((epoch, body.Id, radius, speed));
            }
        } catch (Exception ex) { Errors.Add(new { Line = line, Body = body.Id, Error = ex.Message }); }
    }
    public void Finish(string directory, string input, int lines) {
        var ordered = events.Values.OrderBy(e => e.Generation).ThenBy(e => e.Session).ThenBy(e => e.Sequence).ToArray();
        var transitions = ordered.Where(e => e.Kind != "navDisplay").ToArray();
        var displays = ordered.Where(e => e.Kind == "navDisplay" && double.IsFinite(e.Vrel) && e.After != null).ToArray();
        double origin = Math.Min(firstEpoch, ordered.Where(e=>e.After!=null).Select(e=>e.After!.Epoch).DefaultIfEmpty(firstEpoch).Min());
        var jumps = new List<object>();
        var csv = new List<string> { "generation,session,sequence,gameSeconds,vrelMPerSecond,vxMPerSecond,vyMPerSecond,accelerationMPerSecondSquared,orbitLocked,bodyLocked,referenceVelocityAgeSeconds,referenceBodyId" };
        for (int i = 0; i < displays.Length; i++) {
            var e = displays[i]; var m = e.After!;
            double age = m.Body == null ? double.NaN : m.Epoch - m.Body.VelocityEpoch;
            csv.Add(FormattableString.Invariant($"{e.Generation},{e.Session},{e.Sequence},{m.Epoch-origin:R},{e.Vrel:R},{m.Vx*U.Meters:R},{m.Vy*U.Meters:R},{U.Mag(m.Ax,m.Ay)*U.Meters:R},{(m.Orbit?1:0)},{(m.Locked?1:0)},{age:R},\"{m.Reference.Replace("\"","\"\"")}\""));
            if (i == 0) continue;
            var p = displays[i - 1];
            if (e.Generation != p.Generation || e.Session != p.Session || e.Target != p.Target || e.Label != p.Label) continue;
            double dt = m.Epoch - p.After!.Epoch;
            if (dt < 0) continue;
            var between = transitions.Where(t => t.Generation == e.Generation && t.Session == e.Session && t.Sequence > p.Sequence && t.Sequence <= e.Sequence).Select(t => t.Sequence).ToArray();
            jumps.Add(new { Sequence = e.Sequence, PreviousSequence = p.Sequence, Epoch = m.Epoch, GameSeconds = m.Epoch-origin, DeltaTimeSeconds = dt, VrelBefore = p.Vrel, VrelAfter = e.Vrel,
                VrelJump = e.Vrel-p.Vrel, AbsVrelJump = Math.Abs(e.Vrel-p.Vrel), PlayerDeltaVMPerSecond = U.Mag(m.Vx-p.After.Vx,m.Vy-p.After.Vy)*U.Meters,
                AccelerationMPerSecondSquared = U.Mag(m.Ax,m.Ay)*U.Meters, PreviousAccelerationMPerSecondSquared = U.Mag(p.After.Ax,p.After.Ay)*U.Meters,
                Target = e.Target, TransitionSequences = between, OwnReferenceVelocityAgeSeconds = age, m.Orbit, m.Locked, VcrsBefore = p.VcrsText, VcrsAfter = e.VcrsText });
        }
        double Number(object row,string name) { return (double)row.GetType().GetProperty(name)!.GetValue(row)!; }
        var topJumps = jumps.OrderByDescending(j => Number(j,"AbsVrelJump")).Take(30).ToArray();
        var largestTransitions = transitions.OrderByDescending(e => e.DeltaV).Take(30).ToArray();
        var summary = new {
            Input = Path.GetFullPath(input), Lines = lines, Snapshots = snapshots, FirstUtc = firstUtc, LastUtc = lastUtc,
            FirstEpoch = firstEpoch, LastEpoch = lastEpoch, PlotOriginEpoch = origin, GameMinutes = (lastEpoch-firstEpoch)/60, Player = player, Watched = watched,
            UniqueEvents = ordered.Length, DisplayEvents = displays.Length, OrbitEvents = transitions.Length, DuplicateEvents = duplicates,
            PausedSnapshots = paused, OverrunReplies = overrunReplies, Gaps = gaps, Errors = Errors, TransportErrors = transportErrors,
            InspectionMs = new { Median = inspectionTimes.Order().ElementAt(inspectionTimes.Count/2), Maximum = inspectionTimes.Max() },
            LargestTransitions = largestTransitions.Select(Describe), LargestReadoutJumps = topJumps,
            OrbitFitChecks = fitChecks,
            DockedCacheChecks = new { Count=cacheChecks.Count, MaximumSynchronizedModelVrel=cacheChecks.Count==0?0:cacheChecks.Max(r=>Number(r,"SynchronizedModelVrel")), MaximumRawShipVrel=cacheChecks.Count==0?0:cacheChecks.Max(r=>Number(r,"RawShipVrel")) },
            BodyChecks = new { Count = bodyErrors.Count, MaximumPositionErrorM = bodyErrors.Max(b => Number(b,"PositionErrorM")), MaximumVelocityErrorMPerSecond = bodyErrors.Max(b => Number(b,"VelocityErrorMPerSecond")), MaximumVelocityAgeSeconds = bodyErrors.Max(b => Number(b,"VelocityAgeSeconds")) },
            Orbit = new { StartRadiusKm = orbitRows.First().Radius, EndRadiusKm = orbitRows.Last().Radius, StartSpeedKmS = orbitRows.First().Speed, EndSpeedKmS = orbitRows.Last().Speed },
            Limitations = new[] { "Body validation uses captured elements and actual game orbit code offline, not an independent physical integrator.", "Each UI event includes player/reference-body state; target ship/body state is only present at polling cadence.", "Velocity-change hooks can include cache refreshes; capture epoch is not a ship integration timestamp.", "Readouts are sampled at up to 20 Hz wall time; missing event ranges are explicit. This log ends at the derelict, before the salvage stay/return." }
        };
        U.Json(Path.Combine(directory,"summary.json"),summary);
        U.Json(Path.Combine(directory,"transitions.json"),transitions.Select(Describe));
        U.Json(Path.Combine(directory,"readout-jumps.json"),topJumps);
        U.Json(Path.Combine(directory,"body-checks.json"),bodyErrors);
        U.Json(Path.Combine(directory,"timeline.json"),timeline);
        U.Json(Path.Combine(directory,"orbit-fit-checks.json"),fitChecks);
        U.Json(Path.Combine(directory,"docked-cache-checks.json"),cacheChecks);
        File.WriteAllLines(Path.Combine(directory,"readouts.csv"),csv);
        File.WriteAllLines(Path.Combine(directory,"vorb-orbit.csv"),new[]{"gameSeconds,radiusKm,speedKmPerSecond"}.Concat(orbitRows.Select(r=>FormattableString.Invariant($"{r.Epoch-origin:R},{r.Radius:R},{r.Speed:R}"))));
        File.WriteAllLines(Path.Combine(directory,"handoffs.csv"),new[]{"gameSeconds,deltaVMPerSecond,sequence"}.Concat(transitions.Where(e=>e.After?.Orbit==true).Select(e=>FormattableString.Invariant($"{e.After!.Epoch-origin:R},{e.DeltaV:R},{e.Sequence}"))));
        var contexts = new List<object>();
        foreach (var peak in largestTransitions.Take(8)) contexts.Add(new { CentreSequence = peak.Sequence, Events = ordered.Where(e=>e.Generation==peak.Generation && e.Session==peak.Session && Math.Abs(e.Sequence-peak.Sequence)<=4).Select(Describe) });
        U.Json(Path.Combine(directory,"event-windows.json"),contexts);
        Console.WriteLine(JsonConvert.SerializeObject(summary,Formatting.Indented));
    }
    static object Describe(Event e) {
        return new { e.Generation,e.Session,e.Sequence,e.Frame,e.Line,e.Kind,Epoch=e.After?.Epoch,DeltaTime=e.After?.Epoch-e.Before?.Epoch,DeltaVMPerSecond=e.DeltaV,
            Before=e.Before==null?null:new {e.Before.Vx,e.Before.Vy,e.Before.Orbit,e.Before.Locked,e.Before.Reference,BodyVelocityEpoch=e.Before.Body?.VelocityEpoch,BodyPositionEpoch=e.Before.Body?.PositionEpoch},
            After=e.After==null?null:new {e.After.Vx,e.After.Vy,e.After.Orbit,e.After.Locked,e.After.Reference,BodyVelocityEpoch=e.After.Body?.VelocityEpoch,BodyPositionEpoch=e.After.Body?.PositionEpoch},
            e.Target,e.Vrel,e.VrelText,e.VcrsText };
    }
}
