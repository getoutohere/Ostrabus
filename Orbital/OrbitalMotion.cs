using System;

namespace Ostrabus.Orbital;

internal readonly struct DVector {
    internal readonly double X, Y;
    internal DVector(double x, double y) { X = x; Y = y; }
    internal double Length => Math.Sqrt(X * X + Y * Y);
    internal bool Finite => OrbitAcceleration.Finite(X) && OrbitAcceleration.Finite(Y);
    internal double Dot(DVector b) => X * b.X + Y * b.Y;
    internal DVector Limited(double maximum) { double n = Length; return n > maximum && n > 0 ? this * (maximum / n) : this; }
    public static DVector operator +(DVector a, DVector b) => new DVector(a.X + b.X, a.Y + b.Y);
    public static DVector operator -(DVector a, DVector b) => new DVector(a.X - b.X, a.Y - b.Y);
    public static DVector operator *(DVector a, double b) => new DVector(a.X * b, a.Y * b);
}
internal readonly struct MotionState {
    internal readonly DVector Position, Velocity;
    internal MotionState(DVector position, DVector velocity) { Position = position; Velocity = velocity; }
    internal bool Finite => Position.Finite && Velocity.Finite;
}

internal static class OrbitalMotion {
    internal const double KmPerAU = 149597870.7;
    internal static readonly double GravityConstant = (double)(float)2e-44 * KmPerAU * KmPerAU * KmPerAU;
    internal static bool BodyAt(BodyOrbit body, double epoch, out MotionState state) {
        state = default;
        if (!NativeEphemeris.State(body, epoch, out DVector p, out DVector v)) return false;
        state = new MotionState(p * KmPerAU, v * KmPerAU);
        return state.Finite;
    }
    internal static MotionState Read(ShipSitu situation) => new MotionState(new DVector(situation.vPosx, situation.vPosy) * KmPerAU,
        new DVector(situation.vVelX, situation.vVelY) * KmPerAU);
    internal static void Write(ShipSitu situation, MotionState state) {
        situation.vPosx = state.Position.X / KmPerAU; situation.vPosy = state.Position.Y / KmPerAU;
        situation.vVelX = state.Velocity.X / KmPerAU; situation.vVelY = state.Velocity.Y / KmPerAU;
    }
    internal static DVector Gravity(DVector relative, double mu) {
        double r = relative.Length;
        return r > 0 ? relative * (-mu / (r * r * r)) : default;
    }
    static void Stumpff(double z, out double c, out double s) {
        if (z > .0001) { double q = Math.Sqrt(z); c = (1 - Math.Cos(q)) / z; s = (q - Math.Sin(q)) / (q * q * q); }
        else if (z < -.0001) { double q = Math.Sqrt(-z); c = (Math.Cosh(q) - 1) / -z; s = (Math.Sinh(q) - q) / (q * q * q); }
        else { c = .5 - z / 24 + z * z / 720 - z * z * z / 40320; s = 1d / 6 - z / 120 + z * z / 5040 - z * z * z / 362880; }
    }
    internal static bool Coast(MotionState initial, double seconds, double mu, out MotionState result) {
        result = initial;
        double r0 = initial.Position.Length, v2 = initial.Velocity.Dot(initial.Velocity);
        if (!(r0 > 0) || !(mu > 0) || !initial.Finite || !OrbitAcceleration.Finite(seconds)) return false;
        double dot = initial.Position.Dot(initial.Velocity), alpha = 2 / r0 - v2 / mu, sqrtMu = Math.Sqrt(mu);
        if (alpha > 1e-12) seconds %= 2 * Math.PI / Math.Sqrt(mu * alpha * alpha * alpha);
        if (seconds == 0) return true;
        double chi = sqrtMu * Math.Abs(alpha) * seconds;
        if (Math.Abs(alpha) < 1e-10) chi = sqrtMu * seconds / r0;
        else if (alpha < 0) {
            double sign = Math.Sign(seconds), arg = -2 * mu * alpha * seconds / (dot + sign * Math.Sqrt(-mu / alpha) * (1 - r0 * alpha));
            if (arg > 0) chi = sign * Math.Sqrt(-1 / alpha) * Math.Log(arg);
        }
        bool solved = false;
        double c, s;
        for (int i = 0; i < 80; i++) {
            double z = alpha * chi * chi; Stumpff(z, out c, out s);
            double f = dot / sqrtMu * chi * chi * c + (1 - alpha * r0) * chi * chi * chi * s + r0 * chi - sqrtMu * seconds;
            double derivative = dot / sqrtMu * chi * (1 - z * s) + (1 - alpha * r0) * chi * chi * c + r0;
            double delta = f / derivative;
            if (!OrbitAcceleration.Finite(delta)) return false;
            chi -= delta;
            if (Math.Abs(delta) < 1e-10) { solved = true; break; }
        }
        if (!solved) return false;
        Stumpff(alpha * chi * chi, out c, out s);
        DVector position = initial.Position * (1 - chi * chi / r0 * c) + initial.Velocity * (seconds - chi * chi * chi / sqrtMu * s);
        double radius = position.Length;
        if (!(radius > 0)) return false;
        double fdot = sqrtMu / (radius * r0) * (alpha * chi * chi * chi * s - chi);
        result = new MotionState(position, initial.Position * fdot + initial.Velocity * (1 - chi * chi / radius * c));
        return result.Finite;
    }
}
