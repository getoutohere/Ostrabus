using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json;
using Ostranauts.Core;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;

namespace ShipExecutivePrototype;

sealed class Goal
{
    public string ShipId;
    public JsonShipSitu Situation;
    internal static Goal Capture(Ship ship) => new Goal { ShipId = ship.shipScanTarget?.strRegID, Situation = ship.shipSituTarget?.GetJSON() };
    internal bool Apply(Ship ship, NativeWorld world)
    {
        if (ShipId != null)
        {
            Ship target = world.Resolve(ShipId);
            if (target == null || target.bDestroyed || target.HideFromSystem)
            {
                ship.ClearShipTarget();
                return false;
            }
            new TargetData(target).ApplyToShip(ship);
        }
        else if (Situation != null)
            new TargetData(new ShipSitu(Situation)).ApplyToShip(ship);
        else
            ship.ClearShipTarget();
        return true;
    }
}

sealed class DirectiveState
{
    public string Command;
    public string GraphId;
    public bool Once;
}

sealed class ExecutiveState
{
    public int Version = 1;
    public string Binding;
    public string Actor;
    public long Generation;
    public long DirectiveSerial;
    public string Family;
    public string ActiveNode;
    public string ActiveCommandId;
    public string PendingEffect;
    public Goal PendingGoal;
    public Dictionary<string, Goal> Goals = new Dictionary<string, Goal>();
    public Dictionary<string, int> Cursors = new Dictionary<string, int>();
    public Dictionary<string, CommandState> Commands;
    public List<DirectiveState> Directives = new List<DirectiveState>();
    public Dictionary<string, BlackboardValue> Blackboard;
}

readonly struct Step
{
    internal readonly CommandCode Code;
    internal Step(CommandCode code)
    {
        Code = code;
    }
}

sealed class Executive
{
    internal readonly NativeWorld World;
    internal readonly NavyPilot Pilot;
    internal readonly AIShip Controller;
    internal readonly Policy Policy;
    internal readonly ExecutiveState State;
    readonly Queue<DirectiveState> directives = new Queue<DirectiveState>();
    internal string LastReason;
    internal int NativeCalls;
    internal const string BindingVersion = "native-navy-1.1.0.0-prototype-v2";
    internal static readonly string RuntimeBinding = BindingVersion + "/" + typeof(NavyPilot).Module.ModuleVersionId + "/" + typeof(Ostrabus.OstrabusPlugin).Module.ModuleVersionId;

    static readonly HashSet<string> Guards = new HashSet<string> {
        "AmIInCombat", "AreThreatsNearby", "IsTargetNeutralized", "AmIDamaged", "DoIHaveDecoys",
        "AreMissilesComingMyWay", "AmIArmed", "DoIPreferShortRange", "IsFriendlyUnderAttack", "AmIBeingTargeted"
    };

    internal Executive(NativeWorld world, NavyPilot pilot, AIShip controller, long generation = 1)
    {
        World = world;
        Pilot = pilot;
        Controller = controller;
        Policy = new Policy(pilot.BehaviorTree);
        State = new ExecutiveState { Binding = RuntimeBinding, Actor = pilot.ShipUs.strRegID, Generation = generation };
        State.Goals["ordinary"] = Goal.Capture(pilot.ShipUs);
    }

    internal CommandCode Tick()
    {
        if (World.Retired)
        {
            LastReason = "native arrival acknowledged";
            return CommandCode.Finished;
        }
        if (!World.IsCanonical(Pilot.ShipUs, Controller))
        {
            LastReason = "stale actor binding";
            return CommandCode.Failure;
        }
        if (State.PendingEffect != null)
        {
            LastReason = "awaiting native arrival acknowledgement";
            return CommandCode.Running;
        }
        if (State.Family != null && State.Goals.TryGetValue(State.Family, out var bound) && bound.ShipId != null)
        {
            if (!bound.Apply(Pilot.ShipUs, World))
            {
                World.StopControls();
                State.Goals.Remove(State.Family);
                State.Generation++;
                State.Family = null;
                Policy.Root.Children.Last().Cursor = 1;
                LastReason = "named target became unavailable; policy must choose again";
                return CommandCode.Running;
            }
        }
        Step result;
        if (directives.Count > 0)
        {
            var directive = directives.Peek();
            Switch("directive");
            ICommand command = Policy.Commands.ById[directive.GraphId];
            NativeCalls++;
            var status = command.RunCommand(Pilot.Blackboard);
            if (directive.Once || status.Status != CommandCode.Running)
            {
                directives.Dequeue();
                command.OnExit(Pilot.Blackboard);
                if (!directives.Any(d => d.GraphId == directive.GraphId))
                    Policy.Commands.ForgetTransient(directive.GraphId);
                World.StopControls();
                State.Family = null;
                LastReason = "directive completed";
            }
            State.ActiveNode = directive.GraphId;
            SetActive(status.ActiveCommand ?? command);
            return directive.Once ? CommandCode.Finished : status.Status;
        }
        result = Run(Policy.Root);
        if (World.Retired)
        {
            World.StopControls();
            State.ActiveNode = null;
            LastReason = "native arrival acknowledged";
        }
        return result.Code;
    }

