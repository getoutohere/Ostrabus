using System;
using System.Linq;

namespace Ostrabus.BoneyardTurnover
{
    internal static class Curve
    {
        internal static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);
        internal static bool Positive(double x) => Finite(x) && x > 0;

        internal static bool TryProbability(double age, double days, double halfLife, double shape, out double probability)
        {
            probability = 0;
            if (!Positive(halfLife) || !Positive(shape) || !Finite(age) || !Finite(days) || age < 0 || days < 0)
                return false;
            if (days == 0) return true;
            double hazard = shape == 1 ? days / halfLife :
                Math.Pow((age + days) / halfLife, shape) - Math.Pow(age / halfLife, shape);
            if (double.IsNaN(hazard) || hazard < 0) return false;
            hazard *= Math.Log(2);
            probability = hazard < 1e-5 ? hazard * (1 - hazard * (.5 - hazard / 6)) : 1 - Math.Exp(-hazard);
            return probability >= 0 && probability <= 1;
        }

        // Systematic sampling retains each wreck's marginal probability and
        // rounds the total to floor/ceil(sum p), even when survivor ages differ.
        internal static bool[] Select(double[] probabilities, int[] order, double offset)
        {
            if (probabilities == null || order == null || order.Length != probabilities.Length ||
                !Finite(offset) || offset < 0 || offset >= 1) throw new ArgumentException("Invalid sample.");
            var result = new bool[probabilities.Length];
            var seen = new bool[probabilities.Length];
            double total = offset;
            foreach (int i in order)
            {
                if (i < 0 || i >= result.Length || seen[i] || !Finite(probabilities[i]) || probabilities[i] < 0 || probabilities[i] > 1)
                    throw new ArgumentException("Invalid probability or permutation.");
                seen[i] = true;
                double next = total + probabilities[i];
                result[i] = Math.Floor(next) > Math.Floor(total);
                total = next;
            }
            return result;
        }

        internal static int[] Shuffle(int count, Random random)
        {
            var order = Enumerable.Range(0, count).ToArray();
            for (int i = count - 1; i > 0; --i)
            {
                int j = random.Next(i + 1);
                int old = order[i]; order[i] = order[j]; order[j] = old;
            }
            return order;
        }
    }
}
