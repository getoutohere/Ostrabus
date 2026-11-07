using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using Ostranauts.Ships;

namespace Ostrabus.MiscBugFixes;

// Fix the per-object destruction rule as it is constructed, including on reload.
// Installed meat retains its meat-producing recipe; loose meat follows the same
// native default destruction path as the other loose meat variants.
[HarmonyPatch(typeof(DestCheck), nameof(DestCheck.SetData))]
internal static class LooseMeatDestructionPatch {
    [HarmonyPostfix]
    internal static void Postfix(DestCheck __instance, CondOwner co, bool __result) {
        if (__result && co != null && co.strName == "ItmMeat01Loose" && __instance.strDamageCond == "StatDamage" &&
            __instance.strDamageCondMax == "StatDamageMax" && __instance.strLootModeSwitch == "ACTMeat01Destroy")
            __instance.strLootModeSwitch = "ACTDefaultDestroy";
    }
}

// Correct only the projectile-cover interpretation, not the collider used for
// selecting/picking up an item or its durability in other damage systems.
[HarmonyPatch(typeof(DamageSystem), nameof(DamageSystem.DamageRay))]
internal static class LooseMeatCoverPatch {
    internal static bool IsFloorScrap(Destructable hit) {
        var co = hit.CO;
        return co != null && (co.strName == "ItmMeat01Loose" || co.strName == "ItmMeat01EyeLoose" ||
            co.strName == "ItmMeat01HoofLoose" || co.strName == "ItmMeat01HornLoose") && co.objCOParent == null && co.slotNow == null;
    }
    internal static double RemainingForRay(Destructable hit, string stat) {
        if (stat == "StatDamage" && IsFloorScrap(hit)) return 0;
        return hit.DmgLeft(stat);
    }
    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var code = instructions.ToList();
        var original = AccessTools.Method(typeof(Destructable), nameof(Destructable.DmgLeft));
        int index = -1, matches = 0;
        for (int i = 0; i < code.Count; i++) {
            if (!code[i].Calls(original)) continue;
            index = i; matches++;
        }
        if (matches != 1) {
            OstrabusPlugin.Logger?.LogWarning("[MiscBugFixes] Ranged object damage changed; loose meat cover exclusion was not installed.");
            return code;
        }
        // Native zero-remaining handling continues to the next collider without
        // spending any of the ray's environment, blunt or cut damage budgets.
        code[index].opcode = OpCodes.Call;
        code[index].operand = AccessTools.Method(typeof(LooseMeatCoverPatch), nameof(RemainingForRay));
        return code;
    }
}
