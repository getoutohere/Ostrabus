using System;

namespace Ostrabus.Orbital;

internal static class CoastOrbitFit
{
    // Match the native factory's units and gravitational constant.
    const double AuMeters = 149597863936d;
    const double GravityConstant = 6.674080038626684E-11;
    const double Tau = 2 * Math.PI;
    const double MaximumVelocityErrorMps = .005;
    const double MaximumPositionOffsetMeters = 5;

    internal static BodyOrbit Fit(Ship ship, BodyOrbit parent)
    {
        if (ship?.objSS == null || parent == null)
            return null;
        double epoch = StarSystem.fEpoch;
        // Preserve the native factory's synchronization of the incoming situation.
        ship.objSS.UpdateTime(epoch);
        if (!OrbitVelocity.TryState(parent, epoch, out var reference))
            return null;

        double rx = (ship.objSS.vPosx - reference.X) * AuMeters;
        double ry = (ship.objSS.vPosy - reference.Y) * AuMeters;
        double vx = (ship.objSS.vVelX - reference.VX) * AuMeters;
        double vy = (ship.objSS.vVelY - reference.VY) * AuMeters;
        double radius = Math.Sqrt(rx * rx + ry * ry);
        double speedSquared = vx * vx + vy * vy;
        double mu = GravityConstant * parent.fMass;
        if (!(radius > 0) || !(mu > 0) || !OrbitVelocity.Finite(mu))
            return null;

        double energy = speedSquared / 2 - mu / radius;
        double momentum = rx * vy - ry * vx;
        if (!(energy < 0) || Math.Abs(momentum) < 1e-12)
            return null;

        double radialVelocity = rx * vx + ry * vy;
        double scale = speedSquared - mu / radius;
        double ex = (scale * rx - radialVelocity * vx) / mu;
        double ey = (scale * ry - radialVelocity * vy) / mu;
        double eccentricity = Math.Sqrt(ex * ex + ey * ey);
        double major = -mu / (2 * energy);
        if (!(eccentricity >= 0 && eccentricity < 1))
            return null;

        int direction = momentum < 0 ? -1 : 1;
        double angle = eccentricity < 1e-10 ? Math.Atan2(ry, rx) : Math.Atan2(ey, ex);
        double minor = major * Math.Sqrt(1 - eccentricity * eccentricity);
        double ca = Math.Cos(angle);
        double sa = Math.Sin(angle);
        double localX = rx * ca + ry * sa;
        double localY = -rx * sa + ry * ca;
        double anomaly = Math.Atan2(localY / (minor * direction), localX / major + eccentricity);
        double mean = anomaly - eccentricity * Math.Sin(anomaly);
        double period = Tau * Math.Sqrt(major * major * major / mu);
        double phase = (mean * period / Tau - epoch % period) % period;
        if (!(period > 0) || !OrbitVelocity.Finite(period) || !OrbitVelocity.Finite(phase))
            return null;

        // Keep native ship-body metadata and its ordinary serialization/removal.
        var result = new BodyOrbit(ship.strRegID,
            major * (1 - eccentricity) / AuMeters, major * (1 + eccentricity) / AuMeters,
            angle * 180 / Math.PI, eccentricity, period / 31556926.0,
            (float)ship.objSS.Size / 1000f, ship.Mass, 1.0, parent)
        {
            fPeriodShift = phase,
            nOrbitDirection = direction,
            nDrawFlagsBody = 1,
            nDrawFlagsTrack = 2
        };
        result.dTimeCalcLast = result.dVelCalcLast = double.NegativeInfinity;
        result.UpdateTime(epoch);
        if (!OrbitVelocity.TryState(result, epoch, out var actual))
            return null;

        double velocityError = Magnitude(actual.VX - ship.objSS.vVelX, actual.VY - ship.objSS.vVelY) * AuMeters;
        double positionError = Magnitude(actual.X - ship.objSS.vPosx, actual.Y - ship.objSS.vPosy) * AuMeters;
        // Native supports refusing an unsuitable conversion. Remain in free flight
        // rather than introduce an impulse or a large persistent orbital offset.
        if (velocityError > MaximumVelocityErrorMps || positionError > MaximumPositionOffsetMeters)
            return null;
        return result;
    }

    static double Magnitude(double x, double y) => Math.Sqrt(x * x + y * y);
}
