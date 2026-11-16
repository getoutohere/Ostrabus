using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;
using HarmonyLib;
using MissionContracts;
using Newtonsoft.Json;
using Ostranauts.Core;
using Ostranauts.Ships;
using Ostranauts.Ships.Commands;

namespace ShipExecutivePrototype;

sealed class ReturnCheckpoint
{
    public string Executive, World, Profile;
    public NativeArrivalManifest Arrival;
}

// Second-stage adapter: the return lifecycle is owned by Machine. The opaque
// native LowFuelRetreat and its reflected private flags are never executed.
// The existing native flight primitive, JSON payload and service repairs remain.
sealed class NativeReturnActor
{
    internal readonly NativeWorld World;
    internal readonly Machine Machine;
    internal bool AutoCheckpoint = true;
    internal string LastCheckpoint;
    internal int PolicyRequests, FlightCalls, ArrivalInvocations;
    internal Func<CommandStatus> SelectGoalOverride;
    internal Func<string, Decision> AdditionalEffect;
    internal Action BeforeSnapshot;
    internal string SnapshotExtension;
    internal Func<GoalReference> GoalCaptureOverride;
    internal NativeArrivalManifest Arrival;
    internal Action PrepareArrival;
    internal Func<string> ObserveArrival;
    internal Action RegisterArrivalIntent;
    internal Func<string> HandbackBlocker;
    internal const string CheckpointPath = "obj/ShipExecutiveModel/latest-native-checkpoint.json";
    readonly TargetNearestStation select;
    readonly Dock dock;
    FlyTo flight;
    long flightMission;

    internal NativeReturnActor(NativeWorld world, Definition model, string activation = "fixture-activation")
        : this(world, new Machine(model, world.Subject.strRegID, activation, Executive.RuntimeBinding, StarSystem.fEpoch)) { }

    internal NativeReturnActor(NativeWorld world, Machine machine)
    {
        World = world;
        Machine = machine;
        select = new TargetNearestStation(world.Pilot);
        dock = new Dock(world.Pilot);
        BindFlight();
    }

    internal bool BeginNativeLowFuelReturn()
    {
        if (!HasNativeBinding() || Machine.State.Authority != "Executive")
            return false;
        // Adoption is limited to the funded profile. Do not silently bypass the
        // native already-stranded lifetime/assistance decisions.
        if (World.Subject.GetRCSRemain() < 1)
            return false;
        // Preserve the native trigger and nearest-station selection. This is a
        // fixture profile, not a replacement Navy policy evaluator.
        if (!AIShipManager.LowFuel(World.Subject, World.Pilot.MaxSpeed(null)))
            return false;
        Execute(Machine.Begin(new PolicyChoice { Kind = "Return", Source = "AIShipManager.LowFuel/native-Navy-return" }));
        return true;
    }

    internal void BeginPatrol(GoalReference goal) => Execute(Machine.Begin(new PolicyChoice
    {
        Kind = "Patrol",
        Goal = goal,
        Source = "NavyPilot.GetTarget/fixture-policy-input"
    }));

    internal void Execute(Decision decision)
    {
        using var operation = NativeExecutionFence.Mutation();
        if (!decision.Accepted || !HasNativeBinding() || Machine.State.Authority != "Executive")
            return;
        var effects = new Queue<(string Name, Ticket Ticket)>();
        foreach (string effect in decision.Effects)
            effects.Enqueue((effect, Machine.Ticket));
        int budget = 0;
        while (effects.Count > 0)
        {
            if (++budget > 64)
                throw new InvalidOperationException("Adapter effect budget exhausted");
            var effect = effects.Dequeue();
            if (!Machine.Matches(effect.Ticket))
                continue;
            Decision next = null;
            switch (effect.Name)
            {
                case "StopOwnedControls":
                    World.StopControls();
                    break;
                case "SelectNativeGoal":
                    var selected = SelectGoalOverride == null ? select.RunCommand(World.Pilot.Blackboard) : SelectGoalOverride();
                    next = Signal(selected.Status == CommandCode.Running ? "Blocked" : selected.Status == CommandCode.Finished && World.Subject.shipSituTarget != null ? "GoalSelected" : "NoDestination",
                        selected.Status == CommandCode.Finished ? CaptureGoal() : null);
                    break;
                case "RevalidateGoal":
                    next = Signal(BindGoal() ? "ResumeValidated" : "ResumeRejected", Machine.State.Active.Goal);
                    break;
                case "RunNativeApproach":
                    next = Fly();
                    break;
                case "PersistArrivalIntent":
                    PrepareArrival?.Invoke();
                    if (RegisterArrivalIntent != null)
                    {
                        RegisterArrivalIntent();
                        next = Signal("InvocationBoundaryReady");
                        break;
                    }
                    LastCheckpoint = Save();
                    if (AutoCheckpoint)
                    {
                        PersistCheckpoint();
                        next = Signal("InvocationBoundaryReady");
                    }
                    break;
                case "InvokeNativeArrival":
                    next = Arrive();
                    break;
                case "ObserveWorldEffect":
                    // Unknown readback yields no recursive retry and no second
                    // native call. A later world observation may resolve it.
                    string readback = ArrivalProof();
                    if (readback != null)
                        next = Signal("WorldEffectObserved", proof: readback);
                    break;
                case "RequestNativePolicy":
                    PolicyRequests++;
                    break;
                default:
                    if (AdditionalEffect == null)
                        throw new NotSupportedException("Return fixture has no native adapter for " + effect.Name);
                    next = AdditionalEffect(effect.Name);
                    break;
            }
            if (next != null && next.Accepted)
                foreach (string name in next.Effects)
                    effects.Enqueue((name, Machine.Ticket));
        }
    }

