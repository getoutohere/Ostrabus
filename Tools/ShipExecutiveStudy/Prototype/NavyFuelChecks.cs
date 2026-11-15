using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MissionContracts;
using Ostranauts.Ships.Commands;

namespace ShipExecutivePrototype;

static class NavyFuelChecks
{
    static NativeWorld World()
    {
        var world = new NativeWorld();
        world.Subject.fShallowRCSRemass = 20;
        world.PhysicalFuel = new Dictionary<string, double> { ["SUBJECT"] = 100, ["ENEMY"] = 120 };
        world.FuelDonor = "ENEMY";
        world.FuelMinimum = 60;
        return world;
    }

    internal static void Run(Action<string, Action> scenario, Action<bool, string> check, Definition model)
    {
        scenario("Navy TakeFuel matches native receipt, transfers and completion across two fresh reloads", () =>
        {
            using (var provider = new NativeFuelFixtures())
            {
                var reference = World();
                var command = new TakeFuel(reference.Pilot);
                check(provider.SyncCalls == 1 && reference.PhysicalFuel["SUBJECT"] == 20 && reference.Subject.fAIPauseTimer == 1000.5,
                    "Native receipt characterization changed");
                var expected = new List<CommandCode>();
                foreach (double time in new[] { 1000.25, 1002.0, 1003.0, 1004.0 })
                {
                    StarSystem.fEpoch = time;
                    expected.Add(command.RunCommand(reference.Pilot.Blackboard).Status);
                }
                var events = provider.Events.ToArray();
                check(reference.PhysicalFuel["SUBJECT"] == 60 && reference.PhysicalFuel["ENEMY"] == 80 && expected.Last() == CommandCode.Finished,
                    "Native physical-provider transfer baseline failed");
                provider.Events.Clear();
                int syncBefore = provider.SyncCalls;
                var world = World();
                var actor = new NativeNavyActor(world, model);
                actor.Tick();
                using (var admission = new NativeNavyAdmission(actor))
                {
                    world.AI.AddCommandLoot("TakeFuel", new[] { "PLAYER" });
                    check(provider.SyncCalls == syncBefore + 1 && world.PhysicalFuel["SUBJECT"] == 20,
                        "Raw receipt deferred native SyncFuel");
                    actor = NativeNavyActor.Restore(model, actor.Save());
                    admission.Actor = actor;
                    check(!ReferenceEquals(world.PhysicalFuel, actor.World.PhysicalFuel) && actor.World.FuelDonor == "ENEMY", "Reload retained a physical fixture object instead of saved inputs");
                    StarSystem.fEpoch = 1000.25;
                    actor.Tick();
                    check(actor.World.PhysicalFuel["SUBJECT"] == 20 && actor.State.Inbox.Count == 1, "Reload before dispatch restarted or skipped receipt pause");
                    StarSystem.fEpoch = 1002;
                    actor.Tick();
                    check(actor.World.PhysicalFuel["SUBJECT"] == 50 && actor.World.PhysicalFuel["ENEMY"] == 90, "First transfer changed native amount");
                    actor = NativeNavyActor.Restore(model, actor.Save());
                    admission.Actor = actor;
                    StarSystem.fEpoch = 1002.5;
                    actor.Tick();
                    check(actor.World.PhysicalFuel["SUBJECT"] == 50 && actor.World.Subject.fAIPauseTimer == 1003, "Mid-transfer reload lost fuel or reset pause");
                    StarSystem.fEpoch = 1003;
                    actor.Tick();
                    StarSystem.fEpoch = 1004;
                    actor.Tick();
                    check(actor.State.Inbox.Count == 0 && actor.World.PhysicalFuel.Values.Sum() == 140 && actor.World.Subject.fAIDockingExpire == 0,
                        "Transfer failed completion/conservation");
                    check(provider.SyncCalls == syncBefore + 1 && provider.Events.SequenceEqual(events), "Reload replayed constructor or diverged from native transfer events");
                }
            }
        });
        scenario("Navy delayed and typed fuel receipts retain native shared clock ordering", () =>
        {
            using (var provider = new NativeFuelFixtures())
            {
                var world = World();
                var actor = new NativeNavyActor(world, model);
                actor.Tick();
                using (var admission = new NativeNavyAdmission(actor))
                {
                    world.AI.AddCommand(new TakeFuel(world.Pilot));
                    StarSystem.fEpoch = 1005;
                    world.AI.AddCommandLoot("TakeFuel");
                    actor.Tick();
                    check(world.PhysicalFuel["SUBJECT"] == 20 && world.Subject.fAIPauseTimer == 1005.5 && provider.SyncCalls == 2,
                        "Dispatch restored older queued timer or reconstructed typed constructor");
                    StarSystem.fEpoch = 1006;
                    actor.Tick();
                    check(world.PhysicalFuel["SUBJECT"] == 50 && world.Subject.fAIPauseTimer == 1007, "Delayed dispatch restarted receipt delay");
                    // A later native constructor legitimately replaces the shared pause.
                    world.AI.AddCommandLoot("TakeFuel");
                    StarSystem.fEpoch = 1006.5;
                    actor.Tick();
                    check(world.PhysicalFuel["SUBJECT"] == 50 && world.Subject.fAIPauseTimer == 1007.5 && provider.SyncCalls == 3,
                        "Running directive overwrote a later native receipt's shared timer/effect");
                }
            }
        });
        scenario("Navy full inbox refuses raw fuel construction before side effects", () =>
        {
            using (var provider = new NativeFuelFixtures())
            {
                var world = World();
                var actor = new NativeNavyActor(world, model);
                using (var admission = new NativeNavyAdmission(actor))
                {
                    for (int i = 0; i < 16; i++)
                        world.AI.AddCommandLoot("Shoot");
                    bool refused = false;
                    try
                    {
                        world.AI.AddCommandLoot("TakeFuel");
                    }
                    catch (InvalidOperationException) { refused = true; }
                    check(refused && provider.SyncCalls == 0 && world.PhysicalFuel["SUBJECT"] == 100, "Capacity rejection happened after irreversible constructor effect");
                }
            }
        });
        scenario("Navy external native takeover does not reconstruct an admitted fuel side effect", () =>
        {
            using (var provider = new NativeFuelFixtures())
            {
                var world = World();
                var actor = new NativeNavyActor(world, model);
                actor.Tick();
                using (var admission = new NativeNavyAdmission(actor))
                {
                    world.AI.AddCommandLoot("RaceComms", new[] { "IsPractice" });
                    world.AI.AddCommandLoot("TakeFuel");
                    StarSystem.fEpoch = 1002;
                    actor.Tick();
                    var queue = (Queue<ICommand>)AccessTools.Field(typeof(QueuedCommandsNode), "commandQueue").GetValue(world.Pilot.PriorityQueue);
                    check(actor.Machine.State.Authority == "Native" && queue.Last() is TakeFuel && provider.SyncCalls == 1 && world.Subject.fAIPauseTimer == 1000.5,
                        "Native handback replayed queued receipt constructor");
                    queue.Last().RunCommand(world.Pilot.Blackboard);
                    check(world.PhysicalFuel["SUBJECT"] == 50 && world.PhysicalFuel["ENEMY"] == 90, "Reconstructed native command lost base binding");
                }
            }
        });
        scenario("Navy fuel receipt during a reaction restores the latest shared clock without replay", () =>
        {
            using (var provider = new NativeFuelFixtures())
            {
                var world = World();
                var actor = new NativeNavyActor(world, model);
                actor.Tick();
                actor.Dodge("ENEMY", 1);
                StarSystem.fEpoch++;
                actor.Tick(1);
                using (var admission = new NativeNavyAdmission(actor))
                {
                    world.AI.AddCommandLoot("TakeFuel");
                    double pause = world.Subject.fAIPauseTimer, docking = world.Subject.fAIDockingExpire;
                    string saved = actor.Save();
                    actor = NativeNavyActor.Restore(model, saved);
                    admission.Actor = actor;
                    check(actor.World.Subject.fAIPauseTimer == pause && actor.State.PauseRemaining == pause && actor.World.Subject.fAIDockingExpire == docking,
                        "Reaction restoration overwrote a later receipt's shared clock");
                    StarSystem.fEpoch += 11;
                    actor.Tick(11);
                    actor.Tick();
                    check(provider.SyncCalls == 1 && actor.World.PhysicalFuel["SUBJECT"] == 50 && actor.World.PhysicalFuel["ENEMY"] == 90,
                        "Interrupted fuel receipt replayed constructor or lost transfer progress");
                    var envelope = Newtonsoft.Json.Linq.JObject.Parse(saved);
                    var profile = Newtonsoft.Json.Linq.JObject.Parse((string)envelope["Profile"]);
                    profile["Inbox"][0]["ConstructorRan"] = false;
                    envelope["Profile"] = profile.ToString();
                    bool refused = false;
                    try
                    {
                        NativeNavyActor.Restore(model, envelope.ToString());
                    }
                    catch (InvalidOperationException) { refused = true; }
                    check(refused && provider.SyncCalls == 1, "Malformed saved receipt could replay native constructor");
                }
            }
        });
    }
}
