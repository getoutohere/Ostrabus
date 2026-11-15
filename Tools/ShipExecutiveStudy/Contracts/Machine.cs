using System;
using System.Linq;
using Newtonsoft.Json;

namespace MissionContracts;

// A pure execution reducer: no Ship, Unity, Harmony, native command graph or
// authority to choose destinations. World effects are requested through a port.
public sealed class Machine
{
    public readonly Definition Model;
    public readonly State State;
    public Ticket Ticket => new Ticket(State);
    static readonly JsonSerializerSettings Json = new JsonSerializerSettings
    {
        TypeNameHandling = TypeNameHandling.None,
        MissingMemberHandling = MissingMemberHandling.Error
    };

    public Machine(Definition model, string actor, string activation, string nativeBinding, double epoch)
    {
        if (string.IsNullOrWhiteSpace(actor) || string.IsNullOrWhiteSpace(activation) || string.IsNullOrWhiteSpace(nativeBinding) || !Finite(epoch))
            throw new ArgumentException("An executive requires explicit actor, activation, binding and epoch");
        Model = model;
        State = new State
        {
            ActorId = actor,
            Activation = activation,
            NativeBinding = nativeBinding,
            Epoch = epoch,
            ModelId = model.Id,
            ModelHash = model.Fingerprint,
            ReducerBinding = typeof(Machine).Module.ModuleVersionId.ToString(),
            BindingRevision = 1
        };
    }

    static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    static Decision Reject(string reason) => new Decision { Reason = reason };
    static Decision Accept(string id, params string[] effects) => new Decision { Accepted = true, TransitionId = id, Effects = effects };

    string Entry(PolicyChoice choice)
    {
        if (choice == null || string.IsNullOrWhiteSpace(choice.Source))
            throw new ArgumentException("Policy provenance is required");
        var profile = Model.Profiles.SingleOrDefault(p => p.Kind == choice.Kind);
        if (profile == null)
            throw new NotSupportedException("No mission adapter contract: " + choice.Kind);
        return choice.Goal == null ? profile.WithoutGoal : profile.WithGoal;
    }

    static string FirstEffect(string phase)
    {
        switch (phase)
        {
            case "Selecting":
                return "SelectNativeGoal";
            case "Revalidating":
                return "RevalidateGoal";
            case "Reacting":
                return "RunNativeReaction";
            case "Engaging":
                return "RunNativeCombat";
            case "Operating":
                return "RunNativeDirective";
            default:
                throw new NotSupportedException("No entry adapter: " + phase);
        }
    }

    public Decision Begin(PolicyChoice choice)
    {
        if (State.Authority != "Executive" || State.ActorDisposition != "Active")
            return Reject("native-or-retired-actor");
        string phase = Entry(choice);
        ValidateGoal(choice.Goal);
        if (State.Active != null && State.Active.Phase != "Completed")
            return Reject("active-mission-requires-explicit-preemption");
        if (State.Suspended.Count != 0 || State.PendingArrival != null)
            return Reject("unresolved-continuation");
        State.Active = Create(choice, phase);
        return Accept("mission-begin", "StopOwnedControls", FirstEffect(phase));
    }

    Mission Create(PolicyChoice choice, string phase) => new Mission
    {
        Id = ++State.NextMission,
        Attempt = 1,
        Kind = choice.Kind,
        Source = choice.Source,
        Goal = choice.Goal?.Copy(),
        Phase = phase
    };

    public Decision Preempt(PolicyChoice choice, bool authorizedByPolicy)
    {
        if (State.Authority != "Executive" || State.ActorDisposition != "Active")
            return Reject("native-or-retired-actor");
        string phase = Entry(choice);
        ValidateGoal(choice.Goal);
        if (!authorizedByPolicy)
            return Reject("policy-did-not-authorize-preemption");
        if (State.Active == null || State.Active.Phase == "Completed")
            return Reject("no-running-mission");
        if (State.PendingArrival != null)
            return Reject("irreversible-effect-boundary");
        if (State.Suspended.Count == Model.Limits.SuspendedMissions)
            return Reject("suspension-capacity");
        State.Suspended.Add(State.Active);
        State.Active = Create(choice, phase);
        return Accept("mission-preempt", "StopOwnedControls", FirstEffect(phase));
    }

