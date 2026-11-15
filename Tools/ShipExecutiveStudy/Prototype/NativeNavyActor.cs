using System;
using System.Linq;
using HarmonyLib;
using MissionContracts;
using Newtonsoft.Json;
using Ostranauts.Core;
using Ostranauts.Ships;
using Ostranauts.Ships.Commands;
using Ostranauts.Ships.Sensors;
using Ostranauts.ShipGUIs.Utilities;
using Ostranauts.Utils.Models;

namespace ShipExecutivePrototype;

sealed partial class NativeNavyActor
{
    internal static NativeNavyActor Current;
    internal readonly NativeWorld World;
    internal readonly NativeReturnActor Travel;
    internal Machine Machine => Travel.Machine;
    internal NavyProfileState State;
    internal readonly NavyPolicyBinding Policy;
    internal float SteeringDelta;
    internal int CombatCalls, ReactionCalls;
    readonly KeepWithinFiringDistance steering;
    readonly KeepAtDistance longRangeGeometry;
    readonly Shoot shoot;
    readonly Dodge dodge;
    readonly FlyTo tacticalFlight;

    internal NativeNavyActor(NativeWorld world, Definition model) : this(world,
        new Machine(model, world.Subject.strRegID, "fixture-activation", Executive.RuntimeBinding, StarSystem.fEpoch), new NavyProfileState { NativeBinding = Executive.RuntimeBinding })
    {
    }

    NativeNavyActor(NativeWorld world, Machine machine, NavyProfileState state)
    {
        Current = this;
        World = world;
        State = state;
        Travel = new NativeReturnActor(world, machine);
        Policy = new NavyPolicyBinding(this);
        steering = new KeepWithinFiringDistance(world.Pilot);
        longRangeGeometry = new KeepAtDistance(world.Pilot);
        shoot = new Shoot(world.Pilot);
        dodge = new Dodge(world.Pilot);
        tacticalFlight = new FlyTo(world.Pilot);
        if (State.TacticalFlightPayload != null)
            tacticalFlight.SaveData = State.TacticalFlightPayload;
        Travel.AdditionalEffect = Ability;
        Travel.SelectGoalOverride = SelectGoal;
        Travel.BeforeSnapshot = CaptureProfile;
        Travel.GoalCaptureOverride = CaptureGoal;
        Travel.HandbackBlocker = () => State.Inbox.Count != 0 || State.ActiveDirective != 0 ? "executive command inbox is not empty"
            : State.ReactionStarted ? "urgent reaction still owns native controls" : null;
        if (state.Blackboard != null)
            BlackboardCodec.Restore(world.Pilot.Blackboard, JsonConvert.DeserializeObject<System.Collections.Generic.Dictionary<string, BlackboardValue>>(state.Blackboard, CheckpointJson.Settings), world);
        AccessTools.Field(typeof(AreMissilesComingMyWay), "_timeBetweenChecks").SetValue(Policy.Command<AreMissilesComingMyWay>(), state.MissileCheckInterval);
        if (state.ReactionStarted)
        {
            // Shared native clocks restore with the world. A constructor can
            // change them after the reaction's last tick; its saved per-ability
            // sample must not overwrite that later effect.
            world.Pilot.Blackboard.Set("DodgeTarget", state.ReactionTarget);
            world.Pilot.Blackboard.Set("DodgeDir", state.ReactionDirection);
        }
    }

    internal bool NeedsTacticalReturn()
    {
        bool low = AIShipManager.LowFuel(World.Subject, World.Pilot.MaxSpeed(null));
        var assembly = typeof(Ostrabus.OstrabusPlugin).Assembly;
        var policy = assembly.GetType("Ostrabus.CeresNavigation.CeresNavigationGuardrails", true);
        bool applies = (bool)AccessTools.Method(policy, "PolicyApplies").Invoke(null, new object[] { World.Pilot });
        if (applies)
        {
            object sample = AccessTools.Method(policy, "Reserve").Invoke(null, new object[] { World.Pilot, World.AI });
            low |= (bool)AccessTools.Field(sample.GetType(), "Retreat").GetValue(sample);
        }
        return low;
    }

