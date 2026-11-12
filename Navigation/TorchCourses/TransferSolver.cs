using System;
using Ostranauts.Utils.Models;

namespace Ostrabus.ShipNavigation.TorchCourses;

// Target-relative inertial transfer. Two full-acceleration vector burns and an
// optional constant-velocity coast; no integration steps or heap allocations.
internal static class TransferSolver {
    internal struct Transfer {
        internal Point Cruise, FirstAcceleration, LastAcceleration;
        internal double FirstSeconds, CoastSeconds, LastSeconds;
        internal double Duration => FirstSeconds + CoastSeconds + LastSeconds;
    }
    internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    static bool Finite(Point value) => Finite(value.X) && Finite(value.Y);
    internal static bool Solve(Point displacement, Point velocity, double acceleration, double speedLimit, out Transfer result) {
        result = default;
        if (!Finite(displacement) || !Finite(velocity) || !Finite(acceleration) || acceleration <= 0 ||
            !Finite(speedLimit) || speedLimit <= 0) return false;
        double distance = displacement.magnitude, incoming = velocity.magnitude;
        if (!Finite(distance) || !Finite(incoming) || distance == 0) return false;
        // Normalize to avoid absolute AU-scale tolerances and squaring very
        // different distance/velocity scales. The normalized acceleration is 1.
        double unitSpeed = Math.Max(Math.Sqrt(acceleration * distance), incoming);
        double unitTime = unitSpeed / acceleration;
        double unitDistance = unitSpeed * unitTime;
        if (!Finite(unitDistance) || unitDistance <= 0 || !Finite(unitTime)) return false;
        Point d = displacement * (1 / unitDistance), u = velocity * (1 / unitSpeed);
        if (!Uncapped(d,u,out var plan)) return false;
        double limit = speedLimit / unitSpeed;
        if (plan.Cruise.magnitude > limit * (1 + 1e-12) && !Capped(d,u,limit,out plan)) return false;
        if (!Validate(d,u,plan)) return false;
        result = new Transfer {
            Cruise = plan.Cruise * unitSpeed,
            FirstAcceleration = plan.FirstAcceleration * acceleration,
            LastAcceleration = plan.LastAcceleration * acceleration,
            FirstSeconds = plan.FirstSeconds * unitTime,
            CoastSeconds = plan.CoastSeconds * unitTime,
            LastSeconds = plan.LastSeconds * unitTime
        };
        return Finite(result.Duration);
    }
    static Point CruiseForTime(Point d, Point u, double first) {
        Point remaining = d - u * (.5 * first);
        double length = remaining.magnitude;
        if (length == 0) return default;
        // |remaining| = speed * first/2 + speed^2/2.
        // Rationalized positive root avoids subtracting similar square roots.
        double q = .5 * first;
        double speed = 2 * length / (q + Math.Sqrt(q*q + 2*length));
        return remaining * (speed / length);
    }
    static bool Uncapped(Point d, Point u, out Transfer plan) {
        plan = default;
        Point cruise = CruiseForTime(d,u,0);
        if ((cruise-u).magnitude <= 1e-13) {
            // Already exactly on the braking trajectory: no artificial first burn.
            return Build(u,u,0,0,out plan);
        }
        double lo = 0, hi = Math.Max(1,Math.Sqrt(d.magnitude));
        int brackets = 0;
        while ((CruiseForTime(d,u,hi)-u).magnitude > hi) {
            hi *= 2;
            if (++brackets == 32 || !Finite(hi)) return false;
        }
        for (int i = 0; i < 56; i++) {
            double mid = (lo+hi)*.5;
            if ((CruiseForTime(d,u,mid)-u).magnitude > mid) lo = mid;
            else hi = mid;
        }
        return Build(u,CruiseForTime(d,u,hi),hi,0,out plan);
    }
    static bool Capped(Point d, Point u, double speed, out Transfer plan) {
        plan = default;
        if (speed <= 0 || !Finite(speed)) return false;
        Point initialCruise = d.normalized * speed;
        double initialCoast = d.magnitude/speed-speed*.5;
        if ((initialCruise-u).magnitude <= 1e-13 && initialCoast >= 0) return Build(u,u,0,initialCoast,out plan);
        double lo = 0, hi = u.magnitude + speed;
        // At fixed cruise speed, direction follows d-u*t1/2. The triangle
        // inequality brackets |cruise-u| = t1 within [0, |u|+speed].
        for (int i = 0; i < 56; i++) {
            double mid = (lo+hi)*.5;
            Point w = d-u*(mid*.5);
            Point cruise = w.normalized * speed;
            if ((cruise-u).magnitude > mid) lo = mid;
            else hi = mid;
        }
        Point remaining = d-u*(hi*.5);
        if (remaining.magnitude == 0) return false;
        Point v = remaining.normalized * speed;
        double coast = remaining.magnitude / speed - .5*(hi+speed);
        if (coast < -1e-10 || !Finite(coast)) return false;
        return Build(u,v,hi,Math.Max(0,coast),out plan);
    }
    static bool Build(Point u, Point cruise, double first, double coast, out Transfer plan) {
        double last = cruise.magnitude;
        plan = new Transfer {
            Cruise = cruise, FirstSeconds = first, CoastSeconds = coast, LastSeconds = last,
            FirstAcceleration = first > 0 ? (cruise-u)*(1/first) : default,
            LastAcceleration = last > 0 ? cruise*(-1/last) : default
        };
        return Finite(plan.Duration) && first >= 0 && coast >= 0;
    }
    static bool Validate(Point d, Point u, Transfer plan) {
        Point firstEnd = u + plan.FirstAcceleration * plan.FirstSeconds;
        Point arrival = firstEnd + plan.LastAcceleration * plan.LastSeconds;
        Point position = (u+firstEnd)*(.5*plan.FirstSeconds) + firstEnd*plan.CoastSeconds +
            (firstEnd+arrival)*(.5*plan.LastSeconds);
        return Finite(position) && Finite(arrival) &&
            plan.FirstAcceleration.magnitude <= 1+1e-10 && plan.LastAcceleration.magnitude <= 1+1e-10 &&
            (position-d).magnitude <= 1e-10*Math.Max(1,d.magnitude) && arrival.magnitude <= 1e-10;
    }
}
