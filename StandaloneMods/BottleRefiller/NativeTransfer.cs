using System;
using System.Collections.Generic;

namespace Ostrabus.BottleRefiller
{
    internal static class NativeTransfer
    {
        internal static GasState Snapshot(CondOwner owner, string[] species, bool destination)
        {
            var gas = owner.GasContainer;
            if (gas == null || gas.IsVoid || gas.mapGasMols1 == null || gas.mapDGasMols == null) return null;
            var amounts = new double[species.Length];
            // Reject unknown species rather than silently filtering them away.
            foreach (var pair in gas.mapGasMols1)
                if (pair.Key != "StatGasMolTotal" && Array.IndexOf(species, pair.Key) < 0) return null;
            for (int i = 0; i < species.Length; i++) gas.mapGasMols1.TryGetValue(species[i], out amounts[i]);
            return new GasState(amounts, owner.GetCondAmount("StatGasTemp"), owner.GetCondAmount("StatVolume"),
                destination ? owner.GetCondAmount("StatGasPressureMax") : 0);
        }

        internal static bool Fill(CondOwner rack, CondOwner source, IList<CondOwner> bottles, double elapsedSeconds = 1)
        {
            if (!Runtime.Working(rack) || !Runtime.LiveOwner(source) || source.GasContainer == null ||
                ReferenceEquals(source, rack) || source.GasContainer.IsVoid || bottles.Count == 0 || bottles.Count > 4) return false;
            var seen = new HashSet<CondOwner>();
            foreach (var bottle in bottles)
                if (!Runtime.Bottle(bottle, rack) || ReferenceEquals(source, bottle) || !seen.Add(bottle)) return false;

            // Settle pending native work before computing either capacity or supply.
            source.GasContainer.Run();
            foreach (var bottle in bottles) bottle.GasContainer.Run();
            if (!Runtime.Working(rack) || !Runtime.LiveOwner(source)) return false;
            var species = FluidStrings.mol.ToArray();
            var sourceState = Snapshot(source, species, false);
            var destinationStates = new GasState[bottles.Count];
            for (int b = 0; b < bottles.Count; b++)
            {
                if (!Runtime.Bottle(bottles[b], rack)) return false;
                destinationStates[b] = Snapshot(bottles[b], species, true);
            }
            var plan = TransferPlan.Create(sourceState, destinationStates, elapsedSeconds);
            if (plan == null) return false;

            // All validation precedes mutation. Stage balanced deltas for the whole
            // batch without yielding, then let native pressure/mass code commit it.
            var sourceGas = source.GasContainer;
            for (int s = 0; s < species.Length; s++)
                if (plan.Debits[s] != 0) AddDelta(sourceGas, species[s], -plan.Debits[s]);
            source.GasChanged = true;
            for (int b = 0; b < bottles.Count; b++)
            {
                var gas = bottles[b].GasContainer;
                for (int s = 0; s < species.Length; s++)
                    if (plan.Credits[b][s] != 0) AddDelta(gas, species[s], plan.Credits[b][s]);
                gas.fDGasTemp += plan.Temperatures[b] - destinationStates[b].Temperature;
                bottles[b].GasChanged = true;
            }
            sourceGas.Run();
            foreach (var bottle in bottles) bottle.GasContainer.Run();
            return true;
        }

        private static void AddDelta(GasContainer gas, string species, double delta)
        {
            gas.mapDGasMols.TryGetValue(species, out double previous);
            gas.mapDGasMols[species] = previous + delta;
        }
    }
}
