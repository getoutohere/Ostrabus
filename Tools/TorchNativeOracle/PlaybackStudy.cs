using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Ostrabus.TorchCalculator;
using Ostranauts.ShipGUIs.Utilities;
using Ostranauts.Ships.Commands;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Utils.Models;
using UnityEngine;

static partial class Program {
    static FusionIC playbackReactor;
    static bool componentThrows;
    static readonly AccessTools.FieldRef<NavData, List<NavDataPoint>> StudyPoints = AccessTools.FieldRefAccess<NavData, List<NavDataPoint>>("_navPoints");
    static bool playbackShutdown;
    static bool candidateControls;
    static NavData candidateCourse;
    static double candidateCapacity;
    static double IntervalCycle(NavDataPoint a, NavDataPoint b) =>
        (b.ObjSS.vVel - a.ObjSS.vVel).magnitude < 1e-15 ? 0 : Math.Max(a.TorchCycle, b.TorchCycle);
    static double CourseCycleAt(NavData nav, double time) {
        if (time >= nav.Destination.ArrivalTime) return 0;
        var segment = nav.GetCurrentNavPoints(time);
        return segment == null ? 0 : IntervalCycle(segment.Item1, segment.Item2);
    }
    static double BudgetAt(NavData nav, double time, double p) {
        var points = StudyPoints(nav);
        for (int i = 1; i < points.Count; i++) {
            var a = points[i-1]; var b = points[i];
            if (time > b.ArrivalTime) continue;
            double duration = b.ArrivalTime - a.ArrivalTime;
            double part = Math.Max(0, time - a.ArrivalTime);
            double cycle = IntervalCycle(a, b);
            return a.TorchFuelLevel - SegmentFuel(cycle, cycle, part, p);
        }
        return points[points.Count-1].TorchFuelLevel;
    }
    static void CourseFuelBoundary(ref double fShallowFusionRemain) {
        if (candidateControls && candidateCourse != null) fShallowFusionRemain = BudgetAt(candidateCourse, fixtureEpoch, candidateCapacity);
    }
    static void BalancedControls(double fDeltaTime) {
        if (!candidateControls || playbackReactor == null) return;
        Controls["slidCycle"] = Number(CourseCycleAt(candidateCourse, fixtureEpoch));
        double dt = Math.Min(1, Math.Max(1e-5, fDeltaTime));
        double cycle = double.Parse(Controls["slidCycle"], CultureInfo.InvariantCulture);
        double p = Read(core, "StatICPellMax"), temperature = Read(core, "StatICCoreTemp");
        double minimum = TorchModel.Lerp((float)p, .001f, (float)Math.Min((temperature - cycle * .2 * dt) / TorchModel.Ideal, 1));
        double required = (TorchModel.Ideal - temperature + cycle * .2 * dt) / (.04 * dt);
        double flow = (required - minimum) / (p - minimum);
        Controls["slidFlow"] = Number(Math.Max(0, Math.Min(1, flow)));
    }
    // Integral of max(idleFloor, 5*CYCLE/P), with linearly interpolated CYCLE.
    static double SegmentFuel(double c0, double c1, double duration, double p) {
        double floor = TorchModel.Lerp((float)p, .001f, 1) / p;
        double r0 = c0 * 5 / p, r1 = c1 * 5 / p;
        if (r0 >= floor && r1 >= floor) return duration * (r0 + r1) * .5;
        if (r0 <= floor && r1 <= floor) return duration * floor;
        double crossing = (floor - r0) / (r1 - r0);
        return r0 < floor ? duration * (floor * crossing + (floor + r1) * .5 * (1 - crossing)) :
            duration * ((r0 + floor) * .5 * crossing + floor * (1 - crossing));
    }
    static void Rebudget(NavData nav, double p) {
        var points = StudyPoints(nav);
        for (int i = 1; i < points.Count; i++)
            points[i].TorchFuelLevel = points[i-1].TorchFuelLevel - SegmentFuel(IntervalCycle(points[i-1], points[i]), IntervalCycle(points[i-1], points[i]),
                points[i].ArrivalTime - points[i-1].ArrivalTime, p);
    }
    static bool ShutdownBoundary() { playbackShutdown = true; return false; }
    static bool ThrustBoundary(double __0) {
        thrustForce = __0;
        if (playbackReactor != null) {
            double acceleration = Math.Max(0, __0) / Ostrabus.ShipNavigation.Towing.DockedCoursePlayback.InertialMass(ship) / 149597872 / 1000;
            ship.objSS.vAccIn.Set(-(float)acceleration * Mathf.Sin(ship.objSS.fRot), (float)acceleration * Mathf.Cos(ship.objSS.fRot));
        }
        return false;
    }
    static FusionIC ReactorComponent(Component ignored) => componentThrows ? throw new InvalidOperationException("fixture component failure") : playbackReactor;
    static IEnumerable<CodeInstruction> ComponentBoundary(IEnumerable<CodeInstruction> code) {
        int replaced = 0;
        foreach (var instruction in code) {
            if (instruction.operand is MethodInfo method && method.Name == "GetComponent" && method.IsGenericMethod && method.GetGenericArguments()[0] == typeof(FusionIC)) {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(Program), nameof(ReactorComponent));
                replaced++;
            }
            yield return instruction;
        }
        Check(replaced == 1, "Playback fixture replaces only the Unity reactor-component lookup");
    }
    static bool SetGpm(string __0, string __1) { Controls[__0] = __1; return false; }
    static bool NoAi(ref AIShip __result) { __result = null; return false; }
    static bool StringBoundary(string __0, ref string __result) { __result = __0; return false; }
    static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    static NavData MakeStudyCourse(double cycle, double coastSeconds, double burnSeconds = 1200) {
        var path = Empty<FlyToPath>(); Field(path, "_shipUs", ship);
        var nav = new NavData(ship);
        var origin = new ShipSitu(false);
        nav.AddNavDataPoint(new NavDataPoint(fixtureEpoch, origin, 0, 0, ship.fShallowFusionRemain));
        var destination = new NavDataPoint(fixtureEpoch, new ShipSitu(false), 0, 0, 0);
        var plot = AccessTools.Method(typeof(FlyToPath), "PlotBurnLoop");
        double acceleration = ship.GetMaxTorchThrust((float)cycle);
        // Native waypoint generator, including easing and coast transitions.
        foreach (var stage in new[] { (burnSeconds, acceleration, 2, 0), (coastSeconds, 0d, 0, 0), (burnSeconds, -acceleration, 0, 2) }) {
            if (stage.Item1 <= 0) continue;
            plot.Invoke(path, new object[] { nav, 10, stage.Item1 / 10, new Point(stage.Item2, 0), true, false,
                (float)cycle, destination, ":study", stage.Item3, stage.Item4 });
        }
        nav.Destination.TorchCycle = 0;
        nav.SetFlowMultPlot(NavData.GetFLOWforCYCLE(core, cycle));
        return nav;
    }
    static void RunPlaybackStudy(bool controlChecksOnly = false, bool transferChecksOnly = false, bool clockChecksOnly = false) {
        var harness = new Harmony("offline.torch.playback.boundaries");
        foreach (var patch in new[] { typeof(Ostrabus.MiscBugFixes.TorchCoursePlaybackPatch), typeof(Ostrabus.MiscBugFixes.TorchCourseReactantsPatch), typeof(Ostrabus.MiscBugFixes.TorchCourseReactorPatch), typeof(Ostrabus.MiscBugFixes.TorchCourseFuelConsumptionPatch) }) harness.CreateClassProcessor(patch).Patch();
        void Patch(MethodBase method, string name) => harness.Patch(method, prefix: new HarmonyMethod(typeof(Program), name));
        harness.Patch(AccessTools.Method(typeof(NavData), nameof(NavData.TimeAdvance)), transpiler: new HarmonyMethod(typeof(Program), nameof(ComponentBoundary)));
        Patch(AccessTools.Method(typeof(Ship), nameof(Ship.SetReactorGPMValue)), nameof(SetGpm));
        Patch(AccessTools.Method(typeof(Ship), nameof(Ship.UnlockFromOrbit)), nameof(Skip));
        Patch(AccessTools.Method(typeof(Ship), nameof(Ship.ClearShipTarget)), nameof(Skip));
        Patch(AccessTools.Method(typeof(FusionIC), "ShutDown"), nameof(ShutdownBoundary));
        harness.Patch(AccessTools.Method(typeof(FusionIC), "Run"), prefix: new HarmonyMethod(typeof(Program), nameof(BalancedControls)));
        harness.Patch(AccessTools.Method(typeof(FusionIC), nameof(FusionIC.SetReactants)), prefix: new HarmonyMethod(typeof(Program), nameof(CourseFuelBoundary)));
        foreach (var method in AccessTools.GetDeclaredMethods(typeof(Ship)).Where(m => m.Name == "LogAdd")) Patch(method, nameof(Skip));
        Patch(AccessTools.Method(typeof(AIShipManager), nameof(AIShipManager.GetAIShipByRegID)), nameof(NoAi));
        Patch(AccessTools.Method(typeof(DataHandler), nameof(DataHandler.GetString)), nameof(StringBoundary));
        var output = new List<string> { "model,pairs,cycle,initialTemperatureFraction,schedule,completed,abortSeconds,reactorShutdown,he3ConsumedKg,plannedHe3Kg,finalFuelSeconds,finalTemperatureFraction,finalFlow,positionErrorAU,velocityErrorAUPerSecond" };
        var detail = new List<string> { "model,pairs,cycle,initialTemperatureFraction,schedule,seconds,phase,flow,cycleNow,temperatureFraction,fuelSeconds,he3Kg" };
        var completedResults = new Dictionary<string, double>();
        try {
            if (clockChecksOnly) { RunSffClockStudy(); return; }
            if (controlChecksOnly) { RunControlHandoffChecks(); return; }
            if (transferChecksOnly) { RunTransferPlaybackChecks(); return; }
            // Independent numerical integration and arbitrary subdivision checks
            // include ramps crossing the idle floor, not only constant throttle.
            foreach (double p in new[] { 2d, 4, 8 }) foreach (double c0 in new[] { 0d, .00001, .1, .3 }) foreach (double c1 in new[] { 0d, .00001, .2, .35 }) {
                const int samples = 10000; const double duration = 1234.5;
                double floor = TorchModel.Lerp((float)p, .001f, 1) / p, numerical = 0;
                for (int i = 0; i < samples; i++) numerical += Math.Max(floor, (c0 + (c1-c0) * (i + .5) / samples) * 5 / p) * duration / samples;
                double whole = SegmentFuel(c0, c1, duration, p);
                Near(whole, numerical, "Candidate closed-form fuel matches independent quadrature", 2e-6);
                double split = .317, middle = c0 + (c1-c0) * split;
                Near(whole, SegmentFuel(c0, middle, duration * split, p) + SegmentFuel(middle, c1, duration * (1-split), p), "Candidate fuel is invariant to partial-jump partition", 1e-10);
            }
            foreach (string model in new[] { "native", "candidateBalanced", "production" })
                foreach (int pairs in new[] { 1, 2, 4 }) foreach (double fraction in new[] { .5, .95 }) foreach (double initialTemperature in new[] { 1d, .9 })
                foreach (string schedule in new[] { "normalTicks", "hourJumps", "singleJump", "irregularJumps" }) {
                fixtureEpoch = 100;
                var fit = new ReactorFit { Feeders = pairs, Lasers = pairs, Capacitors = 2, Regulators = 2 };
                double p = TorchModel.PelletMax(fit), cycle = Math.Min(1, .2 * p) * fraction;
                var fusion = Make(fit, TorchModel.Ideal * initialTemperature, cycle, 0);
                CrewSim.coPlayer = model == "production" ? Co() : null;
                if (model == "production") Field(ship, "nLoadState", Ship.Loaded.Full);
                Field(ship, "fFuelEfficiencyMod", 1d);
                ship.aNavs = new List<CondOwner>();
                Values[core]["IsTorchDrive"] = 1;
                Controls["fFlowEpochResume"] = "0"; Controls["fCrsLimMax"] = "1";
                AccessTools.Method(typeof(FusionIC), "Run").Invoke(fusion, new object[] { TorchModel.TickSeconds });
                AccessTools.Method(typeof(FusionIC), "Fusion").Invoke(fusion, new object[] { 0d, new JsonPowerInfo(), 0d });
                Values[core]["StatICCoreTemp"] = TorchModel.Ideal * initialTemperature;
                double usage = fusion.MassUsageMax;
                Values[d2o[0]]["StatVolume"] = 1000; Values[he3[0]]["StatVolume"] = 1000;
                Values[d2o[0]]["StatLiqD2O"] = usage * TorchModel.D2ORatio * 100000;
                Values[he3[0]]["StatSolidHe3"] = usage * 100000;
                fusion.SetReactants(100000);
                Field(fusion, "fTimeNextNWZ", double.MaxValue);
                double firstMass = Read(he3[0], "StatSolidHe3");
                var nav = MakeStudyCourse(cycle, 14400);
                if (model == "production") Check(Ostrabus.MiscBugFixes.TorchCourseFuel.Attach(nav, false) == (initialTemperature == 1), "Production planning eligibility matches warm/cold policy");
                // Candidate is deliberately restricted to an already warm reactor.
                // Cold-core runs remain native controls and native accounting.
                candidateControls = model == "candidateBalanced" && initialTemperature == 1;
                if (candidateControls) Rebudget(nav, p);
                candidateCourse = candidateControls ? nav : null; candidateCapacity = p;
                ship.objSS.NavData = nav;
                double start = fixtureEpoch, end = nav.Destination.ArrivalTime;
                double targetFuel = nav.Destination.TorchFuelLevel;
                Check(targetFuel < nav.Origin.TorchFuelLevel - 1, "Native generated course has a nonzero powered fuel budget");
                var endPosition = nav.Destination.ObjSS.vPos;
                var endVelocity = nav.Destination.ObjSS.vVel;
                Field(nav, "fEpochLast", start);
                Controls["slidFlow"] = Number(NavData.GetFLOWforCYCLE(core, cycle));
                var autopilot = Empty<FlyToAutoPilot>(); Field(autopilot, "_shipUs", ship);
                var requirements = AccessTools.Method(typeof(FlyToAutoPilot), "RequirementsMet");
                playbackReactor = fusion;
                playbackShutdown = false;
                double elapsed = 0; int jumps = 0, sample = -1; bool completed = true;
                while (fixtureEpoch < end - 1e-6) {
                    double dt = schedule == "normalTicks" ? TorchModel.TickSeconds : schedule == "singleJump" ? end - start :
                        schedule == "hourJumps" ? 3600 : new[] { 1700d, 4700, 2100, 3400, 6200 }[jumps++ % 5];
                    dt = Math.Min(dt, end - fixtureEpoch);
                    fixtureEpoch += dt; StarSystem.fEpoch = fixtureEpoch; elapsed = fixtureEpoch - start;
                    bool active = nav.TimeAdvance(ship.objSS);
                    if (Read(core, "IsAutopilotAborted") > 0 || !active || playbackShutdown) { completed = false; break; }
                    if (!(bool)requirements.Invoke(autopilot, null)) { completed = false; break; }
                    // Unity's ordinary reactor Update also runs during a coast.
                    fusion.CatchUp();
                    if (playbackShutdown) { completed = false; break; }
                    int nextSample = (int)(elapsed / 120);
                    if (nextSample != sample || schedule != "normalTicks") {
                        if (candidateControls) Near(ship.fShallowFusionRemain, BudgetAt(nav, fixtureEpoch, p), "Native partial playback consumes analytic candidate budget", 1e-9);
                        sample = nextSample;
                        var segment = nav.GetCurrentNavPoints(fixtureEpoch);
                        string phase = segment != null && segment.Item1.TorchFuelLevel - segment.Item2.TorchFuelLevel > .001 ? "powered" : "coast";
                        detail.Add(string.Join(",", model, pairs, Number(cycle), Number(initialTemperature), schedule, Number(elapsed), phase,
                            Controls["slidFlow"], Controls["slidCycle"], Number(Read(core, "StatICCoreTemp") / TorchModel.Ideal), Number(ship.fShallowFusionRemain), Number(Read(he3[0], "StatSolidHe3"))));
                    }
                }
                double consumed = firstMass - Read(he3[0], "StatSolidHe3");
                double posError = (ship.objSS.vPos - endPosition).magnitude, velError = (ship.objSS.vVel - endVelocity).magnitude;
                if (completed) {
                    Check(posError <= 1e-15, "Native completed-course endpoint position unchanged");
                    Check(velError <= 1e-20, "Native completed-course endpoint velocity unchanged");
                    Near(consumed, (100000 - targetFuel) * usage, "Native tank consumption matches cumulative course budget", 3e-6);
                    if (candidateControls || (model == "production" && initialTemperature == 1)) Near(Read(core, "StatICCoreTemp"), TorchModel.Ideal, "Balanced controls preserve thermal operating point", 1e-5);
                    string key = model + "/" + pairs + "/" + Number(cycle) + "/" + Number(initialTemperature);
                    if (completedResults.TryGetValue(key, out double previous)) Near(consumed, previous, "Native course fuel invariant across normal/SFF partitions", 3e-6);
                    else completedResults[key] = consumed;
                    if (model == "production") Near(consumed, completedResults["candidateBalanced/" + pairs + "/" + Number(cycle) + "/" + Number(initialTemperature)], "Production hooks match validated candidate/fallback budgets", 3e-6);
                }
                Check(!double.IsNaN(consumed) && !double.IsInfinity(consumed), "Finite native course fuel accounting");
                output.Add(string.Join(",", model, pairs, Number(cycle), Number(initialTemperature), schedule, completed, completed ? "" : Number(elapsed), playbackShutdown,
                    Number(consumed), Number((100000 - targetFuel) * usage), Number(ship.fShallowFusionRemain), Number(Read(core, "StatICCoreTemp") / TorchModel.Ideal),
                    Controls["slidFlow"], Number(posError), Number(velError)));
                playbackReactor = null;
                candidateControls = false;
                candidateCourse = null;
            }
            File.WriteAllLines("obj/torch-playback-study.csv", output);
            File.WriteAllLines("obj/torch-playback-events.csv", detail);
            Check(output.Skip(1).All(row => row.Split(',')[5] == "True"), "Every supported/fallback playback scenario completed with native fuel and thermal checks active");
            Console.WriteLine("Playback study: " + (output.Count - 1) + " native waypoint/playback cases; obj/torch-playback-study.csv");
            RunCourseLifecycleChecks();
            RunControlHandoffChecks();
            RunTowCompatibilityChecks();
            RunTransferPlaybackChecks();
            RunSffClockStudy();
        } finally { playbackReactor = null; candidateControls = false; candidateCourse = null; fixtureEpoch = 100; harness.UnpatchSelf(); }
    }
}
