using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;

namespace Ostrabus.ShipNavigation;

// Intentional simulation policy: a full home station is not a reason to kill
// an ordinary retiring pilot. Only offscreen, population-managed stations can
// accept overflow. Native faction matching, transfer and population culling run.
internal static class CrewRetirement {
    internal static bool Routine(Ship source) => source != null && !source.bDestroyed && source.bRemove &&
        source.objSS != null && !source.objSS.bIsBO && source.LoadState < Ship.Loaded.Edit &&
        !source.IsStation() && !source.IsStationHidden() && !source.IsDocked() && source.ShipCO != null &&
        source.ShipCO.GetCondAmount("IsStale", isThreshold: false) > 0 &&
        StarSystem.fEpoch - source.ShipCO.GetCondAmount("IsStale", isThreshold: false) >= 10800;

    static bool Managed(Ship station, double capacity) => station != null && !station.bDestroyed && !station.bRemove &&
        !station.HideFromSystem && station.LoadState < Ship.Loaded.Edit &&
        station.IsStation() && !station.IsStationHidden() && capacity > 0 && !double.IsInfinity(capacity);

    internal static List<Ship> Stations(StarSystem system, bool includeHidden, Ship source) {
        var stations = system.GetStations(includeHidden);
        if (!Routine(source)) return stations;
        var ordinary = new List<Ship>(stations.Count);
        var crowded = new List<(Ship Station, double Excess, int Order)>();
        foreach (var station in stations) {
            double capacity = station.MaxPopulation;
            int population = station.Population;
            if (population >= capacity && Managed(station, capacity))
                crowded.Add((station, population - capacity, ordinary.Count + crowded.Count));
            else ordinary.Add(station);
        }
        // Preserve the native order for every station with room. Overflow is
        // considered only after those choices, with stable least-excess order.
        crowded.Sort((a, b) => {
            int order = a.Excess.CompareTo(b.Excess);
            return order != 0 ? order : a.Order.CompareTo(b.Order);
        });
        foreach (var row in crowded) ordinary.Add(row.Station);
        return ordinary;
    }

    internal static double Capacity(Ship station, Ship source) {
        double capacity = station.MaxPopulation;
        return Routine(source) && Managed(station, capacity) ? double.PositiveInfinity : capacity;
    }
}

[HarmonyPatch(typeof(StarSystem), "TryRecycleCrew")]
internal static class CrewRetirementPatch {
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var code = new List<CodeInstruction>(instructions);
        var stations = AccessTools.Method(typeof(StarSystem), nameof(StarSystem.GetStations));
        var capacity = AccessTools.PropertyGetter(typeof(Ship), nameof(Ship.MaxPopulation));
        int sources = 0, limits = 0;
        for (int i = 0; i < code.Count; i++) {
            bool isStations = code[i].Calls(stations);
            if (!isStations && !code[i].Calls(capacity)) continue;
            var original = code[i];
            // Existing receiver/arguments stay on the stack; supply the retiring
            // hull explicitly. No thread-static scope or replacement AI loop.
            code.Insert(i++, new CodeInstruction(OpCodes.Ldarg_1).MoveLabelsFrom(original));
            original.opcode = OpCodes.Call;
            original.operand = AccessTools.Method(typeof(CrewRetirement), isStations ? nameof(CrewRetirement.Stations) : nameof(CrewRetirement.Capacity));
            if (isStations) sources++; else limits++;
        }
        if (sources != 1 || limits != 1)
            throw new InvalidOperationException("Crew retirement: expected one station enumeration and one population limit.");
        return code;
    }
}
