using System;
using Ostrabus.Orbital;

static class SffTimingChecks {
    internal static void Run(Action<bool, string> check) {
        var sun = new BodyOrbit(new JsonBodyOrbitSave {
            strName = "Sol", fAxis1 = 0, fAxis2 = 0, fPeriod = 31556926,
            fMass = 1.989e30, fRadius = 696340d / OrbitalMotion.KmPerAU
        });
        var environment = new SffEnvironment();
        environment.Bodies.Add(new SffBody(sun));
        var initial = new MotionState(new DVector(OrbitalMotion.KmPerAU, 0), new DVector(0, 29.78));
        foreach (double start in new[] { 0d, 1000000.1, 65634347414.919693, 65634325602.787415 })
        foreach (double seconds in new[] { .001, .01, .02, .03, .1, 1d, 10.123456, 60d, 123.456789, 7200.020003 })
        foreach (bool powered in new[] { false, true }) {
            var engines = powered ? new DVector(.0001, -.0002) : default;
            double represented = start + seconds - start;
            SffOutcome expected, actual;
            try {
                expected = SffDynamics.Propagate(environment, initial, start, represented, engines, null, 0, null);
                actual = SffDynamics.Propagate(environment, initial, start, seconds, engines, null, 0, null);
            } catch (Exception error) {
                throw new Exception("Timing case: start=" + start + " seconds=" + seconds + " powered=" + powered, error);
            }
            check(actual.State.Finite && !actual.BudgetFallback && actual.Impact == null, "Fractional SFF failed or changed outcome");
            check(actual.State.Position.X == expected.State.Position.X && actual.State.Position.Y == expected.State.Position.Y &&
                actual.State.Velocity.X == expected.State.Velocity.X && actual.State.Velocity.Y == expected.State.Velocity.Y,
                "Fractional interval disagrees with the native clock endpoint: start=" + start + " seconds=" + seconds);
            check(actual.Steps == expected.Steps, "Fractional interval added a tail integration step");
        }
        double epoch = 65634347414.919693;
        double next = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(epoch) + 1);
        var tiny = SffDynamics.Propagate(environment, initial, epoch, (next - epoch) * .25, default, null, 0, null);
        check(tiny.Steps == 0 && tiny.State.Position.X == initial.Position.X && tiny.State.Velocity.Y == initial.Velocity.Y,
            "Interval below clock resolution invented motion or required native fallback");
        Console.WriteLine("SFF timing: fractional coasts/powered segments match native representable endpoints without remainder fallbacks.");
    }
}
