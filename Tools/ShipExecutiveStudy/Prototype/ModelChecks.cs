using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using MissionContracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ostranauts.Core;
using Ostranauts.Ships;
using Ostranauts.Ships.Commands;

namespace ShipExecutivePrototype;

static class ModelChecks
{
    const string ModelPath = "Navigation/Architecture/Replacement/mission-model.json";
    static readonly HashSet<string> Edges = new HashSet<string>();
    static Action<bool, string> Check;
    static int forbiddenRetreatCalls;
    static object walkResult;
    static readonly List<object> NativeResults = new List<object>();

    static Machine New(Definition model) => new Machine(model, "SUBJECT", "fixture-activation", Executive.RuntimeBinding, 1000);
    static GoalReference Home() => new GoalReference { TargetId = "HOME", Arrival = "Dock" };
    static PolicyChoice Return(GoalReference goal = null) => new PolicyChoice { Kind = "Return", Source = "native-role-choice/test-input", Goal = goal };
    static Decision Send(Machine m, string input, GoalReference goal = null, string proof = null)
    {
        var pending = m.State.PendingArrival;
        var decision = m.Observe(new Observation
        {
            Ticket = m.Ticket,
            Event = input,
            Epoch = m.State.Epoch,
            Goal = goal,
            EffectId = pending?.Id ?? 0,
            Destination = pending?.Destination,
            WorldProof = proof
        });
        if (decision.Accepted)
            Edges.Add(decision.TransitionId);
        return decision;
    }
    static Machine Approach(Definition model)
    {
        var machine = New(model);
        machine.Begin(Return());
        Send(machine, "GoalSelected", Home());
        return machine;
    }
    static Machine Prepared(Definition model)
    {
        var machine = Approach(model);
        Send(machine, "ApproachComplete");
        Send(machine, "DispatchArrival");
        return machine;
    }
    static void ForbiddenRetreat()
    {
        forbiddenRetreatCalls++;
        throw new InvalidOperationException("Opaque native retreat controller is forbidden in the replacement profile");
    }

    internal static void Run(Action<string, Action> scenario, Action<bool, string> check)
    {
        Check = check;
        var model = Definition.Load(ModelPath);
        Directory.CreateDirectory("obj/ShipExecutiveModel");

        scenario("replacement ability contracts bind native symbols and authored asset examples", () =>
        {
            var contracts = JObject.Parse(File.ReadAllText("Navigation/Architecture/Replacement/ability-contracts.json"));
            foreach (string symbol in contracts["abilities"].SelectMany(a => a["nativeSymbols"]).Select(s => (string)s).Distinct())
            {
                int split = symbol.LastIndexOf('.');
                var type = typeof(Ship).Assembly.GetType(symbol.Substring(0, split));
                Check(type != null && type.GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic).Any(m => m.Name == symbol.Substring(split + 1)), "Unbound native contract symbol: " + symbol);
            }
            var loot = JArray.Parse(File.ReadAllText(Path.Combine(Ostrabus.Development.InstallationPaths.Data, "loot/loot.json")));
            var interactions = JArray.Parse(File.ReadAllText(Path.Combine(Ostrabus.Development.InstallationPaths.Data, "interactions/interactions_ship_to_ship.json")));
            foreach (var binding in contracts["abilities"].SelectMany(a => a["assetBindings"] ?? new JArray()))
            {
                string alias = (string)binding["alias"], command = (string)binding["command"], interaction = (string)binding["interaction"];
                var record = loot.Single(l => (string)l["strName"] == alias);
                Check(record["aCOs"].Any(c => ((string)c).StartsWith(command + "=", StringComparison.Ordinal)), "Authored loot command changed: " + alias);
                var action = interactions.Single(i => (string)i["strName"] == interaction);
                Check(action.Values().Any(v => v.Type == JTokenType.String && (string)v == alias), "Authored interaction no longer admits expected directive: " + interaction);
            }
        });

