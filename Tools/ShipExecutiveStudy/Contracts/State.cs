using System;
using System.Collections.Generic;

namespace MissionContracts;

public sealed class GoalReference
{
    public string TargetId;
    public string FrameId;
    public string Arrival = "Dock";
    public double X, Y;

    public GoalReference Copy() => new GoalReference { TargetId = TargetId, FrameId = FrameId, Arrival = Arrival, X = X, Y = Y };
}

public sealed class PolicyChoice
{
    public string Kind;
    public string Source;
    public GoalReference Goal;
}

public sealed class Mission
{
    public long Id, Attempt;
    public string Kind, Source, Phase;
    public GoalReference Goal;
    // Explicit adapter payload. No arbitrary CLR objects/private-field snapshot.
    public string[] FlightPayload;
}

public sealed class ArrivalIntent
{
    public long Id;
    public string Destination;
}

public sealed class State
{
    public int SchemaVersion = 1;
    public string ModelId, ModelHash, NativeBinding;
    public string ReducerBinding;
    public string ActorId, Activation;
    public long BindingRevision, NextMission, NextEffect;
    public double Epoch;
    public Mission Active;
    public List<Mission> Suspended = new List<Mission>();
    public ArrivalIntent PendingArrival;
    public bool DisableRequested;
    public string Authority = "Executive";
    public string ActorDisposition = "Active";
}

public readonly struct Ticket
{
    public readonly string Actor, Activation;
    public readonly long Binding, Mission, Attempt;
    public Ticket(State state)
    {
        Actor = state.ActorId;
        Activation = state.Activation;
        Binding = state.BindingRevision;
        Mission = state.Active?.Id ?? 0;
        Attempt = state.Active?.Attempt ?? 0;
    }
}

public sealed class Observation
{
    public Ticket Ticket;
    public string Event;
    public double Epoch;
    public GoalReference Goal;
    public long EffectId;
    public string Destination;
    // A producer must supply world readback, not merely a successful command code.
    public string WorldProof;
}

public sealed class Decision
{
    public bool Accepted;
    public string Reason, TransitionId;
    public string[] Effects = Array.Empty<string>();
}