    CommandStatus SelectGoal()
    {
        if (Machine.State.Active.Kind == "Return")
        {
            // Use the same existing Ceres station-choice policy when applicable.
            var type = typeof(Ostrabus.OstrabusPlugin).Assembly.GetType("Ostrabus.CeresNavigation.CeresNavigationGuardrails", true);
            if ((bool)AccessTools.Method(type, "PolicyApplies").Invoke(null, new object[] { World.Pilot }))
            {
                var home = (Ship)AccessTools.Method(type, "RetreatHome").Invoke(null, new object[] { World.Pilot });
                if (home != null)
                    new TargetData(home).ApplyToShip(World.Subject);
                return new CommandStatus(home == null ? CommandCode.Failure : CommandCode.Finished, Policy.Command<TargetNearestStation>());
            }
            return Policy.Command<TargetNearestStation>().RunCommand(World.Pilot.Blackboard);
        }
        if (Machine.State.Active.Source == "NavyPilot/TargetEmergencySignal")
            return Policy.Command<TargetEmergencySignal>().RunCommand(World.Pilot.Blackboard);
        if (State.DepartureMission != Machine.State.Active.Id)
        {
            var result = Policy.Command<Undock>().RunCommand(World.Pilot.Blackboard);
            if (result.Status != CommandCode.Finished)
                return result;
            State.DepartureMission = Machine.State.Active.Id;
        }
        return Policy.Command<TargetNewIfNone>().RunCommand(World.Pilot.Blackboard);
    }

    internal void Tick(float elapsed = 0)
    {
        using var operation = NativeExecutionFence.Mutation();
        Current = this;
        SteeringDelta = elapsed;
        if (Machine.State.Authority != "Executive" || Machine.State.ActorDisposition != "Active")
            return;
        if (!World.IsCanonical(World.Subject, World.AI) && !(Machine.State.PendingArrival != null && Travel.HasNativeBinding()))
            return;
        Machine.SettleWorldTime(StarSystem.fEpoch);
        if (Machine.State.PendingArrival != null)
        {
            Travel.Tick();
            return;
        }
        if (ProcessInbox())
            return;
        if (Machine.State.Active?.Kind == "Reaction" && Machine.State.Active.Phase != "Completed")
        {
            Travel.Tick();
            if (Machine.State.Active.Phase == "Completed" && Machine.State.Suspended.Count != 0)
                Travel.Execute(Machine.Resume());
            return;
        }
        var choice = Policy.Evaluate();
        bool activated = false;
        if (choice != null)
        {
            var current = Machine.State.Active;
            bool same = current != null && current.Kind == choice.Kind && current.Source == choice.Source && current.Goal?.TargetId == (choice.Goal?.TargetId ?? current.Goal?.TargetId)
                && current.Phase != "Completed" && current.Phase != "AwaitingPolicy";
            if (!same)
            {
                if (choice.Kind == "Patrol" && Machine.State.Suspended.LastOrDefault()?.Kind == "Patrol")
                {
                    Travel.Execute(Machine.FinishByPolicy(true));
                    Travel.Execute(Machine.Resume());
                }
                else
                {
                    Decision admitted;
                    if (current == null)
                        admitted = Machine.Begin(choice);
                    else if (current.Kind == "Patrol" && choice.Kind == "Combat")
                        admitted = Machine.Preempt(choice, true);
                    else
                        admitted = Machine.Replace(choice, true);
                    Travel.Execute(admitted);
                }
                activated = true;
            }
        }
        else if (Policy.LastResult == CommandCode.Finished)
        {
            if (Machine.State.Active?.Kind == "Combat" && World.Subject.shipCombatTarget == null)
                Travel.Execute(Machine.FinishByPolicy(true));
            return;
        }
        if (!activated && Machine.State.Active != null)
            Travel.Tick();
        if (Machine.State.Active?.Phase == "Completed" && Machine.State.Active.Goal?.Arrival == "None")
            World.Subject.ClearShipTarget();
    }

