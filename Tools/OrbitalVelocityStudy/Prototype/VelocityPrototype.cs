using System;
using System.Reflection;
using HarmonyLib;

namespace OrbitalVelocityStudy;

// Offline experiment only. This assembly is never deployed or registered by Ostrabus.
internal static class VelocityPrototype
{
    const double Tau = Math.PI * 2;
    static readonly Func<double, double, double> Solve = AccessTools.MethodDelegate<Func<double, double, double>>(
        AccessTools.Method(typeof(BodyOrbit), "SolveBigE"));
    internal static long Evaluations, Hits;
    internal static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);

    internal static bool Update(BodyOrbit body, double epoch, bool correct, bool velocity)
    {
        if (!Finite(epoch))
            return false;
        if (body.dTimeCalcLast == epoch && (!velocity || body.dVelCalcLast == epoch))
        {
            Hits++;
            return true;
        }
        int depth = 0;
        for (var p = body; p != null; p = p.boParent)
        {
            if (++depth > 32 || !Finite(p.fAxis1) || !Finite(p.fAxis2) || !Finite(p.fAngle) ||
                !Finite(p.fPerh) || !Finite(p.fPeriodShift) || !Finite(p.fEcc) ||
                p.fEcc < 0 || p.fEcc >= 1 || p.fAxis1 < 0 || p.fAxis2 < 0 ||
                ((p.fAxis1 != 0 || p.fAxis2 != 0) && (!(p.fPeriod > 0) || !Finite(p.fPeriod))))
                return false;
        }
        Evaluate(body, epoch, correct, velocity);
        return true;
    }

    static void Evaluate(BodyOrbit b, double t, bool correct, bool velocity)
    {
        if (b.dTimeCalcLast == t && (!velocity || b.dVelCalcLast == t))
        {
            Hits++;
            return;
        }
        bool moving = b.fAxis1 != 0 || b.fAxis2 != 0;
        double mean = moving ? Tau * ((t + b.fPeriodShift) % b.fPeriod) / b.fPeriod : 0;
        double e = moving && correct ? Solve(mean, b.fEcc) : mean;
        double sin = Math.Sin(e), cos = Math.Cos(e), a = b.fAxis1 / 2, minor = b.fAxis2 / 2;
        double angle = b.fAngle * (Math.PI / 180.0), ca = Math.Cos(angle), sa = Math.Sin(angle);
        double x = a * cos - (a - b.fPerh), y = minor * b.nOrbitDirection * sin;
        double px = x * ca - y * sa, py = x * sa + y * ca;
        double vx = 0, vy = 0;
        if (velocity)
        {
            double rate = moving ? Tau / b.fPeriod / (correct ? 1 - b.fEcc * cos : 1) : 0;
            double dx = -a * sin * rate, dy = minor * b.nOrbitDirection * cos * rate;
            vx = dx * ca - dy * sa;
            vy = dx * sa + dy * ca;
        }
        if (b.boParent != null)
        {
            Evaluate(b.boParent, t, correct, velocity);
            px += b.boParent.dXReal;
            py += b.boParent.dYReal;
            if (velocity)
            {
                vx += b.boParent.dVelX;
                vy += b.boParent.dVelY;
            }
        }
        b.dXReal = px;
        b.dYReal = py;
        b.dTimeCalcLast = t;
        if (velocity)
        {
            b.dVelX = vx;
            b.dVelY = vy;
            b.dVelCalcLast = t;
        }
        Evaluations++;
    }

    internal static void Install(Harmony owner) => owner.Patch(AccessTools.Method(typeof(BodyOrbit), nameof(BodyOrbit.UpdateTime)),
        prefix: new HarmonyMethod(typeof(VelocityPrototype), nameof(Prefix)) { priority = Priority.Last });
    static bool Prefix(BodyOrbit __instance, double dTime, bool bCorrectTimes, bool bCalcV, bool __runOriginal) =>
        __runOriginal && !Update(__instance, dTime, bCorrectTimes, bCalcV);
}

// Refit from a common-epoch state. Position/velocity continuity is verified by the
// harness after real ShipSitu.LockToOrbit and subsequent native TimeAdvance calls.
internal static class HandoffPrototype
{
    const double AuMeters = 149597863936d, G = 6.674080038626684E-11, Tau = 2 * Math.PI;
    internal static bool ConservativeFit;
    static readonly Func<double, double, double> Solve = AccessTools.MethodDelegate<Func<double, double, double>>(
        AccessTools.Method(typeof(BodyOrbit), "SolveBigE"));