        scenario("replacement model is executable and independent of native/Harmony types", () =>
        {
            var references = typeof(Machine).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();
            Check(!references.Any(n => n == "Assembly-CSharp" || n == "0Harmony" || n.StartsWith("UnityEngine")), "Reducer depends on native controller types");
            var machine = Approach(model);
            var failed = Send(machine, "ApproachFailed");
            Check(failed.Accepted && machine.State.Active.Phase == "AwaitingPolicy", "Failed approach did not require a policy decision");
            Check(failed.Effects.Contains("RequestNativePolicy") && !failed.Effects.Contains("InvokeNativeArrival"), "Failure requested docking");
            var revision = JObject.Parse(File.ReadAllText(ModelPath));
            revision["transitions"].First(t => (string)t["id"] == "approach-failed")["id"] = "approach-failed-modified";
            var different = Definition.Parse(revision.ToString());
            Check(model.Fingerprint != different.Fingerprint, "Checkpoint binding ignores model revision");
            var mutant = JObject.Parse(File.ReadAllText(ModelPath));
            var edge = mutant["transitions"].First(t => (string)t["id"] == "approach-failed");
            edge["to"] = "ArrivalReady";
            bool rejected = false;
            try
            {
                Definition.Parse(mutant.ToString());
            }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Model accepted failure -> successful arrival mutation");
            var typo = JObject.Parse(File.ReadAllText(ModelPath));
            typo["transitions"][0]["efects"] = new JArray();
            bool typoRejected = false;
            try
            {
                Definition.Parse(typo.ToString());
            }
            catch (JsonSerializationException) { typoRejected = true; }
            Check(typoRejected, "Misspelled transition field was silently ignored");
        });

        scenario("replacement owns suspended goals, revalidation and stale completions", () =>
        {
            var machine = New(model);
            var original = Home();
            machine.Begin(Return(original));
            Send(machine, "ResumeValidated", original);
            long mission = machine.State.Active.Id;
            Ticket old = machine.Ticket;
            original.TargetId = "OVERWRITTEN";
            var interruption = machine.Preempt(new PolicyChoice { Kind = "Reaction", Source = "AIShip.AddUrgentCommand/test-input" }, true);
            Check(interruption.Accepted && interruption.Effects.First() == "StopOwnedControls", "No explicit movement release before reaction");
            Check(machine.State.Suspended.Single().Goal.TargetId == "HOME", "Goal aliases caller/global state");
            Send(machine, "Progress");
            Send(machine, "Blocked");
            Send(machine, "ReactionComplete");
            machine.Resume();
            Check(machine.State.Active.Id == mission && machine.State.Active.Phase == "Revalidating", "Resume bypassed canonical revalidation");
            Check(!machine.Observe(new Observation { Ticket = old, Event = "ApproachComplete", Epoch = 1000 }).Accepted, "Suspended ability's completion was accepted");
            Send(machine, "ResumeValidated", Home());
            Check(machine.State.Active.Phase == "Approaching", "Valid resume did not restart native approach");
            var before = machine.Save();
            Check(!machine.Preempt(new PolicyChoice { Kind = "Combat", Source = "native-policy/test-input" }, false).Accepted && machine.Save() == before, "Executive invented an unauthorized policy choice");
            var rebound = machine.Rebind(2);
            Check(rebound.Accepted && machine.State.Active.Phase == "Revalidating" && !machine.Matches(old), "Representation change retained stale ability authority");
        });

