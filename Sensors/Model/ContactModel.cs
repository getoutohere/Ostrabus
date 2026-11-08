using System;

namespace Ostrabus.Sensors.Model;

public enum SensorKind { Radar, Lidar, Optical, IR, EM }
[Flags]
public enum MeasurementSources { None = 0, Radar = 1, Lidar = 2, Optical = 4, IR = 8, EM = 16, Timestamp = 32, LocalNavigation = 64, KnownPosition = 128 }

public readonly struct SensorSpec {
    public readonly SensorKind Kind;
    public readonly double BaseRangeKm, Strength;
    public readonly bool On;
    public SensorSpec(SensorKind kind, double baseRangeKm, double strength = 0, bool on = true) {
        Kind = kind; BaseRangeKm = baseRangeKm; Strength = strength; On = on;
    }
}
public readonly struct Observation {
    public readonly double RangeKm, CrossSection, ProjectedSizeM, HeatSignature, EMEmission, RelativeSpeedMps, BearingDegrees;
    public readonly double SampleAgeSeconds, VelocitySigmaMps, AccelerationSigmaMps2;
    public readonly bool KnownHull, TransponderBroadcasting, Occluded, KnownPosition;
    public Observation(double rangeKm, double crossSection = 1, double projectedSizeM = 20, double heatSignature = 0,
        double emEmission = .6, double relativeSpeedMps = 50, double bearingDegrees = 0, bool knownHull = true,
        bool transponderBroadcasting = true, double sampleAgeSeconds = 0, double velocitySigmaMps = .1,
        double accelerationSigmaMps2 = .01, bool occluded = false, bool knownPosition = false) {
        RangeKm = rangeKm; CrossSection = crossSection; ProjectedSizeM = projectedSizeM; HeatSignature = heatSignature;
        EMEmission = emEmission; RelativeSpeedMps = relativeSpeedMps; BearingDegrees = bearingDegrees;
        KnownHull = knownHull; TransponderBroadcasting = transponderBroadcasting; SampleAgeSeconds = sampleAgeSeconds;
        VelocitySigmaMps = velocitySigmaMps; AccelerationSigmaMps2 = accelerationSigmaMps2;
        Occluded = occluded; KnownPosition = knownPosition;
    }
}

// Fictional-equipment tuning assumptions, not measured specifications. Units and
// geometry are physical; reference ranges and the local-navigation safeguard are gameplay choices.
public readonly struct ModelSettings {
    public const double DefaultCombatRangeKm = 120;
    public readonly double OpticalArcsec, OpticalSizeCentroidFraction, KnownSizeFraction, UnknownSizeFraction;
    public readonly double RadarRangeM, RadarArcsec, LidarRangeM, LidarArcsec, IRArcsec, EMArcsec;
    public readonly double ClockSigmaSeconds, TimestampJitterSeconds, SharedRangeFloorM, SharedBearingArcsec;
    public readonly double ComfortRangeKm, ComfortMPerKm, OutsideComfortMPerKmSquared;
    public readonly double OpticalBearingEnvelopeMultiplier, CoarseRangeFraction, CombatRangeKm, CombatBlendKm;
    public readonly double OpticalScatterStartKm, OpticalScatterScaleKm, OpticalScatterArcsec;
    public readonly double DetectionThreshold;
    public readonly bool InverseSquareEM;
    public ModelSettings(bool defaults, double detectionThreshold = TransponderReception.NormalThreshold, bool inverseSquareEM = true, double opticalScatterArcsec = 21600) {
        OpticalArcsec = .2; OpticalSizeCentroidFraction = .05; KnownSizeFraction = .001; UnknownSizeFraction = .2;
        RadarRangeM = 10; RadarArcsec = 60; LidarRangeM = .5; LidarArcsec = .5; IRArcsec = 30; EMArcsec = 900;
        ClockSigmaSeconds = 20e-9; TimestampJitterSeconds = 20e-9; SharedRangeFloorM = .1; SharedBearingArcsec = .02;
        ComfortRangeKm = 250; ComfortMPerKm = .02; OutsideComfortMPerKmSquared = .01;
        OpticalBearingEnvelopeMultiplier = 200; CoarseRangeFraction = .1;
        OpticalScatterStartKm = 1000000; OpticalScatterScaleKm = 59000000; OpticalScatterArcsec = opticalScatterArcsec;
        CombatRangeKm = DefaultCombatRangeKm; CombatBlendKm = 120;
        DetectionThreshold = detectionThreshold;
        InverseSquareEM = inverseSquareEM;
    }
    public static ModelSettings Default => new ModelSettings(true);
}
public readonly struct ContactResolution {
    public readonly bool HasEstimate, HasMeasuredRange;
    public readonly double RangeSigmaM, CrossRangeSigmaM, PositionRmsM, ObservationAgeSeconds, RoundTripSeconds;
    public readonly MeasurementSources Sources;
    public ContactResolution(bool hasEstimate, bool hasMeasuredRange, double range, double cross, double age, double roundTrip, MeasurementSources sources) {
        HasEstimate = hasEstimate; HasMeasuredRange = hasMeasuredRange; RangeSigmaM = range; CrossRangeSigmaM = cross;
        PositionRmsM = Math.Sqrt(range * range + cross * cross); ObservationAgeSeconds = age; RoundTripSeconds = roundTrip; Sources = sources;
    }
    public static ContactResolution Missing => new ContactResolution(false, false, double.PositiveInfinity, double.PositiveInfinity, 0, 0, MeasurementSources.None);
}

