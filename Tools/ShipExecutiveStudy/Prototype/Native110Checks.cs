using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MissionContracts;
using Newtonsoft.Json.Linq;
using Ostranauts.Core;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using Ostranauts.ShipGUIs.Utilities;
using SolarSystem;

namespace ShipExecutivePrototype;

// 1.1 adapter integration. Registry publication/physical materialization remain
// scene providers here; native stellar DTOs, target application, route playback,
// orbit math, archive writer/parser and executive lifecycle execute.
static class Native110Checks
{
    static JsonStellarShipSave Deferred(string id) => new JsonStellarShipSave
    {
        strID = id,
        strLoot = "fixture-template",
        strOwner = "fixture-owner",
        strPublicName = "deferred target",
        strParent = "HOME",
        nSize = 12,
        nMapID = 42,
        Classification = Ship.TypeClassification.OrbitalStation,
        vBOOffsetx = 1e-5,
        fOrbitW = .001,
        fOrbitTimeLast = 1000,
        aShipCOConds = new[] { "IsEnvironmentEntity" }
    };

    internal static void Run(Action<string, Action> scenario, Action<bool, string> check, Definition model)
    {
        scenario("1.1 deferred destination preserves mission through conversion, reload and physical re-entry", () =>
        {
            var world = new NativeWorld { LowFuel = true };
            var actor = new NativeNavyActor(world, model);
            actor.Travel.BeginNativeLowFuelReturn();
            long mission = actor.Machine.State.Active.Id;
            var deferred = new StellarShip(Deferred("HOME"));
            deferred.objSS.vPosx = world.Home.objSS.vPosx;
            // Explicit scene publication boundary, not a substitute production
            // implementation of EnvironmentEntities.UnloadEnvironmentForRegion.
            CrewSim.system.dictStellarObjects["HOME"] = deferred;
            world.Home.bDestroyed = true;
            world.Ships.Remove("HOME");
            actor.Travel.Tick();
            check(world.Subject.shipScanTarget == null && ReferenceEquals(world.Subject.shipSituTarget, deferred.objSS), "Goal retained a retired physical target");
            check(actor.Machine.State.Active.Id == mission && actor.Machine.State.Active.Phase == "Approaching" && world.ArrivalCalls == 0, "Representation change cancelled or completed the mission");
            actor = NativeNavyActor.Restore(model, actor.Save());
            world = actor.World;
            actor.Travel.Tick();
            check(world.Resolve("HOME") == null && world.NavigationTargetId == "HOME" && actor.Machine.State.Active.Id == mission,
                "Reload resurrected a hull or lost deferred goal identity");
            var route = world.Subject.objSS.NavData;
            CrewSim.system.dictStellarObjects.Remove("HOME");
            world.Home = world.Vessel("HOME", 0);
            world.Home.objSS.bIsBO = true;
            actor.Travel.Tick();
            check(ReferenceEquals(world.Subject.shipScanTarget, world.Home) && actor.Machine.State.Active.Id == mission && ReferenceEquals(route, world.Subject.objSS.NavData),
                "Re-entry introduced a fresh mission or replotted an unchanged destination");
            world.Home.bDestroyed = true;
            actor.Travel.Tick();
            check(actor.Machine.State.Active.Phase == "AwaitingPolicy" && world.ArrivalCalls == 0, "Actual target destruction became successful arrival");
        });

        scenario("1.1 deferred actor cannot be implicitly adopted or mistaken for arrival", () =>
        {
            var world = new NativeWorld { LowFuel = true };
            var actor = new NativeNavyActor(world, model);
            world.Ships.Remove("SUBJECT");
            CrewSim.system.dictStellarObjects["SUBJECT"] = new StellarShip(Deferred("SUBJECT"));
            check(!actor.Travel.BeginNativeLowFuelReturn(), "Dormant representation was adopted as a live controller");
            actor.Tick();
            check(actor.Machine.State.PendingArrival == null && world.ArrivalCalls == 0 && !world.Retired, "Absence certified a world effect");
        });

        foreach (string shape in new[] { "absent", "empty", "populated" })
        {
            scenario("1.1 native archive preserves " + shape + " environment arrays and propulsion state", () =>
            {
                using var dialogue = new NativeDialogueFixtures();
                using var fence = new NativeExecutionFence();
                var world = new NativeWorld { LowFuel = true };
                JsonStellarShipSave[] ships = shape == "absent" ? null : shape == "empty" ? Array.Empty<JsonStellarShipSave>() : new[] { Deferred("DEFERRED") };
                JsonStellarDerelictSave[] wrecks = shape == "absent" ? null : shape == "empty" ? Array.Empty<JsonStellarDerelictSave>() : new[]
                {
                    new JsonStellarDerelictSave { strID = "WRECK", strLoot = "fixture-wreck", strOwner = "owner", strParent = "HOME", nMapID = 43,
                        nSize = 16, vBOOffsetx = 2e-5, fOrbitW = -.002, fOrbitTimeLast = 999, fBreakInMultiplier = .75f, aFactions = new[] { "fixture" } }
                };
                world.LoadStellar(ships, wrecks);
                world.Subject.RCSCount = 7;
                NativeWorld.Set(world.Subject, "nRCSCountRaw", 3);
                NativeWorld.Set(world.Subject, "fRCSExhaustV", 4.75f);
                world.Home.objSS.fOrbitW = .003;
                world.Home.objSS.fOrbitTimeLast = 999.25;
                using var population = new NativeArrivalFixtures(world);
                using var native = new NativeSaveFixtures(world, population);
                var actor = new NativeNavyActor(world, model);
                actor.Travel.BeginNativeLowFuelReturn();
                var port = new NativeSavePort(fence, native, actor);
                string folder = NativeArchiveIO.Folder();
                var dto = port.Capture(folder);
                check((dto.jGameSave.Item2.objSystem.aStellarShips == null) == (shape == "absent"), "Collector/detachment already lost environment shape");
                string digest = NativeSavePort.Digest(dto);
                var archive = NativeArchiveIO.RoundTrip(dto, folder, true);
                var system = archive.Read().jGameSave.Item2.objSystem;
                check((system.aStellarShips == null) == (shape == "absent") && (system.aStellarDerelicts == null) == (shape == "absent"),
                    "Native wire/parser changed world initialization semantics: " + archive.Content()["game"]?["objSystem"]);
                check(shape != "populated" || system.aStellarShips.Single().strID == "DEFERRED" && system.aStellarDerelicts.Single().fOrbitW == -.002,
                    "Deferred identities or circular state were dropped");
                actor = port.Restore(model, archive);
                check(actor.World.Subject.RCSCount == 7 && (int)AccessTools.Field(typeof(Ship), "nRCSCountRaw").GetValue(actor.World.Subject) == 3 &&
                    (float)AccessTools.Field(typeof(Ship), "fRCSExhaustV").GetValue(actor.World.Subject) == 4.75f, "Reload lost new hardware metadata");
                check(actor.World.Home.objSS.fOrbitW == .003 && actor.World.Home.objSS.fOrbitTimeLast == 999.25 &&
                    actor.World.Subject.objSS.NavData.Destination.RCSCount == 7, "Reload lost rotating anchor or nonzero route capability snapshot");
                check(NativeSavePort.DigestContent(archive.Content()) == digest && CrewSim.system.dictStellarObjects.Count == (shape == "populated" ? 2 : 0),
                    "Archive digest or logical population changed during restore");
            });
        }

        scenario("1.1 native save rejects mixed physical/deferred identities and old bindings before adoption", () =>
        {
            using var dialogue = new NativeDialogueFixtures();
            using var fence = new NativeExecutionFence();
            var world = new NativeWorld();
            using var population = new NativeArrivalFixtures(world);
            using var native = new NativeSaveFixtures(world, population);
            var actor = new NativeNavyActor(world, model);
            var port = new NativeSavePort(fence, native, actor);
            actor.Tick();
            CrewSim.system.dictStellarObjects["HOME"] = new StellarShip(Deferred("HOME"));
            bool refused = false;
            try
            {
                port.Capture(NativeArchiveIO.Folder());
            }
            catch (InvalidOperationException) { refused = true; }
            check(refused && world.ArrivalCalls == 0, "Ambiguous physical/deferred generation was certified");
            CrewSim.system.dictStellarObjects.Clear();
            var dto = port.Capture(NativeArchiveIO.Folder());
            var infos = dto.jGameSave.Item2.aCustomInfos;
            int index = Array.FindIndex(infos, s => s.StartsWith(NativeSavePort.Key, StringComparison.Ordinal));
            var record = JObject.Parse(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(infos[index].Substring(NativeSavePort.Key.Length))));
            record["NativeBinding"] = "native-navy-1.0.1.10-prototype-v1";
            infos[index] = NativeSavePort.Key + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(record.ToString()));
            refused = false;
            try
            {
                port.Restore(model, dto);
            }
            catch (InvalidOperationException) { refused = true; }
            check(refused && NativeWorld.Current == world && StarSystem.fEpoch == 1000, "Old binding mutated the world before refusal");
        });

        scenario("1.1 snapshot rejects a changed deferred generation and tampered empty-array evidence", () =>
        {
            using var dialogue = new NativeDialogueFixtures();
            using var fence = new NativeExecutionFence();
            var world = new NativeWorld();
            using var population = new NativeArrivalFixtures(world);
            using var native = new NativeSaveFixtures(world, population);
            var actor = new NativeNavyActor(world, model);
            actor.Tick();
            var port = new NativeSavePort(fence, native, actor);
            native.BeforeShip = ship =>
            {
                if (ship == world.Subject)
                    CrewSim.system.dictStellarObjects["LATE"] = new StellarShip(Deferred("LATE"));
            };
            bool refused = false;
            try
            {
                port.Capture(NativeArchiveIO.Folder());
            }
            catch (InvalidOperationException) { refused = true; }
            check(refused, "Mixed deferred generation was certified");
            native.BeforeShip = null;
            CrewSim.system.dictStellarObjects.Clear();
            string folder = NativeArchiveIO.Folder();
            var dto = port.Capture(folder);
            var infos = dto.jGameSave.Item2.aCustomInfos;
            int index = Array.FindIndex(infos, s => s.StartsWith(NativeSavePort.Key, StringComparison.Ordinal));
            var record = JObject.Parse(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(infos[index].Substring(NativeSavePort.Key.Length))));
            record["EmptyStellarShips"] = false;
            infos[index] = NativeSavePort.Key + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(record.ToString()));
            refused = false;
            try { port.Restore(model, dto); } catch (InvalidOperationException) { refused = true; }
            check(refused && NativeWorld.Current == world, "Direct DTO restore accepted contradictory empty-array metadata");
            var image = NativeArchiveIO.RoundTrip(dto, folder, false);
            refused = false;
            try
            {
                port.Restore(model, image);
            }
            catch (InvalidOperationException) { refused = true; }
            check(refused && NativeWorld.Current == world && StarSystem.fEpoch == 1000, "Changed initialization evidence bypassed the digest or mutated the world");
        });

        scenario("1.1 standalone checkpoints reject historical bindings before world reconstruction", () =>
        {
            var world = new NativeWorld();
            var actor = new NativeNavyActor(world, model);
            actor.Tick();
            var snapshot = JObject.Parse(actor.Save());
            var machine = JObject.Parse((string)snapshot["Executive"]);
            machine["NativeBinding"] = "native-navy-1.0.1.10-prototype-v1";
            snapshot["Executive"] = machine.ToString();
            var profile = JObject.Parse((string)snapshot["Profile"]);
            profile["NativeBinding"] = "native-navy-1.0.1.10-prototype-v1";
            snapshot["Profile"] = profile.ToString();
            var nativeWorld = JObject.Parse((string)snapshot["World"]);
            nativeWorld["Epoch"] = 9000;
            snapshot["World"] = nativeWorld.ToString();
            bool refused = false;
            try
            {
                NativeNavyActor.Restore(model, snapshot.ToString());
            }
            catch (InvalidOperationException) { refused = true; }
            check(refused && NativeWorld.Current == world && StarSystem.fEpoch == 1000, "Historical profile reconstructed a world before refusal");
            refused = false;
            try
            {
                NativeReturnActor.Restore(model, snapshot.ToString());
            }
            catch (InvalidOperationException) { refused = true; }
            check(refused && NativeWorld.Current == world && StarSystem.fEpoch == 1000, "Historical return state reconstructed a world before refusal");
        });

        scenario("1.1 shared SFF grants decision opportunities only to a capable actor before predicted impact", () =>
        {
            foreach (bool capable in new[] { false, true })
            {
                var world = new NativeWorld { PilotAvailable = capable };
                using var data = new WorldServiceFixtures(world);
                using var bridge = new NativeWorldTimeBridge(new NativeNavyActor(world, model), true);
                using var services = new RetainedWorldServices(true);
                data.Body.fRadius = 1e-5;
                world.Subject.objSS.vVelX = -1e-8;
                var urgent = new List<AIShip>();
                double epoch = StarSystem.fEpoch, position = world.Subject.objSS.vPosx, fuel = world.Subject.fShallowRCSRemass;
                using (var preview = (IDisposable)RetainedWorldServices.Call("SffSimulation", "Preview", CrewSim.system, 600d))
                {
                    double step = (double)AccessTools.Method(preview.GetType(), "DecisionStep").Invoke(preview, new object[] { urgent });
                    check((step < 600 && urgent.Contains(world.AI)) == capable, "Decision admission ignored native pilot capability or the predicted impact");
                }
                check(StarSystem.fEpoch == epoch && world.Subject.objSS.vPosx == position && world.Subject.fShallowRCSRemass == fuel && world.Subject.objSS.NavData == null,
                    "Forecast ran policy or changed the live simulation");
                if (!capable)
                    continue; // Uncontrolled impact resolution is a scene outcome, not an executive policy.
                // Patrol target is the fixture's ordinary outward waypoint.
                // This tests a real decision opportunity, not every random target.
                bridge.Sff(600);
                var result = RetainedWorldServices.Snapshot("SffAdvance");
                check((int)result["decisionSteps"] > 0 && bridge.NativeDispatches > 0 && StarSystem.fEpoch == 1600,
                    "The shared coordinator did not execute an intervening native/executive decision");
                check(world.ArrivalCalls == 0 && data.Errors.Count == 0, "Decision stepping invented docking or faulted retained simulation: " + string.Join(";", data.Errors));
            }
        });

        scenario("1.1 native orbit prediction, docking inheritance and DTO reload preserve circular motion", () =>
        {
            var world = new NativeWorld();
            var body = NativeWorld.Empty<BodyOrbit>();
            body.strName = "ORBIT";
            body.fMass = 1e20;
            body.dXReal = .001;
            body.dYReal = -.002;
            body.dTimeCalcLast = 1000;
            CrewSim.system.aBOs[body.strName] = body;
            var anchor = new ShipSitu(false) { vPosx = body.dXReal + 1e-5, vPosy = body.dYReal };
            anchor.LockToBOOrbit(body, false, 1000);
            check(anchor.fOrbitW > 0 && anchor.fOrbitTimeLast == 1000, "Native circular lock was not established");
            var snapshot = new ShipSitu(anchor.GetJSON());
            double quarter = 1000 + Math.PI / (2 * anchor.fOrbitW);
            double originalOffset = anchor.vBOOffsetx;
            snapshot.GetBOOffsetPrediction(quarter, out double x, out double y);
            check(Math.Abs(x) < 1e-17 && Math.Abs(y - 1e-5) < 1e-17 && anchor.vBOOffsetx == originalOffset,
                "Detached quarter-orbit prediction changed the live anchor or failed geometrically");
            var follower = new ShipSitu(false) { strBOPORShip = body.strName, vPosx = anchor.vPosx + 1e-7, vPosy = anchor.vPosy };
            follower.MatchBOOrbit(anchor);
            check(follower.fOrbitW == anchor.fOrbitW && follower.fOrbitTimeLast == anchor.fOrbitTimeLast, "Dock follower lost angular phase");
            body.dTimeCalcLast = quarter;
            snapshot.UpdateTime(quarter);
            check(Math.Abs(snapshot.vPosx - body.dXReal) < 1e-17 && Math.Abs(snapshot.vPosy - (body.dYReal + 1e-5)) < 1e-17,
                "Native reloaded orbit did not reach its predicted position");
            check(Math.Abs(snapshot.vVelX + snapshot.fOrbitW * 1e-5) < 1e-17 && Math.Abs(snapshot.vVelY) < 1e-17,
                "Native circular velocity is not tangent to the orbit");
        });

        scenario("1.1 nonzero route capability preserves native damage abort and unrelated AI", () =>
        {
            var hooks = new Harmony("offline.executive-110.rcs");
            try
            {
                hooks.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.LogAdd)), prefix: new HarmonyMethod(typeof(Native110Checks), nameof(Quiet)));
                hooks.Patch(AccessTools.Method(typeof(DataHandler), nameof(DataHandler.GetString)), prefix: new HarmonyMethod(typeof(Native110Checks), nameof(Quiet)));
                hooks.CreateClassProcessor(typeof(Ostrabus.OstrabusPlugin).Assembly.GetType("Ostrabus.ShipNavigation.RouteCompletionPatch", true)).Patch();
                hooks.CreateClassProcessor(typeof(Ostrabus.OstrabusPlugin).Assembly.GetType("Ostrabus.ShipNavigation.FlightControllerWithdrawalPatch", true)).Patch();
                foreach (bool damaged in new[] { false, true })
                    foreach (bool legacy in new[] { false, true })
                    {
                        var world = new NativeWorld();
                        var ship = CrewSim.coPlayer.ship;
                        NativeWorld.Set(ship, "aNavs", new List<CondOwner>());
                        var pilot = NativeWorld.Empty<AutoPilot>();
                        NativeWorld.Back(pilot, "ShipUs", ship);
                        pilot.Blackboard = new Blackboard();
                        var ai = NativeWorld.Empty<AIShip>();
                        NativeWorld.Set(ai, "_shipUs", ship);
                        NativeWorld.Set(ai, "_aiCharacter", pilot);
                        NativeWorld.Set(ai, "_activeCommand", new FlyToAutoPilot(pilot));
                        AIShipManager.dictAIs["HOME"].Add(ai);
                        var route = new NavData(ship);
                        route.AddNavDataPoint(new NavDataPoint(1000, new ShipSitu(false), 300, 0, 0, legacy ? 0 : 10));
                        route.AddNavDataPoint(new NavDataPoint(1100, new ShipSitu(false) { vPosx = .1 }, 250, 0, 0, legacy ? 0 : 10));
                        ship.objSS.NavData = route;
                        ship.RCSCount = damaged ? 7 : 10;
                        StarSystem.fEpoch = 1050;
                        route.TimeAdvance(ship.objSS);
                        bool abort = damaged && !legacy;
                        check((ship.objSS.NavData == null) == abort && ReferenceEquals(AIShipManager.GetAIShipByRegID(ship.strRegID), abort ? null : ai),
                            "Native capability loss was ignored or mislabeled successful completion");
                        check(Math.Abs(ship.fShallowRCSRemass - 275) < 1e-5 && ReferenceEquals(AIShipManager.GetAIShipByRegID("SUBJECT"), world.AI),
                            "Native fuel debit or unrelated controller identity changed");
                    }
            }
            finally { hooks.UnpatchSelf(); }
        });
    }

    static bool Quiet() => false;
}
