using System;

static partial class Program
{
    // Capture native coordinates before registering any generated plugin hooks.
    // Then execute the same query with the feature present OR deliberately omitted.
    static Action<bool> CaptureOrbitalVelocityControl()
    {
        var parent = new BodyOrbit(new JsonBodyOrbitSave
        {
            strName = "custom-parent", fAxis1 = 1.4, fAxis2 = 1.3,
            fPerh = .5, fPeriod = 19407509, fEcc = .2, nOrbitDirection = 1
        });
        var body = new BodyOrbit(new JsonBodyOrbitSave
        {
            strName = "custom-station", fAxis1 = .0002, fAxis2 = .00015,
            fPerh = .00007, fPeriod = 14470, fEcc = .3, nOrbitDirection = -1
        }) { boParent = parent };
        const double epoch = 101.125;
        body.UpdateTime(epoch);
        double nativeX = body.dXReal, nativeY = body.dYReal;
        double nativeVX = body.dVelX, nativeVY = body.dVelY;
        return included =>
        {
            body.dTimeCalcLast = body.dVelCalcLast = -1000;
            parent.dTimeCalcLast = parent.dVelCalcLast = -1000;
            body.UpdateTime(epoch);
            if (body.dXReal != nativeX || body.dYReal != nativeY)
                throw new Exception("Generated build changed native orbital coordinates.");
            body.UpdateTime(epoch + .25);
            if (included)
            {
                if (body.dVelCalcLast != epoch + .25 || parent.dVelCalcLast != epoch + .25)
                    throw new Exception("Selected orbital velocity repair did not refresh subsecond hierarchy velocities.");
            }
            else if (body.dVelCalcLast != epoch || body.dVelX != nativeVX || body.dVelY != nativeVY)
                throw new Exception("Omitting orbital velocity repair changed native velocity behavior.");
            Console.WriteLine("Custom orbital velocity: native coordinates preserved; feature " +
                (included ? "executes synchronized velocities." : "omission retains native caches."));
        };
    }
}
