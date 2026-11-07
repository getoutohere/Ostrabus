using System;
using System.Collections.Generic;

namespace Ostrabus.Micrometeoroids;

internal static class MeteorTargets {
    internal static double Weight(Ship ship) => ship != null && ship.LoadState == Ship.Loaded.Full && !ship.bDestroyed &&
        !ship.bRemove && !ship.HideFromSystem && ship.aTiles != null && ship.aTiles.Count > 0 ? Math.Max(1, (double)ship.nCols * ship.nRows) : 0;
    internal static Ship Select(IReadOnlyList<Ship> ships, Ship fallback, double roll) {
        double total = 0;
        if (ships != null) for (int i = 0; i < ships.Count; i++) total += Weight(ships[i]);
        if (total <= 0) return fallback;
        double cursor = Math.Max(0, Math.Min(roll, .9999999999999999)) * total;
        Ship last = fallback;
        for (int i = 0; i < ships.Count; i++) {
            double weight = Weight(ships[i]);
            if (weight <= 0) continue;
            last = ships[i]; cursor -= weight;
            if (cursor < 0) return last;
        }
        return last;
    }
    internal static Ship Spawn(Ship original, float multiplier, bool resetTimeScale) {
        var selected = Select(CrewSim.GetAllLoadedShips(), original, UnityEngine.Random.value);
        MeteorTest.Target(selected);
        StarSystem.SpawnMicroMeteoroid(selected, multiplier, resetTimeScale);
        return selected;
    }
}
