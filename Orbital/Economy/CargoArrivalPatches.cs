using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.AIPilots.Interfaces;
using Ostranauts.Ships.Commands;
using Ostranauts.Trading;

namespace Ostrabus.Orbital.Economy;

[HarmonyPatch(typeof(MarketManager), nameof(MarketManager.UnregisterCargoShip))]
internal static class CargoTransferPatch {
    static bool Prefix(string cargoShipRegId, string cargoDestinationStationRegId) {
        CargoDelivery.Deliver(cargoShipRegId, cargoDestinationStationRegId);
        return false;
    }
}

[HarmonyPatch]
internal static class CargoArrivalPatch {
    static IEnumerable<MethodBase> TargetMethods() {
        yield return AccessTools.Method(typeof(DockCargoAndDespawn), nameof(DockCargoAndDespawn.RunCommand));
        yield return AccessTools.Method(typeof(DockCargoAndDespawn), nameof(DockCargoAndDespawn.ResolveInstantly));
    }

    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator) =>
        CargoRetirementIL.RequireSuccess(instructions, generator,
            AccessTools.Method(typeof(DockCargoAndDespawn), "TransferMarketCargo"),
            AccessTools.Method(typeof(CargoArrivalPatch), nameof(Transfer)), extraCommand: false);

    internal static bool Transfer(BaseCommand command) => CargoDelivery.Deliver(command.ShipUs.strRegID, command.ShipUs.shipScanTarget.strRegID);
}

// Keep ordinary Dock's clearance, proximity, player and nonstation branches.
// Only its NPC station-retirement call acquires the cargo handoff missing in
// normal time. PASS has no market, so its existing personnel path is unchanged.
[HarmonyPatch(typeof(Dock), "AttemptToDock")]
internal static class CargoEscapeArrivalPatch {
    static readonly AccessTools.FieldRef<Dock, IAICharacter> Pilot = AccessTools.FieldRefAccess<Dock, IAICharacter>("_ai");

    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator) =>
        CargoRetirementIL.RequireSuccess(instructions, generator,
            AccessTools.Method(typeof(CrewSim), nameof(CrewSim.DockAndDespawn)),
            AccessTools.Method(typeof(CargoEscapeArrivalPatch), nameof(Retire)), extraCommand: true);

    internal static bool Retire(Ship ship, Ship station, string issuer, Dock command) {
        if (Pilot(command).AIType == AIType.HaulerCargo && !CargoDelivery.Deliver(ship.strRegID, station.strRegID)) return false;
        CrewSim.DockAndDespawn(ship, station, issuer);
        return true;
    }
}

internal static class CargoRetirementIL {
    internal static IEnumerable<CodeInstruction> RequireSuccess(IEnumerable<CodeInstruction> instructions, ILGenerator generator,
        MethodInfo original, MethodInfo replacement, bool extraCommand) {
        int sites = 0;
        foreach (var instruction in instructions) {
            if (!instruction.Calls(original)) { yield return instruction; continue; }
            sites++;
            if (extraCommand) {
                var owner = new CodeInstruction(OpCodes.Ldarg_0);
                instruction.MoveLabelsTo(owner); instruction.MoveBlocksTo(owner);
                yield return owner;
            }
            instruction.opcode = OpCodes.Call; instruction.operand = replacement;
            yield return instruction;
            var success = generator.DefineLabel();
            yield return new CodeInstruction(OpCodes.Brtrue, success);
            yield return new CodeInstruction(OpCodes.Ldarg_0);
            yield return new CodeInstruction(OpCodes.Call, AccessTools.PropertyGetter(typeof(BaseCommand), "Failure"));
            yield return new CodeInstruction(OpCodes.Ret);
            var resume = new CodeInstruction(OpCodes.Nop);
            ((System.Collections.IList)AccessTools.Field(typeof(CodeInstruction), "labels").GetValue(resume)).Add(success);
            yield return resume;
        }
        if (sites != 1) throw new InvalidOperationException("Cargo retirement call shape changed; expected one handoff.");
    }
}

internal static class FreightMission {
    static readonly AccessTools.FieldRef<SelectorCommand, List<ICommand>> Branches = AccessTools.FieldRefAccess<SelectorCommand, List<ICommand>>("_childCommands");
    static readonly AccessTools.FieldRef<StickySequenceCommand, List<ICommand>> Children = AccessTools.FieldRefAccess<StickySequenceCommand, List<ICommand>>("_childCommands");
    static readonly AccessTools.FieldRef<SequenceCommand, List<ICommand>> Steps = AccessTools.FieldRefAccess<SequenceCommand, List<ICommand>>("_childCommands");
    static readonly AccessTools.FieldRef<StickySequenceCommand, int> Phase = AccessTools.FieldRefAccess<StickySequenceCommand, int>("_currentChild");
    static readonly AccessTools.FieldRef<BaseCommand, string> Name = AccessTools.FieldRefAccess<BaseCommand, string>("_nodeName");
    static readonly AccessTools.FieldRef<AIShip, ICommand> Active = AccessTools.FieldRefAccess<AIShip, ICommand>("_activeCommand");

    internal static bool IsDelivery(AIShip ai) {
        if (!(ai?.AICharacter?.BehaviorTree is SelectorCommand root)) return false;
        if (ai.AICharacter.PriorityQueue.CanExecute(ai.Blackboard)) return false;
        // Game-load restores tree checkpoints before there is an active leaf.
        // A partially completed Flee remains an escape even with active == null.
        foreach (var branch in Branches(root)) if (branch is SequenceCommand sequence)
            foreach (var step in Steps(sequence))
                if (step is StickySequenceCommand escape && Name(escape) == "Flee" && Phase(escape) != 0) return false;
        foreach (var branch in Branches(root)) {
            if (!(branch is StickySequenceCommand main) || Name(main) != "Main") continue;
            var children = Children(main);
            if (children.Count != 3 || !(children[0] is TargetNewIfNone) || !(children[1] is FlyTo) || !(children[2] is DockCargoAndDespawn)) return false;
            var active = Active(ai);
            return active == null || ReferenceEquals(active, children[1]) || ReferenceEquals(active, children[2]);
        }
        return false;
    }
}
