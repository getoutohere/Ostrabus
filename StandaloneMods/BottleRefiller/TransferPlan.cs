using System;

namespace Ostrabus.BottleRefiller
{
    // Engine-independent thermodynamics. The native adapter owns species IDs,
    // identity checks, pending deltas and the actual GasContainer.Run calls.
    internal sealed class GasState
    {
        internal readonly double[] Species;
        internal readonly double Temperature, Volume, MaxPressure;
        internal GasState(double[] species, double temperature, double volume, double maxPressure = 0)
        {
            Species = species; Temperature = temperature; Volume = volume; MaxPressure = maxPressure;
        }
    }

    internal sealed class TransferPlan
    {
        internal const double R = 0.008314000442624092;
        internal readonly double[] Debits;
        internal readonly double[][] Credits;
        internal readonly double[] Temperatures;
        internal readonly double Total;

        private TransferPlan(double[] debits, double[][] credits, double[] temperatures, double total)
        {
            Debits = debits; Credits = credits; Temperatures = temperatures; Total = total;
        }

        internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static bool Sum(GasState state, int count, out double total)
        {
            total = 0;
            if (state == null || state.Species == null || state.Species.Length != count ||
                !Finite(state.Temperature) || state.Temperature <= 0 ||
                !Finite(state.Volume) || state.Volume <= 0) return false;
            foreach (double amount in state.Species)
            {
                if (!Finite(amount) || amount < 0) return false;
                total += amount;
            }
            return Finite(total);
        }

        internal static TransferPlan Create(GasState source, GasState[] bottles, double elapsedSeconds = 1)
        {
            if (source?.Species == null || source.Species.Length == 0 || bottles == null ||
                bottles.Length == 0 || bottles.Length > 4 ||
                !Sum(source, source.Species.Length, out double supply) || supply <= 0) return null;
            var requests = new double[bottles.Length];
            var existing = new double[bottles.Length];
            double requested = 0;
            for (int b = 0; b < bottles.Length; b++)
            {
                var bottle = bottles[b];
                if (!Sum(bottle, source.Species.Length, out existing[b]) ||
                    !Finite(bottle.MaxPressure) || bottle.MaxPressure <= 0) return null;
                // Tiny headroom absorbs native floating-point condition updates.
                double energyLimit = bottle.MaxPressure * (1 - 1e-10) * bottle.Volume / R;
                double existingEnergy = existing[b] * bottle.Temperature;
                double targetFraction = FillCurve.NextFraction(existingEnergy / energyLimit, elapsedSeconds);
                double spare = energyLimit * targetFraction - existingEnergy;
                if (!Finite(energyLimit) || energyLimit <= 0 || !Finite(spare)) return null;
                requests[b] = Math.Max(0, spare / source.Temperature);
                requested += requests[b];
            }
            if (!Finite(requested) || requested <= 1e-12) return null;
            double scale = Math.Min(1, supply * (1 - 1e-14) / requested);
            var debit = new double[source.Species.Length];
            var credit = new double[bottles.Length][];
            var temperatures = new double[bottles.Length];
            double total = 0;
            for (int b = 0; b < bottles.Length; b++)
            {
                credit[b] = new double[debit.Length];
                double incoming = 0;
                for (int s = 0; s < debit.Length; s++)
                {
                    double amount = requests[b] * scale * (source.Species[s] / supply);
                    if (!Finite(amount) || amount < 0) return null;
                    credit[b][s] = amount;
                    debit[s] += amount;
                    incoming += amount;
                }
                double combined = existing[b] + incoming;
                temperatures[b] = incoming == 0 ? bottles[b].Temperature :
                    (existing[b] / combined * bottles[b].Temperature + incoming / combined * source.Temperature);
                if (!Finite(temperatures[b]) || temperatures[b] <= 0) return null;
                total += incoming;
            }
            for (int s = 0; s < debit.Length; s++)
                if (debit[s] > source.Species[s]) return null;
            return total > 1e-12 && Finite(total) ? new TransferPlan(debit, credit, temperatures, total) : null;
        }
    }
}
