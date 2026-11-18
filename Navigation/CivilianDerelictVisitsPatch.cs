using HarmonyLib;
using Ostranauts.Ships.AIPilots;

namespace Ostrabus.ShipNavigation;

// Simulation policy: random civilian visits are not salvage excursions.
// This helper is private to CivilianPilot's random ship-candidate branch;
// scavengers, haulers and explicit navigation keep their own selectors.
[HarmonyPatch(typeof(CivilianPilot), "CanDockWith")]
internal static class CivilianDerelictVisitsPatch {
    static bool Prefix(Ship shipTarget, ref bool __result) {
        // Asteroid prospecting is a separate native activity. An asteroid's
        // damage category must not turn it into a salvage target here.
        if (shipTarget == null || shipTarget.Classification == Ship.TypeClassification.Asteroid ||
            shipTarget.DMGStatus != Ship.Damage.Derelict) return true;
        __result = false;
        return false;
    }
}
