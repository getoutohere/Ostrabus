using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.Ships.Commands;

namespace Ostrabus.ShipNavigation;

// Missing/destroyed projectile returns Success before native Dodge's normal
// completion cleanup. Do not change the timed maneuver or its failure path.
[HarmonyPatch(typeof(Dodge), nameof(Dodge.RunCommand))]
internal static class DodgeCompletionPatch {
    static void Postfix(Dodge __instance, Blackboard blackboard, CommandStatus __result) {
        if (__result.Status != CommandCode.Finished) return;
        __instance.ShipUs.objSS.bIgnoreGrav = false;
        __instance.ShipUs.fAIDockingExpire = double.NegativeInfinity;
        blackboard?.Remove("DodgeTarget");
        blackboard?.Remove("DodgeDir");
    }
}