        scenario("replacement arrival requires durable intent and correlated world proof", () =>
        {
            var machine = Prepared(model);
            Check(machine.State.PendingArrival != null && machine.State.Active.Phase == "ArrivalPrepared", "No pre-invocation intent");
            string saved = machine.Save();
            var wrong = new Observation { Ticket = machine.Ticket, Event = "InvocationBoundaryReady", Epoch = 1000, EffectId = 999, Destination = "HOME" };
            Check(!machine.Observe(wrong).Accepted && machine.Save() == saved, "Wrong checkpoint token changed state");
            var submitted = Send(machine, "InvocationBoundaryReady");
            Check(submitted.Accepted && submitted.Effects.SequenceEqual(new[] { "InvokeNativeArrival" }), "Arrival invoked before commit or invoked more than once");
            Check(!Send(machine, "InvocationBoundaryReady").Accepted, "Duplicate checkpoint acknowledgement replayed arrival");
            Check(!Send(machine, "WorldEffectObserved", proof: "command-returned-success").Accepted, "Command success masqueraded as world proof");
            Check(!Send(machine, "NoWorldEffectIssued", proof: "not-docked-yet").Accepted, "Missing attachment was treated as safe replay evidence");
            Send(machine, "WorldOutcomeUnknown");
            Check(machine.State.Active.Phase == "ArrivalUncertain", "Unknown result was treated as completion");
            Check(Send(machine, "WorldEffectObserved", proof: "actor-retired-with-crew-at-destination").Accepted, "Correlated arrival proof rejected");
            Check(machine.State.Active.Phase == "Completed" && machine.State.PendingArrival == null, "Observed arrival did not release pending state");
        });

        scenario("replacement reload refuses ambiguous replay and invalidates old callbacks", () =>
        {
            var original = Prepared(model);
            Ticket old = original.Ticket;
            var loaded = Machine.Restore(model, original.Save(), "SUBJECT", "fixture-activation", Executive.RuntimeBinding);
            Check(loaded.State.Active.Phase == "ArrivalUncertain" && !loaded.Matches(old), "Saved prepare was assumed safe to invoke after reload");
            Check(!Send(loaded, "InvocationBoundaryReady").Accepted, "Reloaded prepare accepted a second invocation");
            Check(!loaded.Preempt(new PolicyChoice { Kind = "Combat", Source = "native-policy/test-input" }, true).Accepted, "Preemption crossed an uncertain world effect");
            loaded.RequestDisable();
            Check(!loaded.MayOfferHandback, "Uncertain transfer allowed native handback");
            Send(loaded, "WorldEffectObserved", proof: "actor-retired-with-crew-at-destination");
            Check(loaded.MayOfferHandback && loaded.State.DisableRequested, "Disable request or safe-boundary readiness was lost");
            bool rejected = false;
            try
            {
                Machine.Restore(model, original.Save(), "SUBJECT", "different-activation", Executive.RuntimeBinding);
            }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Hull reuse inherited an old actor's mission");
        });

        scenario("replacement distinguishes waits, failed goals and waypoint completion", () =>
        {
            var waiting = New(model);
            waiting.Begin(Return());
            Send(waiting, "Blocked");
            Check(waiting.State.Active.Phase == "Selecting", "Blocked selection advanced");
            Send(waiting, "NoDestination");
            Check(waiting.State.Active.Phase == "AwaitingPolicy", "Missing station manufactured a return destination");
            waiting.Replace(Return(), true);
            Send(waiting, "GoalSelected", Home());
            Send(waiting, "Progress");
            Send(waiting, "Blocked");
            Check(waiting.State.Active.Phase == "Approaching", "Pilot wait completed flight");
            Send(waiting, "GoalUnavailable");
            Check(waiting.State.Active.Goal == null && waiting.State.Active.Phase == "AwaitingPolicy", "Unavailable goal retained movement authority");
            var point = new GoalReference { FrameId = "HOME", X = 8e-7, Arrival = "None" };
            waiting.Replace(new PolicyChoice { Kind = "Patrol", Goal = point, Source = "NavyPilot.GetTarget/test-input" }, true);
            Send(waiting, "ResumeValidated", point);
            Send(waiting, "ApproachComplete");
            Check(waiting.State.Active.Phase == "Completed" && waiting.State.PendingArrival == null, "Waypoint arrival attempted docking");
            var ready = Approach(model);
            Send(ready, "ApproachComplete");
            Send(ready, "GoalUnavailable");
            Check(ready.State.Active.Phase == "AwaitingPolicy", "Lost arrival goal continued docking");
            var retry = Prepared(model);
            Send(retry, "InvocationBoundaryReady");
            Send(retry, "NoWorldEffectIssued", proof: "native-call-did-not-enter-world-effect");
            Check(retry.State.Active.Phase == "ArrivalReady" && retry.State.PendingArrival == null, "Proven non-submission was treated as uncertain side effect");
            Send(retry, "DispatchArrival");
            Check(retry.State.PendingArrival.Id == 2, "New attempt reused an old effect correlation ID");
            var rejectedResume = Approach(model);
            rejectedResume.Rebind(2);
            Send(rejectedResume, "ResumeRejected");
            Check(rejectedResume.State.Active.Phase == "AwaitingPolicy", "Failed revalidation resumed movement");
            var fight = New(model);
            fight.Begin(new PolicyChoice { Kind = "Combat", Source = "native-role/test-input" });
            Send(fight, "Progress");
            Send(fight, "Blocked");
            Send(fight, "CombatComplete");
            Check(fight.State.Active.Phase == "Completed", "Completed combat did not return to policy");
            fight.Begin(new PolicyChoice { Kind = "Combat", Source = "native-role/test-input" });
            Send(fight, "GoalUnavailable");
            Check(fight.State.Active.Phase == "AwaitingPolicy", "Lost combat target was chased without a new policy decision");
        });

