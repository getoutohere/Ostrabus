using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json;
using OrbitalVelocityStudy;

internal static partial class Program
{
    static BodyOrbit Body(string id, double axis, double eccentricity, double period, BodyOrbit parent = null, int direction = 1)
    {
        return new BodyOrbit(new JsonBodyOrbitSave
        {
            strName = id,
            fAxis1 = 2 * axis,
            fAxis2 = 2 * axis * Math.Sqrt(1 - eccentricity * eccentricity),
            fPerh = axis * (1 - eccentricity),
            fAph = axis * (1 + eccentricity),
            fEcc = eccentricity,
            fPeriod = period,
            nOrbitDirection = direction,
            fMass = axis == 0 ? 1.9891e30 : 4.87e24,
            fAngle = 17.3
        })
        {
            boParent = parent
        };
    }
    static void BoundaryChecks(string stage)
    {
        double largestDerivativeError = 0;
        foreach (double ecc in new[] { 0d, .0067, .455, .9 })
            foreach (int direction in new[] { -1, 1 })
            {
                var root = Body("Sol", 0, 0, 31556926);
                var planet = Body("Venus", .723, ecc, 19407509, root, direction);
                var station = Body("VORB", .00008, .455, 14470, planet, -direction);
                var substation = Body("aux", 0, 0, 14470, station);
                foreach (var b in new[] { root, planet, station, substation })
                    foreach (double epoch in new[] { 123.125, 65634692000.25, -101.25 })
                    {
                        // A central derivative invokes the actual game trajectory, not a second copy of the velocity formula.
                        double h = .0625, lo = epoch - h, hi = epoch + h;
                        b.UpdateTime(lo, true, false);
                        double x = b.dXReal, y = b.dYReal;
                        b.UpdateTime(hi, true, false);
                        double dx = (b.dXReal - x) / (hi - lo), dy = (b.dYReal - y) / (hi - lo);
                        b.UpdateTime(epoch);
                        double error = Mag(dx - b.dVelX, dy - b.dVelY) * Meters;
                        largestDerivativeError = Math.Max(largestDerivativeError, error);
                        Gate(error < .1, "Analytical velocity differs from native position derivative: " + b.strName + " ecc=" + ecc + " dir=" + direction + " epoch=" + epoch + " error=" + error);
                        double px = b.dXReal, py = b.dYReal, vx = b.dVelX, vy = b.dVelY;
                        for (int i = 0; i < 10; i++)
                            b.UpdateTime(epoch);
                        Check(px == b.dXReal && py == b.dYReal && vx == b.dVelX && vy == b.dVelY, "Repeated same-time query mutated state");
                        b.UpdateTime(epoch + 100, false, false);
                        Check(vx == b.dVelX && vy == b.dVelY, "Position-only prediction altered velocity cache");
                        b.UpdateTime(epoch);
                        Check(Math.Abs(vx - b.dVelX) < 1e-20 && Math.Abs(vy - b.dVelY) < 1e-20, "Future/backward query contaminated restored velocity");
                    }
                // bCorrectTimes=false remains the derivative of the requested uncorrected trajectory.
                planet.UpdateTime(200, true, true);
                planet.UpdateTime(201, false, true);
                double rawVx = planet.dVelX, rawVy = planet.dVelY;
                planet.UpdateTime(201 - .0625, false, false);
                double lx = planet.dXReal, ly = planet.dYReal;
                planet.UpdateTime(201 + .0625, false, false);
                Check(Mag((planet.dXReal - lx) / .125 - rawVx, (planet.dYReal - ly) / .125 - rawVy) * Meters < .1, "Uncorrected-time velocity convention changed");
            }
        var invalid = Body("bad", 1, 0, 100);
        invalid.boParent = invalid;
        double old = invalid.dTimeCalcLast;
        Check(!VelocityPrototype.Update(invalid, 100, true, true) && invalid.dTimeCalcLast == old, "Cycle validation changed body");
        invalid.boParent = null;
        invalid.fPeriod = double.NaN;
        Check(!VelocityPrototype.Update(invalid, 100, true, true) && invalid.dTimeCalcLast == old, "Invalid validation changed body");
        var fixedParent = Body("Venus", .723, .0067, 19407509);
        var fixedReference = Body("regional-reference", 0, 0, 1, fixedParent);
        fixedReference.fPeriod = 0;
        fixedReference.UpdateTime(100);
        Gate(VelocityPrototype.Finite(fixedReference.dXReal) && fixedReference.dVelX == fixedParent.dVelX && fixedReference.dVelY == fixedParent.dVelY,
            "Stationary manufactured reference failed to inherit parent velocity");
        double oldEpoch = StarSystem.fEpoch;
        try
        {
            StarSystem.fEpoch = 100;
            fixedReference.UpdateTime(100);
            var relock = new ShipSitu(false)
            {
                bBOLocked = true,
                strBOPORShip = "missing",
                vPosx = fixedReference.dXReal + 10 / Meters,
                vPosy = fixedReference.dYReal,
                vVelX = fixedReference.dVelX,
                vVelY = fixedReference.dVelY
            };
            Gate(relock.TryRelockToBO(fixedReference) && relock.strBOPORShip == fixedReference.strName &&
                VelocityPrototype.Finite(relock.vVelX), "Valid ephemeris missing-reference fallback failed");
            relock.bBOLocked = false;
            relock.vVelX += 1000 / Meters;
            Gate(!relock.TryRelockToBO(fixedReference), "Valid ephemeris fallback bypassed native relative-speed limit");
        }
        finally { StarSystem.fEpoch = oldEpoch; }
        Log(new
        {
            kind = "ephemeris-boundaries",
            stage,
            largestNativeDerivativeErrorMps = largestDerivativeError
        });
    }

