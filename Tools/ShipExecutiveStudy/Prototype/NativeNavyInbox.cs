using System;
using System.Linq;
using HarmonyLib;
using MissionContracts;
using Ostranauts.Ships;
using Ostranauts.Ships.Commands;

namespace ShipExecutivePrototype;

sealed partial class NativeNavyActor
{
    static readonly string[] NativeTakeover = { "Scuttle", "SurrenderArrestAndDespawn", "LockPirate", "RaceComms", "DeployHauler", "DeployLeo" };
    static readonly string[] PrimitiveDirectives = { "SurrenderConsiderOffer", "SurrenderConsiderPlayerDemand", "Shoot", "TargetPlayer", "TakeFuel", "TurnOnAllSensors", "TurnOffAllActiveSensors" };
    ICommand directiveCommand;
    long commandId;

    internal NativeDirective Enqueue(string command, string[] payload, string origin, bool urgent, bool once, string counterpart, string affectedCharacter, string projectile = null)
    {
        using var operation = NativeExecutionFence.Mutation();
        if (State.Inbox.Count >= 16)
            throw new InvalidOperationException("Navy inbox is full; command admission was not acknowledged");
        if (command == "FlyToPatrol" && !once)
        {
            // Beat/mission admission is ordinary intent, never a queue lock on combat.
            if (World.Subject.shipSituTarget != null)
                State.OrdinaryOverride = CaptureRawGoal();
            return null;
        }
        var input = new NativeDirective
        {
            Id = ++State.NextDirective,
            Command = command,
            Payload = payload == null ? Array.Empty<string>() : (string[])payload.Clone(),
            Origin = origin,
            Urgent = urgent,
            Once = once,
            Counterparty = counterpart,
            AffectedCharacter = affectedCharacter,
            ReceivedEpoch = StarSystem.fEpoch,
            DeferredDodgeTarget = projectile,
            ReadDodgeContextAtDispatch = command == "Dodge" && projectile == null
        };
        if (urgent)
            State.Inbox.Insert(0, input);
        else
            State.Inbox.Add(input);
        return input;
    }

    bool ProcessInbox()
    {
        if (State.Inbox.Count == 0)
            return false;
        var input = State.Inbox[0];
        if (input.Command == "Dodge")
        {
            State.Inbox.RemoveAt(0);
            string target = input.ReadDodgeContextAtDispatch ? World.Pilot.Blackboard.Get<string>("DodgeTarget") : input.DeferredDodgeTarget;
            Dodge(target, World.Pilot.Blackboard.Get<float>("DodgeDir"));
            return true;
        }
        if (Machine.State.Active?.Kind == "Reaction" && Machine.State.Active.Phase != "Completed")
            return false;
        if (NativeTakeover.Contains(input.Command))
        {
            // These authored commands supersede navigation or enter an external
            // subsystem. Give native code exclusive control before invoking them.
            Travel.Execute(Machine.FinishByPolicy(true));
            if (!Machine.TransferForSupersedingDirective(true).Accepted)
                return true;
            foreach (var queued in State.Inbox)
            {
                var native = RestorePrimitive(queued);
                if (native == null)
                    throw new InvalidOperationException("Native command factory rejected admitted command " + queued.Command);
                World.AI.AddCommand(queued.Once ? new OneShotWrapperCommand(native) : native);
            }
            State.Inbox.Clear();
            State.ActiveDirective = 0;
            return true;
        }
        if (input.Command != "ComeHere" && !PrimitiveDirectives.Contains(input.Command))
            throw new NotSupportedException("Preserved unsupported Navy input: " + input.Command);
        if (State.ActiveDirective != input.Id || Machine.State.Active?.Kind != "Directive" || Machine.State.Active.Phase == "AwaitingPolicy")
        {
            State.ActiveDirective = input.Id;
            var choice = new PolicyChoice { Kind = "Directive", Source = input.Origin + "/" + input.Command + "/" + input.Id };
            var admitted = Machine.State.Active == null ? Machine.Begin(choice) : Machine.State.Active.Phase == "Completed" || Machine.State.Active.Kind == "Directive" ? Machine.Replace(choice, true) : Machine.Preempt(choice, true);
            Travel.Execute(admitted);
        }
        else
            Travel.Tick();
        if (Machine.State.Active?.Phase == "Completed" || Machine.State.Active?.Phase == "AwaitingPolicy")
        {
            if (Machine.State.Active.Phase == "AwaitingPolicy")
                Travel.Execute(Machine.FinishByPolicy(true));
            State.Inbox.RemoveAt(0);
            State.ActiveDirective = 0;
            directiveCommand = null;
            if (State.OrdinaryOverride != null)
                Machine.RetargetOrdinaryContinuation(State.OrdinaryOverride);
            if (Machine.State.Suspended.Count > 0)
                Travel.Execute(Machine.Resume());
        }
        return true;
    }