    Decision Ability(string name)
    {
        if (name == "RunNativeCombat")
            return Fight();
        if (name == "RunNativeReaction")
            return React();
        if (name == "RunNativeDirective")
            return RunDirective();
        throw new NotSupportedException("Unsupported Navy ability: " + name);
    }

    Decision Fight()
    {
        CombatCalls++;
        Ship enemy = Machine.State.Active.Goal?.TargetId == null ? World.Subject.shipCombatTarget : World.Resolve(Machine.State.Active.Goal.TargetId);
        if (enemy == null || enemy.bDestroyed || enemy.HideFromSystem || enemy.IsDocked())
            return CombatUnavailable();
        World.Subject.shipCombatTarget = enemy;
        var signature = new ShipSignature(enemy, World.Subject);
        if (World.Subject.objSS.GetDistance(enemy.objSS) > World.Subject.ElectronicSystems.GetSensorRangeKm(signature) / 149597872.0)
            return CombatUnavailable();
        shoot.RunCommand(World.Pilot.Blackboard);
        if (State.CombatMode == "Long")
            return FightLong(enemy);
        World.Subject.objSS.ResetNavData();
        double range = World.Pilot.Blackboard.Get<double>("EnagementRange");
        if (range <= 0)
            range = 30;
        Point offset = World.Subject.objSS.vPos - enemy.objSS.vPos;
        Point destination = enemy.objSS.vPos + offset.normalized * (range / 149597872.0);
        AccessTools.Method(typeof(KeepWithinFiringDistance), "KeepRange").Invoke(steering,
            new object[] { enemy.objSS.vPos, destination.X, destination.Y, enemy.objSS.vVelX, enemy.objSS.vVelY, AIShipManager.ShipATCLast.objSS.vVel, World.Pilot.MaxSpeed(false) * 2 });
        return Travel.Signal("Progress");
    }

    Decision FightLong(Ship enemy)
    {
        double range = World.Pilot.Blackboard.Get<double>("EnagementRange");
        if (range <= 0)
            range = 100;
        if (World.Subject.objSS.GetDistance(enemy.objSS) < range / 149597872.0)
        {
            AccessTools.Method(typeof(KeepAtDistance), "SetCourse").Invoke(longRangeGeometry, new object[] { range });
            // Combat movement is a tactical waypoint, never an implicit request
            // to dock with a hostile hull when destination construction fails.
            if (World.Subject.shipScanTarget != null || World.Subject.shipSituTarget == null)
                return CombatUnavailable();
            State.TacticalGoal = World.Subject.shipSituTarget.GetJSON();
        }
        if (State.TacticalGoal == null)
            return CombatUnavailable();
        new TargetData(new ShipSitu(State.TacticalGoal)).ApplyToShip(World.Subject);
        var result = tacticalFlight.RunCommand(World.Pilot.Blackboard);
        State.TacticalFlightPayload = tacticalFlight.SaveData;
        State.TacticalPhase = result.Status == CommandCode.Running ? "Approach" : "Finished";
        return Travel.Signal(result.Status == CommandCode.Running ? "Progress" : "CombatComplete");
    }

    Decision CombatUnavailable()
    {
        State.FailedCombatMode = State.CombatMode;
        return Travel.Signal("GoalUnavailable");
    }

    Decision React()
    {
        ReactionCalls++;
        if (!State.ReactionStarted)
        {
            State.ReactionStarted = true;
            State.ReactionOriginalIgnoreGravity = World.Subject.objSS.bIgnoreGrav;
            World.Subject.fAIDockingExpire = double.NegativeInfinity;
            World.Pilot.Blackboard.Set("DodgeTarget", State.ReactionTarget);
            World.Pilot.Blackboard.Set("DodgeDir", State.ReactionDirection);
        }
        var status = dodge.RunCommand(World.Pilot.Blackboard);
        State.ReactionRemaining = World.Subject.fAIDockingExpire;
        State.PauseRemaining = World.Subject.fAIPauseTimer;
        if (status.Status == CommandCode.Running)
            return Travel.Signal("Progress");
        World.Subject.objSS.bIgnoreGrav = State.ReactionOriginalIgnoreGravity;
        World.Subject.fAIDockingExpire = double.NegativeInfinity;
        World.Subject.fAIPauseTimer = 0;
        World.Pilot.Blackboard.Remove("DodgeTarget");
        World.Pilot.Blackboard.Remove("DodgeDir");
        State.ReactionStarted = false;
        return Travel.Signal("ReactionComplete");
    }

