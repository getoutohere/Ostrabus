using System;
using System.Collections.Generic;

namespace Ostrabus.Orbital;

internal sealed class SffBody {
    internal readonly BodyOrbit Native;
    internal readonly double Mu, Radius, AtmosphereRadius;
    readonly Layer[] layers;
    struct Layer { internal double End, Temperature, PressureMass; }
    double cachedEpoch = double.NaN;
    MotionState cached;
    internal SffBody(BodyOrbit body) {
        Native = body; Mu = OrbitalMotion.GravityConstant * body.fMass; Radius = body.fRadius * OrbitalMotion.KmPerAU;
        if (body.aAtmospheres == null || body.aAtmospheres.Length == 0) return;
        layers = new Layer[body.aAtmospheres.Length];
        for (int i = 0; i < layers.Length; i++) {
            var layer = body.aAtmospheres[i];
            layers[i] = new Layer { End = layer.fMaxAltitude, Temperature = layer.fTemp, PressureMass = GasContainer.GetGasDensity(layer) * layer.fTemp };
            AtmosphereRadius = Math.Max(AtmosphereRadius, layer.fMaxAltitude);
        }
    }
    internal MotionState At(double epoch) {
        if (cachedEpoch == epoch) return cached;
        if (!OrbitalMotion.BodyAt(Native, epoch, out cached)) throw new InvalidOperationException("Invalid ephemeris: " + Native.strName);
        cachedEpoch = epoch; return cached;
    }
    internal DVector Acceleration(double epoch) {
        if (!OrbitAcceleration.TryFrame(Native, epoch, out var a, out _)) throw new InvalidOperationException("Invalid frame: " + Native.strName);
        return new DVector(a.X, a.Y) * OrbitalMotion.KmPerAU;
    }
    internal double Density(double radius) {
        if (layers == null) return 0;
        for (int i = 0; i < layers.Length; i++) if (radius <= layers[i].End) {
            Layer a = layers[i], b = i + 1 < layers.Length ? layers[i + 1] : new Layer { Temperature = 2.72548 };
            double lower = i == 0 ? Radius : layers[i - 1].End;
            double f = a.End > lower ? Math.Max(0, Math.Min(1, (radius - lower) / (a.End - lower))) : 0;
            double temperature = a.Temperature + (b.Temperature - a.Temperature) * f;
            return temperature > 0 ? Math.Max(0, (a.PressureMass + (b.PressureMass - a.PressureMass) * f) / temperature) : 0;
        }
        return 0;
    }
}
internal sealed class SffEnvironment {
    internal readonly List<SffBody> Bodies = new List<SffBody>();
    internal SffBody Select(DVector position, double epoch, out SffBody nearest) {
        nearest = null;
        SffBody selected = null;
        double strongest = -1, closest = double.PositiveInfinity;
        foreach (var body in Bodies) {
            double r = (position - body.At(epoch).Position).Length;
            if (r < closest) { closest = r; nearest = body; }
            double gravity = body.Mu / Math.Max(r * r, 1e-12);
            if (gravity > strongest) { strongest = gravity; selected = body; }
        }
        return selected;
    }
}
internal interface ISffTarget {
    MotionState At(double epoch);
    DVector Acceleration(double epoch);
}
internal sealed class SffPathTarget : ISffTarget {
    internal struct Point {
        internal double Epoch;
        internal MotionState State;
        internal BodyOrbit Body;
        internal DVector Offset;
        internal MotionState At(double epoch) {
            if (Body == null) return State;
            if (!OrbitalMotion.BodyAt(Body, epoch, out var body)) throw new InvalidOperationException("Invalid route target ephemeris");
            return new MotionState(body.Position + Offset, body.Velocity);
        }
    }
    internal Point[] Points;
    public MotionState At(double epoch) {
        int index = 0;
        while (index + 1 < Points.Length && epoch > Points[index + 1].Epoch) index++;
        if (index + 1 == Points.Length) {
            var last = Points[index]; var state = last.At(epoch);
            return last.Body != null ? state : new MotionState(state.Position + state.Velocity * Math.Max(0, epoch - last.Epoch), state.Velocity);
        }
        var a = Points[index]; var b = Points[index + 1];
        double blend = Math.Max(0, Math.Min(1, (epoch - a.Epoch) / (b.Epoch - a.Epoch)));
        var pa = a.At(epoch); var pb = b.At(epoch);
        return new MotionState(pa.Position * (1 - blend) + pb.Position * blend, pa.Velocity * (1 - blend) + pb.Velocity * blend);
    }
    public DVector Acceleration(double epoch) {
        // Advisory target prediction only; this never invokes live NavData playback.
        return (At(epoch + .5).Velocity - At(epoch - .5).Velocity);
    }
}
internal sealed class SffTarget : ISffTarget {
    internal MotionState Initial;
    internal double Start;
    internal BodyOrbit Locked;
    internal DVector Offset;
    internal SffBody Reference;
    public MotionState At(double epoch) {
        if (Locked != null) {
            if (!OrbitalMotion.BodyAt(Locked, epoch, out var body)) throw new InvalidOperationException("Invalid target ephemeris");
            return new MotionState(body.Position + Offset, body.Velocity);
        }
        if (Reference == null) return new MotionState(Initial.Position + Initial.Velocity * (epoch - Start), Initial.Velocity);
        var origin = Reference.At(Start); var end = Reference.At(epoch);
        var local = new MotionState(Initial.Position - origin.Position, Initial.Velocity - origin.Velocity);
        if (!OrbitalMotion.Coast(local, epoch - Start, Reference.Mu, out var coast)) throw new InvalidOperationException("Target coast did not converge");
        return new MotionState(end.Position + coast.Position, end.Velocity + coast.Velocity);
    }
    public DVector Acceleration(double epoch) {
        if (Locked != null) {
            if (!OrbitAcceleration.TryFrame(Locked, epoch, out var a, out _)) throw new InvalidOperationException("Invalid target acceleration");
            return new DVector(a.X, a.Y) * OrbitalMotion.KmPerAU;
        }
        if (Reference == null) return default;
        return Reference.Acceleration(epoch) + OrbitalMotion.Gravity(At(epoch).Position - Reference.At(epoch).Position, Reference.Mu);
    }
}
internal struct SffAero {
    internal double Mass, Lift, Front, Side, Rotation, RotationRate, AngularAcceleration, Weight;
    internal DVector Acceleration(SffBody body, MotionState state, double epoch, double elapsed, DVector gravity) {
        if (Mass <= 0 || body.AtmosphereRadius <= 0) return default;
        var reference = body.At(epoch);
        DVector r = state.Position - reference.Position, v = state.Velocity - reference.Velocity;
        double density = body.Density(r.Length), speed = v.Length;
        if (density <= 0 || speed <= 0) return default;
        double angle = Rotation + RotationRate * elapsed + .5 * AngularAcceleration * elapsed * elapsed;
        DVector forward = new DVector(-Math.Sin(angle), Math.Cos(angle));
        double cos = Math.Max(-1, Math.Min(1, v.Dot(forward) / speed));
        double sideFactor = Math.Sqrt(Math.Max(0, 1 - cos * cos));
        double dragCoefficient = Front + (Side - Front) * sideFactor;
        double dynamicPressure = .5 * density * speed * speed * 1000000;
        double drag = Math.Min(2000, dynamicPressure * dragCoefficient / Mass) / 1000;
        DVector result = v * (-drag / speed);
        double horizonCos = r.Length > 0 ? new DVector(-r.Y, r.X).Dot(forward) / r.Length : 0;
        double lift = Math.Min(gravity.Length * 10, Math.Abs(dynamicPressure * Lift * cos * horizonCos / Mass) / 1000);
        if (gravity.Length > 0) result += gravity * (-lift / gravity.Length);
        return result * Weight;
    }
}
internal sealed class SffFlight {
    internal ISffTarget Target;
    internal double Acceleration, MaxSpeed, MaxSpeedAtmo, HandoffKm, ArrivalSpeed;
    internal bool UsedDetour;
    readonly ShipSitu shadowOrigin = new ShipSitu(addPath: false);
    readonly BodyOrbit shadowBody = new BodyOrbit(new JsonBodyOrbitSave { fPeriod = 31556926 });
    internal MotionState NavigationTarget(MotionState origin, MotionState target, SffBody body, double epoch) {
        // Reuse vanilla's planetary-occlusion waypoint policy with DETACHED shadows;
        // the live ephemeris and the player's plotted route are never advanced here.
        OrbitalMotion.Write(shadowOrigin, origin);
        var center = body.At(epoch).Position * (1 / OrbitalMotion.KmPerAU);
        shadowBody.dXReal = center.X; shadowBody.dYReal = center.Y; shadowBody.fRadius = body.Radius / OrbitalMotion.KmPerAU;
        double x = target.Position.X / OrbitalMotion.KmPerAU, y = target.Position.Y / OrbitalMotion.KmPerAU;
        if (!FlightCPU.GetDockingNavPoint(shadowOrigin, shadowBody, ref x, ref y)) UsedDetour = true;
        return new MotionState(new DVector(x, y) * OrbitalMotion.KmPerAU, target.Velocity);
    }
    internal static DVector Steering(MotionState state, MotionState target, DVector targetAcceleration, DVector freeAcceleration, double thrust, double speed, double handoff) {
        DVector delta = target.Position - state.Position;
        double range = delta.Length, remaining = Math.Max(0, range - handoff * .8);
        double approach = Math.Min(speed, .75 * Math.Sqrt(2 * thrust * remaining));
        DVector desired = range > 0 ? delta * (approach / range) : default;
        return ((desired - (state.Velocity - target.Velocity)) * .1 + targetAcceleration - freeAcceleration).Limited(thrust);
    }
}
internal struct SffOutcome {
    internal MotionState State;
        internal DVector Acceleration;
    internal SffBody Body, Impact;
    internal double ArrivalAfter;
    internal int Steps, Switches;
    internal bool Arrived, Atmosphere, BudgetFallback;
}
internal static class SffDynamics {
    internal static SffOutcome Propagate(SffEnvironment environment, MotionState initial, double start, double seconds,
        DVector engines, SffAero[] aero, double collisionRadius, SffFlight flight) {
        var state = initial;
        var result = new SffOutcome();
        double elapsed = 0;
        SffBody previous = null;
        while (elapsed < seconds) {
            double t = start + elapsed;
            var body = environment.Select(state.Position, t, out _);
            if (body == null) throw new InvalidOperationException("No gravity reference");
            if (previous != null && previous != body) result.Switches++;
            previous = body;
            var reference = body.At(t);
            DVector relative = state.Position - reference.Position;
            double radius = relative.Length;
            if (radius <= body.Radius + collisionRadius) return Impact(result, body, relative, state.Velocity - reference.Velocity, start + seconds, collisionRadius);
            if (flight != null) {
                var target = flight.Target.At(t);
                if ((target.Position - state.Position).Length <= flight.HandoffKm && (target.Velocity - state.Velocity).Length <= flight.ArrivalSpeed) {
                    var goalEnd = flight.Target.At(start + seconds);
                    DVector offset = state.Position - target.Position;
                    result.State = new MotionState(goalEnd.Position + offset, goalEnd.Velocity);
                    result.Body = environment.Select(result.State.Position, start + seconds, out _);
                    result.Acceleration = flight.Target.Acceleration(start + seconds);
                    result.Arrived = true; result.ArrivalAfter = elapsed; return result;
                }
            }
            double step = Math.Min(seconds - elapsed, flight == null ? 60 : 5);
            step = Math.Min(step, .02 * Math.Sqrt(Math.Max(1, radius * radius * radius / body.Mu)));
            // Bound travel through small bodies/gravity domains, even at high speeds.
            foreach (var other in environment.Bodies) {
                var otherState = other.At(t);
                double distance = (state.Position - otherState.Position).Length;
                double speed = (state.Velocity - otherState.Velocity).Length;
                if (speed > 0) step = Math.Min(step, Math.Max(.02, .1 * distance / speed));
            }
            bool inAtmosphere = body.AtmosphereRadius > 0 && radius <= body.AtmosphereRadius + 100;
            result.Atmosphere |= inAtmosphere;
            bool atmosphere = inAtmosphere && aero != null && aero.Length > 0 && body.Density(radius) > 0;
            if (atmosphere) {
                step = Math.Min(step, .5);
                DVector aeroNow = default;
                DVector g = OrbitalMotion.Gravity(relative, body.Mu);
                foreach (var model in aero) aeroNow += model.Acceleration(body, state, t, elapsed, g);
                double speed = (state.Velocity - reference.Velocity).Length;
                if (aeroNow.Length > 0 && speed > 0) step = Math.Min(step, Math.Max(.001, .1 * speed / aeroNow.Length));
            }
            // Epochs are ~6.5e10 seconds: use the actual representable increment,
            // so the next segment starts at precisely the previous endpoint epoch.
            step = (t + step) - t;
            if (step <= 0) throw new InvalidOperationException("SFF step below clock resolution");
            if (++result.Steps > 200000) {
                result.BudgetFallback = true;
                if (!Coast(state, body, t, seconds - elapsed, out state)) throw new InvalidOperationException("SFF trajectory budget exceeded");
                break;
            }
            MotionState next;
            if (flight == null && engines.Length == 0 && !atmosphere && Coast(state, body, t, step, out next)) { }
            else {
                // RK4 only for powered/atmospheric flight. Coasters use exact conic segments.
                var a1 = Acceleration(state, body, t, elapsed, engines, aero, flight);
                var s2 = new MotionState(state.Position + state.Velocity * (step / 2), state.Velocity + a1 * (step / 2));
                var a2 = Acceleration(s2, body, t + step / 2, elapsed + step / 2, engines, aero, flight);
                var s3 = new MotionState(state.Position + s2.Velocity * (step / 2), state.Velocity + a2 * (step / 2));
                var a3 = Acceleration(s3, body, t + step / 2, elapsed + step / 2, engines, aero, flight);
                var s4 = new MotionState(state.Position + s3.Velocity * step, state.Velocity + a3 * step);
                var a4 = Acceleration(s4, body, t + step, elapsed + step, engines, aero, flight);
                next = new MotionState(state.Position + (state.Velocity + s2.Velocity * 2 + s3.Velocity * 2 + s4.Velocity) * (step / 6),
                    state.Velocity + (a1 + a2 * 2 + a3 * 2 + a4) * (step / 6));
            }
            if (!next.Finite) throw new InvalidOperationException("Nonfinite SFF trajectory");
            // Swept relative segment catches surface crossings instead of teleporting through a body.
            var endReference = body.At(t + step);
            DVector endRelative = next.Position - endReference.Position, delta = endRelative - relative;
            double fraction = delta.Dot(delta) > 0 ? Math.Max(0, Math.Min(1, -relative.Dot(delta) / delta.Dot(delta))) : 0;
            DVector nearest = relative + delta * fraction;
            if (nearest.Length <= body.Radius + collisionRadius) {
                // A chord can cut inside the surface while a grazing conic stays
                // above it. Verify the actual coast arc before declaring impact.
                if (flight == null && engines.Length == 0 && !atmosphere) {
                    double lo = 0, hi = step;
                    for (int i = 0; i < 28; i++) {
                        double left = lo + (hi - lo) / 3, right = hi - (hi - lo) / 3;
                        if (!Coast(state, body, t, left, out var l) || !Coast(state, body, t, right, out var r)) break;
                        if ((l.Position - body.At(t + left).Position).Length < (r.Position - body.At(t + right).Position).Length) hi = right; else lo = left;
                    }
                    double closest = (lo + hi) * .5;
                    if (!Coast(state, body, t, closest, out var exact)) throw new InvalidOperationException("Impact arc did not converge");
                    var center = body.At(t + closest);
                    if ((exact.Position - center.Position).Length <= body.Radius + collisionRadius)
                        return Impact(result, body, exact.Position - center.Position, exact.Velocity - center.Velocity, start + seconds, collisionRadius);
                } else return Impact(result, body, nearest, next.Velocity - endReference.Velocity, start + seconds, collisionRadius);
            }
            state = next; elapsed += step;
        }
        result.State = state; result.Body = environment.Select(state.Position, start + seconds, out _);
        result.Acceleration = Acceleration(state, result.Body, start + seconds, seconds, engines, aero, flight);
        return result;
    }
    static SffOutcome Impact(SffOutcome result, SffBody body, DVector relative, DVector velocity, double end, double shipRadius) {
        var atEnd = body.At(end);
        double r = relative.Length;
        DVector inward = r > 0 ? relative * (Math.Max(0, body.Radius + shipRadius - .05) / r) : new DVector(body.Radius - .05, 0);
        result.State = new MotionState(atEnd.Position + inward, atEnd.Velocity + velocity);
        result.Acceleration = body.Acceleration(end) + OrbitalMotion.Gravity(inward, body.Mu);
        result.Impact = result.Body = body; return result;
    }
    internal static bool Coast(MotionState state, SffBody body, double epoch, double seconds, out MotionState result) {
        var a = body.At(epoch); var b = body.At(epoch + seconds);
        if (!OrbitalMotion.Coast(new MotionState(state.Position - a.Position, state.Velocity - a.Velocity), seconds, body.Mu, out var local)) { result = default; return false; }
        result = new MotionState(b.Position + local.Position, b.Velocity + local.Velocity); return result.Finite;
    }
    static DVector Acceleration(MotionState state, SffBody body, double epoch, double elapsed, DVector engines, SffAero[] aero, SffFlight flight) {
        DVector localGravity = OrbitalMotion.Gravity(state.Position - body.At(epoch).Position, body.Mu);
        DVector free = localGravity + body.Acceleration(epoch);
        DVector control = engines;
        if (flight != null) {
            double speed = body.Density((state.Position - body.At(epoch).Position).Length) > 0 && flight.MaxSpeedAtmo > 0 ? flight.MaxSpeedAtmo : flight.MaxSpeed;
            control = SffFlight.Steering(state, flight.NavigationTarget(state, flight.Target.At(epoch), body, epoch),
                flight.Target.Acceleration(epoch), free, flight.Acceleration, speed, flight.HandoffKm);
        }
        DVector drag = default;
        if (aero != null && body.AtmosphereRadius > 0) foreach (var model in aero) drag += model.Acceleration(body, state, epoch, elapsed, localGravity);
        return free + control + drag;
    }
}