    internal Decision Signal(string input, GoalReference goal = null, string proof = null)
    {
        var pending = Machine.State.PendingArrival;
        return Machine.Observe(new Observation
        {
            Event = input,
            Ticket = Machine.Ticket,
            Epoch = StarSystem.fEpoch,
            Goal = goal,
            EffectId = pending?.Id ?? 0,
            Destination = pending?.Destination,
            WorldProof = proof
        });
    }

    internal GoalReference CaptureGoal() => GoalCaptureOverride != null ? GoalCaptureOverride() : World.NavigationTargetId != null
        ? new GoalReference { TargetId = World.NavigationTargetId, Arrival = "Dock" }
        : new GoalReference { FrameId = World.Subject.shipSituTarget.strBOPORShip ?? World.Home.strRegID, X = World.Subject.shipSituTarget.vPosx, Y = World.Subject.shipSituTarget.vPosy, Arrival = "None" };

    bool BindGoal()
    {
        var goal = Machine.State.Active?.Goal;
        if (goal == null)
            return false;
        if (goal.TargetId != null)
        {
            var target = World.ResolveTarget(goal.TargetId);
            if (target == null)
                return false;
            target.ApplyToShip(World.Subject);
        }
        else
            new TargetData(new ShipSitu(false) { vPosx = goal.X, vPosy = goal.Y, strBOPORShip = goal.FrameId, bBOLocked = true }).ApplyToShip(World.Subject);
        return true;
    }

    void BindFlight()
    {
        long id = Machine.State.Active?.Id ?? 0;
        if (flight != null && flightMission == id)
            return;
        flight = new FlyTo(World.Pilot);
        flightMission = id;
        if (Machine.State.Active?.FlightPayload != null)
            flight.SaveData = Machine.State.Active.FlightPayload;
    }

    Decision Fly()
    {
        if (!BindGoal())
            return Signal("GoalUnavailable");
        // Eligibility is checked before either native navigation branch. The
        // installed pilot-wait service covers FlyToPath; manual steering must
        // not bypass the same capability boundary in this replacement adapter.
        // Existing authorized NavData still advances through world simulation.
        BindFlight();
        if (!World.Subject.NavAIManned && !(bool)AccessTools.Method(typeof(FlyTo), "HasValidPathTarget").Invoke(flight, null))
            return Signal("Blocked");
        FlightCalls++;
        // CanExecute=false is an authored skip, not proof of completed travel.
        if (!flight.CanExecute(World.Pilot.Blackboard))
            return Signal("Blocked");
        var result = flight.RunCommand(World.Pilot.Blackboard);
        Machine.State.Active.FlightPayload = flight.SaveData;
        if (result.Status == CommandCode.Failure)
            return Signal("ApproachFailed");
        return Signal(result.Status == CommandCode.Finished ? "ApproachComplete" : World.PilotAvailable ? "Progress" : "Blocked");
    }

