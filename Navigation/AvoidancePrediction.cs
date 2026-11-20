using System;
using Ostrabus.Orbital;
using Ostranauts.ShipGUIs.Utilities;

namespace Ostrabus.ShipNavigation;

// Optional geometric confirmation of a broad prediction segment. Angles are
// displacements from each controlling hull's current orientation. A true result
// also represents an unresolved/budget-limited query, never an assumed miss.
internal interface IApproachProbe {
    bool Collides(DVector position, DVector velocity, DVector acceleration, double elapsed, double seconds,
        double ourStart, double ourEnd, double theirStart, double theirEnd);
}

// AI caller only. Shared native projectile/history collision tests retain their
// elapsed-interval semantics and numeric thresholds. Units here are metres.
internal static class AvoidancePrediction
{
    const double MetresPerAU = OrbitalMotion.KmPerAU * 1000;
    internal const double AccelerationSeconds = 30;

    internal static bool Collision(ShipSitu us, ShipSitu them, double seconds, NavData ourRoute = null, NavData theirRoute = null,
        ShipSitu ourOrigin = null, ShipSitu theirOrigin = null, IApproachProbe probe = null)
    {
        if (AvoidanceRouteForecast.TryCollision(us, them, seconds, ourRoute ?? us.NavData, theirRoute ?? them.NavData, out bool planned, ourOrigin, theirOrigin, probe)) return planned;
        var p = new DVector(us.vPosx - them.vPosx, us.vPosy - them.vPosy) * MetresPerAU;
        var v = new DVector(us.vVelX - them.vVelX, us.vVelY - them.vVelY) * MetresPerAU;
        var a = (Acceleration(us) - Acceleration(them)) * MetresPerAU;
        double radius = (double)CollisionManager.GetCollisionDistanceAU(us, them) * MetresPerAU;
        if (!p.Finite || !v.Finite || !a.Finite || !OrbitAcceleration.Finite(radius) || radius < 0)
            return CollisionManager.WillCollide(us, them, seconds);

        // Preserve the native stopping-time horizon for fast approaches. Its
        // default 30 s escape projection also supplies useful lookahead at rest.
        double horizon = seconds >= AccelerationSeconds ? seconds : AccelerationSeconds;
        double squared = radius * radius;
        if (p.Dot(p) <= squared && probe == null) return true;
        if (a.Dot(a) == 0) return LinearMinimum(p, v, horizon) <= squared &&
            Confirm(probe, p, v, a, 0, horizon, us, them);

        // Admit using the same radii/motion as the precise test. The old caller
        // rejected by centre distance versus braking distance before hull sizes
        // or an accelerating departure could be considered.
        double burn = Math.Min(horizon, AccelerationSeconds);
        double reach = double.IsPositiveInfinity(horizon) ? double.PositiveInfinity :
            v.Length * horizon + a.Length * burn * (horizon - .5 * burn);
        if (p.Length > radius + reach) return false;

        // Current acceleration is a short local forecast, not an assumption
        // that this thrust continues for a possibly much longer braking window.
        if (ConstantAccelerationApproach.MinimumSquared(p, v, a, burn) <= squared &&
            Confirm(probe, p, v, a, 0, burn, us, them)) return true;
        if (!(horizon > burn)) return false;
        var tailPosition = p + v * burn + a * (.5 * burn * burn);
        var tailVelocity = v + a * burn;
        return LinearMinimum(tailPosition, tailVelocity, horizon - burn) <= squared &&
            Confirm(probe, tailPosition, tailVelocity, default, burn, horizon - burn, us, them);
    }

    static bool Confirm(IApproachProbe probe, DVector p, DVector v, DVector a, double elapsed, double seconds,
        ShipSitu us, ShipSitu them) => probe == null || probe.Collides(p, v, a, elapsed, seconds,
            us.fW * elapsed, us.fW * (elapsed + seconds), them.fW * elapsed, them.fW * (elapsed + seconds));

    static DVector Acceleration(ShipSitu s) => new DVector(
        (double)s.vAccRCS.x + s.vAccIn.x + s.vAccEx.x + s.vAccLift.x + s.vAccDrag.x,
        (double)s.vAccRCS.y + s.vAccIn.y + s.vAccEx.y + s.vAccLift.y + s.vAccDrag.y);

    static double LinearMinimum(DVector p, DVector v, double seconds)
    {
        double speedSquared = v.Dot(v);
        // No game-speed epsilon: metres/second can safely be much smaller than
        // the native AU/second squared cutoff equivalent to about 150 m/s.
        double time = speedSquared > 0 ? Math.Max(0, Math.Min(seconds, -p.Dot(v) / speedSquared)) : 0;
        var closest = p + v * time;
        return closest.Dot(closest);
    }
}