    internal bool Dodge(string target, float direction)
    {
        using var operation = NativeExecutionFence.Mutation();
        if (Machine.State.PendingArrival != null)
            return false;
        State.ReactionTarget = target;
        State.ReactionDirection = direction;
        if (Machine.State.Active?.Kind == "Reaction" && Machine.State.Active.Phase != "Completed")
        {
            // Native urgent replacement shares the existing reaction timer.
            // Updating the threat must not create another suspended reaction or
            // restart ten seconds of gravity suppression on every projectile.
            World.Pilot.Blackboard.Set("DodgeTarget", target);
            World.Pilot.Blackboard.Set("DodgeDir", direction);
            return true;
        }
        State.ReactionStarted = false;
        var choice = new PolicyChoice { Kind = "Reaction", Source = "AIShip.AddUrgentCommand/Dodge" };
        var admitted = Machine.State.Active == null ? Machine.Begin(choice) : Machine.Preempt(choice, true);
        Travel.Execute(admitted);
        return admitted.Accepted;
    }

    void CaptureProfile()
    {
        if (State.ReactionStarted)
        {
            State.ReactionRemaining = World.Subject.fAIDockingExpire;
            State.PauseRemaining = World.Subject.fAIPauseTimer;
        }
        State.MissileCheckInterval = (double)AccessTools.Field(typeof(AreMissilesComingMyWay), "_timeBetweenChecks").GetValue(Policy.Command<AreMissilesComingMyWay>());
        State.Blackboard = JsonConvert.SerializeObject(BlackboardCodec.Capture(World.Pilot.Blackboard), CheckpointJson.Settings);
        Travel.SnapshotExtension = JsonConvert.SerializeObject(State, CheckpointJson.Settings);
    }
    internal string Save() => Travel.Save();
    internal static NativeNavyActor Restore(Definition model, string snapshot)
    {
        var saved = JsonConvert.DeserializeObject<ReturnCheckpoint>(snapshot);
        var world = WorldCheckpoint.Restore(saved.World);
        return RestoreBound(model, saved, world);
    }
    internal static NativeNavyActor RestoreBound(Definition model, ReturnCheckpoint saved, NativeWorld world)
    {
        var state = ValidateProfile(saved);
        var machine = Machine.Restore(model, saved.Executive, world.Subject.strRegID, "fixture-activation", Executive.RuntimeBinding);
        if (state.ReactionStarted && (machine.State.Active?.Kind != "Reaction" || machine.State.Active.Phase != "Reacting"))
            throw new InvalidOperationException("Reaction state does not match executive mission");
        var actor = new NativeNavyActor(world, machine, state);
        actor.Travel.Arrival = saved.Arrival;
        return actor;
    }
    internal static NavyProfileState ValidateProfile(ReturnCheckpoint saved)
    {
        var state = JsonConvert.DeserializeObject<NavyProfileState>(saved.Profile, CheckpointJson.Settings);
        if (state == null || state.SchemaVersion != 2 || state.NativeBinding != Executive.RuntimeBinding)
            throw new InvalidOperationException("Incompatible Navy profile");
        if (state.Inbox == null || state.Inbox.Count > 16 || state.Inbox.Any(i => i.Id <= 0 || i.Id > state.NextDirective || i.Payload == null) ||
            state.Inbox.Select(i => i.Id).Distinct().Count() != state.Inbox.Count || state.ActiveDirective != 0 && !state.Inbox.Any(i => i.Id == state.ActiveDirective))
            throw new InvalidOperationException("Invalid Navy inbox checkpoint");
        if (state.Inbox.Any(i => i.Command == "TakeFuel" && (!i.ConstructorRan || i.NativeState == null || i.NativeState.Length != 1)))
            throw new InvalidOperationException("Fuel input lacks its native receipt construction record");
        return state;
    }
}
