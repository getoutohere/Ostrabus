using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json;
using Ostrabus.ShipNavigation;
using Ostranauts.Core;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;

static partial class PatrolQueueChecks {
    sealed class FailedSave : BaseCommand {
        public override CommandStatus RunCommand(Blackboard board) => throw new InvalidOperationException("fixture-save-failure");
        public override List<JsonCommandPayload> GetSaveData(string path) => throw new InvalidOperationException("fixture-save-failure");
    }
    internal static void Interactions() {
        var h = new Harmony("offline.patrol-queue.interactions");
        try {
            foreach (var type in new[] { typeof(CombatCleanupAdmissionPatch), typeof(PatrolAdmissionPatch), typeof(PatrolRestorePatch), typeof(OneShotPersistencePatch), typeof(CommandQueueLoadPatch), typeof(CommandTreeSaveContextPatch) })
                h.CreateClassProcessor(type).Patch();
            CeresAuditChecks.Run(true, true);
        } finally { h.UnpatchSelf(); }
    }
    static StickySequenceCommand Normal(NavyPilot pilot) => (StickySequenceCommand)((List<ICommand>)AccessTools.Field(typeof(SelectorCommand), "_childCommands").GetValue(pilot.BehaviorTree)).Last();
    static List<ICommand> Children(StickySequenceCommand main) => (List<ICommand>)AccessTools.Field(typeof(StickySequenceCommand), "_childCommands").GetValue(main);
    static JsonCommandPayload[] Roundtrip(JsonCommandPayload[] payload) => JsonConvert.DeserializeObject<JsonCommandPayload[]>(JsonConvert.SerializeObject(payload));
    static FlyToPatrol Flight(NavyPilot pilot, string trip = "repair-trip") => new FlyToPatrol(pilot) { SaveData = new[] { "0.000000001", trip, "12", "0.25", "1" } };

