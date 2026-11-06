using System;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Ostrabus.Orbital;

static class EphemerisBoundaryChecks {
    static double N(JToken data, string name) => data[name].Value<double>();
    internal static void Run(Action<bool, string> check) {
        var captured = JObject.Parse(FixtureFiles.Read("VORB-BOUND-CURRENT.json"))["data"]["bodies"]["Venus"];
        var planet = new BodyOrbit(new JsonBodyOrbitSave { strName = "Venus", fAxis1 = N(captured, "majorAxisAU"),
            fAxis2 = N(captured, "minorAxisAU"), fPerh = N(captured, "periapsisAU"), fAph = N(captured, "apoapsisAU"),
            fPeriod = N(captured, "periodSeconds"), fEcc = N(captured, "eccentricity"),
            fPeriodShift = N(captured, "phaseSeconds"), fAngle = N(captured, "angleDegrees"), nOrbitDirection = 1 });
        double epoch = N(captured, "positionCalculationEpoch");
        planet.UpdateTime(epoch - 1, bCalcV: false); planet.UpdateTime(epoch, bCalcV: false);
        check(OrbitalMotion.BodyAt(planet, epoch, out var predicted), "SFF ephemeris unavailable");
        double error = (predicted.Position - new DVector(planet.dXReal, planet.dYReal) * OrbitalMotion.KmPerAU).Length;
        Console.WriteLine("Captured Venus SFF/native coordinate discrepancy: " + error + " km.");
        check(error < .000002, "SFF body position disagrees with native coordinates: " + error + " km");
        var solve = typeof(BodyOrbit).GetMethod("SolveBigE", BindingFlags.Static | BindingFlags.NonPublic);
        foreach (double eccentricity in new[] { 0, .0068, .45, .9, .99 }) {
            foreach (int direction in new[] { -1, 1 }) {
                var body = new BodyOrbit(new JsonBodyOrbitSave { strName = "probe", fAxis1 = 2,
                    fAxis2 = 2 * Math.Sqrt(1 - eccentricity * eccentricity), fPerh = 1 - eccentricity,
                    fAph = 1 + eccentricity, fPeriod = 31556926, fPeriodShift = 12345,
                    fEcc = eccentricity, fAngle = 37, nOrbitDirection = direction }) { boParent = planet };
                foreach (double time in new[] { -31569272.0, -100.0, 0, 1, 31544581, 31544582, epoch, epoch + 21600 }) {
                    body.UpdateTime(time - 1, bCalcV: false); body.UpdateTime(time, bCalcV: false);
                    double x = body.dXReal, y = body.dYReal, stamp = body.dTimeCalcLast, velocityStamp = body.dVelCalcLast;
                    check(NativeEphemeris.State(body, time, out var p, out var v), "Nested ephemeris unavailable");
                    check((p - new DVector(x, y)).Length * OrbitalMotion.KmPerAU < .000002, "Native nested position mismatch");
                    check(CoastIntegration.NativePosition(body, time, out var coast) && (p - coast).Length == 0, "SFF/coast boundary mismatch");
                    check(body.dXReal == x && body.dYReal == y && body.dTimeCalcLast == stamp && body.dVelCalcLast == velocityStamp,
                        "Pure ephemeris modified native caches");
                    DVector expectedVelocity = default;
                    for (var current = body; current != null; current = current.boParent) {
                        double mean = Math.PI * 2 * ((time + current.fPeriodShift) % current.fPeriod) / current.fPeriod;
                        double e = (double)solve.Invoke(null, new object[] { mean, current.fEcc });
                        double rate = Math.PI * 2 / current.fPeriod / (1 - current.fEcc * Math.Cos(e));
                        double vx = -current.fAxis1 / 2 * Math.Sin(e) * rate;
                        double vy = current.fAxis2 / 2 * current.nOrbitDirection * Math.Cos(e) * rate;
                        double angle = current.fAngle * (Math.PI / 180);
                        expectedVelocity += new DVector(vx * Math.Cos(angle) - vy * Math.Sin(angle), vx * Math.Sin(angle) + vy * Math.Cos(angle));
                    }
                    check((v - expectedVelocity).Length < 1e-20, "Velocity phase differs from native solver");
                    double cacheVx = body.dVelX, cacheVy = body.dVelY;
                    body.dVelX = 123; body.dVelY = -456;
                    check(NativeEphemeris.State(body, time, out _, out var independent) && (independent - v).Length == 0 &&
                        body.dVelX == 123 && body.dVelY == -456, "Prediction reads or overwrites cached native velocity");
                    body.dVelX = cacheVx; body.dVelY = cacheVy;
                }
            }
        }
        var fixedBody = new BodyOrbit(new JsonBodyOrbitSave { strName = "fixed", fPeriod = 1, fPerh = .01 }) { boParent = planet };
        fixedBody.fPeriod = 0;
        check(NativeEphemeris.State(fixedBody, epoch, out var fixedPosition, out var fixedVelocity) && fixedPosition.Finite && fixedVelocity.Finite,
            "Stationary placeholder needs no orbital period");
        fixedBody.boParent = fixedBody;
        check(!NativeEphemeris.State(fixedBody, epoch, out _, out _), "Cyclic hierarchy not rejected");
        check(!NativeEphemeris.State(planet, double.NaN, out _, out _), "Invalid epoch not rejected");
        double savedPeriod = planet.fPeriod;
        planet.fPeriod = 0;
        check(!NativeEphemeris.State(planet, epoch, out _, out _), "Invalid moving orbit not rejected");
        planet.fPeriod = savedPeriod;
    }
}
