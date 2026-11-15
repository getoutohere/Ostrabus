using System;
using System.Linq;
using MissionContracts;

namespace ShipExecutivePrototype;

static class NativeArrivalChecks
{
    internal static void Run(Action<string, Action> scenario, Action<bool, string> check, Definition model)
    {
        scenario("Native arrival accounts for group crew through authored transfer and destruction branches", () =>
        {
            using (var dialogue = new NativeDialogueFixtures())
            using (var fence = new NativeExecutionFence())
            {
                var world = new NativeWorld { LowFuel = true };
                using (var fixtures = new NativeArrivalFixtures(world))
                {
                    fixtures.Tow = world.Vessel("TOW", 1e-7);
                    var guest = fixtures.Add("guest", fixtures.Tow);
                    var victim = fixtures.Add("victim", world.Subject, "IsDerelictVictim");
                    var pirate = fixtures.Add("hostile", fixtures.Tow, reputation: -100);
                    var actor = new NativeReturnActor(world, model);
                    var observer = new NativeArrivalObservation(actor);
                    int registrations = 0;
                    actor.RegisterArrivalIntent = () => registrations++;
                    actor.BeginNativeLowFuelReturn();
                    check(actor.Advance(40, "normal") && world.Retired && actor.Machine.State.ActorDisposition == "Retired", "Native arrival did not account for legitimate mixed outcomes");
                    check(world.ArrivalCalls == 1 && registrations == 1 && actor.Arrival.Members.SequenceEqual(new[] { "SUBJECT", "TOW" }) && actor.Arrival.Crew.Length == 4,
                        "Arrival did not capture the complete affected group");
                    check(world.Captain.ship == world.Home && guest.Person.ship == world.Home && guest.Claims.Contains("HOME") && !fixtures.People["captain-subject"].Claims.Contains("SUBJECT") && guest.Queued.Contains("WanderSoon"),
                        "Native successful drop-off sequence was not preserved");
                    check(victim.Person.bDestroyed && pirate.Person.bDestroyed && actor.Arrival.DestroyedWitnesses.OrderBy(s => s).SequenceEqual(new[] { "hostile", "victim" }),
                        "Authored invalid/faction-hostile drop-off was replaced with forced survival");
                    check(world.Subject.HideFromSystem && world.Subject.strAIDespawnedAt == "HOME" && AIShipManager.GetAIShipByRegID("SUBJECT") == null && world.Resolve("TOW") == fixtures.Tow,
                        "Native retirement lost another hull or retained its controller");
                }
            }
        });
        scenario("Native arrival missing crew stays uncertain after hull unregister and can reconcile without another docking", () =>
        {
            using (var dialogue = new NativeDialogueFixtures())
            {
                var world = new NativeWorld { LowFuel = true };
                using (var fixtures = new NativeArrivalFixtures(world))
                {
                    var guest = fixtures.Add("guest", world.Subject);
                    var actor = new NativeReturnActor(world, model);
                    var observer = new NativeArrivalObservation(actor);
                    actor.RegisterArrivalIntent = () => { };
                    fixtures.AfterNativeBody = () => DataHandler.mapCOs.Remove("guest");
                    actor.BeginNativeLowFuelReturn();
                    check(!actor.Advance(60, "normal") && actor.Machine.State.PendingArrival != null && actor.Machine.State.ActorDisposition == "Active", "Missing crew was mistaken for destruction/success");
                    actor.Tick();
                    check(world.ArrivalCalls == 1 && actor.Machine.State.Active.Phase == "ArrivalUncertain", "Unregistered hull retried its arrival");
                    DataHandler.mapCOs["guest"] = guest.Person;
                    actor.Tick();
                    check(world.ArrivalCalls == 1 && world.Retired && actor.Machine.State.ActorDisposition == "Retired", "Readback was blocked permanently by native unregister");
                }
            }
        });
        scenario("Native partial arrival fails closed and the capture fence releases after exceptions", () =>
        {
            using (var dialogue = new NativeDialogueFixtures())
            using (var fence = new NativeExecutionFence())
            {
                var world = new NativeWorld { LowFuel = true };
                using (var fixtures = new NativeArrivalFixtures(world))
                {
                    fixtures.Add("guest", world.Subject);
                    var actor = new NativeReturnActor(world, model);
                    var observer = new NativeArrivalObservation(actor);
                    actor.RegisterArrivalIntent = () => { };
                    fixtures.BeforeMove = id => { if (id == "captain-subject") throw new InvalidOperationException("injected move failure"); };
                    actor.BeginNativeLowFuelReturn();
                    bool threw = false;
                    try
                    {
                        actor.Advance(40, "normal");
                    }
                    catch (InvalidOperationException) { threw = true; }
                    check(threw && actor.Machine.State.PendingArrival != null && fixtures.People["guest"].Person.ship == world.Home && world.Captain.ship == world.Subject,
                        "Partial native effect was erased or declared completed");
                    using (fence.Capture())
                    {
                        check(world.ArrivalCalls == 1, "Save fence leaked after arrival failure");
                    }
                    actor.Tick();
                    check(world.ArrivalCalls == 1 && actor.Machine.State.Active.Phase == "ArrivalUncertain", "Partial transfer was replayed");
                }
            }
        });
    }
}