    Decision RunDirective()
    {
        var input = State.Inbox.First(i => i.Id == State.ActiveDirective);
        if (input.Command == "ComeHere")
            return ComeHere(input);
        // Primitive commands use the native ship's shared clocks. They are
        // saved with the world, not restored from an older queued input: a
        // later receipt or urgent command may legitimately have changed them.
        if (directiveCommand == null || commandId != input.Id)
        {
            directiveCommand = RestorePrimitive(input) ?? throw new InvalidOperationException("Missing native directive factory");
            commandId = input.Id;
            input.ConstructorRan = true;
        }
        var status = directiveCommand.RunCommand(World.Pilot.Blackboard);
        input.NativeState = directiveCommand.SaveData;
        input.PauseUntil = World.Subject.fAIPauseTimer;
        input.DockingRemaining = World.Subject.fAIDockingExpire;
        if (input.Command == "TargetPlayer")
            State.OrdinaryOverride = CaptureRawGoal();
        return Travel.Signal(input.Once || status.Status == CommandCode.Finished ? "DirectiveComplete" : status.Status == CommandCode.Failure ? "DirectiveFailed" : "DirectiveProgress");
    }

    ICommand RestorePrimitive(NativeDirective input)
    {
        var command = input.Command == "TakeFuel" && input.ConstructorRan
            ? NativeCommandLifetime.RestoreTakeFuel(World.Subject, input.NativeState)
            : Ostranauts.Ships.AIPilots.AIShip.CreateCommand(input.Command, World.Pilot);
        if (command != null && (input.NativeState ?? input.Payload).Length > 0)
            command.SaveData = input.NativeState ?? input.Payload;
        return command;
    }

    Decision ComeHere(NativeDirective input)
    {
        if (input.Phase == "Start")
        {
            var result = Policy.Command<Undock>().RunCommand(World.Pilot.Blackboard);
            if (result.Status == CommandCode.Running)
                return Travel.Signal("DirectiveProgress");
            if (result.Status == CommandCode.Failure)
                return Travel.Signal("DirectiveFailed");
            new TargetPlayer(World.Pilot).RunCommand(World.Pilot.Blackboard);
            input.Goal = CaptureRawGoal();
            input.Goal.Arrival = "None";
            input.Phase = "Approach";
            directiveCommand = new FlyTo(World.Pilot);
            commandId = input.Id;
        }
        if (input.Phase == "Approach")
        {
            var target = World.Resolve(input.Goal.TargetId);
            if (target == null || target.bDestroyed)
                return Travel.Signal("DirectiveFailed");
            new TargetData(target).ApplyToShip(World.Subject);
            if (!(directiveCommand is FlyTo) || commandId != input.Id)
            {
                directiveCommand = new FlyTo(World.Pilot);
                commandId = input.Id;
                if (input.NativeState != null)
                    directiveCommand.SaveData = input.NativeState;
            }
            var result = directiveCommand.RunCommand(World.Pilot.Blackboard);
            input.NativeState = directiveCommand.SaveData;
            if (result.Status == CommandCode.Failure)
                return Travel.Signal("DirectiveFailed");
            if (result.Status == CommandCode.Running)
                return Travel.Signal("DirectiveProgress");
            new TargetATC(World.Pilot).RunCommand(World.Pilot.Blackboard);
            State.OrdinaryOverride = CaptureRawGoal();
            input.OriginalIgnoreGravity = World.Subject.objSS.bIgnoreGrav;
            input.Phase = "Hold";
            input.NativeState = null;
            directiveCommand = null;
        }
        if (!(directiveCommand is Hold) || commandId != input.Id)
        {
            directiveCommand = new Hold(World.Pilot);
            commandId = input.Id;
            AccessTools.Field(typeof(Hold), "_updateTimeStamp").SetValue(directiveCommand, input.HoldUpdated);
        }
        if (input.ConstructorRan)
        {
            World.Subject.fAIDockingExpire = input.DockingRemaining;
            World.Subject.fAIPauseTimer = input.PauseUntil;
        }
        input.ConstructorRan = true;
        var held = directiveCommand.RunCommand(World.Pilot.Blackboard);
        input.HoldUpdated = (double)AccessTools.Field(typeof(Hold), "_updateTimeStamp").GetValue(directiveCommand);
        input.DockingRemaining = World.Subject.fAIDockingExpire;
        input.PauseUntil = World.Subject.fAIPauseTimer;
        if (held.Status == CommandCode.Running)
            return Travel.Signal("DirectiveProgress");
        World.Subject.objSS.bIgnoreGrav = input.OriginalIgnoreGravity;
        World.Subject.shipScanTarget = null; // authored ComeHere completion
        return Travel.Signal(held.Status == CommandCode.Finished ? "DirectiveComplete" : "DirectiveFailed");
    }

    GoalReference CaptureRawGoal() => World.Subject.shipScanTarget != null
        ? new GoalReference { TargetId = World.Subject.shipScanTarget.strRegID, Arrival = "Dock" }
        : new GoalReference { FrameId = World.Subject.shipSituTarget.strBOPORShip ?? World.Home.strRegID, X = World.Subject.shipSituTarget.vPosx, Y = World.Subject.shipSituTarget.vPosy, Arrival = "None" };
    GoalReference CaptureGoal() => CaptureRawGoal();
}
