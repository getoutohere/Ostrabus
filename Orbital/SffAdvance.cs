using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Collections;
using System.Diagnostics;
using HarmonyLib;
using Ostranauts.Ships.AIPilots;
using Ostrabus.Inspection;
using Ostranauts.ShipGUIs.Utilities;

namespace Ostrabus.Orbital;

// Select world timesteps, not AI behavior. A live pilot must get ordinary
// native decision turns before a long ballistic projection kills its ship.
internal static class SffAdvance {
    internal static bool Active { get; private set; }
    const double MaximumWindow = 21600;
    sealed class Run {
        internal double Duration, End;
        internal bool FinalWindow;
        internal int DispatchDepth;
        internal int Admitted;
        internal readonly Dictionary<AIShip, double> Birth = new Dictionary<AIShip, double>();
        internal readonly HashSet<AIShip> Called = new HashSet<AIShip>();
        internal readonly HashSet<NavData> CompletionOffered = new HashSet<NavData>();
        internal void Observe(double epoch) {
            if (AIShipManager.dictAIs == null) return;
            foreach (var list in AIShipManager.dictAIs.Values) {
                if (list == null) continue;
                foreach (var ai in list) if (ai != null && !Birth.ContainsKey(ai)) Birth.Add(ai, epoch);
            }
        }
    }
    static Run current;
    static object last = SnapshotJson.Map("state", "No decision-aware skip recorded.");
    internal static object Snapshot() => last;
    internal static void Reset() => last = SnapshotJson.Map("state", "No decision-aware skip recorded.");

    internal static void Update(StarSystem system, double seconds) {
        if (!GUIFFWD.Active) { system.Update(seconds); return; }
        Advance(system, seconds, "gui-sff");
    }
    internal static void Pass(StarSystem system, double seconds) => Advance(system, seconds, "pass");
    static void Advance(StarSystem system, double seconds, string source) {
        if (!OrbitAcceleration.Finite(seconds) || seconds <= 0 || !OrbitAcceleration.Finite(StarSystem.fEpoch + seconds)) {
            system.Update(seconds);
            return;
        }
        using var boundary = new JumpBoundary(source);
        if (Active || !OrbitalSimulation.Enabled || !SffSimulation.Enabled) { system.Update(seconds); return; }
        double start = StarSystem.fEpoch, end = start + seconds;
        int steps = 0, decisionSteps = 0, courseSteps = 0;
        int previews = 0;
        double previewSeconds = 0;
        long began = Stopwatch.GetTimestamp(), previewTicks = 0, worldTicks = 0;
        bool budgetFallback = false;
        var urgent = new List<AIShip>();
        var affected = new HashSet<string>(StringComparer.Ordinal);
        var previous = current;
        var run = new Run { Duration = seconds, End = end };
        current = run;
        Active = true;
        try {
            run.Observe(start);
            while (StarSystem.fEpoch < end) {
                double before = StarSystem.fEpoch, remaining = end - before, step = Math.Min(MaximumWindow, remaining);
                urgent.Clear();
                // A fixed bound limits pathological planning cost. Falling back
                // preserves native time progression; it grants no crash immunity.
                if (steps < 512) {
                    // Playback can change the next decision at this boundary.
                    // Do not calculate hours of trajectories beyond it first.
                    double horizon = before + step;
                    double course = CourseBoundary(system, before, horizon, run.CompletionOffered);
                    step = course - before;
                    previews++; previewSeconds += step;
                    long previewBegan = Stopwatch.GetTimestamp();
                    using (var preview = SffSimulation.Preview(system, step))
                        if (preview != null) step = Math.Min(step, preview.DecisionStep(urgent));
                    previewTicks += Stopwatch.GetTimestamp() - previewBegan;
                    if (course < horizon && before + step == course) courseSteps++;
                } else budgetFallback = true;
                if (urgent.Count != 0 && step < remaining) {
                    decisionSteps++;
                    foreach (var pilot in urgent) {
                        AIShipManager.PrioritizeShip(pilot.Ship);
                        if (affected.Count < 512) affected.Add(pilot.Ship.strRegID);
                    }
                }
                // The original world update owns all physics, AI, collisions,
                // economy and clock advancement. No detached actor is ticked
                // against a temporarily rewound shared world.
                run.FinalWindow = before + step == end;
                long worldBegan = Stopwatch.GetTimestamp();
                try { system.Update(step); }
                finally { worldTicks += Stopwatch.GetTimestamp() - worldBegan; }
                steps++;
                if (!(StarSystem.fEpoch > before) || StarSystem.fEpoch > end)
                    throw new InvalidOperationException("Time-skip world step did not advance within the requested interval.");
                run.Observe(StarSystem.fEpoch);
                // A stale/inactive member course may not be consumed by native
                // playback. Offer its completion once, never 512 tiny retries.
                foreach (var course in Courses(system)) if (course?.Destination != null && course.Destination.ArrivalTime < StarSystem.fEpoch)
                    run.CompletionOffered.Add(course);
            }
        } finally {
            Active = false;
            current = previous;
            var ids = new string[affected.Count]; affected.CopyTo(ids);
            Array.Sort(ids, StringComparer.Ordinal);
            last = SnapshotJson.Map("source", source, "startEpoch", start, "requestedSeconds", seconds,
                "endEpoch", StarSystem.fEpoch, "worldSteps", steps, "decisionSteps", decisionSteps,
                "courseCompletionSteps", courseSteps, "roleCallbacksAdmitted", run.Admitted,
                "previews", previews, "previewSeconds", previewSeconds,
                "previewWallMs", previewTicks * 1000d / Stopwatch.Frequency,
                "worldWallMs", worldTicks * 1000d / Stopwatch.Frequency,
                "elapsedWallMs", (Stopwatch.GetTimestamp() - began) * 1000d / Stopwatch.Frequency,
                "affectedShips", ids, "budgetFallback", budgetFallback);
        }
    }
    static IEnumerable<NavData> Courses(StarSystem system) {
        if (system?.dictShips == null) yield break;
        foreach (var ship in system.dictShips.Values) {
            if (ship == null || ship.bDestroyed || ship.bRemove || ship.HideFromSystem || ship.objSS == null) continue;
            if (ship.objSS.NavData != null) yield return ship.objSS.NavData;
            if (ship.HasDockGroup && ship.DockGroup.Anchor?.NavData != null) yield return ship.DockGroup.Anchor.NavData;
        }
    }
    static double CourseBoundary(StarSystem system, double now, double end, HashSet<NavData> offered) {
        double next = end;
        foreach (var course in Courses(system)) if (!offered.Contains(course)) Consider(course);
        return next;
        void Consider(NavData course) {
            var destination = course?.Destination;
            if (destination == null || !OrbitAcceleration.Finite(destination.ArrivalTime)) return;
            // Do not backdate an expired course. Native completion still owns
            // its playback; only schedule the next opportunity to finish it.
            double arrival = Math.Max(now, destination.ArrivalTime);
            if (arrival >= end) return;
            double after = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(arrival) + (arrival >= 0 ? 1 : -1));
            if (after > now && after < next) next = after;
        }
    }
    internal static bool EnterCatchup(ref double seconds) {
        var run = current;
        if (run == null) return false;
        run.DispatchDepth++;
        seconds = run.FinalWindow && run.Duration >= GUIFFWD.FFWDTIMEUNIT ? run.Duration : 0;
        return true;
    }
    internal static void LeaveCatchup(bool entered) { if (entered && current != null) current.DispatchDepth--; }
    internal static bool AllowCatchup(AIShip pilot) {
        var run = current;
        if (run == null || run.DispatchDepth == 0) return true;
        if (!run.FinalWindow || !run.Birth.TryGetValue(pilot, out double birth) ||
            run.End - birth < GUIFFWD.FFWDTIMEUNIT || !run.Called.Add(pilot)) return false;
        run.Admitted++;
        return true;
    }
}