    public Decision Replace(PolicyChoice choice, bool authorizedByPolicy)
    {
        if (State.Authority != "Executive" || State.ActorDisposition != "Active")
            return Reject("native-or-retired-actor");
        string phase = Entry(choice);
        ValidateGoal(choice.Goal);
        if (!authorizedByPolicy)
            return Reject("policy-did-not-authorize-replacement");
        if (State.PendingArrival != null)
            return Reject("irreversible-effect-boundary");
        State.Active = Create(choice, phase);
        return Accept("mission-replace", "StopOwnedControls", FirstEffect(phase));
    }

    public Decision Resume()
    {
        if (State.Authority != "Executive" || State.ActorDisposition != "Active")
            return Reject("native-or-retired-actor");
        if (State.Active?.Phase != "Completed" || State.Suspended.Count == 0 || State.PendingArrival != null)
            return Reject("no-resumable-continuation");
        State.Active = State.Suspended[State.Suspended.Count - 1];
        State.Suspended.RemoveAt(State.Suspended.Count - 1);
        State.Active.Attempt++;
        if (State.Active.Phase == "AwaitingPolicy" || State.Active.Kind != "Return" && State.Active.Kind != "Patrol")
        {
            State.Active.Phase = "AwaitingPolicy";
            return Accept("mission-resume-policy", "StopOwnedControls", "RequestNativePolicy");
        }
        State.Active.Phase = State.Active.Goal == null ? "Selecting" : "Revalidating";
        return Accept("mission-resume", "StopOwnedControls", State.Active.Goal == null ? "SelectNativeGoal" : "RevalidateGoal");
    }

    public Decision FinishByPolicy(bool authorizedByPolicy)
    {
        if (State.Authority != "Executive" || State.ActorDisposition != "Active" || State.Active == null || State.PendingArrival != null || !authorizedByPolicy)
            return Reject("policy-completion-refused");
        State.Active.Phase = "Completed";
        State.Active.FlightPayload = null;
        State.Active.Attempt++;
        return Accept("mission-complete-native-policy", "StopOwnedControls");
    }

    public Decision TransferForSupersedingDirective(bool authorizedByPolicy)
    {
        if (!authorizedByPolicy || State.Authority != "Executive" || State.ActorDisposition != "Active" || State.PendingArrival != null)
            return Reject("superseding-native-transfer-refused");
        State.Suspended.Clear();
        if (State.Active != null)
        {
            State.Active.Phase = "Completed";
            State.Active.Attempt++;
        }
        State.Authority = "Native";
        State.BindingRevision++;
        return Accept("superseding-native-transfer");
    }

    public void RetargetOrdinaryContinuation(GoalReference goal)
    {
        ValidateGoal(goal);
        if (State.Authority != "Executive" || State.PendingArrival != null || State.ActorDisposition != "Active")
            throw new InvalidOperationException("No ordinary goal authority");
        var frame = State.Suspended.LastOrDefault(m => m.Kind == "Patrol" || m.Kind == "Return");
        if (frame != null)
        {
            frame.Goal = goal.Copy();
            frame.Attempt++;
        }
    }

    public bool Matches(Ticket ticket) => State.Authority == "Executive" && ticket.Actor == State.ActorId && ticket.Activation == State.Activation && ticket.Binding == State.BindingRevision
        && ticket.Mission == (State.Active?.Id ?? 0) && ticket.Attempt == (State.Active?.Attempt ?? 0);

