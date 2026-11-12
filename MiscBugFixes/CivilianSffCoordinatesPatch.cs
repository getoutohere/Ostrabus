using System;
using System.Collections.Generic;
using HarmonyLib;
using Ostranauts.Ships.AIPilots;

namespace Ostrabus.MiscBugFixes;

// Preserve both native FFWD branches and their docking/hold decisions. Only the
// second coordinate supplied to regional station selection changes from X to Y.
[HarmonyPatch(typeof(CivilianPilot), nameof(CivilianPilot.FFWD))]
internal static class CivilianSffCoordinatesPatch {
    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var code = new List<CodeInstruction>(instructions);
        var nearest = AccessTools.Method(typeof(StarSystem), nameof(StarSystem.GetNearestStationRegional), new[] { typeof(double), typeof(double) });
        var x = AccessTools.Field(typeof(ShipSitu), nameof(ShipSitu.vPosx));
        var y = AccessTools.Field(typeof(ShipSitu), nameof(ShipSitu.vPosy));
        int matches = 0;
        for (int i = 1; i < code.Count; i++) {
            if (!code[i].Calls(nearest)) continue;
            if (!code[i - 1].LoadsField(x)) throw new InvalidOperationException("Unknown civilian FFWD regional lookup; expected the native second X coordinate.");
            code[i - 1].operand = y;
            matches++;
        }
        if (matches != 2) throw new InvalidOperationException("Expected two civilian FFWD regional lookups; found " + matches);
        return code;
    }
}
