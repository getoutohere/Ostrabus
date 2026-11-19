using System;
using System.Collections.Generic;
using System.Linq;
using Ostranauts.ShipGUIs.Utilities;

// Independent reference: actual BodyOrbit.UpdateTime and actual native
// NavData.GetShipSituAtTime. Does not call the production ephemeris, sampler,
// clearance predicate, detour code or its acceleration bounds.
static class NativeCourseGeometry {
    internal readonly struct Result {
        internal readonly double ClearanceKm, Epoch;
        internal readonly string Body;
        internal readonly int Samples;
        internal Result(double clearance, double epoch, string body, int samples) {
            ClearanceKm = clearance; Epoch = epoch; Body = body; Samples = samples;
        }
    }
    static double SpeedBound(BodyOrbit body) {
        double result = 0;
        for (; body != null; body = body.boParent) {
            if (body.fAxis1 == 0 && body.fAxis2 == 0) continue;
            result += Math.PI * body.fAxis1 / Math.Abs(body.fPeriod) * Math.Sqrt((1 + body.fEcc) / (1 - body.fEcc));
        }
        return result;
    }
    static double Interval(BodyOrbit body) {
        double result = double.PositiveInfinity;
        for (; body != null; body = body.boParent)
            if (body.fAxis1 != 0 || body.fAxis2 != 0) result = Math.Min(result, Math.Abs(body.fPeriod) / 64);
        return result;
    }
    internal static Result Inspect(NavData source, double from, double hullRadius) {
        var originalWorld = CrewSim.system;
        var world = FreightCourseChecks.Empty<StarSystem>();
        world.aBOs = originalWorld.aBOs.ToDictionary(pair => pair.Key, pair => new BodyOrbit(pair.Value.GetJSONSave()));
        world.dictShips = originalWorld.dictShips;
        foreach (var pair in originalWorld.aBOs) if (pair.Value.boParent != null)
            world.aBOs[pair.Key].boParent = world.aBOs[pair.Value.boParent.strName];
        CrewSim.system = world;
        try {
            var route = new NavData(source.GetJSON());
            var points = route.GetNavDataPoints();
            double minimum = double.PositiveInfinity, at = 0;
            string name = null;
            int samples = 0;
            foreach (var body in world.aBOs.Values.Where(body => !body.IsPlaceholder() && !body.IsAsteroidField && body.fRadius > 0)) {
                double radius = body.fRadius + hullRadius;
                double Distance(double epoch) {
                    var motion = route.GetShipSituAtTime(epoch, false);
                    if (motion == null) throw new Exception("Native reference could not sample an in-range route");
                    body.UpdateTime(epoch);
                    double dx = motion.vPosx - body.dXReal, dy = motion.vPosy - body.dYReal;
                    double d = Math.Sqrt(dx * dx + dy * dy);
                    samples++;
                    if (d - radius < minimum) { minimum = d - radius; name = body.strName; at = epoch; }
                    return d;
                }
                for (int i = 1; i < points.Count; i++) {
                    double begin = Math.Max(from, points[i - 1].ArrivalTime), end = points[i].ArrivalTime;
                    if (end < begin) continue;
                    double startDistance = Distance(begin), endDistance = Distance(end);
                    var a = points[i - 1].ObjSS; var b = points[i].ObjSS;
                    if (!a.bBOLocked && !a.bIsBO && !a.bOrbitLocked && !b.bBOLocked && !b.bIsBO && !b.bOrbitLocked) {
                        // Conservative broad phase with an independent velocity
                        // bound; the actual closest approach is searched below.
                        var p = route.GetShipSituAtTime(begin, false); var q = route.GetShipSituAtTime(end, false);
                        body.UpdateTime(begin);
                        double x = p.vPosx - body.dXReal, y = p.vPosy - body.dYReal;
                        double dx = q.vPosx - p.vPosx, dy = q.vPosy - p.vPosy;
                        double length = dx * dx + dy * dy;
                        double u = length > 0 ? Math.Max(0, Math.Min(1, -(x * dx + y * dy) / length)) : 0;
                        double closest = Math.Sqrt((x + dx * u) * (x + dx * u) + (y + dy * u) * (y + dy * u));
                        if (closest > radius + 2 * SpeedBound(body) * (end - begin) + 1 / 149597872d) continue;
                    }
                    double step = Math.Min(Interval(body), end - begin);
                    if (!(step > 0)) continue;
                    for (double left = begin; left < end;) {
                        double right = Math.Min(end, left + step);
                        if (!(right > left)) throw new Exception("Independent reference interval did not advance");
                        // <= 1/64 orbit per interval, then minimize actual native
                        // distance rather than assuming the inter-waypoint chord.
                        double lo = left, hi = right, ratio = .6180339887498949;
                        double t0 = hi - (hi - lo) * ratio, t1 = lo + (hi - lo) * ratio;
                        double f0 = Distance(t0), f1 = Distance(t1);
                        for (int n = 0; n < 40 && hi - lo > .0001; n++) {
                            if (f0 < f1) { hi = t1; t1 = t0; f1 = f0; t0 = hi - (hi - lo) * ratio; f0 = Distance(t0); }
                            else { lo = t0; t0 = t1; f0 = f1; t1 = lo + (hi - lo) * ratio; f1 = Distance(t1); }
                        }
                        Distance(left); Distance(right); left = right;
                    }
                }
            }
            return new Result(minimum * 149597872d, at, name, samples);
        } finally { CrewSim.system = originalWorld; }
    }
}