    static void Benchmark(string stage)
    {
        const int bodyCount = 64, frames = 300;
        var root = Body("Sol", 0, 0, 31556926);
        var venus = Body("Venus", .723, .0067, 19407509, root);
        var bodies = new BodyOrbit[bodyCount];
        for (int i = 0; i < bodyCount; i++)
            bodies[i] = Body("station-" + i, .00008 + i * .0000001, .2, 14000 + i * 20, venus);
        Func<long> allocated = null;
        var alloc = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", BindingFlags.Static | BindingFlags.Public);
        if (alloc != null)
            allocated = (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), alloc);
        foreach (int repeats in new[] { 1, 10 })
        {
            for (int f = 0; f < 100; f++)
                foreach (var b in bodies)
                    b.UpdateTime(65634690000 + f / 60d);
            var runs = new List<double>();
            long maxAlloc = 0;
            for (int run = 0; run < 5; run++)
            {
                long bytes = allocated?.Invoke() ?? 0;
                var timer = Stopwatch.StartNew();
                for (int f = 0; f < frames; f++)
                {
                    double t = 65634691000 + f / 60d;
                    for (int repeat = 0; repeat < repeats; repeat++)
                        for (int i = 0; i < bodies.Length; i++)
                            bodies[i].UpdateTime(t);
                }
                timer.Stop();
                long used = (allocated?.Invoke() ?? 0) - bytes;
                // Stopwatch construction itself is inside the allocation interval.
                maxAlloc = Math.Max(maxAlloc, Math.Max(0, used));
                runs.Add(timer.Elapsed.TotalMilliseconds);
            }
            runs.Sort();
            Log(new
            {
                kind = "mono-ephemeris-benchmark",
                stage,
                bodyCount,
                frames,
                repeatsPerFrame = repeats,
                medianMs = runs[2],
                bestMs = runs[0],
                worstMs = runs[4],
                medianMsPerFrame = runs[2] / frames,
                maximumAllocatedBytes = allocated == null ? (object)null : maxAlloc,
                note = "Solver-only bundled Mono; excludes Unity rendering, AI, collision and whole-game frame timing."
            });
        }
    }

    static int Compose(string[] args, bool precise, bool adaptDetached, bool preserve, bool production)
    {
        if (args.Length == 0)
            throw new ArgumentException("compose OUTPUT ASSEMBLY [suite arguments]");
        var patch = new Harmony("offline.orbital-velocity.composition");
        HandoffPrototype.ConservativeFit = preserve;
        if (precise)
            PrecisionPrototype.Install(patch);
        if (production)
            InstallProduction(patch);
        else
        {
            VelocityPrototype.Install(patch);
            HandoffPrototype.Install(patch);
        }
        string assemblyPath = Path.GetFullPath(args[0]);
        // Some unchanged suites locate fixtures relative to the entry assembly.
        // Stage only their JSON fixtures into this harness's build output.
        foreach (string source in Directory.GetFiles(Path.GetDirectoryName(assemblyPath), "*.json", SearchOption.AllDirectories))
        {
            string relative = source.Substring(Path.GetDirectoryName(assemblyPath).Length + 1);
            string destination = Path.Combine(AppContext.BaseDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            File.Copy(source, destination, true);
        }
        ResolveEventHandler resolver = (sender, e) =>
        {
            string file = Path.Combine(Path.GetDirectoryName(assemblyPath), new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(file) ? Assembly.LoadFrom(file) : null;
        };
        AppDomain.CurrentDomain.AssemblyResolve += resolver;
        try
        {
            var assembly = Assembly.LoadFrom(assemblyPath);
            var entry = assembly.EntryPoint;
            if (precise && adaptDetached)
                PrecisionPrototype.AdaptDetachedHelpers(patch, assembly);
            if (entry == null)
                throw new Exception("Suite has no entry point");
            Console.WriteLine("COMPOSE: " + (production ? "production" : "experimental") + " velocity + handoff, then unchanged suite " + assembly.GetName().Name);
            object result = entry.Invoke(null, entry.GetParameters().Length == 0 ? null : new object[] { args.Skip(1).ToArray() });
            return result is int code ? code : 0;
        }
        catch (TargetInvocationException e) { Console.Error.WriteLine(e.InnerException ?? e); return 1; }
        finally { HandoffPrototype.ConservativeFit = false; AppDomain.CurrentDomain.AssemblyResolve -= resolver; patch.UnpatchSelf(); }
    }
}
