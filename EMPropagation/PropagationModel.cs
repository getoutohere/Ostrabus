using System;

namespace Ostrabus.EMPropagation;

// Pure arithmetic shared by offline comparisons and future runtime patches. No hooks installed.
public static class PropagationModel {
    public static double Signal(double rangeKm, double baseRangeKm, double emissions, bool inverseSquare = true) {
        if (emissions <= 0 || baseRangeKm <= 0) return 0;
        double ratio = baseRangeKm / Math.Max(rangeKm, .009999999776482582);
        double distanceMod = rangeKm <= baseRangeKm ? ratio : inverseSquare ? ratio * ratio : ratio * ratio * ratio;
        return Math.Min(.31, emissions * distanceMod);
    }
    public static double DetectionDistance(double baseRangeKm, double emissions, double threshold, bool inverseSquare = true) {
        if (emissions <= 0 || threshold <= 0 || baseRangeKm <= 0) return 0;
        double ratio = emissions / threshold;
        return baseRangeKm * (ratio <= 1 ? ratio : inverseSquare ? Math.Sqrt(ratio) : Math.Pow(ratio, 1d/3));
    }
}
