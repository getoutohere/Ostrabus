using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;

namespace Ostrabus.MiscBugFixes;

// State belongs to an existing native Main/Hold, never a fleet-wide controller.
internal static class CivilianVisitWaiting {
    const string Wire = "ostrabus.civilian-visit.v1";
    static readonly ConditionalWeakTable<Hold, Visit> Visits = new();
    static readonly AccessTools.FieldRef<Hold, double> Updated = AccessTools.FieldRefAccess<Hold, double>("_updateTimeStamp");
    static readonly AccessTools.FieldRef<Ship, Dictionary<string, Ship>> Attached = AccessTools.FieldRefAccess<Ship, Dictionary<string, Ship>>("aDocked");
    static readonly Func<BaseCommand, CommandStatus> Finished = (Func<BaseCommand, CommandStatus>)
        AccessTools.PropertyGetter(typeof(BaseCommand), "Success").CreateDelegate(typeof(Func<BaseCommand, CommandStatus>));

    internal sealed class Visit {
        internal readonly CivilianPilot Pilot;
        internal readonly Hold Hold;
        bool started;
        internal bool completed;
        internal Visit(CivilianPilot pilot, Hold hold) { Pilot = pilot; Hold = hold; Visits.Add(hold, this); }
        internal bool Eligible {
            get {
                Ship ship = Hold.ShipUs;
                return ship != null && ReferenceEquals(ship, Pilot.ShipUs) && !ship.bDestroyed && ship.IsAIShip && !ship.HideFromSystem && ship.objSS != null &&
                    ship.objSS.NavData == null &&
                    !ship.objSS.bIsBO && !ship.NavPlayerManned && !ReferenceEquals(CrewSim.coPlayer?.ship, ship) &&
                    ship.shipCombatTarget == null && !(Pilot.Blackboard != null && Pilot.Blackboard.HasKey("InCombat") && Pilot.Blackboard.Get<bool>("InCombat"));
            }
        }
        internal bool Shallow => Eligible && Hold.ShipUs.LoadState <= Ship.Loaded.Shallow;
        internal void Reset() { started = completed = false; Updated(Hold) = 0; }
        internal void Begin() {
            if (!started) {
                Hold.ShipUs.fAIDockingExpire = Duration(Hold);
                Updated(Hold) = StarSystem.fEpoch;
                started = true;
            } else if (Updated(Hold) <= 0 || Updated(Hold) > StarSystem.fEpoch) Updated(Hold) = StarSystem.fEpoch;
        }
        internal void Save() {
            // Vanilla Hold accepts/ignores primitive payloads; no custom save type.
            if (Hold.SaveData != null && (Hold.SaveData.Length == 0 || Hold.SaveData[0] != Wire)) return;
            Hold.SaveData = new[] { Wire, started ? "1" : "0", completed ? "1" : "0",
                Updated(Hold).ToString("R", CultureInfo.InvariantCulture) };
        }
        internal void Load(bool holding) {
            string[] data = Hold.SaveData;
            if (data?.Length == 4 && data[0] == Wire && (data[1] == "0" || data[1] == "1") &&
                (data[2] == "0" || data[2] == "1") && double.TryParse(data[3], NumberStyles.Float,
                CultureInfo.InvariantCulture, out double stamp) && !double.IsNaN(stamp) && !double.IsInfinity(stamp) && stamp >= 0) {
                started = data[1] == "1"; completed = data[2] == "1";
                Updated(Hold) = stamp > 0 && stamp <= StarSystem.fEpoch ? stamp : StarSystem.fEpoch;
            } else {
                // Preserve an old visit's remaining native countdown on first load.
                started = holding && !double.IsNegativeInfinity(Hold.ShipUs.fAIDockingExpire);
                completed = false; Updated(Hold) = holding ? StarSystem.fEpoch : 0;
            }
        }
    }

