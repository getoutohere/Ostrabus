using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MissionContracts;
using Ostranauts.Core;
using Ostranauts.Ships.Commands;
using Ostranauts.Ships.Comms;

namespace ShipExecutivePrototype;

static class NavyInputChecks
{
    internal static Loot Text(params string[] words) => Loot.CloneFromJSON(new JsonLoot
    {
        strName = "offline-command-input",
        strType = "text",
        aCOs = words.Select(w => w + "=1x1").ToArray(),
        aLoots = Array.Empty<string>()
    });

    internal static void Run(Action<string, Action> scenario, Action<bool, string> check, Definition model, NavyFixtures fixtures)
    {
        scenario("Navy native factory rejection is explicit and unsupported bodies remain in the inbox", () =>
        {
            var world = new NativeWorld();
            var actor = new NativeNavyActor(world, model);
            using (var admission = new NativeNavyAdmission(actor))
            {
                bool refused = false;
                try
                {
                    world.AI.AddCommandLoot("MissingOfflineCommand");
                }
                catch (NotSupportedException) { refused = true; }
                check(refused && actor.State.Inbox.Count == 0, "Failed native factory silently acknowledged an input");
                world.AI.AddCommandLoot("Hold", new[] { "PLAYER" });
                refused = false;
                try
                {
                    actor.Tick();
                }
                catch (NotSupportedException) { refused = true; }
                check(refused && actor.State.Inbox.Single().Command == "Hold" && actor.State.Inbox.Single().Payload.SequenceEqual(new[] { "PLAYER" }),
                    "Unsupported native command was silently executed or dropped");
                world.AI.AddCommand(new Shoot(world.Pilot));
                check(actor.State.Inbox.Last().Origin == "AIShip.AddCommand", "Rejected raw scope contaminated later typed input");
                while (actor.State.Inbox.Count < 16)
                    world.AI.AddCommandLoot("Shoot");
                AccessTools.Method(typeof(Comms), "ApplyLoot").Invoke(world.Subject.Comms, new object[] { Text("TUpAltruism"), world.Captain, "PLAYER" });
                check(actor.State.Inbox.Count == 16, "Harmless native condition-loot probe was rejected or consumed inbox capacity");
            }
        });
        scenario("Navy checkpoint retains missile IDs and rejects duplicated directive identities", () =>
        {
            var w = new NativeWorld();
            var actor = new NativeNavyActor(w, model);
            actor.Tick();
            w.Pilot.Blackboard.Set("InComingMissiles", new List<string> { "incoming-1", "incoming-2" });
            using (var admission = new NativeNavyAdmission(actor))
                w.AI.AddCommandLoot("Shoot");
            var loaded = NativeNavyActor.Restore(model, actor.Save());
            check(loaded.World.Pilot.Blackboard.Get<List<string>>("InComingMissiles").SequenceEqual(new[] { "incoming-1", "incoming-2" }), "Incoming missile list disappeared across reload");
            var envelope = Newtonsoft.Json.Linq.JObject.Parse(loaded.Save());
            var profile = Newtonsoft.Json.Linq.JObject.Parse((string)envelope["Profile"]);
            ((Newtonsoft.Json.Linq.JArray)profile["Inbox"]).Add(profile["Inbox"][0].DeepClone());
            envelope["Profile"] = profile.ToString();
            bool refused = false;
            try
            {
                NativeNavyActor.Restore(model, envelope.ToString());
            }
            catch (InvalidOperationException) { refused = true; }
            check(refused, "Duplicated input identity was accepted");
        });
        scenario("Navy native comms loot retains counterpart and write-only command arguments", () =>
        {
            var w = new NativeWorld();
            var actor = new NativeNavyActor(w, model);
            actor.Tick();
            using (var admission = new NativeNavyAdmission(actor))
            {
                AccessTools.Method(typeof(Comms), "ApplyLoot").Invoke(w.Subject.Comms, new object[] { Text("RaceComms", "IsPractice"), w.Captain, "PLAYER" });
                var input = actor.State.Inbox.Single();
                check(input.Command == "RaceComms" && input.Payload.SequenceEqual(new[] { "IsPractice" }) && input.Counterparty == "PLAYER", "Native comms options erased counterpart context");
                var loaded = NativeNavyActor.Restore(model, actor.Save());
                admission.Actor = loaded;
                loaded.Tick();
                check(loaded.Machine.State.Authority == "Native" && loaded.State.Inbox.Count == 0, "External subsystem command retained competing executive authority");
                var queue = (Queue<ICommand>)AccessTools.Field(typeof(QueuedCommandsNode), "commandQueue").GetValue(loaded.World.Pilot.PriorityQueue);
                var command = queue.Single();
                check(command is RaceComms && ((string[])AccessTools.Field(typeof(RaceComms), "_payload").GetValue(command)).SequenceEqual(new[] { "IsPractice" }), "Native factory handback lost write-only payload");
                loaded.World.AI.AddCommandLoot("Shoot", new[] { "PLAYER" });
                check(queue.Count == 2 && loaded.State.Inbox.Count == 0, "Command adapter intercepted after native takeover");
            }
        });
        scenario("Navy native typed one-shot and target directive preserve ordinary intent", () =>
        {
            var w = new NativeWorld();
            var actor = new NativeNavyActor(w, model);
            actor.Tick();
            using (var admission = new NativeNavyAdmission(actor))
            {
                w.AI.AddCommand(new OneShotWrapperCommand(new TargetPlayer(w.Pilot)));
                check(actor.State.Inbox.Single().Once && !w.Pilot.PriorityQueue.CanExecute(w.Pilot.Blackboard), "Typed wrapper escaped admission");
                w.PilotAvailable = false; // Check intent independently of player-approach physics.
                actor.Tick();
                check(actor.State.Inbox.Count == 0 && actor.Machine.State.Active.Kind == "Patrol" && actor.Machine.State.Active.Goal.TargetId == "PLAYER", "Native target directive was overwritten by suspended patrol goal");
                check(actor.Machine.State.Suspended.Count == 0, "Completed one-shot leaked a continuation");
            }
        });
        scenario("Navy urgent producer's late target publication and repeated dodge are bounded", () =>
        {
            var w = new NativeWorld { Threat = true };
            var actor = new NativeNavyActor(w, model);
            actor.Tick();
            actor.Tick();
            using (var admission = new NativeNavyAdmission(actor))
            {
                w.AI.AddUrgentCommand(new Dodge(w.Pilot));
                w.Pilot.Blackboard.Set("DodgeTarget", "ENEMY"); // native producer order
                w.Pilot.Blackboard.Set("DodgeDir", 1f);
                actor.Tick();
                StarSystem.fEpoch += 3;
                actor.Tick(3);
                double remaining = actor.State.ReactionRemaining;
                int suspended = actor.Machine.State.Suspended.Count;
                for (int i = 0; i < 20; i++)
                {
                    w.AI.AddUrgentCommand(new Dodge(w.Pilot));
                    w.Pilot.Blackboard.Set("DodgeTarget", "ENEMY");
                    actor.Tick();
                }
                check(actor.State.ReactionTarget == "ENEMY" && actor.State.ReactionRemaining == remaining, "Repeated urgent admission restarted timer or read context too early");
                check(actor.Machine.State.Suspended.Count == suspended && suspended <= model.Limits.SuspendedMissions, "Repeated dodge stacked reactions");
                w.Ships.Remove("ENEMY");
                actor.Tick(1);
                check(!actor.State.ReactionStarted && !w.Subject.objSS.bIgnoreGrav, "Native early reaction completion leaked gravity suppression");
            }
        });
        scenario("Navy native TakeFuel constructor pause survives reload and failure does not retry", () =>
        {
            var w = new NativeWorld();
            var actor = new NativeNavyActor(w, model);
            actor.Tick();
            using (var admission = new NativeNavyAdmission(actor))
            {
                w.AI.AddCommandLoot("TakeFuel", new[] { "PLAYER" });
                actor.Tick();
                check(actor.State.Inbox.Single().ConstructorRan && w.Subject.fAIPauseTimer == 1000.5, "Native constructor pause not captured");
                var loaded = NativeNavyActor.Restore(model, actor.Save());
                admission.Actor = loaded;
                StarSystem.fEpoch += .25;
                loaded.Tick();
                check(loaded.World.Subject.fAIPauseTimer == 1000.5 && loaded.State.Inbox.Count == 1, "Reload restarted constructor delay");
                StarSystem.fEpoch += 1;
                loaded.Tick();
                check(loaded.State.Inbox.Count == 0 && loaded.Machine.State.Active.Kind == "Patrol", "Failed fuel directive looped or became arrival");
            }
        });
        scenario("Navy native surrender offer accepts only after attacker breaks target lock", () =>
        {
            var w = new NativeWorld { Threat = true };
            var actor = new NativeNavyActor(w, model);
            actor.Tick();
            actor.Tick();
            var sender = NativeWorld.Empty<CondOwner>();
            sender.ship = w.Enemy;
            var interaction = NativeWorld.Empty<Interaction>();
            interaction.objThem = sender;
            interaction.LootCTsUs = Text("SurrenderConsiderOffer");
            var messages = (List<ShipMessage>)AccessTools.Field(typeof(Comms), "_messageLog").GetValue(w.Subject.Comms);
            messages.Add(new ShipMessage { AvailableTime = StarSystem.fEpoch, Interaction = interaction });
            using (var admission = new NativeNavyAdmission(actor))
            {
                w.Enemy.shipCombatTarget = w.Subject;
                w.AI.AddCommandLoot("SurrenderConsiderOffer");
                actor.Tick();
                check(fixtures.Sent.Last() == "SHIPDenySurrenderBreakLock" && actor.State.Inbox.Count == 0, "Native surrender rejection was bypassed");
                w.Enemy.shipCombatTarget = null;
                w.AI.AddCommandLoot("SurrenderConsiderOffer");
                actor.Tick();
                check(fixtures.Sent.Last() == "SHIPAcceptSurrender" && w.Pilot.Blackboard.Get<List<Ship>>("SurrenderedToUs").Contains(w.Enemy), "Native surrender acceptance did not update combat policy state");
                check(w.Subject.shipCombatTarget == null && !w.Pilot.Blackboard.Get<bool>("InCombat"), "Surrender left combat active");
            }
        });
        scenario("Navy native demand result can terminate combat without running hidden retreat", () =>
        {
            fixtures.AcceptDemand = true;
            try
            {
                var w = new NativeWorld { Threat = true };
                var actor = new NativeNavyActor(w, model);
                actor.Tick();
                check(fixtures.Sent.Contains("STKShipSurrender_AIDemand_Intro") && actor.CombatCalls == 0, "Native accepted demand was ignored");
            }
            finally { fixtures.AcceptDemand = false; }
        });
        scenario("Navy explicit ComeHere reaches player, reloads native hold timer, then resumes ATC intent", () =>
        {
            var w = new NativeWorld();
            var actor = new NativeNavyActor(w, model);
            actor.Tick();
            CrewSim.coPlayer.ship.objSS.vPosx = w.Subject.objSS.vPosx + 1e-10;
            w.Subject.fAIDockingExpire = double.NegativeInfinity;
            fixtures.CollisionRadius = 1e-8f;
            try
            {
                using (var admission = new NativeNavyAdmission(actor))
                {
                    w.AI.AddCommandLoot("ComeHere", new[] { "PLAYER" });
                    actor.Tick();
                    var directive = actor.State.Inbox.Single();
                    check(directive.Phase == "Hold" && w.Subject.objSS.bIgnoreGrav && w.Subject.shipScanTarget == w.Home, "ComeHere skipped its authored player/ATC/hold sequence");
                    double left = directive.DockingRemaining;
                    var loaded = NativeNavyActor.Restore(model, actor.Save());
                    admission.Actor = loaded;
                    StarSystem.fEpoch += 10;
                    loaded.Tick();
                    check(loaded.State.Inbox.Single().DockingRemaining == left - 10 && loaded.Machine.State.Active.Kind == "Directive", "Reload restarted or skipped native hold duration");
                    StarSystem.fEpoch += left;
                    loaded.Tick();
                    check(loaded.State.Inbox.Count == 0 && loaded.Machine.State.Active.Kind == "Patrol" && loaded.Machine.State.Active.Goal.TargetId == "HOME" && !loaded.World.Subject.objSS.bIgnoreGrav,
                        "Completed ComeHere restored stale patrol intent or retained gravity suppression");
                }
            }
            finally { fixtures.CollisionRadius = 0; }
        });
    }
}
