using System;
using BepInEx.Configuration;

// Offline experiment, not a BepInPlugin or a change to the running game.
internal sealed class CurvePrototype
{
    internal readonly ConfigEntry<double> HalfLifeDays;
    internal readonly ConfigEntry<double> CurveShape;

    internal CurvePrototype(ConfigFile config)
    {
        HalfLifeDays = config.Bind("Turnover", "HalfLifeDays", 7d,
            "Accumulated in-game days outside the region at which an initially eligible wreck has a 50% replacement chance. Must be finite and positive.");
        CurveShape = config.Bind("Turnover", "CurveShape", 1d,
            "1 = exponential; below 1 = more early turnover with a longer tail; above 1 = slower early turnover followed by a sharper rise. Must be finite and positive.");
    }

    internal static bool Valid(double value) => value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);

    // Carry accumulated ABSENT age for survivors if a shaped curve is offered.
    // Without it, splitting travel into short trips changes the intended curve.
    internal static bool TryProbability(double ageDays, double absentDays, double halfLifeDays, double shape, out double probability)
    {
        probability = 0;
        if (!Valid(halfLifeDays) || !Valid(shape) || ageDays < 0 || absentDays < 0 ||
            double.IsNaN(ageDays) || double.IsNaN(absentDays) || double.IsInfinity(ageDays) || double.IsInfinity(absentDays)) return false;
        if (absentDays == 0) return true;
        double hazard = shape == 1 ? absentDays / halfLifeDays :
            Math.Pow((ageDays + absentDays) / halfLifeDays, shape) - Math.Pow(ageDays / halfLifeDays, shape);
        if (double.IsNaN(hazard) || hazard < 0) return false;
        hazard *= Math.Log(2);
        // Preserve small probabilities instead of cancellation in 1-exp(-x).
        probability = hazard < 1e-5 ? hazard * (1 - hazard * (.5 - hazard / 6)) : 1 - Math.Exp(-hazard);
        return probability >= 0 && probability <= 1;
    }

    // Randomly permute candidates first, then use one independent uniform offset.
    // Each candidate keeps its own probability; total count is floor/ceil(sum p).
    internal static bool[] Select(double[] probabilities, int[] order, double offset)
    {
        if (probabilities == null || order == null || probabilities.Length != order.Length ||
            double.IsNaN(offset) || offset < 0 || offset >= 1) throw new ArgumentException("Invalid selection input.");
        var selected = new bool[probabilities.Length];
        var seen = new bool[probabilities.Length];
        double total = offset;
        foreach (int index in order)
        {
            if (index < 0 || index >= probabilities.Length || seen[index]) throw new ArgumentException("Order is not a permutation.");
            seen[index] = true;
            double p = probabilities[index];
            if (double.IsNaN(p) || p < 0 || p > 1) throw new ArgumentException("Probability outside [0,1].");
            double next = total + p;
            selected[index] = Math.Floor(next) > Math.Floor(total);
            total = next;
        }
        return selected;
    }
}
