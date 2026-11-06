using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Ostrabus.Orbital;

// Scoped to actual StarSystem.UpdateShip calls. Temporary prediction situations,
// docking groups, NavData and SFF never enter this integration path.
internal static class CoastIntegration {
    internal struct Context {
        internal ShipSitu Situation;
        internal BodyOrbit Body;
        internal double Seconds;
        internal bool ExternalForce;
        internal Vector2 Gravity;
    }
    internal struct Result {
        internal bool Valid;
        internal double X, Y, VX, VY;
        internal float AccelerationSquared;
    }
    sealed class Frame {
        public Frame() {}
        internal double Epoch, Seconds, MinimumRadius;
        internal bool Valid, Initialized;
        internal DVector Start, End, StartAcceleration, EndAcceleration;
    }
    static Context current;
    static ConditionalWeakTable<BodyOrbit, Frame> frames = new ConditionalWeakTable<BodyOrbit, Frame>();
    internal static bool Enabled = true;
    static long integrated, rejected, invalid;
    internal static void SetEnabled(bool enabled) { Enabled = enabled; Reset(); }
    internal static Context Enter(Ship ship, double seconds) {
        Context previous = current;
        current = default;
        if (Enabled && OrbitalSimulation.Enabled && !GUIFFWD.Active && SffSimulation.Current == null &&
            seconds > 0 && seconds <= 60 && ship != null && !ship.HasDockGroup && !ship.bDestroyed && ship.objSS != null) {
            current.Situation = ship.objSS; current.Seconds = seconds;
            current.ExternalForce = !Zero(ship.objSS.vAccEx);
        }
        return previous;
    }
    internal static void Leave(Context previous) { current = previous; }
    internal static void Selected(ShipSitu situation, BodyOrbit body, Vector2 gravity) {
        if (ReferenceEquals(current.Situation, situation)) { current.Body = body; current.Gravity = gravity; }
    }
    static bool Zero(Vector2 value) => value.x == 0 && value.y == 0;
    internal static Result Prepare(ShipSitu situation, double seconds, bool ignoreAcceleration) {
        if (!Enabled || !OrbitalSimulation.Enabled || GUIFFWD.Active || SffSimulation.Current != null ||
            !ReferenceEquals(situation, current.Situation) || current.Body == null) return default;
        if (ignoreAcceleration || seconds != current.Seconds || current.ExternalForce || situation.NavData != null ||
            situation.vAccEx.x != current.Gravity.x || situation.vAccEx.y != current.Gravity.y ||
            situation.bIsBO || situation.bBOLocked || situation.bOrbitLocked || situation.bIgnoreGrav ||
            !Zero(situation.vAccIn) || !Zero(situation.vAccRCS) || !Zero(situation.vAccLift) || !Zero(situation.vAccDrag)) { rejected++; return default; }
        Frame frame = frames.GetOrCreateValue(current.Body);
        double epoch = StarSystem.fEpoch;
        if (!frame.Initialized || frame.Epoch != epoch || frame.Seconds != seconds) {
            frame.Initialized = true; frame.Epoch = epoch; frame.Seconds = seconds;
            frame.Valid = NativePosition(current.Body, epoch - seconds, out frame.Start) && NativePosition(current.Body, epoch, out frame.End);
            if (frame.Valid) {
                bool firstValid = OrbitAcceleration.TryFrame(current.Body, epoch - seconds, out var start, out _);
                bool lastValid = OrbitAcceleration.TryFrame(current.Body, epoch, out var end, out _);
                frame.Valid = firstValid && lastValid;
                frame.StartAcceleration = new DVector(start.X, start.Y); frame.EndAcceleration = new DVector(end.X, end.Y);
            }
            frame.MinimumRadius = Math.Max(0, current.Body.fRadius);
            if (current.Body.aAtmospheres != null) foreach (var layer in current.Body.aAtmospheres)
                frame.MinimumRadius = Math.Max(frame.MinimumRadius, layer.fMaxAltitude / OrbitalMotion.KmPerAU);
            frame.MinimumRadius += 1 / OrbitalMotion.KmPerAU;
        }
        if (!frame.Valid) { invalid++; return default; }
        var position = new DVector(situation.vPosx, situation.vPosy);
        var velocity = new DVector(situation.vVelX, situation.vVelY);
        double mu = (double)(float)2e-44 * current.Body.fMass;
        DVector relative = position - frame.Start;
        double radius = relative.Length;
        if (!(mu > 0) || !(radius > frame.MinimumRadius) || seconds > .01 * Math.Sqrt(radius * radius * radius / mu)) { rejected++; return default; }
        DVector first = OrbitalMotion.Gravity(relative, mu) + frame.StartAcceleration;
        DVector next = position + velocity * seconds + first * (.5 * seconds * seconds);
        DVector endRelative = next - frame.End, segment = endRelative - relative;
        double lengthSquared = segment.Dot(segment);
        double fraction = lengthSquared > 0 ? Math.Max(0, Math.Min(1, -relative.Dot(segment) / lengthSquared)) : 0;
        if ((relative + segment * fraction).Length <= frame.MinimumRadius) { rejected++; return default; }
        DVector last = OrbitalMotion.Gravity(endRelative, mu) + frame.EndAcceleration;
        DVector mean = (first + last) * .5, nextVelocity = velocity + mean * seconds;
        if (!next.Finite || !nextVelocity.Finite || !mean.Finite) { invalid++; return default; }
        return new Result { Valid = true, X = next.X, Y = next.Y, VX = nextVelocity.X, VY = nextVelocity.Y,
            AccelerationSquared = (float)mean.Dot(mean) };
    }
    internal static void Complete(ShipSitu situation, Result result) {
        if (!result.Valid) return;
        situation.vPosx = result.X; situation.vPosy = result.Y;
        situation.vVelX = result.VX; situation.vVelY = result.VY;
        situation.fAccelMagSquaredLast = result.AccelerationSquared;
        integrated++;
    }
    internal static bool NativePosition(BodyOrbit body, double epoch, out DVector position) => NativeEphemeris.Position(body, epoch, out position);
    internal static void Reset() {
        current = default; frames = new ConditionalWeakTable<BodyOrbit, Frame>(); integrated = rejected = invalid = 0;
    }
    internal static object Snapshot() => Ostrabus.Inspection.SnapshotJson.Map("enabled", Enabled,
        "integratedSteps", integrated, "rejectedSteps", rejected, "invalidSteps", invalid,
        "scope", "Undocked, unpowered, atmosphere-free normal simulation only; native angular motion retained.");
}
