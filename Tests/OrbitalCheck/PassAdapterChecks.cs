using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using Ostrabus.Orbital;
using Ostranauts.Core;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;
using Ostranauts.ShipGUIs.Utilities;

static partial class SffDecisionChecks {
    static readonly Dictionary<AIShip, int> callbacks = new Dictionary<AIShip, int>();
    static void RoleProbe(AIShip pilot) {
        // Count after the production eligibility guards, without invoking
        // role-specific Unity station/spawn services in this boundary fixture.
        callbacks.TryGetValue(pilot, out int count); callbacks[pilot] = count + 1;
    }
    static IEnumerable<CodeInstruction> RoleBody(IEnumerable<CodeInstruction> _) => new[] {
        new CodeInstruction(OpCodes.Ldarg_0), new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SffDecisionChecks), nameof(RoleProbe))),
        new CodeInstruction(OpCodes.Ret)
    };
    static void SafeCoast() {
        Setup(); guiActive = false;
        omitQueue = true;
        ((AIShipQueue)AccessTools.Field(typeof(AIShipManager), "_aIQueue").GetValue(null)).Clear();
        AIShipManager.dictAIs["fixture-region"].Clear();
        OrbitalMotion.BodyAt(planet, epoch, out var center);
        var radius = OrbitalMotion.Read(ship.objSS).Position - center.Position;
        var velocity = center.Velocity + new DVector(-radius.Y, radius.X) *
            (Math.Sqrt(OrbitalMotion.GravityConstant * planet.fMass / radius.Length) / radius.Length);
        ship.objSS.vVelX = velocity.X / OrbitalMotion.KmPerAU;
        ship.objSS.vVelY = velocity.Y / OrbitalMotion.KmPerAU;
    }
    static int drainCalls, nativeMoves, nativeDisposals;
    static IEnumerator Drain() { drainCalls++; yield return "pending-worker"; }
    static bool SchedulerBoundary(ref IEnumerator __result) { __result = Drain(); return false; }
    static IEnumerator NativeSequence() { try { nativeMoves++; yield return "native-loading"; nativeMoves++; } finally { nativeDisposals++; } }
    static void PassAdapterChecks(Action<bool, string> check) {
        var guards = new Harmony("offline.pass-adapter.guards");
        try {
            guards.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.StopManeuver)),
                prefix: new HarmonyMethod(typeof(SffDecisionChecks), nameof(CompletionEngineBoundary)));
            guards.CreateClassProcessor(typeof(SffBeforeCatchupPatch)).Patch();
            guards.CreateClassProcessor(typeof(SffCatchupPatch)).Patch();
            guards.Patch(AccessTools.Method(typeof(AIShip), nameof(AIShip.FFWD)),
                transpiler: new HarmonyMethod(typeof(SffDecisionChecks), nameof(RoleBody)));

            foreach (double duration in new[] { .5, 30, 60, 1500, 3599, 3600, 43201, 172801 }) {
                SafeCoast();
                // Stay inside Ceres' strongest-gravity region; this integrator
                // intentionally switches parent when solar gravity dominates.
                if (duration > 3600) {
                    OrbitalMotion.BodyAt(planet, epoch, out var parent);
                    var r = new DVector(1000, 0);
                    OrbitalMotion.Write(ship.objSS, new MotionState(parent.Position + r,
                        parent.Velocity + new DVector(0, Math.Sqrt(OrbitalMotion.GravityConstant * planet.fMass / r.Length))));
                }
                var initial = OrbitalMotion.Read(ship.objSS);
                OrbitalMotion.BodyAt(planet, epoch, out var center);
                callbacks.Clear(); AIShipManager.dictAIs["fixture-region"].Add(controller);
                stepAction = seconds => {
                    check(JumpBoundary.Active && JumpBoundary.Current.Source == "pass" && !GUIFFWD.Active,
                        "PASS world window has the wrong boundary/UI flag");
                    AIShipManager.FFWD(seconds);
                };
                long generation = JumpBoundary.Generation;
                SffAdvance.Pass(world, duration);
                OrbitalMotion.BodyAt(planet, StarSystem.fEpoch, out var finalCenter);
                double radiusError = Math.Abs((OrbitalMotion.Read(ship.objSS).Position - finalCenter.Position).Length - (initial.Position - center.Position).Length);
                check(StarSystem.fEpoch == epoch + duration && radiusError < .001 && !GUIFFWD.Active && !JumpBoundary.Active && !SffAdvance.Active,
                    "PASS changed elapsed time, circular orbit or scope: duration=" + duration + " radiusErrorKm=" + radiusError);
                check(JumpBoundary.Generation == generation + 2 && callbacks.Count == (duration >= 3600 ? 1 : 0) && callbacks.Values.All(n => n == 1),
                    "PASS changed hourly native role eligibility: duration=" + duration);
                if (duration > 21600) check(worldSteps == (int)Math.Ceiling(duration / 21600), "Long PASS interval did not use bounded six-hour windows");
            }

            foreach (double arrivalOffset in new[] { 30d, -60d }) {
                SafeCoast();
                var route = new NavData(ship);
                route.AddNavDataPoint(new NavDataPoint(epoch - 120, new ShipSitu(ship.objSS, false), 100, 0, 0, 8));
                route.AddNavDataPoint(new NavDataPoint(epoch + arrivalOffset, new ShipSitu(ship.objSS, false), 100, 0, 0, 8));
                ship.objSS.NavData = route;
                SffAdvance.Pass(world, 30);
                check(StarSystem.fEpoch == epoch + 30 && (arrivalOffset == 30 ? ship.objSS.NavData != null && worldSteps == 1 : ship.objSS.NavData == null && worldSteps == 2),
                    "PASS overshot an exact-end arrival or backdated an already expired route");
            }
            SafeCoast(); SffSimulation.Enabled = false;
            stepAction = _ => check(JumpBoundary.Active && !SffAdvance.Active && !GUIFFWD.Active,
                "Collision-only PASS activated disabled orbital simulation");
            SffAdvance.Pass(world, 30); SffSimulation.Enabled = true;
            check(worldSteps == 1 && !JumpBoundary.Active, "Disabled simulation did not preserve native PASS progression");

            SafeCoast(); OrbitalSimulation.ObserveUpdate(world, 1500);
            var movingTarget = Destination(epoch);
            var freshTarget = new ShipSitu(movingTarget, false);
            using (var context = SffSimulation.Begin(world, 1500, true)) {
                var plan = ((List<SffSimulation.Plan>)AccessTools.Field(typeof(SffSimulation), "plans").GetValue(context)).Single();
                plan.Travel = plan.Applied = true; plan.Target = movingTarget; plan.Handoff = .01; plan.ArrivalSpeed = .001;
                StarSystem.fEpoch = epoch + 1500;
                freshTarget.UpdateTime(StarSystem.fEpoch);
                OrbitalMotion.Write(ship.objSS, OrbitalMotion.Read(freshTarget));
                check((OrbitalMotion.Read(movingTarget).Position - OrbitalMotion.Read(freshTarget).Position).Length > 1,
                    "PASS target-refresh control was not stale");
                context.BeforeAi();
                check(plan.Ready && (OrbitalMotion.Read(movingTarget).Position - OrbitalMotion.Read(freshTarget).Position).Length < .001,
                    "PASS arrival gating compared against old body-locked target coordinates");
            }

            // The native callback budget belongs to the original interval,
            // even when a course endpoint introduces an additional window.
            foreach (double duration in new[] { 1500, 3600 }) {
                SafeCoast(); callbacks.Clear();
                var original = controller;
                AIShipManager.dictAIs["fixture-region"].Add(original);
                var course = new NavData(ship);
                course.AddNavDataPoint(new NavDataPoint(epoch, new ShipSitu(ship.objSS, false), 100, 0, 0, 8));
                var end = new ShipSitu(ship.objSS, false);
                course.AddNavDataPoint(new NavDataPoint(epoch + 300, end, 100, 0, 0, 8));
                ship.objSS.NavData = course;
                AIShip newborn = null;
                stepAction = seconds => {
                    AIShipManager.FFWD(seconds);
                    if (newborn == null) {
                        newborn = Empty<AIShip>(); Set(newborn, "_shipUs", ship);
                        AIShipManager.dictAIs["fixture-region"].Add(newborn);
                    }
                };
                SffAdvance.Pass(world, duration);
                check(ship.objSS.NavData == null && worldSteps == 2 && StarSystem.fEpoch == epoch + duration,
                    "PASS did not split once at successful native course completion");
                check(callbacks.ContainsKey(original) == (duration >= 3600) && !callbacks.ContainsKey(newborn),
                    "Split PASS callback mismatch: duration=" + duration + " original=" + callbacks.ContainsKey(original) +
                    " newborn=" + callbacks.ContainsKey(newborn) + " receipt=" + Newtonsoft.Json.JsonConvert.SerializeObject(SffAdvance.Snapshot()));
            }

            // The factory wrapper drains native end-turn work before advancing
            // the passenger iterator. No discontinuity scope spans a yield.
            guards.Patch(AccessTools.Method(typeof(Condowner.Utils.Scheduler), "CompletePendingEndTurns"),
                prefix: new HarmonyMethod(typeof(SffDecisionChecks), nameof(SchedulerBoundary)));
            drainCalls = nativeMoves = nativeDisposals = 0;
            IEnumerator native = NativeSequence(), wrapped = native;
            PassPendingEndTurnsPatch.Postfix(30, ref wrapped);
            check(!ReferenceEquals(wrapped, native) && wrapped.MoveNext() && wrapped.Current is IEnumerator && nativeMoves == 0 && !JumpBoundary.Active,
                "PASS did not wait for pending native end turns before loading");
            var drain = (IEnumerator)wrapped.Current;
            check(drain.MoveNext() && drainCalls == 1 && nativeMoves == 0 && !JumpBoundary.Active,
                "PASS advanced native passenger work while the scheduler was pending");
            check(!drain.MoveNext() && wrapped.MoveNext() && (string)wrapped.Current == "native-loading" && nativeMoves == 1 && !JumpBoundary.Active,
                "PASS did not resume native coroutine yields after draining");
            ((IDisposable)wrapped).Dispose();
            check(nativeDisposals == 1 && !JumpBoundary.Active, "Cancelled PASS retained its native iterator/scope");
            foreach (double jump in new[] { 0d, -1, double.NaN }) {
                native = NativeSequence(); wrapped = native;
                PassPendingEndTurnsPatch.Postfix(jump, ref wrapped);
                check(ReferenceEquals(native, wrapped), "Ordinary/invalid teleport acquired a PASS scheduler wrapper");
            }
            guards.CreateClassProcessor(typeof(PassPendingEndTurnsPatch)).Patch();
            var factory = AccessTools.Method(typeof(CrewSim), "_TeleportCO");
            wrapped = (IEnumerator)factory.Invoke(Empty<CrewSim>(), new object[] { null, null, 30d });
            check(wrapped.MoveNext() && wrapped.Current is IEnumerator, "Native PASS factory did not acquire its drain wrapper");
            drain = (IEnumerator)wrapped.Current; while (drain.MoveNext()) { }
            check(!wrapped.MoveNext() && !JumpBoundary.Active, "Native null-passenger exit leaked a coroutine scope");
            check(SffDecisionStepsPatch.Targets().Count() == 2 && SffDecisionStepsPatch.PassTarget() != null,
                "Native PASS/GUI iterator targets did not bind");
            var worldCall = new CodeInstruction(OpCodes.Callvirt, AccessTools.Method(typeof(StarSystem), nameof(StarSystem.Update)));
            check(SffDecisionStepsPatch.Transpiler(new[] { worldCall }, SffDecisionStepsPatch.PassTarget()).Single().Calls(AccessTools.Method(typeof(SffAdvance), nameof(SffAdvance.Pass))),
                "PASS iterator transpiler was wired to the GUI-only adapter");

            SafeCoast(); throwOnStep = true;
            long beforeFailure = JumpBoundary.Generation;
            try { SffAdvance.Pass(world, 30); } catch (InvalidOperationException) { }
            check(!JumpBoundary.Active && !SffAdvance.Active && JumpBoundary.Generation == beforeFailure + 2,
                "Failed PASS leaked its active interval or collision-history generation");
            using (var outer = new JumpBoundary("outer")) {
                using (var inner = new JumpBoundary("inner")) check(ReferenceEquals(JumpBoundary.Current, inner), "Nested boundary entry failed");
                check(ReferenceEquals(JumpBoundary.Current, outer), "Nested boundary did not restore its caller");
            }
            Console.WriteLine("PASS adapter: short/long physics, strict native course completion, original hourly role budget, newborn identity exclusion, scheduler drain/cancellation and scope cleanup passed.");
        } finally { stepAction = null; guards.UnpatchSelf(); }
    }
}
