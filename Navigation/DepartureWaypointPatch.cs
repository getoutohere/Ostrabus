using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Ostranauts.Ships.Commands;
using Ostranauts.ShipGUIs.Utilities;

namespace Ostrabus.ShipNavigation;

// Only the short AI no-wake departure branch. Planetary gravity, obstacle
// waypoints, player plotting and the interplanetary destination stay native.
[HarmonyPatch(typeof(FlyToPath), nameof(FlyToPath.PlanTrip4))]
internal static class DepartureWaypointPatch {
    internal static void Bind(ShipSitu point, double epoch, bool includePlaceholder, FlyToPath planner, NavDataPoint origin) {
        var world = CrewSim.system;
        if (planner?.ShipUs?.IsAIShip == true && origin?.ObjSS != null && world?.aBOs != null) {
            // Match CreateBorderSituBetweenPoints: identify the departure depot
            // using the original origin, never the final cargo destination.
            var depot = world.GetNearestStation(origin.ObjSS.vPosx, origin.ObjSS.vPosy, excludeOutposts:true);
            var situation = depot?.objSS;
            if (depot != null && !depot.bDestroyed && !depot.bRemove && situation != null &&
                (situation.bIsBO || situation.bBOLocked || situation.bOrbitLocked) &&
                situation.strBOPORShip != null && world.aBOs.TryGetValue(situation.strBOPORShip, out var reference) && reference != null) {
                point.LockToBO(reference, epoch);
                return;
            }
        }
        point.LockToBO(epoch, includePlaceholder);
    }
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var bind = AccessTools.Method(typeof(ShipSitu), nameof(ShipSitu.LockToBO), new[]{typeof(double),typeof(bool)});
        var replace = AccessTools.Method(typeof(DepartureWaypointPatch), nameof(Bind));
        int matches = 0;
        foreach (var instruction in instructions) {
            if (instruction.Calls(bind)) {
                matches++;
                var instance = new CodeInstruction(instruction) { opcode = OpCodes.Ldarg_0, operand = null };
                yield return instance;
                yield return new CodeInstruction(OpCodes.Ldarg_1);
                yield return new CodeInstruction(OpCodes.Call, replace);
                continue;
            }
            yield return instruction;
        }
        if (matches!=1) throw new InvalidOperationException("Expected one AI departure binding in PlanTrip4; refusing to patch an unknown game version.");
    }
}
