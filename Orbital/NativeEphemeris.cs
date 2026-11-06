using System;

namespace Ostrabus.Orbital;

// Detached BodyOrbit coordinates: match native SolveBigE's stopping rule and
// arithmetic, without changing its shared position/velocity/time caches.
internal static class NativeEphemeris {
    internal static bool Position(BodyOrbit body, double epoch, out DVector position) => Evaluate(body, epoch, false, out position, out _);
    internal static bool State(BodyOrbit body, double epoch, out DVector position, out DVector velocity) => Evaluate(body, epoch, true, out position, out velocity);

    static bool Evaluate(BodyOrbit body, double epoch, bool includeVelocity, out DVector position, out DVector velocity) {
        position = velocity = default;
        if (body == null || !OrbitAcceleration.Finite(epoch)) return false;
        int depth = 0;
        for (BodyOrbit current = body; current != null; current = current.boParent) {
            if (++depth > 32) return false;
            double a = current.fAxis1 * .5, b = current.fAxis2 * .5, ecc = current.fEcc;
            if (!OrbitAcceleration.Finite(a) || !OrbitAcceleration.Finite(b) || !OrbitAcceleration.Finite(current.fPerh) ||
                !OrbitAcceleration.Finite(ecc) || ecc < 0 || ecc >= 1 || a < 0 || b < 0 || !OrbitAcceleration.Finite(current.fAngle)) return false;
            double e = 0, rate = 0;
            if (a != 0 || b != 0) {
                double period = current.fPeriod;
                if (!(period > 0) || !OrbitAcceleration.Finite(period) || !OrbitAcceleration.Finite(current.fPeriodShift)) return false;
                double mean = Math.PI * 2 * ((epoch + current.fPeriodShift) % period) / period;
                if (!OrbitAcceleration.Finite(mean)) return false;
                e = ecc < .8 ? mean : Math.PI;
                for (int i = 0; i < 10; i++) {
                    double error = e - ecc * Math.Sin(e) - mean;
                    if (Math.Abs(error) < 1e-6) break;
                    double derivative = 1 - ecc * Math.Cos(e);
                    if (Math.Abs(derivative) < 1e-10) derivative = derivative >= 0 ? 1e-10 : -1e-10;
                    e -= error / derivative;
                }
                double turns = Math.Floor(mean / (Math.PI * 2));
                e %= Math.PI * 2;
                if (e < 0) e += Math.PI * 2;
                e += Math.PI * 2 * turns;
                // Derive velocity at the same solved orbital phase. Do not copy
                // native dVel, whose finite-difference cache may describe another epoch.
                if (includeVelocity) rate = Math.PI * 2 / period / (1 - ecc * Math.Cos(e));
            }
            double sin = Math.Sin(e), cos = Math.Cos(e);
            double x = a * cos - (a - current.fPerh), y = b * current.nOrbitDirection * sin;
            double angle = current.fAngle * (Math.PI / 180), ca = Math.Cos(angle), sa = Math.Sin(angle);
            position += new DVector(x * ca - y * sa, x * sa + y * ca);
            if (includeVelocity) {
                double vx = -a * sin * rate, vy = b * current.nOrbitDirection * cos * rate;
                velocity += new DVector(vx * ca - vy * sa, vx * sa + vy * ca);
            }
        }
        return position.Finite && velocity.Finite;
    }
}