    public Decision Observe(Observation input)
    {
        if (input == null || !Matches(input.Ticket))
            return Reject("stale-observation");
        if (!Finite(input.Epoch) || input.Epoch < State.Epoch)
            return Reject("nonmonotonic-world-time");
        if (State.Active == null)
            return Reject("no-active-mission");
        var edge = Model.Find(State.Active.Phase, input.Event, State.Active.Goal?.Arrival);
        if (edge == null)
            return Reject("invalid-phase-event");
        ValidateGoal(input.Goal);
        if (edge.Operations.Contains("setGoal") && input.Goal == null)
            return Reject("missing-canonical-goal");
        if (edge.Operations.Contains("prepareArrival") && string.IsNullOrWhiteSpace(State.Active.Goal?.TargetId))
            return Reject("arrival-requires-named-destination");
        if (State.PendingArrival != null && input.Event != "DispatchArrival")
        {
            if (input.EffectId != State.PendingArrival.Id || input.Destination != State.PendingArrival.Destination)
                return Reject("wrong-world-effect");
        }
        if (input.Event == "WorldEffectObserved" && input.WorldProof != "actor-retired-with-crew-at-destination" && input.WorldProof != "actor-retired-with-all-crew-accounted" && input.WorldProof != "actor-attached-to-destination")
            return Reject("missing-world-readback");
        if (input.Event == "NoWorldEffectIssued" && input.WorldProof != "native-call-did-not-enter-world-effect")
            return Reject("absence-is-not-proof");
        foreach (string operation in edge.Operations)
        {
            switch (operation)
            {
                case "setGoal":
                    State.Active.Goal = input.Goal.Copy();
                    break;
                case "clearGoal":
                    State.Active.Goal = null;
                    break;
                case "prepareArrival":
                    State.PendingArrival = new ArrivalIntent { Id = ++State.NextEffect, Destination = State.Active.Goal.TargetId };
                    break;
                case "clearArrival":
                    State.PendingArrival = null;
                    break;
                case "complete":
                    State.Active.FlightPayload = null;
                    break;
                case "observeDisposition":
                    if (input.WorldProof == "actor-retired-with-crew-at-destination" || input.WorldProof == "actor-retired-with-all-crew-accounted")
                    {
                        State.ActorDisposition = "Retired";
                        State.Suspended.Clear();
                    }
                    break;
                default:
                    throw new InvalidOperationException("Unimplemented state operation: " + operation);
            }
        }
        if (State.Active.Phase != edge.To)
            State.Active.Attempt++;
        State.Active.Phase = edge.To;
        State.Epoch = input.Epoch;
        return Accept(edge.Id, edge.Effects);
    }

    public Decision Rebind(long revision)
    {
        if (State.Authority != "Executive" || State.ActorDisposition != "Active")
            return Reject("native-or-retired-actor");
        if (revision <= State.BindingRevision)
            return Reject("binding-revision-must-increase");
        if (State.PendingArrival != null)
            return Reject("resolve-world-effect-before-rebinding");
        State.BindingRevision = revision;
        if (State.Active == null || State.Active.Phase == "Completed")
            return Accept("actor-rebind", "StopOwnedControls");
        State.Active.Attempt++;
        State.Active.Phase = State.Active.Goal == null ? "Selecting" : "Revalidating";
        return Accept("actor-rebind", "StopOwnedControls", State.Active.Goal == null ? "SelectNativeGoal" : "RevalidateGoal");
    }

    public void RequestDisable() => State.DisableRequested = true;

    public bool SettleWorldTime(double epoch)
    {
        if (State.Authority != "Executive" || !Finite(epoch) || epoch < State.Epoch)
            return false;
        State.Epoch = epoch;
        return true;
    }

    // A native adapter must separately prove that the supported phase can be
    // exported. The reducer does not silently erase combat or pending effects.
    public bool MayOfferHandback => State.Authority == "Executive" && State.PendingArrival == null && State.Suspended.Count == 0;

    public Decision ReleaseToNative(bool projectionCertifiedByAdapter)
    {
        if (!State.DisableRequested || !MayOfferHandback || !projectionCertifiedByAdapter)
            return Reject("native-projection-not-certified");
        State.Authority = "Native";
        State.BindingRevision++;
        return Accept("authority-native");
    }

    public string Save() => JsonConvert.SerializeObject(State, Json);

