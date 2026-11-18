using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OrbitalVelocityStudy;
using UnityEngine;
using Debug = UnityEngine.Debug;

internal static partial class Program
{
    internal const double Meters = 149597870700d;
    static int assertions;
    static readonly List<object> Results = new List<object>();
    static readonly List<string> FailedGates = new List<string>();
    static string Output;
    static JObject Fixture;
    internal static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition)
            throw new Exception(message);
    }
    internal static void Gate(bool condition, string message)
    {
        assertions++;
        if (!condition)
            FailedGates.Add(message);
    }
    internal static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    internal static double Mag(double x, double y) => Math.Sqrt(x * x + y * y);
    internal static double D(JToken j, string key) => (double?)j?[key] ?? 0;
    internal static BodyOrbit Model(JToken j, JObject bodies, int depth = 0)
    {
        if (depth > 20)
            throw new Exception("Fixture cycle");
        var body = new BodyOrbit(new JsonBodyOrbitSave
        {
            strName = (string)j["id"],
            fPerh = D(j, "periapsisAU"),
            fAph = D(j, "apoapsisAU"),
            fAxis1 = D(j, "majorAxisAU"),
            fAxis2 = D(j, "minorAxisAU"),
            fEcc = D(j, "eccentricity"),
            fAngle = D(j, "angleDegrees"),
            fPeriod = D(j, "periodSeconds"),
            fPeriodShift = D(j, "phaseSeconds"),
            fMass = D(j, "massKg"),
            fRadius = D(j, "radiusAU"),
            nOrbitDirection = (int?)j["direction"] ?? 1
        });
        string parent = (string)j["parentId"];
        if (!string.IsNullOrEmpty(parent))
            body.boParent = Model(bodies[parent], bodies, depth + 1);
        body.dTimeCalcLast = body.dVelCalcLast = double.NegativeInfinity;
        return body;
    }
    static void Log(object value)
    {
        Results.Add(value);
        Console.WriteLine(JsonConvert.SerializeObject(value));
    }
    static bool QuietLog() => false;
    public static int Main(string[] args)
    {
        Output = Path.GetFullPath(args.Length > 1 ? args[1] : "obj/OrbitalVelocityStudy/results");
        Directory.CreateDirectory(Output);
        var boundary = new Harmony("offline.orbital-velocity.unity-log-boundary");
        foreach (string name in new[] { "Log", "LogWarning", "LogError" })
        {
            var method = AccessTools.Method(typeof(Debug), name, new[] { typeof(object) });
            if (method != null)
                boundary.Patch(method, prefix: new HarmonyMethod(typeof(Program), nameof(QuietLog)));
        }
        try
        {
            if (args.Length > 0 && args[0].StartsWith("compose", StringComparison.Ordinal))
                return Compose(args.Skip(2).ToArray(), args[0].StartsWith("compose-precise", StringComparison.Ordinal), args[0] != "compose-precise-native", args[0] == "compose-preserve", args[0] == "compose-production");
            Fixture = JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/Venus.json")));
            PhaseComparison.Capture(Fixture);
            LegacyWorldChecks.Capture();
            foreach (string stage in (args.Length > 0 && (args[0] == "precise" || args[0] == "preserve" || args[0] == "production") ? new[] { args[0] } : new[] { "native", "velocity", "handoff", "combined", "precise" }))
            {
                var patch = new Harmony("offline.orbital-velocity." + stage);
                int failedBefore = FailedGates.Count;
                try
                {
                    if (stage == "precise")
                        PrecisionPrototype.Install(patch);
                    if (stage == "production")
                        InstallProduction(patch);
                    HandoffPrototype.ConservativeFit = stage == "preserve";
                    if (stage == "velocity" || stage == "combined" || stage == "precise" || stage == "preserve")
                        VelocityPrototype.Install(patch);
                    if (stage == "handoff" || stage == "combined" || stage == "precise" || stage == "preserve")
                        HandoffPrototype.Install(patch);
                    Replay(stage);
                    if (stage == "velocity" || stage == "combined" || stage == "precise")
                        BoundaryChecks(stage);
                    if (stage == "combined" || stage == "precise" || stage == "preserve" || stage == "production")
                        LifecycleChecks(stage);
                    if (stage == "preserve" || stage == "production")
                    {
                        PreservationChecks();
                        Log(LegacyWorldChecks.Check(Gate));
                    }
                    if (stage == "production")
                        ProductionChecks();
                    if (stage == "precise")
                        Log(PhaseComparison.Compare());
                    Benchmark(stage);
                    Log(new
                    {
                        kind = "variant-verdict",
                        stage,
                        failedGates = FailedGates.Skip(failedBefore).ToArray(),
                        passed = failedBefore == FailedGates.Count
                    });
                }
                finally { HandoffPrototype.ConservativeFit = false; patch.UnpatchSelf(); }
            }
            Log(new
            {
                result = FailedGates.Count == 0 ? "passed" : "blocked",
                assertions,
                failedGates = FailedGates,
                gameMvid = typeof(BodyOrbit).Assembly.ManifestModule.ModuleVersionId,
                limitations = new[] { "Study hooks are installed in this separate process only.", "No live Unity frame-rate measurement or full save replay.", "Atmospheric lift/drag and authored AI policy are not replaced." }
            });
            File.WriteAllText(Path.Combine(Output, "results.json"), JsonConvert.SerializeObject(Results, Formatting.Indented));
            return FailedGates.Count == 0 ? 0 : 1;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            File.WriteAllText(Path.Combine(Output, "failure.txt"), e.ToString());
            return 1;
        }
        finally { boundary.UnpatchSelf(); }
    }

    static void Replay(string stage)
    {
        var handoffs = new List<object>();
        double maxJump = 0, maxFit = 0, maxNext = 0, maxPosition = 0;
        var oldWorld = CrewSim.system;
        double oldEpoch = StarSystem.fEpoch;
        try
        {
            foreach (var token in (JArray)Fixture["handoffs"])
            {
                var before = token["before"];
                double epoch = D(token, "epoch");
                var bodies = (JObject)token["bodies"];
                var parent = Model(before["referenceBody"], bodies);
                parent.UpdateTime(D(before["referenceBody"], "velocityCalculationEpoch"));
                parent.UpdateTime(epoch);
                var world = Empty<StarSystem>();
                world.aBOs = new Dictionary<string, BodyOrbit>();
                for (var b = parent; b != null; b = b.boParent)
                    world.aBOs[b.strName] = b;
                CrewSim.system = world;
                StarSystem.fEpoch = epoch;
                var ship = Empty<Ship>();
                ship.strRegID = "STUDY-VESSEL";
                // Fixture boundary: a known cached mass avoids loading the Unity market singleton.
                AccessTools.Field(typeof(Ship), "nLoadState").SetValue(ship, Ship.Loaded.Full);
                AccessTools.Field(typeof(Ship), "_mass").SetValue(ship, 32000d);
                var situation = new ShipSitu(false)
                {
                    vPosx = D(before, "xAU"),
                    vPosy = D(before, "yAU"),
                    vVelX = D(before, "vxAUPerSecond"),
                    vVelY = D(before, "vyAUPerSecond")
                };
                AccessTools.Property(typeof(Ship), nameof(Ship.objSS)).SetValue(ship, situation);
                double px = situation.vPosx, py = situation.vPosy, vx = situation.vVelX, vy = situation.vVelY;
                var fitted = BodyOrbit.CreateBOFromShip(ship, parent);
                Check(fitted != null, stage + ": native orbit factory rejected captured handoff");
                world.aBOs[fitted.strName] = fitted;
                situation.LockToOrbit(fitted);
                double jump = Mag(situation.vVelX - vx, situation.vVelY - vy) * Meters;
                double positionError = Mag(fitted.dXReal - px, fitted.dYReal - py) * Meters;
                var instant = HandoffPrototype.Detached(fitted, epoch);
                double fitError = Mag(instant.VX - vx, instant.VY - vy) * Meters;
                situation.TimeAdvance(0);
                double nextJump = Mag(situation.vVelX - vx, situation.vVelY - vy) * Meters;
                double actualPositionChange = Mag(situation.vPosx - px, situation.vPosy - py) * Meters;
                maxJump = Math.Max(maxJump, jump);
                maxFit = Math.Max(maxFit, fitError);
                maxNext = Math.Max(maxNext, nextJump);
                maxPosition = Math.Max(maxPosition, positionError);
                handoffs.Add(new
                {
                    epoch,
                    jumpMps = jump,
                    instantaneousFitErrorMps = fitError,
                    nextNativeUpdateErrorMps = nextJump,
                    positionFitErrorM = positionError,
                    actualPositionChangeM = actualPositionChange
                });
                // Characterization stages are not incorrectly asserted to be complete fixes.
                if (stage == "native")
                    Check(jump > 1, "Control failed to reproduce captured velocity loss");
                if (stage == "combined" || stage == "precise" || stage == "preserve" || stage == "production")
                {
                    Gate(jump < .01, "Combined handoff velocity discontinuity: " + jump);
                    Gate(nextJump < .01, "Next native update undid handoff correction: " + nextJump);
                    Gate(actualPositionChange < .001, "Combined handoff moved the hull: " + actualPositionChange);
                    Gate(positionError < 5, "Combined fit needs excessive native orbital offset: " + positionError);
                }
            }
        }
        finally { CrewSim.system = oldWorld; StarSystem.fEpoch = oldEpoch; }
        Log(new
        {
            kind = "captured-handoffs",
            stage,
            count = handoffs.Count,
            maxJumpMps = maxJump,
            maxInstantaneousFitErrorMps = maxFit,
            maxNextUpdateErrorMps = maxNext,
            maxPositionFitErrorM = maxPosition
        });
        File.WriteAllText(Path.Combine(Output, "handoffs-" + stage + ".json"), JsonConvert.SerializeObject(handoffs, Formatting.Indented));
        ReplayReadouts(stage);
    }

    static void ReplayReadouts(string stage)
    {
        var rows = new List<object>();
        foreach (var s in (JArray)Fixture["samples"])
        {
            var ships = (JArray)s["ships"];
            var own = ships.First(j => (string)j["id"] == (string)s["playerShipId"])["motion"];
            var target = ships.First(j => (string)j["id"] == "B-7G57")["motion"];
            double t = D(s, "epoch"), ox = D(own, "vxAUPerSecond"), oy = D(own, "vyAUPerSecond"), tx = D(target, "vxAUPerSecond"), ty = D(target, "vyAUPerSecond");
            bool corrected = stage == "velocity" || stage == "combined" || stage == "precise" || stage == "preserve";
            if (corrected)
            {
                foreach (var m in new[] { own, target })
                    if ((bool)m["orbitLocked"] || (bool)m["bodyOrbitLocked"] || (bool)m["isBodyOrbit"])
                    {
                        var b = Model(m["referenceBody"], (JObject)s["bodies"]);
                        b.UpdateTime(t);
                        if (ReferenceEquals(m, own))
                        {
                            ox = b.dVelX;
                            oy = b.dVelY;
                        }
                        else
                        {
                            tx = b.dVelX;
                            ty = b.dVelY;
                        }
                    }
            }
            double dx = D(target, "xAU") - D(own, "xAU"), dy = D(target, "yAU") - D(own, "yAU"), range = Mag(dx, dy);
            double cross = (dx * (ty - oy) - dy * (tx - ox)) / range * Meters;
            rows.Add(new
            {
                epoch = t,
                crossTrackMps = cross,
                display = (string)s["navDisplay"]["vcrsText"]
            });
        }
        File.WriteAllText(Path.Combine(Output, "readouts-" + stage + ".json"), JsonConvert.SerializeObject(rows, Formatting.Indented));
    }
}
