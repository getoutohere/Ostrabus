using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;

namespace Ostrabus.ShipNavigation;

// Losing sensor contact must not bypass the native neutralization/retargeting
// branch. Adapt only its admission gates, retaining the real detector and all
// of the authored combat decisions that follow it.
internal static class CombatCleanupAdmission {
    const string AdmissionName = "OstrabusCombatAdmission";
    const string ScanName = "OstrabusCombatScan";
    static readonly AccessTools.FieldRef<SelectorCommand, List<ICommand>> Branches =
        AccessTools.FieldRefAccess<SelectorCommand, List<ICommand>>("_childCommands");
    static readonly AccessTools.FieldRef<SequenceCommand, List<ICommand>> Steps =
        AccessTools.FieldRefAccess<SequenceCommand, List<ICommand>>("_childCommands");
    static readonly AccessTools.FieldRef<BaseCommand, string> Name =
        AccessTools.FieldRefAccess<BaseCommand, string>("_nodeName");

    internal static bool Configure(BaseCharacter pilot) {
        if (pilot == null || pilot.GetType() != typeof(NavyPilot) && pilot.GetType() != typeof(BlockadePilot) ||
            pilot.BehaviorTree == null) return false;
        bool blockade = pilot.GetType() == typeof(BlockadePilot);
        if (!(pilot.BehaviorTree is SelectorCommand root)) throw LayoutChanged(pilot);
        var branches = Branches(root);
        if (branches.Count != 5 || !ReferenceEquals(branches[0], pilot.PriorityQueue) ||
            !(branches[1] is SequenceCommand combat) || Name(combat) != "CombatResponse") throw LayoutChanged(pilot);
        var gates = Steps(combat);
        if (gates.Count != 4 || gates[2].GetType() != typeof(TurnOnAllSensors) ||
            !(gates[3] is SelectorCommand responses)) throw LayoutChanged(pilot);
        var choices = Branches(responses);
        if (choices.Count != (blockade ? 5 : 6) || !(choices[0] is SequenceCommand neutralization)) throw LayoutChanged(pilot);
        var cleanup = Steps(neutralization);
        if (cleanup.Count != 3 || cleanup[0].GetType() != typeof(IsTargetNeutralized) ||
            cleanup[1].GetType() != typeof(TargetNearestStation) || cleanup[2].GetType() != typeof(TurnOffAllActiveSensors))
            throw LayoutChanged(pilot);

        // Idempotence without a registry or state added to the save. These
        // stateless leaves contribute no serialized command paths/payloads.
        if (gates[0] is ConditionCommand first && Name(first) == AdmissionName &&
            gates[1] is ConditionCommand second && Name(second) == ScanName) return false;
        if (gates[0].GetType() != typeof(AmIInCombat) ||
            gates[1].GetType() != (blockade ? typeof(AreThreatsNearbyBlockade) : typeof(AreThreatsNearby)))
            throw LayoutChanged(pilot);

        ICommand admission = gates[0], scan = gates[1];
        var admissionGate = new ConditionCommand(AdmissionName, pilot, (_, board) => Admit(admission, board));
        var scanGate = new ConditionCommand(ScanName, pilot, (_, board) => Admit(scan, board));
        // Native ConditionCommand ignores its name argument. Set the diagnostic
        // names explicitly; SaveData remains null, just like the original gates.
        Name(admissionGate) = AdmissionName;
        Name(scanGate) = ScanName;
        gates[0] = admissionGate;
        gates[1] = scanGate;
        return true;
    }

    static bool Admit(ICommand original, Blackboard board) =>
        original.RunCommand(board).Status == CommandCode.Finished || board.Get<List<Ship>>("Combatants")?.Count > 0;

    static InvalidOperationException LayoutChanged(BaseCharacter pilot) => new InvalidOperationException(
        "Ostrabus combat cleanup admission: unexpected " + pilot.GetType().Name + " combat tree; no gates changed.");
}

[HarmonyPatch]
internal static class CombatCleanupAdmissionPatch {
    static IEnumerable<MethodBase> TargetMethods() {
        yield return AccessTools.Constructor(typeof(NavyPilot), new[] { typeof(string), typeof(Ship) });
        yield return AccessTools.Constructor(typeof(BlockadePilot), new[] { typeof(string), typeof(Ship) });
    }

    [HarmonyPostfix]
    static void Postfix(BaseCharacter __instance) => CombatCleanupAdmission.Configure(__instance);
}