        scenario("replacement resume preserves failure boundaries and combat intent", () =>
        {
            var machine = Approach(model);
            Send(machine, "ApproachFailed");
            machine.Preempt(new PolicyChoice { Kind = "Reaction", Source = "native-urgent/test-input" }, true);
            Send(machine, "ReactionComplete");
            var resumed = machine.Resume();
            Check(machine.State.Active.Phase == "AwaitingPolicy" && resumed.Effects.Contains("RequestNativePolicy"), "Interrupting failed approach authorized an unrequested retry");
            var combat = New(model);
            combat.Begin(new PolicyChoice { Kind = "Combat", Source = "native-role/test-input" });
            combat.Preempt(new PolicyChoice { Kind = "Reaction", Source = "native-urgent/test-input" }, true);
            Send(combat, "ReactionComplete");
            combat.Resume();
            Check(combat.State.Active.Kind == "Combat" && combat.State.Active.Phase == "AwaitingPolicy", "Resuming combat became station selection or travel");
        });

        scenario("replacement actor retirement invalidates suspended missions", () =>
        {
            var machine = Approach(model);
            machine.Preempt(Return(Home()), true);
            Send(machine, "ResumeValidated", Home());
            Send(machine, "ApproachComplete");
            Send(machine, "DispatchArrival");
            Send(machine, "InvocationBoundaryReady");
            Send(machine, "WorldEffectObserved", proof: "actor-retired-with-crew-at-destination");
            Check(machine.State.ActorDisposition == "Retired" && machine.State.Suspended.Count == 0, "Retirement retained executable suspended work");
            Check(!machine.Resume().Accepted && !machine.Begin(Return()).Accepted && !machine.Replace(Return(), true).Accepted && !machine.Rebind(2).Accepted, "A retired actor could receive new movement authority");
            var restored = Machine.Restore(model, machine.Save(), "SUBJECT", "fixture-activation", Executive.RuntimeBinding);
            Check(restored.State.ActorDisposition == "Retired" && !restored.Begin(Return()).Accepted, "Reload resurrected a retired actor");
        });

        scenario("replacement authority release revokes every executive writer", () =>
        {
            var machine = Approach(model);
            Ticket before = machine.Ticket;
            machine.RequestDisable();
            Check(!machine.ReleaseToNative(false).Accepted, "Handback trusted an uncertified projection");
            Check(machine.ReleaseToNative(true).Accepted && !machine.Matches(before), "Handback retained old ability authority");
            string released = machine.Save();
            Check(!machine.Begin(Return()).Accepted && !machine.Replace(Return(), true).Accepted && !machine.Preempt(new PolicyChoice { Kind = "Reaction", Source = "native-urgent/test-input" }, true).Accepted && !machine.Rebind(2).Accepted && !machine.Resume().Accepted, "Native authority admitted a competing executive writer");
            Check(!machine.SettleWorldTime(1100) && machine.Save() == released, "Executive changed native-owned state after release");
        });

