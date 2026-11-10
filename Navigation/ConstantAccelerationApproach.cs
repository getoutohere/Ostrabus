using System;
using Ostrabus.Orbital;

namespace Ostrabus.ShipNavigation;

// Exact minimum for a quadratic relative trajectory over a bounded interval.
// Normalize time to [0,1], split the cubic derivative at its turning points,
// then bisect its (at most three) monotonic root intervals. No sampling arrays.
internal static class ConstantAccelerationApproach {
    internal static double MinimumSquared(DVector position, DVector velocity, DVector acceleration, double seconds) {
        DVector v = velocity * seconds, a = acceleration * (.5 * seconds * seconds);
        double c0 = position.Dot(v), c1 = v.Dot(v) + 2 * position.Dot(a), c2 = 3 * v.Dot(a), c3 = 2 * a.Dot(a);
        double first = 1, second = 1;
        double qa = 3 * c3, qb = 2 * c2, qc = c1;
        if (qa > 0) {
            double discriminant = qb * qb - 4 * qa * qc;
            if (discriminant >= 0) {
                double root = Math.Sqrt(discriminant), q = -.5 * (qb + (qb < 0 ? -root : root));
                double r1 = q / qa, r2 = q == 0 ? r1 : qc / q;
                if (r1 > r2) { double swap = r1; r1 = r2; r2 = swap; }
                first = Math.Max(0, Math.Min(1, r1)); second = Math.Max(0, Math.Min(1, r2));
            }
        } else if (qb != 0) first = second = Math.Max(0, Math.Min(1, -qc / qb));
        double minimum = position.Dot(position);
        Interval(0, first, position, v, a, c0, c1, c2, c3, ref minimum);
        Interval(first, second, position, v, a, c0, c1, c2, c3, ref minimum);
        Interval(second, 1, position, v, a, c0, c1, c2, c3, ref minimum);
        return minimum;
    }
    static double Derivative(double t, double c0, double c1, double c2, double c3) => ((c3 * t + c2) * t + c1) * t + c0;
    static double Distance(double t, DVector p, DVector v, DVector a) {
        DVector point = p + v * t + a * (t * t);
        return point.Dot(point);
    }
    static void Interval(double left, double right, DVector p, DVector v, DVector a, double c0, double c1, double c2, double c3, ref double minimum) {
        minimum = Math.Min(minimum, Math.Min(Distance(left, p, v, a), Distance(right, p, v, a)));
        if (right <= left) return;
        double fl = Derivative(left, c0, c1, c2, c3), fr = Derivative(right, c0, c1, c2, c3);
        if (!((fl < 0 && fr > 0) || (fl > 0 && fr < 0))) return;
        for (int i = 0; i < 36; i++) {
            double mid = (left + right) * .5, fm = Derivative(mid, c0, c1, c2, c3);
            if (fm == 0) { left = right = mid; break; }
            if ((fl < 0) == (fm < 0)) { left = mid; fl = fm; }
            else right = mid;
        }
        minimum = Math.Min(minimum, Distance((left + right) * .5, p, v, a));
    }
}
