using System;
using HarmonyLib;
using Ostranauts.Ships;
using Ostranauts.Ships.Commands;
using Ostrabus.ShipNavigation;

namespace Ostrabus.MiscBugFixes;

// SaveToShallow replaces a Ship instance without changing its logical identity.
// Transfer only references to that instance; independently owned waypoints stay intact.
[HarmonyPatch(typeof(CrewSim), "DestroyAndReload")]
internal static class ShipReplacementTargetsPatch {
    [ThreadStatic] internal static Ship Replacing;
    internal readonly struct Replacement {
        internal readonly StarSystem System;
        internal readonly Ship Previous;
        internal readonly ShipSitu Situation;
        internal readonly string Id;
        internal readonly Ship PreviousScope;
        internal readonly bool Captured;
        internal Replacement(Ship ship) {
            System = CrewSim.system;
            Previous = ship;
            Situation = ship?.objSS;
            Id = ship?.strRegID;
            PreviousScope = Replacing;
            Captured = true;
        }
    }
    [HarmonyPrefix]
    internal static void Prefix(Ship ship, out Replacement __state) {
        __state = new Replacement(ship);
        Replacing = ship;
    }

    [HarmonyPostfix]
    internal static void Postfix(Replacement __state) {
        Complete(__state);
    }

    [HarmonyFinalizer]
    internal static void Finalizer(Replacement __state, Exception __exception) {
        if (!__state.Captured) return;
        try {
            if (__exception != null) Complete(__state);
        } finally { Replacing = __state.PreviousScope; }
    }

    internal static void Complete(Replacement state, bool stellarConversion = false) {
        try {
            if (!ShipTargetLifetime.IsCurrentWorld(state.System) || state.Id == null || state.Situation == null || state.Previous == null) return;
            Ship replacement = state.System.GetShipByRegID(state.Id);
            if (ReferenceEquals(replacement, state.Previous) && !state.Previous.bDestroyed) return;
            if (replacement == null || replacement.bDestroyed || replacement.objSS == null) {
                if (stellarConversion && state.System.dictStellarObjects != null &&
                    state.System.dictStellarObjects.TryGetValue(state.Id, out var stellar) && stellar?.objSS != null)
                    ShipTargetLifetime.Converted(state, stellar.objSS);
                else ShipTargetLifetime.Removed(state);
                return;
            }
            if (ReferenceEquals(replacement, state.Previous)) return;
            foreach (Ship observer in state.System.dictShips.Values) {
                if (observer == null || observer.bDestroyed) continue;
                if (ReferenceEquals(observer.shipScanTarget, state.Previous)) observer.shipScanTarget = replacement;
                if (ReferenceEquals(observer.shipStationKeepingTarget, state.Previous)) observer.shipStationKeepingTarget = replacement;
                if (ReferenceEquals(observer.shipCombatTarget, state.Previous)) observer.shipCombatTarget = replacement;
                if (ReferenceEquals(observer.shipUndock, state.Previous)) observer.shipUndock = replacement;
                if (ReferenceEquals(observer.shipSituTarget, state.Situation)) observer.shipSituTarget = replacement.objSS;
                TargetData target = observer.TargetData;
                if (target == null) continue;
                if (ReferenceEquals(target.Ship, state.Previous)) target.Ship = replacement;
                if (ReferenceEquals(target.Situ, state.Situation)) target.Situ = replacement.objSS;
            }
        } catch (Exception ex) {
            Ostrabus.OstrabusPlugin.Logger?.LogWarning($"[Ship targets] Could not complete target lifetime change for {state.Id}: {ex.Message}");
        }
    }
}

internal static class ShipTargetLifetime {
    internal static bool IsCurrentWorld(StarSystem world) => world != null && ReferenceEquals(world, CrewSim.system) && world.aBOs != null && world.dictShips != null;

    static void Invalidate(Ship ship) {
        // Stop the abandoned command, never change momentum, fuel or task policy.
        if (ship.objSS != null) {
            if (ship.objSS.NavData != null || (ship.IsAIShip && !ship.NavPlayerManned)) NavigationRoute.Stop(ship);
            else ship.objSS.ResetNavData();
        }
        ship.ClearShipTarget();
    }

    internal static void Converted(ShipReplacementTargetsPatch.Replacement state, ShipSitu situation) {
        foreach (Ship observer in state.System.dictShips.Values) {
            if (observer == null || observer.bDestroyed) continue;
            bool shared = ReferenceEquals(observer.shipSituTarget, state.Situation);
            if (shared) observer.shipSituTarget = situation;
            if (ReferenceEquals(observer.shipScanTarget, state.Previous)) observer.shipScanTarget = null;
            TargetData target = observer.TargetData;
            if (target != null) {
                if (ReferenceEquals(target.Ship, state.Previous)) target.Ship = null;
                if (ReferenceEquals(target.Situ, state.Situation)) target.Situ = situation;
            }
            if (ReferenceEquals(observer.shipStationKeepingTarget, state.Previous)) observer.shipStationKeepingTarget = null;
            if (ReferenceEquals(observer.shipCombatTarget, state.Previous)) observer.shipCombatTarget = null;
            if (ReferenceEquals(observer.shipUndock, state.Previous)) observer.shipUndock = null;
            JsonShip json = observer.json;
            if (json == null) continue;
            // Keep the logical stellar ID and independently owned waypoints.
            // Only references to the retired Ship representation are obsolete.
            if (shared && json.strTargetRegID == state.Id) json.objSituScanTarget = situation.GetJSON();
            if (json.strScanTargetID == state.Id) json.strScanTargetID = null;
            if (json.strStationKeepingTargetID == state.Id) json.strStationKeepingTargetID = null;
            if (json.strCombatTargetID == state.Id) json.strCombatTargetID = null;
            if (json.strUndockID == state.Id) json.strUndockID = null;
        }
    }

