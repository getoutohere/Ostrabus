using System;
using Ostrabus.NavConsole.Rotation;

static partial class Program {
    static void MovingTargets() {
        foreach (double dt in new[] { .02, .1, .5, 1.0 })
        foreach (double rate in new[] { .0005, .01, .05, .4 }) {
            RotationRuntime.Cleanup();
            var ship = Hull("moving-hold-" + dt + "-" + rate);
            var target = Hull("moving-target-" + dt + "-" + rate);
            double radius = 1000 / 149597870700.0;
            target.objSS.vPosy = radius;
            var hold = Order(ship, 0, hold: true, target: target.strRegID);
            double maxError = 0, lateFuel = 0;
            for (double t = 0; t < 90; t += dt) {
                double angle = rate * t;
                target.objSS.vPosx = -Math.Sin(angle) * radius;
                target.objSS.vPosy = Math.Cos(angle) * radius;
                double before = ship.fShallowRCSRemass;
                RotationRuntime.SampleWorld();
                StarSystem.fEpoch += dt;
                ship.objSS.TimeAdvance(dt);
                if (t > 30) {
                    maxError = Math.Max(maxError, Math.Abs(RotationPid.Wrap(ship.objSS.fRot - (angle + rate * dt))) * 180 / Math.PI);
                    lateFuel += before - ship.fShallowRCSRemass;
                }
            }
            Console.WriteLine("MOVING dt=" + dt + " rate=" + rate + " maxErrorDeg=" + maxError + " trackingFuel=" + lateFuel);
            Check(hold.Order.Active && hold.Order.Hold, "Moving-target hold silently ended");
            Check(maxError < .04, "Changing line of sight drifted outside the tracking hysteresis dt=" + dt + " rate=" + rate);
            Check(lateFuel < .02, "Matched angular motion kept spending substantial RCS fuel");
        }
        RotationRuntime.Cleanup();
        var stationary = Hull("quiet-hold");
        stationary.objSS.fRot = (float)(-.2 * Math.PI / 180);
        var command = Order(stationary, 0, hold: true);
        Simulate(stationary, command, .02, 60);
        double fuel = stationary.fShallowRCSRemass;
        Simulate(stationary, command, .02, 60);
        Console.WriteLine("STATIC errorDeg=" + Math.Abs(RotationPid.Wrap(stationary.objSS.fRot)) * 180 / Math.PI + " idleFuel=" + (fuel - stationary.fShallowRCSRemass));
        Check(Math.Abs(RotationPid.Wrap(stationary.objSS.fRot)) <= RotationPid.AngleTolerance && fuel == stationary.fShallowRCSRemass,
            "Stationary hold retained the old 0.2-degree offset or kept spending fuel");
        RotationRuntime.Cleanup();
    }
}