    Decision Arrive()
    {
        if (!BindGoal())
            return Signal("NoWorldEffectIssued", proof: "native-call-did-not-enter-world-effect");
        // World fixture instruments the only supported station-arrival seam.
        // Do not generalize this counter to materialized docking or mooring.
        if (!ReferenceEquals(World.Subject.shipScanTarget, World.Home))
            throw new NotSupportedException("Only fixture home-station arrival is qualified");
        int attempts = World.ArrivalCalls;
        ArrivalInvocations++;
        dock.RunCommand(World.Pilot.Blackboard);
        string readback = ArrivalProof();
        if (readback != null)
            return Signal("WorldEffectObserved", proof: readback);
        return Signal(attempts == World.ArrivalCalls ? "NoWorldEffectIssued" : "WorldOutcomeUnknown",
            proof: attempts == World.ArrivalCalls ? "native-call-did-not-enter-world-effect" : null);
    }

    bool ArrivalObserved() => World.Retired && World.Subject.HideFromSystem && ReferenceEquals(World.Captain.ship, World.Resolve(Machine.State.PendingArrival?.Destination));
    string ArrivalProof() => ObserveArrival != null ? ObserveArrival() : ArrivalObserved() ? "actor-retired-with-crew-at-destination" : null;

    internal void Tick()
    {
        using var operation = NativeExecutionFence.Mutation();
        if (Machine.State.Authority != "Executive" || !HasNativeBinding())
            return;
        switch (Machine.State.Active?.Phase)
        {
            case "Selecting":
                Execute(new Decision { Accepted = true, Effects = new[] { "SelectNativeGoal" } });
                break;
            case "Revalidating":
                Execute(new Decision { Accepted = true, Effects = new[] { "RevalidateGoal" } });
                break;
            case "Approaching":
                Execute(Fly());
                break;
            case "ArrivalReady":
                if (!BindGoal())
                    Execute(Signal("GoalUnavailable"));
                else
                    Execute(Signal("DispatchArrival"));
                break;
            case "ArrivalUncertain":
                Execute(new Decision { Accepted = true, Effects = new[] { "ObserveWorldEffect" } });
                break;
            case "ArrivalPrepared":
            case "AwaitingPolicy":
            case "Completed":
                break;
            default:
                string effect = Machine.State.Active?.Phase == "Engaging" ? "RunNativeCombat" : Machine.State.Active?.Phase == "Reacting" ? "RunNativeReaction" : Machine.State.Active?.Phase == "Operating" ? "RunNativeDirective" : null;
                if (effect == null || AdditionalEffect == null)
                    throw new NotSupportedException("No adapter for phase " + Machine.State.Active?.Phase);
                Execute(AdditionalEffect(effect));
                break;
        }
    }

    internal void CommitCheckpoint()
    {
        PersistCheckpoint();
        Execute(Signal("InvocationBoundaryReady"));
    }

