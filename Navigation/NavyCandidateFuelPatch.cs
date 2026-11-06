using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Ostranauts.Ships.AIPilots;
using Ostrabus.CeresNavigation;

namespace Ostrabus.ShipNavigation;

// Filter before native single-candidate/random/fallback selection. Keep native
// candidate generation, distance ordering and random preference intact.
[HarmonyPatch(typeof(NavyPilot), nameof(NavyPilot.GetTarget))]
internal static class NavyCandidateFuelPatch {
    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var add = AccessTools.Method(typeof(List<Tuple<double, ShipSitu>>), "AddRange");
        var replacement = AccessTools.Method(typeof(NavyCandidateFuelPatch), nameof(AddEligible));
        int count = 0;
        foreach (var instruction in instructions) {
            if (instruction.Calls(add)) {
                var load = new CodeInstruction(OpCodes.Ldarg_0);
                instruction.MoveLabelsTo(load);
                load.blocks.AddRange(instruction.blocks); instruction.blocks.Clear();
                yield return load;
                yield return new CodeInstruction(OpCodes.Call, replacement);
                count++;
            } else yield return instruction;
        }
        if (count != 1) throw new InvalidOperationException("Expected one native Navy candidate merge; found " + count);
    }
    internal static void AddEligible(List<Tuple<double, ShipSitu>> list, IEnumerable<Tuple<double, ShipSitu>> extra, NavyPilot pilot) {
        Ship ship = pilot.ShipUs;
        double speed = pilot.MaxSpeed(null);
        bool ceres = CeresNavigationGuardrails.IsCeresFactionShip(ship);
        Ship home = ceres ? CeresNavigationGuardrails.FindHome(ship) : null;
        bool Eligible(ShipSitu target) => target != null && !AIShipManager.BingoFuelCheck(ship, target, speed) &&
            (!ceres || home != null && !CeresNavigationGuardrails.LacksRoundTripReserve(ship, target, home, speed));
        for (int i = list.Count - 1; i >= 0; i--) if (!Eligible(list[i].Item2)) list.RemoveAt(i);
        foreach (var candidate in extra) if (Eligible(candidate.Item2)) list.Add(candidate);
    }
}

