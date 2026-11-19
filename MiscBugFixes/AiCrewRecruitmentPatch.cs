using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Ostranauts.Ships.AIPilots;

namespace Ostrabus.MiscBugFixes;

internal static class AiCrewRecruitment {
    internal static List<string> Sources(string region) {
        var sources = AIShipManager.GetAllATCRecycleStations(region);
        // AddCrew adds OKLG_UNK and filters transient underpopulation. Those
        // edits belong to this search, not the shared regional source registry.
        return sources == null ? null : new List<string>(sources);
    }
}

[HarmonyPatch(typeof(AIShip), nameof(AIShip.AddCrew), new[] {
    typeof(string), typeof(Ship), typeof(string), typeof(bool)
})]
internal static class AiCrewRecruitmentPatch {
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var code = new List<CodeInstruction>(instructions);
        var sources = AccessTools.Method(typeof(AIShipManager), nameof(AIShipManager.GetAllATCRecycleStations));
        int count = 0;
        foreach (var instruction in code) if (instruction.Calls(sources)) {
            instruction.operand = AccessTools.Method(typeof(AiCrewRecruitment), nameof(AiCrewRecruitment.Sources));
            count++;
        }
        if (count != 1) throw new InvalidOperationException("AI crew recruitment: expected one regional source lookup.");
        return code;
    }
}
