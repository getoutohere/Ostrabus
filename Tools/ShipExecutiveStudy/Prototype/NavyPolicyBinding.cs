using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MissionContracts;
using Ostranauts.Core;
using Ostranauts.Ships;
using Ostranauts.Ships.Commands;

namespace ShipExecutivePrototype;

// Visit the actual constructor's ordered role policy. Only native guards and
// declared immediate policy actions run here; mission controllers emit choices.
// No native root dispatch, combat composite or movement action executes here.
sealed class NavyPolicyBinding
{
    readonly NativeNavyActor actor;
    readonly Policy definition;
    readonly Dictionary<Type, ICommand> commands;
    internal string LastNode;
    internal CommandCode LastResult;
    internal NavyPolicyBinding(NativeNavyActor actor)
    {
        this.actor = actor;
        definition = new Policy(actor.World.Pilot.BehaviorTree);
        commands = definition.Nodes.Values.Where(n => n.Kind == NodeKind.Leaf).GroupBy(n => n.Native.GetType()).ToDictionary(g => g.Key, g => g.First().Native);
    }

    internal ICommand Command<T>() where T : ICommand => commands[typeof(T)];
    internal PolicyChoice Evaluate()
    {
        choice = null;
        LastResult = Visit(definition.Root);
        return choice;
    }
    PolicyChoice choice;

    CommandCode Visit(PolicyNode node)
    {
        if (node.Kind == NodeKind.InputQueue)
            return CommandCode.Failure; // admitted separately
        if (node == definition.Root.Children.Last())
        {
            var current = actor.Machine.State.Active;
            if (current != null && (current.Kind == "Return" || current.Kind == "Patrol") && current.Phase != "Completed" && current.Phase != "AwaitingPolicy")
                return CommandCode.Running; // native TargetNewIfNone preserves an existing goal
            choice = new PolicyChoice { Kind = "Patrol", Source = "NavyPilot.MainBehavior/TargetNewIfNone", Goal = actor.State.OrdinaryOverride };
            return CommandCode.Running;
        }
        if (node.Kind == NodeKind.Leaf)
            return Leaf(node);
        if (node.Kind == NodeKind.Selector)
        {
            foreach (var child in node.Children)
            {
                var status = Visit(child);
                if (status != CommandCode.Failure)
                    return status;
            }
            return CommandCode.Failure;
        }
        foreach (var child in node.Children)
        {
            var status = Visit(child);
            if (status != CommandCode.Finished)
                return status;
        }
        return CommandCode.Finished;
    }

    CommandCode Leaf(PolicyNode node)
    {
        ICommand command = node.Native;
        LastNode = node.Id;
        if (command is TargetNearestStation)
        {
            choice = new PolicyChoice { Kind = "Return", Source = "NavyPilot/" + node.Id };
            return CommandCode.Running;
        }
        if (command is KeepWithinFiringDistance || command is KeepAtDistance)
        {
            string mode = command is KeepWithinFiringDistance ? "Short" : "Long";
            if (actor.State.FailedCombatMode == mode)
            {
                actor.State.FailedCombatMode = null;
                return CommandCode.Failure;
            }
            // The native tactical retreat predicate is an authored policy input;
            // the compound LowFuelRetreat state machine is not an ability here.
            if (actor.NeedsTacticalReturn())
                choice = new PolicyChoice { Kind = "Return", Source = "NavyPilot/native-tactical-retreat" };
            else if (actor.Machine.State.Active?.Kind == "Return" && actor.Machine.State.Active.Source == "NavyPilot/native-tactical-retreat")
                return CommandCode.Running; // preserve the native latched retreat
            else
            {
                actor.State.CombatMode = command is KeepWithinFiringDistance ? "Short" : "Long";
                choice = new PolicyChoice
                {
                    Kind = "Combat",
                    Source = "NavyPilot/" + command.GetType().Name,
                    Goal = actor.World.Subject.shipCombatTarget == null ? null : new GoalReference { TargetId = actor.World.Subject.shipCombatTarget.strRegID, Arrival = "None" }
                };
            }
            return CommandCode.Running;
        }
        if (command is TargetEmergencySignal)
        {
            var signal = actor.World.Pilot.Blackboard.Get<Ostranauts.Core.Models.Tuple<double, ShipSitu>>("EmergencySignal");
            if (signal?.Item2 == null)
                return CommandCode.Failure;
            choice = new PolicyChoice { Kind = "Patrol", Source = "NavyPilot/TargetEmergencySignal" };
            return CommandCode.Running;
        }
        if (command is FlyTo || command is Dock || command is Undock || command is TargetNewIfNone)
            throw new InvalidOperationException("Unbound movement command in policy: " + node.Id);
        if (!command.CanExecute(actor.World.Pilot.Blackboard))
            return CommandCode.Finished;
        return command.RunCommand(actor.World.Pilot.Blackboard).Status;
    }
}
