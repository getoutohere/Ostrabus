using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json;
using Ostranauts.Core;
using Ostranauts.Ships;
using Ostranauts.Ships.Commands;
using UnityEngine;

namespace ShipExecutivePrototype;

static class Program
{
    static int checks;
    static object AllocationSample;
    static readonly List<object> Results = new List<object>();
    // Candidate 1.1 adapter suite. Reviewed 1.0 results remain versioned evidence;
    // accepting this assembly enables qualification, not production adoption.
    const string ExpectedGameAssemblySha256 = "a79a5081d9c94307a45fe1d0b55ff1aa1d0af6ea8dfce1f9acc5961cf260ef07";
    static void Check(bool ok, string message)
    {
        checks++;
        if (!ok)
            throw new Exception(message);
    }
    static void Close(double a, double b, string message) => Check(Math.Abs(a - b) <= 1e-18 + 1e-10 * Math.Abs(b), message + $" ({a} vs {b})");
    static void Case(string name, Action body)
    {
        int before = checks;
        try
        {
            body();
            Results.Add(new
            {
                name,
                passed = true,
                assertions = checks - before
            });
            Console.WriteLine("PASS " + name);
        }
        catch (Exception e) { Results.Add(new { name, passed = false, assertions = checks - before, error = e.ToString() }); Console.WriteLine("FAIL " + name + ": " + e); }
    }

    static Executive Start(NativeWorld world, bool home = false)
    {
        if (home)
            new TargetData(world.Home).ApplyToShip(world.Subject);
        var executive = new Executive(world, world.Pilot, world.AI);
        for (int i = 0; i < 3; i++)
            executive.Tick();
        return executive;
    }

    static Executive Combat(NativeWorld world)
    {
        var executive = Start(world);
        world.Threat = true;
        executive.Tick();
        executive.Tick();
        Check(world.NativeShotRequests > 0, "Native combat steering/Shoot request was not reached");
        return executive;
    }

    static string Hash(string path)
    {
        using (var sha = System.Security.Cryptography.SHA256.Create())
        using (var file = File.OpenRead(path))
            return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
    }

