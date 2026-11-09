using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;

static partial class Program {
    static bool acceptDropoff = true;
    static float rescueFactionScore;
    static int rescueUndocks;
    static bool Dropoff(string strName, ref CondTrigger __result) { __result = Empty<CondTrigger>(); return false; }
    static bool ValidDropoff(ref bool __result) { __result = acceptDropoff; return false; }
    static bool Factions(ref List<JsonFaction> __result) { __result = new List<JsonFaction> { new JsonFaction { strName = "fixture" } }; return false; }
    static bool FactionScore(ref float __result) { __result = rescueFactionScore; return false; }
    static bool NoPlots(ref List<string> __result) { __result = new List<string>(); return false; }
    static bool PhysicalBody(ref BodyOrbit __result) { __result = Empty<BodyOrbit>(); __result.strName = "BODY"; return false; }
    static bool RescueUndock(Ship __instance, Ship __0) {
        rescueUndocks++;
        __instance.ResetDockedCache(); __0.ResetDockedCache();
        ((Dictionary<string,Ship>)AccessTools.Field(typeof(Ship), "aDocked").GetValue(__instance)).Clear();
        ((Dictionary<string,Ship>)AccessTools.Field(typeof(Ship), "aDocked").GetValue(__0)).Clear();
        __instance.ResetDockedCache(); __0.ResetDockedCache();
        __instance.objSS._dockGroup = null; __0.objSS._dockGroup = null;
        return false;
    }
    static (int status, Dictionary<string,object> body) Rescue(string id, bool apply) {
        var type = typeof(Ostrabus.OstrabusPlugin).Assembly.GetType("Ostrabus.Inspection.HaulerRescue", true);
        var reply = type.GetMethod("Run", BindingFlags.Static|BindingFlags.NonPublic).Invoke(null, new object[] { "/ships/"+id+"/hauler/rescue", apply });
        return ((int)reply.GetType().GetField("Status", BindingFlags.Instance|BindingFlags.NonPublic).GetValue(reply),
            (Dictionary<string,object>)reply.GetType().GetField("Body", BindingFlags.Instance|BindingFlags.NonPublic).GetValue(reply));
    }
    static void CheckHaulerRescue() {
        var boundary = new Harmony("offline.hauler-rescue.boundaries");
        try {
            boundary.Patch(AccessTools.Method(typeof(DataHandler), nameof(DataHandler.GetCondTrigger), new[] { typeof(string) }), prefix:new HarmonyMethod(typeof(Program), nameof(Dropoff)));
            boundary.Patch(AccessTools.Method(typeof(CondTrigger), nameof(CondTrigger.Triggered), new[] { typeof(CondOwner), typeof(string), typeof(bool) }), prefix:new HarmonyMethod(typeof(Program), nameof(ValidDropoff)));
            boundary.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.GetShipFactions)), prefix:new HarmonyMethod(typeof(Program), nameof(Factions)));
            boundary.Patch(AccessTools.Method(typeof(CondOwner), nameof(CondOwner.GetFactionScore), new[] { typeof(List<string>) }), prefix:new HarmonyMethod(typeof(Program), nameof(FactionScore)));
            boundary.Patch(AccessTools.Method(typeof(PlotManager), nameof(PlotManager.GetShipsInPlots)), prefix:new HarmonyMethod(typeof(Program), nameof(NoPlots)));
            boundary.Patch(AccessTools.Method(typeof(StarSystem), nameof(StarSystem.GetNearestBO), new[] { typeof(ShipSitu), typeof(double), typeof(bool) }), prefix:new HarmonyMethod(typeof(Program), nameof(PhysicalBody)));
            boundary.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.StopManeuver)), prefix:new HarmonyMethod(typeof(Program), nameof(Quiet)));
            boundary.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.ToggleTowBracesShallow)), prefix:new HarmonyMethod(typeof(Program), nameof(Quiet)));
            boundary.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.Undock)), prefix:new HarmonyMethod(typeof(Program), nameof(RescueUndock)));
            PlotManager.dictPlotsActive = new Dictionary<string,JsonPlotSave>();
            CrewSim.coPlayer = Empty<CondOwner>(); Set(CrewSim.coPlayer, "_strID", "PLAYER"); CrewSim.coPlayer.ship = MakeShip("PLAYER");
            CrewSim.system = Empty<StarSystem>(); AIShipManager.strATCLast = "OKLG";
            AIShipManager.dictAIs = new Dictionary<string,List<AIShip>> { ["OKLG"] = new List<AIShip>() };
            var home = MakeShip("OKLG"); home.objSS.bIsBO = true; Set(home, "aDockingPorts", new List<string> { "dock" }); home.objSS.vPosx = .01; home.objSS.vVelY = 1E-7;
            var ship = MakeShip("ANY-HAULER"); Set(ship, "bAIShip", true); ship.RCSCount = 8;
            Set(ship, "fShallowRCSRemassMax", 3000d);
            var pilot = Empty<HaulerRetrievingPilot>(); Back(pilot, "ShipUs", ship); pilot.Blackboard = new Blackboard(); pilot.PriorityQueue = new QueuedCommandsNode("PriorityQueue", pilot);
            var ai = Controller(ship, pilot); Back(ai, "HomeStation", "OKLG"); AIShipManager.dictAIs["OKLG"].Add(ai);
            var person = Person(ship, "Existing Pilot");
            var wreck = MakeShip("ANY-WRECK"); wreck.DMGStatus = Ship.Damage.Derelict; Connect(ship, wreck);
            CrewSim.system.dictShips = new Dictionary<string,Ship> { [home.strRegID] = home, [ship.strRegID] = ship, [wreck.strRegID] = wreck };
            var owners = new Dictionary<string,string> { [ship.strRegID] = "OKLGHauler", [wreck.strRegID] = "OKLGCiv" }; Set(CrewSim.system, "dictShipOwners", owners);
            ship.objSS.vPosx = 3; ship.objSS.vVelX = 1E-5; ship.objSS.fW = 700; ship.objSS.fA = 1;
            double wreckX = wreck.objSS.vPosx; rescueUndocks = 0;
            var preview = Rescue(ship.strRegID, false);
            if (preview.body.TryGetValue("reason", out var refused)) Console.WriteLine("Preview refused: "+refused);
            Check((bool)preview.body["eligible"] && !(bool)preview.body["rescued"] && rescueUndocks == 0 && ship.objSS.vPosx == 3 && ship.fShallowRCSRemass == 0, "Rescue preview is eligible without physical/controller/fuel changes");
            acceptDropoff = false;
            Check(Rescue(ship.strRegID, true).status == 409 && rescueUndocks == 0, "Native dropoff rejection prevents rescue before any undocking"); acceptDropoff = true;
            owners[wreck.strRegID] = "PLAYER"; Check(Rescue(ship.strRegID, true).status == 409 && rescueUndocks == 0, "Player tow is protected"); owners[wreck.strRegID] = "OKLGCiv";
            rescueFactionScore = -100; Check(Rescue(ship.strRegID, true).status == 409 && rescueUndocks == 0, "Native hostile-station crew deletion is refused"); rescueFactionScore = 0;
            person.bAlive = false; Check(Rescue(ship.strRegID, true).status == 409, "Dead crew cannot authorize rescue"); person.bAlive = true;
            conditions[person] = new HashSet<string> { "Unconscious" }; Check(Rescue(ship.strRegID, true).status == 409, "Unconscious-only crew cannot authorize flight"); conditions[person].Clear();
            ai.Blackboard.Set("InCombat", true); Check(Rescue(ship.strRegID, true).status == 409, "Active combat is excluded"); ai.Blackboard.Set("InCombat", false);
            Back(ai, "HomeStation", "MISSING"); Check(Rescue(ship.strRegID, true).status == 409, "Missing native home refuses rescue"); Back(ai, "HomeStation", "OKLG");
            Set(ship, "fShallowRCSRemassMax", 0d); Check(Rescue(ship.strRegID, false).status == 200 && !(bool)Rescue(ship.strRegID, false).body["eligible"], "Unknown capacity is refused without lazily inventing capacity"); Set(ship, "fShallowRCSRemassMax", 3000d);
            Set(ship, "nLoadState", Ship.Loaded.Full); Check(Rescue(ship.strRegID, true).status == 409, "Materialized ships are protected"); Set(ship, "nLoadState", Ship.Loaded.Shallow);
            pilot.PriorityQueue.EnqueueCommand(new Hold(pilot)); Check(Rescue(ship.strRegID, true).status == 409, "Pending priority commands are protected");
            ((Queue<ICommand>)AccessTools.Field(typeof(QueuedCommandsNode), "commandQueue").GetValue(pilot.PriorityQueue)).Clear();
            Check(rescueUndocks == 0 && ship.objSS.vPosx == 3 && person.ship == ship, "All refused operations precede native detachment and preserve position/crew");
            var applied = Rescue(ship.strRegID, true);
            Check(applied.status == 200 && (bool)applied.body["rescued"] && (bool)applied.body["returnRequested"] && rescueUndocks == 1, "Rescue requests native station return after explicit detachment");
            Check(person.bAlive && ReferenceEquals(person.ship, ship) && AIShipManager.GetAIShipByRegID(ship.strRegID) == ai, "Same NPC and controller remain aboard; neither is recreated or deleted");
            Check(!ship.IsDocked() && !wreck.bDestroyed && wreck.objSS.vPosx == wreckX, "Wreck is detached and preserved for separate pruning");
            Check(Math.Abs((ship.objSS.vPosx-home.objSS.vPosx)*149597870.7-100) < 1E-6 && ship.objSS.vVelX == home.objSS.vVelX && ship.objSS.vVelY == home.objSS.vVelY && ship.objSS.fW == 0 && ship.objSS.fA == 0, "Explicit repair stages hauler 100 km outward at home velocity without spin");
            Check(ship.fShallowRCSRemass == ship.GetRCSMax() && ship.shipScanTarget == home && ship.objSS.strBOPORShip == "BODY" && ai.Blackboard.Get<string>("TemplateExample.StationReturn.Destination") == "OKLG", "Native refueling, repaired reference body and persistent return intent are established");
            Check(Rescue(ship.strRegID, true).status == 409 && rescueUndocks == 1, "Repeated rescue does not relocate an already-returning hauler");
            Console.WriteLine(checks + " hauler rescue API checks passed; docking/scene/eligibility data boundaries substituted.");
        } finally { boundary.UnpatchSelf(); }
    }
}
