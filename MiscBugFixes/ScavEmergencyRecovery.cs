using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.ShipGUIs.Utilities;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.AIPilots.Interfaces;
using Ostranauts.Ships.Commands;

namespace Ostrabus.MiscBugFixes;

internal static class ScavEmergencyRecovery {
    static readonly ConditionalWeakTable<Hold, object> Visits = new ConditionalWeakTable<Hold, object>();
    static readonly AccessTools.FieldRef<TargetNewIfNone, IAICharacter> Pilot = AccessTools.FieldRefAccess<TargetNewIfNone, IAICharacter>("_ai");
    internal static readonly FieldInfo PlanField = AccessTools.Field(AccessTools.Inner(typeof(TargetNewIfNone), "TargetDataDTO"), "NavData");
    static readonly AccessTools.FieldRef<object, NavData> HomePlan = AccessTools.FieldRefAccess<object, NavData>(PlanField);
    static readonly AccessTools.FieldRef<object, TargetData> HomeTarget = AccessTools.FieldRefAccess<object, TargetData>(AccessTools.Field(PlanField.DeclaringType, "TargetData"));
    static readonly Func<TargetNewIfNone, int> RequestDistance = (Func<TargetNewIfNone, int>)AccessTools.Method(typeof(TargetNewIfNone), "GetRequestDistance").CreateDelegate(typeof(Func<TargetNewIfNone, int>));

    internal static void MarkVisit(ScavPilot pilot) {
        if (!(pilot.BehaviorTree is SelectorCommand root)) return;
        var branches = (List<ICommand>)AccessTools.Field(typeof(SelectorCommand), "_childCommands").GetValue(root);
        foreach (ICommand branch in branches) {
            if (!(branch is StickySequenceCommand) || (string)AccessTools.Field(typeof(BaseCommand), "_nodeName").GetValue(branch) != "Main") continue;
            var leaves = (List<ICommand>)AccessTools.Field(typeof(StickySequenceCommand), "_childCommands").GetValue(branch);
            if (leaves.Count != 5 || !(leaves[0] is Undock) || !(leaves[4] is Hold hold)) throw new InvalidOperationException("Scav Main visit layout changed.");
            Visits.GetValue(hold, _ => new object());
        }
    }

    // Only the visit's fuel gate changes. Native timer and requested-help gate remain.
    internal static double VisitFuel(Ship ship, Hold hold) => Visits.TryGetValue(hold, out _) ? 1 : ship.GetRCSRemain();

    internal static void StoreHomePlan(object dto, NavData plan, TargetNewIfNone command) {
        HomePlan(dto) = plan;
        IAICharacter pilot = Pilot(command);
        TargetData home = HomeTarget(dto);
        // A native accepted plan already passed its propulsion/fuel checks.
        // Only its manual fallback needs the separate one-leg predicate.
        if (pilot.AIType != AIType.Scav || plan != null || home?.Situ == null) return;
        Ship ship = pilot.ShipUs;
        // Same one-leg exceptions as AmIOutOfFuel; no round-trip reserve and no
        // assumption that a null planned route rules out manual flight.
        double available = ship.DeltaVRemainingRCS;
        double needed = AIShipManager.GetDeltaVNeededToTargetFullTrip(ship.objSS, home.Situ, pilot.MaxSpeed(null), ship.bFusionReactorRunning);
        if (available >= needed || ship.LiftRotorsThrustStrength > 0) return;
        if (ship.bFusionReactorRunning && FlyToPath.IsInterregionalPath(ship.objSS, home.Situ)) return;
        HomeTarget(dto) = null;
    }

    internal static bool RequestHelp(TargetNewIfNone command, ref bool result) {
        if (Pilot(command).AIType != AIType.Scav) return true;
        Ship ship = command.ShipUs;
        // true means keep this controller, not "rescue dispatched". The native
        // caller supplies its existing pause. Failed sends must not retire crew.
        result = true;
        ship.Comms.GetCaptain(out CondOwner captain);
        if (DataHandler.GetCondTrigger("TIsHasRequestedHelp").Triggered(captain)) return false;
        Ship player = CrewSim.coPlayer.ship;
        bool atStation = false;
        foreach (Ship attached in player.GetAllDockedShips()) {
            // Preserve native FirstOrDefault station test, rather than changing policy.
            atStation = attached.IsStation();
            break;
        }
        if (!atStation && ship.GetRangeTo(player) * 149597872.0 < RequestDistance(command)) {
            if (ship.Comms.SendMessage("SHIPSosAskPlayer", player.strRegID)) BeatManager.ResetTensionTimer();
            return false;
        }
        if (ship.shipSituTarget != null && ship.objSS.GetRangeTo(ship.shipSituTarget) < 3.342293553032505E-08) return false;
        Ship station = CrewSim.system.GetNearestStationRegional(ship.objSS.vPosx, ship.objSS.vPosy);
        ship.Comms.SendMessage("SHIPSosStranded", station != null ? station.strRegID : AIShipManager.strATCLast);
        return false;
    }
}

[HarmonyPatch(typeof(ScavPilot), MethodType.Constructor, new[] { typeof(string), typeof(Ship) })]
internal static class ScavVisitRegistrationPatch {
    static void Postfix(ScavPilot __instance) => ScavEmergencyRecovery.MarkVisit(__instance);
}

[HarmonyPatch(typeof(Hold), nameof(Hold.RunCommand))]
internal static class ScavVisitFuelPatch {
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        int count = 0;
        foreach (CodeInstruction instruction in instructions) {
            if (instruction.Calls(AccessTools.Method(typeof(Ship), nameof(Ship.GetRCSRemain)))) {
                var first = new CodeInstruction(OpCodes.Ldarg_0);
                instruction.MoveLabelsTo(first); instruction.MoveBlocksTo(first);
                yield return first;
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ScavEmergencyRecovery), nameof(ScavEmergencyRecovery.VisitFuel)));
                count++;
            } else yield return instruction;
        }
        if (count != 1) throw new InvalidOperationException("Hold fuel gate changed.");
    }
}

[HarmonyPatch(typeof(TargetNewIfNone), nameof(TargetNewIfNone.RunCommand))]
internal static class ScavHomeFeasibilityPatch {
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        int count = 0;
        foreach (CodeInstruction instruction in instructions) {
            if (instruction.opcode == OpCodes.Stfld && Equals(instruction.operand, ScavEmergencyRecovery.PlanField)) {
                var first = new CodeInstruction(OpCodes.Ldarg_0);
                instruction.MoveLabelsTo(first); instruction.MoveBlocksTo(first);
                yield return first;
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ScavEmergencyRecovery), nameof(ScavEmergencyRecovery.StoreHomePlan)));
                count++;
            } else yield return instruction;
        }
        if (count != 1) throw new InvalidOperationException("Home selection boundary changed.");
    }
}

[HarmonyPatch(typeof(TargetNewIfNone), "GenerateNavData")]
internal static class ScavNullHomePlanPatch {
    static bool Prefix(TargetData target) => target != null;
}

[HarmonyPatch(typeof(TargetNewIfNone), "RequestHelp")]
internal static class ScavHelpLifecyclePatch {
    static bool Prefix(TargetNewIfNone __instance, ref bool __result) => ScavEmergencyRecovery.RequestHelp(__instance, ref __result);
}