    static void Production() {
        var production = new Harmony("offline.patrol-queue.production");
        try {
            foreach (var type in new[] { typeof(CombatCleanupAdmissionPatch), typeof(PatrolAdmissionPatch), typeof(PatrolRestorePatch), typeof(OneShotPersistencePatch), typeof(CommandQueueLoadPatch), typeof(CommandTreeSaveContextPatch) })
                production.CreateClassProcessor(type).Patch();

            foreach (bool inCombat in new[] { false, true }) foreach (bool isDamaged in new[] { false, true }) {
                var pilot = Pilot(); var ai = Controller(pilot); var command = Flight(pilot);
                var point = pilot.ShipUs.shipSituTarget; double fuel = pilot.ShipUs.fShallowRCSRemass;
                ai.AddCommand(command);
                Check(!pilot.PriorityQueue.CanExecute(pilot.Blackboard), "New Navy patrol still entered priority queue");
                Check(ReferenceEquals(Children(Normal(pilot))[2], command), "Admission copied/discarded flight command state");
                Check(ReferenceEquals(point, pilot.ShipUs.shipSituTarget) && fuel == pilot.ShipUs.fShallowRCSRemass, "Admission changed physical goal or fuel");
                combat = inCombat; damaged = isDamaged; RunTree(pilot);
                Check(calls.Contains("AmIInCombat") && calls.Contains("AreThreatsNearby"), "Patrol admission bypassed native emergency evaluation");
                Check(inCombat ? calls.Contains(isDamaged ? "SurrenderOffer" : "KeepWithinFiringDistance") && !calls.Contains("FlyToPatrol") : calls.Contains("FlyToPatrol"),
                    "Native combat/damage policy did not retain authority");
            }
            damaged = false; combat = true;
            var urgent = Pilot(); var urgentAI = Controller(urgent);
            urgentAI.AddUrgentCommand(new Dodge(urgent)); urgentAI.AddCommand(Flight(urgent)); RunTree(urgent);
            Check(calls.SequenceEqual(new[] { "Dodge" }), "Ordinary patrol preempted an urgent dodge");

            // Admission must not rewrite a different pilot's authored script.
            var pirate = new PiratePilot(null, Pilot().ShipUs); var pirateAI = Controller(pirate);
            pirateAI.AddCommandLoot("FlyToPatrol"); pirateAI.AddCommandLoot("LockPirate");
            Check(Waiting(pirate.PriorityQueue).Select(c => c.GetType().Name).SequenceEqual(new[] { "FlyToPatrol", "LockPirate" }), "Pirate approach/lock script was rewritten");

            // Saved old Main contains another trip. Migration must happen AFTER
            // Main.LoadSaveData or it overwrites the recovered trip/fuel ledger.
            foreach (bool current in new[] { false, true }) {
                var source = Pilot(); ((FlyToPatrol)Children(Normal(source))[2]).SaveData = Flight(source, "obsolete-main-trip").SaveData;
                var old = Flight(source, "queued-trip"); source.PriorityQueue.EnqueueCommand(old);
                if (current) { combat = false; source.PriorityQueue.RunCommand(source.Blackboard); }
                var saved = Roundtrip(Controller(source).GetBehaviorTreeSaveData());
                var loaded = Pilot(); Controller(loaded).LoadBehaviorTreeSave(saved);
                Check(!loaded.PriorityQueue.CanExecute(loaded.Blackboard), "Isolated legacy Navy patrol still masks combat");
                var restored = Children(Normal(loaded))[2];
                Check(restored.SaveData.SequenceEqual(old.SaveData) && Normal(loaded).SaveData[0] == "2", "Legacy migration lost route/fuel progress to old Main data");
                combat = true; RunTree(loaded);
                Check(calls.Contains("KeepWithinFiringDistance") && !calls.Contains("FlyToPatrol"), "Recovered Navy patrol still starves combat");
            }
            var mixed = Pilot(); mixed.PriorityQueue.EnqueueCommand(Flight(mixed)); mixed.PriorityQueue.EnqueueCommand(new Dodge(mixed) { SaveData = new[] { "dodge" } });
            var mixedLoad = Pilot(); Controller(mixedLoad).LoadBehaviorTreeSave(Roundtrip(Controller(mixed).GetBehaviorTreeSaveData()));
            Check(Waiting(mixedLoad.PriorityQueue).Select(c => c.GetType().Name).SequenceEqual(new[] { "FlyToPatrol", "Dodge" }), "Legacy recovery dismantled/reordered a mixed script");

            // General wrapper persistence: test other roles and multiple save
            // generations without interpreting unmarked legacy leaves as SFF.
            foreach (string name in new[] { "FlyTo", "FlyToPatrol", "Hold", "FlyToPath", "TurnOffAllActiveSensors" }) {
                var source = Pilot(); var leaf = AIShip.CreateCommand(name, source);
                Check(leaf != null, "Native command fixture could not be created: " + name);
                if (name != "TurnOffAllActiveSensors") leaf.SaveData = new[] { "0.000000001", "trip-fixture", "12", "0.25", "1" };
                string[] original = leaf.SaveData;
                source.PriorityQueue.EnqueueCommand(new OneShotWrapperCommand(leaf));
                for (int generation = 0; generation < 3; generation++) {
                    var saved = Roundtrip(Controller(source).GetBehaviorTreeSaveData());
                    var fallback = saved.Single(p => p.strPath == "/BehaviorTree/PriorityQueue/Queue/" + name);
                    Check((original ?? Array.Empty<string>()).SequenceEqual(fallback.aCommandPayload), "Mod marker contaminated vanilla leaf payload: " + name);
                    var loaded = Pilot(); Controller(loaded).LoadBehaviorTreeSave(saved);
                    Check(Waiting(loaded.PriorityQueue).Count == 1 && Waiting(loaded.PriorityQueue).Peek() is OneShotWrapperCommand, "SFF wrapper did not survive round trip for " + name);
                    var child = Waiting(loaded.PriorityQueue).Peek().FindCommand(leaf.GetType());
                    Check(child != null && (original == null ? child.SaveData == null : original.SequenceEqual(child.SaveData)), "SFF child state changed: " + name);
                    source = loaded;
                }
                if (name == "FlyToPatrol") {
                    combat = true; RunTree(source);
                    Check(calls.SequenceEqual(new[] { "FlyToPatrol" }) && !source.PriorityQueue.CanExecute(source.Blackboard), "Restored callback did not release queue after one invocation");
                    RunTree(source); Check(calls.Contains("KeepWithinFiringDistance"), "Combat did not resume after restored one-shot callback");
                }
            }

            // Save/load FIFO and current-command identity, with repeated types
            // and wrappers, must match the native saving traversal exactly.
            var order = Pilot();
            Set(order.PriorityQueue, "currentCommand", new OneShotWrapperCommand(Flight(order, "current-trip")));
            order.PriorityQueue.EnqueueCommand(new OneShotWrapperCommand(Flight(order, "first-trip")));
            order.PriorityQueue.EnqueueCommand(new OneShotWrapperCommand(Flight(order, "second-trip")));
            order.PriorityQueue.EnqueueCommand(new Dodge(order) { SaveData = new[] { "third" } });
            var orderSave = Roundtrip(Controller(order).GetBehaviorTreeSaveData());
            var orderLoad = Pilot(); Controller(orderLoad).LoadBehaviorTreeSave(orderSave);
            Check(Current(orderLoad.PriorityQueue) is OneShotWrapperCommand, "Saved current wrapper was discarded");
            Check(Current(orderLoad.PriorityQueue).FindCommand(typeof(FlyToPatrol)).SaveData[1] == "current-trip", "Current and waiting commands were exchanged");
            var ordered = Waiting(orderLoad.PriorityQueue).ToArray();
            Check(ordered.Length == 3 && ordered[0].FindCommand(typeof(FlyToPatrol)).SaveData[1] == "first-trip" &&
                ordered[1].FindCommand(typeof(FlyToPatrol)).SaveData[1] == "second-trip" && ordered[2] is Dodge, "Saved FIFO order was reversed or wrappers merged");

            var shared = Pilot(); var sharedLeaf = Children(Normal(shared))[2]; sharedLeaf.SaveData = Flight(shared, "shared-trip").SaveData;
            shared.PriorityQueue.EnqueueCommand(new OneShotWrapperCommand(sharedLeaf));
            var sharedSave = Roundtrip(Controller(shared).GetBehaviorTreeSaveData());
            Check(CommandQueuePersistence.Saving == null, "Tree save retained a live pilot context");
            Check(sharedSave.Any(p => p.strPath.EndsWith("/OstrabusOneShot") && p.aCommandPayload[3] == "/BehaviorTree/MainBehavior/FlyToPatrol"), "Shared native leaf identity was not saved");
            var sharedLoad = Pilot(); Controller(sharedLoad).LoadBehaviorTreeSave(sharedSave);
            Check(ReferenceEquals(Waiting(sharedLoad.PriorityQueue).Peek().FindCommand(typeof(FlyToPatrol)), Children(Normal(sharedLoad))[2]), "Callback restored a duplicate of the native Main flight");
            var failing = Controller(Pilot()); Set(failing, "_behaviorTree", new FailedSave());
            bool failed = false;
            try { failing.GetBehaviorTreeSaveData(); } catch (InvalidOperationException e) { failed = e.Message == "fixture-save-failure"; }
            Check(failed && CommandQueuePersistence.Saving == null, "Failed save swallowed its error or retained the temporary pilot context");

            // Removing the feature still leaves native-loadable commands and
            // ordinary Main progress; unknown metadata is silently ignored.
            production.UnpatchSelf();
            var withoutMod = Pilot(); Controller(withoutMod).LoadBehaviorTreeSave(orderSave);
            Check(Current(withoutMod.PriorityQueue) is FlyToPatrol && Waiting(withoutMod.PriorityQueue).Count == 3, "Vanilla could not load saved fallback commands without mod");
            foreach (var type in new[] { typeof(CombatCleanupAdmissionPatch), typeof(PatrolAdmissionPatch), typeof(PatrolRestorePatch), typeof(OneShotPersistencePatch), typeof(CommandQueueLoadPatch), typeof(CommandTreeSaveContextPatch) })
                production.CreateClassProcessor(type).Patch();

            // IL drift must reject the patch rather than guessing a new site.
            bool rejected = false;
            try { CommandQueueLoadPatch.Transpiler(new[] { new CodeInstruction(System.Reflection.Emit.OpCodes.Ret) }, AccessTools.Method(typeof(QueuedCommandsNode), nameof(QueuedCommandsNode.LoadSaveData))).ToArray(); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Changed queue-loader IL was silently accepted");
            Console.WriteLine("Production: interruptible Navy patrol admission/reload, scripted/urgent preservation, one-shot continuation state, FIFO and mod-removal fallback verified.");
        } finally { production.UnpatchSelf(); }
    }
}
