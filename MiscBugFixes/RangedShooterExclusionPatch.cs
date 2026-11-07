using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using Ostranauts.Ships;

namespace Ostrabus.MiscBugFixes;

// Native DamageRay excludes the attacker from destructible hits but omits the
// same check before character wounds. A skipped wound must not spend ray damage.
[HarmonyPatch(typeof(DamageSystem), nameof(DamageSystem.DamageRay))]
internal static class RangedShooterExclusionPatch {
    internal static Wound WoundForHit(CondOwner hit, bool blunt, bool cut, string trigger, CondOwner attacker) {
        if (attacker != null && (hit == attacker || hit.RootParent() == attacker)) return null;
        return hit.GetWoundLocation(blunt, cut, trigger);
    }
    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var code = instructions.ToList();
        var original = AccessTools.Method(typeof(CondOwner), nameof(CondOwner.GetWoundLocation), new[] { typeof(bool), typeof(bool), typeof(string) });
        int index = -1, matches = 0;
        for (int i = 0; i < code.Count; i++) {
            if (!code[i].Calls(original)) continue;
            index = i; matches++;
        }
        if (matches != 1 || code[index].blocks.Count != 0) {
            OstrabusPlugin.Logger?.LogWarning("[MiscBugFixes] Ranged wound selection changed; shooter exclusion was not installed.");
            return code;
        }
        // Stack already contains hit, blunt, cut, trigger. DamageRay's sixth
        // instance argument is coAttacker; the replacement adds only that check.
        code[index].opcode = OpCodes.Ldarg_S;
        code[index].operand = (byte)6;
        code.Insert(index + 1, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(RangedShooterExclusionPatch), nameof(WoundForHit))));
        return code;
    }
}
