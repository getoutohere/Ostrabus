using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using HarmonyLib;
using Newtonsoft.Json;
using Ostranauts.Core;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.AIPilots.Interfaces;
using Ostranauts.Ships.Commands;
using Ostranauts.Ships.Comms;

// Characterize the shipped runtime only. No Ostrabus assembly is referenced or
// loaded. Harmony fixtures stand in for Unity identity/captain/asset services.
static class Program
{
    static int checks;
    static readonly List<object> evidence = new List<object>();
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Field(object target, string name, object value) => AccessTools.Field(target.GetType(), name).SetValue(target, value);
    static void Check(bool value, string reason) { checks++; if (!value) throw new Exception(reason); }
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = ReferenceEquals(__0, __1); return false; }
    static bool Unequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = !ReferenceEquals(__0, __1); return false; }
    static bool Quiet() => false;
    static bool Owner(ref string __result) { __result = "fixture-faction"; return false; }
    sealed class Probe : BaseCommand
    {
        internal bool Enabled = true;
        internal CommandCode Result = CommandCode.Running;
        internal int Runs, Exits;
        internal Probe(string name) { _nodeName = name; }
        public override bool CanExecute(Blackboard board) => Enabled;
        public override CommandStatus RunCommand(Blackboard board) { Runs++; return new CommandStatus(Result, this); }
        public override void OnExit(Blackboard board) { Exits++; }
    }
    sealed class Pilot : IAICharacter
    {
        public Ship ShipUs { get; set; }
        public CondOwner CaptainUs => null;
        public QueuedCommandsNode PriorityQueue { get; set; }
        public ICommand BehaviorTree { get; set; }
        public Blackboard Blackboard { get; set; } = new Blackboard();
        public AIType AIType => AIType.Navy;
        public TargetData GetTarget() => null;
        public TargetData GetTargetHome() => null;
        public double MaxSpeed(bool? atmo = null) => 1e-9;
        public ICommand FFWD(ICommand last, Blackboard board) => last;
    }
    static Ship Ship(string id) { var ship = Empty<Ship>(); ship.strRegID = id; Field(ship, "<objSS>k__BackingField", new ShipSitu(false)); Field(ship, "<Comms>k__BackingField", Empty<Comms>()); return ship; }
    static AIShip Controller(IAICharacter pilot) { var ai = Empty<AIShip>(); Field(ai, "_shipUs", pilot.ShipUs); Field(ai, "_aiCharacter", pilot); Field(ai, "_behaviorTree", pilot.BehaviorTree); Field(ai, "<Blackboard>k__BackingField", pilot.Blackboard); return ai; }
    static object Tree(ICommand command, int depth = 0)
    {
        if (depth > 40) throw new Exception("Unexpected deep native tree");
        var field = AccessTools.Field(command.GetType(), "_childCommands");
        var children = field?.GetValue(command) as List<ICommand>;
        return new { type = command.GetType().Name, name = AccessTools.Field(typeof(BaseCommand), "_nodeName").GetValue(command), children = children?.Select(c => Tree(c, depth + 1)).ToArray() };
    }
    static void Main()
    {
        Directory.CreateDirectory("obj/ShipExecutiveStudy");
        var h = new Harmony("offline.executive-study.boundaries");
        try
        {
            h.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Equality"), prefix: new HarmonyMethod(typeof(Program), nameof(Equal)));
            h.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Inequality"), prefix: new HarmonyMethod(typeof(Program), nameof(Unequal)));
            h.Patch(AccessTools.Method(typeof(Comms), nameof(Comms.GetCaptain)), prefix: new HarmonyMethod(typeof(Program), nameof(Quiet)));
            h.Patch(AccessTools.Method(typeof(DataHandler), nameof(DataHandler.GetCondTrigger), new[] { typeof(string) }), prefix: new HarmonyMethod(typeof(Program), nameof(Quiet)));
            h.Patch(AccessTools.Method(typeof(StarSystem), nameof(StarSystem.GetShipOwner)), prefix: new HarmonyMethod(typeof(Program), nameof(Owner)));
            CrewSim.system = Empty<StarSystem>();
            var board = new Blackboard(); var urgent = new Probe("emergency") { Enabled = false }; var ordinary = new Probe("patrol");
            var selector = new SelectorCommand("root", urgent, ordinary);
            Check(ReferenceEquals(selector.RunCommand(board).ActiveCommand, ordinary), "Ordinary branch not selected");
            urgent.Enabled = true;
            Check(ReferenceEquals(selector.RunCommand(board).ActiveCommand, urgent), "Native selector failed to preempt");
            Check(ordinary.Exits == 0, "Native selector unexpectedly exited interrupted leaf");
            Check(selector.SaveData[0] == "-1", "Selector unexpectedly recorded running child");
            evidence.Add(new { probe = "selector-preemption", ordinaryRuns = ordinary.Runs, emergencyRuns = urgent.Runs, interruptedExits = ordinary.Exits, savedChild = selector.SaveData[0], meaning = "Native priority selection preempts without a cancellation callback; saved selector index does not identify current leaf." });

            var first = new Probe("refresh") { Result = CommandCode.Finished }; var work = new Probe("work");
            var reactive = new SequenceCommand("reactive", new ICommand[] { first, work });
            reactive.RunCommand(board); reactive.RunCommand(board);
            Check(first.Runs == 2 && work.Runs == 2, "Reactive sequence semantics changed");
            var falseGate = new Probe("not-executable") { Enabled = false }; var afterGate = new Probe("after") { Result = CommandCode.Finished };
            var skip = new SequenceCommand("skip", new ICommand[] { falseGate, afterGate });
            Check(skip.RunCommand(board).Status == CommandCode.Finished && afterGate.Runs == 1, "CanExecute false did not mean skip");
            evidence.Add(new { probe = "reactive-sequence", firstRuns = first.Runs, workRuns = work.Runs, disabledChildRuns = falseGate.Runs, followingRuns = afterGate.Runs, meaning = "Sequence re-evaluates earlier children every tick; CanExecute=false skips rather than fails the sequence." });

            var approach = new Probe("approach") { Result = CommandCode.Failure }; var dock = new Probe("dock");
            var sticky = new StickySequenceCommand("return", new ICommand[] { approach, dock });
            Check(sticky.RunCommand(board).Status == CommandCode.Running && dock.Runs == 1, "Native failed approach did not fall through to docking");
            Check(approach.Exits == 0, "Failed native sticky child unexpectedly exited");
            evidence.Add(new { probe = "sticky-failure", approachRuns = approach.Runs, dockRuns = dock.Runs, approachExits = approach.Exits, phase = sticky.SaveData[0], meaning = "Failure advances to the next child in the same dispatch; a new executive needs explicit failure edges." });

            var pilot = new Pilot { ShipUs = Ship("study") }; pilot.PriorityQueue = new QueuedCommandsNode("PriorityQueue", pilot);
            var a = Ship("A"); var b = Ship("B");
            var missionA = new FlyTo(pilot, a);
            Check(ReferenceEquals(missionA.ShipUs.shipSituTarget, a.objSS), "First native goal not installed");
            var missionB = new FlyTo(pilot, b);
            Check(ReferenceEquals(missionA.ShipUs.shipSituTarget, b.objSS) && ReferenceEquals(missionA.ShipUs.shipCombatTarget, b), "Native flight retained a private destination unexpectedly");
            evidence.Add(new { probe = "goal-ownership", missionAObserves = missionA.ShipUs.shipScanTarget.strRegID, meaning = "Native FlyTo commands share ship-global destination/combat fields. Suspending a command object does not preserve its mission target." });

            pilot.PriorityQueue.EnqueueCommand(new FlyToPath(pilot) { SaveData = new[] { "1" } });
            pilot.PriorityQueue.EnqueueCommand(new FlyToManual(pilot) { SaveData = new[] { "2" } });
            var saved = pilot.PriorityQueue.GetSaveData("/root").ToArray();
            var loaded = new QueuedCommandsNode("PriorityQueue", pilot); loaded.LoadSaveData("/root", saved);
            var waiting = (Queue<ICommand>)AccessTools.Field(typeof(QueuedCommandsNode), "commandQueue").GetValue(loaded);
            Check(waiting.Peek() is FlyToManual, "Native saved FIFO did not reverse");
            evidence.Add(new { probe = "queue-order", restored = waiting.Select(c => c.GetType().Name).ToArray(), meaning = "Native loader reverses saved waiting commands; known existing patch repairs this." });

            var lostBoard = new Blackboard(); lostBoard.Set("EmergencySignal", new Ostranauts.Core.Models.Tuple<double, ShipSitu>(123, new ShipSitu(false))); lostBoard.Set("empty", new List<Ship>()); lostBoard.Set("phase", 2);
            var serialized = lostBoard.GetJson(); var restoredBoard = new Blackboard(); restoredBoard.LoadSavedData(serialized);
            Check(!restoredBoard.HasKey("EmergencySignal") && !restoredBoard.HasKey("empty") && restoredBoard.Get<int>("phase") == 2, "Blackboard unsupported/empty behavior differs");
            var retreat = new LowFuelRetreat(pilot); Field(retreat, "_retreating", true); Field(retreat, "_targetSelected", true); Field(retreat, "_docking", true);
            Check(retreat.GetSaveData("/root") == null, "Nested retreat stage unexpectedly serialized");
            evidence.Add(new { probe = "persisted-state", emergencyTuplePreserved = restoredBoard.HasKey("EmergencySignal"), emptyListPreserved = restoredBoard.HasKey("empty"), primitivePhase = restoredBoard.Get<int>("phase"), nestedRetreatHasPayload = retreat.GetSaveData("/root") != null, meaning = "Blackboard is not a general durable mission state store; nested leaf state needs explicit capture/reconstruction." });

            var navy = new NavyPilot(null, Ship("native-navy"));
            File.WriteAllText("obj/ShipExecutiveStudy/native-navy-tree.json", JsonConvert.SerializeObject(Tree(navy.BehaviorTree), Formatting.Indented));
            var root = (List<ICommand>)AccessTools.Field(typeof(SelectorCommand), "_childCommands").GetValue(navy.BehaviorTree);
            Check(root.Count == 5 && ReferenceEquals(root[0], navy.PriorityQueue), "Navy root shape differs");
            Check(root.Last() is StickySequenceCommand, "Native ordinary mission isn't sticky");
            evidence.Add(new { probe = "native-role-policy", root = root.Select(c => new { type = c.GetType().Name, name = AccessTools.Field(typeof(BaseCommand), "_nodeName").GetValue(c) }).ToArray(), meaning = "Native role tree construction can remain the source of ordered choices; composites and nested action controllers require explicit adapter semantics." });
            var nativeAI = Controller(navy); var flight = navy.BehaviorTree.FindCommand(typeof(FlyToPatrol)); Field(nativeAI, "_activeCommand", flight); nativeAI.FFWD();
            var callbacks = (Queue<ICommand>)AccessTools.Field(typeof(QueuedCommandsNode), "commandQueue").GetValue(navy.PriorityQueue);
            Check(callbacks.Count == 1 && callbacks.Peek() is OneShotWrapperCommand, "Native Navy SFF did not schedule continuation");
            evidence.Add(new { probe = "native-sff", queued = callbacks.Peek().GetType().Name, meaning = "Native Navy fast-forward schedules later work; it is not a synchronous mission advancement interface." });

            File.WriteAllText("obj/ShipExecutiveStudy/native-probes.json", JsonConvert.SerializeObject(new { checks, productionPatchesEnabled = false, fixtures = "Unity object equality, captain, trigger lookup and ship owner; controlled probe leaves characterize combinator semantics.", evidence }, Formatting.Indented));
            Console.WriteLine(checks + " shipped-assembly characterization checks passed. No Ostrabus production patch enabled.");
            foreach (object entry in evidence) Console.WriteLine(JsonConvert.SerializeObject(entry));
        }
        finally { h.UnpatchSelf(); }
    }
}
