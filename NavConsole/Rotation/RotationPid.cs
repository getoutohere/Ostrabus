using System;

namespace Ostrabus.NavConsole.Rotation;

// Radians, radians/second and game seconds throughout. No Unity state or clocks.
internal sealed class RotationPid {
    internal const double MaxSlewRate = 0.5;
    internal const double AngleTolerance = Math.PI / 18000; // 0.01 degrees
    internal const double RateTolerance = Math.PI / 180000; // 0.001 degrees/s
    internal const double WakeAngle = AngleTolerance * 3;
    internal const double WakeRate = RateTolerance * 4;
    double integral;
    bool braking;

    internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    internal static double Wrap(double value) {
        value %= 2 * Math.PI;
        if (value > Math.PI) value -= 2 * Math.PI;
        if (value < -Math.PI) value += 2 * Math.PI;
        return value;
    }

    internal void Reset() { integral = 0; braking = false; }

    internal double Step(double error, double angularRate, double targetRate, double dt, double limit) {
        if (!Finite(error) || !Finite(angularRate) || !Finite(targetRate) || !Finite(dt) ||
            !Finite(limit) || dt <= 0 || limit <= 0) return 0;
        error = Wrap(error);
        double relativeRate = angularRate - targetRate;
        if (Math.Abs(error) > 0.1) {
            integral = 0;
            double direction = Math.Sign(error);
            double stopAngle = relativeRate * relativeRate / (4 * limit) + Math.Abs(relativeRate) * dt * 1.5;
            if (relativeRate * direction > 0 && Math.Abs(error) <= stopAngle) braking = true;
            if (Math.Abs(relativeRate) <= RateTolerance) braking = false;
            if (braking) return Math.Max(-limit, Math.Min(limit, -relativeRate / (2 * dt)));
            // On a coarse frame, reaching cruise speed can itself consume the
            // remaining angle. Leave a small capture margin rather than jumping
            // across the destination with a large residual spin.
            double budgetRate = (Math.Max(0, Math.Abs(error) - 0.05) / dt - 0.25 * relativeRate * direction) / 0.75;
            double desiredRate = direction * Math.Min(MaxSlewRate, budgetRate);
            if (Math.Abs(relativeRate - desiredRate) < RateTolerance) return 0;
            return Math.Max(-limit, Math.Min(limit, (desiredRate - relativeRate) / (2 * dt)));
        }
        braking = false;
        // Native Maneuver changes fW immediately, then ShipSitu integrates fA.
        // Tune for that two-stage response, and lower gains for coarse game ticks.
        double frequency = Math.Min(3.0, 0.4 / dt);
        double kp = frequency * frequency * 0.5;
        double kd = frequency;
        double ki = frequency * frequency * frequency * 0.015;
        // Limit the integral's positional bias to half the capture tolerance.
        // Carrying a large slew integral into a tiny-error hold otherwise takes
        // minutes to unwind and keeps requesting needless correction pulses.
        double maxIntegral = kp * AngleTolerance / (2 * ki);
        double candidate = Math.Max(-maxIntegral, Math.Min(maxIntegral, integral + error * dt));
        double output = kp * error + ki * candidate - kd * (angularRate - targetRate);
        // Conditional integration: do not wind up while the actuator is saturated.
        if (Math.Abs(output) <= limit || Math.Sign(output) != Math.Sign(error)) integral = candidate;
        output = kp * error + ki * integral - kd * (angularRate - targetRate);
        double maxRate = Math.Min(MaxSlewRate, Math.Sqrt(Math.Max(0, 4 * limit * Math.Abs(error))));
        if (relativeRate > maxRate) output = Math.Min(output, -limit * 0.25);
        if (relativeRate < -maxRate) output = Math.Max(output, limit * 0.25);
        return Math.Max(-limit, Math.Min(limit, output));
    }
}

internal enum RotationDirection { Forward, Backward, Left, Right }

internal sealed class RotationOrder {
    readonly RotationPid pid = new();
    internal bool Active { get; private set; }
    internal bool Hold { get; private set; }
    internal bool HasLastCommand { get; private set; }
    internal string TargetId { get; private set; }
    internal double Heading { get; private set; }
    internal double Offset { get; private set; }
    double previousBearing;
    bool hasBearing;
    double targetRate;
    double observationEpoch = double.NaN;
    bool coasting;
    internal double TargetRate => targetRate;
    internal bool Quiet => coasting;

