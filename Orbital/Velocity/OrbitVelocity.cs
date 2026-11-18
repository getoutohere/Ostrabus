using System;
using HarmonyLib;

namespace Ostrabus.Orbital;

// Retain native positions and phase precision, but evaluate tangent velocities
// at the requested epoch. This matches the existing detached SFF convention.
internal static class OrbitVelocity
{
    const double Tau = Math.PI * 2;
    static readonly Func<double, double, double> SolveEccentricAnomaly =
        AccessTools.MethodDelegate<Func<double, double, double>>(
            AccessTools.Method(typeof(BodyOrbit), "SolveBigE"));

    internal struct State
    {
        internal double X, Y, VX, VY;
    }

    internal static bool Finite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);

    internal static bool Supports(BodyOrbit body)
    {
        if (body == null)
            return false;

        int depth = 0;
        for (var current = body; current != null; current = current.boParent)
        {
            bool moving = current.fAxis1 != 0 || current.fAxis2 != 0;
            if (++depth > 32 ||
                !Finite(current.fAxis1) || !Finite(current.fAxis2) ||
                !Finite(current.fAngle) || !Finite(current.fPerh) ||
                !Finite(current.fPeriodShift) || !Finite(current.fEcc) ||
                current.fEcc < 0 || current.fEcc >= 1 ||
                current.fAxis1 < 0 || current.fAxis2 < 0 ||
                moving && (!(current.fPeriod > 0) || !Finite(current.fPeriod)))
                return false;
        }
        return true;
    }

    internal static bool TryUpdate(BodyOrbit body, double epoch, bool correctTimes, bool includeVelocity)
    {
        if (body == null || !Finite(epoch))
            return false;
        if (body.dTimeCalcLast == epoch && (!includeVelocity || body.dVelCalcLast == epoch))
            return true;
        if (!Supports(body))
            return false;

        Update(body, epoch, correctTimes, includeVelocity);
        return true;
    }

    static void Update(BodyOrbit body, double epoch, bool correctTimes, bool includeVelocity)
    {
        if (body.dTimeCalcLast == epoch && (!includeVelocity || body.dVelCalcLast == epoch))
            return;

        var state = Local(body, epoch, correctTimes, includeVelocity);
        if (body.boParent != null)
        {
            Update(body.boParent, epoch, correctTimes, includeVelocity);
            state.X += body.boParent.dXReal;
            state.Y += body.boParent.dYReal;
            if (includeVelocity)
            {
                state.VX += body.boParent.dVelX;
                state.VY += body.boParent.dVelY;
            }
        }
        body.dXReal = state.X;
        body.dYReal = state.Y;
        body.dTimeCalcLast = epoch;
        if (includeVelocity)
        {
            body.dVelX = state.VX;
            body.dVelY = state.VY;
            body.dVelCalcLast = epoch;
        }
    }

    internal static bool TryState(BodyOrbit body, double epoch, out State state)
    {
        state = default;
        if (!Finite(epoch) || !Supports(body))
            return false;
        state = Detached(body, epoch);
        return Finite(state.X) && Finite(state.Y) && Finite(state.VX) && Finite(state.VY);
    }

    // Fit validation must not move the live reference body's caches to another time.
    static State Detached(BodyOrbit body, double epoch)
    {
        var state = Local(body, epoch, correctTimes: true, includeVelocity: true);
        if (body.boParent != null)
        {
            var parent = Detached(body.boParent, epoch);
            state.X += parent.X;
            state.Y += parent.Y;
            state.VX += parent.VX;
            state.VY += parent.VY;
        }
        return state;
    }

    static State Local(BodyOrbit body, double epoch, bool correctTimes, bool includeVelocity)
    {
        bool moving = body.fAxis1 != 0 || body.fAxis2 != 0;
        double mean = moving ? Tau * ((epoch + body.fPeriodShift) % body.fPeriod) / body.fPeriod : 0;
        double anomaly = moving && correctTimes ? SolveEccentricAnomaly(mean, body.fEcc) : mean;
        double sin = Math.Sin(anomaly);
        double cos = Math.Cos(anomaly);
        double major = body.fAxis1 / 2;
        double minor = body.fAxis2 / 2;
        // Preserve native arithmetic order, including the degree conversion.
        double angle = body.fAngle * (Math.PI / 180.0);
        double ca = Math.Cos(angle);
        double sa = Math.Sin(angle);
        double x = major * cos - (major - body.fPerh);
        double y = minor * body.nOrbitDirection * sin;
        var state = new State { X = x * ca - y * sa, Y = x * sa + y * ca };
        if (includeVelocity)
        {
            double rate = moving ? Tau / body.fPeriod / (correctTimes ? 1 - body.fEcc * cos : 1) : 0;
            double dx = -major * sin * rate;
            double dy = minor * body.nOrbitDirection * cos * rate;
            state.VX = dx * ca - dy * sa;
            state.VY = dx * sa + dy * ca;
        }
        return state;
    }
}