    internal static BodyOrbit Fit(Ship ship, BodyOrbit parent)
    {
        if (ship?.objSS == null || parent == null)
            return null;
        double t = StarSystem.fEpoch;
        // Detached parent evaluation does not advance or poison the live body's caches.
        var state = Detached(parent, t);
        if (!state.Valid)
            return null;
        double rx = (ship.objSS.vPosx - state.X) * AuMeters, ry = (ship.objSS.vPosy - state.Y) * AuMeters;
        double vx = (ship.objSS.vVelX - state.VX) * AuMeters, vy = (ship.objSS.vVelY - state.VY) * AuMeters;
        double radius = Math.Sqrt(rx * rx + ry * ry), speed2 = vx * vx + vy * vy, mu = G * parent.fMass;
        if (!(radius > 0) || !(mu > 0))
            return null;
        double energy = speed2 / 2 - mu / radius, h = rx * vy - ry * vx;
        if (!(energy < 0) || Math.Abs(h) < 1e-12)
            return null;
        double rv = rx * vx + ry * vy, scale = speed2 - mu / radius;
        double ex = (scale * rx - rv * vx) / mu, ey = (scale * ry - rv * vy) / mu;
        double ecc = Math.Sqrt(ex * ex + ey * ey), a = -mu / (2 * energy);
        if (!(ecc >= 0 && ecc < 1))
            return null;
        int direction = h < 0 ? -1 : 1;
        double angle = ecc < 1e-10 ? Math.Atan2(ry, rx) : Math.Atan2(ey, ex);
        double minor = a * Math.Sqrt(1 - ecc * ecc), ca = Math.Cos(angle), sa = Math.Sin(angle);
        double localX = rx * ca + ry * sa, localY = -rx * sa + ry * ca;
        double e = Math.Atan2(localY / (minor * direction), localX / a + ecc);
        double mean = e - ecc * Math.Sin(e), period = Tau * Math.Sqrt(a * a * a / mu);
        double phase = (mean * period / Tau - t % period) % period;
        // Use the same constructor and ship-body metadata as the native factory.
        // In particular IsShipOrbit and native removal depend on the draw flag.
        var result = new BodyOrbit(ship.strRegID,
            a * (1 - ecc) / AuMeters, a * (1 + ecc) / AuMeters,
            angle * 180 / Math.PI, ecc, period / 31556926.0,
            (float)ship.objSS.Size / 1000f, ship.Mass, 1.0, parent)
        {
            fPeriodShift = phase,
            nOrbitDirection = direction,
            nDrawFlagsBody = 1,
            nDrawFlagsTrack = 2
        };
        result.dTimeCalcLast = result.dVelCalcLast = double.NegativeInfinity;
        result.UpdateTime(t);
        if (ConservativeFit)
        {
            var actual = Detached(result, t);
            double velocityError = Math.Sqrt(Math.Pow(actual.VX - ship.objSS.vVelX, 2) + Math.Pow(actual.VY - ship.objSS.vVelY, 2)) * AuMeters;
            double positionError = Math.Sqrt(Math.Pow(actual.X - ship.objSS.vPosx, 2) + Math.Pow(actual.Y - ship.objSS.vPosy, 2)) * AuMeters;
            // Native already permits declining an unsuitable conic conversion.
            // Keep free flight rather than accept a meaningful optimization impulse.
            if (!actual.Valid || velocityError > .005 || positionError > 5)
                return null;
        }
        return result;
    }

    internal struct State
    {
        internal bool Valid; internal double X, Y, VX, VY;
    }
    internal static State Detached(BodyOrbit body, double t, int depth = 0)
    {
        if (body == null || depth > 32)
            return default;
        bool moving = body.fAxis1 != 0 || body.fAxis2 != 0;
        if (moving && !(body.fPeriod > 0))
            return default;
        double mean = moving ? Tau * ((t + body.fPeriodShift) % body.fPeriod) / body.fPeriod : 0;
        double e = moving ? Solve(mean, body.fEcc) : 0, a = body.fAxis1 / 2, b = body.fAxis2 / 2;
        double sin = Math.Sin(e), cos = Math.Cos(e), angle = body.fAngle * (Math.PI / 180.0);
        double ca = Math.Cos(angle), sa = Math.Sin(angle), x = a * cos - (a - body.fPerh), y = b * body.nOrbitDirection * sin;
        double rate = moving ? Tau / body.fPeriod / (1 - body.fEcc * cos) : 0, vx = -a * sin * rate, vy = b * body.nOrbitDirection * cos * rate;
        var result = new State { Valid = true, X = x * ca - y * sa, Y = x * sa + y * ca, VX = vx * ca - vy * sa, VY = vx * sa + vy * ca };
        if (body.boParent != null)
        {
            var p = Detached(body.boParent, t, depth + 1);
            if (!p.Valid)
                return default;
            result.X += p.X;
            result.Y += p.Y;
            result.VX += p.VX;
            result.VY += p.VY;
        }
        return result;
    }

    internal static void Install(Harmony owner) => owner.Patch(AccessTools.Method(typeof(BodyOrbit), nameof(BodyOrbit.CreateBOFromShip)),
        prefix: new HarmonyMethod(typeof(HandoffPrototype), nameof(Prefix)) { priority = Priority.Last });
    static bool Prefix(Ship ship, BodyOrbit boGreatestGrav, ref BodyOrbit __result, bool __runOriginal)
    {
        if (!__runOriginal)
            return false;
        __result = Fit(ship, boGreatestGrav);
        return false;
    }
}
