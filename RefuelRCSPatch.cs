using System;
using HarmonyLib;

namespace Ostrabus;

[HarmonyPatch(typeof(Ship), nameof(Ship.RefuelRCS), new Type[] { typeof(float) })]
internal static class RefuelRCSPatch
{
    [HarmonyPrefix]
    internal static bool Prefix(Ship __instance, float amount, ref float __result)
    {
        if (__instance.LoadState > Ship.Loaded.Shallow) return true;

        // The original shallow branch changes ship mass and misreports overflow.
        // RefuelRCS returns the fuel NOT accepted, as expected by TakeFuel.
        __result = amount;
        if (amount <= 0 || float.IsNaN(amount) || float.IsInfinity(amount)) return false;

        double fuel = __instance.fShallowRCSRemass;
        double capacity = __instance.GetRCSMax();
        if (fuel < 0 || double.IsNaN(fuel) || double.IsInfinity(fuel) ||
            capacity <= 0 || double.IsNaN(capacity) || double.IsInfinity(capacity)) return false;

        double accepted = Math.Min((double)amount, Math.Max(0, capacity - fuel));
        if (accepted > 0) {
            __instance.fShallowRCSRemass = Math.Min(capacity, fuel + accepted);
        }
        __result = (float)(amount - accepted);
        return false;
    }
}