        scenario("replacement withstands bounded adversarial event sequences", () =>
        {
            var random = new Random(2079);
            int accepted = 0, rejected = 0;
            for (int sequence = 0; sequence < 64; sequence++)
            {
                var machine = Approach(model);
                Ticket old = machine.Ticket;
                for (int step = 0; step < 24; step++)
                {
                    string before = machine.Save();
                    string phase = machine.State.Active.Phase;
                    Decision result;
                    switch (random.Next(7))
                    {
                        case 0:
                            result = machine.Preempt(new PolicyChoice { Kind = "Reaction", Source = "native-urgent/test-input" }, true);
                            break;
                        case 1:
                            result = machine.Resume();
                            break;
                        case 2:
                            result = machine.Replace(Return(Home()), true);
                            break;
                        case 3:
                            result = machine.Rebind(machine.State.BindingRevision + 1);
                            break;
                        default:
                            var pending = machine.State.PendingArrival;
                            result = machine.Observe(new Observation
                            {
                                Ticket = random.Next(4) == 0 ? old : machine.Ticket,
                                Event = model.Events[random.Next(model.Events.Length)],
                                Epoch = random.Next(5) == 0 ? 999 : machine.State.Epoch,
                                Goal = Home(),
                                EffectId = pending?.Id ?? 0,
                                Destination = pending?.Destination,
                                WorldProof = "actor-retired-with-crew-at-destination"
                            });
                            break;
                    }
                    if (!result.Accepted)
                    {
                        rejected++;
                        if (machine.Save() != before)
                            throw new Exception($"Rejected observation mutated state: sequence {sequence}, step {step}");
                    }
                    else
                        accepted++;
                    if (machine.State.Suspended.Count > model.Limits.SuspendedMissions || machine.State.Suspended.Select(m => m.Id).Concat(new[] { machine.State.Active.Id }).Distinct().Count() != machine.State.Suspended.Count + 1)
                        throw new Exception("Bounded mission identity invariant failed");
                    if (result.Effects.Contains("InvokeNativeArrival") && phase != "ArrivalPrepared")
                        throw new Exception("Arrival escaped checkpoint barrier");
                    if (machine.State.PendingArrival != null && machine.MayOfferHandback)
                        throw new Exception("Handback escaped pending-effect barrier");
                }
            }
            Check(accepted > 0 && rejected > 0, "Adversarial walk did not exercise acceptance and refusal");
            walkResult = new
            {
                seed = 2079,
                sequences = 64,
                eventsPerSequence = 24,
                accepted,
                rejected,
                exhaustive = false
            };
        });

