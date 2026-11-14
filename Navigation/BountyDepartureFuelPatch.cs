using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Systems;

namespace Ostrabus.ShipNavigation;

// Bounty reactivation supplies an existing hull to its pilot constructor, so it
// bypasses CreateAIShip and its station servicing. Restore that one missing step.
[HarmonyPatch(typeof(BountyManager), "SpawnBountyTarget")]
internal static class BountyDepartureFuelPatch {
    internal static void Embark(CondOwner crew, Ship ship, bool spawned, string stationId) {
        Ship source = crew.ship;
        bool service = source != null && !ReferenceEquals(source, ship) && source.IsStation() &&
            ship != null && !ship.bDestroyed && ship.LoadState == Ship.Loaded.Shallow &&
            ship.objSS != null && !ship.objSS.bIsBO;
        AIShip.AddCrew(crew, ship, spawned, stationId);
        if (service && ReferenceEquals(crew.ship, ship)) ship.AIRefuel();
    }
    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var embark = AccessTools.Method(typeof(AIShip), nameof(AIShip.AddCrew), new[] { typeof(CondOwner), typeof(Ship), typeof(bool), typeof(string) });
        var replacement = AccessTools.Method(typeof(BountyDepartureFuelPatch), nameof(Embark));
        var code = new List<CodeInstruction>(instructions);
        int count = 0;
        foreach (var instruction in code) {
            if (!instruction.Calls(embark)) continue;
            instruction.opcode = OpCodes.Call;
            instruction.operand = replacement;
            count++;
        }
        // One station-person relocation and one flight-hull embarkation. The
        // helper retains native transfer behavior and excludes station targets.
        if (count != 2) throw new InvalidOperationException("Bounty departure: expected two native AddCrew calls, found " + count);
        return code;
    }
}
