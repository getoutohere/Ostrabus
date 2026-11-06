using System.Text.Json;
using System.Text.Json.Nodes;

if (args.Length == 1 && args[0] == "--self-test") { Drift.Tests(); return; }
if (args.Length > 0 && args[0] == "--exit-prediction") { Drift.ExitPrediction(args.Length > 1 ? Path.GetFullPath(args[1]) : Environment.CurrentDirectory,args.Length > 2 ? Path.GetFullPath(args[2]) : null); return; }
if (args.Length > 0 && args[0] == "--undocked-baseline") {
    Drift.UndockedBaseline(args.Length > 1 ? Path.GetFullPath(args[1]) : Environment.CurrentDirectory,
        args.Length > 2 ? double.Parse(args[2],System.Globalization.CultureInfo.InvariantCulture) : null); return;
}
if (args.Length > 0 && args[0] == "--coast-test") {
    string studyRoot=args.Length > 1 ? Path.GetFullPath(args[1]) : Environment.CurrentDirectory;
    Drift.NormalTest(studyRoot,args.Length > 2 ? double.Parse(args[2],System.Globalization.CultureInfo.InvariantCulture) : null,
        Path.Combine(studyRoot,"Orbital/LiveTest/COAST-16X")); return;
}
if (args.Length > 0 && args[0] == "--binding") { Drift.Binding(args.Length > 1 ? Path.GetFullPath(args[1]) : Environment.CurrentDirectory); return; }
if (args.Length > 0 && args[0] == "--sff-study") { Drift.SffStudy(args.Length > 1 ? Path.GetFullPath(args[1]) : Environment.CurrentDirectory); return; }
if (args.Length > 0 && args[0] == "--normal-test") { Drift.NormalTest(args.Length > 1 ? Path.GetFullPath(args[1]) : Environment.CurrentDirectory,
    args.Length > 2 ? double.Parse(args[2],System.Globalization.CultureInfo.InvariantCulture) : null); return; }
if (args.Length > 0 && args[0] == "--timestep-study") { Drift.TimestepStudy(args.Length > 1 ? Path.GetFullPath(args[1]) : Environment.CurrentDirectory); return; }
string root = args.Length > 0 ? Path.GetFullPath(args[0]) : Environment.CurrentDirectory;
Drift.Run(root);

