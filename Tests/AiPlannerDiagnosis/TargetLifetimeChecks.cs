using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using HarmonyLib;
using LitJson;
using Newtonsoft.Json.Linq;
using Ostrabus.MiscBugFixes;
using Ostranauts.Core;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using Ostranauts.ShipGUIs.Utilities;

static partial class LifecycleChecks {
    static Action nestedReplacement;
    static void InvokeNestedReplacement() {
        Action action = nestedReplacement; nestedReplacement = null; action?.Invoke();
    }
    static void FailMaterialization() => throw new InvalidOperationException("offline: replacement initialization failed");
    sealed class StellarFixture : IStellarObject {
        public string strID { get; set; }
        public ShipSitu objSS { get; } = new ShipSitu(false);
        public string strLoot { get; set; }
    }
    static Ship LifetimeShip(StarSystem world, string id) {
        var ship = ShipAt(id, new ShipSitu(false) { vPosx = 1, vPosy = 2, vVelX = 3e-9, vVelY = 4e-9, fW = .02f });
        Field(ship, "nLoadState", Ship.Loaded.Shallow);
        world.dictShips[id] = ship;
        ship.fShallowRCSRemass = 100;
        ship.fShallowFusionRemain = 200;
        return ship;
    }
    static NavData LifetimeRoute(Ship ship, bool huge = false) {
        var route = new NavData(ship);
        route.AddNavDataPoint(new NavDataPoint(1000, new ShipSitu(false), 100, 0, 200));
        route.AddNavDataPoint(new NavDataPoint(huge ? 1e20 : 2000, new ShipSitu(false) { vPosx = 2 }, 50, 0, 150));
        ship.objSS.NavData = route;
        return route;
    }
    static Blackboard LifetimeBoard(Ship ship) {
        var controller = Empty<AIShip>(); Field(controller, "_shipUs", ship);
        Field(controller, "_aiCharacter", new Program.Pilot { ShipUs = ship });
        var board = new Blackboard(); Field(controller, "<Blackboard>k__BackingField", board);
        if (!AIShipManager.dictAIs.TryGetValue("target-lifetime-fixture", out var list))
            AIShipManager.dictAIs["target-lifetime-fixture"] = list = new List<AIShip>();
        list.Add(controller);
        board.Set("TripFuelPrediction", "obsolete-goal&5000");
        return board;
    }
    static void SavedGoal(Ship ship) {
        ship.json = new JsonShip { strRegID = ship.strRegID, strTargetRegID = ship.targetRegID,
            strScanTargetID = ship.shipScanTarget?.strRegID, objSituScanTarget = ship.shipSituTarget?.GetJSON() };
    }
    static void CheckCleared(Ship ship, Blackboard board, string label) {
        Check(ship.shipScanTarget == null && ship.shipSituTarget == null && ship.targetRegID == null && ship.TargetData == null,
            label + ": target references survive");
        Check(ship.objSS.NavData == null && !board.HasKey("TripFuelPrediction"), label + ": route/prediction survive");
        Check(ship.json.strScanTargetID == null && ship.json.strTargetRegID == null && ship.json.objSituScanTarget == null,
            label + ": saved target can resurrect");
        Check(ship.objSS.vPosx == 1 && ship.objSS.vPosy == 2 && ship.objSS.vVelX == 3e-9 && ship.objSS.vVelY == 4e-9 && ship.objSS.fW == .02f,
            label + ": position, velocity or spin changed");
        Check(ship.fShallowRCSRemass == 100 && ship.fShallowFusionRemain == 200, label + ": fuel changed");
    }
    static void TargetLifetimeRepair(CrewSim crewSim, StarSystem world, Harmony h, string capturedLayout) {
        h.CreateClassProcessor(typeof(ShipDestroyedTargetsPatch)).Patch();
        h.CreateClassProcessor(typeof(ShipRestoredTargetsPatch)).Patch();
        // Unity presentation boundary only. Native StopManeuver/SetThrust still execute.
        Patch(h, typeof(GUIOrbitDraw), "TriggerShipRedraw", nameof(Quiet));
        foreach (bool copy in new[] { false, true }) {
            foreach (bool huge in new[] { false, true }) {
                var wreck = LifetimeShip(world, "lifetime-wreck-" + copy + huge);
                wreck.objSS.bBOLocked = true; wreck.objSS.strBOPORShip = "Sol";
                var observer = LifetimeShip(world, "lifetime-observer-" + copy + huge);
                var data = new TargetData(wreck);
                if (copy) data.Situ = new ShipSitu(wreck.objSS, false) { vBOOffsetx = 1e-7 };
                data.ApplyToShip(observer); SavedGoal(observer); LifetimeRoute(observer, huge);
                observer.shipCombatTarget = observer.shipStationKeepingTarget = observer.shipUndock = wreck;
                observer.json.strCombatTargetID = observer.json.strStationKeepingTargetID = observer.json.strUndockID = wreck.strRegID;
                var board = LifetimeBoard(observer);
                var flight = new FlyTo(new Program.Pilot { ShipUs = observer });
                flight.SaveData = new[] { "0.000000001", "obsolete-goal", "5000", "0", "1" };
                observer.objSS.vAccRCS = new UnityEngine.Vector2(1e-10f, 2e-10f);
                observer.objSS.fA = .03f;
                observer.objSS.vAccIn = new UnityEngine.Vector2(3e-10f, 4e-10f);
                Field(observer, "bTorchDriveThrusting", true);
                wreck.Destroy();
                CheckCleared(observer, board, "permanent removal");
                Check(observer.objSS.vAccRCS.x == 0 && observer.objSS.vAccIn.x == 0 && observer.objSS.fA == 0,
                    "Old propulsion commands survive target removal");
                Check(observer.shipCombatTarget == null && observer.shipStationKeepingTarget == null && observer.shipUndock == null,
                    "Auxiliary live references survive removal");
                Check(observer.json.strCombatTargetID == null && observer.json.strStationKeepingTargetID == null && observer.json.strUndockID == null,
                    "Auxiliary saved references survive removal");
                observer.CheckTargets(); CheckCleared(observer, board, "repeat native restoration");
                Check(flight.RunCommand(board).Status == CommandCode.Failure,
                    "Native flight does not release the invalid destination normally");
                Check(observer.fShallowRCSRemass == 100, "Cleared goal debited the old cached manual trip budget");
            }
        }
        foreach (bool orphan in new[] { false, true }) {
            var observer = LifetimeShip(world, "saved-missing-" + orphan);
            observer.IsAIShip = true;
            observer.json = new JsonShip { strTargetRegID = "absent-destination", strScanTargetID = "absent-destination",
                objSituScanTarget = new JsonShipSitu { bBOLocked = true, boPORShip = orphan ? null : "Sol", vPosx = 2, vVelX = 1e-7 } };
            LifetimeRoute(observer, true); var board = LifetimeBoard(observer);
            observer.CheckTargets(); CheckCleared(observer, board, "saved missing identity");
        }
        var removedStation = LifetimeShip(world, "missing-station-observer");
        world.aBOs["former-station-body"] = Empty<BodyOrbit>();
        removedStation.json = new JsonShip { strTargetRegID = "former-station-body", strScanTargetID = "former-station-body",
            objSituScanTarget = new JsonShipSitu { bIsBO = true, boPORShip = null } };
        LifetimeRoute(removedStation); var stationBoard = LifetimeBoard(removedStation);
        removedStation.CheckTargets();
        CheckCleared(removedStation, stationBoard, "missing station with leftover body record");

        // Install all production hooks during actual SaveToShallow so destruction
        // inside replacement cannot accidentally cancel healthy routes.
        var station = LifetimeShip(world, "lifetime-replaced");
        var traveler = LifetimeShip(world, "lifetime-traveler");
        station.objSS.bIsBO = true; station.objSS.strBOPORShip = station.strRegID;
        new TargetData(station).ApplyToShip(traveler); SavedGoal(traveler);
        var preserved = LifetimeRoute(traveler, true); var preservedBoard = LifetimeBoard(traveler);
        for (int repeat = 0; repeat < 3; repeat++) {
            crewSim.SaveToShallow(station); station = world.GetShipByRegID(station.strRegID);
            Check(ReferenceEquals(traveler.shipScanTarget, station) && ReferenceEquals(traveler.shipSituTarget, station.objSS), "All-hook replacement did not rebind");
            Check(ReferenceEquals(traveler.objSS.NavData, preserved) && preservedBoard.HasKey("TripFuelPrediction"), "All-hook replacement cancelled an unchanged route");
            Check(ShipReplacementTargetsPatch.Replacing == null, "Replacement context retains a Ship");
        }
        station.HideFromSystem = true; traveler.CheckTargets();
        Check(ReferenceEquals(traveler.objSS.NavData, preserved) && ReferenceEquals(traveler.shipScanTarget, station), "Hidden live hull treated as dead");
        for (int i = 0; i < 100; i++) ShipTargetLifetime.Restored(traveler);
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 5000; i++) ShipTargetLifetime.Restored(traveler);
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Check(allocated == 0, "Unchanged restored target allocates: " + allocated);

