using System;

namespace Ostrabus.Orbital;

// Derivative of the prescribed BodyOrbit trajectory, in AU / s^2. Does not call
// UpdateTime, sample cached velocities, change ephemerides or evaluate gravity masses.
internal static class OrbitAcceleration {
    internal readonly struct Acceleration {
        internal readonly double X, Y;
        internal Acceleration(double x, double y) { X = x; Y = y; }
        internal double Magnitude => Math.Sqrt(X * X + Y * Y);
    }
    internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    internal static bool TryFrame(BodyOrbit body, double epoch, out Acceleration acceleration, out int depth) {
        acceleration = default; depth = 0;
        if (body == null || !Finite(epoch)) return false;
        double x = 0, y = 0;
        for (BodyOrbit current = body; current != null; current = current.boParent) {
            if (++depth > 32 || !TryLocal(current, epoch, out Acceleration local)) return false;
            x += local.X; y += local.Y;
        }
        if (!Finite(x) || !Finite(y)) return false;
        acceleration = new Acceleration(x, y);
        return true;
    }
    internal static bool TryLocal(BodyOrbit body, double epoch, out Acceleration acceleration) {
        acceleration = default;
        double a = body.fAxis1 * .5, b = body.fAxis2 * .5;
        if (!Finite(a) || !Finite(b)) return false;
        // A stationary placeholder still inherits the acceleration of its parents.
        if (a == 0 && b == 0) return true;
        double ecc = body.fEcc, period = body.fPeriod, phase = body.fPeriodShift, angle = body.fAngle;
        if (a < 0 || b < 0 || !Finite(ecc) || ecc < 0 || ecc >= 1 || !Finite(period) || period <= 0 || !Finite(phase) || !Finite(angle)) return false;
        double mean = 2 * Math.PI * (((epoch + phase) % period) / period);
        if (!Finite(mean)) return false;
        if (mean < 0) mean += 2 * Math.PI;
        double e = ecc < .8 ? mean : Math.PI;
        bool solved = false;
        for (int i = 0; i < 32; i++) {
            double error = e - ecc * Math.Sin(e) - mean;
            if (Math.Abs(error) < 1e-13) { solved = true; break; }
            e -= error / (1 - ecc * Math.Cos(e));
        }
        if (!solved) return false;
        double sin = Math.Sin(e), cos = Math.Cos(e), denominator = 1 - ecc * cos;
        double rate = 2 * Math.PI / period / denominator;
        double rateSquared = rate * rate;
        double second = -ecc * sin * rateSquared / denominator;
        double ax = -a * (cos * rateSquared + sin * second);
        double ay = b * body.nOrbitDirection * (-sin * rateSquared + cos * second);
        double radians = (angle % 360) * Math.PI / 180;
        double x = ax * Math.Cos(radians) - ay * Math.Sin(radians);
        double y = ax * Math.Sin(radians) + ay * Math.Cos(radians);
        if (!Finite(x) || !Finite(y)) return false;
        acceleration = new Acceleration(x, y);
        return true;
    }
}
