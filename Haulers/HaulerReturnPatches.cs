using System;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;

namespace Ostrabus.Haulers;

[HarmonyPatch(typeof(HaulShip), nameof(HaulShip.RunCommand))]
internal static class HaulShipReturnPatch {
    [HarmonyPrefix]
    internal static void Prefix(HaulShip __instance, out bool __state) {
        __state = false;
        try {
            Ship ship = __instance?.ShipUs;
            __state = HaulerRecovery.Controller(ship) != null && HaulerRecovery.EmptyAndUncontrolled(ship);
        } catch (Exception ex) { HaulerRecovery.Fault(ex); }
    }
    [HarmonyPostfix]
    internal static void Postfix(HaulShip __instance, CommandStatus __result, bool __state) {
        if (!__state || __result == null || __result.Status != CommandCode.Failure) return;
        try {
            Ship ship = __instance?.ShipUs;
            AIShip ai = HaulerRecovery.Controller(ship);
            if (ai == null || !HaulerRecovery.EmptyAndUncontrolled(ship) || !HaulerRecovery.Targetless(ship)) return;
            HaulerRecovery.StopAbandonedManeuver(ship, "empty haul completed");
            HaulerRecovery.RestoreHomeTarget(ship, ai, "empty-hauler recovery");
        } catch (Exception ex) { HaulerRecovery.Fault(ex); }
    }
}

[HarmonyPatch(typeof(AnchorDockedShip), nameof(AnchorDockedShip.RunCommand))]
internal static class AnchorDockedShipReturnPatch {
    [HarmonyPrefix]
    internal static void Prefix(AnchorDockedShip __instance) {
        try {
            Ship ship = __instance?.ShipUs;
            if (HaulerRecovery.Controller(ship) == null) return;
            foreach (var docked in ship.GetAllDockedShips()) if (docked != null && docked.IsDerelict()) { HaulerRecovery.MarkRelease(ship); break; }
        } catch (Exception ex) { HaulerRecovery.Fault(ex); }
    }
    [HarmonyPostfix]
    internal static void Postfix(AnchorDockedShip __instance, CommandStatus __result) {
        Ship ship = __instance?.ShipUs;
        if (!HaulerRecovery.PendingRelease(ship) || __result == null || __result.Status == CommandCode.Running) return;
        HaulerRecovery.EndRelease(ship);
        if (__result.Status != CommandCode.Finished) return;
        try {
            var ai = HaulerRecovery.Controller(ship);
            if (ai == null || !HaulerRecovery.EmptyAndUncontrolled(ship) || !HaulerRecovery.Targetless(ship)) return;
            HaulerRecovery.StopAbandonedManeuver(ship, "derelict released");
            HaulerRecovery.RestoreHomeTarget(ship, ai, "derelict dropoff");
        } catch (Exception ex) { HaulerRecovery.Fault(ex); }
    }
    [HarmonyFinalizer]
    internal static Exception Finalizer(AnchorDockedShip __instance, Exception __exception) {
        if (__exception != null) HaulerRecovery.EndRelease(__instance?.ShipUs);
        return __exception;
    }
}

[HarmonyPatch(typeof(AIShip), nameof(AIShip.RunAI))]
internal static class HaulerIdleManeuverPatch {
    [HarmonyPostfix]
    internal static void Postfix(AIShip __instance) {
        try { HaulerRecovery.AfterIdleAI(__instance); }
        catch (Exception ex) { HaulerRecovery.Fault(ex); }
    }
}
