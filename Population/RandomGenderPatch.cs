using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;

namespace Ostrabus.Population;

// Intentional population policy: 1% NB, with the remainder split equally.
// Change constants at patch installation; keep native RNG and explicit genders.
[HarmonyPatch(typeof(PersonSpec), "GetRandomGender")]
internal static class RandomGenderPatch {
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var code = instructions.ToList();
        var nb = code.Where(c => c.opcode == OpCodes.Ldc_R4 && Equals(c.operand, 10f)).ToArray();
        var male = code.Where(c => c.opcode == OpCodes.Ldc_R4 && Equals(c.operand, 54f)).ToArray();
        if (nb.Length != 1 || male.Length != 1)
            throw new InvalidOperationException("Native random gender thresholds changed; expected 10 and 54.");
        nb[0].operand = 1f;
        male[0].operand = 50.5f;
        return code;
    }
}