    public static Machine Restore(Definition model, string json, string actor, string activation, string nativeBinding)
    {
        var loaded = JsonConvert.DeserializeObject<State>(json, Json);
        if (loaded == null || loaded.SchemaVersion != 1 || loaded.ModelId != model.Id || loaded.ModelHash != model.Fingerprint || loaded.ReducerBinding != typeof(Machine).Module.ModuleVersionId.ToString() || loaded.ActorId != actor || loaded.Activation != activation || loaded.NativeBinding != nativeBinding)
            throw new InvalidOperationException("Incompatible mission checkpoint");
        if (!Finite(loaded.Epoch) || loaded.BindingRevision < 1 || loaded.NextMission < 0 || loaded.NextEffect < 0 || loaded.Suspended == null || loaded.Suspended.Count > model.Limits.SuspendedMissions || loaded.Authority != "Executive" && loaded.Authority != "Native")
            throw new InvalidOperationException("Invalid bounded checkpoint state");
        var missions = loaded.Suspended.Concat(loaded.Active == null ? Array.Empty<Mission>() : new[] { loaded.Active }).ToArray();
        if (missions.Any(m => m == null || m.Id < 1 || m.Id > loaded.NextMission || m.Attempt < 1 || !model.Phases.Contains(m.Phase) || !model.Profiles.Any(p => p.Kind == m.Kind) || string.IsNullOrWhiteSpace(m.Source)) || missions.Select(m => m.Id).Distinct().Count() != missions.Length)
            throw new InvalidOperationException("Invalid mission identity or phase");
        foreach (var mission in missions)
            ValidateGoal(mission.Goal);
        bool awaiting = loaded.Active?.Phase == "ArrivalPrepared" || loaded.Active?.Phase == "ArrivalUncertain";
        if (awaiting != (loaded.PendingArrival != null) || loaded.PendingArrival != null && (loaded.PendingArrival.Id < 1 || loaded.PendingArrival.Id > loaded.NextEffect || loaded.PendingArrival.Destination != loaded.Active.Goal?.TargetId))
            throw new InvalidOperationException("Inconsistent pending world effect");
        if (loaded.Authority == "Native" && (loaded.PendingArrival != null || loaded.Suspended.Count != 0))
            throw new InvalidOperationException("Native authority contains unresolved executive state");
        if (loaded.ActorDisposition != "Active" && loaded.ActorDisposition != "Retired" || loaded.ActorDisposition == "Retired" && (loaded.Active?.Phase != "Completed" || loaded.Suspended.Count != 0 || loaded.PendingArrival != null))
            throw new InvalidOperationException("Invalid retired actor continuation");
        var machine = new Machine(model, actor, activation, nativeBinding, loaded.Epoch);
        machine.State.Active = loaded.Active;
        machine.State.Suspended = loaded.Suspended;
        machine.State.PendingArrival = loaded.PendingArrival;
        // Reconstructed native objects must not accept callbacks issued against
        // the preceding representation, including same-process reloads.
        machine.State.BindingRevision = loaded.BindingRevision + 1;
        machine.State.NextMission = loaded.NextMission;
        machine.State.NextEffect = loaded.NextEffect;
        machine.State.DisableRequested = loaded.DisableRequested;
        machine.State.Authority = loaded.Authority;
        machine.State.ActorDisposition = loaded.ActorDisposition;
        if (awaiting)
        {
            // A saved prepare can be from either side of an unrecorded native
            // invocation. Never infer that replay is safe from its label.
            machine.State.Active.Phase = "ArrivalUncertain";
            machine.State.Active.Attempt++;
        }
        return machine;
    }

    static void ValidateGoal(GoalReference goal)
    {
        if (goal == null)
            return;
        if ((goal.Arrival != "Dock" && goal.Arrival != "None") || !Finite(goal.X) || !Finite(goal.Y) || goal.TargetId == null && string.IsNullOrWhiteSpace(goal.FrameId))
            throw new ArgumentException("Invalid logical goal");
    }
}
