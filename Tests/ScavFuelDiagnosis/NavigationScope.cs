using System;
using System.Collections.Generic;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using Ostranauts.ShipGUIs.Utilities;

static partial class Program {
    static int scopeFlights;
    static bool ScopeFlight(FlyTo __instance, ref CommandStatus __result) {
        if (__instance.ShipUs.shipSituTarget == null) return true;
        scopeFlights++; __result = new CommandStatus(CommandCode.Running, __instance); return false;
    }
    static bool ScopeCollision(ref float __result) { __result = 0; return false; }
    static NavData ScopeRoute(Ship ship) {
        var route = new NavData(ship);
        route.AddNavDataPoint(new NavDataPoint(1000, Empty<ShipSitu>(), 100, 0, 0));
        var end = Empty<ShipSitu>(); end.vPosx = 1;
        route.AddNavDataPoint(new NavDataPoint(1100, end, 50, 0, 0));
        ship.objSS.NavData = route;
        return route;
    }
    static void CheckNavigationScope(Harmony boundary) {
        var production = new Harmony("offline.navigation-scope.production");
        boundary.Patch(AccessTools.Method(typeof(ShipSitu), nameof(ShipSitu.UpdateTime)), prefix: new HarmonyMethod(typeof(Program), nameof(Quiet)));
        try {
            foreach (string name in new[] { "Ostrabus.ShipNavigation.RetargetFuelPatch", "Ostrabus.ShipNavigation.RcsAutomaticReplenishmentPatch", "Ostrabus.ShipNavigation.RcsPhysicalReplenishmentPatch", "Ostrabus.RefuelRCSPatch" })
                production.CreateClassProcessor(typeof(Ostrabus.OstrabusPlugin).Assembly.GetType(name, true)).Patch();
            CrewSim.coPlayer = Empty<CondOwner>(); Set(CrewSim.coPlayer, "_strID", "PLAYER"); CrewSim.coPlayer.ship = MakeShip(0);
            foreach (var scenario in new[] {
                (AIType.HaulerDeployer, "VORB"), (AIType.HaulerDeployer, "OKLG"), (AIType.HaulerDeployer, "HQCH"),
                (AIType.HaulerRetriever, "VORB"), (AIType.HaulerRetriever, "OKLG"), (AIType.HaulerCargo, "OKLG"), (AIType.Scav, "OKLG") }) {
                bool guarded = scenario.Item1 == AIType.HaulerDeployer && scenario.Item2 == "VORB";
                var ship = MakeShip(100); ship.IsAIShip = true; ship.fShallowRCSRemassMax = 120;
                ((Dictionary<string, Ship>)AccessTools.Field(typeof(Ship), "aDocked").GetValue(ship)).Clear();
                var pilot = new HelpPilot(ship) { Role = scenario.Item1 };
                var ai = Empty<AIShip>(); Set(ai, "_shipUs", ship); Set(ai, "_aiCharacter", pilot);
                Back(ai, "Blackboard", pilot.Blackboard); Back(ai, "HomeStation", scenario.Item2);
                AIShipManager.dictAIs = new Dictionary<string, List<AIShip>> { [scenario.Item2] = new List<AIShip> { ai } };
                var old = MakeShip(0); old.strRegID = "old"; old.objSS.bIsBO = true;
                var next = MakeShip(0); next.strRegID = "new"; next.objSS.bIsBO = true; next.objSS.vPosx = 2;
                new TargetData(old).ApplyToShip(ship);
                StarSystem.fEpoch = 1000;
                var route = ScopeRoute(ship); pilot.Blackboard.Set("TripFuelPrediction", "old-leg&50");
                new TargetData(old).ApplyToShip(ship);
                Check(ReferenceEquals(ship.objSS.NavData, route) && pilot.Blackboard.HasKey("TripFuelPrediction"), "Equivalent target lost route/prediction");
                new TargetData(next).ApplyToShip(ship);
                Check(!pilot.Blackboard.HasKey("TripFuelPrediction"), "General retarget prediction invalidation failed");
                Check((ship.objSS.NavData == null) == guarded, "Unexpected current target-change guard scope");
                if (!guarded) {
                    StarSystem.fEpoch = 1050; ship.objSS.NavData.TimeAdvance(ship.objSS);
                    Check(Math.Abs(ship.objSS.vPosx - .5) < 1e-6 && ReferenceEquals(ship.shipScanTarget, next),
                        "Old stored route no longer moves ship after target changes");
                }
                // Isolate native AIRefuel and absolute NavData reconciliation.
                // No explicit maintenance endpoint is used; that endpoint already rebases.
                StarSystem.fEpoch = 1000; ship.objSS.vPosx = 0; ship.fShallowRCSRemass = 100;
                route = ScopeRoute(ship); ship.AIRefuel();
                Check(ship.GetRCSRemain() == 120, "Native AIRefuel did not fill prescribed capacity");
                StarSystem.fEpoch = 1050; Check(route.TimeAdvance(ship.objSS), "Native route did not advance");
                double remaining = ship.GetRCSRemain();
                Check(Math.Abs(remaining - 95) < 1e-5, "Replenished fuel was not preserved across roles/homes");
                Console.WriteLine(scenario.Item1 + " / " + scenario.Item2 + ": changed-target old route " + (guarded ? "cleared" : "retained") +
                    "; 100 kg + 20 kg refill - 25 kg planned use -> " + remaining + " kg.");
                StarSystem.fEpoch = 1000; ship.fShallowRCSRemass = 100;
                route = ScopeRoute(ship); ship.fShallowRCSRemass += 20;
                foreach (var point in route.GetNavDataPoints()) point.FuelLevel += 20;
                StarSystem.fEpoch = 1050; route.TimeAdvance(ship.objSS);
                Check(Math.Abs(ship.GetRCSRemain() - 95) < 1e-5 && route.Origin.FuelLevel - route.Destination.FuelLevel == 50,
                    "Role-independent fuel-rebase control altered planned consumption");
                if (guarded) {
                    StarSystem.fEpoch = 1000; ship.fShallowRCSRemass = 100;
                    route = ScopeRoute(ship); route.Destination.FuelLevel = -10;
                    ship.AIRefuel();
                    Check(ship.GetRCSRemain() == 120 && ReferenceEquals(ship.objSS.NavData, route), "Refill rejected route before rebasing");
                    Check(route.Destination.FuelLevel == 10, "Replenishment did not translate negative old budget");
                    var guardType = typeof(Ostrabus.OstrabusPlugin).Assembly.GetType("Ostrabus.Venus.VorbNavPlanGuard", true);
                    Check((bool)AccessTools.Method(guardType, "ValidateCurrentPlan").Invoke(null, new object[] { ship }), "Rebased VORB route did not pass its unchanged validator");
                    Console.WriteLine("VORB ordering regression: funded arrival budget is +10 kg before route validation.");
                }
            }
            // Whole native Main/selector/AI progression with only general deployer
            // recovery installed: the VORB FlyTo prefix is deliberately absent.
            foreach (string name in new[] { "HaulShipReturnPatch", "AnchorDockedShipReturnPatch", "HaulerIdleManeuverPatch" })
                production.CreateClassProcessor(typeof(Ostrabus.OstrabusPlugin).Assembly.GetType("Ostrabus.Haulers." + name, true)).Patch();
            boundary.Patch(AccessTools.Method(typeof(FlyTo), nameof(FlyTo.RunCommand)), prefix: new HarmonyMethod(typeof(Program), nameof(ScopeFlight)));
            boundary.Patch(AccessTools.Method(typeof(CollisionManager), nameof(CollisionManager.GetCollisionDistanceAU), new[] { typeof(ShipSitu), typeof(ShipSitu) }), prefix: new HarmonyMethod(typeof(Program), nameof(ScopeCollision)));
            foreach (string stationId in new[] { "VORB", "OKLG", "HQCH" }) foreach (string stage in new[] { "1", "3" }) {
                var ship = MakeShip(100); ship.IsAIShip = true;
                Set(ship, "aNavs", new List<CondOwner>());
                ((Dictionary<string, Ship>)AccessTools.Field(typeof(Ship), "aDocked").GetValue(ship)).Clear();
                var station = MakeShip(0); station.strRegID = stationId; station.objSS.bIsBO = true; station.objSS.vPosx = .0001;
                var pilot = new HelpPilot(ship) { Role = AIType.HaulerDeployer, Candidate = station };
                var ai = Empty<AIShip>(); Set(ai, "_shipUs", ship); Set(ai, "_aiCharacter", pilot);
                Back(ai, "HomeStation", stationId); Back(ai, "Blackboard", pilot.Blackboard);
                AIShipManager.dictAIs = new Dictionary<string, List<AIShip>> { [stationId] = new List<AIShip> { ai } };
                CrewSim.system.dictShips = new Dictionary<string, Ship> { [ship.strRegID] = ship, [stationId] = station };
                var main = new StickySequenceCommand("Main", new ICommand[] { new Dock(pilot), new HaulShip(pilot), new AnchorDockedShip(pilot), new FlyTo(pilot) });
                main.SaveData = new[] { stage };
                pilot.BehaviorTree = new SelectorCommand("BehaviorTree", pilot.PriorityQueue, main); Set(ai, "_behaviorTree", pilot.BehaviorTree);
                int before = scopeFlights;
                ai.RunAI(); if (stage == "3") ai.RunAI();
                Check(scopeFlights > before && ReferenceEquals(ship.shipScanTarget, station), "General recovery did not reach native FlyTo from Main[" + stage + "] at " + stationId);
                Check(!new AnchorDockedShip(pilot).CanExecute(pilot.Blackboard), "Empty AnchorDockedShip no longer skips inherited Undock execution");
            }
            Console.WriteLine(checks + " native cross-role/home regression assertions passed. Retarget scope remains regional; refill accounting is shared.");
        } finally { production.UnpatchSelf(); }
    }
}
