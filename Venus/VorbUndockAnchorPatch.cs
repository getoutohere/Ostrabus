using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace Ostrabus.Venus;

[HarmonyPatch(typeof(Ship), nameof(Ship.Undock), new[] { typeof(Ship) })]
internal static class VorbUndockAnchorPatch {
    internal static BodyOrbit Reference(StarSystem system, ShipSitu situation, double epoch, bool includePlaceholders, Ship ship) {
        BodyOrbit original = system.GetNearestBO(situation, epoch, includePlaceholders);
        if (!VenusDerelicts.Eligible(ship)) return original;
        BodyOrbit candidate = system.GetNearestBO(situation, epoch, true);
        return VenusDerelicts.IsVorbOrbit(candidate) ? candidate : original;
    }
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var original = instructions.ToList();
        MethodInfo nearest = AccessTools.Method(typeof(StarSystem), nameof(StarSystem.GetNearestBO), new[] { typeof(ShipSitu), typeof(double), typeof(bool) });
        int count = original.Count(instruction => instruction.Calls(nearest));
        if (count != 1) {
            Ostrabus.OstrabusPlugin.Logger?.LogWarning("[Venus] Undock orbit lookup changed; anchoring correction was not installed.");
            return original;
        }
        var result = new List<CodeInstruction>(original.Count + 1);
        foreach (CodeInstruction instruction in original) {
            if (instruction.Calls(nearest)) {
                // Keep the original instruction (and its labels/blocks) as the first
                // instruction in the replacement, so branches cannot skip ldarg.0.
                instruction.opcode = OpCodes.Ldarg_0;
                instruction.operand = null;
                result.Add(instruction);
                result.Add(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(VorbUndockAnchorPatch), nameof(Reference))));
                continue;
            }
            result.Add(instruction);
        }
        return result;
    }
    static void Prefix(Ship __instance, out bool __state) { __state = VenusDerelicts.Eligible(__instance) && !__instance.objSS.bBOLocked; }
    static void Postfix(Ship __instance, bool __state) {
        if (__state && __instance.objSS.bBOLocked && (__instance.objSS.strBOPORShip == "VORB" || (__instance.objSS.strBOPORShip?.StartsWith("VORB|", StringComparison.Ordinal) ?? false))) {
            Ostrabus.OstrabusPlugin.Logger?.LogInfo("[Venus] Restored derelict " + __instance.strRegID + " to " + __instance.objSS.strBOPORShip + " after undocking.");
        }
    }
}