    static int Main()
    {
        Directory.CreateDirectory("obj/ShipExecutivePrototype");
        var boundary = new Harmony("offline.executive-prototype.world");
        var services = new Harmony("offline.executive-prototype.retained-services");
        try
        {
            if (Hash(typeof(Ship).Assembly.Location) != ExpectedGameAssemblySha256)
                throw new InvalidOperationException("Native assembly changed: revalidate policy and adapters before updating the prototype baseline.");
            NativeClock.Install();
            NativeWorld.Install(boundary);
            foreach (string name in new[] { "FlightPilotAvailabilityPatch", "FlightControlHandoffPatch", "ShallowTorchShutdownPatch", "ShallowRcsTorchReleasePatch", "LowFuelRetreatProgressPatch" })
                services.CreateClassProcessor(typeof(Ostrabus.OstrabusPlugin).Assembly.GetType("Ostrabus.ShipNavigation." + name, true)).Patch();
            foreach (string name in new[] { "AIFlightFuelPathPatch", "AIFlightFuelManualPatch", "AIFlightFuelSavePatch", "AIFlightFuelLoadPatch", "AIFlightPhysicalFuelPatch" })
                services.CreateClassProcessor(typeof(Ostrabus.OstrabusPlugin).Assembly.GetType("Ostrabus.MiscBugFixes." + name, true)).Patch();

            Case("native Navy patrol -> combat -> low-fuel retreat -> station arrival", () =>
            {
                var w = new NativeWorld();
                var e = Combat(w);
                w.LowFuel = true;
                e.Tick();
                Check(ReferenceEquals(w.Subject.shipScanTarget, w.Home), "Native retreat did not choose home");
                Check(e.Advance(60, "SFF"), "Retreat route was not advanced");
                Check(w.Retired && w.ArrivalCalls == 1 && w.ClearanceCalls == 1, "Native Dock did not request a single acknowledged station arrival");
                Check(ReferenceEquals(w.Captain.ship, w.Home), "Scene fixture did not receive native transfer request");
                e.Tick();
                e.Tick();
                Check(w.ArrivalCalls == 1, "Terminal request replayed");
                Close(w.Subject.fShallowRCSRemass, 295, "Native route fuel did not settle exactly once");
                Close(StarSystem.fEpoch, 1060, "Residual interval was lost");
            });

            Case("ordinary patrol admission cannot monopolize combat; stale lease rejected", () =>
            {
                var w = new NativeWorld();
                var e = Start(w);
                long old = e.State.Generation;
                w.Subject.objSS.vVelX = 1e-10;
                w.Subject.objSS.vAccIn = new Vector2(2, 3);
                e.Admit(w.Pilot.BehaviorTree.FindCommand(typeof(FlyToPatrol)));
                w.Threat = true;
                e.Tick();
                Check(w.Pilot.Blackboard.Get<bool>("InCombat"), "Patrol suppressed native threat response");
                Check(!e.AcceptCompletion(old), "Old mission completion accepted");
                Check(w.Subject.objSS.NavData == null && w.Subject.objSS.vAccIn == default(Vector2), "Ownership handoff left route or torch input active");
                Close(w.Subject.objSS.vVelX, 1e-10, "Handoff changed physical velocity");
            });

            Case("fresh-object checkpoint resumes native nested retreat and route fuel", () =>
            {
                var w = new NativeWorld();
                var e = Combat(w);
                w.LowFuel = true;
                e.Tick();
                Check(e.Advance(20, "normal"), "Could not reach midpoint");
                string sidecar = e.Save(), native = WorldCheckpoint.Capture(w);
                File.WriteAllText("obj/ShipExecutivePrototype/retreat-sidecar.json", sidecar);
                Check(!e.CanHandBack(out _), "Unsafe retreat handback was incorrectly certified");
                Check(e.Advance(40, "SFF"), "Uninterrupted retreat failed");
                double fuel = w.Subject.fShallowRCSRemass, x = w.Subject.objSS.vPosx;
                var restored = WorldCheckpoint.Restore(native);
                var loaded = new Executive(restored, restored.Pilot, restored.AI);
                loaded.Load(sidecar);
                Check(loaded.Policy.Commands.ById.Values.OfType<LowFuelRetreat>().Any(r => (bool)AccessTools.Field(typeof(LowFuelRetreat), "_retreating").GetValue(r)), "Private native retreat progress was lost");
                Check(ReferenceEquals(restored.Subject.shipScanTarget, restored.Home), "Saved goal did not rebind to canonical home");
                Check(loaded.Advance(40, "PASS"), "Reloaded retreat failed");
                Check(restored.Retired && restored.ArrivalCalls == 1, "Reload did not produce one arrival");
                Close(restored.Subject.fShallowRCSRemass, fuel, "Reload replayed or omitted fuel");
                Close(restored.Subject.objSS.vPosx, x, "Reload changed final route position");
            });

            Case("normal / SFF / shuttle labels share route boundary semantics", () =>
            {
                double expected = -1;
                foreach (var variant in new[] { ("normal", 10), ("SFF", 60), ("PASS", 20) })
                {
                    var w = new NativeWorld();
                    var e = Start(w, home: true);
                    int elapsed = 0;
                    while (elapsed < 60)
                    {
                        Check(e.Advance(variant.Item2, variant.Item1), "Advance failed");
                        elapsed += variant.Item2;
                    }
                    Check(w.Retired && w.ArrivalCalls == 1, "Interval partition changed arrival");
                    Close(StarSystem.fEpoch, 1060, "Interval partition changed clock");
                    if (expected < 0)
                        expected = w.Subject.fShallowRCSRemass;
                    else
                        Close(w.Subject.fShallowRCSRemass, expected, "Interval partition changed fuel");
                }
            });

            Case("no-pilot refusal waits; existing course can finish without pilot", () =>
            {
                var w = new NativeWorld();
                w.PilotAvailable = false;
                new TargetData(w.Home).ApplyToShip(w.Subject);
                var e = new Executive(w, w.Pilot, w.AI);
                e.Policy.Root.Children.Last().Cursor = 2;
                w.Subject.objSS.vVelX = 1e-10;
                Check(e.Tick() == CommandCode.Running && w.Subject.objSS.NavData == null, "Unavailable pilot falsely completed flight");
                Check(e.Policy.Root.Children.Last().Cursor == 2 && w.ArrivalCalls == 0, "Unavailable pilot advanced into Dock");
                double x = w.Subject.objSS.vPosx;
                Check(e.Advance(20, "PASS"), "Pilotless coast failed");
                Close(w.Subject.objSS.vPosx, x + 20e-10, "Coast changed inertial velocity");
                w.PilotAvailable = true;
                e.Tick();
                Check(w.Subject.objSS.NavData != null, "Pilot did not recover planning");
                w.PilotAvailable = false;
                Check(e.Advance(20, "SFF"), "Existing route did not progress unmanned");
                Check(w.Subject.fShallowRCSRemass < 300 && w.ArrivalCalls == 0, "Unmanned playback fuel or arrival mismatch");
                Check(e.Advance(30, "PASS") && w.ArrivalCalls == 0, "Pilotless arrival invoked docking");
                w.PilotAvailable = true;
                e.Tick();
                Check(w.Retired, "Pilot did not resume arrival after recovery");
            });

            Case("failed native approach cannot fall through to Dock", () =>
            {
                var w = new NativeWorld();
                new TargetData(w.Home).ApplyToShip(w.Subject);
                var e = new Executive(w, w.Pilot, w.AI);
                e.Policy.Root.Children.Last().Cursor = 2;
                w.PlanFails = true;
                for (int i = 0; i < 6; i++)
                    e.Tick();
                Check(w.ArrivalCalls == 0 && e.Policy.Root.Children.Last().Cursor == 2, "Failed approach advanced to arrival");
            });

            Case("native arrival without observed effect waits rather than replaying", () =>
            {
                var w = new NativeWorld();
                var e = Start(w, home: true);
                w.RefuseArrival = true;
                Check(!e.Advance(60, "SFF"), "Unacknowledged effect was marked successful");
                Check(e.State.PendingEffect != null && w.ArrivalCalls == 1, "Unacknowledged request lost");
                e.Tick();
                e.Tick();
                Check(w.ArrivalCalls == 1, "Unacknowledged effect replayed");
                Check(!e.CanHandBack(out _), "Unresolved native effect was exportable");
                string sidecar = e.Save(), native = WorldCheckpoint.Capture(w);
                var restored = WorldCheckpoint.Restore(native);
                var loaded = new Executive(restored, restored.Pilot, restored.AI);
                loaded.Load(sidecar);
                loaded.Tick();
                Check(restored.ArrivalCalls == 0 && loaded.State.PendingEffect != null, "Reload replayed uncertain arrival");
            });

            Case("native damage/surrender selection preserves authored return", () =>
            {
                var w = new NativeWorld();
                var e = Combat(w);
                w.Damaged = true;
                e.Tick();
                Check(!w.Pilot.Blackboard.Get<bool>("InCombat"), "Native surrender did not end combat");
                Check(w.Pilot.Blackboard.Get<List<Ship>>("SurrenderedTo")?.Contains(w.Enemy) == true, "Native surrender state was not retained");
                Check(ReferenceEquals(w.Subject.shipScanTarget, w.Home), "Native damaged return did not choose home");
                // Native perception next removes hostility after accepted surrender.
                w.Threat = false;
                e.Tick();
                Check(ReferenceEquals(w.Subject.shipScanTarget, w.Home), "Suspended patrol overwrote authored return");
            });

            Case("typed blackboard retains empty lists, references and emergency tuple", () =>
            {
                var w = new NativeWorld();
                var e = Start(w);
                w.Pilot.Blackboard.Set("Empty", new List<Ship>());
                w.Pilot.Blackboard.Set("EmergencySignal", new Ostranauts.Core.Models.Tuple<double, ShipSitu>(999, w.Home.objSS));
                w.Pilot.Blackboard.Set("Double", 1.25);
                w.Pilot.Blackboard.Set("Float", .5f);
                string json = e.Save();
                var restored = new NativeWorld();
                var loaded = new Executive(restored, restored.Pilot, restored.AI);
                loaded.Load(json);
                Check(restored.Pilot.Blackboard.Get<List<Ship>>("Empty")?.Count == 0, "Empty collection was dropped");
                Check(restored.Pilot.Blackboard.Get<Ostranauts.Core.Models.Tuple<double, ShipSitu>>("EmergencySignal")?.Item1 == 999, "Emergency tuple was dropped");
                Check(restored.Pilot.Blackboard.Get<double>("Double") == 1.25 && restored.Pilot.Blackboard.Get<float>("Float") == .5f, "Primitive types changed");
                restored.Pilot.Blackboard.Set("Unsupported", new object());
                bool blocked = false;
                try
                {
                    loaded.Save();
                }
                catch (InvalidOperationException) { blocked = true; }
                Check(blocked, "Unknown state was silently discarded");
            });

            Case("directive FIFO, one-shot and fresh-graph round trip", () =>
            {
                var w = new NativeWorld();
                var e = Start(w);
                e.Admit(new TurnOnAllSensors(w.Pilot), once: true);
                e.Admit(new TurnOffAllActiveSensors(w.Pilot));
                string json = e.Save();
                Check(!e.CanHandBack(out _), "Unsafe native queue export was certified");
                var restored = new NativeWorld();
                var loaded = new Executive(restored, restored.Pilot, restored.AI);
                loaded.Load(json);
                Check(loaded.Save().Contains("\"Once\":true"), "One-shot marker lost");
                loaded.Tick();
                Check(restored.SensorEvents.SequenceEqual(new[] { "on" }), "Directive order changed");
                loaded.Tick();
                Check(restored.SensorEvents.SequenceEqual(new[] { "on", "off" }), "FIFO lost second directive");
                Check(JsonConvert.DeserializeObject<ExecutiveState>(loaded.Save(), CheckpointJson.Settings).Directives.Count == 0, "Completed directive replayable");
            });

            Case("canonical target rebind / disappearance / stale actor / version refusal", () =>
            {
                var w = new NativeWorld();
                var e = Start(w, home: true);
                Ship old = w.Home;
                w.Home = w.ReplaceHome();
                e.Tick();
                Check(ReferenceEquals(w.Subject.shipScanTarget, w.Home) && !ReferenceEquals(old, w.Home), "Goal held stale target instance");
                w.Ships.Remove("HOME");
                e.Tick();
                Check(w.Subject.shipScanTarget == null && w.Subject.objSS.NavData == null, "Missing named target retained an active route");
                var w2 = new NativeWorld();
                var e2 = Start(w2);
                w2.Ships.Remove("SUBJECT");
                Check(e2.Tick() == CommandCode.Failure, "Stale hull was still executed");
                var w3 = new NativeWorld();
                var e3 = Start(w3);
                string json = e3.Save();
                bool refused = false;
                try
                {
                    e3.Load(json.Replace(Executive.BindingVersion, "wrong-version"));
                }
                catch (InvalidOperationException) { refused = true; }
                Check(refused, "Changed native binding was silently accepted");
            });

            Case("directive capacity is bounded and completed command objects are released", () =>
            {
                var w = new NativeWorld();
                var e = Start(w);
                int baseline = e.Policy.Commands.ById.Count;
                for (int i = 0; i < 100; i++)
                {
                    e.Admit(new TurnOnAllSensors(w.Pilot));
                    e.Tick();
                }
                Check(e.Policy.Commands.ById.Count == baseline, "Completed directives accumulated in command graph");
                for (int i = 0; i < 16; i++)
                    e.Admit(new TurnOnAllSensors(w.Pilot));
                bool refused = false;
                try
                {
                    e.Admit(new TurnOnAllSensors(w.Pilot));
                }
                catch (InvalidOperationException) { refused = true; }
                Check(refused, "Directive capacity limit was not enforced");
            });

            Case("native export is repeatable for supported ordinary flight", () =>
            {
                var w = new NativeWorld();
                var e = Start(w, home: true);
                Check(e.CanHandBack(out _), "Ordinary flight incorrectly blocked");
                string first = JsonConvert.SerializeObject(e.ExportNative()), second = JsonConvert.SerializeObject(e.ExportNative());
                Check(first == second, "Export mutated the command queue");
                Check(e.Policy.Root.Children.Last().Native.SaveData[0] == "2", "Native cursor projection lost flight stage");
                Check(w.Pilot.BehaviorTree.RunCommand(w.Pilot.Blackboard).Status == CommandCode.Running, "Projected native dispatcher did not continue flight");
            });

            Case("combat time-skip is an explicit integration blocker", () =>
            {
                var w = new NativeWorld();
                var e = Combat(w);
                double before = StarSystem.fEpoch;
                Check(!e.Advance(3600, "SFF"), "Fixture fabricated a bulk combat outcome");
                Close(StarSystem.fEpoch, before, "Unsupported combat partially advanced the clock");
            });

            Case("urgent native dodge returns control without suppressing combat", () =>
            {
                var w = new NativeWorld();
                var e = Combat(w);
                w.Pilot.Blackboard.Set("DodgeTarget", "missing-projectile");
                e.Admit(new Dodge(w.Pilot));
                Check(e.Tick() == CommandCode.Finished, "Native dodge did not complete");
                int shots = w.NativeShotRequests;
                e.Tick();
                Check(w.NativeShotRequests > shots, "Combat did not resume after urgent directive");
                bool rejected = false;
                try
                {
                    e.Admit(new ComeHere(w.Pilot));
                }
                catch (NotSupportedException) { rejected = true; }
                Check(rejected, "Unvalidated comms directive was silently admitted");
            });

            Case("nested alias graph survives reconstruction", () =>
            {
                var w = new NativeWorld();
                var e = Combat(w);
                // Capture actual native combinator aliases as well as nested commands.
                string active = w.AI.ActiveCommandName;
                var before = e.Policy.Commands.Capture();
                string json = e.Save();
                var restored = new NativeWorld();
                var loaded = new Executive(restored, restored.Pilot, restored.AI);
                loaded.Load(json);
                Check(restored.AI.ActiveCommandName == active, "Status projection lost native active command identity");
                var after = loaded.Policy.Commands.Capture();
                foreach (var pair in before)
                    foreach (var link in pair.Value.Links)
                        Check(after[pair.Key].Links.TryGetValue(link.Key, out var target) && target == link.Value, "Native command alias lost: " + pair.Key);
                Check(loaded.Policy.Commands.ById.Count == e.Policy.Commands.ById.Count, "Command graph was cloned or truncated");
            });

            ModelChecks.Run(Case, Check);
            NavyChecks.Run(Case, Check);

            // Diagnostic measurement only: includes native command/fixture allocations,
            // excludes initialization and checkpoints. Never a Unity FPS prediction.
            var bench = new NativeWorld();
            var measured = Combat(bench);
            for (int i = 0; i < 100; i++)
                measured.Tick();
            long calibrationStart = GC.GetAllocatedBytesForCurrentThread();
            byte[] calibration = new byte[1048576];
            long calibrated = GC.GetAllocatedBytesForCurrentThread() - calibrationStart;
            GC.KeepAlive(calibration);
            long bytes = GC.GetAllocatedBytesForCurrentThread();
            int calls = measured.NativeCalls;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 2000; i++)
                measured.Tick();
            clock.Stop();
            bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
            AllocationSample = new
            {
                iterations = 2000,
                allocationCounterReliable = calibrated >= 1048576,
                counterCalibrationBytes = calibrated,
                totalAllocatedBytes = calibrated >= 1048576 ? (long?)bytes : null,
                bytesPerDispatch = calibrated >= 1048576 ? (double?)(bytes / 2000.0) : null,
                elapsedMilliseconds = clock.Elapsed.TotalMilliseconds,
                nativeCalls = measured.NativeCalls - calls
            };

            var report = new
            {
                schemaVersion = 1,
                binding = Executive.RuntimeBinding,
                assertions = checks,
                allocationSample = AllocationSample,
                gameAssemblySha256 = Hash(typeof(Ship).Assembly.Location),
                pluginSha256 = Hash(typeof(Ostrabus.OstrabusPlugin).Assembly.Location),
                replacementModelSha256 = Hash("Navigation/Architecture/Replacement/mission-model.json"),
                abilityContractsSha256 = Hash("Navigation/Architecture/Replacement/ability-contracts.json"),
                replacementCoreModuleId = typeof(MissionContracts.Machine).Module.ModuleVersionId,
                navyWorldEvidence = NavyTimeChecks.Evidence,
                retainedWorldEvidence = WorldServiceChecks.Evidence,
                nativeSaveEvidence = new
                {
                    collectorCalls = NativeSaveFixtures.TotalCaptures,
                    threadedArchiveWrites = NativeArchiveIO.ThreadedWrites,
                    synchronousArchiveWrites = NativeArchiveIO.SynchronousWrites,
                    excludedSceneCalls = NativeClock.ExcludedSceneCalls,
                    excludedSteeringCalls = NativeClock.ExcludedSteeringCalls,
                    scope = "One actor; native collector/writer/parser, arrival policy and unregister; physical ship/crew/scene and unrelated subsystem snapshots retain providers.",
                    performanceQualified = false
                },
                retainedWorldServicePatches = RetainedWorldServices.PatchNames,
                nativeGameVersion = "1.1.0.0",
                executiveSaveRecordSchema = 2,
                sharedJumpCoordinator = "Ostrabus.Orbital.SffAdvance",
                reviewedSources = new[] {
                    "Navigation/FlightControlHandoffPatch.cs", "Navigation/CivilianAsteroidVisits.cs",
                    "MiscBugFixes/SffNavigationDestinationPatch.cs", "MiscBugFixes/CivilianVisitWaiting.cs",
                    "Orbital/SffSimulation.cs", "Orbital/Economy/CargoArrivalPatches.cs",
                    "Orbital/Economy/CargoDelivery.cs", "Orbital/Economy/MarketStockPatches.cs",
                    "Orbital/OrbitalPatches.cs", "Orbital/CoastIntegration.cs", "Orbital/NativeEphemeris.cs",
                    "Orbital/SffDynamics.cs", "Orbital/Economy/EconomyPatches.cs", "Orbital/Economy/SffEconomy.cs", "Orbital/Economy/FreightRoute.cs",
                    "Orbital/SffAdvance.cs", "Orbital/JumpBoundary.cs", "Navigation/RouteCompletionPatch.cs",
                    "SaveCompatibility/LegacyRcsCaches.cs", "SaveCompatibility/LegacyRcsLoadingPatch.cs", "MiscBugFixes/ShipReplacementTargetsPatch.cs"
                }.Select(path => new { path, sha256 = Hash(path) }).ToArray(),
                results = Results,
                fixtureMethods = boundary.GetPatchedMethods().Select(m => m.DeclaringType.FullName + "." + m.Name).OrderBy(x => x).ToArray(),
                retainedPatchMethods = services.GetPatchedMethods().Select(m => m.DeclaringType.FullName + "." + m.Name).OrderBy(x => x).ToArray()
            };
            File.WriteAllText("obj/ShipExecutivePrototype/results.json", JsonConvert.SerializeObject(report, Formatting.Indented));
            Console.WriteLine(checks + " assertions across " + Results.Count + " scenarios; see obj/ShipExecutivePrototype/results.json");
            return Results.Any(r => !(bool)r.GetType().GetProperty("passed").GetValue(r)) ? 1 : 0;
        }
        catch (Exception e) { Console.WriteLine(e); return 1; }
        finally { services.UnpatchSelf(); boundary.UnpatchSelf(); }
    }
}
