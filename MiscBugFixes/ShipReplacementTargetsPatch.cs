using System;
using HarmonyLib;
using Ostranauts.Ships;

namespace Ostrabus.MiscBugFixes;

// SaveToShallow replaces a Ship instance without changing its logical identity.
// Transfer only references to that instance; independently owned waypoints stay intact.
[HarmonyPatch(typeof(CrewSim), "DestroyAndReload")]
internal static class ShipReplacementTargetsPatch {
    internal readonly struct Replacement {
        internal readonly StarSystem System;
        internal readonly Ship Previous;
        internal readonly ShipSitu Situation;
        internal readonly string Id;
        internal Replacement(Ship ship) {
            System = CrewSim.system;
            Previous = ship;
            Situation = ship?.objSS;
            Id = ship?.strRegID;
        }
    }
    [HarmonyPrefix]
    internal static void Prefix(Ship ship, out Replacement __state) => __state = new Replacement(ship);

    [HarmonyPostfix]
    internal static void Postfix(Replacement __state) {
        try {
            if (__state.System == null || !ReferenceEquals(__state.System, CrewSim.system) || __state.Id == null || __state.Situation == null) return;
            Ship replacement = __state.System.GetShipByRegID(__state.Id);
            if (replacement == null || replacement.bDestroyed || replacement.objSS == null || ReferenceEquals(replacement, __state.Previous)) return;
            foreach (Ship observer in __state.System.dictShips.Values) {
                if (observer == null || observer.bDestroyed) continue;
                if (ReferenceEquals(observer.shipScanTarget, __state.Previous)) observer.shipScanTarget = replacement;
                if (ReferenceEquals(observer.shipStationKeepingTarget, __state.Previous)) observer.shipStationKeepingTarget = replacement;
                if (ReferenceEquals(observer.shipCombatTarget, __state.Previous)) observer.shipCombatTarget = replacement;
                if (ReferenceEquals(observer.shipUndock, __state.Previous)) observer.shipUndock = replacement;
                if (ReferenceEquals(observer.shipSituTarget, __state.Situation)) observer.shipSituTarget = replacement.objSS;
                TargetData target = observer.TargetData;
                if (target == null) continue;
                if (ReferenceEquals(target.Ship, __state.Previous)) target.Ship = replacement;
                if (ReferenceEquals(target.Situ, __state.Situation)) target.Situ = replacement.objSS;
            }
        } catch (Exception ex) {
            Ostrabus.OstrabusPlugin.Logger?.LogWarning($"[Ship replacement] Could not transfer target references for {__state.Id}: {ex.Message}");
        }
    }
}