    internal void Command(RotationDirection direction, double heading, string targetId, double? bearing) {
        bool hold = Hold;
        Cancel();
        Offset = direction switch {
            RotationDirection.Left => Math.PI / 2,
            RotationDirection.Right => -Math.PI / 2,
            RotationDirection.Backward => Math.PI,
            _ => 0
        };
        TargetId = bearing.HasValue ? targetId : null;
        double relativeTurn = direction is RotationDirection.Forward or RotationDirection.Backward ? Math.PI : Offset;
        Heading = RotationPid.Wrap(bearing.HasValue ? bearing.Value + Offset : heading + relativeTurn);
        previousBearing = bearing ?? 0;
        hasBearing = bearing.HasValue;
        HasLastCommand = Active = true;
        Hold = hold;
    }

    internal void SetHold(bool value) {
        if (!HasLastCommand) return;
        Hold = value;
        Active = value;
        targetRate = 0;
        hasBearing = false;
        observationEpoch = double.NaN;
        coasting = false;
        pid.Reset();
    }

    internal void Restore(bool active, bool hold, string targetId, double heading, double offset) {
        Cancel();
        if (!RotationPid.Finite(heading) || !RotationPid.Finite(offset)) return;
        HasLastCommand = true;
        Active = active;
        Hold = hold;
        TargetId = targetId;
        Heading = RotationPid.Wrap(heading);
        Offset = RotationPid.Wrap(offset);
    }

    internal void ObserveBearing(double? originalTargetBearing, double epoch) {
        if (!Active) return;
        if (Hold && TargetId != null) {
            if (!originalTargetBearing.HasValue) { Cancel(); return; }
            double bearing = originalTargetBearing.Value;
            double elapsed = epoch - observationEpoch;
            // Only new world observations measure motion. Repeated angular
            // substeps must not turn one sample into a sequence of zero rates.
            if (hasBearing && RotationPid.Finite(elapsed) && elapsed > 0) {
                double measuredRate = RotationPid.Wrap(bearing - previousBearing) / elapsed;
                double alpha = elapsed / (0.3 + elapsed);
                targetRate += alpha * (Math.Max(-RotationPid.MaxSlewRate, Math.Min(RotationPid.MaxSlewRate, measuredRate)) - targetRate);
            }
            Heading = RotationPid.Wrap(bearing + Offset);
            previousBearing = bearing;
            hasBearing = true;
            observationEpoch = epoch;
        } else targetRate = 0;
    }

    internal double Step(double heading, double rate, double dt, double limit, double elapsed = 0) {
        if (!Active || dt <= 0) return 0;
        double desired = Heading;
        if (Hold && TargetId != null) desired = RotationPid.Wrap(previousBearing + Offset + targetRate * elapsed);
        double error = RotationPid.Wrap(desired - heading);
        double relativeRate = rate - targetRate;
        bool stationary = Math.Abs(targetRate) < 1e-7;
        double rateTolerance = stationary ? Math.PI / 1800 : coasting ? RotationPid.WakeRate : RotationPid.RateTolerance;
        bool settled = Math.Abs(error) <= (coasting ? RotationPid.WakeAngle : RotationPid.AngleTolerance) &&
            Math.Abs(relativeRate) <= rateTolerance;
        if (settled) {
            coasting = true;
            pid.Reset();
            // A moving line of sight requires angular velocity, not continuous
            // angular acceleration. Coast at matched rate instead of repeatedly
            // braking slow tracking motion to zero at the old 0.2-degree edge.
            if (stationary) {
                if (rate != 0) return -Math.Sign(rate) * Math.Min(limit, Math.Max(0.0001, Math.Abs(rate) / dt * 1.01));
                if (!Hold) Active = false;
            }
            return 0;
        }
        coasting = false;
        return pid.Step(error, rate, targetRate, dt, limit);
    }

    internal void Cancel() {
        Active = Hold = HasLastCommand = false;
        TargetId = null;
        hasBearing = false;
        targetRate = 0;
        observationEpoch = double.NaN;
        coasting = false;
        pid.Reset();
    }
}