        // Scanning the removed ship must not cancel a different coordinate course.
        var contact = LifetimeShip(world, "unrelated-scan-contact");
        var coordinate = LifetimeShip(world, "coordinate-course");
        coordinate.shipScanTarget = contact;
        coordinate.shipSituTarget = new ShipSitu(false) { vPosx = 30 };
        SavedGoal(coordinate); var independent = LifetimeRoute(coordinate); var independentBoard = LifetimeBoard(coordinate);
        contact.Destroy();
        Check(coordinate.shipScanTarget == null && ReferenceEquals(coordinate.objSS.NavData, independent) && coordinate.shipSituTarget.vPosx == 30,
            "Removing a scanned-only contact cancelled an unrelated coordinate course");
        coordinate.CheckTargets();
        Check(ReferenceEquals(coordinate.objSS.NavData, independent) && independentBoard.HasKey("TripFuelPrediction"), "Coordinate course invalidated during restore");
        var selected = LifetimeShip(world, "retained-selection");
        var goal = LifetimeShip(world, "different-removed-goal");
        new TargetData(goal).ApplyToShip(coordinate); coordinate.shipScanTarget = selected; SavedGoal(coordinate); LifetimeRoute(coordinate);
        goal.Destroy();
        Check(coordinate.shipScanTarget == null && coordinate.shipSituTarget == null && coordinate.objSS.NavData == null,
            "Clearing a lost flight goal left another selected contact as an accidental flight target");
        Check(new FlyTo(new Program.Pilot { ShipUs = coordinate }).RunCommand(independentBoard).Status == CommandCode.Failure,
            "Native flight pursued another selected contact after its goal was cleared");
        Check(world.GetShipByRegID(selected.strRegID) == selected, "Target cleanup removed another hull");