    Step Run(PolicyNode node)
    {
        if (node.Kind == NodeKind.InputQueue)
            return new Step(CommandCode.Failure);
        if (node.Kind == NodeKind.Leaf)
            return Leaf(node);
        if (node.Kind == NodeKind.Selector)
        {
            foreach (var child in node.Children)
            {
                var result = Run(child);
                if (result.Code != CommandCode.Failure)
                    return result;
            }
            return new Step(CommandCode.Failure);
        }
        if (node.Kind == NodeKind.ReactiveSequence)
        {
            bool success = false;
            foreach (var child in node.Children)
            {
                var result = Run(child);
                if (result.Code == CommandCode.Running || result.Code == CommandCode.Failure)
                    return result;
                success = true;
            }
            return new Step(success ? CommandCode.Finished : CommandCode.Failure);
        }
        while (node.Cursor < node.Children.Length)
        {
            var child = node.Children[node.Cursor];
            var result = Run(child);
            if (result.Code == CommandCode.Running)
                return result;
            if (result.Code == CommandCode.Failure && (child.Native is FlyTo || child.Native is Dock))
            {
                if (child.Native is Dock)
                    node.Cursor = Math.Max(0, node.Cursor - 1);
                LastReason = "failed ability requires another approach";
                return new Step(CommandCode.Running);
            }
            node.Cursor++;
            if (result.Code == CommandCode.Finished)
            {
                if (node.Cursor >= node.Children.Length)
                    node.Cursor = 0;
                return result;
            }
        }
        node.Cursor = 0;
        return new Step(CommandCode.Failure);
    }

    Step Leaf(PolicyNode node)
    {
        ICommand command = node.Native;
        bool guard = Guards.Contains(command.GetType().Name);
        if (!guard)
            Switch(node.Id.StartsWith("root/4:", StringComparison.Ordinal) || node.Id.StartsWith("root/3:", StringComparison.Ordinal) ? "ordinary" : "combat");
        if (!command.CanExecute(Pilot.Blackboard))
        {
            if (command is Dock && Pilot.ShipUs.shipScanTarget != null)
                return new Step(CommandCode.Failure);
            return new Step(CommandCode.Finished); // native authored skip
        }
        Goal before = Goal.Capture(Pilot.ShipUs);
        int arrivals = World.ArrivalCalls;
        NativeCalls++;
        var status = command.RunCommand(Pilot.Blackboard);
        State.ActiveNode = node.Id;
        SetActive(status.ActiveCommand ?? command);
        if (!guard)
            State.Goals[State.Family] = Goal.Capture(Pilot.ShipUs);
        if (status.Status == CommandCode.Finished && (command is TargetNearestStation || command is TargetEscapeStation || command is TargetATC || command is TargetEmergencySignal))
            State.Goals["ordinary"] = Goal.Capture(Pilot.ShipUs); // authored replacement, not a suspended patrol resume
        if (World.ArrivalCalls != arrivals && !World.Retired && !Pilot.ShipUs.IsDocked())
        {
            State.PendingEffect = node.Id;
            State.PendingGoal = before;
            LastReason = "awaiting native arrival acknowledgement";
            return new Step(CommandCode.Running);
        }
        LastReason = command.GetType().Name + ": " + status.Status;
        return new Step(status.Status);
    }

    void Switch(string family)
    {
        if (State.Family == family)
            return;
        if (State.Family != null)
            State.Goals[State.Family] = Goal.Capture(Pilot.ShipUs);
        World.StopControls();
        State.Generation++;
        State.Family = family;
        if (State.Goals.TryGetValue(family, out var goal))
            goal.Apply(Pilot.ShipUs, World);
        else
            Pilot.ShipUs.ClearShipTarget();
    }

    void SetActive(ICommand command)
    {
        State.ActiveCommandId = command != null && Policy.Commands.Ids.TryGetValue(command, out var id) ? id : null;
        AccessTools.Field(typeof(AIShip), "_activeCommand").SetValue(Controller, command);
    }

    internal bool AcceptCompletion(long generation) => generation == State.Generation && World.IsCanonical(Pilot.ShipUs, Controller);

    internal void Admit(ICommand command, bool once = false)
    {
        if (command is FlyToPatrol && !once)
        {
            State.Goals["ordinary"] = Goal.Capture(Pilot.ShipUs);
            var main = Policy.Root.Children.Last();
            main.Cursor = 2;
            LastReason = "ordinary patrol goal admitted";
            return;
        }
        if (!(command is TurnOnAllSensors) && !(command is TurnOffAllActiveSensors) && !(command is Dodge))
            throw new NotSupportedException("Directive has no validated offline adapter: " + command?.GetType().FullName);
        if (directives.Count >= 16)
            throw new InvalidOperationException("Prototype directive capacity reached");
        string id = Policy.Commands.Ids.TryGetValue(command, out var existing) ? existing : "directive/" + ++State.DirectiveSerial;
        Policy.Commands.Register(command, id);
        directives.Enqueue(new DirectiveState { Command = command.GetType().Name, GraphId = id, Once = once });
    }