readonly record struct V(double X, double Y) {
    public static V operator +(V a, V b) => new(a.X + b.X, a.Y + b.Y);
    public static V operator -(V a, V b) => new(a.X - b.X, a.Y - b.Y);
    public static V operator *(V a, double n) => new(a.X * n, a.Y * n);
    public double Length => Math.Sqrt(X * X + Y * Y);
}
readonly record struct State(V P, V V);
readonly record struct Ephemeris(V P, V V, V A);
sealed class Orbit {
    public const double AU = 149597870.7; // km
    public double Axis, Minor, Ecc, Angle, Period, Phase, Mass, Direction;
    public Orbit? Parent;
    public double Mu => 4 * Math.PI * Math.PI * Axis * Axis * Axis / (Period * Period);
    public static Orbit Parse(JsonNode j, Orbit? parent) => new() {
        Axis = Drift.D(j, "majorAxisAU") * AU / 2, Minor = Drift.D(j, "minorAxisAU") * AU / 2,
        Ecc = Drift.D(j, "eccentricity"), Angle = Drift.D(j, "angleDegrees") * Math.PI / 180,
        Period = Drift.D(j, "periodSeconds"), Phase = Drift.D(j, "phaseSeconds"), Mass = Drift.D(j, "massKg"),
        Direction = Drift.D(j, "direction"), Parent = parent
    };
    public Ephemeris At(double epoch, bool gameTolerance = false) {
        if (Axis == 0) return new(default, default, default);
        double mean = 2 * Math.PI * ((epoch + Phase) % Period) / Period;
        double e = Ecc < .8 ? mean : Math.PI;
        for (int i = 0; i < 30; i++) {
            double f = e - Ecc * Math.Sin(e) - mean;
            if (Math.Abs(f) < (gameTolerance ? 1e-6 : 1e-14)) break;
            e -= f / (1 - Ecc * Math.Cos(e));
        }
        double rate = 2 * Math.PI / Period / (1 - Ecc * Math.Cos(e));
        V Rotate(V p) => new(p.X * Math.Cos(Angle) - p.Y * Math.Sin(Angle), p.X * Math.Sin(Angle) + p.Y * Math.Cos(Angle));
        V p = Rotate(new(Axis * (Math.Cos(e) - Ecc), Minor * Direction * Math.Sin(e)));
        V v = Rotate(new(-Axis * Math.Sin(e) * rate, Minor * Direction * Math.Cos(e) * rate));
        V a = p * (-Mu / Math.Pow(p.Length, 3));
        var parent = Parent?.At(epoch, gameTolerance) ?? new Ephemeris(default, default, default);
        return new(parent.P + p, parent.V + v, parent.A + a);
    }
}
static partial class Drift {
    // Match the actual subnormal single-precision constant in StarSystem, not decimal 2e-44.
    static readonly double G = (double)(float)2e-44 * Math.Pow(Orbit.AU, 3); // km^3 kg^-1 s^-2
    internal static double D(JsonNode j, string key) => j[key]!.GetValue<double>();
    static V Position(JsonNode j) => new(D(j["positionAU"]!, "x") * Orbit.AU, D(j["positionAU"]!, "y") * Orbit.AU);
    static V Velocity(JsonNode j) => new(D(j["velocityAUPerSecond"]!, "x") * Orbit.AU, D(j["velocityAUPerSecond"]!, "y") * Orbit.AU);
    static V Gravity(V fromCenter, double mu) => fromCenter * (-mu / Math.Pow(fromCenter.Length, 3));
    static State Advance(State s, double t, double dt, Func<double, V, V> accel) {
        V a1 = accel(t, s.P);
        V p2 = s.P + s.V * (dt / 2), v2 = s.V + a1 * (dt / 2);
        V a2 = accel(t + dt / 2, p2);
        V p3 = s.P + v2 * (dt / 2), v3 = s.V + a2 * (dt / 2);
        V a3 = accel(t + dt / 2, p3);
        V p4 = s.P + v3 * dt, v4 = s.V + a3 * dt;
        V a4 = accel(t + dt, p4);
        return new(s.P + (s.V + v2 * 2 + v3 * 2 + v4) * (dt / 6), s.V + (a1 + a2 * 2 + a3 * 2 + a4) * (dt / 6));
    }
    static State Integrate(State start, double from, double to, double step, Func<double, V, V> accel, Action<double, State>? sample = null) {
        State state = start;
        double t = from, nextSample = from;
        while (t < to) {
            if (sample != null && t >= nextSample) { sample(t, state); nextSample += 30; }
            double dt = Math.Min(step, to - t);
            state = Advance(state, t, dt, accel); t += dt;
        }
        sample?.Invoke(to, state);
        return state;
    }
    internal static void Run(string root) {
        JsonNode Load(string file) => JsonNode.Parse(File.ReadAllText(Path.Combine(root, file)))!;
        JsonNode bodies = Load("VORB-DRIFT-ORBIT-CHECK.json")["data"]!["bodies"]!;
        Orbit sol = Orbit.Parse(bodies["Sol"]!, null), venus = Orbit.Parse(bodies["Venus"]!, sol), vorb = Orbit.Parse(bodies["VORB"]!, venus);
        JsonNode startJson = Load("VORB-DRIFT-INITIAL.json");
        JsonNode endJson = Load("VORB-DRIFT-M-5N4.json");
        double t0 = D(startJson, "epoch"), t1 = D(endJson, "epoch");
        var m0 = startJson["motion"]!;
        var m1 = endJson["data"]!["navigation"]!["currentMotion"]!;
        Ephemeris planet0 = venus.At(t0), planet1 = venus.At(t1), station0 = vorb.At(t0), station1 = vorb.At(t1);
        State observed0 = new(Position(m0) - planet0.P, Velocity(m0) - planet0.V);
        State observed1 = new(Position(m1) - planet1.P, Velocity(m1) - planet1.V);
        V relativeStation0 = station0.P - planet0.P, relativeStation1 = station1.P - planet1.P;
        V stationV0 = station0.V - planet0.V, stationV1 = station1.V - planet1.V;
        string output = Path.Combine(root, "VORB-DRIFT-ANALYSIS"); Directory.CreateDirectory(output);
        var cases = new List<object>();
        var trajectories = new List<string> { "model,elapsedSeconds,rangeKm,relativeSpeedMps,xRelativeVorbKm,yRelativeVorbKm" };
        Func<double, V, V> game = (t, r) => Gravity(r, G * venus.Mass) - venus.At(t).A;
        Func<double, V, V> coherent = (t, r) => Gravity(r, G * venus.Mass);
        Func<double, V, V> solar = (t, r) => {
            Ephemeris planet = venus.At(t);
            return Gravity(r, G * venus.Mass) + Gravity(planet.P + r - sol.At(t).P, G * sol.Mass) - planet.A;
        };
        State? gameEnd = null, coherentEnd = null;
        foreach (var model in new[] { ("strongest-body-only", game), ("consistent-venus-frame", coherent), ("sun-plus-venus", solar) }) {
            State coarse = Integrate(observed0, t0, t1, 1, model.Item2);
            State fine = Integrate(observed0, t0, t1, .25, model.Item2, (t, s) => {
                Ephemeris planet = venus.At(t), station = vorb.At(t);
                V dp = s.P - (station.P - planet.P), dv = s.V - (station.V - planet.V);
                trajectories.Add(FormattableString.Invariant($"{model.Item1},{t-t0:R},{dp.Length:R},{dv.Length*1000:R},{dp.X:R},{dp.Y:R}"));
            });
            if (model.Item1 == "strongest-body-only") gameEnd = fine;
            if (model.Item1 == "consistent-venus-frame") coherentEnd = fine;
            cases.Add(new { model = model.Item1, endRangeKm = (fine.P-relativeStation1).Length,
                endRelativeVelocityMps = (fine.V-stationV1).Length * 1000,
                endpointPositionErrorKm = (fine.P-observed1.P).Length, endpointVelocityErrorMps = (fine.V-observed1.V).Length * 1000,
                relativeXKm = fine.P.X-relativeStation1.X, relativeYKm = fine.P.Y-relativeStation1.Y,
                convergencePositionMeters = (fine.P-coarse.P).Length*1000, convergenceVelocityMps = (fine.V-coarse.V).Length*1000 });
        }
        V offset0 = observed0.P-relativeStation0;
        cases.Add(new { model = "fixed-offset-lock", endRangeKm = offset0.Length, endRelativeVelocityMps = 0d,
            endpointPositionErrorKm = (relativeStation1+offset0-observed1.P).Length,
            endpointVelocityErrorMps = (stationV1-observed1.V).Length*1000,
            caveat = "Constraint, not an independent free orbit; enforces zero relative velocity instead of preserving measured initial velocity." });
        // Isolate position offset alone: release from station velocity at the measured offset.
        State offsetOnlyStart = new(observed0.P, stationV0);
        State offsetOnly = Integrate(offsetOnlyStart, t0, t1, .25, coherent);
        cases.Add(new { model = "offset-only-consistent-frame", endRangeKm = (offsetOnly.P-relativeStation1).Length,
            endRelativeVelocityMps = (offsetOnly.V-stationV1).Length*1000,
            caveat = "Starts with exact station velocity; isolates drift from releasing the fixed-offset formation." });
        JsonNode watched = Load("VORB-DRIFT-ORBIT-CHECK.json")["data"]!["ships"]!.AsArray().First(x => x!["id"]!.GetValue<string>() == "M-5N4")!["motion"]!;
        V savedOffset = new(D(watched,"bodyOffsetXAU")*Orbit.AU,D(watched,"bodyOffsetYAU")*Orbit.AU);
        State savedOffsetStart = new(relativeStation0+savedOffset, stationV0);
        State savedOffsetEnd = Integrate(savedOffsetStart,t0,t1,.25,coherent);
        cases.Add(new { model = "saved-98km-lock-offset-release", initialRangeKm = savedOffset.Length,
            endRangeKm = (savedOffsetEnd.P-relativeStation1).Length,
            endRelativeVelocityMps = (savedOffsetEnd.V-stationV1).Length*1000,
            caveat = "Counterfactual release at baseline epoch using the wreck's retained lock offset and exact VORB velocity. Not a reconstruction of the unknown docking instant." });
        State stationPhysical = Integrate(new(relativeStation0, stationV0),t0,t1,.25,coherent);
        double maxPositionMismatch = 0;
        foreach (var pair in new[] { ("Venus", venus), ("VORB", vorb) }) {
            JsonNode j = bodies[pair.Item1]!;
            var predicted = pair.Item2.At(D(j,"positionCalculationEpoch"), true);
            maxPositionMismatch = Math.Max(maxPositionMismatch, (predicted.P-new V(D(j,"xAU")*Orbit.AU,D(j,"yAU")*Orbit.AU)).Length);
        }
        double solarAccel = (venus.At(t0).A*1000).Length;
        var report = new {
            ship = "M-5N4", initialEpoch = t0, finalEpoch = t1, elapsedSeconds = t1-t0,
            initialProvenance = startJson["provenance"]!.GetValue<string>(),
            observed = new { initialRangeKm = offset0.Length, initialRelativeSpeedMps = (observed0.V-stationV0).Length*1000,
                finalRangeKm = (observed1.P-relativeStation1).Length, finalRelativeSpeedMps = (observed1.V-stationV1).Length*1000,
                initialRelativeXKm = offset0.X, initialRelativeYKm = offset0.Y,
                finalRelativeXKm = observed1.P.X-relativeStation1.X, finalRelativeYKm = observed1.P.Y-relativeStation1.Y },
            mu = new { gravityVenusKm3PerS2 = G*venus.Mass, orbitVenusKm3PerS2 = vorb.Mu,
                gravitySunKm3PerS2 = G*sol.Mass, orbitSunKm3PerS2 = venus.Mu },
            validation = new { gameToleranceEphemerisPositionErrorMeters = maxPositionMismatch*1000,
                stationTwoBodyEndpointErrorMeters = (stationPhysical.P-relativeStation1).Length*1000,
                stationTwoBodyVelocityErrorMps = (stationPhysical.V-stationV1).Length*1000,
                integrator = "RK4 in Venus-relative double-precision km, ephemeris evaluated at every substage; 1s vs 0.25s convergence" },
            initialDifferentialVenusAccelerationMps2 = ((Gravity(observed0.P,G*venus.Mass)-Gravity(relativeStation0,G*venus.Mass))*1000).Length,
            omittedParentAccelerationMps2 = solarAccel,
            strongestVsCoherentPositionVectorDifferenceKm = (gameEnd!.Value.P-coherentEnd!.Value.P).Length,
            strongestVsCoherentVelocityVectorDifferenceMps = (gameEnd.Value.V-coherentEnd.Value.V).Length*1000,
            cases,
            limits = new[] { "Initial state precedes salvage work, not necessarily the exact docking instant.",
                "No continuous control/collision/docking history was recorded over this interval. Endpoint agreement cannot rule out all intervening events.",
                "This isolates force laws with converged integration; it does not reproduce Unity's variable timesteps, docking-group anchor changes or float gravity rounding.",
                "Ship coordinates are stored values without their own integration timestamps. Assigning the API epoch to the initial/final ship state is an approximation; source and target cached positions can lag differently.",
                "Parent and station are evaluated at the same epoch rather than comparing cached velocities." }
        };
        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(output,"summary.json"), json);
        File.WriteAllLines(Path.Combine(output,"trajectories.csv"), trajectories);
        Console.WriteLine(json);
    }
    internal static void Tests() {
        const double r = 10000, mu = 320000;
        double speed = Math.Sqrt(mu/r), period = 2*Math.PI*Math.Sqrt(r*r*r/mu);
        var orbit = new Orbit { Axis=r, Minor=r, Direction=1, Period=period };
        Ephemeris p = orbit.At(period/4);
        if (Math.Abs(p.P.X)>1e-8 || Math.Abs(p.P.Y-r)>1e-8 || Math.Abs(p.V.Length-speed)>1e-10) throw new Exception("Circular ephemeris failed");
        State initial = new(new(r,0),new(0,speed));
        State integrated=Integrate(initial,0,period,.5,(_,pos)=>Gravity(pos,mu));
        if ((integrated.P-initial.P).Length>1e-7 || (integrated.V-initial.V).Length>1e-10) throw new Exception("Circular integration failed");
        var elliptic = new Orbit { Axis=12000, Minor=12000*Math.Sqrt(1-.45*.45), Ecc=.45, Direction=1, Period=15000 };
        var e0=elliptic.At(1234); var e1=elliptic.At(5234);
        var eIntegrated=Integrate(new(e0.P,e0.V),1234,5234,.5,(_,pos)=>Gravity(pos,elliptic.Mu));
        if ((eIntegrated.P-e1.P).Length>1e-6 || (eIntegrated.V-e1.V).Length>1e-8) throw new Exception("Elliptic integration failed");
        Console.WriteLine("Circular and eccentric ephemeris/RK4 reference checks passed.");
        BindingTests();
    }
}