        foreach (string kind in new[] { "coordinate", "body", "stellar", "unresolved-point" }) {
            var ship = LifetimeShip(world, "live-target-" + kind);
            ship.shipSituTarget = new ShipSitu(false) { vPosx = 9 };
            if (kind == "body") {
                ship.targetRegID = "fixture-body";
                world.aBOs[ship.targetRegID] = Empty<BodyOrbit>();
            } else if (kind == "stellar") {
                ship.targetRegID = "fixture-stellar";
                world.dictStellarObjects[ship.targetRegID] = new StellarFixture { strID = ship.targetRegID };
            } else if (kind == "unresolved-point") ship.targetRegID = "not-proven-to-be-a-ship";
            SavedGoal(ship); var route = LifetimeRoute(ship); var board = LifetimeBoard(ship);
            ship.CheckTargets();
            Check(ReferenceEquals(ship.objSS.NavData, route) && ship.shipSituTarget.vPosx == 9 && board.HasKey("TripFuelPrediction"),
                "Restoration invalidated a legitimate " + kind + " destination");
        }
        ReplacementFailureAndNesting(crewSim, world, h);

        if (capturedLayout != null) CapturedLifetimeRecovery(world, capturedLayout);
        var weak = RetiredLifetimeReferences(world);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Check(!weak[0].IsAlive && !weak[1].IsAlive && !weak[2].IsAlive, "Retired target, situation or TargetData is still rooted");
        Check(ShipReplacementTargetsPatch.Replacing == null, "Completed work left a replacement scope active");
        Console.WriteLine("Production target lifetime: removal, restoration, replacement, unrelated goals and weak-reference release passed; unchanged restore allocated " + allocated + " bytes / 5000 calls.");
    }
    static void ReplacementFailureAndNesting(CrewSim crewSim, StarSystem world, Harmony h) {
        var outer = LifetimeShip(world, "outer-replacement");
        var inner = LifetimeShip(world, "inner-replacement");
        var first = LifetimeShip(world, "outer-traveler");
        var second = LifetimeShip(world, "inner-traveler");
        new TargetData(outer).ApplyToShip(first); SavedGoal(first); var route = LifetimeRoute(first);
        new TargetData(inner).ApplyToShip(second); SavedGoal(second); var secondRoute = LifetimeRoute(second);
        var endpoint = AccessTools.Method(AccessTools.TypeByName("Ostranauts.TargetVisualization.TargetVisController"), "ClearTargetVis");
        var callback = AccessTools.Method(typeof(LifecycleChecks), nameof(InvokeNestedReplacement));
        h.Patch(endpoint, prefix: new HarmonyMethod(callback) { priority = Priority.First });
        nestedReplacement = () => {
            Check(ReferenceEquals(ShipReplacementTargetsPatch.Replacing, outer), "Outer replacement scope missing before nested load");
            crewSim.SaveToShallow(inner);
            Check(ReferenceEquals(ShipReplacementTargetsPatch.Replacing, outer), "Nested load did not restore outer scope");
        };
        try { crewSim.SaveToShallow(outer); }
        finally { nestedReplacement = null; h.Unpatch(endpoint, callback); }
        Check(ShipReplacementTargetsPatch.Replacing == null, "Nested replacement retains a scope");
        Check(ReferenceEquals(first.objSS.NavData, route) && ReferenceEquals(second.objSS.NavData, secondRoute), "Nested replacement cancelled valid navigation");
        Check(first.shipScanTarget == world.GetShipByRegID(outer.strRegID) && second.shipScanTarget == world.GetShipByRegID(inner.strRegID),
            "Nested replacement left either target reference dangling");

        var failed = LifetimeShip(world, "failed-replacement");
        new TargetData(failed).ApplyToShip(first); SavedGoal(first); LifetimeRoute(first);
        var board = LifetimeBoard(first);
        var initialize = AccessTools.Method(typeof(Ship), "InitShip");
        var fail = AccessTools.Method(typeof(LifecycleChecks), nameof(FailMaterialization));
        h.Patch(initialize, prefix: new HarmonyMethod(fail) { priority = Priority.First });
        bool propagated = false;
        try { crewSim.SaveToShallow(failed); }
        catch (InvalidOperationException ex) { propagated = ex.Message == "offline: replacement initialization failed"; }
        finally { h.Unpatch(initialize, fail); }
        Check(propagated && failed.bDestroyed && world.GetShipByRegID(failed.strRegID) == null, "Failed replacement boundary was not exercised");
        Check(ShipReplacementTargetsPatch.Replacing == null, "Failed replacement leaves a permanent suppression scope");
        CheckCleared(first, board, "failed replacement");
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference[] RetiredLifetimeReferences(StarSystem world) {
        var wreck = LifetimeShip(world, "weak-retired-wreck");
        var observer = LifetimeShip(world, "weak-retired-observer");
        var data = new TargetData(wreck); data.ApplyToShip(observer); SavedGoal(observer); LifetimeRoute(observer);
        var result = new[] { new WeakReference(wreck), new WeakReference(wreck.objSS), new WeakReference(data) };
        wreck.Destroy();
        return result;
    }
    static void CapturedLifetimeRecovery(StarSystem world, string path) {
        JToken saved = JToken.Parse(File.ReadAllText(path));
        if (saved is JArray array) saved = array[0];
        var ship = LifetimeShip(world, "captured-retired-target-observer");
        Field(ship, "<objSS>k__BackingField", new ShipSitu(saved["objSS"].ToObject<JsonShipSitu>()));
        ship.json = new JsonShip { strTargetRegID = (string)saved["strTargetRegID"], strScanTargetID = (string)saved["strScanTargetID"],
            objSituScanTarget = saved["objSituScanTarget"].ToObject<JsonShipSitu>() };
        Check(world.GetShipByRegID(ship.json.strTargetRegID) == null && ship.objSS.NavData != null,
            "Captured fixture is not a stored route toward a missing ship");
        var before = ship.objSS.GetJSON(); var board = LifetimeBoard(ship);
        ship.CheckTargets();
        Check(ship.targetRegID == null && ship.shipSituTarget == null && ship.objSS.NavData == null && !board.HasKey("TripFuelPrediction"),
            "Actual captured missing-target route was not invalidated");
        Check(ship.objSS.vPosx == before.vPosx && ship.objSS.vPosy == before.vPosy && ship.objSS.vVelX == before.vVelX && ship.objSS.vVelY == before.vVelY,
            "Captured ship was teleported or its momentum changed");
        Check(ship.fShallowRCSRemass == 100 && ship.fShallowFusionRemain == 200, "Captured recovery changed its fuel budget");
        Console.WriteLine("Captured O-ZY3A saved target/route invalidated without moving the vessel or changing fuel.");
    }
}