    internal bool Advance(double seconds, string source)
    {
        if (seconds < 0 || !double.IsFinite(seconds))
            throw new ArgumentOutOfRangeException(nameof(seconds));
        double end = StarSystem.fEpoch + seconds;
        int work = 0;
        while (StarSystem.fEpoch < end && !World.Retired)
        {
            if (++work > 128)
                throw new InvalidOperationException("Prototype advance budget reached");
            if (State.Family == "combat" && !IsRetreating())
            {
                LastReason = "bulk combat requires native world integration";
                return false;
            }
            Tick();
            if (State.PendingEffect != null)
                return false;
            var route = Pilot.ShipUs.objSS.NavData;
            if (route == null)
            {
                if (World.Retired)
                    break;
                if (World.PilotAvailable)
                {
                    LastReason = "no supported trajectory";
                    return false;
                }
                World.StopControls();
                World.Coast(end - StarSystem.fEpoch);
                StarSystem.fEpoch = end;
                break;
            }
            double boundary = Math.Min(end, route.GetArrivalEpoch());
            if (boundary <= StarSystem.fEpoch)
            {
                var result = Tick();
                if (result == CommandCode.Running && ReferenceEquals(route, Pilot.ShipUs.objSS.NavData) && !World.Retired)
                    throw new InvalidOperationException("No phase progress at trajectory boundary");
                continue;
            }
            StarSystem.fEpoch = boundary;
            route.TimeAdvance(Pilot.ShipUs.objSS); // shipped playback and native fuel settlement
            for (int i = 0; i < 16 && !World.Retired; i++)
                if (Tick() != CommandCode.Finished)
                    break;
        }
        if (World.Retired)
            StarSystem.fEpoch = end;
        return true;
    }

    internal string Save()
    {
        State.Cursors = Policy.Nodes.ToDictionary(p => p.Key, p => p.Value.Cursor);
        State.Commands = Policy.Commands.Capture();
        State.Directives = directives.ToList();
        State.Blackboard = BlackboardCodec.Capture(Pilot.Blackboard);
        return JsonConvert.SerializeObject(State, CheckpointJson.Settings);
    }

    internal void Load(string json)
    {
        var loaded = JsonConvert.DeserializeObject<ExecutiveState>(json, CheckpointJson.Settings);
        if (loaded.Version != 1 || loaded.Binding != RuntimeBinding || loaded.Actor != State.Actor)
            throw new InvalidOperationException("Incompatible executive checkpoint");
        foreach (var directive in loaded.Directives)
        {
            if (!Policy.Commands.ById.ContainsKey(directive.GraphId))
            {
                var native = AIShip.CreateCommand(directive.Command, Pilot);
                if (native == null)
                    throw new InvalidOperationException("Unsupported saved directive");
                Policy.Commands.Register(native, directive.GraphId);
            }
        }
        Policy.Commands.Restore(loaded.Commands);
        foreach (var pair in loaded.Cursors)
            Policy.Nodes[pair.Key].Cursor = pair.Value;
        State.Generation = loaded.Generation;
        State.Family = loaded.Family;
        State.ActiveNode = loaded.ActiveNode;
        State.ActiveCommandId = loaded.ActiveCommandId;
        State.DirectiveSerial = loaded.DirectiveSerial;
        State.Goals = loaded.Goals;
        State.PendingEffect = loaded.PendingEffect;
        State.PendingGoal = loaded.PendingGoal;
        directives.Clear();
        foreach (var directive in loaded.Directives)
            directives.Enqueue(directive);
        BlackboardCodec.Restore(Pilot.Blackboard, loaded.Blackboard, World);
        SetActive(State.ActiveCommandId == null ? null : Policy.Commands.ById[State.ActiveCommandId]);
        if (State.Family != null && State.Goals.TryGetValue(State.Family, out var goal))
            goal.Apply(Pilot.ShipUs, World);
    }

    internal bool CanHandBack(out string reason)
    {
        if (State.PendingEffect != null)
        {
            reason = "native arrival effect is unresolved";
            return false;
        }
        if (directives.Count != 0)
        {
            reason = "native queue persistence cannot certify directive identity and one-shot markers";
            return false;
        }
        if (IsRetreating())
        {
            reason = "nested retreat stage has no lossless native save representation";
            return false;
        }
        reason = null;
        return true;
    }

    internal JsonCommandPayload[] ExportNative()
    {
        if (!CanHandBack(out var reason))
            throw new InvalidOperationException(reason);
        Policy.ExportCursors();
        return Controller.GetBehaviorTreeSaveData();
    }

    bool IsRetreating() => Policy.Commands.ById.Values.OfType<LowFuelRetreat>().Any(r => (bool)AccessTools.Field(typeof(LowFuelRetreat), "_retreating").GetValue(r));
}
