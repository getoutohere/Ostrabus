using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Ostrabus.NavConsole;

internal static class ProximityRange {
    // Existing saves use these keys; they are not C# namespace references.
    internal const string RangeKey = "TemplateExample.ProximityRangeKm", ShipKey = "TemplateExample.ProximityShip";
    internal const double KmPerAU = 149597870.7;
    internal const float NativeShipAU = 3.3422936E-08f;
    internal const double NativeBodyAU = 3.342293553032505E-08;
    sealed class Value { internal int Km; internal Value(int km) { Km = km; } }
    static ConditionalWeakTable<Ship, Value> values = new ConditionalWeakTable<Ship, Value>();
    internal static bool Valid(int km) => km == 5 || km == 10 || km == 15 || km == 20;
    internal static int Get(Ship ship) {
        if (ship == null || ship.bDestroyed) return 5;
        if (values.TryGetValue(ship, out var value)) return value.Km;
        if (ship.LoadState < Ship.Loaded.Edit || ship.aNavs == null || ship.aNavs.Count == 0) return 5;
        int km = 5;
        foreach (var nav in ship.aNavs) {
            if (nav?.mapGUIPropMaps == null || !nav.mapGUIPropMaps.TryGetValue("Panel A", out var map)) continue;
            if (map.TryGetValue(ShipKey, out string owner) && owner == ship.strRegID
                && map.TryGetValue(RangeKey, out string raw) && int.TryParse(raw, out int saved) && Valid(saved)) { km = saved; break; }
        }
        values.Add(ship, new Value(km));
        return km;
    }
    internal static void Set(Ship ship, int km) {
        if (!Valid(km)) throw new ArgumentOutOfRangeException(nameof(km));
        if (ship == null || ship.bDestroyed || ship.LoadState < Ship.Loaded.Edit) return;
        values.GetValue(ship, _ => new Value(km)).Km = km;
        Remember(ship);
        // Clear acknowledgements when changing the range, so a newly relevant
        // contact isn't silently hidden by an earlier Clear button press.
        ship.aProxIgnores?.Clear();
    }
    internal static void Remember(Ship ship) {
        int km = Get(ship);
        if (ship?.aNavs == null) return;
        string text = km.ToString(System.Globalization.CultureInfo.InvariantCulture);
        foreach (var nav in ship.aNavs) {
            if (nav?.mapGUIPropMaps == null || !nav.mapGUIPropMaps.TryGetValue("Panel A", out var map)) continue;
            map[RangeKey] = text; map[ShipKey] = ship.strRegID;
        }
    }
    internal static void Reset(UnityEngine.SceneManagement.Scene _) { values = new ConditionalWeakTable<Ship, Value>(); }
}
