using System;
using HarmonyLib;
using Ostranauts.Ships;
using Ostranauts.Ships.Commands;

namespace Ostrabus.ShipNavigation;

// Native retreat selection is authoritative. Its small setter omitted the
// route handoff and left target metadata/predictions belonging to the old trip.
[HarmonyPatch(typeof(TargetEscapeStation), "SetTarget")]
internal static class EscapeTargetPatch {
    [HarmonyPrefix]
    internal static bool Prefix(TargetEscapeStation __instance, Ship target) {
        Ship ship = __instance.ShipUs;
        var before = new NavigationTargetKey(ship);
        TargetData data = ship.TargetData;
        if (data == null || !ReferenceEquals(data.Ship, target) || !ReferenceEquals(data.Situ, target.objSS) || data.TargetRegId != target.strRegID)
            data = new TargetData(target);
        // Existing ApplyToShip observation clears the old manual prediction.
        data.ApplyToShip(ship);
        if (!before.Same(new NavigationTargetKey(ship))) ship.objSS.ResetNavData();
        return false;
    }
}