        var guard = new Harmony("offline.replacement.forbid-opaque-retreat");
        guard.Patch(AccessTools.Method(typeof(LowFuelRetreat), nameof(LowFuelRetreat.RunCommand)), prefix: new HarmonyMethod(typeof(ModelChecks), nameof(ForbiddenRetreat)));
        try
        {
            scenario("replacement native return omits opaque retreat and commits before scene effect", () =>
            {
                var w = new NativeWorld { LowFuel = true };
                var actor = new NativeReturnActor(w, model);
                bool committedAtInvocation = false;
                w.BeforeArrival = () => committedAtInvocation = actor.Machine.State.Active.Phase == "ArrivalUncertain" && actor.Machine.State.PendingArrival != null
                    && File.ReadAllText(NativeReturnActor.CheckpointPath) == actor.LastCheckpoint;
                Check(actor.BeginNativeLowFuelReturn(), "Native low-fuel policy did not admit return");
                Check(actor.Advance(60, "SFF"), "Replacement return did not finish");
                Check(w.Retired && w.ArrivalCalls == 1 && actor.Machine.State.Active.Phase == "Completed", "Return did not acknowledge actual native Dock request");
                Check(committedAtInvocation, "Native world effect preceded durable arrival intent");
                Check(Math.Abs(w.Subject.fShallowRCSRemass - 295) < 1e-6 && StarSystem.fEpoch == 1060, "Fuel or residual time was lost");
                Check(forbiddenRetreatCalls == 0, "Replacement executed opaque native LowFuelRetreat");
                NativeResults.Add(new
                {
                    scenario = "explicit-return",
                    nativeRetreatCalls = forbiddenRetreatCalls,
                    finalFuel = w.Subject.fShallowRCSRemass,
                    finalEpoch = StarSystem.fEpoch,
                    committedAtInvocation
                });
            });

            scenario("replacement native midpoint reload uses explicit phase and flight payload", () =>
            {
                var w = new NativeWorld { LowFuel = true };
                var actor = new NativeReturnActor(w, model);
                actor.BeginNativeLowFuelReturn();
                Check(actor.Advance(20, "normal"), "No midpoint progress");
                string checkpoint = actor.Save();
                var ticket = actor.Machine.Ticket;
                Check(!checkpoint.Contains("_retreating") && !checkpoint.Contains("LowFuelRetreat"), "Replacement saves private composite-controller state");
                actor.Advance(40, "SFF");
                double expected = w.Subject.fShallowRCSRemass;
                var loaded = NativeReturnActor.Restore(model, checkpoint);
                Check(!loaded.Machine.Matches(ticket) && loaded.Machine.State.Active.Phase == "Approaching", "Reconstruction lost phase or retained stale binding authority");
                Check(loaded.Advance(40, "PASS") && loaded.World.Retired && loaded.World.ArrivalCalls == 1, "Reload did not complete native arrival once");
                Check(Math.Abs(loaded.World.Subject.fShallowRCSRemass - expected) < 1e-6, "Native fuel settlement replayed across reload");
                NativeResults.Add(new
                {
                    scenario = "midpoint-reload",
                    executiveBytes = System.Text.Encoding.UTF8.GetByteCount(JsonConvert.DeserializeObject<ReturnCheckpoint>(checkpoint).Executive),
                    fuel = expected
                });
            });

            scenario("replacement native pilot wait, failed planning and arrival refusal stay distinct", () =>
            {
                var w = new NativeWorld { LowFuel = true, PilotAvailable = false };
                var actor = new NativeReturnActor(w, model);
                actor.BeginNativeLowFuelReturn();
                Check(actor.Machine.State.Active.Phase == "Approaching" && w.Subject.objSS.NavData == null, "Unavailable pilot advanced mission");
                w.PilotAvailable = true;
                actor.Tick();
                w.PilotAvailable = false;
                Check(actor.Advance(40, "PASS") && w.ArrivalCalls == 0, "Pilotless course attempted scene transfer");
                w.PilotAvailable = true;
                actor.Tick();
                Check(w.Retired, "Restored pilot did not resume native docking");
                var bad = new NativeWorld { LowFuel = true, PlanFails = true };
                var blocked = new NativeReturnActor(bad, model);
                blocked.BeginNativeLowFuelReturn();
                for (int i = 0; i < 6; i++)
                    blocked.Tick();
                Check(bad.ArrivalCalls == 0 && blocked.Machine.State.Active.Phase == "AwaitingPolicy" && blocked.PolicyRequests == 1, "Failed planner became successful docking or an unbounded retry policy");
                var stranded = new NativeWorld { LowFuel = true };
                stranded.Subject.fShallowRCSRemass = 0;
                var unqualified = new NativeReturnActor(stranded, model);
                Check(!unqualified.BeginNativeLowFuelReturn() && unqualified.Machine.State.Active == null && ReferenceEquals(AIShipManager.GetAIShipByRegID("SUBJECT"), stranded.AI), "Funded-return fixture rewrote already-stranded actor policy");
                var refused = new NativeWorld { LowFuel = true, RefuseArrival = true };
                var uncertain = new NativeReturnActor(refused, model);
                uncertain.BeginNativeLowFuelReturn();
                Check(!uncertain.Advance(60, "SFF") && uncertain.Machine.State.Active.Phase == "ArrivalUncertain", "Unacknowledged effect was completed");
                var loaded = NativeReturnActor.Restore(model, uncertain.Save());
                loaded.Tick();
                loaded.Tick();
                Check(loaded.World.ArrivalCalls == 0 && loaded.Machine.State.PendingArrival != null, "Uncertain scene request replayed after reload");
            });

            scenario("replacement prepare/scene-commit crash windows reconcile without replay", () =>
            {
                var w = new NativeWorld { LowFuel = true };
                var actor = new NativeReturnActor(w, model) { AutoCheckpoint = false };
                actor.BeginNativeLowFuelReturn();
                Check(actor.Advance(40, "SFF") && w.ArrivalCalls == 0 && actor.Machine.State.Active.Phase == "ArrivalPrepared", "World effect ignored checkpoint barrier");
                string before = actor.Save();
                actor.CommitCheckpoint();
                Check(w.Retired && w.ArrivalCalls == 1, "Checkpoint acknowledgement did not invoke arrival");
                string committedWorld = WorldCheckpoint.Capture(w);
                var uncertain = NativeReturnActor.Restore(model, before);
                uncertain.Tick();
                Check(uncertain.World.ArrivalCalls == 0 && uncertain.Machine.State.Active.Phase == "ArrivalUncertain", "Reload treated prepare as safe replay");
                // Combine committed native world data with the older executive snapshot.
                var mixed = new ReturnCheckpoint { Executive = JsonConvert.DeserializeObject<ReturnCheckpoint>(before).Executive, World = committedWorld };
                var reconciled = NativeReturnActor.Restore(model, JsonConvert.SerializeObject(mixed));
                reconciled.Tick();
                Check(reconciled.Machine.State.Active.Phase == "Completed" && reconciled.World.ArrivalCalls == 0, "World-committed / executive-uncommitted reload repeated crew transfer");
            });

            scenario("replacement qualified handback continues through the real native dispatcher", () =>
            {
                var w = new NativeWorld { LowFuel = true };
                var actor = new NativeReturnActor(w, model);
                actor.BeginNativeLowFuelReturn();
                actor.Advance(20, "normal");
                double before = StarSystem.fEpoch, fuel = w.Subject.fShallowRCSRemass;
                Check(actor.TryHandBack(out var reason), "Qualified native projection refused: " + reason);
                actor.Tick();
                Check(!actor.Advance(20, "SFF") && StarSystem.fEpoch == before && w.Subject.fShallowRCSRemass == fuel, "Old executive continued advancing a native-owned actor");
                StarSystem.fEpoch += 20;
                w.Subject.objSS.NavData.TimeAdvance(w.Subject.objSS);
                for (int i = 0; i < 3 && !w.Retired; i++)
                    w.Pilot.BehaviorTree.RunCommand(w.Pilot.Blackboard);
                Check(w.Retired && w.ArrivalCalls == 1 && Math.Abs(w.Subject.fShallowRCSRemass - 295) < 1e-6, "Native dispatcher could not continue projected return");
                var restored = NativeReturnActor.Restore(model, actor.Save());
                restored.Tick();
                Check(restored.Machine.State.Authority == "Native" && restored.World.ArrivalCalls == 0, "Reload resurrected executive authority after handback");
            });

            scenario("replacement combat-sensitive handback defers without erasing native policy", () =>
            {
                var w = new NativeWorld { LowFuel = true };
                var actor = new NativeReturnActor(w, model);
                actor.BeginNativeLowFuelReturn();
                w.Pilot.Blackboard.Set("InCombat", true);
                Check(!actor.TryHandBack(out _) && actor.Machine.State.DisableRequested && actor.Machine.State.Authority == "Executive", "Unrepresentable combat retreat was handed back anyway");
                Check(w.Pilot.Blackboard.Get<bool>("InCombat"), "Handback suppressed combat to force export");
                w.Pilot.Blackboard.Set("InCombat", false);
                actor.Advance(60, "SFF");
                Check(actor.TryHandBack(out _) && actor.Machine.State.Authority == "Native", "Deferred disable did not permit handback at retired boundary");
            });

            scenario("replacement return conserves route accounting across interval partitions", () =>
            {
                double fuel = -1;
                foreach (var partition in new[] { 10, 20, 60 })
                {
                    var w = new NativeWorld { LowFuel = true };
                    var actor = new NativeReturnActor(w, model);
                    actor.BeginNativeLowFuelReturn();
                    for (int elapsed = 0; elapsed < 60; elapsed += partition)
                        Check(actor.Advance(partition, "fixture-normal-SFF-PASS"), "Partitioned route failed");
                    Check(w.Retired && w.ArrivalCalls == 1 && StarSystem.fEpoch == 1060, "Partition changed final state or world-effect count");
                    if (fuel < 0)
                        fuel = w.Subject.fShallowRCSRemass;
                    else
                        Check(Math.Abs(w.Subject.fShallowRCSRemass - fuel) < 1e-6, "Partition changed fuel accounting");
                }
            });
        }
        finally { guard.UnpatchSelf(); }

