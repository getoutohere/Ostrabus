using System;
using System.Collections.Generic;
using HarmonyLib;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;

namespace Ostrabus.ShipNavigation;

// A Navy patrol is an ordinary mission, not an overriding scripted action.
// Keep the native command instance, including its route/fuel progress, and let
// the native root evaluate combat, threats and support before running it.
internal static class PatrolCommand {
    static readonly AccessTools.FieldRef<SelectorCommand, List<ICommand>> Branches = AccessTools.FieldRefAccess<SelectorCommand, List<ICommand>>("_childCommands");
    static readonly AccessTools.FieldRef<StickySequenceCommand, List<ICommand>> Children = AccessTools.FieldRefAccess<StickySequenceCommand, List<ICommand>>("_childCommands");
    static readonly AccessTools.FieldRef<StickySequenceCommand, int> Phase = AccessTools.FieldRefAccess<StickySequenceCommand, int>("_currentChild");
    static readonly AccessTools.FieldRef<BaseCommand, string> Name = AccessTools.FieldRefAccess<BaseCommand, string>("_nodeName");
    static readonly AccessTools.FieldRef<QueuedCommandsNode, Queue<ICommand>> Waiting = AccessTools.FieldRefAccess<QueuedCommandsNode, Queue<ICommand>>("commandQueue");
    static readonly AccessTools.FieldRef<QueuedCommandsNode, ICommand> Current = AccessTools.FieldRefAccess<QueuedCommandsNode, ICommand>("currentCommand");
    static readonly AccessTools.FieldRef<QueuedCommandsNode, Ostranauts.Ships.AIPilots.Interfaces.IAICharacter> Pilot = AccessTools.FieldRefAccess<QueuedCommandsNode, Ostranauts.Ships.AIPilots.Interfaces.IAICharacter>("_ai");
    static readonly AccessTools.FieldRef<AIShip, ICommand> Active = AccessTools.FieldRefAccess<AIShip, ICommand>("_activeCommand");

    static StickySequenceCommand FindMain(NavyPilot pilot) {
        if (pilot == null || pilot.GetType() != typeof(NavyPilot) || !(pilot.BehaviorTree is SelectorCommand root)) return null;
        var branches = Branches(root);
        if (branches.Count != 5 || !ReferenceEquals(branches[0], pilot.PriorityQueue) ||
            !(branches[4] is StickySequenceCommand main) || Name(main) != "MainBehavior") return null;
        var children = Children(main);
        return children.Count == 4 && children[0].GetType() == typeof(Undock) && children[1].GetType() == typeof(TargetNewIfNone) &&
            children[2].GetType() == typeof(FlyToPatrol) && children[3].GetType() == typeof(Dock) ? main : null;
    }

    internal static bool Assign(AIShip ai, ICommand command) {
        if (command?.GetType() != typeof(FlyToPatrol) || !(ai?.AICharacter is NavyPilot pilot)) return false;
        var main = FindMain(pilot);
        if (main == null || !ReferenceEquals(command.ShipUs, pilot.ShipUs)) return false;
        ICommand previous = Children(main)[2];
        Children(main)[2] = command;
        Phase(main) = 2;
        if (ReferenceEquals(Active(ai), previous)) Active(ai) = command;
        return true;
    }

    internal static void Restore(QueuedCommandsNode queue) {
        if (!(Pilot(queue) is NavyPilot pilot) || !ReferenceEquals(queue, pilot.PriorityQueue)) return;
        var main = FindMain(pilot);
        if (main == null) return;
        var waiting = Waiting(queue);
        ICommand current = Current(queue);
        // No provenance survives older saves. Migrate only the unambiguous
        // single Navy patrol mission; never dismantle a mixed scripted queue.
        ICommand patrol = current ?? (waiting.Count == 1 ? waiting.Peek() : null);
        if (patrol?.GetType() != typeof(FlyToPatrol) || current != null && waiting.Count != 0 ||
            !ReferenceEquals(patrol.ShipUs, pilot.ShipUs)) return;
        Children(main)[2] = patrol;
        Phase(main) = 2;
        if (current != null) Current(queue) = null;
        else waiting.Dequeue();
    }
}

[HarmonyPatch(typeof(AIShip), nameof(AIShip.AddCommand))]
internal static class PatrolAdmissionPatch {
    [HarmonyPrefix]
    static bool Prefix(AIShip __instance, ICommand command) => !PatrolCommand.Assign(__instance, command);
}

// Run after the entire tree is restored. Migrating inside the queue loader
// would let the subsequent MainBehavior load overwrite the migrated flight state.
[HarmonyPatch(typeof(AIShip), nameof(AIShip.LoadBehaviorTreeSave))]
internal static class PatrolRestorePatch {
    [HarmonyPostfix]
    static void Postfix(AIShip __instance) {
        if (__instance.AICharacter?.PriorityQueue is QueuedCommandsNode queue) PatrolCommand.Restore(queue);
    }
}
