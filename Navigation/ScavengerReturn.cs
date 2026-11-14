using System;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.ShipGUIs.Utilities;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;

namespace Ostrabus.ShipNavigation;

// Intentional fallback policy after the ordinary random job search is exhausted.
// Destination, route, fuel validation, flight and docking remain native.
internal static class ScavengerReturn {
    static readonly AccessTools.FieldRef<object, TargetData> Target = AccessTools.FieldRefAccess<object, TargetData>(
        AccessTools.Field(AccessTools.Inner(typeof(TargetNewIfNone), "TargetDataDTO"), "TargetData"));
    static readonly AccessTools.FieldRef<object, NavData> Plan = AccessTools.FieldRefAccess<object, NavData>(
        AccessTools.Field(AccessTools.Inner(typeof(TargetNewIfNone), "TargetDataDTO"), "NavData"));
    static readonly AccessTools.FieldRef<object, double> Fuel = AccessTools.FieldRefAccess<object, double>(
        AccessTools.Field(AccessTools.Inner(typeof(TargetNewIfNone), "TargetDataDTO"), "FuelCostRCS"));
    static readonly Func<TargetNewIfNone, TargetData, NavData> Generate = (Func<TargetNewIfNone, TargetData, NavData>)
        AccessTools.Method(typeof(TargetNewIfNone), "GenerateNavData").CreateDelegate(typeof(Func<TargetNewIfNone, TargetData, NavData>));

    internal static void Fallback(TargetNewIfNone command, object dto) {
        if (dto == null || Target(dto) != null) return;
        var pilot = MiscBugFixes.ScavEmergencyRecovery.GetPilot(command);
        if (pilot.AIType != AIType.Scav || command.ShipUs.shipCombatTarget != null ||
            pilot.Blackboard.HasKey("InCombat") && pilot.Blackboard.Get<bool>("InCombat")) return;
        TargetData home = pilot.GetTargetHome();
        Ship station = home?.Ship;
        if (station == null || station.bDestroyed || station.HideFromSystem || !station.IsStation() ||
            !ReferenceEquals(CrewSim.system.GetShipByRegID(station.strRegID), station) || !home.TargetIsBoLocked) return;
        NavData plan = Generate(command, home);
        if (plan?.Origin == null || plan.Destination == null) return;
        double fuel = plan.Origin.FuelLevel - plan.Destination.FuelLevel;
        if (double.IsNaN(fuel) || double.IsInfinity(fuel) || fuel < 0) return;
        Target(dto) = home;
        Plan(dto) = plan;
        Fuel(dto) = fuel;
    }
}

[HarmonyPatch(typeof(TargetNewIfNone), "FindTarget")]
internal static class ScavengerReturnFallbackPatch {
    static void Postfix(TargetNewIfNone __instance, object __result) => ScavengerReturn.Fallback(__instance, __result);
}