    internal static bool BeforeRun(Hold hold, ref CommandStatus result) {
        if (!Visits.TryGetValue(hold, out Visit visit) || !visit.Eligible) return true;
        // Preserve native failed-route/pause handling before starting a visit.
        if (hold.ShipUs.fAIPauseTimer > StarSystem.fEpoch && hold.ShipUs.fAIDockingExpire == 0) return true;
        if (visit.completed) {
            hold.ShipUs.Maneuver(0, 0, 0, 0, 1E-10f);
            result = Finished(hold); return false;
        }
        visit.Begin();
        return true;
    }
    internal static void AfterRun(Hold hold, CommandStatus result) {
        if (result.Status == CommandCode.Finished && Visits.TryGetValue(hold, out Visit visit)) visit.completed = true;
    }
    internal static double Fuel(double actual, Hold hold) {
        if (actual >= 1 || !Visits.TryGetValue(hold, out Visit visit) || !visit.Shallow) return actual;
        // This only releases a wait. Actual tanks, propulsion and planning stay native.
        // Do not release an empty vessel toward a missing or hostile home port.
        TargetData home = SafeHome(visit.Pilot);
        return home == null ? actual : 1;
    }
    internal static TargetData SafeHome(CivilianPilot pilot) {
        if (pilot?.ShipUs == null || pilot.CaptainUs == null || !pilot.CaptainUs.bAlive || CrewSim.system == null) return null;
        TargetData home = pilot.GetTargetHome();
        Ship station = home?.Ship;
        if (station == null || station.bDestroyed || station.HideFromSystem || !station.IsStation() ||
            !ReferenceEquals(CrewSim.system.GetShipByRegID(station.strRegID), station)) return null;
        string owner = CrewSim.system.GetShipOwner(station.strRegID);
        return JsonFaction.GetReputation(pilot.CaptainUs.GetFactionScore(owner)) == JsonFaction.Reputation.Dislikes ? null : home;
    }
    internal static bool AsteroidVisit(Hold hold) {
        if (!Visits.TryGetValue(hold, out Visit visit) || !visit.Eligible) return false;
        var links = Attached(hold.ShipUs);
        if (links != null) foreach (Ship peer in links.Values)
            if (peer != null && !peer.bDestroyed && peer.Classification == Ship.TypeClassification.Asteroid) return true;
        return false;
    }
    // Independent simulation feature changes only this one-time duration draw.
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static double Duration(Hold hold) => MathUtils.Rand(100, 650, MathUtils.RandType.Flat);
}

[HarmonyPatch(typeof(Hold), nameof(Hold.RunCommand))]
internal static class CivilianVisitHoldPatch {
    static bool Prefix(Hold __instance, ref CommandStatus __result) => CivilianVisitWaiting.BeforeRun(__instance, ref __result);
    static void Postfix(Hold __instance, CommandStatus __result) => CivilianVisitWaiting.AfterRun(__instance, __result);
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var code = new List<CodeInstruction>(instructions);
        int sites = 0;
        for (int i = 0; i < code.Count; i++) {
            var instruction = code[i];
            if (instruction.opcode == OpCodes.Ldc_R8 && Equals(instruction.operand, 1d) && i > 0 &&
                code[i - 1].operand is System.Reflection.MethodInfo read && read.ReturnType == typeof(double) &&
                (read == AccessTools.Method(typeof(Ship), nameof(Ship.GetRCSRemain)) ||
                 read.Name == "VisitFuel" && read.DeclaringType?.FullName == "Ostrabus.MiscBugFixes.ScavEmergencyRecovery")) {
                var owner = new CodeInstruction(OpCodes.Ldarg_0);
                instruction.MoveLabelsTo(owner); instruction.MoveBlocksTo(owner);
                yield return owner;
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(CivilianVisitWaiting), nameof(CivilianVisitWaiting.Fuel)));
                sites++;
            }
            yield return instruction;
        }
        if (sites != 1) throw new InvalidOperationException("Native civilian visit fuel boundary changed.");
    }
}

[HarmonyPatch(typeof(BaseCharacter), nameof(BaseCharacter.GetTargetHome))]
internal static class CivilianHomeDispatchPatch {
    static bool Prefix(BaseCharacter __instance, ref TargetData __result) {
        if (!(__instance is CivilianPilot civilian) || civilian.ShipUs == null || civilian.CaptainUs == null) return true;
        TargetData home = civilian.GetTargetHome();
        if (home == null) return true; // Preserve the native fallback for actors without a faction home.
        __result = home;
        return false;
    }
}
