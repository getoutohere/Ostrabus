using System;
using Ostrabus.Orbital;
using Ostranauts.ShipGUIs.Utilities;

namespace Ostrabus.ShipNavigation;

// AI caller only. Shared native projectile/history collision tests retain their
// elapsed-interval semantics and numeric thresholds. Units here are metres.
internal static class AvoidancePrediction
{
    const double MetresPerAU = OrbitalMotion.KmPerAU * 1000;
    internal const double AccelerationSeconds = 30;

    internal static bool Collision(ShipSitu us, ShipSitu them, double seconds, NavData ourRoute = null, NavData theirRoute = null,
        ShipSitu ourOrigin = null, ShipSitu theirOrigin = null)
    {
        if (AvoidanceRouteForecast.TryCollision(us, them, seconds, ourRoute ?? us.NavData, theirRoute ?? them.NavData, out bool planned, ourOrigin, theirOrigin)) return planned;
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
        if (p.Dot(p) <= squared) return true;
        if (a.Dot(a) == 0) return LinearMinimum(p, v, horizon) <= squared;

        // Admit using the same radii/motion as the precise test. The old caller
        // rejected by centre distance versus braking distance before hull sizes
        // or an accelerating departure could be considered.
        double burn = Math.Min(horizon, AccelerationSeconds);
        double reach = double.IsPositiveInfinity(horizon) ? double.PositiveInfinity :
            v.Length * horizon + a.Length * burn * (horizon - .5 * burn);
        if (p.Length > radius + reach) return false;

        // Current acceleration is a short local forecast, not an assumption
        // that this thrust continues for a possibly much longer braking window.
        double minimum = ConstantAccelerationApproach.MinimumSquared(p, v, a, burn);
        if (horizon > burn)
            minimum = Math.Min(minimum, LinearMinimum(p + v * burn + a * (.5 * burn * burn), v + a * burn, horizon - burn));
        return minimum <= squared;
    }

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
