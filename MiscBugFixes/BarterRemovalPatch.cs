using HarmonyLib;
using Ostranauts.Ships;

namespace Ostrabus.MiscBugFixes;

[HarmonyPatch(typeof(Ship), nameof(Ship.RemoveCO), new[] { typeof(CondOwner), typeof(bool) })]
internal static class BarterRemovalPatch {
    internal static bool Prefix(Ship __instance, CondOwner objCO, bool bForce) {
        if (__instance is not BarterZoneShip barter) return true;
        // RemoveCO is hidden, not overridden: callers typed as Ship otherwise
        // bypass removal from the barter copy's separate inventory JSON.
        barter.RemoveCO(objCO, bForce);
        return false;
    }
}
