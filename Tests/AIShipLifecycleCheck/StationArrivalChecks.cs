using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using Ostranauts.Ships.Comms;
using Ostranauts.Trading;

static partial class Program {
    static int arrivalMoves, arrivalDeaths, cargoArrivals;
    static bool expireOnCatchup, failArrivalMove;
    static bool ArrivalCatchup(CondOwner __instance) { if (expireOnCatchup) __instance.bAlive = false; return false; }
    static bool AlreadyWandering(ref bool __result) { __result = true; return false; }
    static bool ArrivalMove(CondOwner objCO, Ship objShipNew) {
        if (failArrivalMove) throw new InvalidOperationException("Fixture scene handoff failed.");
        arrivalMoves++;
        ((List<PersonSpec>)AccessTools.Field(typeof(Ship), "aPeople").GetValue(objCO.ship)).Remove(objCO.pspec);
        ((List<PersonSpec>)AccessTools.Field(typeof(Ship), "aPeople").GetValue(objShipNew)).Add(objCO.pspec);
        objCO.ship = objShipNew; return false;
    }
    static bool ArrivalDeath(CondOwner __instance) { arrivalDeaths++; __instance.bDestroyed = true; return false; }
    static bool CargoArrival() { cargoArrivals++; return false; }
    static bool WreckDestroy(Ship __instance) { __instance.bDestroyed = true; CrewSim.system.dictShips.Remove(__instance.strRegID); return false; }
    static bool HelpLoot(string strName, ref Loot __result) { __result = Empty<Loot>(); return false; }
    static (int status, Dictionary<string,object> body) Arrival(string path, bool apply) {
        var type = typeof(Ostrabus.OstrabusPlugin).Assembly.GetType("Ostrabus.Inspection.ShipArrival", true);
        var reply = type.GetMethod("Run", BindingFlags.Static|BindingFlags.NonPublic).Invoke(null, new object[] { path, apply });
        return ((int)reply.GetType().GetField("Status", BindingFlags.Instance|BindingFlags.NonPublic).GetValue(reply),
            (Dictionary<string,object>)reply.GetType().GetField("Body", BindingFlags.Instance|BindingFlags.NonPublic).GetValue(reply));
    }
    static void CheckStationArrival() {
        var boundary = new Harmony("offline.station-arrival.boundaries");
        try {
            // Execute the actual retirement method, unlike the earlier lifecycle
            // fixture which only observed that the command reached this boundary.
            boundary.Unpatch(AccessTools.Method(typeof(CrewSim), nameof(CrewSim.DockAndDespawn)), HarmonyPatchType.Prefix, "offline.ai-lifecycle.boundaries");
            boundary.Patch(AccessTools.Method(typeof(DataHandler), nameof(DataHandler.GetCondTrigger), new[] { typeof(string) }), prefix:new HarmonyMethod(typeof(Program), nameof(Dropoff)));
            boundary.Patch(AccessTools.Method(typeof(CondTrigger), nameof(CondTrigger.Triggered), new[] { typeof(CondOwner), typeof(string), typeof(bool) }), prefix:new HarmonyMethod(typeof(Program), nameof(ValidDropoff)));
            boundary.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.GetShipFactions)), prefix:new HarmonyMethod(typeof(Program), nameof(Factions)));
            boundary.Patch(AccessTools.Method(typeof(CondOwner), nameof(CondOwner.GetFactionScore), new[] { typeof(List<string>) }), prefix:new HarmonyMethod(typeof(Program), nameof(FactionScore)));
            boundary.Patch(AccessTools.Method(typeof(PlotManager), nameof(PlotManager.GetShipsInPlots)), prefix:new HarmonyMethod(typeof(Program), nameof(NoPlots)));
            boundary.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.StopManeuver)), prefix:new HarmonyMethod(typeof(Program), nameof(Quiet)));
            boundary.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.LogAdd)), prefix:new HarmonyMethod(typeof(Program), nameof(Quiet)));
            boundary.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.ToggleVis)), prefix:new HarmonyMethod(typeof(Program), nameof(Quiet)));
            boundary.Patch(AccessTools.Method(typeof(DataHandler), nameof(DataHandler.GetString), new[] { typeof(string), typeof(bool) }), prefix:new HarmonyMethod(typeof(Program), nameof(TextKey)));
            boundary.Patch(AccessTools.Method(typeof(CondOwner), nameof(CondOwner.CatchUp)), prefix:new HarmonyMethod(typeof(Program), nameof(ArrivalCatchup)));
            boundary.Patch(AccessTools.Method(typeof(CondOwner), nameof(CondOwner.LogMove)), prefix:new HarmonyMethod(typeof(Program), nameof(Quiet)));
            boundary.Patch(AccessTools.Method(typeof(CondOwner), nameof(CondOwner.HasQueuedInteraction)), prefix:new HarmonyMethod(typeof(Program), nameof(AlreadyWandering)));
            boundary.Patch(AccessTools.Method(typeof(CondOwner), nameof(CondOwner.Destroy)), prefix:new HarmonyMethod(typeof(Program), nameof(ArrivalDeath)));
            boundary.Patch(AccessTools.Method(typeof(CrewSim), nameof(CrewSim.MoveCO)), prefix:new HarmonyMethod(typeof(Program), nameof(ArrivalMove)));
            boundary.Patch(AccessTools.Method(typeof(MarketManager), nameof(MarketManager.UnregisterCargoShip)), prefix:new HarmonyMethod(typeof(Program), nameof(CargoArrival)));
            boundary.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.ToggleTowBracesShallow)), prefix:new HarmonyMethod(typeof(Program), nameof(Quiet)));
            boundary.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.Undock)), prefix:new HarmonyMethod(typeof(Program), nameof(RescueUndock)));
            boundary.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.Destroy)), prefix:new HarmonyMethod(typeof(Program), nameof(WreckDestroy)));
            boundary.Patch(AccessTools.Method(typeof(DataHandler), nameof(DataHandler.GetLoot)), prefix:new HarmonyMethod(typeof(Program), nameof(HelpLoot)));
            boundary.Patch(AccessTools.Method(typeof(Loot), nameof(Loot.ApplyCondLoot)), prefix:new HarmonyMethod(typeof(Program), nameof(Quiet)));
            foreach (bool loadedStation in new[] { false, true }) foreach (var role in new[] { AIType.Police, AIType.Scav, AIType.HaulerRetriever, AIType.HaulerDeployer }) {
                PlotManager.dictPlotsActive = new Dictionary<string,JsonPlotSave>(); acceptDropoff = true; rescueFactionScore = 0; expireOnCatchup = false;
                arrivalMoves = arrivalDeaths = cargoArrivals = 0;
                CrewSim.coPlayer = Empty<CondOwner>(); Set(CrewSim.coPlayer, "_strID", "PLAYER"); CrewSim.coPlayer.ship = MakeShip("PLAYER");
                Set(CrewSim.coPlayer, "aMyShips", new HashSet<string>());
                CrewSim.system = Empty<StarSystem>(); AIShipManager.dictAIs = new Dictionary<string,List<AIShip>> { ["HOME"] = new List<AIShip>() };
                var home = MakeShip("HOME"); home.objSS.bIsBO = true; home.objSS.vPosx = .01; Set(home, "aDockingPorts", new List<string> { "dock" });
                if (loadedStation) Set(home, "nLoadState", Ship.Loaded.Full);
                var ship = MakeShip("TRAFFIC"); Set(ship, "bAIShip", true); ship.objSS.vPosx = 3; ship.objSS.vVelX = 1E-5;
                BaseCharacter pilot = role == AIType.Police ? (BaseCharacter)Empty<PolicePilot>() : role == AIType.Scav ? Empty<ScavPilot>() : role == AIType.HaulerRetriever ? Empty<HaulerRetrievingPilot>() : Empty<HaulerDeployingPilot>();
                Back(pilot, "ShipUs", ship); pilot.Blackboard = new Blackboard(); pilot.PriorityQueue = new QueuedCommandsNode("PriorityQueue", pilot);
                var ai = Controller(ship, pilot); Back(ai, "HomeStation", home.strRegID); AIShipManager.dictAIs["HOME"].Add(ai);
                var person = Person(ship, "Original Pilot"); var second = Person(ship, "Original Crew");
                CrewSim.system.dictShips = new Dictionary<string,Ship> { [home.strRegID] = home, [ship.strRegID] = ship };
                var owners = new Dictionary<string,string> { [ship.strRegID] = "TRAFFICOWNER" }; Set(CrewSim.system, "dictShipOwners", owners);
                Ship wreck = null;
                {
                    wreck = MakeShip("TOW"); wreck.DMGStatus = Ship.Damage.Derelict;
                    var comms = Empty<Comms>(); Set(comms, "_ship", wreck); Back(wreck, "Comms", comms);
                    CrewSim.system.dictShips.Add(wreck.strRegID, wreck); owners[wreck.strRegID] = "TRAFFICOWNER"; Connect(ship, wreck);
                }
                var preview = Arrival("/ships/TRAFFIC/arrival", false);
                Check((bool)preview.body["eligible"] && arrivalMoves == 0 && !ship.HideFromSystem && person.ship == ship && ship.objSS.vPosx == 3, "Preview does not move/retire crew or hull: "+role);
                acceptDropoff = false; Check(Arrival("/ships/TRAFFIC/arrival", true).status == 409 && arrivalMoves == 0 && arrivalDeaths == 0 && !ship.HideFromSystem, "Rejected native dropoff cannot delete crew"); acceptDropoff = true;
                rescueFactionScore = -100; Check(Arrival("/ships/TRAFFIC/arrival", true).status == 409 && arrivalDeaths == 0, "Hostile station dropoff refuses native deletion branch"); rescueFactionScore = 0;
                owners[ship.strRegID] = "PLAYER"; Check(Arrival("/ships/TRAFFIC/arrival", true).status == 409, "Player hull ownership is protected"); owners[ship.strRegID] = "TRAFFICOWNER";
                Set(ship, "nLoadState", Ship.Loaded.Full); Check(Arrival("/ships/TRAFFIC/arrival", true).status == 409, "Materialized source cannot be retired under player's scene"); Set(ship, "nLoadState", Ship.Loaded.Shallow);
                Check(Arrival("/ships/TRAFFIC/arrival/MISSING", true).status == 409, "Unavailable explicit destination refuses arrival");
                if (wreck != null) {
                    var neighbor = Person(wreck, "Neighbor Pilot");
                    Check(Arrival("/ships/TRAFFIC/arrival", true).status == 409 && neighbor.ship == wreck, "Connected occupants cannot be silently collected by retirement");
                    ((List<PersonSpec>)AccessTools.Field(typeof(Ship), "aPeople").GetValue(wreck)).Clear();
                    owners[wreck.strRegID] = "PLAYER"; Check(Arrival("/ships/TRAFFIC/arrival", true).status == 409, "Player-owned wreck cannot be consumed by hauler delivery"); owners[wreck.strRegID] = "TRAFFICOWNER";
                }
                expireOnCatchup = true; Check(Arrival("/ships/TRAFFIC/arrival", true).status == 409 && !ship.HideFromSystem && arrivalDeaths == 0, "Catchup state is checked before native destructive arrival"); expireOnCatchup = false; person.bAlive = second.bAlive = true;
                // A native failure may already have changed targets, claims or
                // delivered a tow. Report that partial state without rollback.
                failArrivalMove = true;
                var failed = Arrival("/ships/TRAFFIC/arrival/HOME", true);
                Check(failed.status == 500 && !(bool)failed.body["arrived"] && failed.body["error"] != null && arrivalDeaths == 0,
                    "Native exception is reported as incomplete with postconditions, never success");
                failArrivalMove = false;
                int priorCargoArrivals = cargoArrivals;
                var applied = Arrival("/ships/TRAFFIC/arrival/HOME", true);
                Check(applied.status == 200 && (bool)applied.body["arrived"] && (bool)applied.body["crewPreservedAtStation"], "Native station arrival verified: "+role+" loaded="+loadedStation);
                Check(arrivalMoves == 2 && arrivalDeaths == 0 && person.ship == home && second.ship == home && home.People.Count == 2 && ship.People.Count == 0, "Original crew identities transferred to station without deletion");
                Check(person.OwnsShip(home.strRegID) && !person.OwnsShip(ship.strRegID), "Native station claim and former ship unclaim execute");
                Check(ship.HideFromSystem && !ship.bDestroyed && ship.strAIDespawnedAt == home.strRegID && AIShipManager.GetAIShipByRegID(ship.strRegID) == null, "Native hull retirement records home and unregisters controller");
                Check(ship.fShallowRCSRemass == 0, "Instant arrival does not require or fabricate fuel");
                bool hauler = role == AIType.HaulerRetriever || role == AIType.HaulerDeployer;
                Check(hauler ? cargoArrivals == priorCargoArrivals+1 && wreck.bDestroyed : cargoArrivals == 0 && !wreck.bDestroyed && !wreck.IsDocked(),
                    "Haulers deliver wrecks; police/salvagers release and preserve them");
                Check(Arrival("/ships/TRAFFIC/arrival", true).status == 409 && arrivalMoves == 2 && arrivalDeaths == 0, "Repeat arrival does not transfer twice or recreate controller");
            }
            Console.WriteLine(checks + " station arrival checks passed using native Dock/DockCargoAndDespawn and CrewSim.DockAndDespawn.");
        } finally { boundary.UnpatchSelf(); }
    }
}
