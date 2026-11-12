using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json;
using Ostranauts.Core;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using Ostranauts.Trading;

static partial class Program {
    static MarketManager recoveryMarket;
    static Dictionary<string,ShipMarket> recoveryMarkets;
    static Ship recoveryHull,recoveryStation;
    static AIShip recoveryAI;
    static CondOwner recoveryCrew;
    static bool recoveryStageFailure,recoverySyncFailure;
    static int recoverySyncs;
    static bool RecoverySync() { recoverySyncs++;if(recoverySyncFailure)throw new InvalidOperationException("fixture sync failure");return false; }
    static void RecoveryBuildGuard(ShipMarket __instance) {
        if(recoveryStageFailure && !ReferenceEquals(__instance,recoveryMarkets["MTRS"]))throw new InvalidOperationException("fixture staging failure");
    }
    static bool RecoveryStop(Ship __instance) { __instance.objSS.vAccRCS=default;__instance.objSS.vAccIn=default;return false; }
    static bool NoNearbyBody(ref BodyOrbit __result) { __result=null;return false; }
    static bool NoFreightThreat(ref CommandStatus __result) { __result=new CommandStatus(CommandCode.Failure,null);return false; }
    static (int status,Dictionary<string,object> body) Freight(string path,bool apply=false,string confirm=null) {
        var type=typeof(Ostrabus.OstrabusPlugin).Assembly.GetType("Ostrabus.Inspection.FreightRecovery",true);
        var reply=AccessTools.Method(type,"Run").Invoke(null,new object[]{path,apply,confirm});
        return ((int)AccessTools.Field(reply.GetType(),"Status").GetValue(reply),(Dictionary<string,object>)AccessTools.Field(reply.GetType(),"Body").GetValue(reply));
    }
    static MarketActorConfig RecoveryActor(int amount,int capacity,bool cargo=false,int first=0) {
        var actor=Empty<MarketActorConfig>();actor.Stock=new Dictionary<string,List<MarketItem>>{{"Electronics",Enumerable.Range(first,amount).Select(i=>new MarketItem{COName="item"+i,COCollectionName="Electronics"}).ToList()}};
        actor.MaxVirtualInventorySize=new Dictionary<string,int>{{"Electronics",capacity}};
        Set(actor,"_productionMaps",new List<Producer>());
        if(cargo) Back(actor,"CargoSpecs",new List<JsonCargoSpec>{new JsonCargoSpec{strName="fixture"}});
        return actor;
    }
    static void RecoveryWorld(bool lost=false,int amount=633) {
        recoveryStageFailure=recoverySyncFailure=false;recoverySyncs=0;
        CrewSim.system=Empty<StarSystem>();CrewSim.system.dictShips=new Dictionary<string,Ship>();
        Set(CrewSim.system,"dictShipOwners",new Dictionary<string,string>{{"FREIGHT","HOMECargoHauler"},{"MTRS","MTRS"}});
        CrewSim.coPlayer=Empty<CondOwner>();Set(CrewSim.coPlayer,"_strID","PLAYER");
        PlotManager.dictPlotsActive=new Dictionary<string,JsonPlotSave>();
        DataHandler.mapCOs=new Dictionary<string,CondOwner>();
        recoveryHull=MakeShip("FREIGHT");recoveryHull.IsAIShip=true;recoveryHull.origin="HOME";
        Set(recoveryHull,"aCores",new List<CondOwner>());
        recoveryHull.fShallowRCSRemass=0;recoveryHull.fShallowRCSRemassMax=1000;recoveryHull.fShallowFusionRemain=800;
        recoveryHull.objSS.vPosx=1;recoveryHull.objSS.Size=20;
        recoveryStation=MakeShip("MTRS");recoveryStation.objSS.bIsBO=true;recoveryStation.objSS.vPosx=.2;recoveryStation.objSS.Size=200;
        ((List<string>)AccessTools.Field(typeof(Ship),"aDockingPorts").GetValue(recoveryStation)).Add("port");
        CrewSim.system.dictShips.Add("MTRS",recoveryStation);
        if(!lost)CrewSim.system.dictShips.Add("FREIGHT",recoveryHull);
        recoveryCrew=Person(recoveryHull,"Existing Pilot");
        var pilot=new HaulerCargoPilot("HOME",recoveryHull,"fixture");
        recoveryAI=Controller(recoveryHull,pilot);Back(recoveryAI,"Blackboard",pilot.Blackboard);Back(recoveryAI,"HomeStation","HOME");
        Set(recoveryAI,"_behaviorTree",pilot.BehaviorTree);Set(recoveryAI,"_activeCommand",new FlyTo(pilot));
        AIShipManager.dictAIs=new Dictionary<string,List<AIShip>>{{"INTERREGIONAL",lost ? new List<AIShip>() : new List<AIShip>{recoveryAI}}};
        var queue=new AIShipQueue();AccessTools.Field(typeof(AIShipManager),"_aIQueue").SetValue(null,queue);if(!lost)queue.Enqueue(recoveryAI);
        Bind(recoveryHull,recoveryStation);
        recoveryMarket=Empty<MarketManager>();recoveryMarkets=new Dictionary<string,ShipMarket>();Set(recoveryMarket,"_market",recoveryMarkets);
        Set(recoveryMarket,"_regionalMarketMap",new Dictionary<string,List<string>>{{"FREIGHT",new List<string>{"FREIGHT"}},{"MTRS",new List<string>{"MTRS","FREIGHT"}}});
        AccessTools.Field(typeof(MonoSingleton<MarketManager>),"_instance").SetValue(null,recoveryMarket);
        var collection=new DataCoCollection(new JsonDCOCollection{strName="Electronics"});Set(collection,"_avgPrice",10d);Set(collection,"_avgMass",1d);
        DataHandler.dictDataCoCollections=new Dictionary<string,DataCoCollection>{{"Electronics",collection}};
        DataHandler.dictDataCOs=new Dictionary<string,DataCO>();for(int i=0;i<amount;i++) {
            var definition=Empty<DataCO>();definition.JCO=new JsonCondOwner{strName="item"+i};DataHandler.dictDataCOs.Add("item"+i,definition);
        }
        var source=new ShipMarket("FREIGHT");source.AddMarketConfig("pod",RecoveryActor(amount,1000,true));source.BuildConfig();
        var destination=new ShipMarket("MTRS");destination.AddMarketConfig("storeA",RecoveryActor(0,300));destination.AddMarketConfig("storeB",RecoveryActor(0,400));destination.BuildConfig();
        recoveryMarkets.Add("FREIGHT",source);recoveryMarkets.Add("MTRS",destination);
    }
    static string RecoverySave() => JsonConvert.SerializeObject(MarketManager.GetJSONSave());
    static void CheckFreightRecovery() {
        var h=new Harmony("offline.freight.recovery.boundaries");
        try {
            h.Patch(AccessTools.Method(typeof(BaseCharacter),"AssignCaptain"),prefix:new HarmonyMethod(typeof(Program),nameof(Quiet)));
            h.Patch(AccessTools.Method(typeof(Ship),nameof(Ship.StopManeuver)),prefix:new HarmonyMethod(typeof(Program),nameof(RecoveryStop)));
            h.Patch(AccessTools.Method(typeof(ShipSitu),nameof(ShipSitu.UpdateTime)),prefix:new HarmonyMethod(typeof(Program),nameof(Quiet)));
            h.Patch(AccessTools.PropertyGetter(typeof(ShipMarket),nameof(ShipMarket.BlockadeDiscount)),prefix:new HarmonyMethod(typeof(Program),nameof(IdentityBlockade)));
            h.Patch(AccessTools.Method(typeof(ShipMarket),nameof(ShipMarket.RequestTraderUpdate)),prefix:new HarmonyMethod(typeof(Program),nameof(RecoverySync)));
            h.Patch(AccessTools.Method(typeof(ShipMarket),nameof(ShipMarket.BuildConfig)),prefix:new HarmonyMethod(typeof(Program),nameof(RecoveryBuildGuard)));
            h.Patch(AccessTools.Method(typeof(StarSystem),nameof(StarSystem.GetNearestBO)),prefix:new HarmonyMethod(typeof(Program),nameof(NoNearbyBody)));
            h.Patch(AccessTools.Method(typeof(AIShipManager),nameof(AIShipManager.IsOnCollisionCourse)),prefix:new HarmonyMethod(typeof(Program),nameof(False)));
            h.Patch(AccessTools.Method(typeof(AreThreatsNearby),nameof(AreThreatsNearby.RunCommand)),prefix:new HarmonyMethod(typeof(Program),nameof(NoFreightThreat)));
            RecoveryWorld();
            var source=recoveryMarkets["FREIGHT"];var saved=RecoverySave();double x=recoveryHull.objSS.vPosx;
            var preview=Freight("/economy/freight/FREIGHT/approach");
            Check(preview.status==200 && (bool)preview.body["eligible"] && !(bool)preview.body["applied"],"Live freight approach preview is not eligible");
            Check(saved==RecoverySave() && recoveryHull.objSS.vPosx==x && recoverySyncs==0,"Approach preview changed world/markets");
            Check(Freight("/economy/freight/FREIGHT/approach",true).status==400,"Approach accepted missing confirmation");
            Check(Freight("/economy/freight/FREIGHT/approach/HOME").status==409,"Approach redirected shipment to another station");
            recoveryAI.Blackboard.Set("TripFuelPrediction","stale&999999");recoveryHull.fAIPauseTimer=StarSystem.fEpoch+10000;
            cleanupCalls=0;
            var result=Freight("/economy/freight/FREIGHT/approach",true,"APPROACH");
            Check(result.status==200 && (bool)result.body["applied"] && DockCargoAndDespawn.IsWithinRangeSpeed(recoveryHull.objSS,recoveryStation.objSS),"Native FlyTo positioning did not reach normal docking gate");
            Check(recoveryHull.objSS.NavData==null && !recoveryAI.Blackboard.HasKey("TripFuelPrediction") && recoveryHull.fAIPauseTimer==0,"Old route/prediction/pause survived approach");
            Check(Math.Abs(recoveryHull.objSS.GetRangeTo(recoveryStation.objSS)*149597870.7-10)<1e-5,"Approach did not stage clear of station geometry at 10 km");
            var checkpoint=recoveryAI.GetBehaviorTreeSaveData();
            Check(checkpoint.Any(p=>p.strPath.EndsWith("/Main") && p.aCommandPayload[0]=="2") &&
                AccessTools.Field(typeof(AIShip),"_activeCommand").GetValue(recoveryAI) is DockCargoAndDespawn,"Native arrival checkpoint is absent from ordinary AI save data");
            var restoredPilot=new HaulerCargoPilot("HOME",recoveryHull,"fixture");
            restoredPilot.BehaviorTree.LoadSaveData("",checkpoint);
            Check(restoredPilot.BehaviorTree.GetSaveData("").Any(p=>p.strPath.EndsWith("/Main") && p.aCommandPayload[0]=="2"),"Native role cannot restore the pending arrival without the mod");
            Check(Freight("/economy/freight/FREIGHT/approach",true,"APPROACH").status==409,"Repeated approach overwrote a pending native arrival");
            Check(ReferenceEquals(recoveryMarkets["FREIGHT"],source) && cleanupCalls==0 && recoveryMarkets["MTRS"].GetStockCountForCategory("Electronics")==0,"Approach bypassed native AI and delivered/retired immediately");
            Check(recoveryHull.fShallowRCSRemass==0 && recoveryHull.fShallowFusionRemain==800 && recoveryCrew.bAlive && recoveryCrew.ship==recoveryHull,"Approach changed fuel or crew");
            // The real native role tree, FlyTo and DockCargoAndDespawn execute.
            // CrewSim's Unity/population handoff is the existing fixture boundary.
            recoveryAI.RunAI();
            Check(!recoveryMarkets.ContainsKey("FREIGHT") && recoveryMarkets["MTRS"].GetStockCountForCategory("Electronics")==633 && cleanupCalls==1,"Subsequent native AI ticks did not deliver cargo exactly once");

            foreach(string kind in new[]{"docked","materialized","player-owned","crewless","unconscious","combat","duplicate","missing-controller","queued"}) {
                RecoveryWorld();
                switch(kind) {
                    case "docked":Connect(recoveryHull,recoveryStation);break;
                    case "materialized":Set(recoveryHull,"nLoadState",Ship.Loaded.Full);break;
                    case "player-owned":Set(CrewSim.system,"dictShipOwners",new Dictionary<string,string>{{"FREIGHT","PLAYER"}});break;
                    case "crewless":((List<PersonSpec>)AccessTools.Field(typeof(Ship),"aPeople").GetValue(recoveryHull)).Clear();break;
                    case "unconscious":conditions[recoveryCrew]=new HashSet<string>{"Unconscious"};break;
                    case "combat":recoveryHull.shipCombatTarget=recoveryStation;break;
                    case "duplicate":AIShipManager.dictAIs["INTERREGIONAL"].Add(Controller(recoveryHull,new HaulerCargoPilot("HOME",recoveryHull,"fixture")));break;
                    case "missing-controller":AIShipManager.dictAIs["INTERREGIONAL"].Clear();break;
                    case "queued":recoveryAI.AddCommand(new Hold(recoveryAI.AICharacter));break;
                }
                saved=RecoverySave();x=recoveryHull.objSS.vPosx;
                Check(Freight("/economy/freight/FREIGHT/approach",true,"APPROACH").status==409 && saved==RecoverySave() && recoveryHull.objSS.vPosx==x,"Unsafe approach was not rejected without changes: "+kind);
            }

            RecoveryWorld(true);saved=RecoverySave();
            Check(Freight("/economy/freight/FREIGHT/recover").status==400,"Orphan recovery guessed a lost destination");
            Check(Freight("/economy/freight/FREIGHT/recover/MTRS",true).status==400,"Cargo credit accepted missing confirmation");
            preview=Freight("/economy/freight/FREIGHT/recover/MTRS");
            Check(preview.status==200 && (bool)preview.body["eligible"] && saved==RecoverySave() && recoverySyncs==0,"Recovery preview mutated markets");
            var destination=recoveryMarkets["MTRS"];var config=destination.GetConfigs()[0];var list=config.Stock["Electronics"];
            var old=recoveryAI;AIShipManager.dictAIs["INTERREGIONAL"].Add(old);
            var queue=(AIShipQueue)AccessTools.Field(typeof(AIShipManager),"_aIQueue").GetValue(null);queue.Enqueue(old);queue.PrioritizeShip(old);
            result=Freight("/economy/freight/FREIGHT/recover/MTRS",true,"RECOVER");
            Check(result.status==200 && (bool)result.body["credited"] && !recoveryMarkets.ContainsKey("FREIGHT"),"Lost cargo was not credited/removed");
            Check(destination.GetStockCountForCategory("Electronics")==633 && destination.GetConfigs().Sum(c=>c.Stock["Electronics"].Count)==633,"Recovered payload quantity not conserved");
            var names=destination.GetConfigs().SelectMany(c=>c.Stock["Electronics"]).Select(i=>i.COName).ToArray();
            Check(names.Distinct().Count()==633 && Enumerable.Range(0,633).All(i=>names.Contains("item"+i)),"Multiple destination stores duplicated prefixes or changed item types");
            Check(ReferenceEquals(destination,recoveryMarkets["MTRS"]) && ReferenceEquals(config,destination.GetConfigs()[0]) && ReferenceEquals(list,config.Stock["Electronics"]),"Recovery replaced market/config/list objects held by the station UI");
            Check(AIShipManager.dictAIs.Values.All(r=>r.Count==0) && queue.Count()==0 && !queue.Dequeue().Any(),"Stale controller survived regional or scheduler cleanup");
            var hubs=(Dictionary<string,List<string>>)AccessTools.Field(typeof(MarketManager),"_regionalMarketMap").GetValue(recoveryMarket);
            Check(!hubs.ContainsKey("FREIGHT") && !hubs["MTRS"].Contains("FREIGHT") && CrewSim.system.GetShipOwner("FREIGHT")=="UNREGISTERED","Stale hub/ownership entries survived recovery");
            Check(recoverySyncs==1 && recoveryCrew.bAlive && !recoveryHull.bDestroyed,"Recovery failed to synchronize or deleted unrelated hull/crew objects");
            Check(Freight("/economy/freight/FREIGHT/recover/MTRS",true,"RECOVER").status==404 && destination.GetStockCountForCategory("Electronics")==633,"Repeated recovery credited cargo twice");
            var reload=new ShipMarket(destination.GetJson());
            Check(reload.GetStockCountForCategory("Electronics")==633 && reload.GetConfigs().SelectMany(c=>c.Stock["Electronics"]).Select(i=>i.COName).Distinct().Count()==633,"Credited payload does not survive native save/reload");

            foreach(string kind in new[]{"hull-present","full","wrong-type","mismatched-cache","non-cargo","unknown-item","shared-source","shared-list","missing-destination","stage-failure"}) {
                RecoveryWorld(true);
                switch(kind) {
                    case "hull-present":CrewSim.system.dictShips["FREIGHT"]=recoveryHull;break;
                    case "full":recoveryMarkets["MTRS"].GetConfigs()[0].Stock["Electronics"]=Enumerable.Range(0,300).Select(_=>new MarketItem{COName="item0",COCollectionName="Electronics"}).ToList();recoveryMarkets["MTRS"].BuildConfig();break;
                    case "wrong-type":recoveryMarkets["FREIGHT"].GetConfigs()[0].Stock["Electronics"][0].COCollectionName="Wrong";break;
                    case "mismatched-cache":recoveryMarkets["FREIGHT"].GetConfigs()[0].Stock["Electronics"].RemoveAt(0);break;
                    case "non-cargo":Back(recoveryMarkets["FREIGHT"].GetConfigs()[0],"CargoSpecs",null);break;
                    case "unknown-item":recoveryMarkets["FREIGHT"].GetConfigs()[0].Stock["Electronics"][0].COName="Unknown";break;
                    case "shared-source":var other=new ShipMarket("OTHER");other.AddMarketConfig("pod",recoveryMarkets["FREIGHT"].GetConfigs()[0]);other.BuildConfig();recoveryMarkets.Add("OTHER",other);break;
                    case "shared-list":recoveryMarkets["MTRS"].GetConfigs()[0].Stock["Electronics"]=recoveryMarkets["FREIGHT"].GetConfigs()[0].Stock["Electronics"];break;
                    case "missing-destination":recoveryMarkets.Remove("MTRS");break;
                    case "stage-failure":recoveryStageFailure=true;break;
                }
                saved=RecoverySave();result=Freight("/economy/freight/FREIGHT/recover/MTRS",true,"RECOVER");
                Check(result.status>=400 && saved==RecoverySave() && recoveryMarkets.ContainsKey("FREIGHT"),"Unsafe/failed recovery mutated inventory: "+kind);
            }
            RecoveryWorld(true);recoverySyncFailure=true;
            result=Freight("/economy/freight/FREIGHT/recover/MTRS",true,"RECOVER");
            Check(result.status==200 && (bool)result.body["credited"] && result.body["traderSynchronizationError"]!=null && !recoveryMarkets.ContainsKey("FREIGHT"),"Post-credit trader sync failure made the transfer retryable or lost cargo");
            RecoveryWorld(true,0);result=Freight("/economy/freight/FREIGHT/recover/MTRS",true,"RECOVER");
            Check(result.status==200 && !recoveryMarkets.ContainsKey("FREIGHT") && recoveryMarkets["MTRS"].GetStockCountForCategory("Electronics")==0,"Empty orphan cleanup fabricated stock");
            Console.WriteLine(checks+" native freight approach/recovery checks passed.");
        } finally { h.UnpatchSelf(); }
    }
}
