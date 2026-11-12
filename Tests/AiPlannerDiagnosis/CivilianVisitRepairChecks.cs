using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LitJson;
using Ostrabus.MiscBugFixes;
using Ostrabus.ShipNavigation;
using Ostranauts.Core;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using Ostranauts.ShipGUIs.Utilities;
using UnityEngine;

static partial class TrafficChecks {
    static bool visitLowFuel;
    static Ship visitPlannerShip;
    static int retirementBodies;
    static bool VisitLow(ref bool __result) { __result = visitLowFuel; return false; }
    static bool VisitCost(ref double __result) { __result = 12; return false; }
    static bool VisitPlan(TargetData target, ref NavData __result) {
        __result = new NavData(visitPlannerShip);
        __result.AddNavDataPoint(new NavDataPoint(1000, new ShipSitu(false), 100, 0, 0));
        __result.AddNavDataPoint(new NavDataPoint(2000, target.Situ, 88, 0, 0));
        return false;
    }
    static void NoVisitAudio(ref bool bPlayerNotice) { bPlayerNotice = false; }
    static bool RetirementBoundary(bool __runOriginal) { if (__runOriginal) retirementBodies++; return false; }
    static List<ICommand> VisitChildren(StickySequenceCommand main) => (List<ICommand>)AccessTools.Field(typeof(StickySequenceCommand), "_childCommands").GetValue(main);
    static void LinkVisit(Ship a, Ship b) {
        ((Dictionary<string,Ship>)AccessTools.Field(typeof(Ship), "aDocked").GetValue(a))["dock"] = b;
        ((Dictionary<string,Ship>)AccessTools.Field(typeof(Ship), "aDocked").GetValue(b))["dock"] = a;
        a.CachedAllDockedShips = b.CachedAllDockedShips = null;
    }
    static void CivilianVisitRepair(Harmony outer) {
        int before = checks;
        var world = CrewSim.system; var controllers = AIShipManager.dictAIs;
        string region = AIShipManager.strATCLast;
        var boundary = new Harmony("offline.civilian-visit.physics");
        var production = new Harmony("offline.civilian-visit.production");
        try {
            outer.Unpatch(AccessTools.Method(typeof(Ship), nameof(Ship.IsDocked)), AccessTools.Method(typeof(TrafficChecks), nameof(Docked)));
            foreach (var type in new[] { typeof(CivilianVisibleVisitPatch), typeof(CivilianVisitSequencePatch), typeof(CivilianVisitRetiredPatch),
                typeof(CivilianVisitRestoredPatch), typeof(CivilianVisitSffPatch), typeof(AttachedRetirementPatch), typeof(DetachedReserveHullPatch), typeof(ClearTargetFuelPatch) })
                production.CreateClassProcessor(type).Patch();
            boundary.Patch(AccessTools.Method(typeof(AIShipManager), nameof(AIShipManager.LowFuel)), prefix: new HarmonyMethod(typeof(TrafficChecks), nameof(VisitLow)));
            boundary.Patch(AccessTools.Method(typeof(TargetNewIfNone), "GetNeededFuel"), prefix: new HarmonyMethod(typeof(TrafficChecks), nameof(VisitCost)));
            boundary.Patch(AccessTools.Method(typeof(TargetNewIfNone), "GenerateNavData"), prefix: new HarmonyMethod(typeof(TrafficChecks), nameof(VisitPlan)));
            boundary.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.StopManeuver)), prefix: new HarmonyMethod(typeof(TrafficChecks), nameof(NoVisitAudio)));
            boundary.Patch(AccessTools.Method(typeof(CrewSim), nameof(CrewSim.DockAndDespawn)), prefix: new HarmonyMethod(typeof(TrafficChecks), nameof(RetirementBoundary)) { priority = Priority.Last });
            var leafTypes = new[] { "AmIInCombat", "AmIDamaged", "SurrenderOffer", "DoIHaveDecoys", "AreMissilesComingMyWay", "DeployDecoys", "AmIArmed",
                "SetEngagementProfile", "DoIPreferShortRange", "KeepWithinFiringDistance", "KeepAtDistance", "TargetEscapeStation", "AreThreatsNearby",
                "AmIBeingTargeted", "TargetNearestStation", "SosThreatApproaching", "Undock", "FlyTo", "Dock", "Hold" };
            foreach (string name in leafTypes) {
                var type = AccessTools.TypeByName("Ostranauts.Ships.Commands." + name);
                boundary.Patch(AccessTools.Method(type, "RunCommand"), prefix: new HarmonyMethod(typeof(TrafficChecks), nameof(Leaf)));
                var can = AccessTools.DeclaredMethod(type, "CanExecute");
                if (can != null) boundary.Patch(can, prefix: new HarmonyMethod(typeof(TrafficChecks), nameof(Yes)));
            }
            foreach (string scenario in new[] { "retire", "hide-reuse", "saved-hidden", "saved-marker", "combat", "threat", "priority", "reaction", "sff", "sff-wrapper", "docked", "station-only", "low-fuel", "unchanged", "explicit-queue", "hauler-tow" }) {
                CrewSim.system = Empty<StarSystem>(); Collections(CrewSim.system);
                AIShipManager.dictAIs = new Dictionary<string,List<AIShip>>(); AIShipManager.strATCLast = "visit-fixture";
                leafResults.Clear(); leafCalls.Clear();
                leafResults["AmIInCombat"] = leafResults["AreThreatsNearby"] = CommandCode.Failure;
                leafResults["FlyTo"] = leafResults["Dock"] = CommandCode.Running;
                visitLowFuel = scenario == "low-fuel"; docked = false;
                var station = Hull("visit-station"); station.objSS.bIsBO = station.objSS.bIsRegion = true;
                home = station;
                var ship = Hull("visitor"); ship.IsAIShip = true; visitPlannerShip = ship;
                var target = Hull("old-rendezvous"); var alternative = Hull("visible-alternative");
                var pilot = new CivilianPilot(station.strRegID, ship);
                Set(pilot, "<CaptainUs>k__BackingField", Empty<CondOwner>());
                var main = CivilianVisits.Sequence(pilot);
                Check(main != null, "Production constructor did not retain native Main through the visit sequence");
                var children = VisitChildren(main); var fly = children[2];
                var ai = Empty<AIShip>(); Set(ai, "_shipUs", ship); Set(ai, "_aiCharacter", pilot);
                Set(ai, "_behaviorTree", pilot.BehaviorTree); Set(ai, "<Blackboard>k__BackingField", pilot.Blackboard);
                Set(ai, "<HomeStation>k__BackingField", station.strRegID);
                new TargetData(target).ApplyToShip(ship);
                ship.json = new JsonShip { strRegID = ship.strRegID, strScanTargetID = target.strRegID, strTargetRegID = target.strRegID, objSituScanTarget = target.objSS.GetJSON() };
                CivilianVisits.Phase(main) = 2;
                Set(ai, "_activeCommand", fly);
                var route = new NavData(ship); ship.objSS.NavData = route;
                ((FlyTo)fly).SaveData = new[] { "0.000000001", "old-trip", "12", "0", "1" };
                pilot.Blackboard.Set("TripFuelPrediction", "old-trip&12");
                ship.objSS.vAccRCS = new Vector2(1e-10f, 2e-10f); ship.objSS.fA = .03f;
                ship.objSS.vVelX = 3e-9; ship.objSS.vVelY = 4e-9;
                ship.fShallowFusionRemain = 200;
                if (scenario.StartsWith("saved-", StringComparison.Ordinal)) {
                    Set(ai, "_activeCommand", null);
                    target.HideFromSystem = true;
                    if (scenario == "saved-marker") {
                        pilot.Blackboard.Set(CivilianVisits.InvalidKey, target.strRegID);
                        target.HideFromSystem = false;
                    }
                    var saved = pilot.BehaviorTree.GetSaveData("").ToArray();
                    Check(saved.Any(s => s.strPath == "/BehaviorTree/Main" && s.aCommandPayload[0] == "2") &&
                        saved.All(s => !s.strPath.Contains("VisitSequence")), "Visit sequence leaked a mod type into native save paths");
                    var constructor = AccessTools.Constructor(typeof(CivilianPilot), new[] { typeof(string), typeof(Ship) });
                    production.Unpatch(constructor, AccessTools.Method(typeof(CivilianVisitSequencePatch), "Transpiler"));
                    try {
                        var vanillaShip = Hull("vanilla-load-control");
                        var vanilla = new CivilianPilot(station.strRegID, vanillaShip);
                        vanilla.BehaviorTree.LoadSaveData("", saved);
                        var branches = (List<ICommand>)AccessTools.Field(typeof(SelectorCommand), "_childCommands").GetValue(vanilla.BehaviorTree);
                        var vanillaMain = (StickySequenceCommand)branches[branches.Count - 1];
                        Check(vanillaMain.GetType() == typeof(StickySequenceCommand) && CivilianVisits.Phase(vanillaMain) == 2,
                            "Unpatched native constructor could not load the mod's saved Main progress");
                        CrewSim.system.dictShips.Remove(vanillaShip.strRegID);
                    } finally { production.CreateClassProcessor(typeof(CivilianVisitSequencePatch)).Patch(); }
                    var board = new Blackboard(); board.LoadSavedData(pilot.Blackboard.GetJson());
                    pilot.Blackboard.Clear(); pilot.Blackboard.LoadSavedData(board.GetJson());
                    main.LoadSaveData("/BehaviorTree", saved);
                    ship.ClearShipTarget(); ship.json.strTargetRegID = ship.json.strScanTargetID = target.strRegID; ship.json.objSituScanTarget = target.objSS.GetJSON();
                    ship.CheckTargets();
                }
                AIShipManager.AddAIToDict(ai);
                if (scenario == "combat" || scenario == "threat") {
                    // The actual native parent picks the higher-priority branch.
                    leafResults["AmIInCombat"] = scenario == "combat" ? CommandCode.Finished : CommandCode.Failure;
                    leafResults["AreThreatsNearby"] = scenario == "threat" ? CommandCode.Finished : CommandCode.Failure;
                    Set(ai, "_activeCommand", pilot.BehaviorTree.FindCommand(typeof(KeepWithinFiringDistance)));
                }
                if (scenario == "priority" || scenario == "explicit-queue") {
                    pilot.PriorityQueue.EnqueueCommand(new FlyTo(pilot));
                    if (scenario == "explicit-queue") Set(ai, "_activeCommand", new FlyTo(pilot));
                }
                if (scenario == "reaction") Set(ai, "_activeReaction", new FlyTo(pilot));
                if (scenario == "sff" || scenario == "sff-wrapper") {
                    Set(ai, "_activeCommand", null); // A saved/deferred leaf; validate on SFF entry.
                    if (scenario == "sff-wrapper") pilot.PriorityQueue.EnqueueCommand(new OneShotWrapperCommand(fly));
                }
                if (scenario == "docked" || scenario == "station-only" || scenario == "hauler-tow") LinkVisit(ship, scenario == "station-only" ? station : target);
                if (scenario == "hauler-tow") Set(ai, "_aiCharacter", new Program.Pilot { ShipUs = ship, Role = AIType.HaulerRetriever });
                if (scenario != "unchanged" && !scenario.StartsWith("saved-", StringComparison.Ordinal)) target.HideFromSystem = true;
                bool higher = new[] { "combat", "threat", "priority", "reaction", "explicit-queue" }.Contains(scenario);
                bool attached = scenario == "docked" || scenario == "station-only" || scenario == "hauler-tow";
                if (higher || attached || scenario == "unchanged") {
                    Check(ReferenceEquals(ship.shipScanTarget, target) && ReferenceEquals(ship.objSS.NavData, route), "Retirement disrupted another controller or physical encounter: " + scenario);
                    if (higher) {
                        if (scenario == "explicit-queue") {
                            var explicitFlight = new FlyTo(pilot);
                            Check(ReferenceEquals(pilot.FFWD(explicitFlight, pilot.Blackboard), explicitFlight) && ReferenceEquals(ship.objSS.NavData, route),
                                "Visit SFF validity guard intercepted an explicitly queued flight leaf");
                        }
                        int calls = leafCalls.Count;
                        ai.RunAI();
                        Check(leafCalls.Count > calls && !leafCalls.Contains("TargetNewIfNone") && ReferenceEquals(ship.shipScanTarget, target), "Visit repair preempted native priority/combat/reaction: " + scenario);
                        if (scenario != "explicit-queue") {
                            // Reuse can happen while another native branch owns
                            // control. The primitive marker must survive until
                            // the actual parent selects ordinary Main again.
                            target.HideFromSystem = false;
                            leafResults["AmIInCombat"] = leafResults["AreThreatsNearby"] = CommandCode.Failure;
                            pilot.PriorityQueue.OnExit(pilot.Blackboard);
                            Set(ai, "_activeReaction", null);
                            ai.RunAI();
                            Check(ship.shipScanTarget == null && ship.objSS.NavData == null && !pilot.Blackboard.HasKey("TripFuelPrediction"),
                                "Deferred invalid visit resumed after reuse when higher-priority behavior finished: " + scenario);
                        }
                    }
                    if (scenario == "unchanged") {
                        for (int i = 0; i < 100; i++) main.Validate();
                        long allocated = GC.GetAllocatedBytesForCurrentThread();
                        for (int i = 0; i < 5000; i++) main.Validate();
                        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
                        Check(allocated == 0, "Unchanged civilian visit validation allocates: " + allocated);
                    }
                    if (attached) {
                        retirementBodies = 0; CrewSim.DockAndDespawn(ship, station);
                        Check(retirementBodies == (scenario == "docked" ? 0 : 1), "Attached retirement disrupted a civilian encounter, station-only parking or native hauler tow delivery");
                        if (scenario == "docked") {
                            CrewSim.DockAndDespawn(target, station);
                            Check(retirementBodies == 0, "The other participant retired while its civilian visitor remained attached");
                            ((Dictionary<string,string>)AccessTools.Field(typeof(StarSystem), "dictShipOwners").GetValue(CrewSim.system))[target.strRegID] = "owner-faction";
                            Check(AIShip.FindShipForFaction("owner-faction") != target, "Attached hidden encounter hull was reused");
                        }
                    }
                } else {
                    if (scenario == "hide-reuse") { target.HideFromSystem = false; target.objSS.vPosx += .001; }
                    if (scenario == "sff" || scenario == "sff-wrapper") {
                        int instant = instantCalls;
                        Check(pilot.FFWD(fly, pilot.Blackboard) == null && instantCalls == instant, "SFF attempted docking/travel before rejecting the invalid ordinary visit");
                    } else if (scenario.StartsWith("saved-", StringComparison.Ordinal)) ai.RunAI();
                    Check(ship.shipScanTarget == null && ship.shipSituTarget == null && ship.targetRegID == null && ship.objSS.NavData == null &&
                        !pilot.Blackboard.HasKey("TripFuelPrediction") && !pilot.Blackboard.HasKey(CivilianVisits.InvalidKey), "Obsolete visit state survived: " + scenario);
                    Check(ship.json.strScanTargetID == null && ship.json.strTargetRegID == null && ship.json.objSituScanTarget == null,
                        "Saved destination can resurrect canceled visit: " + scenario);
                    Check(ship.objSS.vAccRCS == Vector2.zero && ship.objSS.fA == 0 && ship.objSS.vVelX == 3e-9 && ship.objSS.vVelY == 4e-9 &&
                        ship.fShallowRCSRemass == 100 && ship.fShallowFusionRemain == 200 && !ship.bDestroyed, "Visit cancellation changed momentum, spent fuel or hull lifetime");
                    if (scenario == "sff-wrapper") pilot.PriorityQueue.OnExit(pilot.Blackboard);
                    for (int i = 0; i < 4 && ship.shipScanTarget == null; i++) ai.RunAI();
                    Check(ship.shipScanTarget == (visitLowFuel ? station : alternative) || scenario == "saved-marker" && ship.shipScanTarget == target,
                        "Native AI did not select its next eligible rendezvous or low-fuel home: " + scenario);
                    Check(pilot.Blackboard.Get<string>("TripFuelPrediction") != "old-trip&12", "Native replanning retained the abandoned trip's budget");
                }
                Console.WriteLine("VISIT " + scenario + " passed");
            }
            Console.WriteLine((checks - before) + " production civilian visit lifecycle checks passed; native sticky/root/queue/save mechanics execute, physical flight and numerical affordability are controlled boundaries.");
        } finally {
            production.UnpatchSelf(); boundary.UnpatchSelf(); CrewSim.system = world; AIShipManager.dictAIs = controllers; AIShipManager.strATCLast = region;
            outer.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.IsDocked)), prefix: new HarmonyMethod(typeof(TrafficChecks), nameof(Docked)));
            home = world.GetShipByRegID("traffic-source"); visitPlannerShip = null; visitLowFuel = false;
            leafResults.Clear(); leafCalls.Clear(); docked = false;
        }
    }
}