[HarmonyPatch]
internal static class SffDecisionStepsPatch {
    internal static MethodBase TargetMethod() => AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(GUIFFWD), "FFWD"));
    internal static MethodBase PassTarget() => AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(CrewSim), "_TeleportCO"));
    [HarmonyTargetMethods]
    internal static IEnumerable<MethodBase> Targets() { yield return TargetMethod(); yield return PassTarget(); }

    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod = null) {
        var native = AccessTools.Method(typeof(StarSystem), nameof(StarSystem.Update));
        var advance = AccessTools.Method(typeof(SffAdvance), __originalMethod == PassTarget() ? nameof(SffAdvance.Pass) : nameof(SffAdvance.Update));
        int sites = 0;
        foreach (var instruction in instructions) {
            if (!instruction.Calls(native)) { yield return instruction; continue; }
            sites++;
            yield return new CodeInstruction(instruction) { opcode = OpCodes.Call, operand = advance };
        }
        if (sites != 1) throw new InvalidOperationException("Expected one GUI/PASS world update; time-jump integration needs review.");
    }
}

[HarmonyPatch(typeof(CrewSim), "_TeleportCO")]
internal static class PassPendingEndTurnsPatch {
    [HarmonyPostfix]
    internal static void Postfix(double timeJump, ref IEnumerator __result) {
        if (__result != null && timeJump > 0 && OrbitAcceleration.Finite(timeJump) && OrbitalSimulation.Enabled && SffSimulation.Enabled)
            __result = Ready(__result);
    }
    static IEnumerator Ready(IEnumerator native) {
        try {
            yield return Condowner.Utils.Scheduler.CompletePendingEndTurns();
            while (native.MoveNext()) yield return native.Current;
        } finally { (native as IDisposable)?.Dispose(); }
    }
}
