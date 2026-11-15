using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Ostranauts.Ships.AIPilots;

namespace Ostrabus.MiscBugFixes;

internal static class AiCrewIdentity {
    // Called only by AddCrew's fresh-profile fallback, after native recruitment
    // has failed. Do not use bNew=true: its bounded live-name loop can cache an
    // occupied actor and does not check saved identities.
    internal static PersonSpec Create(JsonPersonSpec definition, bool _) {
        var person = new PersonSpec(definition, false);
        while (Occupied(person.FullName))
            DataHandler.GetFullName(person.strGender, out person.strFirstName, out person.strLastName);
        return person;
    }

    static bool Occupied(string id) => !string.IsNullOrEmpty(id) &&
        (DataHandler.mapCOs?.ContainsKey(id) == true || DataHandler.dictCOSaves?.ContainsKey(id) == true);
}

[HarmonyPatch(typeof(AIShip), nameof(AIShip.AddCrew), new[] {
    typeof(string), typeof(Ship), typeof(string), typeof(bool)
})]
internal static class AiCrewIdentityPatch {
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var constructor = AccessTools.Constructor(typeof(PersonSpec), new[] { typeof(JsonPersonSpec), typeof(bool) });
        var code = new List<CodeInstruction>(instructions);
        int index = -1, count = 0;
        for (int i = 0; i < code.Count; i++) {
            if (code[i].opcode != OpCodes.Newobj || !Equals(code[i].operand, constructor)) continue;
            index = i; count++;
        }
        if (count != 1 || index < 1 || code[index - 1].opcode != OpCodes.Ldc_I4_0)
            throw new InvalidOperationException("AI crew creation changed; expected one fresh PersonSpec(false) fallback.");
        // Same two arguments and result; recruitment, native factory lookup,
        // character generation and boarding retain their original bodies.
        code[index].opcode = OpCodes.Call;
        code[index].operand = AccessTools.Method(typeof(AiCrewIdentity), nameof(AiCrewIdentity.Create));
        return code;
    }
}
