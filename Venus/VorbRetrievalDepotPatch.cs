using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Ostranauts.Ships.AIPilots;

namespace Ostrabus.Venus;

// VORB shares VNCA's traffic region, but not its wreck ownership or dispatch desk.
// Change only the empty retriever's depot lookup; native message precedence,
// wreck filtering/randomness and the already-towing return branch stay intact.
[HarmonyPatch(typeof(HaulerRetrievingPilot), nameof(HaulerRetrievingPilot.GetTarget))]
internal static class VorbRetrievalDepotPatch {
    static readonly AccessTools.FieldRef<AIShip, Ship> RawShip = AccessTools.FieldRefAccess<AIShip, Ship>("_shipUs");
    static Ship Depot(StarSystem system, double x, double y, HaulerRetrievingPilot pilot) {
        if (system?.dictShips != null && pilot?.ShipUs != null && AIShipManager.dictAIs != null) {
            foreach (var region in AIShipManager.dictAIs.Values) {
                if (region == null) continue;
                foreach (var ai in region) {
                    if (ai == null || !ReferenceEquals(ai.AICharacter, pilot) || !ReferenceEquals(RawShip(ai), pilot.ShipUs)) continue;
                    if (ai.HomeStation == "VORB" && system.dictShips.TryGetValue("VORB", out var home) &&
                        home != null && !home.bDestroyed && !home.bRemove && !home.HideFromSystem && home.objSS?.bIsBO == true) return home;
                    return system.GetNearestStationRegional(x, y);
                }
            }
        }
        return system.GetNearestStationRegional(x, y);
    }
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var nearest = AccessTools.Method(typeof(StarSystem), nameof(StarSystem.GetNearestStationRegional), new[] { typeof(double), typeof(double) });
        var depot = AccessTools.Method(typeof(VorbRetrievalDepotPatch), nameof(Depot));
        int matches = 0;
        foreach (var instruction in instructions) {
            if (instruction.Calls(nearest)) {
                matches++;
                var load = new CodeInstruction(OpCodes.Ldarg_0);
                instruction.MoveLabelsTo(load); instruction.MoveBlocksTo(load);
                yield return load;
                yield return new CodeInstruction(OpCodes.Call, depot);
            } else yield return instruction;
        }
        if (matches != 1) throw new InvalidOperationException("Expected one regional depot lookup in HaulerRetrievingPilot.GetTarget; refusing to patch an unknown version.");
    }
}