    internal static void Removed(ShipReplacementTargetsPatch.Replacement state) {
        foreach (Ship observer in state.System.dictShips.Values) {
            if (observer == null || observer.bDestroyed) continue;
            TargetData target = observer.TargetData;
            bool scan = ReferenceEquals(observer.shipScanTarget, state.Previous);
            bool goal = observer.targetRegID == state.Id || ReferenceEquals(observer.shipSituTarget, state.Situation) ||
                (observer.shipSituTarget != null && target != null && ReferenceEquals(target.Ship, state.Previous) && ReferenceEquals(target.Situ, observer.shipSituTarget));
            if (goal) Invalidate(observer);
            else {
                if (scan) observer.shipScanTarget = null;
                if (target != null && (ReferenceEquals(target.Ship, state.Previous) || ReferenceEquals(target.Situ, state.Situation))) observer.TargetData = null;
            }
            if (ReferenceEquals(observer.shipStationKeepingTarget, state.Previous)) observer.shipStationKeepingTarget = null;
            if (ReferenceEquals(observer.shipCombatTarget, state.Previous)) observer.shipCombatTarget = null;
            if (ReferenceEquals(observer.shipUndock, state.Previous)) observer.shipUndock = null;
            JsonShip json = observer.json;
            if (json == null) continue;
            if (json.strTargetRegID == state.Id) {
                json.strTargetRegID = null;
                json.objSituScanTarget = null;
            }
            if (json.strScanTargetID == state.Id) json.strScanTargetID = null;
            if (json.strStationKeepingTargetID == state.Id) json.strStationKeepingTargetID = null;
            if (json.strCombatTargetID == state.Id) json.strCombatTargetID = null;
            if (json.strUndockID == state.Id) json.strUndockID = null;
        }
    }

    internal static void Restored(Ship ship) {
        StarSystem world = CrewSim.system;
        if (ship == null || ship.bDestroyed || !IsCurrentWorld(world) || ShipReplacementTargetsPatch.Replacing != null) return;
        string id = ship.targetRegID;
        if (string.IsNullOrEmpty(id) || ship.shipScanTarget != null || world.GetShipByRegID(id) != null) return;
        // Only a saved/retained Ship association proves this was a ship goal.
        // Unregistered stellar objects and custom coordinate identifiers can be
        // resolved lazily; absence from dictShips alone is not proof of removal.
        if (world.dictStellarObjects != null && world.dictStellarObjects.TryGetValue(id, out var stellar) && stellar != null) return;
        bool wasShip = ship.json?.strScanTargetID == id || ship.TargetData?.Ship?.strRegID == id;
        if (!wasShip) return;
        Invalidate(ship);
    }
}

// Native unmooring temporarily destroys an asteroid Ship before recreating its
// stellar representation. Resolve its lifetime after that synchronous operation,
// with the same exception-safe, nested scope as SaveToShallow replacement.
[HarmonyPatch(typeof(Undock), "AttemptToUnMoor")]
internal static class ShipStellarConversionTargetsPatch {
    [HarmonyPrefix]
    internal static void Prefix(Ship ship, out ShipReplacementTargetsPatch.Replacement __state) {
        __state = default;
        if (ship == null || ship.bDestroyed || ship.Classification != Ship.TypeClassification.Asteroid ||
            !ShipTargetLifetime.IsCurrentWorld(CrewSim.system) || !ReferenceEquals(CrewSim.system.GetShipByRegID(ship.strRegID), ship)) return;
        ShipReplacementTargetsPatch.Prefix(ship, out __state);
    }
    [HarmonyFinalizer]
    internal static void Finalizer(ShipReplacementTargetsPatch.Replacement __state) {
        if (!__state.Captured) return;
        try { ShipReplacementTargetsPatch.Complete(__state, stellarConversion: true); }
        finally { ShipReplacementTargetsPatch.Replacing = __state.PreviousScope; }
    }
}

[HarmonyPatch(typeof(Ship), nameof(Ship.Destroy))]
internal static class ShipDestroyedTargetsPatch {
    [HarmonyPrefix]
    internal static void Prefix(Ship __instance, out ShipReplacementTargetsPatch.Replacement __state) {
        __state = default;
        // Projectile teardown is a hot combat path. Its native targeting remains
        // authoritative; this repair concerns vessel navigation lifetimes.
        if (__instance.bDestroyed || __instance.Classification == Ship.TypeClassification.Projectile ||
            ReferenceEquals(ShipReplacementTargetsPatch.Replacing, __instance) || !ShipTargetLifetime.IsCurrentWorld(CrewSim.system)) return;
        if (!ReferenceEquals(CrewSim.system.GetShipByRegID(__instance.strRegID), __instance)) return;
        __state = new ShipReplacementTargetsPatch.Replacement(__instance);
    }
    [HarmonyFinalizer]
    internal static void Finalizer(ShipReplacementTargetsPatch.Replacement __state) => ShipReplacementTargetsPatch.Complete(__state);
}

[HarmonyPatch(typeof(Ship), nameof(Ship.CheckTargets))]
internal static class ShipRestoredTargetsPatch {
    [HarmonyPostfix]
    internal static void Postfix(Ship __instance) {
        try { ShipTargetLifetime.Restored(__instance); }
        catch (Exception ex) { Ostrabus.OstrabusPlugin.Logger?.LogWarning($"[Ship targets] Could not validate restored destination for {__instance.strRegID}: {ex.Message}"); }
    }
}
