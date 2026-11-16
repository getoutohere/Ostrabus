using System;
using HarmonyLib;
using Ostrabus.Orbital;
using Ostranauts.ShipGUIs.Utilities;
using UnityEngine;

// Study-only negative control. The shared completion bugfix releases drives
// correctly, but a bulk scope alone does not own a plotted route's coast tail.
static partial class SffDecisionChecks {
    static bool CompletionEngineBoundary(Ship __instance) {
        __instance.objSS.vAccIn = __instance.objSS.vAccRCS = default;
        return false;
    }
    static void PassCompletionStudy(Action<bool, string> check, Harmony h) {
        h.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.StopManeuver)),
            prefix: new HarmonyMethod(typeof(SffDecisionChecks), nameof(CompletionEngineBoundary)));
        MotionState unscoped = default;
        foreach (int mode in new[] { 0, 1, 2, 3 }) {
            bool shared = mode != 0, split = mode >= 2, actualAdapter = mode == 3;
            Setup(); guiActive = false;
            const double arrivalAfter = 300, total = 1500;
            double arrival = epoch + arrivalAfter, end = epoch + total;
            OrbitalMotion.BodyAt(planet, epoch, out var center);
            var radius = OrbitalMotion.Read(ship.objSS).Position - center.Position;
            double mu = OrbitalMotion.GravityConstant * planet.fMass;
            var tangent = new DVector(-radius.Y, radius.X) * (Math.Sqrt(mu / radius.Length) / radius.Length);
            OrbitalMotion.BodyAt(planet, arrival, out var atArrival);
            var endpoint = new MotionState(atArrival.Position + radius, atArrival.Velocity + tangent);
            var terminal = new ShipSitu(false);
            OrbitalMotion.Write(terminal, endpoint);
            terminal.vAccRCS = new Vector2(1e-12f, 0);
            var course = new NavData(ship);
            course.AddNavDataPoint(new NavDataPoint(epoch, new ShipSitu(ship.objSS, false), ship.fShallowRCSRemass, 0, 0, ship.RCSCount));
            course.AddNavDataPoint(new NavDataPoint(arrival, terminal, ship.fShallowRCSRemass, 0, 0, ship.RCSCount));
            ship.objSS.NavData = course;
            OrbitAcceleration.TryFrame(planet, arrival, out var frame, out _);
            var ambient = new DVector(frame.X, frame.Y) * OrbitalMotion.KmPerAU + OrbitalMotion.Gravity(radius, mu);
            ship.objSS.vAccEx = new Vector2((float)(ambient.X / OrbitalMotion.KmPerAU), (float)(ambient.Y / OrbitalMotion.KmPerAU));
            var environment = new SffEnvironment();
            foreach (var body in world.aBOs.Values) environment.Bodies.Add(new SffBody(body));
            var correct = SffDynamics.Propagate(environment, endpoint, arrival, total - arrivalAfter, default, null,
                ship.objSS.GetRadiusAU() * OrbitalMotion.KmPerAU, null);
            OrbitalSimulation.ObserveUpdate(world, total);
            // Native completion tests strictly greater-than arrival. Use the
            // next representable epoch, without adding a fixed time epsilon.
            double firstEnd = split ? BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(arrival) + 1) : end;
            if (actualAdapter) {
                ship.objSS.vAccEx = default;
                gravitySample = new Vector2((float)(ambient.X / OrbitalMotion.KmPerAU), (float)(ambient.Y / OrbitalMotion.KmPerAU));
                omitQueue = true;
                SffAdvance.Pass(world, total);
                check(worldSteps == 2 && StarSystem.fEpoch == end, "Deployed PASS adapter did not cut once at the native completion boundary");
            } else {
            using (var context = shared ? SffSimulation.Begin(world, firstEnd - epoch, true) : null) {
                StarSystem.fEpoch = firstEnd;
                ship.objSS.TimeAdvance(firstEnd - epoch);
            }
            if (split) {
                // StarSystem.UpdateShip restores its saved external acceleration
                // after removing the temporary gravity sample.
                ship.objSS.vAccEx = default;
                using (var context = SffSimulation.Begin(world, end - firstEnd, true)) {
                    StarSystem.fEpoch = end;
                    ship.objSS.TimeAdvance(end - firstEnd);
                }
            }
            }
            {
                var actual = OrbitalMotion.Read(ship.objSS);
                double error = (actual.Position - correct.State.Position).Length;
                check(ship.objSS.NavData == null && ship.objSS.vAccRCS.x == 0 && !GUIFFWD.Active,
                    "Study failed to execute the existing non-GUI successful-completion repair");
                check(correct.Impact == null && (split ? error < .001 : error > 1),
                    split ? "Completion-boundary prototype did not reuse conic coasting correctly" : "Bulk route-completion coast gap did not reproduce");
                if (!shared) unscoped = actual;
                else if (!split) check((actual.Position - unscoped.Position).Length < .0001,
                    "An explicit motion scope unexpectedly took ownership of the excluded plotted route");
                Console.WriteLine("PASS route handoff study: sharedScope=" + shared + "; splitAtCompletion=" + split +
                    "; deployedAdapter=" + actualAdapter + "; true coast remainder=1200s; position error versus existing conic engine=" + error + " km.");
            }
        }
    }
}
