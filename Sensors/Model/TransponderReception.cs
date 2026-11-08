using System;
using Ostrabus.EMPropagation;

namespace Ostrabus.Sensors.Model;

public static class TransponderReception {
    public const double BroadcastEmission = .6000000238418579;
    public const double NormalThreshold = (double).3f;
    public static double Signal(SensorSpec[] sensors, double rangeKm, bool inverseSquare = true) {
        if (sensors == null || double.IsNaN(rangeKm) || double.IsInfinity(rangeKm) || rangeKm < 0) return 0;
        double signal = 0;
        foreach (var sensor in sensors) {
            if (sensor.Kind != SensorKind.EM || !sensor.On || sensor.BaseRangeKm <= 0 ||
                double.IsNaN(sensor.BaseRangeKm) || double.IsInfinity(sensor.BaseRangeKm) ||
                sensor.Strength < 0 || double.IsNaN(sensor.Strength) || double.IsInfinity(sensor.Strength)) continue;
            signal += PropagationModel.Signal(rangeKm,sensor.BaseRangeKm,BroadcastEmission,inverseSquare);
        }
        return signal;
    }
    public static bool Received(SensorSpec[] sensors, in Observation target, double threshold = NormalThreshold) =>
        target.TransponderBroadcasting && !target.Occluded && target.EMEmission > 0 && threshold > 0 && Signal(sensors,target.RangeKm) >= threshold;
}
