using System;
using System.Collections.Generic;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.Core.Models;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using Ostrabus.MiscBugFixes;
using LitJson;

static partial class LifecycleChecks {
    static readonly HashSet<CondOwner> regionalDestroyed = new HashSet<CondOwner>();
    static int regionalCrewCleanupCalls;
    static readonly Dictionary<AIShip,int> regionalRuns = new Dictionary<AIShip,int>();
    static bool ObserveRegionalRun(AIShip __instance, bool __runOriginal) {
        if (!__runOriginal) return false;
        regionalRuns.TryGetValue(__instance, out int count); regionalRuns[__instance] = count + 1; return false;
    }
    static bool RegionalSetupAI(AIShip __instance, AIType at, Ship ship) {
        // Role/asset construction is outside this cleanup harness. Execute the
        // native saved-controller constructor, save loading and registration.
        var pilot = new Program.Pilot { ShipUs = ship, Role = at };
        pilot.PriorityQueue = new QueuedCommandsNode("fixture-priorities", pilot);
        Field(__instance, "_shipUs", ship); Field(__instance, "_aiCharacter", pilot);
        Field(__instance, "<Blackboard>k__BackingField", new Blackboard());
        return false;
    }
    static bool NoCond(ref bool __result) { __result = false; return false; }
    static bool NoPlots(ref List<string> __result) { __result = new List<string>(); return false; }
    static void ObserveCrewCleanup() { regionalCrewCleanupCalls++; }
    static bool RegionalDestroyCO(CondOwner __instance) {
        regionalDestroyed.Add(__instance);
        __instance.bDestroyed = true;
        __instance.ship = null;
        return false;
    }
    static bool RegionalRemoveCO(Ship __instance, CondOwner objCO) {
        // Mechanical item-map unlink is a fixture boundary. The native region
        // branch and actual Ship.Destroy choose whether each CO is destroyed.
        var items = (DictionaryArray<string, CondOwner>)AccessTools.Field(typeof(Ship), "mapICOs").GetValue(__instance);
        items.Remove(objCO.strID);
        objCO.ship = null;
        return false;
    }
    static CondOwner RegionalPerson(Ship vessel, string id) {
        var person = Empty<CondOwner>(); Field(person, "_strID", id); person.ship = vessel; person.bAlive = true;
        var spec = Empty<PersonSpec>(); spec.strCO = id; Field(spec, "co", person); person.pspec = spec;
        ((List<PersonSpec>)AccessTools.Field(typeof(Ship), "aPeople").GetValue(vessel)).Add(spec);
        ((DictionaryArray<string, CondOwner>)AccessTools.Field(typeof(Ship), "mapICOs").GetValue(vessel))[id] = person;
        return person;
    }
    static void RegionalLink(Ship a, Ship b, string key) {
        ((Dictionary<string, Ship>)AccessTools.Field(typeof(Ship), "aDocked").GetValue(a))[key] = b;
        ((Dictionary<string, Ship>)AccessTools.Field(typeof(Ship), "aDocked").GetValue(b))[key] = a;
        a.CachedAllDockedShips = null; b.CachedAllDockedShips = null;
    }
    static void RegionCleanupReachability(Harmony lifecycle) {
        var boundary = new Harmony("offline.region-cleanup.boundaries");
        var removal = AccessTools.Method(typeof(Ship), nameof(Ship.Destroy));
        try {
            foreach (var method in AccessTools.GetDeclaredMethods(typeof(CondOwner))) if (method.Name == "HasCond")
                boundary.Patch(method, prefix: new HarmonyMethod(typeof(LifecycleChecks), nameof(NoCond)));
            boundary.Patch(AccessTools.Method(typeof(CondOwner), nameof(CondOwner.Destroy)),
                prefix: new HarmonyMethod(typeof(LifecycleChecks), nameof(RegionalDestroyCO)) { priority = Priority.First });
            boundary.Patch(AccessTools.Method(typeof(CondOwner), nameof(CondOwner.ValidateParent)), prefix: new HarmonyMethod(typeof(LifecycleChecks), nameof(Quiet)));
            boundary.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.RemoveCO), new[] { typeof(CondOwner), typeof(bool) }),
                prefix: new HarmonyMethod(typeof(LifecycleChecks), nameof(RegionalRemoveCO)));
            boundary.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.Maneuver)), prefix: new HarmonyMethod(typeof(LifecycleChecks), nameof(Quiet)));
            boundary.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.SetThrust)), prefix: new HarmonyMethod(typeof(LifecycleChecks), nameof(Quiet)));
            boundary.Patch(AccessTools.Method(typeof(AIShipManager), "CrewCleanup"), prefix: new HarmonyMethod(typeof(LifecycleChecks), nameof(ObserveCrewCleanup)));
            boundary.Patch(AccessTools.Method(typeof(AIShipManager), "RunAIQueue"), prefix: new HarmonyMethod(typeof(LifecycleChecks), nameof(Quiet)));
            boundary.Patch(AccessTools.Method(typeof(AIShipManager), "CheckTracking"), prefix: new HarmonyMethod(typeof(LifecycleChecks), nameof(Quiet)));
            boundary.Patch(AccessTools.Method(typeof(PlotManager), nameof(PlotManager.GetShipsInPlots)), prefix: new HarmonyMethod(typeof(LifecycleChecks), nameof(NoPlots)));
            AIShipManager.IsTestCase = true; // Native test gate occurs AFTER RegionCleanup.
            foreach (bool production in new[] { false, true }) {
                if (!production) {
                    lifecycle.Unpatch(removal, AccessTools.Method(typeof(Ostrabus.MiscBugFixes.ShipDestroyedTargetsPatch), "Prefix"));
                    lifecycle.Unpatch(removal, AccessTools.Method(typeof(Ostrabus.MiscBugFixes.ShipDestroyedTargetsPatch), "Finalizer"));
                } else lifecycle.CreateClassProcessor(typeof(Ostrabus.MiscBugFixes.ShipDestroyedTargetsPatch)).Patch();
                foreach (var role in new[] { AIType.Police, AIType.Navy, AIType.Scav, AIType.Civilian })
                    foreach (bool shallow in new[] { false, true }) {
                        RunRegionalCase(role, shallow, "direct", production);
                        RunRegionalCase(role, shallow, "chain", production);
                    }
                foreach (string scenario in new[] { "unchanged", "interregional", "station", "remote-owned", "player-aboard", "unconnected-empty" })
                    RunRegionalCase(AIType.Civilian, true, scenario, production);
            }
            lifecycle.CreateClassProcessor(typeof(PlayerAttachedRegionCleanupPatch)).Patch();
            lifecycle.CreateClassProcessor(typeof(PlayerAttachedRegionSchedulingPatch)).Patch();
            boundary.Unpatch(AccessTools.Method(typeof(AIShipManager), "RunAIQueue"), AccessTools.Method(typeof(LifecycleChecks), nameof(Quiet)));
            boundary.Patch(AccessTools.Method(typeof(AIShip), nameof(AIShip.RunAI)), prefix: new HarmonyMethod(typeof(LifecycleChecks), nameof(ObserveRegionalRun)) { priority = Priority.Last });
            boundary.Patch(AccessTools.Method(typeof(AIShip), "SetupAI"), prefix: new HarmonyMethod(typeof(LifecycleChecks), nameof(RegionalSetupAI)));
            foreach (var role in new[] { AIType.Police, AIType.Navy, AIType.Scav, AIType.Civilian, AIType.Pirate,
                AIType.HaulerDeployer, AIType.HaulerRetriever, AIType.HaulerCargo })
                foreach (bool shallow in new[] { false, true }) {
                    RunRegionalCase(role, shallow, "direct", true, true);
                    RunRegionalCase(role, shallow, "chain", true, true);
                }
            foreach (string scenario in new[] { "unchanged", "interregional", "station", "remote-owned", "player-aboard", "unconnected-empty",
                "crewless-capture", "unregistered-capture", "unregistered-derelict", "ownership-change" })
                RunRegionalCase(AIType.Pirate, true, scenario, true, true);
            lifecycle.CreateClassProcessor(typeof(CrewedAIRegistrationPatch)).Patch();
            lifecycle.CreateClassProcessor(typeof(CrewedAIExecutionPatch)).Patch();
            foreach (string scenario in new[] { "direct", "chain", "crewless-capture", "unregistered-derelict", "remote-owned" })
                RunRegionalCase(AIType.Pirate, true, scenario, true, true, true);
        } finally { AIShipManager.IsTestCase = false; regionalDestroyed.Clear(); regionalRuns.Clear(); boundary.UnpatchSelf(); }
        Console.WriteLine("Reproduced native player-connected cleanup destruction, then verified repaired retention, native scheduling, primitive save data and detachment without changing crew, orders, motion or fuel.");
    }
    static void RunRegionalCase(AIType role, bool shallow, string scenario, bool production, bool repaired = false, bool crewGuard = false) {
        var world = Empty<StarSystem>(); InitializeCollections(world); CrewSim.system = world;
        AIShipManager.dictAIs = new Dictionary<string, List<AIShip>>();
        AccessTools.Field(typeof(AIShipManager), "dictPilotSrcRegIDs").SetValue(null, new Dictionary<string, List<string>>());
        AccessTools.Field(typeof(AIShipManager), "dictSpawnRegIDs").SetValue(null, new Dictionary<string, List<string>>());
        object queue = AccessTools.Field(typeof(AIShipManager), "_aIQueue").GetValue(null);
        queue.GetType().GetMethod("Clear").Invoke(queue, null);
        regionalDestroyed.Clear(); regionalRuns.Clear(); regionalCrewCleanupCalls = 0;
        var oldStation = LifetimeShip(world, "OLD-ATC"); oldStation.objSS.bIsBO = oldStation.objSS.bIsRegion = true;
        var newStation = LifetimeShip(world, "NEW-ATC"); newStation.objSS.bIsBO = newStation.objSS.bIsRegion = true;
        ((Dictionary<string, List<string>>)AccessTools.Field(typeof(AIShipManager), "dictPilotSrcRegIDs").GetValue(null))[oldStation.strRegID] = new List<string> { oldStation.strRegID };
        oldStation.objSS.vPosx = 0; oldStation.objSS.vPosy = 0;
        newStation.objSS.vPosx = 1; newStation.objSS.vPosy = 0;
        foreach (var station in new[] { oldStation, newStation })
            ((List<string>)AccessTools.Field(typeof(Ship), "aDockingPorts").GetValue(station)).Add("station-port");
        var playerHull = LifetimeShip(world, "PLAYER-TUG"); var actor = LifetimeShip(world, "LOCAL-AI");
        Field(actor, "nLoadState", shallow ? Ship.Loaded.Shallow : Ship.Loaded.Full); actor.IsAIShip = true;
        playerHull.objSS.vPosx = playerHull.objSS.vPosy = actor.objSS.vPosx = actor.objSS.vPosy = 0;
        Ship selectedHull = scenario == "player-aboard" ? actor : scenario == "remote-owned" ? LifetimeShip(world, "PLAYER-FERRY") : playerHull;
        selectedHull.objSS.vPosx = selectedHull.objSS.vPosy = 0;
        var player = RegionalPerson(selectedHull, "regional-player"); CrewSim.coPlayer = player;
        ((Dictionary<string, string>)AccessTools.Field(typeof(StarSystem), "dictShipOwners").GetValue(world))[playerHull.strRegID] = player.strID;
        bool unregistered = scenario.StartsWith("unregistered-", StringComparison.Ordinal);
        bool crewless = unregistered || scenario == "crewless-capture" || scenario == "unconnected-empty";
        CondOwner pilot = crewless ? null : RegionalPerson(actor, "regional-pilot");
        var owners = (Dictionary<string,string>)AccessTools.Field(typeof(StarSystem), "dictShipOwners").GetValue(world);
        owners[oldStation.strRegID] = owners[actor.strRegID] = "pirate-faction";
        if (scenario == "unregistered-derelict") actor.DMGStatus = Ship.Damage.Derelict;
        if (scenario == "chain") {
            var middle = LifetimeShip(world, "MIDDLE-HULL"); middle.objSS.vPosx = middle.objSS.vPosy = 0;
            RegionalLink(actor, middle, "ai-middle"); RegionalLink(middle, playerHull, "middle-player");
        } else if (scenario != "unconnected-empty") RegionalLink(actor, playerHull, "ai-player");
        if (scenario == "station") actor.objSS.bIsBO = true;
        var controller = Empty<AIShip>(); Field(controller, "_shipUs", actor);
        if (scenario == "interregional") role = AIType.HaulerCargo;
        var aiPilot = new Program.Pilot { ShipUs = actor, Role = role };
        aiPilot.PriorityQueue = new QueuedCommandsNode("fixture-priorities", aiPilot);
        Field(controller, "_aiCharacter", aiPilot);
        var board = new Blackboard(); board.Set("ExistingIntent", "native-order");
        Field(controller, "<Blackboard>k__BackingField", board);
        Field(controller, "<HomeStation>k__BackingField", oldStation.strRegID);
        var route = LifetimeRoute(actor);
        string routeBefore = JsonMapper.ToJson(route.GetJSON());
        AIShipManager.strATCLast = oldStation.strRegID;
        AccessTools.Field(typeof(AIShipManager), "_shipATCLast").SetValue(null, oldStation);
        string bucket = scenario == "interregional" ? "INTERREGIONAL" : oldStation.strRegID;
        if (!unregistered) {
            AIShipManager.AddAIToDict(controller, bucket);
            bool rejected = crewGuard && crewless;
            Check(AIShipManager.dictAIs[bucket].Contains(controller) != rejected, "Registration did not obey the existing crew-integrity rule");
            unregistered = rejected;
        }
        bool connected = scenario != "unconnected-empty";
        Check(actor.IsDockedWithPlayer() == connected, "Native ownership/recursive docking predicate did not establish the intended case");
        CollisionManager.RefreshCurrentRegion();
        Check(CollisionManager.strATCClosest == oldStation.strRegID, "Native initial ATC selection is not the old region");
        if (scenario != "unchanged") {
            player.ship.objSS.vPosx = 1;
            if (scenario != "remote-owned") {
                playerHull.objSS.vPosx = actor.objSS.vPosx = 1;
                foreach (var attached in playerHull.GetAllDockedShips()) attached.objSS.vPosx = 1;
            }
        }
        CollisionManager.RefreshCurrentRegion();
        bool changed = scenario != "unchanged";
        Check((CollisionManager.strATCClosest != AIShipManager.strATCLast) == changed, "Native station lookup did not produce the expected region boundary");
        AIShipManager.Update();
        bool retained = repaired && changed && connected && !unregistered && scenario != "interregional" && scenario != "station";
        bool destroyed = changed && !unregistered && scenario != "interregional" && scenario != "station" && !retained;
        Check(actor.bDestroyed == destroyed && (world.GetShipByRegID(actor.strRegID) == null) == destroyed,
            "Normal Update did not reach the expected hull destruction outcome: " + scenario);
        if (pilot != null) Check(regionalDestroyed.Contains(pilot) == destroyed,
            "Remaining NPC did not reach native hull-driven CO destruction: " + scenario);
        Check(regionalCrewCleanupCalls == (scenario == "unconnected-empty" ? 1 : 0),
            "Player connection was not the native crew-relocation gate: " + scenario);
        Check(!playerHull.bDestroyed && world.GetShipByRegID(playerHull.strRegID) == playerHull,
            "Cleanup destroyed the player's owned hull");
        Check(regionalDestroyed.Contains(player) == (scenario == "player-aboard" && destroyed), "Unexpected selected-character destruction boundary");
        if (changed) Check(AIShipManager.strATCLast == newStation.strRegID, "Region cleanup did not finish its normal caller path");
        if (retained) {
            Check(AIShipManager.dictAIs["INTERREGIONAL"].Contains(controller) && !AIShipManager.dictAIs[oldStation.strRegID].Contains(controller), "Retained AI has wrong or duplicate registration");
            Check(regionalRuns.TryGetValue(controller, out int ran) && ran == 1, "Retained controller was not natively scheduled exactly once after cleanup");
            Check(actor.IsDockedWithPlayer() && actor.fShallowRCSRemass == 100 && actor.fShallowFusionRemain == 200 &&
                actor.objSS.vVelX == 3e-9 && actor.objSS.vVelY == 4e-9 && actor.objSS.fW == .02f &&
                ReferenceEquals(actor.objSS.NavData, route) && JsonMapper.ToJson(route.GetJSON()) == routeBefore &&
                controller.HomeStation == oldStation.strRegID && ReferenceEquals(controller.AICharacter, aiPilot), "Retention changed crew connection, motion, fuel, route, pilot or home");
            var save = AIShipManager.GetJSONAIShipSave(actor.strRegID);
            var json = JsonMapper.ToJson(save);
            var restored = JsonMapper.ToObject<JsonAIShipSave>(json);
            var nativeBoard = new Blackboard(); nativeBoard.LoadSavedData(restored.ablackboard);
            Check(restored.strATCLast == "INTERREGIONAL" && restored.strHomeStation == oldStation.strRegID && restored.enumAIType == role &&
                nativeBoard.Get<string>(PlayerAttachedRegion.OriginKey) == oldStation.strRegID && nativeBoard.Get<string>("ExistingIntent") == "native-order", "Native controller/blackboard serialization did not retain valid primitive state");
            var previous = controller;
            AIShipManager.dictAIs = new Dictionary<string,List<AIShip>>();
            queue.GetType().GetMethod("Clear").Invoke(queue, null);
            controller = AIShipManager.AddAIToShip(actor, restored.enumAIType, restored.strATCLast, restored, gameLoad:true);
            Check(!ReferenceEquals(controller, previous) && ReferenceEquals(controller.Ship, actor) && controller.HomeStation == oldStation.strRegID &&
                controller.Blackboard.Get<string>(PlayerAttachedRegion.OriginKey) == oldStation.strRegID &&
                controller.Blackboard.Get<string>("ExistingIntent") == "native-order" && AIShipManager.dictAIs["INTERREGIONAL"].Count == 1,
                "Native saved-controller restore lost membership, home, primitive marker or existing state");
            // Another boundary while still attached must neither dispose nor duplicate it.
            CollisionManager.strATCClosest = oldStation.strRegID;
            AIShipManager.Update();
            Check(!actor.bDestroyed && AIShipManager.dictAIs["INTERREGIONAL"].Count == 1 && regionalRuns[controller] == 1, "Repeated boundary lost or duplicated retained controller");
            // A detach in the active region resumes local scheduling; a remote
            // detach returns to dormant native regional membership until entry.
            if (scenario == "ownership-change") owners[playerHull.strRegID] = "new-owner";
            else foreach (var hull in world.dictShips.Values) {
                ((Dictionary<string,Ship>)AccessTools.Field(typeof(Ship), "aDocked").GetValue(hull)).Clear();
                hull.CachedAllDockedShips = null;
            }
            actor.objSS.vPosx = scenario == "remote-owned" ? 1 : 0;
            string expectedRegion = scenario == "remote-owned" ? newStation.strRegID : oldStation.strRegID;
            queue.GetType().GetMethod("Clear").Invoke(queue, null);
            AccessTools.Method(typeof(AIShipManager), "RunAIQueue").Invoke(null, null);
            Check(AIShipManager.dictAIs["INTERREGIONAL"].Count == 0 && AIShipManager.dictAIs[expectedRegion].Contains(controller) &&
                !controller.Blackboard.HasKey(PlayerAttachedRegion.OriginKey), "Detached/ownership-changed controller did not resume physical regional membership");
            Check(regionalRuns[controller] == (scenario == "remote-owned" ? 1 : 2), "Detached controller did not obey native local scheduling policy");
            Check(!actor.bDestroyed && controller.Blackboard.Get<string>("ExistingIntent") == "native-order" && controller.HomeStation == oldStation.strRegID &&
                ReferenceEquals(actor.objSS.NavData, route), "Detachment reset native intent, home, route or hull");
        }
        if (repaired && scenario == "interregional") Check(!board.HasKey(PlayerAttachedRegion.OriginKey), "Real freight was reclassified as a retained local AI");
        if (unregistered) Check(!AIShipManager.dictAIs.ContainsKey("INTERREGIONAL"), "Crewless captured hull was given an invented controller");
        Console.WriteLine("REGION " + (crewGuard ? "repaired+crew-integrity" : repaired ? "repaired" : production ? "current-lifetime-hooks" : "native") + " " + role + " " + (shallow ? "shallow" : "full") + " " + scenario
            + ": destroyed=" + actor.bDestroyed + ", crew-relocation-calls=" + regionalCrewCleanupCalls + ", player-CO-destroyed=" + regionalDestroyed.Contains(player));
    }
}