        scenario("reference prototype lacks the replacement's pre-invocation barrier (negative control)", () =>
        {
            var w = new NativeWorld();
            new TargetData(w.Home).ApplyToShip(w.Subject);
            var reference = new Executive(w, w.Pilot, w.AI);
            for (int i = 0; i < 3; i++)
                reference.Tick();
            bool missingBarrier = false;
            w.BeforeArrival = () => missingBarrier = reference.State.PendingEffect == null;
            reference.Advance(60, "SFF");
            Check(missingBarrier && w.ArrivalCalls == 1, "Negative control did not distinguish old wrapper from replacement architecture");
        });

        scenario("replacement directives distinguish progress, completion and failure", () =>
        {
            var m = New(model);
            m.Begin(new PolicyChoice { Kind = "Directive", Source = "native-command/test-input" });
            Check(Send(m, "DirectiveProgress").Accepted && m.State.Active.Phase == "Operating", "Directive progress advanced lifecycle");
            Check(Send(m, "DirectiveComplete").Accepted && m.State.Active.Phase == "Completed", "Directive completion not terminal");
            m.Begin(new PolicyChoice { Kind = "Directive", Source = "native-command/test-input" });
            var failed = Send(m, "DirectiveFailed");
            Check(failed.Accepted && failed.Effects.Contains("RequestNativePolicy") && m.State.PendingArrival == null, "Directive failure became docking or skipped policy");
        });

