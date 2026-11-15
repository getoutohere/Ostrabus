using System;
using System.Linq;
using MissionContracts;
using Ostranauts.Ships.Comms;

namespace ShipExecutivePrototype;

static class NavyDialogueChecks
{
    const string Prefix = "STKShipSurrender_PlayerDemand_";
    static Interaction Message(NativeWorld world, string suffix)
    {
        var interaction = DataHandler.GetInteraction(Prefix + suffix);
        interaction.objUs = CrewSim.coPlayer;
        interaction.objThem = world.Captain;
        interaction.strChainOwner = CrewSim.coPlayer.strID;
        interaction.strChainStart = Prefix + "Intro";
        return interaction;
    }
    static void Condition(CondOwner captain, string name) => captain.mapConds[name] = new Condition(null) { strName = name, fCount = 1 };

    internal static void Run(Action<string, Action> scenario, Action<bool, string> check, Definition model)
    {
        scenario("Navy shipped surrender reply order and target-lock rules execute through native evaluators", () =>
        {
            using (var dialogue = new NativeDialogueFixtures())
            {
                foreach (string reason in new[] { "Violated", "Cooldown", "BreakLock", "Friendly" })
                {
                    var world = new NativeWorld();
                    NativeDialogueFixtures.Bind(world);
                    var actor = new NativeNavyActor(world, model);
                    actor.Tick();
                    using (var admission = new NativeNavyAdmission(actor))
                    {
                        if (reason == "Violated")
                        {
                            Condition(world.Captain, "IsAngryCeasefireViolated");
                            Condition(world.Captain, "IsSTKShipSurrender_PlayerDemand_Fail_Cooldown");
                        }
                        if (reason == "Cooldown")
                            Condition(world.Captain, "IsSTKShipSurrender_PlayerDemand_Fail_Cooldown");
                        if (reason == "BreakLock")
                            CrewSim.coPlayer.ship.shipCombatTarget = world.Subject;
                        var intro = Message(world, "Intro");
                        CrewSim.coPlayer.ship.Comms.SendMessage(intro);
                        StarSystem.fEpoch += 2;
                        dialogue.Deliver();
                        check(actor.State.Inbox.Single().Command == "SurrenderConsiderPlayerDemand", "Native receive did not admit authored surrender consideration");
                        actor.Tick();
                        var reply = dialogue.Pending.Single().Interaction;
                        check(reply.strName == Prefix + "Intro_" + (reason == "Friendly" ? "Allow_" : "Deny_") + reason,
                            "Native reply selection diverged for " + reason + ": " + reply.strName);
                        check(ReferenceEquals(reply.objUs, world.Captain) && ReferenceEquals(reply.objThem, CrewSim.coPlayer), "Native reply lost speaker swap");
                        dialogue.ClearPending();
                    }
                }
                check(!dialogue.LogMessages.Any(m => m.Contains("No such") || m.Contains("Unable to load")), "Surrender evaluation used missing asset definitions: " + string.Join("; ", dialogue.LogMessages.Where(m => m.Contains("No such") || m.Contains("Unable to load"))));
            }
        });
        scenario("Navy received surrender message restores canonical roles and does not replay reception effects", () =>
        {
            using (var dialogue = new NativeDialogueFixtures())
            {
                var world = new NativeWorld();
                NativeDialogueFixtures.Bind(world);
                var actor = new NativeNavyActor(world, model);
                actor.Tick();
                using (var admission = new NativeNavyAdmission(actor))
                {
                    CrewSim.coPlayer.ship.Comms.SendMessage(Message(world, "Intro"));
                    StarSystem.fEpoch += 2;
                    dialogue.Deliver();
                    int effects = dialogue.Effects.Count;
                    actor = NativeNavyActor.Restore(model, actor.Save());
                    admission.Actor = actor;
                    var restored = actor.World.Subject.Comms.GetMessages().Single().Interaction;
                    check(ReferenceEquals(restored.objUs, CrewSim.coPlayer) && ReferenceEquals(restored.objThem, actor.World.Captain), "Native comms DTO restored stale crew references");
                    check(restored.strChainOwner == "captain-player" && dialogue.Effects.Count == effects && actor.State.Inbox.Count == 1,
                        "Load replayed reception or lost chain context");
                    actor.Tick();
                    check(dialogue.Pending.Single().Interaction.strName == Prefix + "Intro_Allow_Friendly" && actor.State.Inbox.Count == 0,
                        "Restored message failed native reply selection");
                    dialogue.ClearPending();
                    actor = NativeNavyActor.Restore(model, actor.Save());
                    admission.Actor = actor;
                    actor.Tick();
                    check(dialogue.Pending.Count == 0 && dialogue.Effects.Count == effects && actor.State.Inbox.Count == 0,
                        "Completed dialogue replayed after second reload");
                }
            }
        });
        scenario("Navy expired and unavailable surrender requests retain native refusal semantics", () =>
        {
            using (var dialogue = new NativeDialogueFixtures())
            {
                foreach (bool expired in new[] { true, false })
                {
                    var world = new NativeWorld();
                    NativeDialogueFixtures.Bind(world);
                    var actor = new NativeNavyActor(world, model);
                    actor.Tick();
                    using (var admission = new NativeNavyAdmission(actor))
                    {
                        CrewSim.coPlayer.ship.Comms.SendMessage(Message(world, "Intro"));
                        StarSystem.fEpoch += 2;
                        dialogue.Deliver();
                        if (expired)
                            StarSystem.fEpoch += 121;
                        else
                        {
                            Condition(world.Captain, "Unconscious");
                        }
                        actor = NativeNavyActor.Restore(model, actor.Save());
                        admission.Actor = actor;
                        actor.Tick();
                        check(dialogue.Pending.Count == 0 && actor.State.Inbox.Count == 0, "Unavailable request generated a forced reply: " + expired);
                    }
                }
            }
        });
        scenario("Navy shipped boarding reply admits ComeHere on sender and survives reload without resending", () =>
        {
            using (var dialogue = new NativeDialogueFixtures())
            {
                var world = new NativeWorld();
                NativeDialogueFixtures.Bind(world);
                var actor = new NativeNavyActor(world, model);
                actor.Tick();
                using (var admission = new NativeNavyAdmission(actor))
                {
                    var demand = Message(world, "Gambit_DemandBoarding");
                    var allowed = DataHandler.GetInteraction(Prefix + "Gambit_DemandBoarding_Allow");
                    demand.AssignReplyRoles(allowed, new[] { allowed.strName }, false);
                    check(allowed.Triggered(), "Shipped boarding allowance conditions unexpectedly fail");
                    world.Subject.Comms.SendMessage(allowed);
                    var input = actor.State.Inbox.Single();
                    check(input.Command == "ComeHere" && input.AffectedCharacter == world.Captain.strID && input.Counterparty == "PLAYER" && input.Payload.SequenceEqual(new[] { "PLAYER" }),
                        "Boarding command was applied to requester instead of responding captain");
                    dialogue.ClearPending(); // transport/receiver notification is outside this saved-message test
                    actor = NativeNavyActor.Restore(model, actor.Save());
                    admission.Actor = actor;
                    check(actor.State.Inbox.Count == 1 && actor.World.Subject.Comms.GetMessages().Single().Interaction.strName == allowed.strName,
                        "Boarding intent/log failed to restore");
                    actor.World.Subject.fAIPauseTimer = StarSystem.fEpoch + 10; // inspect approach admission while its native movement pause is active
                    actor.Tick();
                    check(actor.State.Inbox.Count == 1 && actor.State.Inbox.Single().Phase == "Approach" && dialogue.Pending.Count == 0,
                        "Restored boarding resent reply or skipped explicit approach");
                    check(NativeClock.ExcludedSteeringCalls == 0, "Dialogue fixture unexpectedly entered excluded physical steering");
                }
            }
        });
        scenario("Navy native in-flight message queue survives request and reply reloads without duplicate delivery", () =>
        {
            using (var dialogue = new NativeDialogueFixtures())
            {
                var world = new NativeWorld();
                NativeDialogueFixtures.Bind(world);
                var actor = new NativeNavyActor(world, model);
                actor.Tick();
                using (var admission = new NativeNavyAdmission(actor))
                {
                    var sent = CrewSim.coPlayer.ship.Comms.SendMessage(Message(world, "Intro"));
                    check(!CrewSim.system.SendMessage(sent) && dialogue.Pending.Count == 1, "Native queue accepted duplicate message identity");
                    actor = NativeNavyActor.Restore(model, actor.Save());
                    admission.Actor = actor;
                    var request = dialogue.Pending.Single();
                    check(!ReferenceEquals(request, sent) && request.ID == sent.ID && request.Interaction.objThem == actor.World.Captain,
                        "Pending request retained old objects or lost identity");
                    StarSystem.fEpoch = request.AvailableTime;
                    dialogue.Deliver();
                    check(actor.State.Inbox.Count == 0 && dialogue.Effects.Count == 0, "Delivery violated native strict timestamp boundary");
                    StarSystem.fEpoch += .1;
                    dialogue.Deliver();
                    dialogue.Deliver();
                    check(actor.State.Inbox.Count == 1 && dialogue.Effects.Count == 1, "Request was delivered more than once");
                    actor.Tick();
                    var reply = dialogue.Pending.Single();
                    actor = NativeNavyActor.Restore(model, actor.Save());
                    admission.Actor = actor;
                    check(dialogue.Pending.Single().ID == reply.ID && actor.State.Inbox.Count == 0 && dialogue.Effects.Count == 1,
                        "Reply reload replayed command/reception or lost pending response");
                    StarSystem.fEpoch = reply.AvailableTime + .1;
                    dialogue.Deliver();
                    dialogue.Deliver();
                    check(dialogue.Pending.Count == 0 && dialogue.Effects.Count == 2 && CrewSim.coPlayer.ship.Comms.GetMessages().Count == 2,
                        "Reply delivery was lost or repeated");
                    actor = NativeNavyActor.Restore(model, actor.Save());
                    admission.Actor = actor;
                    dialogue.Deliver();
                    actor.Tick();
                    check(dialogue.Pending.Count == 0 && dialogue.Effects.Count == 2 && actor.State.Inbox.Count == 0, "Completed conversation reappeared after reload");
                }
            }
        });
    }
}
