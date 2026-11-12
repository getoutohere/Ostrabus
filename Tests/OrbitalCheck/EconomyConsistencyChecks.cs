using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ostrabus.Inspection;
using Ostranauts.Core;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using Ostranauts.Trading;

static partial class EconomyChecks {
    static JObject ControllerData(ControllerInspection index, string id, Ship ship) => JObject.Parse(SnapshotJson.Encode(index.Snapshot(id,ship)));
    static JObject ControllerPage(ControllerInspection index, int offset=0, int limit=50, string id=null) {
        var reply=index.Page(offset,limit,id);
        if (reply.Status != 200) throw new Exception("Controller page failed: "+SnapshotJson.Encode(reply.Body));
        return JObject.Parse(SnapshotJson.Encode(reply.Body));
    }
    static void ConsistencyChecks(Action<bool,string> check) {
        Setup();
        AccessTools.Field(typeof(MonoSingleton<MarketManager>),"_instance").SetValue(null,manager);
        hulls["SOURCE"].objSS.bIsBO=hulls["DEST"].objSS.bIsBO=true;
        Hull("LOCAL").objSS.bIsBO=true;Market("LOCAL",null,0);
        var cargo=new AIShip("SOURCE",AIType.HaulerCargo,"fixture");
        var ship=cargo.Ship;
        ship.shipScanTarget=hulls["DEST"];ship.shipSituTarget=hulls["DEST"].objSS;
        AIShipManager.AddAIToDict(cargo,"INTERREGIONAL");
        MarketManager.RegisterAICargoHauler(ship,new List<Ostranauts.Core.Models.TradeRouteDTO> {
            new Ostranauts.Core.Models.TradeRouteDTO { OriginStation="SOURCE",DestinationStation="DEST",CoCollection=DataHandler.dictDataCoCollections["Metal"],Amount=20 }
        });
        var local=Empty<AIShip>();var scav=Empty<ScavPilot>();
        Field(scav,"<ShipUs>k__BackingField",ship);Field(scav,"<PriorityQueue>k__BackingField",new QueuedCommandsNode("queue",scav));
        Field(local,"_shipUs",ship);Field(local,"_aiCharacter",scav);Field(local,"<HomeStation>k__BackingField","LOCAL");
        AIShipManager.dictAIs.Add("LOCAL",new List<AIShip> { local });
        var stale=Empty<AIShip>();var replaced=Empty<Ship>();replaced.strRegID=ship.strRegID;
        Field(stale,"_shipUs",replaced);Field(stale,"_aiCharacter",scav);Field(stale,"<HomeStation>k__BackingField","OLD");
        AIShipManager.dictAIs.Add("STALE",new List<AIShip> { stale });
        AIShipManager.dictAIs.Add("BROKEN",new List<AIShip> { null,Empty<AIShip>() });
        string saveBefore=JsonConvert.SerializeObject(MarketManager.GetJSONSave());
        var orderBefore=AIShipManager.dictAIs.SelectMany(r=>r.Value).ToArray();
        var guard=new Harmony("offline.economy.consistency.readonly");
        try {
            foreach (var method in new[] {
                AccessTools.PropertyGetter(typeof(AIShip),nameof(AIShip.Ship)),
                AccessTools.Method(typeof(ShipMarket),nameof(ShipMarket.BuildConfig)),
                AccessTools.Method(typeof(MarketActorConfig),nameof(MarketActorConfig.GetStockTotals)),
                AccessTools.Method(typeof(MarketActorConfig),nameof(MarketActorConfig.GetFlatStockList)),
                AccessTools.Method(typeof(MarketManager),nameof(MarketManager.Run)),
                AccessTools.Method(typeof(ShipMarket),nameof(ShipMarket.RequestTraderUpdate))
            }) guard.Patch(method,prefix:new HarmonyMethod(typeof(EconomyChecks),nameof(Forbidden)) { priority=Priority.First });
            var index=ControllerInspection.Capture(long.MaxValue);
            var data=ControllerData(index,ship.strRegID,ship);
            check(index.Complete && index.Scanned == 5,"Complete request-scoped controller census includes broken entries");
            check((int)data["registrationCount"] == 3 && (int)data["liveHullControllerCount"] == 2 && (int)data["staleHullReferences"] == 1,
                "Distinct same-ID stale hull is separated from two controllers for current hull");
            check((bool)data["multipleControllers"] && (bool)data["duplicateRegistrations"] && index.Single(ship.strRegID,ship) == null,
                "Conflicting controllers stay visible; no winner is invented");
            check(data["registrations"].Select(r=>(string)r["role"]).SequenceEqual(new[]{"HaulerCargo","Scav","Scav"}),"All roles retained in registry order");
            check((bool)data["registrations"][0]["hullMatchesRegistry"] && !(bool)data["registrations"][2]["hullMatchesRegistry"],"Raw versus current hull identity reported");
            check(index.HomeMatches(ship.strRegID,"SOURCE") && index.HomeMatches(ship.strRegID,"LOCAL") && !index.HomeMatches(ship.strRegID,"OLD"),
                "Station association considers every current controller and excludes stale hull");
            var page=ControllerPage(index,0,1,ship.strRegID);
            check(page["items"].Count()==1 && (int)page["nextOffset"]==1 && (bool)page["summary"]["multipleControllers"],"Direct controller detail pages preserve ambiguity summary");
            check(ControllerPage(index,2,1,ship.strRegID)["nextOffset"].Type==JTokenType.Null,"Controller pagination terminates");
            check(ControllerPage(index)["items"].Any(r=>(bool)r["nullController"]) && ControllerPage(index)["items"].Any(r=>(bool)r["nullHull"]),"Broken registrations remain inspectable");
            var detached=ControllerPage(index);
            Field(local,"<HomeStation>k__BackingField","CHANGED");
            check((string)detached["items"][1]["home"]=="LOCAL","Controller responses contain detached values");
            Field(local,"<HomeStation>k__BackingField","LOCAL");
            var freight=Inspect("/economy/freight/"+ship.strRegID,began:long.MaxValue)["flight"];
            check(freight["pilotType"].Type==JTokenType.Null && freight["home"].Type==JTokenType.Null && freight["cargoPilot"].Type==JTokenType.Null &&
                (bool)freight["controllers"]["multipleControllers"],"Freight stops concealing duplicates behind one singular pilot");
            check(Inspect("/economy/stations/SOURCE/freight",began:long.MaxValue)["items"].Count()==1 &&
                Inspect("/economy/stations/LOCAL/freight",began:long.MaxValue)["items"].Count()==1,"Both conflicting controller homes appear in station freight association");
            var audit=Inspect("/economy/markets/"+ship.strRegID+"/audit",began:long.MaxValue);
            check((bool)audit["stockComparison"]["matches"] && (long)audit["stockComparison"]["backingQuantityObserved"]==20,"Audit counts backing stock without flattening items");
            check(audit["observations"].Values<string>().Contains("duplicateLiveHullRegistrations") && audit["observations"].Values<string>().Contains("staleControllerHullReference"),
                "Audit joins duplicate and stale references to the same market");
            var actor=markets[ship.strRegID].GetConfigs()[0];
            var original=actor.Stock["Metal"];
            original.RemoveAt(0);
            audit=Inspect("/economy/freight/"+ship.strRegID+"/audit",began:long.MaxValue);
            check(!(bool)audit["stockComparison"]["matches"] && (long)audit["stockComparison"]["mismatches"][0]["cached"]==20 &&
                (long)audit["stockComparison"]["mismatches"][0]["backing"]==19,"Audit detects stale aggregate count and leaves it untouched");
            actor.Stock["Extra"]=new List<MarketItem> { new MarketItem { COName="extra",COCollectionName="Extra" } };
            var cache=(Dictionary<string,int>)AccessTools.Field(typeof(ShipMarket),"_stock").GetValue(markets[ship.strRegID]);cache["Ghost"]=4;
            audit=Inspect("/economy/markets/"+ship.strRegID+"/audit",began:long.MaxValue);
            check((int)audit["stockComparison"]["mismatchCount"]==3,"Both backing-only and cached-only categories are compared");
            cache.Remove("Ghost");actor.Stock.Remove("Extra");original.Add(new MarketItem { COName="metal",COCollectionName="Metal" });
            var budget=Inspect("/economy/markets/"+ship.strRegID+"/audit",began:0);
            check(!(bool)budget["stockComparison"]["complete"] && budget["stockComparison"]["matches"].Type==JTokenType.Null &&
                budget["stockComparison"]["mismatchCount"].Type==JTokenType.Null,"Incomplete audit never claims clean stock or definite mismatches");
            var partial=ControllerInspection.Capture(0);var unknown=ControllerData(partial,"UNKNOWN",null);
            check(!partial.Complete && unknown["registered"].Type==JTokenType.Null && unknown["multipleControllers"].Type==JTokenType.Null,
                "Incomplete controller census makes missing/unique claims unknown");
            check(partial.Page(20,5).Status==503,"Unreached controller offset returns retryable error");
            AIShipManager.dictAIs["LOCAL"].Clear();AIShipManager.dictAIs["STALE"].Clear();
            AIShipManager.dictAIs["BROKEN"].Clear();AIShipManager.dictAIs["LOCAL"].Add(cargo);
            index=ControllerInspection.Capture(long.MaxValue);data=ControllerData(index,ship.strRegID,ship);
            check((bool)data["duplicateRegistrations"] && !(bool)data["multipleControllers"] && ReferenceEquals(index.Single(ship.strRegID,ship),cargo),
                "Same controller registered twice is distinguished from competing controllers");
            AIShipManager.dictAIs["LOCAL"].Clear();AIShipManager.dictAIs["LOCAL"].Add(local);
            hulls.Remove(ship.strRegID);CrewSim.system.dictShips.Remove(ship.strRegID);
            audit=Inspect("/economy/markets/"+ship.strRegID+"/audit",began:long.MaxValue);
            check(!(bool)audit["shipResolved"] && (int)audit["controllers"]["staleHullReferences"]==2 &&
                audit["observations"].Values<string>().Contains("unresolvedMarketHull"),"Missing hull keeps market and all stale registrations available for diagnosis");
            check((int)Inspect("/economy/freight/"+ship.strRegID,began:long.MaxValue)["flight"]["controllers"]["staleHullReferences"]==2,
                "Freight detail retains stale controllers even without a resolved hull");
            check(markets.ContainsKey(ship.strRegID) && original.Count==20 && cache["Metal"]==20,"Missing hull is not grounds for deletion or cargo transfer");
            hulls[ship.strRegID]=ship;CrewSim.system.dictShips[ship.strRegID]=ship;
            AIShipManager.dictAIs["STALE"].Add(stale);AIShipManager.dictAIs["BROKEN"].Add(null);AIShipManager.dictAIs["BROKEN"].Add(orderBefore[4]);
            check(orderBefore.SequenceEqual(AIShipManager.dictAIs.SelectMany(r=>r.Value)),"Inspection preserves controller entries and order");
            // Native serialization itself flattens stock. Allow it only for
            // this before/after assertion, never during inspection requests.
            var flatten=AccessTools.Method(typeof(MarketActorConfig),nameof(MarketActorConfig.GetFlatStockList));
            guard.Unpatch(flatten,HarmonyPatchType.Prefix,guard.Id);
            check(saveBefore==JsonConvert.SerializeObject(MarketManager.GetJSONSave()),"Audit and freight queries leave serialized markets unchanged");
            guard.Patch(flatten,prefix:new HarmonyMethod(typeof(EconomyChecks),nameof(Forbidden)) { priority=Priority.First });
            var pages=Inspect("/economy/audit",0,1,long.MaxValue);
            check(pages["items"].Count()==1 && (int)pages["nextOffset"]==1 && (bool)pages["controllerScanComplete"],"Audit collection paginates markets");
            check(Inspect("/economy/audit",1,1,long.MaxValue)["items"].Count()==1,"Audit skips earlier markets without processing their manifests");
            var second=Config("second",null,100);
            second.Stock["Metal"]=Enumerable.Range(0,5).Select(_=>new MarketItem { COName="metal",COCollectionName="DifferentLabel" }).ToList();
            var configs=(Dictionary<string,MarketActorConfig>)AccessTools.Field(typeof(ShipMarket),"_marketMapping").GetValue(markets[ship.strRegID]);
            configs.Add("second",second);cache["Metal"]=25;
            audit=Inspect("/economy/markets/"+ship.strRegID+"/audit",began:long.MaxValue);
            check((bool)audit["stockComparison"]["matches"] && (long)audit["stockComparison"]["backingQuantityObserved"]==25,
                "Audit sums multiple actors by native stock keys, not item labels");
            configs.Remove("second");cache["Metal"]=20;
            actor.Stock.Clear();
            for (int i=0;i<100;i++) actor.Stock["Difference"+i]=new List<MarketItem> { new MarketItem() };
            audit=Inspect("/economy/markets/"+ship.strRegID+"/audit",began:long.MaxValue);
            check((bool)audit["stockComparison"]["complete"] && (int)audit["stockComparison"]["mismatchCount"]==101 &&
                audit["stockComparison"]["mismatches"].Count()==64 && (bool)audit["stockComparison"]["mismatchesTruncated"],
                "Detailed discrepancy output is bounded without concealing total mismatch count");
            actor.Stock.Clear();
            original.Clear();
            for (int i=0;i<270;i++) actor.Stock["Category"+i]=new List<MarketItem>();
            audit=Inspect("/economy/markets/"+ship.strRegID+"/audit",began:long.MaxValue);
            check(!(bool)audit["stockComparison"]["complete"] && (bool)audit["stockComparison"]["budgetOrLimitReached"],"Bounded category scan reports unknown instead of silently omitting data");
            actor.Stock.Clear();actor.Stock["Metal"]=null;
            audit=Inspect("/economy/markets/"+ship.strRegID+"/audit",began:long.MaxValue);
            check(!(bool)audit["stockComparison"]["complete"] && (bool)audit["stockComparison"]["invalidBackingData"],"Malformed backing list is reported without repairing it");
            actor.Stock.Clear();
            for (int i=0;i<513;i++) configs.Add("empty"+i,Config("empty"+i,null,0));
            audit=Inspect("/economy/markets/"+ship.strRegID+"/audit",began:long.MaxValue);
            check(!(bool)audit["stockComparison"]["complete"] && (int)audit["stockComparison"]["actorsExamined"]==512,
                "Actor work is bounded even with empty inventories");
            AIShipManager.dictAIs["LOCAL"].Clear();AIShipManager.dictAIs["STALE"].Clear();AIShipManager.dictAIs["BROKEN"].Clear();
            for (int i=0;i<4100;i++) AIShipManager.dictAIs["LOCAL"].Add(cargo);
            index=ControllerInspection.Capture(long.MaxValue);data=ControllerData(index,ship.strRegID,ship);
            check(!index.Complete && index.Scanned==4096 && (bool)data["registrationsTruncated"] && data["registrations"].Count()==32,
                "Hard controller scan and detail bounds cannot create unbounded responses");
        } finally { guard.UnpatchSelf(); }
    }
}
