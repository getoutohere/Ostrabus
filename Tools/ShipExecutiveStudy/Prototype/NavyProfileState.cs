using System;
using System.Collections.Generic;
using MissionContracts;

namespace ShipExecutivePrototype;

sealed class NavyProfileState
{
    public int SchemaVersion = 2;
    public string NativeBinding;
    public string Blackboard;
    public string CombatMode;
    public string TacticalPhase = "Choose";
    public JsonShipSitu TacticalGoal;
    public string[] TacticalFlightPayload;
    public double ReactionRemaining = double.NegativeInfinity;
    public double PauseRemaining;
    public bool ReactionOriginalIgnoreGravity;
    public bool ReactionStarted;
    public string ReactionTarget;
    public float ReactionDirection;
    public long ActiveDirective;
    public long NextDirective;
    public long DepartureMission;
    public double MissileCheckInterval = 8;
    public GoalReference OrdinaryOverride;
    public string FailedCombatMode;
    public List<NativeDirective> Inbox = new List<NativeDirective>();
}

sealed class NativeDirective
{
    public long Id;
    public string Command, Origin, Counterparty, AffectedCharacter;
    public string[] Payload = Array.Empty<string>();
    public double ReceivedEpoch;
    public bool Once, Urgent;
    public string Phase = "Start";
    public GoalReference Goal;
    public string[] NativeState;
    public double PauseUntil, DockingRemaining, HoldUpdated;
    public bool ConstructorRan, OriginalIgnoreGravity;
    public string DeferredDodgeTarget;
    public bool ReadDodgeContextAtDispatch;
}
