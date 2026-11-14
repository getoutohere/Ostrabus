using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ostranauts.Core;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.AIPilots.Interfaces;
using Ostranauts.Ships.Commands;
using Ostranauts.Ships.Comms;

// Investigation only: the native selector, queue, wrapper and save loader run
// unmodified. World-dependent leaves receive controlled outcomes under Mono.
static partial class PatrolQueueChecks {
    static int checks;
    static bool combat, damaged;
    static CommandCode patrolResult = CommandCode.Running;
    static readonly List<string> calls = new List<string>();
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Set(object target, string field, object value) => AccessTools.Field(target.GetType(), field).SetValue(target, value);
    static void Check(bool result, string message) { checks++; if (!result) throw new Exception(message); }
    static bool Quiet() => false;
    static bool Owner(ref string __result) { __result = "fixture-faction"; return false; }
    static bool Leaf(BaseCommand __instance, ref CommandStatus __result) {
        string name = __instance.GetType().Name;
        calls.Add(name);
        var code = CommandCode.Finished;
        if (name == "AmIInCombat" || name == "AreThreatsNearby") code = combat ? CommandCode.Finished : CommandCode.Failure;
        else if (name == "AmIDamaged") code = damaged ? CommandCode.Finished : CommandCode.Failure;
        else if (new[] { "IsTargetNeutralized", "DoIHaveDecoys", "SurrenderDemand", "IsFriendlyUnderAttack" }.Contains(name)) code = CommandCode.Failure;
        else if (name == "FlyToPatrol") code = patrolResult;
        else if (name == "SurrenderOffer" || name == "KeepWithinFiringDistance" || name == "KeepAtDistance" || name == "Dodge") code = CommandCode.Running;
        __result = new CommandStatus(code, __instance);
        return false;
    }
    static NavyPilot Pilot() {
        var ship = Empty<Ship>(); ship.strRegID = "queue-fixture";
        Set(ship, "<objSS>k__BackingField", new ShipSitu(false));
        ship.shipSituTarget = new ShipSitu(false) { vPosx = .001 };
        Set(ship, "<Comms>k__BackingField", Empty<Comms>());
        var pilot = new NavyPilot(null, ship);
        var root = (SelectorCommand)pilot.BehaviorTree;
        var branches = (List<ICommand>)AccessTools.Field(typeof(SelectorCommand), "_childCommands").GetValue(root);
        ((StickySequenceCommand)branches.Last()).SaveData = new[] { "2" };
        return pilot;
    }
    static AIShip Controller(IAICharacter pilot) {
        var ai = Empty<AIShip>();
        Set(ai, "_shipUs", pilot.ShipUs); Set(ai, "_aiCharacter", pilot);
        Set(ai, "_behaviorTree", pilot.BehaviorTree); Set(ai, "<Blackboard>k__BackingField", pilot.Blackboard);
        return ai;
    }
    static Queue<ICommand> Waiting(QueuedCommandsNode queue) => (Queue<ICommand>)AccessTools.Field(typeof(QueuedCommandsNode), "commandQueue").GetValue(queue);
    static ICommand Current(QueuedCommandsNode queue) => (ICommand)AccessTools.Field(typeof(QueuedCommandsNode), "currentCommand").GetValue(queue);
    static void RunTree(NavyPilot pilot) { calls.Clear(); Controller(pilot).RunAI(); }
    internal static void Run(string[] args) {
        var h = new Harmony("offline.patrol-queue.diagnosis");
        try {
            CrewSim.system = Empty<StarSystem>();
            h.Patch(AccessTools.Method(typeof(StarSystem), nameof(StarSystem.GetShipOwner)), prefix: new HarmonyMethod(typeof(PatrolQueueChecks), nameof(Owner)));
            h.Patch(AccessTools.Method(typeof(Comms), nameof(Comms.GetCaptain)), prefix: new HarmonyMethod(typeof(PatrolQueueChecks), nameof(Quiet)));
            h.Patch(AccessTools.Method(typeof(DataHandler), nameof(DataHandler.GetCondTrigger), new[] { typeof(string) }), prefix: new HarmonyMethod(typeof(PatrolQueueChecks), nameof(Quiet)));
            foreach (string name in new[] { "AmIInCombat", "AreThreatsNearby", "TurnOnAllSensors", "IsTargetNeutralized", "AmIDamaged", "DoIHaveDecoys",
                "SurrenderDemand", "SurrenderOffer", "AmIArmed", "SetEngagementProfile", "DoIPreferShortRange", "KeepWithinFiringDistance", "KeepAtDistance", "IsFriendlyUnderAttack", "FlyToPatrol", "Dodge" })
                h.Patch(AccessTools.DeclaredMethod(AccessTools.TypeByName("Ostranauts.Ships.Commands." + name), "RunCommand"), prefix: new HarmonyMethod(typeof(PatrolQueueChecks), nameof(Leaf)));

            var ordinary = Pilot(); combat = false; RunTree(ordinary);
            Check(calls.Contains("AmIInCombat") && calls.Contains("AreThreatsNearby") && calls.Contains("FlyToPatrol"), "Normal patrol did not visit emergency gates before flying");
            combat = true; RunTree(ordinary);
            Check(calls.Contains("KeepWithinFiringDistance") && !calls.Contains("FlyToPatrol"), "Native ordinary patrol failed to yield to combat");
            Console.WriteLine("Normal MainBehavior patrol: emergency gates run, combat interrupts immediately.");

            var explicitQueue = Pilot(); Controller(explicitQueue).AddCommandLoot("FlyToPatrol"); RunTree(explicitQueue);
            Check(calls.SequenceEqual(new[] { "FlyToPatrol" }), "Queued patrol did not exclusively own native selector");
            Check(Current(explicitQueue.PriorityQueue) is FlyToPatrol, "Queued patrol did not remain current");
            Console.WriteLine("Native AddCommandLoot(FlyToPatrol): patrol Running blocks all combat/threat gates.");
            damaged = true; RunTree(explicitQueue);
            Check(!calls.Contains("AmIDamaged") && !calls.Contains("SurrenderOffer"), "Queued patrol did not block damaged-ship handling");
            RunTree(ordinary);
            Check(calls.Contains("AmIDamaged") && calls.Contains("SurrenderOffer") && !calls.Contains("FlyToPatrol"), "Ordinary patrol failed to yield to native damage/surrender handling");
            damaged = false;
            patrolResult = CommandCode.Finished; RunTree(explicitQueue);
            Check(!explicitQueue.PriorityQueue.CanExecute(explicitQueue.Blackboard), "Completed priority patrol did not release the queue");
            RunTree(explicitQueue);
            Check(calls.Contains("KeepWithinFiringDistance"), "Combat did not resume once the whole queued patrol finished");
            patrolResult = CommandCode.Running;
            var urgent = Pilot(); Controller(urgent).AddUrgentCommand(new Dodge(urgent)); RunTree(urgent);
            Check(calls.SequenceEqual(new[] { "Dodge" }), "Genuine urgent dodge no longer owns the native queue");

            // Counterfactual producer repair: retain the assigned patrol point,
            // use the existing ordinary Main, and never insert a priority patrol.
            var ambient = Pilot(); var main = (StickySequenceCommand)((List<ICommand>)AccessTools.Field(typeof(SelectorCommand), "_childCommands").GetValue(ambient.BehaviorTree)).Last();
            main.SaveData = new[] { "1" }; combat = false;
            var assigned = ambient.ShipUs.shipSituTarget; RunTree(ambient);
            Check(ReferenceEquals(assigned, ambient.ShipUs.shipSituTarget) && main.SaveData[0] == "2", "Native target selection replaced the assigned encounter waypoint");
            RunTree(ambient); Check(calls.Contains("FlyToPatrol"), "Native Main failed to execute the assigned encounter patrol");
            combat = true; RunTree(ambient);
            Check(calls.Contains("KeepWithinFiringDistance") && !calls.Contains("FlyToPatrol"), "Counterfactual ambient producer still blocks native combat");
            Check(!ambient.PriorityQueue.CanExecute(ambient.Blackboard), "Ordinary ambient assignment introduced a priority order");

            var sff = Pilot(); var ai = Controller(sff);
            var patrol = (FlyToPatrol)sff.BehaviorTree.FindCommand(typeof(FlyToPatrol));
            patrol.SaveData = new[] { "0.000000001", "trip-fixture", "12", "0", "1" };
            Set(ai, "_activeCommand", patrol); ai.FFWD();
            Check(Waiting(sff.PriorityQueue).Count == 1 && Waiting(sff.PriorityQueue).Peek() is OneShotWrapperCommand, "Native Navy SFF did not enqueue a one-shot wrapper");
            var payload = ai.GetBehaviorTreeSaveData();
            Check(payload.Any(p => p.strPath == "/BehaviorTree/PriorityQueue/Queue/FlyToPatrol"), "Wrapper did not save a bare patrol payload");
            Check(!payload.Any(p => p.strPath.Contains("OneShotWrapper")), "Unexpected wrapper provenance survived native save");
            RunTree(sff);
            Check(calls.SequenceEqual(new[] { "FlyToPatrol" }) && !sff.PriorityQueue.CanExecute(sff.Blackboard), "Unreloaded one-shot failed to finish after one invocation");
            RunTree(sff);
            Check(calls.Contains("KeepWithinFiringDistance"), "Combat did not resume after unpersisted SFF one-shot");

            var restored = Pilot(); var roundtrip = JsonConvert.DeserializeObject<JsonCommandPayload[]>(JsonConvert.SerializeObject(payload));
            restored.BehaviorTree.LoadSaveData("", roundtrip);
            Check(Waiting(restored.PriorityQueue).Count == 1 && Waiting(restored.PriorityQueue).Peek() is FlyToPatrol, "Native load did not flatten SFF wrapper into a patrol");
            for (int i = 0; i < 5; i++) {
                RunTree(restored);
                Check(calls.SequenceEqual(new[] { "FlyToPatrol" }) && Current(restored.PriorityQueue) is FlyToPatrol, "Flattened SFF patrol did not starve combat after reload");
            }
            Console.WriteLine("Native SFF -> JSON -> load: one-shot wrapper lost, persistent priority patrol; combat starved for every sampled tick.");

            foreach (string name in new[] { "FlyTo", "FlyToPatrol", "Hold", "FlyToPath" }) {
                var source = Pilot(); var leaf = AIShip.CreateCommand(name, source);
                leaf.SaveData = new[] { "0.000000001", "trip-fixture", "12", "0", "1" };
                source.PriorityQueue.EnqueueCommand(new OneShotWrapperCommand(leaf));
                var saved = source.PriorityQueue.GetSaveData("/BehaviorTree").ToArray();
                var load = Pilot(); load.PriorityQueue.LoadSaveData("/BehaviorTree", saved);
                Check(Waiting(load.PriorityQueue).Count == 1 && Waiting(load.PriorityQueue).Peek().GetType() == leaf.GetType(), "Wrapper flattening differs for " + name);
                Check(!(Waiting(load.PriorityQueue).Peek() is OneShotWrapperCommand), "Wrapper unexpectedly restored for " + name);
                Console.WriteLine("Wrapper round-trip also loses one-shot semantics for " + name + ".");
            }
            if (args.Length > 1) AuditSave(args[1]);
            Console.WriteLine(checks + " native baseline diagnosis checks passed before enabling production patches.");
            Production();
            Console.WriteLine(checks + " native baseline/production patrol scheduling and queue persistence checks passed.");
        } finally { h.UnpatchSelf(); }
    }
    static void AuditSave(string path) {
        using (var archive = ZipFile.OpenRead(path)) {
            foreach (var entry in archive.Entries.Where(e => !e.FullName.Contains("/") && e.FullName.EndsWith(".json"))) {
                using (var stream = new StreamReader(entry.Open()))
                using (var reader = new JsonTextReader(stream) { MaxDepth = null }) {
                    while (reader.Read()) {
                        if (reader.TokenType != JsonToken.PropertyName || (string)reader.Value != "aAIShips") continue;
                        reader.Read(); var data = JArray.Load(reader);
                        foreach (var record in data.Where(r => (string)r["strRegId"] == "O-P5U" || (string)r["strRegId"] == "O-P65"))
                            Console.WriteLine("SAVE " + entry.FullName + " " + record.ToString(Formatting.None));
                        return;
                    }
                }
            }
        }
        throw new Exception("No aAIShips found in save archive");
    }
}
