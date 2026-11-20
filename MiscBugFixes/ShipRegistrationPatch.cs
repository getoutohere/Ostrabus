using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace Ostrabus.MiscBugFixes;

internal static class ShipRegistration {
    internal static bool Occupied(Dictionary<string, Ship> loadedShips, string id) {
        bool loaded = loadedShips.ContainsKey(id);
        bool deferred = !loaded && CrewSim.system?.dictStellarObjects?.ContainsKey(id) == true;
        if (loaded || deferred)
            Debug.LogWarning("[Ostrabus] Prevented duplicate ship registration " + id +
                "; already used by " + (loaded ? "a loaded ship" : "a deferred space object") + ". Drawing another ID.");
        return loaded || deferred;
    }
}

[HarmonyPatch(typeof(Ship), nameof(Ship.GenerateID))]
internal static class ShipRegistrationPatch {
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var code = new List<CodeInstruction>(instructions);
        var contains = AccessTools.Method(typeof(Dictionary<string, Ship>), nameof(Dictionary<string, Ship>.ContainsKey));
        var generate = AccessTools.Method(typeof(Ship), nameof(Ship.GenerateID));
        int check = -1, retry = -1, checks = 0, retries = 0;
        for (int i = 0; i < code.Count; i++) {
            if (code[i].Calls(contains)) { check = i; checks++; }
            if (code[i].Calls(generate)) { retry = i; retries++; }
        }
        if (checks != 1 || retries != 1 || retry < 1 || code[retry - 1].opcode != OpCodes.Ldnull)
            throw new InvalidOperationException("Ship registration allocation changed; expected one registry check and one default-prefix retry.");
        code[check] = new CodeInstruction(code[check]) {
            opcode = OpCodes.Call, operand = AccessTools.Method(typeof(ShipRegistration), nameof(ShipRegistration.Occupied))
        };
        // Native retries used the default colony, losing an explicit station or
        // asteroid-field prefix. Keep the caller's prefix on every fresh draw.
        code[retry - 1] = new CodeInstruction(code[retry - 1]) { opcode = OpCodes.Ldarg_0, operand = null };
        return code;
    }
}
