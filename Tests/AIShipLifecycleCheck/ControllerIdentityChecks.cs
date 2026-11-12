using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using Ostranauts.Trading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

static partial class Program {
    static int identityFactories,identityEnqueues,identityDestructions;
    static MarketManager identityMarket;
    static bool IdentityManager(ref MarketManager __result) {__result=identityMarket;return false;}
    static bool IdentityQueue() {identityEnqueues++;return false;}
    static bool IdentityMass(ref double __result) {__result=1000;return false;}
    static bool IdentityReady(ref bool __result) {__result=true;return false;}
    static bool IdentityDeltaV(ref double __result) {__result=100;return false;}
    static bool IdentityAttack(ref JsonAttackMode __result) {__result=null;return false;}
    static bool IdentityString(string __0,ref string __result) {__result=__0;return false;}
    static bool IdentityBlockade(ref float __result) {__result=0;return false;}
    static bool IdentityFactory(AIShip __instance, AIType at, Ship ship) {
        identityFactories++;
        BaseCharacter pilot;
        switch(at) {
            case AIType.HaulerCargo: pilot=Empty<HaulerCargoPilot>();break;
            case AIType.Scav: pilot=Empty<ScavPilot>();break;
            case AIType.Police: pilot=Empty<PolicePilot>();break;
            case AIType.Navy: pilot=Empty<NavyPilot>();break;
            case AIType.Pirate: pilot=Empty<PiratePilot>();break;
            case AIType.Civilian: pilot=Empty<CivilianPilot>();break;
            case AIType.HaulerDeployer: pilot=Empty<HaulerDeployingPilot>();break;
            case AIType.HaulerRetriever: pilot=Empty<HaulerRetrievingPilot>();break;
            case AIType.Auto: pilot=Empty<AutoPilot>();break;
            default: throw new Exception("Unhandled fixture role: "+at);
        }
        Back(pilot,"ShipUs",ship);pilot.Blackboard=new Blackboard();pilot.PriorityQueue=new QueuedCommandsNode("PriorityQueue",pilot);
        Set(__instance,"_shipUs",ship);Set(__instance,"_aiCharacter",pilot);Back(__instance,"Blackboard",pilot.Blackboard);
        var command=new Hold(pilot);Set(__instance,"_activeCommand",command);Set(__instance,"_behaviorTree",new SelectorCommand("FixtureTree",command));
        return false;
    }
    static bool IdentityDestroy(Ship __instance) {
        identityDestructions++;CrewSim.system.dictShips.Remove(__instance.strRegID);__instance.bDestroyed=true;return false;
    }
    static Ship IdentityWorld() {
        CrewSim.system=Empty<StarSystem>();CrewSim.system.dictShips=new Dictionary<string,Ship>();
        Set(CrewSim.system,"dictShipOwners",new Dictionary<string,string>());
        AIShipManager.dictAIs=new Dictionary<string,List<AIShip>>();AIShipManager.strATCLast="VNCA";
        CrewSim.coPlayer=Empty<CondOwner>();Set(CrewSim.coPlayer,"_strID","PLAYER");
        var station=MakeShip("VNCA");station.objSS.bIsBO=true;CrewSim.system.dictShips.Add(station.strRegID,station);
        var ship=MakeShip("O-FREIGHT");ship.IsAIShip=true;ship.origin="MHNG";
        ship.fShallowRCSRemass=0.000000002;ship.fShallowFusionRemain=100;
        CrewSim.system.dictShips.Add(ship.strRegID,ship);
        ((Dictionary<string,string>)AccessTools.Field(typeof(StarSystem),"dictShipOwners").GetValue(CrewSim.system))[ship.strRegID]="MHNGCargoHauler";
        identityFactories=identityEnqueues=identityDestructions=0;
        return ship;
    }
    static AIShip IdentityCargo(Ship ship) {
        var cargo=new AIShip("MHNG",AIType.HaulerCargo,ship);AIShipManager.AddAIToDict(cargo,AIShipManager.INTERREGIONAL_ATC);
        cargo.Blackboard.Set("FreightFixture","retain mission state");return cargo;
    }
    static int IdentityCount(Ship ship) => AIShipManager.dictAIs.Values.Sum(region=>region.Count(ai=>ReferenceEquals(AccessTools.Field(typeof(AIShip),"_shipUs").GetValue(ai),ship)));
    static MethodInfo IdentityRepair => AccessTools.Method(typeof(Ostrabus.OstrabusPlugin).Assembly.GetType("Ostrabus.MiscBugFixes.AIControllerIdentity",true),"RepairSavedCargoDuplicates");
    static int RepairIdentity(JsonAIShipManagerSave save) => (int)IdentityRepair.Invoke(null,new object[]{save});
    static JsonAIShipSave IdentityRecord(AIType role,string region,string home,string id="O-FREIGHT") => new JsonAIShipSave {
        enumAIType=role,strATCLast=region,strHomeStation=home,strRegId=id,strActiveCommand="Hold",
        strActiveCommandPayload=new[]{"sentinel"},aTreeSave=Array.Empty<JsonCommandPayload>(),ablackboard=Array.Empty<string>()
    };
    static void CheckControllerIdentity(string capturedSave=null) {
        int before=checks;
        var environment=new Harmony("offline.controller.identity.environment");var production=new Harmony("offline.controller.identity.production");
        try {
            environment.Patch(AccessTools.Method(typeof(AIShip),"SetupAI"),prefix:new HarmonyMethod(typeof(Program),nameof(IdentityFactory)));
            var queueField=AccessTools.Field(typeof(AIShipManager),"_aIQueue");
            queueField.SetValue(null,FormatterServices.GetUninitializedObject(queueField.FieldType));
            environment.Patch(AccessTools.Method(queueField.FieldType,"Enqueue"),prefix:new HarmonyMethod(typeof(Program),nameof(IdentityQueue)));
            environment.Patch(AccessTools.Method(typeof(Ship),nameof(Ship.ToggleVis)),prefix:new HarmonyMethod(typeof(Program),nameof(Quiet)));
            environment.Patch(AccessTools.Method(typeof(Ship),nameof(Ship.Destroy)),prefix:new HarmonyMethod(typeof(Program),nameof(IdentityDestroy)));
            environment.Patch(AccessTools.Method(typeof(AIShipManager),"SetATCValues"),prefix:new HarmonyMethod(typeof(Program),nameof(Quiet)));
            var ferry=typeof(AIShip).Assembly.GetTypes().Single(t=>t.Name=="Ferry");
            environment.Patch(AccessTools.Method(ferry,"ApplySaveData"),prefix:new HarmonyMethod(typeof(Program),nameof(Quiet)));
            identityMarket=Empty<MarketManager>();
            var markets=new Dictionary<string,ShipMarket>();Set(identityMarket,"_market",markets);
            environment.Patch(AccessTools.PropertyGetter(typeof(MarketManager).BaseType,"Instance"),prefix:new HarmonyMethod(typeof(Program),nameof(IdentityManager)));

            // This is the exact unsaved registration call made by both station
            // collision branches, under the actual native regional lookup.
            var ship=IdentityWorld();var cargo=IdentityCargo(ship);
            markets[ship.strRegID]=new ShipMarket(ship.strRegID);
            var pod=Empty<MarketActorConfig>();pod.Stock=new Dictionary<string,List<MarketItem>>{{"AnyElectronics",Enumerable.Range(0,633).Select(_=>new MarketItem{COCollectionName="AnyElectronics",COName="fixture"}).ToList()}};
            markets[ship.strRegID].AddMarketConfig("cargo-pod",pod);
            environment.Patch(AccessTools.Method(typeof(Ship),nameof(Ship.UnlockFromOrbit)),prefix:new HarmonyMethod(typeof(Program),nameof(Quiet)));
            environment.Patch(AccessTools.Method(typeof(Ship),nameof(Ship.IsPlayerShip)),prefix:new HarmonyMethod(typeof(Program),nameof(False)));
            environment.Patch(AccessTools.PropertyGetter(typeof(Ship),nameof(Ship.Mass)),prefix:new HarmonyMethod(typeof(Program),nameof(IdentityMass)));
            environment.Patch(AccessTools.PropertyGetter(typeof(Ship),nameof(Ship.NavAIManned)),prefix:new HarmonyMethod(typeof(Program),nameof(IdentityReady)));
            environment.Patch(AccessTools.PropertyGetter(typeof(Ship),nameof(Ship.DeltaVRemainingRCS)),prefix:new HarmonyMethod(typeof(Program),nameof(IdentityDeltaV)));
            environment.Patch(AccessTools.PropertyGetter(typeof(Ship),nameof(Ship.IsLocalAuthority)),prefix:new HarmonyMethod(typeof(Program),nameof(False)));
            environment.Patch(AccessTools.Method(typeof(DataHandler),nameof(DataHandler.GetAttackMode)),prefix:new HarmonyMethod(typeof(Program),nameof(IdentityAttack)));
            environment.Patch(AccessTools.Method(typeof(DataHandler),nameof(DataHandler.GetString),new[]{typeof(string),typeof(bool)}),prefix:new HarmonyMethod(typeof(Program),nameof(IdentityString)));
            environment.Patch(AccessTools.Method(typeof(CollisionManager),"TriggerCollisionEvent"),prefix:new HarmonyMethod(typeof(Program),nameof(Quiet)));
            var collision=AccessTools.Method(typeof(CollisionManager),"ProcessCollision",new[]{typeof(Ship),typeof(Ship),typeof(double)});
            ship.objSS.vPosx=1e-8;
            collision.Invoke(null,new object[]{ship,CrewSim.system.dictShips["VNCA"],1e-10});
            var scav=AIShipManager.dictAIs["VNCA"].Single();
            Check(!ReferenceEquals(scav,cargo)&&scav.HomeStation=="VNCA"&&IdentityCount(ship)==2,"Native station-collision registration did not reproduce duplicate cargo/local controllers");
            var brokenSave=AIShipManager.GetJSONSave();
            Check(brokenSave.aAIShips.Count(r=>r.strRegId==ship.strRegID)==2,"Native save did not persist conflicting controller records");
            AccessTools.Method(typeof(AIShipManager),"AIShipCleanup").Invoke(null,new object[]{scav,null,CrewSim.system.dictShips["VNCA"]});
            Check(identityDestructions==1&&!CrewSim.system.dictShips.ContainsKey(ship.strRegID),"Native regional cleanup did not retire the globally active cargo hull");
            Check(MarketManager.GetShipMarket(ship.strRegID)?.GetConfigs().Sum(c=>c.GetFlatStockList().Count)==633,"Native regional cleanup did not reproduce surviving inaccessible cargo");

            Install(production,"AIControllerReusePatch");Install(production,"CargoControllerSavePatch");
            foreach(bool reverse in new[]{false,true}) {
                ship=IdentityWorld();cargo=IdentityCargo(ship);ship.objSS.vPosx=1e-8;var station=CrewSim.system.dictShips["VNCA"];
                collision.Invoke(null,reverse?new object[]{station,ship,1e-10}:new object[]{ship,station,1e-10});
                Check(IdentityCount(ship)==1&&ReferenceEquals(AIShipManager.GetAIShipByRegID(ship.strRegID),cargo)&&!AIShipManager.dictAIs.ContainsKey("VNCA"),"Native station collision still enrolled cargo for regional destruction");
            }
            foreach(var role in new[]{AIType.HaulerCargo,AIType.Navy,AIType.Pirate,AIType.Scav,AIType.Police,AIType.Civilian,AIType.HaulerDeployer,AIType.HaulerRetriever,AIType.Auto}) {
                ship=IdentityWorld();var existing=new AIShip("MHNG",role,ship);AIShipManager.AddAIToDict(existing,"INTERREGIONAL");
                var state=existing.Blackboard;int queued=identityEnqueues;
                foreach(var requested in new[]{AIType.Scav,AIType.Police})foreach(string region in new[]{null,"VNCA","MHNG","INTERREGIONAL"}) {
                    var reused=AIShipManager.AddAIToShip(ship,requested,region);
                    Check(ReferenceEquals(reused,existing)&&IdentityCount(ship)==1,"Unsaved cross-region call created/replaced "+role+" controller");
                    Check(identityFactories==1&&identityEnqueues==queued&&existing.HomeStation=="MHNG"&&ReferenceEquals(state,existing.Blackboard),"Reuse reset or requeued an existing mission");
                }
                Check(AIShipManager.AddAIToShip(ship,AIType.NA)==null,"NA request returned an existing controller");
            }
            ship=IdentityWorld();cargo=IdentityCargo(ship);
            Check(AIShipManager.AddAIToShip(null,AIType.Scav)==null,"Null hull was accepted");
            var replacement=MakeShip(ship.strRegID);
            Check(!ReferenceEquals(AIShipManager.AddAIToShip(replacement,AIType.Scav),cargo),"Replacement hull instance inherited a different object's controller");
            var fresh=MakeShip("FRESH");Check(AIShipManager.AddAIToShip(fresh,AIType.Scav).AIType==AIType.Scav,"Native unregistered-hull fallback stopped creating its requested role");
            int factories=identityFactories;
            var explicitAuto=AIShipManager.AddAIToShip(ship,AIType.Auto,"INTERREGIONAL",IdentityRecord(AIType.Auto,"INTERREGIONAL","MHNG"),gameLoad:true);
            Check(explicitAuto.AIType==AIType.Auto&&!ReferenceEquals(explicitAuto,cargo)&&identityFactories==factories+1,"Explicit saved/player-autopilot construction was intercepted");

            CheckSavedControllerIdentity(brokenSave);
            CheckIdentityDelivery(markets,environment);
            if (capturedSave!=null) CheckCapturedIdentity(capturedSave);
            Console.WriteLine((checks-before)+" controller identity/native registration/save/cleanup/orphaned-cargo checks passed.");
        } finally {production.UnpatchSelf();environment.UnpatchSelf();identityMarket=null;}
    }
    static void CheckCapturedIdentity(string path) {
        var root=JObject.Parse(System.IO.File.ReadAllText(path));
        var records=root["pilots"].ToObject<JsonAIShipSave[]>();
        IdentityWorld();CrewSim.system.dictShips.Clear();
        var owners=new Dictionary<string,string>();Set(CrewSim.system,"dictShipOwners",owners);
        foreach(var record in records.Where(r=>r.enumAIType==AIType.HaulerCargo)) {
            var hull=MakeShip(record.strRegId);hull.IsAIShip=true;hull.origin=record.strHomeStation;
            CrewSim.system.dictShips[hull.strRegID]=hull;owners[hull.strRegID]=record.strHomeStation+"CargoHauler";
        }
        var canonical=records.Single(r=>r.strRegId=="O-LVKJ"&&r.enumAIType==AIType.HaulerCargo);
        string original=JsonConvert.SerializeObject(canonical);
        var save=new JsonAIShipManagerSave{aAIShips=records};
        Check(records.Count(r=>r.strRegId=="O-LVKJ")==2,"Captured save no longer contains the diagnosed duplicate");
        Check(RepairIdentity(save)==1&&save.aAIShips.Count(r=>r.strRegId=="O-LVKJ")==1,"Actual captured controller records were not repaired");
        Check(ReferenceEquals(save.aAIShips.Single(r=>r.strRegId=="O-LVKJ"),canonical)&&JsonConvert.SerializeObject(canonical)==original,"Captured native cargo mission payload was altered");
        Check(save.aAIShips.Length==records.Length-1&&save.aAIShips.All(r=>r.enumAIType==AIType.HaulerCargo),"Captured repair removed unrelated freight");
        Console.WriteLine("Captured save replay: "+root["save"]+"; removed the competing O-LVKJ/VNCA scavenger, retained original INTERREGIONAL/MHNG cargo state unchanged.");
    }
    static void CheckIdentityDelivery(Dictionary<string,ShipMarket> markets,Harmony environment) {
        var ship=IdentityWorld();var cargo=IdentityCargo(ship);ship.fShallowRCSRemass=0;
        var destination=MakeShip("MTRS");destination.objSS.bIsBO=true;destination.Classification=Ship.TypeClassification.OrbitalStation;
        ((List<string>)AccessTools.Field(typeof(Ship),"aDockingPorts").GetValue(destination)).Add("destination-port");
        CrewSim.system.dictShips.Add(destination.strRegID,destination);Bind(ship,destination);
        markets.Clear();
        var sourceMarket=new ShipMarket(ship.strRegID);var destinationMarket=new ShipMarket(destination.strRegID);
        var collection=new DataCoCollection(new JsonDCOCollection{strName="AnyElectronics"});
        Set(collection,"_avgPrice",10d);Set(collection,"_avgMass",1d);
        DataHandler.dictDataCoCollections=new Dictionary<string,DataCoCollection>{{collection.Name,collection}};
        foreach(var pair in new[]{new KeyValuePair<ShipMarket,int>(sourceMarket,633),new KeyValuePair<ShipMarket,int>(destinationMarket,0)}) {
            var config=Empty<MarketActorConfig>();config.COwnerId="pod";Set(config,"_productionMaps",new List<Producer>());
            config.MaxVirtualInventorySize=new Dictionary<string,int>{{collection.Name,1000}};
            config.Stock=new Dictionary<string,List<MarketItem>>{{collection.Name,Enumerable.Range(0,pair.Value).Select(_=>new MarketItem{COCollectionName=collection.Name,COName="fixture"}).ToList()}};
            pair.Key.AddMarketConfig("pod",config);pair.Key.BuildConfig();markets.Add(pair.Key.ShipRegId,pair.Key);
        }
        environment.Patch(AccessTools.PropertyGetter(typeof(ShipMarket),nameof(ShipMarket.BlockadeDiscount)),prefix:new HarmonyMethod(typeof(Program),nameof(IdentityBlockade)));
        var command=new DockCargoAndDespawn(cargo.AICharacter);cleanupCalls=0;
        var result=command.RunCommand(cargo.Blackboard);
        Check(result.Status==CommandCode.Finished&&cleanupCalls==1,"Surviving cargo controller did not complete native station arrival: status="+result.Status+", station="+destination.IsStation()+", range="+DockCargoAndDespawn.IsWithinRangeSpeed(ship.objSS,destination.objSS)+", cleanup="+cleanupCalls);
        Check(MarketManager.GetShipMarket(ship.strRegID)==null&&destinationMarket.GetConfigs().Sum(c=>c.GetFlatStockList().Count)==633,"Native arrival did not conserve and deliver the original 633 cargo units");
        Check(ship.fShallowRCSRemass==0,"Controller/arrival fix silently refueled the ship");
        Check(command.RunCommand(cargo.Blackboard).Status==CommandCode.Failure&&destinationMarket.GetConfigs().Sum(c=>c.GetFlatStockList().Count)==633,"Repeated completion duplicated the shipment");
    }
    static void CheckSavedControllerIdentity(JsonAIShipManagerSave brokenSave) {
        var ship=IdentityWorld();
        var cargo=brokenSave.aAIShips.Single(r=>r.enumAIType==AIType.HaulerCargo);
        var fallback=brokenSave.aAIShips.Single(r=>r.enumAIType==AIType.Scav);
        var payload=cargo.strActiveCommandPayload;var tree=cargo.aTreeSave;var board=cargo.ablackboard;
        var original=brokenSave.aAIShips;
        AccessTools.Method(typeof(AIShipManager),"ApplySaveData").Invoke(null,new object[]{brokenSave});
        Check(IdentityCount(ship)==1&&AIShipManager.GetAIShipByRegID(ship.strRegID).AIType==AIType.HaulerCargo,"Actual native save restore retained the duplicate local controller");
        Check(brokenSave.aAIShips.Length==1&&ReferenceEquals(brokenSave.aAIShips[0],cargo)&&original.Length==2,"Load repair did not retain the exact authoritative record");
        Check(ReferenceEquals(payload,cargo.strActiveCommandPayload)&&ReferenceEquals(tree,cargo.aTreeSave)&&ReferenceEquals(board,cargo.ablackboard)&&cargo.strHomeStation=="MHNG","Load repair modified mission payload, tree, blackboard or home");
        Check(ship.fShallowRCSRemass==0.000000002&&ship.fShallowFusionRemain==100,"Load repair changed fuel");
        Check(RepairIdentity(brokenSave)==0,"Load repair was not idempotent");
        Check(!AIShipManager.dictAIs.ContainsKey("VNCA"),"Repaired shipment remains enrolled for regional destruction");
        foreach(bool reverse in new[]{false,true})foreach(var role in new[]{AIType.Scav,AIType.Police}) {
            var bad=IdentityRecord(role,"VNCA","VNCA");
            var save=new JsonAIShipManagerSave{aAIShips=reverse?new[]{bad,cargo}:new[]{cargo,bad}};
            Check(RepairIdentity(save)==1&&save.aAIShips.Length==1&&ReferenceEquals(save.aAIShips[0],cargo),"Repair depends on record order or missed a collision fallback role");
        }
        foreach(string kind in new[]{"missing-hull","wrong-owner","changed-origin","multiple-cargo","explicit-auto","different-role","different-home","ordinary-records","missing-region"}) {
            ship=IdentityWorld();var wanted=IdentityRecord(AIType.HaulerCargo,"INTERREGIONAL","MHNG");var bad=IdentityRecord(AIType.Scav,"VNCA","VNCA");
            var records=new List<JsonAIShipSave>{wanted,bad};
            switch(kind) {
                case "missing-hull": CrewSim.system.dictShips.Remove(ship.strRegID);break;
                case "wrong-owner": Set(CrewSim.system,"dictShipOwners",new Dictionary<string,string>{{ship.strRegID,"PLAYER"}});break;
                case "changed-origin": ship.origin="elsewhere";break;
                case "multiple-cargo": records.Add(IdentityRecord(AIType.HaulerCargo,"INTERREGIONAL","MHNG"));break;
                case "explicit-auto": records.Add(IdentityRecord(AIType.Auto,"INTERREGIONAL","MHNG"));break;
                case "different-role": bad.enumAIType=AIType.Navy;break;
                case "different-home": bad.strHomeStation="elsewhere";break;
                case "ordinary-records": wanted.enumAIType=AIType.Scav;break;
                case "missing-region": bad.strATCLast=null;break;
            }
            var save=new JsonAIShipManagerSave{aAIShips=records.ToArray()};var same=save.aAIShips;
            Check(RepairIdentity(save)==0&&ReferenceEquals(same,save.aAIShips),"Ambiguous/non-cargo save was altered: "+kind);
        }
        Check(RepairIdentity(null)==0&&RepairIdentity(new JsonAIShipManagerSave())==0,"Missing saved AI state was rejected");
    }
}
