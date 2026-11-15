using HarmonyLib;
using Ostranauts.Ships.Commands;

namespace Ostrabus.ShipNavigation;

[HarmonyPatch(typeof(MiscBugFixes.CivilianVisitWaiting), nameof(MiscBugFixes.CivilianVisitWaiting.Duration))]
internal static class CivilianAsteroidVisitPatch {
    static bool Prefix(Hold hold, ref double __result) {
        if (!MiscBugFixes.CivilianVisitWaiting.AsteroidVisit(hold)) return true;
        __result = MathUtils.Rand(300, 7200, MathUtils.RandType.Flat);
        return false;
    }
}
