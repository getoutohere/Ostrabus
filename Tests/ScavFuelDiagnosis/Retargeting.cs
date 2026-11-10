using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using Ostranauts.ShipGUIs.Utilities;
using Ostranauts.Ships.Comms;

static partial class Program {
    const double RetreatStepAU = 1e-6; // 149.6 km; detected threat is at 74.8 km.
    static bool retreatArmed, retreatThreat;
    static Ship retreatEnemy;
    static CondOwner retreatCaptain;
    static int escapeTargets, pathAccepts;
    static bool Healthy(AmIDamaged __instance, ref CommandStatus __result) { __result = new CommandStatus(CommandCode.Failure, __instance); return false; }
    static bool Armed(AmIArmed __instance, ref CommandStatus __result) { __result = new CommandStatus(retreatArmed ? CommandCode.Finished : CommandCode.Failure, __instance); return false; }
    static bool DetectedThreat(BaseCommand __instance, Blackboard blackboard, ref CommandStatus __result) {
        if (retreatThreat) { blackboard.Set("ClosestEnemy", retreatEnemy); blackboard.Set("Enemies", new List<Ship> { retreatEnemy }); }
        __result = new CommandStatus(retreatThreat ? CommandCode.Finished : CommandCode.Failure, __instance); return false;
    }
    static bool SensorsOn(TurnOnAllSensors __instance, ref CommandStatus __result) { __result = new CommandStatus(CommandCode.Finished, __instance); return false; }
    static bool LivingCaptain(ref CondOwner captain, ref bool __result) { captain = retreatCaptain; __result = true; return false; }
    static void EscapeSelected() { escapeTargets++; }
    static void PathAccepted() { pathAccepts++; }
    static bool NoRegionalAlternatives(ref List<string> __result) { __result = new List<string>(); return false; }
    static bool FixedInteger(ref int __result) { __result = 12345; return false; }
    static bool NoSurrenderDemand(SurrenderDemand __instance, ref CommandStatus __result) { __result = new CommandStatus(CommandCode.Failure, __instance); return false; }
    static ICommand ActiveLeaf(AIShip ai) => (ICommand)AccessTools.Field(typeof(AIShip), "_activeCommand").GetValue(ai);
    static List<ICommand> Children(ICommand command) => (List<ICommand>)AccessTools.Field(command.GetType(), "_childCommands").GetValue(command);
    static Ship RetargetShip(string id, double x = 0, bool station = false) {
        var ship = HelpShip(); ship.strRegID = id; ship.IsAIShip = !station; ship.fShallowRCSRemass = 100; ship.RCSCount = 1;
        ship.objSS.vPosx = x; ship.objSS.bIsBO = station; return ship;
    }
    static NavData RetargetRoute(Ship ship) {
        var route = new NavData(ship); var end = Empty<ShipSitu>(); end.vPosx = RetreatStepAU;
        route.AddNavDataPoint(new NavDataPoint(1000, Empty<ShipSitu>(), 100, 0, 0));
        route.AddNavDataPoint(new NavDataPoint(2000, end, 50, 0, 0)); ship.objSS.NavData = route; return route;
    }
    static AIShip PilotAI(BaseCharacter pilot) {
        var ai = Empty<AIShip>(); Set(ai, "_shipUs", pilot.ShipUs); Set(ai, "_aiCharacter", pilot);
        Set(ai, "_behaviorTree", pilot.BehaviorTree); Back(ai, "Blackboard", pilot.Blackboard); Back(ai, "HomeStation", "OKLG");
        AIShipManager.dictAIs = new Dictionary<string, List<AIShip>> { ["OKLG"] = new List<AIShip> { ai } }; return ai;
    }
    static void CheckRetargeting(Harmony boundary) {
        var production = new Harmony("offline.retarget.current-observer");
        var repair = new Harmony("offline.retarget.escape-repair");
        void Patch(Type type, string method, string replacement) => boundary.Patch(AccessTools.Method(type, method), prefix: new HarmonyMethod(typeof(Program), replacement));
        Patch(typeof(ShipSitu), nameof(ShipSitu.UpdateTime), nameof(Quiet));
        Patch(typeof(AmIDamaged), nameof(AmIDamaged.RunCommand), nameof(Healthy));
        Patch(typeof(AmIArmed), nameof(AmIArmed.RunCommand), nameof(Armed));
        Patch(typeof(AreThreatsNearby), nameof(AreThreatsNearby.RunCommand), nameof(DetectedThreat));
        Patch(typeof(TurnOnAllSensors), nameof(TurnOnAllSensors.RunCommand), nameof(SensorsOn));
        Patch(typeof(StarSystem), nameof(StarSystem.GetNearestStationRegional), nameof(RegionFixture));
        Patch(typeof(AIShipManager), nameof(AIShipManager.IsOnCollisionCourse), nameof(CollisionState));
        boundary.Patch(AccessTools.Method(typeof(CollisionManager), nameof(CollisionManager.GetCollisionDistanceAU), new[] { typeof(ShipSitu), typeof(ShipSitu) }), prefix: new HarmonyMethod(typeof(Program), nameof(ZeroCollision)));
        var captainMethod = AccessTools.Method(typeof(Comms), nameof(Comms.GetCaptain));
        boundary.Unpatch(captainMethod, HarmonyPatchType.Prefix, boundary.Id);
        boundary.Patch(captainMethod, prefix: new HarmonyMethod(typeof(Program), nameof(LivingCaptain)));
        boundary.Patch(AccessTools.Method(typeof(TargetEscapeStation), "SetTarget"), postfix: new HarmonyMethod(typeof(Program), nameof(EscapeSelected)));
        boundary.Patch(AccessTools.Method(typeof(FlyToPath), nameof(FlyToPath.RunCommand)), prefix: new HarmonyMethod(typeof(Program), nameof(PathAccepted)));
        try {
            retreatCaptain = Empty<CondOwner>(); retreatCaptain.bAlive = true;
            CrewSim.coPlayer = Empty<CondOwner>(); Set(CrewSim.coPlayer, "_strID", "PLAYER"); CrewSim.coPlayer.ship = HelpShip();
            fixtureCollision = false;
            AccessTools.Field(typeof(AIShipManager), "dictSpawnRegIDs").SetValue(null, new Dictionary<string, List<string>>());
            for (int mode = 0; mode < 3; mode++) {
                if (mode == 1) production.CreateClassProcessor(typeof(Ostrabus.OstrabusPlugin).Assembly.GetType("Ostrabus.ShipNavigation.RetargetFuelPatch", true)).Patch();
                if (mode == 2) repair.CreateClassProcessor(typeof(Ostrabus.OstrabusPlugin).Assembly.GetType("Ostrabus.ShipNavigation.EscapeTargetPatch", true)).Patch();
                StarSystem.fEpoch = 1000; retreatThreat = false; retreatArmed = true;
                var old = RetargetShip("OLD", RetreatStepAU, true); homeFixture = RetargetShip("OKLG", -RetreatStepAU, true);
                retreatEnemy = RetargetShip("ENEMY", .5 * RetreatStepAU); retreatEnemy.IsAIShip = false;
                var ship = RetargetShip("POLICE");
                CrewSim.system.dictShips = new Dictionary<string, Ship> { [old.strRegID] = old, [homeFixture.strRegID] = homeFixture, [ship.strRegID] = ship, [retreatEnemy.strRegID] = retreatEnemy };
                var pilot = new PolicePilot("OKLG", ship); var ai = PilotAI(pilot);
                var main = (StickySequenceCommand)Children(pilot.BehaviorTree).Last(); main.SaveData = new[] { "3" };
                var flee = (StickySequenceCommand)Children(Children(Children(pilot.BehaviorTree)[1])[1]).Last();
                new TargetData(old).ApplyToShip(ship); var route = RetargetRoute(ship); pilot.Blackboard.Set("TripFuelPrediction", "old-trip&50");
                pilot.Blackboard.Set("DecoyCount", 0);
                ai.RunAI(); Check(ReferenceEquals(ship.objSS.NavData, route), "Normal Main lost its prescribed in-progress plan");
                Check(ActiveLeaf(ai) is FlyTo, "Native police Main did not run FlyTo");
                retreatThreat = true; ai.RunAI();
                Check(pilot.Blackboard.Get<bool>("InCombat") && pilot.Blackboard.Get<List<Ship>>("Combatants").Contains(retreatEnemy), "Native threat branch did not initiate combat");
                Check(ReferenceEquals(ship.objSS.NavData, route), "Native combat entry unexpectedly cleaned the plan");
                // World input changes: offensive weapons are lost, with usable
                // propulsion and a living captain. This is not target injection.
                retreatArmed = false; int undockBefore = undocks, selected = escapeTargets;
                ai.RunAI();
                Check(escapeTargets == selected + 1 && ReferenceEquals(ship.shipScanTarget, homeFixture), "Native flee branch did not select its actual escape station");
                Check(flee.SaveData[0] == "1" && main.SaveData[0] == "3", "Retreat did not preempt existing Main at the expected native step");
                if (mode < 2) {
                    Check(ReferenceEquals(ship.objSS.NavData, route), "Native escape setter unexpectedly cleared stale route");
                    Check(ship.targetRegID == "OLD" && ReferenceEquals(ship.TargetData.Ship, old), "Native direct setter no longer reproduces inconsistent target metadata");
                    int accepted = pathAccepts; ai.RunAI();
                    Check(undocks == undockBefore && ActiveLeaf(ai) is FlyTo && pathAccepts == accepted + 1, "Free-space flee did not skip Undock and reach actual planned FlyTo");
                    StarSystem.fEpoch = 1500; route.TimeAdvance(ship.objSS);
                    Check(Math.Abs(ship.objSS.vPosx - .5 * RetreatStepAU) < 1e-12 && ReferenceEquals(ship.shipScanTarget, homeFixture), "Old route did not move away from selected escape station");
                    Check(pilot.Blackboard.HasKey("TripFuelPrediction"), "Direct escape setter unexpectedly invalidated old manual prediction");
                    Console.WriteLine((mode == 0 ? "Native" : "Retarget observer alone") + ": police Main flight -> native threat initiation -> weapon loss -> native flee; target OKLG at -149.6 km, old route still moves ship to +74.8 km. Undock skipped; old target metadata/prediction retained.");
                } else {
                    Check(ship.objSS.NavData == null && !pilot.Blackboard.HasKey("TripFuelPrediction"), "Production setter repair retained stale route/prediction");
                    Check(ship.targetRegID == "OKLG" && ReferenceEquals(ship.TargetData.Ship, homeFixture), "Repair did not use coherent native target assignment");
                    var replacement = RetargetRoute(ship); replacement.Destination.ObjSS.vPosx = -RetreatStepAU;
                    new TargetEscapeStation(pilot).RunCommand(pilot.Blackboard);
                    Check(ReferenceEquals(ship.objSS.NavData, replacement), "Equivalent escape selection discarded a current route");
                    ai.RunAI(); StarSystem.fEpoch = 1500; replacement.TimeAdvance(ship.objSS);
                    Check(ActiveLeaf(ai) is FlyTo && undocks == undockBefore && Math.Abs(ship.objSS.vPosx + .5 * RetreatStepAU) < 1e-12,
                        "Native retreat did not retain and advance the supplied replacement leg toward its escape destination");
                    Check(new TargetEscapeStation(pilot).RunCommand(new Blackboard()).Status == CommandCode.Failure,
                        "Repair invented an escape when native combat eligibility failed");
                    Check(ReferenceEquals(ship.objSS.NavData, replacement), "Failed native escape selection altered current flight");
                    Console.WriteLine("Production escape-setter repair clears obsolete route/prediction, updates all native target fields and preserves equivalent-station reapplication.");
                }
            }
            CheckRetargetControls(boundary);
            CheckEscapeRoles(boundary);
            Console.WriteLine(checks + " retarget caller checks passed. Real native police tree, combat initiation, escape selection, flight dispatch and NavData advance execute; world/health/weapon/sensor inputs and prior plan are prescribed. Production repair installed in patched cases.");
        } finally { repair.UnpatchSelf(); production.UnpatchSelf(); }
    }
    static void CheckEscapeRoles(Harmony boundary) {
        StarSystem.fEpoch = 1000;
        homeFixture = RetargetShip("OKLG", -RetreatStepAU, true);
        var old = RetargetShip("OLD", RetreatStepAU, true);
        var ship = RetargetShip("ROLE");
        CrewSim.system.dictShips = new Dictionary<string, Ship> { [homeFixture.strRegID] = homeFixture, [old.strRegID] = old, [ship.strRegID] = ship };
        var assign = (Func<TargetEscapeStation, Ship, bool>)Delegate.CreateDelegate(typeof(Func<TargetEscapeStation, Ship, bool>),
            AccessTools.Method(typeof(Ostrabus.OstrabusPlugin).Assembly.GetType("Ostrabus.ShipNavigation.EscapeTargetPatch", true), "Prefix"));
        foreach (BaseCharacter pilot in new BaseCharacter[] { new ScavPilot(null, ship), new CivilianPilot(null, ship), new PiratePilot(null, ship), new PolicePilot(null, ship) }) {
            PilotAI(pilot); new TargetData(old).ApplyToShip(ship); RetargetRoute(ship);
            pilot.Blackboard.Set("Combatants", new List<Ship> { retreatEnemy }); pilot.Blackboard.Set("TripFuelPrediction", "old-trip&50");
            var escape = (TargetEscapeStation)pilot.BehaviorTree.FindCommand(typeof(TargetEscapeStation));
            Check(escape.RunCommand(pilot.Blackboard).Status == CommandCode.Finished && ship.objSS.NavData == null &&
                ship.targetRegID == "OKLG" && ReferenceEquals(ship.TargetData.Ship, homeFixture), pilot.AIType + " escape assignment not repaired");
            Check(!pilot.Blackboard.HasKey("TripFuelPrediction"), pilot.AIType + " retained prior trip prediction");
            var route = RetargetRoute(ship); var targetData = ship.TargetData; escape.RunCommand(pilot.Blackboard);
            Check(ReferenceEquals(ship.objSS.NavData, route) && ReferenceEquals(ship.TargetData, targetData), pilot.AIType + " repeated escape discarded plan or allocated replacement target data");
            if (pilot is PolicePilot) {
                // Measure the production repair body and patched ApplyToShip,
                // excluding native station selection's own temporary lists.
                for (int i = 0; i < 100; i++) assign(escape, homeFixture);
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 1000; i++) assign(escape, homeFixture);
                Check(GC.GetAllocatedBytesForCurrentThread() - before == 0, "Stable escape assignment allocated after warmup");
            }
        }
        boundary.Patch(AccessTools.Method(typeof(AreThreatsNearbyBlockade), nameof(AreThreatsNearbyBlockade.RunCommand)), prefix: new HarmonyMethod(typeof(Program), nameof(DetectedThreat)));
        boundary.Patch(AccessTools.Method(typeof(SurrenderDemand), nameof(SurrenderDemand.RunCommand)), prefix: new HarmonyMethod(typeof(Program), nameof(NoSurrenderDemand)));
        foreach (BaseCharacter pilot in new BaseCharacter[] { new NavyPilot(null, ship), new BlockadePilot(null, ship) }) {
            var ai = PilotAI(pilot); new TargetData(old).ApplyToShip(ship); RetargetRoute(ship);
            pilot.Blackboard.Set("ClosestEnemy", retreatEnemy); pilot.Blackboard.Set("Enemies", new List<Ship> { retreatEnemy }); pilot.Blackboard.Set("DecoyCount", 0);
            new InitiateAgainstBiggestThreat(pilot).RunCommand(pilot.Blackboard);
            retreatThreat = true; retreatArmed = false; int selected = escapeTargets;
            var main = (StickySequenceCommand)Children(pilot.BehaviorTree).Last(); main.SaveData = new[] { "2" };
            var flee = (StickySequenceCommand)Children(Children(Children(pilot.BehaviorTree)[1])[3]).Last();
            Check(Children(flee)[0] is TargetNearestStation, pilot.AIType + " no longer uses the already-clearing retreat command");
            ai.RunAI(); Check(ship.objSS.NavData == null && ReferenceEquals(ship.shipScanTarget, homeFixture) && escapeTargets == selected,
                pilot.AIType + " native retreat failed cleanup or unexpectedly entered escape-setter hook");
            Check(flee.SaveData[0] == "1" && main.SaveData[0] == "2", pilot.AIType + " native retreat preemption changed");
        }
        Console.WriteLine("Shared escape-leaf regressions pass for Scav/Civilian/Pirate/Police; Navy/Blockade native retreat clears the route without entering the new hook. Stable assignment body allocates zero bytes after warmup.");
    }
    static void CheckRetargetControls(Harmony boundary) {
        void Patch(Type type, string method, string replacement) => boundary.Patch(AccessTools.Method(type, method), prefix: new HarmonyMethod(typeof(Program), replacement));
        StarSystem.fEpoch = 1000; retreatThreat = false;
        var ship = RetargetShip("CONTROL"); var pilot = new HelpPilot(ship);
        var old = RetargetShip("OLD", 1, true); homeFixture = RetargetShip("OKLG", -1, true);
        CrewSim.system.dictShips = new Dictionary<string, Ship> { [old.strRegID] = old, [homeFixture.strRegID] = homeFixture, [ship.strRegID] = ship };
        new TargetData(old).ApplyToShip(ship); var route = ScopeRoute(ship);

        // Existing native cleanup: nearest-station fallback resets before
        // selection. Alternative-station/faction filtering is outside this case.
        Patch(typeof(AIShipManager), nameof(AIShipManager.GetAllStationsInATCRegion), nameof(NoRegionalAlternatives));
        Patch(typeof(StarSystem), nameof(StarSystem.GetNearestStation), nameof(RegionFixture));
        Check(new TargetNearestStation(pilot).RunCommand(pilot.Blackboard).Status == CommandCode.Finished &&
            ship.objSS.NavData == null && ReferenceEquals(ship.shipScanTarget, homeFixture), "Native nearest-station cleanup failed");

        // TargetNewIfNone replaces an old route before installing its newly
        // generated one. Planning geometry/feasibility is a supplied boundary.
        Patch(typeof(AIShipManager), nameof(AIShipManager.LowFuel), nameof(LowFixture)); lowFixture = true;
        Patch(typeof(AIShipManager), nameof(AIShipManager.Log), nameof(Quiet));
        Patch(typeof(TargetNewIfNone), "GenerateNavData", nameof(FailedPlan));
        boundary.Patch(AccessTools.Method(typeof(UnityEngine.Random), nameof(UnityEngine.Random.Range), new[] { typeof(int), typeof(int) }), prefix: new HarmonyMethod(typeof(Program), nameof(FixedInteger)));
        ship.ClearShipTarget(); route = ScopeRoute(ship); plannedFixture = ScopeRoute(ship); ship.objSS.NavData = route;
        Check(new TargetNewIfNone(pilot).RunCommand(pilot.Blackboard).Status == CommandCode.Finished &&
            ReferenceEquals(ship.shipScanTarget, homeFixture) && ReferenceEquals(ship.objSS.NavData, plannedFixture), "Native target selection discarded newly installed plan or retained old one");

        route = ScopeRoute(ship); ship.shipCombatTarget = retreatEnemy;
        pilot.Blackboard.Set("EmergencySignal", new Ostranauts.Core.Models.Tuple<double, ShipSitu>(1000, homeFixture.objSS));
        Check(new TargetEmergencySignal(pilot).RunCommand(pilot.Blackboard).Status == CommandCode.Finished &&
            ship.objSS.NavData == null && ship.shipCombatTarget == null, "Native emergency-signal route cleanup failed");

        // A free moving target takes the parent's manual branch, which already
        // discards the plotted route. Manual steering itself is a fixture.
        new TargetData(retreatEnemy).ApplyToShip(ship); route = ScopeRoute(ship);
        var flight = new FlyTo(pilot); Set(flight, "_flyToManual", new SteeringBoundary(false));
        Check(flight.RunCommand(pilot.Blackboard).Status == CommandCode.Running && ship.objSS.NavData == null,
            "Free moving target did not use native manual-branch cleanup");

        // A route endpoint differing from the final destination is legitimate
        // for staged cargo travel. Do not infer stale ownership from coordinates.
        var cargo = RetargetShip("CARGO"); cargo.bFusionReactorRunning = true;
        var destination = RetargetShip("FAR", 4, true);
        CrewSim.system.dictShips[cargo.strRegID] = cargo; CrewSim.system.dictShips[destination.strRegID] = destination;
        var cargoPilot = new HaulerCargoPilot("OKLG", cargo, null); var cargoAI = PilotAI(cargoPilot);
        ((StickySequenceCommand)Children(cargoPilot.BehaviorTree).Last()).SaveData = new[] { "1" };
        new TargetData(destination).ApplyToShip(cargo); var cargoLeg = ScopeRoute(cargo); cargoLeg.Destination.ObjSS.vPosx = .1;
        cargoAI.RunAI(); Check(ActiveLeaf(cargoAI) is FlyTo && ReferenceEquals(cargo.objSS.NavData, cargoLeg), "Legitimate intermediate cargo leg was invalidated");
        Check(ReferenceEquals(cargo.shipScanTarget, destination) && cargoLeg.Destination.ObjSS.vPosx != destination.objSS.vPosx, "Cargo control did not retain distinct leg endpoint/final destination");

        // Player autopilot also shares FlyToPath. Merely selecting/rebinding a
        // target is not authorization to discard the player's plotted course.
        var player = RetargetShip("PLAYER-SHIP"); player.IsAIShip = false; CrewSim.coPlayer.ship = player;
        var auto = new AutoPilot(null, player); var playerAI = PilotAI(auto);
        new TargetData(destination).ApplyToShip(player); var playerLeg = ScopeRoute(player);
        playerAI.RunAI(); Check(ReferenceEquals(player.objSS.NavData, playerLeg) && ActiveLeaf(playerAI) is FlyToPath,
            "Native player autopilot lost its plotted course under the escape-only repair");
        Console.WriteLine("Native cleanup controls passed: nearest-station fallback, new target/new plan, emergency signal and moving-contact manual flight. Intermediate cargo and player autopilot plans remain intact.");
    }
}
