using System;
using HarmonyLib;
using Ostranauts.ShipGUIs.Utilities;

namespace Ostrabus.ShipNavigation;

// External additions change the origin of an absolute remaining-fuel budget,
// not its expenditure. This is independent of pilot role and navigation policy.
internal static class RcsReplenishment {
    static readonly AccessTools.FieldRef<NavData, Ship> PlanShip = AccessTools.FieldRefAccess<NavData, Ship>("_shipUs");
    internal struct Capture {
        internal NavData Plan;
        internal double Before;
        internal bool Track, Physical;
    }
    internal static Capture Before(Ship ship, bool physicalRefill) {
        if (ship?.objSS == null || ship.bDestroyed || !ship.IsAIShip || ship.objSS.bIsBO || ship.LoadState < Ship.Loaded.Shallow ||
            ReferenceEquals(CrewSim.coPlayer?.ship, ship) ||
            CrewSim.coPlayer != null && CrewSim.system?.GetShipOwner(ship.strRegID) == CrewSim.coPlayer.strID) return default;
        bool physical = ship.LoadState > Ship.Loaded.Shallow;
        // AIRefuel only assigns the shallow counter; it does not fill Full tanks.
        if (physical && !physicalRefill) return default;
        if (physical && ship.NavPlayerManned) return default;
        NavData plan = ship.objSS.NavData;
        if (plan != null && !ReferenceEquals(PlanShip(plan), ship)) plan = null;
        if (plan == null && !physical) return default;
        double fuel = ship.GetRCSRemain();
        return NavigationFuel.Finite(fuel) && fuel >= 0 ? new Capture { Plan = plan, Before = fuel, Track = true, Physical = physical } : default;
    }
    internal static void After(Ship ship, Capture capture) {
        if (!capture.Track || ship?.objSS == null || ship.bDestroyed) return;
        double added = ship.GetRCSRemain() - capture.Before;
        if (!NavigationFuel.Finite(added) || added <= 0) return;
        // Preserve any pending shallow-to-physical debit; assigning the physical
        // total would forgive fuel already spent in shallow simulation.
        if (capture.Physical && NavigationFuel.Finite(ship.fShallowRCSRemass) && ship.fShallowRCSRemass >= 0 &&
            NavigationFuel.Finite(ship.fShallowRCSRemass + added)) ship.fShallowRCSRemass += added;
        if (Rebase(ship, capture.Plan, added)) Ostrabus.Venus.VorbNavPlanGuard.RecordFuelAdded(ship, added);
    }
    internal static bool Rebase(Ship ship, NavData plan, double added) {
        if (plan == null || !ReferenceEquals(ship?.objSS?.NavData, plan) || !NavigationFuel.Finite(added) || added <= 0) return false;
        var points = plan.GetNavDataPoints();
        if (points == null || points.Count == 0) return false;
        // Validate every write before changing any point. Negative old budgets
        // may become affordable after refilling; route policy is evaluated later.
        foreach (var point in points) if (point == null || !NavigationFuel.Finite(point.FuelLevel) || !NavigationFuel.Finite(point.FuelLevel + added)) return false;
        foreach (var point in points) point.FuelLevel += added;
        return true;
    }
}

[HarmonyPatch(typeof(Ship), nameof(Ship.RefuelRCS), new[] { typeof(float) })]
internal static class RcsPhysicalReplenishmentPatch {
    // Capture even when the shallow setter fix subsequently skips the original.
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    static void Prefix(Ship __instance, out RcsReplenishment.Capture __state) => __state = RcsReplenishment.Before(__instance, true);
    [HarmonyPostfix]
    static void Postfix(Ship __instance, RcsReplenishment.Capture __state) => RcsReplenishment.After(__instance, __state);
}

[HarmonyPatch(typeof(Ship), nameof(Ship.AIRefuel))]
internal static class RcsAutomaticReplenishmentPatch {
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    static void Prefix(Ship __instance, out RcsReplenishment.Capture __state) => __state = RcsReplenishment.Before(__instance, false);
    [HarmonyPostfix]
    static void Postfix(Ship __instance, RcsReplenishment.Capture __state) => RcsReplenishment.After(__instance, __state);
}
