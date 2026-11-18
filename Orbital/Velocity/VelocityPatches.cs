using HarmonyLib;

namespace Ostrabus.Orbital;

[HarmonyPatch(typeof(BodyOrbit), nameof(BodyOrbit.UpdateTime))]
internal static class OrbitalVelocityPatch
{
    [HarmonyPrefix, HarmonyPriority(Priority.Last)]
    internal static bool Prefix(BodyOrbit __instance, double dTime, bool bCorrectTimes, bool bCalcV, bool __runOriginal)
    {
        if (!__runOriginal)
            return false;
        return !OrbitVelocity.TryUpdate(__instance, dTime, bCorrectTimes, bCalcV);
    }
}

[HarmonyPatch(typeof(BodyOrbit), nameof(BodyOrbit.CreateBOFromShip))]
internal static class CoastOrbitFitPatch
{
    [HarmonyPrefix, HarmonyPriority(Priority.Last)]
    internal static bool Prefix(Ship ship, BodyOrbit boGreatestGrav, ref BodyOrbit __result, bool __runOriginal)
    {
        if (!__runOriginal)
            return false;
        // Unsupported native representations (including negative-period moons)
        // retain their existing factory and ephemeris behavior.
        if (!OrbitVelocity.Supports(boGreatestGrav))
            return true;
        __result = CoastOrbitFit.Fit(ship, boGreatestGrav);
        return false;
    }
}