public static class ContactModel {
    public const double LightSpeedMps = 299792458;
    public const double ArcsecondRadians = Math.PI / (180 * 3600);
    static double Square(double x) => x * x;
    static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);
    static double Information(double sigma) => 1 / Math.Max(1e-24, sigma * sigma);
    static double Penalty(double quality) => Math.Sqrt(1 + 1 / Math.Max(1e-30, quality));

    public static ContactResolution Evaluate(SensorSpec[] sensors, in Observation target, in ModelSettings settings) {
        if (!Finite(target.RangeKm) || target.RangeKm < 0 || !Finite(target.CrossSection) || target.CrossSection < 0 ||
            !Finite(target.ProjectedSizeM) || target.ProjectedSizeM <= 0 || !Finite(target.SampleAgeSeconds) || target.SampleAgeSeconds < 0 ||
            !Finite(target.VelocitySigmaMps) || target.VelocitySigmaMps < 0 || !Finite(target.AccelerationSigmaMps2) || target.AccelerationSigmaMps2 < 0 ||
            !Finite(target.HeatSignature) || target.HeatSignature < 0 || !Finite(target.EMEmission) || target.EMEmission < 0) return ContactResolution.Missing;
        if (target.KnownPosition) return new ContactResolution(true, true, 0, 0, 0, 0, MeasurementSources.KnownPosition);
        if (target.Occluded || sensors == null || sensors.Length == 0) return ContactResolution.Missing;
        double distance = Math.Max(1, target.RangeKm * 1000), rangeKm = distance / 1000;
        double radialInfo = 0, angularInfo = 0, opticalSizeInfo = 0, timestampInfo = 0;
        double timestampSignal = target.TransponderBroadcasting && target.EMEmission > 0 ? TransponderReception.Signal(sensors,rangeKm,settings.InverseSquareEM) : 0;
        bool timestampReceived = settings.DetectionThreshold > 0 && timestampSignal >= settings.DetectionThreshold;
        // Fade timing information through the last 2% of signal margin to avoid a position-error cliff at packet loss.
        double timestampWeight = timestampReceived ? Math.Min(1,Math.Max(0,
            (timestampSignal - settings.DetectionThreshold) / (settings.DetectionThreshold * .02))) : 0;
        var sources = MeasurementSources.None;
        foreach (var sensor in sensors) {
            if (!sensor.On || !Finite(sensor.BaseRangeKm) || sensor.BaseRangeKm <= 0 || !Finite(sensor.Strength) || sensor.Strength < 0) continue;
            double ratio = sensor.BaseRangeKm / rangeKm, ratio2 = ratio * ratio;
            double angularSigma, penalty;
            switch (sensor.Kind) {
                case SensorKind.Radar:
                case SensorKind.Lidar:
                    if (sensor.Strength <= 0 || target.CrossSection <= 0) continue;
                    // Active echo proxy: inverse fourth power. This scales uncertainty, not native detection.
                    penalty = Penalty(sensor.Strength * target.CrossSection * ratio2 * ratio2);
                    bool lidar = sensor.Kind == SensorKind.Lidar;
                    radialInfo += Information((lidar ? settings.LidarRangeM : settings.RadarRangeM) * penalty);
                    angularSigma = (lidar ? settings.LidarArcsec : settings.RadarArcsec) * ArcsecondRadians * penalty;
                    angularInfo += Information(angularSigma);
                    sources |= lidar ? MeasurementSources.Lidar : MeasurementSources.Radar;
                    break;
                case SensorKind.Optical:
                    if (target.CrossSection <= 0) continue;
                    // Stable bearing near the instrument's reference envelope; angular noise rises at extreme range.
                    double opticalRatio = rangeKm / (sensor.BaseRangeKm * settings.OpticalBearingEnvelopeMultiplier);
                    angularSigma = settings.OpticalArcsec * ArcsecondRadians * (25 / sensor.BaseRangeKm) *
                        Math.Sqrt(1 + opticalRatio) / Math.Sqrt(target.CrossSection);
                    // Extra interplanetary imaging/pointing uncertainty; saturates instead of exploding at extreme range.
                    double scatterRatio = Math.Max(0,rangeKm-settings.OpticalScatterStartKm)/settings.OpticalScatterScaleKm;
                    double scatter = settings.OpticalScatterArcsec * ArcsecondRadians * (25/sensor.BaseRangeKm) *
                        scatterRatio / Math.Sqrt(1+scatterRatio*scatterRatio) / Math.Sqrt(target.CrossSection);
                    angularSigma = Math.Sqrt(angularSigma*angularSigma+scatter*scatter);
                    angularInfo += Information(angularSigma);
                    opticalSizeInfo += Information(distance * distance * angularSigma * settings.OpticalSizeCentroidFraction / target.ProjectedSizeM);
                    sources |= MeasurementSources.Optical;
                    break;
                case SensorKind.IR:
                    if (target.HeatSignature <= 0) continue;
                    angularSigma = settings.IRArcsec * ArcsecondRadians * Penalty(target.HeatSignature * ratio2);
                    angularInfo += Information(angularSigma); sources |= MeasurementSources.IR;
                    break;
                case SensorKind.EM:
                    if (target.EMEmission <= 0) continue;
                    penalty = Penalty(target.EMEmission * ratio2);
                    angularInfo += Information(settings.EMArcsec * ArcsecondRadians * penalty);
                    sources |= MeasurementSources.EM;
                    if (timestampWeight > 0) {
                        double timingPenalty = Penalty(TransponderReception.BroadcastEmission * ratio2);
                        timestampInfo += timestampWeight * timestampWeight * Information(LightSpeedMps * settings.TimestampJitterSeconds * timingPenalty);
                        sources |= MeasurementSources.Timestamp;
                    }
                    break;
            }
        }
        if (angularInfo <= 0) return ContactResolution.Missing;
        if (timestampInfo > 0) radialInfo += Information(Math.Sqrt(1 / timestampInfo + Square(LightSpeedMps * settings.ClockSigmaSeconds)));
        if (opticalSizeInfo > 0) {
            double sizeFraction = target.KnownHull ? settings.KnownSizeFraction : settings.UnknownSizeFraction;
            double opticalSigma = Math.Sqrt(1 / opticalSizeInfo + Square(distance * sizeFraction));
            // Explicit early-game abstraction: preserve close-range passive navigation even for unknown dark wrecks.
            // This is a shared track/proximity prior, not range information manufactured by telescope bearing alone.
            double outside = Math.Max(0, rangeKm - settings.ComfortRangeKm);
            double comfortCap = Math.Max(.25, Math.Min(rangeKm, settings.ComfortRangeKm) * settings.ComfortMPerKm) +
                settings.OutsideComfortMPerKmSquared * outside * outside;
            if (comfortCap < opticalSigma) { opticalSigma = comfortCap; sources |= MeasurementSources.LocalNavigation; }
            radialInfo += Information(opticalSigma);
        }
        bool measuredRange = radialInfo > 0;
        // A weak new observation must not make a coarse existing estimate worse.
        double prior = distance * settings.CoarseRangeFraction;
        double radial = measuredRange ? Math.Sqrt(1 / (radialInfo + Information(prior))) : prior;
        double transverse = Math.Min(distance / Math.Sqrt(2), distance / Math.Sqrt(angularInfo));
        // A reflected observation is one light-time old on reception. Two light-times is the active acquisition latency.
        double age = target.SampleAgeSeconds + distance / LightSpeedMps;
        double motionVariance = Square(target.VelocitySigmaMps * age) + Square(.5 * target.AccelerationSigmaMps2 * age * age);
        radial = Math.Sqrt(radial * radial + Square(settings.SharedRangeFloorM) + motionVariance);
        transverse = Math.Sqrt(transverse * transverse + Square(distance * settings.SharedBearingArcsec * ArcsecondRadians) + motionVariance);
        // Fresh acquired contacts retain native combat precision. Lost/stale tracks do not gain knowledge.
        // Use the strongest native railgun's base 120 km envelope, without its gunnery multiplier.
        if (target.SampleAgeSeconds == 0) {
            double blend = Math.Max(0, Math.Min(1, (rangeKm - settings.CombatRangeKm) / settings.CombatBlendKm));
            double scale = blend * blend * (3 - 2 * blend);
            radial *= scale; transverse *= scale;
        }
        // No solar-system distance cutoff: reject numerical overflow, not Saturn.
        double roundTrip = 2 * distance / LightSpeedMps;
        if (!Finite(radial) || !Finite(transverse) || !Finite(age) || !Finite(roundTrip) || !Finite(radial*radial+transverse*transverse)) return ContactResolution.Missing;
        return new ContactResolution(true, measuredRange, radial, transverse, age, roundTrip, sources);
    }
}