    void PersistCheckpoint()
    {
        // A single replaceable fixture checkpoint, not a per-flight journal.
        string temporary = CheckpointPath + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(CheckpointPath));
        byte[] bytes = Encoding.UTF8.GetBytes(LastCheckpoint ?? throw new InvalidOperationException("No prepared arrival snapshot"));
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(true);
        }
        if (File.Exists(CheckpointPath))
            File.Replace(temporary, CheckpointPath, null);
        else
            File.Move(temporary, CheckpointPath);
    }

    internal bool Advance(double seconds, string source)
    {
        using var operation = NativeExecutionFence.Mutation();
        if (Machine.State.Authority != "Executive" || !HasNativeBinding())
            return false;
        if (seconds < 0 || double.IsInfinity(seconds) || double.IsNaN(seconds))
            throw new ArgumentOutOfRangeException(nameof(seconds));
        if (Machine.State.Active?.Kind == "Combat")
            return false;
        double end = StarSystem.fEpoch + seconds;
        int budget = 0;
        while (StarSystem.fEpoch < end && !World.Retired)
        {
            if (++budget > 128)
                throw new InvalidOperationException("Advance budget exhausted");
            Tick();
            if (Machine.State.PendingArrival != null || Machine.State.Active.Phase == "AwaitingPolicy")
                return false;
            var route = World.Subject.objSS.NavData;
            if (route == null)
            {
                if (World.Retired || Machine.State.Active.Phase == "Completed")
                    break;
                if (!World.PilotAvailable)
                {
                    World.StopControls();
                    World.Coast(end - StarSystem.fEpoch);
                    StarSystem.fEpoch = end;
                    break;
                }
                if (Machine.State.Active.Phase == "ArrivalReady")
                    continue;
                return false;
            }
            double boundary = Math.Min(end, route.GetArrivalEpoch());
            if (boundary <= StarSystem.fEpoch)
                throw new InvalidOperationException("Trajectory did not release its completed boundary");
            StarSystem.fEpoch = boundary;
            World.Subject.objSS.NavData.TimeAdvance(World.Subject.objSS);
            Machine.SettleWorldTime(boundary);
            Tick();
            if (Machine.State.Active.Phase == "ArrivalReady")
                Tick();
        }
        if (World.Retired || Machine.State.Active.Phase == "Completed")
            StarSystem.fEpoch = end;
        Machine.SettleWorldTime(StarSystem.fEpoch);
        return true;
    }

    internal string Save()
    {
        var snapshot = CaptureState();
        snapshot.World = WorldCheckpoint.Capture(World);
        return JsonConvert.SerializeObject(snapshot);
    }

    internal ReturnCheckpoint CaptureState()
    {
        BeforeSnapshot?.Invoke();
        if (Machine.State.Authority == "Executive" && flightMission == Machine.State.Active?.Id && Machine.State.Active.Phase != "Completed")
            Machine.State.Active.FlightPayload = flight.SaveData;
        return new ReturnCheckpoint { Executive = Machine.Save(), Profile = SnapshotExtension, Arrival = Arrival };
    }

    internal bool HasNativeBinding() => ReferenceEquals(NativeWorld.Current, World)
        && (World.Retired || World.IsCanonical(World.Subject, World.AI)
            || Machine.State.PendingArrival != null && ObserveArrival != null && ReferenceEquals(World.Resolve(World.Subject.strRegID), World.Subject)
            && World.Subject.HideFromSystem && AIShipManager.GetAIShipByRegID(World.Subject.strRegID) == null);

    internal bool TryHandBack(out string reason)
    {
        using var operation = NativeExecutionFence.Mutation();
        Machine.RequestDisable();
        reason = HandbackBlocker?.Invoke();
        if (reason != null)
            return false;
        if (!Machine.MayOfferHandback || !HasNativeBinding())
        {
            reason = "pending effect, suspended mission or stale binding";
            return false;
        }
        if (World.Pilot.Blackboard.Get<bool>("InCombat") || World.Subject.shipCombatTarget != null || World.Pilot.PriorityQueue.CanExecute(World.Pilot.Blackboard))
        {
            reason = "native combat/queue state is not covered by this projection";
            return false;
        }
        if (!World.Retired)
        {
            string phase = Machine.State.Active?.Phase;
            bool patrol = Machine.State.Active?.Kind == "Patrol" && Machine.State.Active.Source == "NavyPilot.MainBehavior/TargetNewIfNone"
                && Machine.State.Active.Goal?.Arrival == "None" && phase == "Approaching";
            if ((!patrol && Machine.State.Active?.Kind != "Return") || phase != "Approaching" && phase != "ArrivalReady" || !BindGoal())
            {
                reason = "only ordinary return or native main-patrol approach is qualified";
                return false;
            }
            BindFlight();
            var children = (List<ICommand>)AccessTools.Field(typeof(SelectorCommand), "_childCommands").GetValue(World.Pilot.BehaviorTree);
            var main = (StickySequenceCommand)children.Last();
            var nativeFlight = (FlyToPatrol)World.Pilot.BehaviorTree.FindCommand(typeof(FlyToPatrol));
            nativeFlight.SaveData = flight.SaveData;
            main.SaveData = new[] { phase == "Approaching" ? "2" : "3" };
            World.AI.GetBehaviorTreeSaveData(); // Verify the projection can be serialized before releasing authority.
            AccessTools.Field(World.AI.GetType(), "_activeCommand").SetValue(World.AI, phase == "Approaching" ? (ICommand)nativeFlight : dock);
        }
        bool released = Machine.ReleaseToNative(true).Accepted;
        reason = released ? null : "projection release was refused";
        return released;
    }

    internal static NativeReturnActor Restore(Definition model, string checkpoint)
    {
        var saved = JsonConvert.DeserializeObject<ReturnCheckpoint>(checkpoint);
        Machine.Restore(model, saved.Executive, "SUBJECT", "fixture-activation", Executive.RuntimeBinding);
        var world = WorldCheckpoint.Restore(saved.World);
        var machine = Machine.Restore(model, saved.Executive, world.Subject.strRegID, "fixture-activation", Executive.RuntimeBinding);
        if (machine.State.ActorDisposition == "Retired" && !world.Retired)
            throw new InvalidOperationException("Executive/world checkpoint disagrees about actor retirement");
        var actor = new NativeReturnActor(world, machine);
        actor.Arrival = saved.Arrival;
        if (machine.State.Authority == "Executive" && !world.Retired && (machine.State.Active?.Phase == "Approaching" || machine.State.Active?.Phase == "Revalidating"))
            actor.BindGoal();
        return actor;
    }
}
