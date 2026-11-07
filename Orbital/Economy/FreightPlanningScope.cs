using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Ostranauts.Ships.Commands;

namespace Ostrabus.Orbital.Economy;

// A local planner clock, not a change to the world's epoch. Native helper methods
// otherwise subtract the END of the skip from a historical departure/arrival.
internal sealed class FreightPlanningScope : IDisposable {
    internal static FreightPlanningScope Current;
    readonly List<ShipSitu> live = new List<ShipSitu>();
    readonly List<ShipSitu> saved = new List<ShipSitu>();
    internal readonly double Departure;
    internal static double Epoch() => Current?.Departure ?? StarSystem.fEpoch;
    internal FreightPlanningScope(double departure) {
        if (Current != null) throw new InvalidOperationException("Nested historical freight planner.");
        Departure = departure;
        try {
            foreach (var ship in CrewSim.system.dictShips.Values) {
                if (ship == null || ship.bDestroyed || !ship.IsStation() || ship.objSS == null) continue;
                var situation = ship.objSS;
                if (!situation.bIsBO && !situation.bBOLocked && !situation.bOrbitLocked) continue;
                live.Add(situation); saved.Add(new ShipSitu(situation, createPath: false));
                OrbitalMotion.Write(situation, FreightRoute.StationAt(situation, departure));
            }
            Current = this;
        } catch { Dispose(); throw; }
    }
    internal static bool WithinNoWake(ShipSitu position, double epoch) {
        foreach (var ship in CrewSim.system.dictShips.Values) {
            if (ship == null || ship.bDestroyed || !ship.IsStation() || ship.IsNotAFullStation) continue;
            var clone = new ShipSitu(ship.objSS, createPath: false);
            if (clone.bIsBO || clone.bBOLocked || clone.bOrbitLocked) OrbitalMotion.Write(clone, FreightRoute.StationAt(clone, epoch));
            if (clone.GetDistance(position) < 2.005376018132665E-06) return true;
        }
        return false;
    }
    public void Dispose() {
        for (int i = 0; i < live.Count; i++) live[i].CopyFrom(saved[i], bKinematicsOnly: false);
        live.Clear(); saved.Clear();
        if (ReferenceEquals(Current, this)) Current = null;
        foreach (var body in CrewSim.system.aBOs.Values) body.UpdateTime(StarSystem.fEpoch);
    }
}
[HarmonyPatch]
internal static class FreightPlanningClockPatch {
    [HarmonyTargetMethods]
    internal static IEnumerable<MethodBase> Targets() {
        yield return AccessTools.Method(typeof(FlyToPath), nameof(FlyToPath.PlanTrip4));
        yield return AccessTools.Method(typeof(FlightCPU), nameof(FlightCPU.GetDockingNavPoint), new[] { typeof(ShipSitu), typeof(ShipSitu), typeof(double), typeof(bool) });
        yield return AccessTools.Method(typeof(ShipSitu), nameof(ShipSitu.LockToBO), new[] { typeof(BodyOrbit), typeof(double) });
        yield return AccessTools.Method(typeof(ShipSitu), nameof(ShipSitu.LockToBO), new[] { typeof(double), typeof(bool) });
    }
    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var clock = AccessTools.Field(typeof(StarSystem), nameof(StarSystem.fEpoch));
        var replacement = AccessTools.Method(typeof(FreightPlanningScope), nameof(FreightPlanningScope.Epoch));
        int matches = 0;
        var code = new List<CodeInstruction>(instructions);
        foreach (var instruction in code) if (instruction.opcode == OpCodes.Ldsfld && Equals(instruction.operand, clock)) {
            instruction.opcode = OpCodes.Call; instruction.operand = replacement; matches++;
        }
        if (matches == 0) throw new InvalidOperationException("Freight planner clock layout changed.");
        return code;
    }
}
[HarmonyPatch(typeof(StarSystem), nameof(StarSystem.IsWithinNoWakeRangeOfAnyStation))]
internal static class FreightNoWakePatch {
    [HarmonyPrefix]
    internal static bool Prefix(ShipSitu objSS, double fEpochCheck, ref bool __result) {
        if (FreightPlanningScope.Current == null) return true;
        __result = FreightPlanningScope.WithinNoWake(objSS, fEpochCheck);
        return false;
    }
}
