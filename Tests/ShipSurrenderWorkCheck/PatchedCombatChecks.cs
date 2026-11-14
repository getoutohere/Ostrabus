using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using Ostranauts.Ships.Comms;
using Ostrabus.ShipNavigation;

internal static partial class Program {
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static Ship TestShip(string id) {
        var ship = Empty<Ship>(); ship.strRegID = id;
        AccessTools.Field(typeof(Ship), "<objSS>k__BackingField").SetValue(ship, Empty<ShipSitu>());
        AccessTools.Field(typeof(Ship), "<Comms>k__BackingField").SetValue(ship, Empty<Comms>());
        return ship;
    }
    static SurrenderOffer OfferFor(Ship ship) {
        var command = Empty<SurrenderOffer>();
        AccessTools.Field(typeof(BaseCommand), "_shipUs").SetValue(command, ship);
        return command;
    }
    sealed class MarkerCommand : BaseCommand {
        internal int Calls;
        public override CommandStatus RunCommand(Blackboard board) { Calls++; return Success; }
    }

    static int CheckPatchedCombat() {
        var fixture = new Harmony("offline.surrender-accounting.fixture");
        var production = new Harmony("offline.surrender-accounting.production");
        fixture.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Equality"), prefix: new HarmonyMethod(typeof(Program), nameof(Equal)));
        fixture.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Inequality"), prefix: new HarmonyMethod(typeof(Program), nameof(Unequal)));
        fixture.Patch(AccessTools.Method(typeof(Comms), "SendMessage", new[] { typeof(string), typeof(string), typeof(string) }),
            prefix: new HarmonyMethod(typeof(Program), nameof(Send)));
        fixture.Patch(AccessTools.Method(typeof(AIShipManager), "GetAIShipByRegID"), prefix: new HarmonyMethod(typeof(Program), nameof(NoController)));
        try {
            production.CreateClassProcessor(typeof(SurrenderOfferPatch)).Patch();
            var us = TestShip("offerer"); var a = TestShip("player"); var b = TestShip("npc");
            var c = TestShip("uninvolved");
            var offer = OfferFor(us);
            var peer = Empty<AIShip>(); var peerBoard = new Blackboard();
            AccessTools.Field(typeof(AIShip), "<Blackboard>k__BackingField").SetValue(peer, peerBoard);
            controllers[b.strRegID] = peer;

            Blackboard Board(params Ship[] foes) {
                var board = new Blackboard();
                board.Set("Combatants", foes.ToList()); board.Set("Enemies", foes.ToList());
                board.Set("InCombat", true); us.shipCombatTarget = foes.FirstOrDefault();
                peerBoard.Set("Combatants", new List<Ship> { us, c });
                return board;
            }
            // Both a player opponent (no AI controller) and a separate NPC board.
            foreach (Ship foe in new[] { a, b }) {
                var board = Board(foe);
                var originalCombatants = board.Get<List<Ship>>("Combatants");
                var originalEnemies = board.Get<List<Ship>>("Enemies");
                sendOutcome = _ => false;
                for (int tick = 0; tick < 6; tick++) {
                    Check(offer.RunCommand(board).Status == CommandCode.Failure, "Failed offer reports success");
                    Check(us.shipCombatTarget == foe && board.Get<bool>("InCombat"), "Failed offer loses combat lock/state");
                    Check(ReferenceEquals(originalCombatants, board.Get<List<Ship>>("Combatants")) &&
                        ReferenceEquals(originalEnemies, board.Get<List<Ship>>("Enemies")), "Failure replaces perception lists");
                    Check(!board.HasKey("SurrenderedTo") && peerBoard.Get<List<Ship>>("Combatants").Contains(us),
                        "Failure mutates surrender or peer combat state");
                }
                sendOutcome = _ => true;
                Check(offer.RunCommand(board).Status == CommandCode.Finished, "Successful offer fails");
                Check(!board.Get<bool>("InCombat") && !board.HasKey("Combatants") && us.shipCombatTarget == null,
                    "Success leaves original engagement active");
                Check(board.Get<List<Ship>>("SurrenderedTo").SequenceEqual(new[] { foe }), "Success not recorded exactly once");
                Check(peerBoard.Get<List<Ship>>("Combatants").Contains(us) == (foe == a), "Wrong opponent board edited");
            }

            // Success against one opponent does not surrender to everyone else.
            foreach (bool firstSucceeds in new[] { false, true }) {
                var board = Board(a, b);
                sendOutcome = id => (id == a.strRegID) == firstSucceeds;
                Check(offer.RunCommand(board).Status == CommandCode.Failure, "Partial send reports all clear");
                Ship sent = firstSucceeds ? a : b, failed = firstSucceeds ? b : a;
                Check(board.Get<List<Ship>>("SurrenderedTo").SequenceEqual(new[] { sent }), "Mixed outcomes misrecorded");
                Check(board.Get<bool>("InCombat") && board.Get<List<Ship>>("Combatants").SequenceEqual(new[] { failed }),
                    "Failed foe lost after partial success");
                Check(us.shipCombatTarget == (firstSucceeds ? null : a), "Unrelated or failed target cleared");
                Check(peerBoard.Get<List<Ship>>("Combatants").Contains(us) == firstSucceeds, "Peer side effects ignore send outcome");
                sendOutcome = _ => true;
                int before = attempts;
                Check(offer.RunCommand(board).Status == CommandCode.Finished && attempts == before + 1,
                    "Retry resends a completed opponent or fails remaining foe");
                Check(board.Get<List<Ship>>("SurrenderedTo").Distinct().Count() == 2, "Retry duplicates surrender records");
            }

            var prior = new List<Ship> { a };
            var existing = Board(a, b, b);
            existing.Set("SurrenderedTo", prior);
            sendOutcome = _ => true;
            int start = attempts;
            Check(offer.RunCommand(existing).Status == CommandCode.Finished && attempts == start + 1,
                "Prior/duplicate foes produce duplicate offers");
            Check(prior.SequenceEqual(new[] { a }) && existing.Get<List<Ship>>("SurrenderedTo").SequenceEqual(new[] { a, b }),
                "Adding a new offer mutated the prior board snapshot");

            var aliased = Board(a, b);
            var shared = aliased.Get<List<Ship>>("Combatants");
            aliased.Set("Enemies", shared);
            var priorEmpty = new List<Ship>(); aliased.Set("SurrenderedTo", priorEmpty);
            sendOutcome = _ => false;
            Check(offer.RunCommand(aliased).Status == CommandCode.Failure && ReferenceEquals(priorEmpty, aliased.Get<List<Ship>>("SurrenderedTo")),
                "All-failed attempt changes an existing empty offer list");
            sendOutcome = _ => true; start = attempts;
            Check(offer.RunCommand(aliased).Status == CommandCode.Finished && attempts == start + 2,
                "Multiple successful offers not completed");
            Check(shared.SequenceEqual(new[] { a, b }) && aliased.Get<List<Ship>>("Enemies").Count == 2,
                "Combatants/Enemies alias was erased by successful cleanup");
            Check(peerBoard.Get<List<Ship>>("Combatants").SequenceEqual(new[] { c }), "Successful NPC surrender erases unrelated combatants");

            // Native fallback selects aggressors, without deleting passive Enemies.
            var fallback = Board();
            var enemies = new List<Ship> { a, b, null };
            a.shipCombatTarget = us; b.shipCombatTarget = c; us.shipCombatTarget = a;
            fallback.Set("Enemies", enemies);
            sendOutcome = _ => false;
            Check(offer.RunCommand(fallback).Status == CommandCode.Failure && enemies.Count == 3,
                "Failed fallback mutates Enemies");
            sendOutcome = _ => true; start = attempts;
            Check(offer.RunCommand(fallback).Status == CommandCode.Finished && attempts == start + 1 && enemies.Count == 3,
                "Fallback offers to a non-aggressor or corrupts perception");
            Check(offer.RunCommand(new Blackboard()).Status == CommandCode.Failure, "No candidates reports success");

            var interrupted = Board(a, b);
            sendOutcome = id => id == a.strRegID ? true : throw new InvalidOperationException("fixture send failure");
            bool threw = false;
            try { offer.RunCommand(interrupted); } catch (InvalidOperationException) { threw = true; }
            Check(threw && interrupted.Get<List<Ship>>("SurrenderedTo").SequenceEqual(new[] { a }), "Sent message lost after later exception");
            Check(interrupted.Get<bool>("InCombat") && interrupted.Get<List<Ship>>("Combatants").SequenceEqual(new[] { b }),
                "Exception discards unattempted opponent");

            // Execute native tree combinators: failed send reaches the next
            // response; successful send reaches the health branch continuation.
            foreach (bool success in new[] { false, true }) {
                var board = Board(a);
                sendOutcome = _ => success;
                var returnHome = new MarkerCommand(); var otherResponse = new MarkerCommand();
                var health = new SequenceCommand("health", new ICommand[] { offer, returnHome });
                var response = new SelectorCommand("response", health, otherResponse);
                Check(response.RunCommand(board).Status == CommandCode.Finished, "Native response tree cannot progress");
                Check(returnHome.Calls == (success ? 1 : 0) && otherResponse.Calls == (success ? 0 : 1),
                    "Offer result routes the native tree to the wrong continuation");
            }
        } finally {
            sendOutcome = null; controllers.Clear(); production.UnpatchSelf(); fixture.UnpatchSelf();
        }
        Console.WriteLine(checks + " production surrender accounting checks passed.");
        return 0;
    }
}
