using System;

namespace Ostrabus.BottleRefiller
{
    internal static class FillCurve
    {
        internal const double Knee = .9;
        internal const double FastSeconds = 5;
        internal const double TailSeconds = 25;

        internal static double NextFraction(double fraction, double elapsedSeconds)
        {
            if (!TransferPlan.Finite(fraction) || !TransferPlan.Finite(elapsedSeconds) || elapsedSeconds < 0)
                return double.NaN;
            if (fraction >= 1 || elapsedSeconds == 0) return fraction;
            fraction = Math.Max(0, fraction);
            // Infer the current position on the pressure curve. No per-bottle
            // timer, saved job, substep loop or high-frequency simulation.
            double age = fraction <= Knee ? fraction / Knee * FastSeconds :
                FastSeconds + TailSeconds * (1 - Math.Sqrt((1 - fraction) / (1 - Knee)));
            double next = age + Math.Min(1, elapsedSeconds);
            if (next >= FastSeconds + TailSeconds - 1e-6) return 1;
            if (next <= FastSeconds) return Knee * next / FastSeconds;
            double remaining = (FastSeconds + TailSeconds - next) / TailSeconds;
            return 1 - (1 - Knee) * remaining * remaining;
        }
    }

    internal sealed class ServiceClock
    {
        private double previous = double.NaN;

        internal bool Due(double now, out double elapsed)
        {
            elapsed = 0;
            if (!TransferPlan.Finite(now)) return false;
            if (double.IsNaN(previous) || now < previous) { previous = now; return false; }
            if (now - previous < 1) return false;
            // Time skips do one bounded service pass, not accumulated backfill.
            elapsed = 1;
            previous = now;
            return true;
        }
    }
}
