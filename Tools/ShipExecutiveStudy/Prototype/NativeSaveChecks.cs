using System;
using System.Linq;
using HarmonyLib;
using MissionContracts;
using Newtonsoft.Json.Linq;

namespace ShipExecutivePrototype;

static class NativeSaveChecks
{
    internal static void Run(Action<string, Action> scenario, Action<bool, string> check, Definition model)
    {
        scenario("Native synchronous capture and threaded archive keep executive state with the matching world", () =>
        {
            using (var dialogue = new NativeDialogueFixtures())
            using (var fence = new NativeExecutionFence())
            {
                var world = new NativeWorld { LowFuel = true };
                using (var population = new NativeArrivalFixtures(world))
                using (var native = new NativeSaveFixtures(world, population))
                {
                    var actor = new NativeNavyActor(world, model);
                    var port = new NativeSavePort(fence, native, actor);
                    actor.Travel.BeginNativeLowFuelReturn();
                    actor.Travel.Advance(20, "normal");
                    world.Captain.jCOS = native.SavePerson(population.People[world.Captain.strID]);
                    string folder = NativeArchiveIO.Folder();
                    var captured = port.Capture(folder);
                    string digest = NativeSavePort.Digest(captured);
                    check(captured.jGameSave.Item2.objSystem.dfEpoch == 1020 && captured.dictShips.Count == world.Ships.Count && captured.jGameSave.Item2.aCustomInfos.Count(s => s.StartsWith(NativeSavePort.Key)) == 1,
                        "Native collector did not capture one complete coherent generation");
                    actor.Travel.Advance(40, "SFF");
                    world.Captain.jCOS.aMyShips = new[] { "later-live-mutation" };
                    check(NativeSavePort.Digest(captured) == digest && captured.dictShips.Values.Single(s => s.strRegID == "SUBJECT").aCOs.Single().aMyShips.Contains("SUBJECT"),
                        "Detached snapshot retained a live shallow crew DTO reference");
                    var saved = NativeArchiveIO.RoundTrip(captured, folder, true);
                    check(NativeSavePort.DigestContent(saved.Content()) == digest && saved.Read().jGameSave.Item2.aCustomInfos.Contains("UnrelatedMod=unchanged"), "Native archive changed snapshot identity or another mod's metadata: " + Difference(NativeSavePort.Content(captured), saved.Content()));
                    // Vanilla ignores unknown namespaced custom info; no plugin
                    // parser or installed patch is used by this native call.
                    AccessTools.Method(typeof(CrewSim), "SetCustomInfos").Invoke(null, new object[] { saved.Read().jGameSave.Item2.aCustomInfos });
                    actor = port.Restore(model, saved);
                    check(StarSystem.fEpoch == 1020 && actor.Machine.State.Active.Phase == "Approaching" && actor.World.Captain.ship == actor.World.Subject,
                        "Restore used later live-world state instead of the native archive");
                    check(actor.Travel.Advance(40, "PASS") && actor.World.Retired && actor.World.ArrivalCalls == 1 && Math.Abs(actor.World.Subject.fShallowRCSRemass - 295) < 1e-6,
                        "Native archive return failed exact-once arrival or route settlement");
                }
            }
        });
        scenario("Native capture rejects partial ship serialization and mixed snapshot metadata", () =>
        {
            using (var dialogue = new NativeDialogueFixtures())
            using (var fence = new NativeExecutionFence())
            {
                var world = new NativeWorld();
                using (var population = new NativeArrivalFixtures(world))
                using (var native = new NativeSaveFixtures(world, population))
                {
                    var actor = new NativeNavyActor(world, model);
                    var port = new NativeSavePort(fence, native, actor);
                    actor.Tick();
                    native.OmitShip = "SUBJECT";
                    bool refused = false;
                    try
                    {
                        port.Capture(NativeArchiveIO.Folder());
                    }
                    catch (InvalidOperationException) { refused = true; }
                    check(refused, "Native per-ship exception became a supposedly complete executive save");
                    native.OmitShip = null;
                    var first = port.Capture(NativeArchiveIO.Folder());
                    actor.Travel.Advance(10, "normal");
                    var second = port.Capture(NativeArchiveIO.Folder());
                    second.jGameSave.Item2.aCustomInfos = first.jGameSave.Item2.aCustomInfos;
                    refused = false;
                    try
                    {
                        port.Restore(model, second);
                    }
                    catch (InvalidOperationException) { refused = true; }
                    check(refused && StarSystem.fEpoch == 1010, "Mixed native world/executive generations were accepted or mutated the world before validation");
                    string folder = NativeArchiveIO.Folder();
                    var corrupt = port.Capture(folder);
                    corrupt.dictShips.Values.Single(s => s.strRegID == "SUBJECT").fShallowRCSRemass += 1;
                    var wrongWorld = NativeArchiveIO.RoundTrip(corrupt, folder, false);
                    refused = false;
                    try
                    {
                        port.Restore(model, wrongWorld);
                    }
                    catch (InvalidOperationException) { refused = true; }
                    check(refused && StarSystem.fEpoch == 1010, "Same-epoch archive with altered native hull data bypassed snapshot binding");
                }
            }
        });
        scenario("Native capture fence blocks reentrant simulation and saves during crew transfer", () =>
        {
            using (var dialogue = new NativeDialogueFixtures())
            using (var fence = new NativeExecutionFence())
            {
                var world = new NativeWorld { LowFuel = true };
                using (var population = new NativeArrivalFixtures(world))
                using (var native = new NativeSaveFixtures(world, population))
                {
                    var actor = new NativeNavyActor(world, model);
                    var port = new NativeSavePort(fence, native, actor);
                    actor.Travel.BeginNativeLowFuelReturn();
                    bool simulationBlocked = false, saveBlocked = false;
                    native.BeforeShip = _ =>
                    {
                        try
                        {
                            actor.Tick();
                        }
                        catch (InvalidOperationException) { simulationBlocked = true; }
                    };
                    port.Capture(NativeArchiveIO.Folder());
                    check(simulationBlocked, "Reentrant actor mutated the snapshot during native collection");
                    native.BeforeShip = null;
                    int wrongThreadRejections = 0;
                    var foreign = new System.Threading.Thread(() =>
                    {
                        try
                        {
                            actor.Tick();
                        }
                        catch (InvalidOperationException) { wrongThreadRejections++; }
                        try
                        {
                            port.Capture(NativeArchiveIO.Folder());
                        }
                        catch (InvalidOperationException) { wrongThreadRejections++; }
                    });
                    foreign.Start();
                    if (!foreign.Join(10000))
                        throw new TimeoutException("Foreign-thread fence probe did not finish");
                    check(wrongThreadRejections == 2, "A background thread reached native simulation or save collection");
                    using (var admission = new NativeNavyAdmission(actor))
                    {
                        bool receiptBlocked = false;
                        double pause = world.Subject.fAIPauseTimer;
                        native.BeforeShip = _ =>
                        {
                            try
                            {
                                world.AI.AddCommandLoot("TakeFuel");
                            }
                            catch (InvalidOperationException) { receiptBlocked = true; }
                        };
                        port.Capture(NativeArchiveIO.Folder());
                        check(receiptBlocked && actor.State.Inbox.Count == 0 && world.Subject.fAIPauseTimer == pause, "Raw constructor ran inside a native save snapshot");
                    }
                    native.BeforeShip = null;
                    int captures = native.Captures;
                    population.BeforeMove = _ =>
                    {
                        try
                        {
                            port.Capture(NativeArchiveIO.Folder());
                        }
                        catch (InvalidOperationException) { saveBlocked = true; }
                    };
                    actor.Travel.Advance(40, "normal");
                    check(saveBlocked && native.Captures == captures && world.Retired, "Save entered the middle of native crew transfer or prevented its completion");
                    var completed = port.Capture(NativeArchiveIO.Folder());
                    check(completed.dictShips.Values.Single(s => s.strRegID == "HOME").aCOs.Any(c => c.strID == "captain-subject"), "Fence did not release for a coherent post-arrival save");
                }
            }
        });
        scenario("Native coherent prepared arrival can resume, while standalone uncertain checkpoints cannot guess", () =>
        {
            using (var dialogue = new NativeDialogueFixtures())
            using (var fence = new NativeExecutionFence())
            {
                var world = new NativeWorld { LowFuel = true };
                using (var population = new NativeArrivalFixtures(world))
                using (var native = new NativeSaveFixtures(world, population))
                {
                    population.Add("victim", world.Subject, "IsDerelictVictim");
                    var actor = new NativeNavyActor(world, model);
                    var port = new NativeSavePort(fence, native, actor);
                    actor.Travel.RegisterArrivalIntent = null;
                    actor.Travel.AutoCheckpoint = false;
                    actor.Travel.BeginNativeLowFuelReturn();
                    actor.Travel.Advance(40, "normal");
                    check(actor.Machine.State.Active.Phase == "ArrivalPrepared" && world.ArrivalCalls == 0, "Prepared-save fixture already submitted an arrival");
                    string folder = NativeArchiveIO.Folder();
                    var image = NativeArchiveIO.RoundTrip(port.Capture(folder), folder, false);
                    actor = port.Restore(model, image);
                    check(actor.Machine.State.Active.Phase == "ArrivalReady" && actor.Machine.State.PendingArrival == null && actor.World.ArrivalCalls == 0,
                        "Coherent pre-invocation proof was discarded or replayed immediately");
                    actor.Tick();
                    check(actor.World.Retired && actor.World.ArrivalCalls == 1 && actor.Travel.Arrival.DestroyedWitnesses.Contains("victim"),
                        "Restored prepared arrival failed native participant readback");
                }
            }
        });
        scenario("Native archive preserves mixed crew outcomes after retirement without a per-arrival journal", () =>
        {
            using (var dialogue = new NativeDialogueFixtures())
            using (var fence = new NativeExecutionFence())
            {
                var world = new NativeWorld { LowFuel = true };
                using (var population = new NativeArrivalFixtures(world))
                using (var native = new NativeSaveFixtures(world, population))
                {
                    population.Tow = world.Vessel("TOW", 1e-7);
                    population.Add("guest", population.Tow);
                    population.Add("victim", world.Subject, "IsDerelictVictim");
                    var actor = new NativeNavyActor(world, model);
                    var port = new NativeSavePort(fence, native, actor);
                    string previous = System.IO.File.Exists(NativeReturnActor.CheckpointPath) ? System.IO.File.ReadAllText(NativeReturnActor.CheckpointPath) : null;
                    actor.Travel.BeginNativeLowFuelReturn();
                    actor.Travel.Advance(60, "PASS");
                    check((System.IO.File.Exists(NativeReturnActor.CheckpointPath) ? System.IO.File.ReadAllText(NativeReturnActor.CheckpointPath) : null) == previous,
                        "Integrated arrival still wrote a per-arrival file");
                    string folder = NativeArchiveIO.Folder();
                    var image = NativeArchiveIO.RoundTrip(port.Capture(folder), folder, true);
                    actor = port.Restore(model, image);
                    actor.Tick();
                    check(actor.Machine.State.ActorDisposition == "Retired" && actor.World.Retired && actor.World.ArrivalCalls == 0 && !DataHandler.mapCOs.ContainsKey("victim"),
                        "Retired archive resurrected destroyed crew or repeated arrival");
                    check(population.People["guest"].Person.ship == actor.World.Home && actor.World.Resolve("TOW") != null && AIShipManager.GetAIShipByRegID("SUBJECT") == null,
                        "Native crew/group state was replaced by executive metadata");
                }
            }
        });
        scenario("Native partial-arrival archive does not retry a crew transfer after reload", () =>
        {
            using (var dialogue = new NativeDialogueFixtures())
            using (var fence = new NativeExecutionFence())
            {
                var world = new NativeWorld { LowFuel = true };
                using (var population = new NativeArrivalFixtures(world))
                using (var native = new NativeSaveFixtures(world, population))
                {
                    population.Add("guest", world.Subject);
                    var actor = new NativeNavyActor(world, model);
                    var port = new NativeSavePort(fence, native, actor);
                    population.BeforeMove = id => { if (id == "captain-subject") throw new InvalidOperationException("injected arrival failure"); };
                    actor.Travel.BeginNativeLowFuelReturn();
                    try
                    {
                        actor.Travel.Advance(40, "normal");
                    }
                    catch (InvalidOperationException) { }
                    string folder = NativeArchiveIO.Folder();
                    var captured = port.Capture(folder);
                    string original = captured.jGameSave.Item2.aCustomInfos.Single(s => s.StartsWith(NativeSavePort.Key));
                    var record = JObject.Parse(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(original.Substring(NativeSavePort.Key.Length))));
                    record["PreparedBeforeNativeCall"] = true;
                    captured.jGameSave.Item2.aCustomInfos = new[] { NativeSavePort.Key + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(record.ToString())), "UnrelatedMod=unchanged" };
                    bool rejected = false;
                    try
                    {
                        port.Restore(model, captured);
                    }
                    catch (InvalidOperationException) { rejected = true; }
                    check(rejected && world.ArrivalCalls == 1 && ReferenceEquals(native.World, world), "A false pre-invocation marker could authorize replay of a partial transfer");
                    captured.jGameSave.Item2.aCustomInfos = new[] { "UnrelatedMod=unchanged", original };
                    var image = NativeArchiveIO.RoundTrip(captured, folder, false);
                    population.BeforeMove = null;
                    actor = port.Restore(model, image);
                    actor.Tick();
                    actor.Tick();
                    check(actor.Machine.State.Active.Phase == "ArrivalUncertain" && actor.World.ArrivalCalls == 0 && actor.World.Captain.ship == actor.World.Subject
                        && population.People["guest"].Person.ship == actor.World.Home, "Partial arrival reload replayed or erased a native crew outcome");
                }
            }
        });
        scenario("Native archive does not resurrect a captain destroyed by authored station drop-off policy", () =>
        {
            using (var dialogue = new NativeDialogueFixtures())
            using (var fence = new NativeExecutionFence())
            {
                var world = new NativeWorld { LowFuel = true };
                using (var population = new NativeArrivalFixtures(world))
                using (var native = new NativeSaveFixtures(world, population))
                {
                    world.Captain.mapConds["IsOKLGPirate"] = new Condition(null) { strName = "IsOKLGPirate", fCount = 1 };
                    var actor = new NativeNavyActor(world, model);
                    var port = new NativeSavePort(fence, native, actor);
                    actor.Travel.BeginNativeLowFuelReturn();
                    actor.Travel.Advance(60, "normal");
                    check(world.Captain.bDestroyed && actor.Machine.State.ActorDisposition == "Retired", "Native forbidden drop-off did not retire with witnessed destruction");
                    string folder = NativeArchiveIO.Folder();
                    var image = NativeArchiveIO.RoundTrip(port.Capture(folder), folder, true);
                    actor = port.Restore(model, image);
                    actor.Tick();
                    check(!DataHandler.mapCOs.ContainsKey("captain-subject") && actor.Machine.State.ActorDisposition == "Retired" && actor.World.ArrivalCalls == 0,
                        "Archive loading resurrected or replayed the destroyed captain");
                }
            }
        });
        scenario("Native patrol handback survives its native archive and resumes the actual dispatcher", () =>
        {
            using (var dialogue = new NativeDialogueFixtures())
            using (var fence = new NativeExecutionFence())
            {
                var world = new NativeWorld();
                using (var population = new NativeArrivalFixtures(world))
                using (var native = new NativeSaveFixtures(world, population))
                {
                    var actor = new NativeNavyActor(world, model);
                    var port = new NativeSavePort(fence, native, actor);
                    actor.Tick();
                    actor.Travel.Advance(20, "normal");
                    check(actor.Travel.TryHandBack(out var reason) && actor.Machine.State.Authority == "Native", "Ordinary patrol handback refused: " + reason);
                    string folder = NativeArchiveIO.Folder();
                    var captured = port.Capture(folder);
                    check(!captured.jGameSave.Item2.aCustomInfos.Any(s => s.StartsWith(NativeSavePort.Key)), "Released actor retained executive save ownership");
                    var image = NativeArchiveIO.RoundTrip(captured, folder, false);
                    var loaded = native.LoadWorld(image.Read());
                    StarSystem.fEpoch += 20;
                    loaded.Subject.objSS.NavData.TimeAdvance(loaded.Subject.objSS);
                    loaded.Pilot.BehaviorTree.RunCommand(loaded.Pilot.Blackboard);
                    check(Math.Abs(loaded.Subject.fShallowRCSRemass - 295) < 1e-6 && loaded.ArrivalCalls == 0, "Native patrol handback lost route settlement or docked with a waypoint");
                    check(!actor.Travel.Advance(1, "normal"), "Released executive still owned time advancement");
                }
            }
        });
        scenario("Native handback defers executive inbox and reaction state without discarding either", () =>
        {
            var world = new NativeWorld();
            var actor = new NativeNavyActor(world, model);
            actor.Tick();
            using (var admission = new NativeNavyAdmission(actor))
            {
                world.AI.AddCommandLoot("TurnOnAllSensors");
                check(!actor.Travel.TryHandBack(out var reason) && reason.Contains("inbox") && actor.State.Inbox.Count == 1 && actor.Machine.State.Authority == "Executive",
                    "Handback silently discarded an admitted executive input");
                actor.Tick();
                actor.Dodge("ENEMY", 1);
                check(!actor.Travel.TryHandBack(out _) && actor.Machine.State.Authority == "Executive" && actor.Machine.State.Active.Kind == "Reaction",
                    "Handback erased an active reaction to manufacture a serializable state");
            }
        });
    }
    static string Difference(JToken a, JToken b)
    {
        if (JToken.DeepEquals(a, b))
            return "";
        if (a is JObject ao && b is JObject bo)
        {
            foreach (var field in ao.Properties())
            {
                var delta = Difference(field.Value, bo[field.Name]);
                if (delta != "")
                    return field.Name + "." + delta;
            }
        }
        if (a is JArray aa && b is JArray ba)
        {
            if (aa.Count != ba.Count)
                return "length " + aa.Count + " / " + ba.Count;
            for (int i = 0; i < aa.Count; i++)
            {
                var delta = Difference(aa[i], ba[i]);
                if (delta != "")
                    return i + "." + delta;
            }
        }
        return a + " / " + b;
    }
}