        var lines = new List<string> { "flowchart TD", "  %% Generated from mission-model.json; normative replacement, not native behavior." };
        foreach (var edge in model.Transitions)
            lines.Add("  " + edge.From + " -->|" + edge.Event + (edge.WhenArrival == null ? "" : " [" + edge.WhenArrival + "]") + "| " + edge.To);
        File.WriteAllLines("obj/ShipExecutiveModel/mission-model.mmd", lines);
        File.WriteAllText("obj/ShipExecutiveModel/conformance.json", JsonConvert.SerializeObject(new
        {
            modelId = model.Id,
            modelSha256 = model.Fingerprint,
            coveredTransitionIds = Edges.OrderBy(x => x).ToArray(),
            unexercisedTransitionIds = model.Transitions.Select(t => t.Id).Except(Edges).ToArray(),
            adversarialWalk = walkResult,
            nativeProfiles = NativeResults,
            referencePrototypeViolations = new[] { "no durable pre-invocation arrival barrier", "opaque nested retreat persistence remains in reference prototype" },
            productionReady = false,
            limitations = new[] { "native world/physics fixtures; no live Unity", "Navy ability fixture results are in test-results.json and NAVY-LIFECYCLE.md", "authored reply/subsystem outcomes and constructor effect closure still open", "all-state native handback pending; ordinary return subset qualified", "retained world-service integration qualified only for WORLD-SERVICES.md fixture cases; coherent production save boundary still open" }
        }, Formatting.Indented));
    }
}
