using System;
using System.Collections.Generic;
using HarmonyLib;
using Ostranauts.ShipGUIs.Utilities;

static partial class Program {
    static double physicalFuel;
    static int physicalFills;
    static bool PhysicalFuel(Ship __instance,ref double __result) {if(__instance.LoadState!=Ship.Loaded.Full)return true;__result=physicalFuel;return false;}
    static bool PhysicalCapacity(Ship __instance,ref double __result) {if(__instance.LoadState!=Ship.Loaded.Full)return true;__result=100;return false;}
    static bool PhysicalFill(Ship __instance,float amount,ref float __result) {
        if(__instance.LoadState!=Ship.Loaded.Full)return true;
        double accepted=Math.Min(amount,100-physicalFuel);physicalFuel+=accepted;physicalFills++;__result=(float)(amount-accepted);return false;
    }
    static void ExistingRouteRebase(Ship __instance) {if(__instance.objSS.NavData!=null)foreach(var p in __instance.objSS.NavData.GetNavDataPoints())p.FuelLevel+=90;}
    static Type RefuelType => typeof(Ostrabus.OstrabusPlugin).Assembly.GetType("Ostrabus.Inspection.ShipRefueling",true);
    static (int status,Dictionary<string,object> data) Refuel(bool apply,double? target=null,string path="/ships/SCAV-FIXTURE/refuel") {
        object reply=AccessTools.Method(RefuelType,"Run").Invoke(null,new object[]{path,apply,target});
        return ((int)AccessTools.Field(reply.GetType(),"Status").GetValue(reply),(Dictionary<string,object>)AccessTools.Field(reply.GetType(),"Body").GetValue(reply));
    }
    static void CheckRefuelApi(Harmony boundary) {
        var production=new Harmony("offline.refuel.production");
        production.CreateClassProcessor(typeof(Ostrabus.OstrabusPlugin).Assembly.GetType("Ostrabus.RefuelRCSPatch",true)).Patch();
        foreach(string name in new[]{"RcsAutomaticReplenishmentPatch","RcsPhysicalReplenishmentPatch"})
            production.CreateClassProcessor(typeof(Ostrabus.OstrabusPlugin).Assembly.GetType("Ostrabus.ShipNavigation."+name,true)).Patch();
        boundary.Patch(AccessTools.Method(typeof(ShipSitu),nameof(ShipSitu.UpdateTime)),prefix:new HarmonyMethod(typeof(Program),nameof(Quiet)));
        boundary.Patch(AccessTools.PropertyGetter(typeof(Ship),nameof(Ship.NavPlayerManned)),prefix:new HarmonyMethod(typeof(Program),nameof(NotPlayerManned)));
        try {
            var ship=MakeShip(10);ship.IsAIShip=true;ship.fShallowRCSRemassMax=100;Set(ship,"fShallowMass",1234d);
            CrewSim.coPlayer=Empty<CondOwner>();Set(CrewSim.coPlayer,"_strID","PLAYER");CrewSim.coPlayer.ship=MakeShip(0);
            CrewSim.system.dictShips=new Dictionary<string,Ship>{{ship.strRegID,ship}};
            var before=Refuel(false);
            Check(before.status==200 && (double)before.data["wouldAddKg"]==90 && ship.GetRCSRemain()==10,"Preview changed fuel or misreported capacity.");
            var route=new NavData(ship);route.AddNavDataPoint(new NavDataPoint(1000,Empty<ShipSitu>(),10,0,0));route.AddNavDataPoint(new NavDataPoint(1100,Empty<ShipSitu>(),0,0,0));ship.objSS.NavData=route;
            boundary.Patch(AccessTools.Method(typeof(Ship),nameof(Ship.AIRefuel)),postfix:new HarmonyMethod(typeof(Program),nameof(ExistingRouteRebase)));
            var filled=Refuel(true,1000);
            Check(filled.status==200 && ship.GetRCSRemain()==100 && (double)filled.data["targetKg"]==100,"Full refill failed capacity clamp.");
            Check((double)AccessTools.Field(typeof(Ship),"fShallowMass").GetValue(ship)==1234,"Refilling changed structural ship mass.");
            Check(route.Origin.FuelLevel==100 && route.Destination.FuelLevel==90 && (bool)filled.data["routeRebased"],"Existing route was not rebased by actual addition.");
            StarSystem.fEpoch=1000;route.TimeAdvance(ship.objSS);
            Check(ship.GetRCSRemain()==100,"Stored route discarded replenished fuel.");
            StarSystem.fEpoch=1100;route.TimeAdvance(ship.objSS);
            Check(ship.GetRCSRemain()==90,"Rebased route stopped charging its real fuel difference.");
            Refuel(true,50);Check(ship.GetRCSRemain()==90,"A lower refuel target drained fuel.");
            ship.objSS.NavData=null;ship.fShallowRCSRemass=10;Refuel(true,35);
            Check(ship.GetRCSRemain()==35,"Partial shallow refill did not use corrected native refueling.");
            Refuel(true,35);Check(ship.GetRCSRemain()==35,"Repeated refill exceeded requested target.");
            ship.fShallowRCSRemassMax=0;Check(Refuel(false).status==409 && ship.fShallowRCSRemassMax==0,"Preview invented an unknown capacity.");ship.fShallowRCSRemassMax=100;
            ship.IsAIShip=false;Check(Refuel(true).status==409 && ship.GetRCSRemain()==35,"Non-AI vessel was refilled.");ship.IsAIShip=true;
            Set(CrewSim.system,"dictShipOwners",new Dictionary<string,string>{{ship.strRegID,"PLAYER"}});
            Check(Refuel(true).status==409,"Player ownership was ignored.");
            Set(CrewSim.system,"dictShipOwners",new Dictionary<string,string>{{ship.strRegID,"OKLGScav"}});
            Check(Refuel(true,double.NaN).status==400 && Refuel(true,-1).status==400,"Invalid fuel targets accepted.");
            Check(Refuel(true,null,"/ships/NO-SUCH-SHIP/refuel").status==404,"Missing ship was accepted.");
            Check(Refuel(true,null,"/ships/a%2Fb/refuel").status==400,"Registry ID path escape was accepted.");
            var httpType=typeof(Ostrabus.OstrabusPlugin).Assembly.GetType("Ostrabus.Inspection.InspectionHttp",true);
            foreach(string input in new[]{"NaN","Infinity","-1","1,2","200"}) {
                object http=Activator.CreateInstance(httpType,true);
                AccessTools.Method(httpType,"Parse").Invoke(http,new object[]{"POST /ships/SCAV-FIXTURE/refuel?targetKg="+input+" HTTP/1.1\r\nHost: 127.0.0.1:18764\r\nContent-Length: 0",18764});
                Check(AccessTools.Field(httpType,"Error").GetValue(http)==null,"HTTP transport rejected supported targetKg before endpoint validation.");
                object[] arguments={http,null,null};bool valid=(bool)AccessTools.Method(RefuelType,"Parse").Invoke(null,arguments);
                Check(valid==(input=="200"),"Refuel request parsing accepted invalid number: "+input);
            }
            route.Destination.FuelLevel=double.NaN;ship.objSS.NavData=route;
            Check(Refuel(true).status==409 && ship.GetRCSRemain()==35,"Invalid route was mutated/refueled.");
            ship.objSS.NavData=null;Set(ship,"nLoadState",Ship.Loaded.Full);physicalFuel=10;
            boundary.Patch(AccessTools.Method(typeof(Ship),nameof(Ship.GetRCSRemain)),prefix:new HarmonyMethod(typeof(Program),nameof(PhysicalFuel)));
            boundary.Patch(AccessTools.Method(typeof(Ship),nameof(Ship.GetRCSMax)),prefix:new HarmonyMethod(typeof(Program),nameof(PhysicalCapacity)));
            boundary.Patch(AccessTools.Method(typeof(Ship),nameof(Ship.RefuelRCS)),prefix:new HarmonyMethod(typeof(Program),nameof(PhysicalFill)));
            Check(Refuel(false).status==200 && physicalFuel==10 && physicalFills==0,"Full-vessel preview filled tanks.");
            Refuel(true,50);Check(physicalFuel==50 && physicalFills==1 && ship.fShallowRCSRemass==50,"Full-vessel refill failed physical delegation or shallow reconciliation budget update.");
            physicalFuel=50; ship.fShallowRCSRemass=40; Refuel(true,75);
            Check(physicalFuel==75 && ship.fShallowRCSRemass==65,"Explicit refill forgave a pending shallow fuel debit.");
            boundary.Patch(AccessTools.Method(typeof(Ship),nameof(Ship.RemoveGasMass)),prefix:new HarmonyMethod(typeof(Program),nameof(LoadedRemoval)));
            ship.SyncFuel(); Check(physicalFuel==65,"Explicit refill did not preserve native delayed fuel reconciliation.");
            ship.fShallowRCSRemass=double.NaN;
            Check(Refuel(false).status==409,"Invalid loaded reconciliation budget was accepted.");
            Console.WriteLine(checks+" refuel API checks passed: native shallow filling/accounting and route preservation; no game scene or HTTP listener started.");
        } finally {production.UnpatchSelf();}
    }
}
