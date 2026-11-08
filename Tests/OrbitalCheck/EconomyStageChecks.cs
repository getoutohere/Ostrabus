using System;
using HarmonyLib;
using Ostrabus.Orbital;
using Ostrabus.Orbital.Economy;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;
using Ostranauts.ShipGUIs.Utilities;

static partial class EconomyChecks {
    static bool Consume(Ship __instance,float __0,ref float __result) { __instance.fShallowRCSRemass=Math.Max(0,__instance.fShallowRCSRemass-__0);__result=__0;return false; }
    static NavData Leg(Ship ship, double epoch, double duration, double fromKm, double toKm, double rcs, double fusion) {
        var result=new NavData(ship);
        var a=new ShipSitu(false){vPosx=fromKm/OrbitalMotion.KmPerAU};
        var b=new ShipSitu(false){vPosx=toKm/OrbitalMotion.KmPerAU};
        result.AddNavDataPoint(new NavDataPoint(epoch,a,rcs,0,fusion));
        result.AddNavDataPoint(new NavDataPoint(epoch+duration,b,rcs-10,0,fusion-5));
        return result;
    }
    static AIShip StagePilot() {
        var pilot=new AIShip("SOURCE",AIType.HaulerCargo,"fixture");
        pilot.Ship.fFusionThrustMax=100;pilot.Ship.bFusionReactorRunning=true;
        hulls["DEST"].objSS.vBOOffsetx=hulls["DEST"].objSS.vPosx=.01;
        return pilot;
    }
    static void StageChecks(Action<bool,string> check,Harmony fixture) {
        fixture.Patch(AccessTools.Method(typeof(Ship),nameof(Ship.RemoveGasMass),new[]{typeof(float),typeof(bool)}),prefix:new HarmonyMethod(typeof(EconomyChecks),nameof(Consume)));
        foreach (double duration in new[]{1800d,21600d}) {
            Setup(); var pilot=StagePilot();var ship=pilot.Ship;
            var departure=Leg(ship,start,600,0,385,100,100);ship.objSS.NavData=departure;
            StarSystem.fEpoch=start+duration;
            stagedPlan=(planner,origin,destination)=>{
                check(origin.ArrivalTime==start+600,"Continuation plans at actual departure-leg completion");
                check(Math.Abs(origin.ObjSS.vPosx*OrbitalMotion.KmPerAU-385)<1e-6,"Continuation starts at virtual departure endpoint");
                check(origin.FuelLevel==90 && origin.TorchFuelLevel==95,"Continuation carries preceding fuel budget");
                check(StarSystem.fEpoch==start+duration && FreightPlanningScope.Epoch()==start+600,"Stage planner uses historical scope without rewinding world");
                check(ReferenceEquals(ship.objSS.NavData,departure) && OrbitalMotion.Read(ship.objSS).Position.Length==0,"Planning does not mutate live freight position/course");
                return Leg(ship,origin.ArrivalTime,3600,385,.01*OrbitalMotion.KmPerAU,origin.FuelLevel,origin.TorchFuelLevel);
            };
            var route=new FreightRoute(pilot,hulls["DEST"],start,start+duration,new SffEnvironment(),departure);
            check(route.Prepare() && route.StagesPlanned==1 && planCalls==1,"Departure continues to interplanetary course inside one skip");
            check(FreightPlanningScope.Current==null,"Continuation planning releases scope");
            if(duration==1800) {
                check(double.IsPositiveInfinity(route.Arrival) && route.Final.Position.X>385 && route.Final.Position.X<.01*OrbitalMotion.KmPerAU,"Partial torch travel is retained, not counted as delivery");
                route.Apply();check(ship.objSS.NavData!=null && ship.objSS.NavData.Destination.ArrivalTime==start+4200,"Partial continuation persists as native course");
                check(Math.Abs(ship.fShallowRCSRemass-(90-10d/3))<1e-4,"Prior and partial stage fuel deducted once");
            } else {
                check(route.Arrival==start+4200,"Arrival includes departure and interplanetary stage durations");
                route.Apply();check(ship.objSS.NavData==null && ship.fShallowRCSRemass==80 && ship.fShallowFusionRemain==90,"Completed stages clear course and conserve cumulative budget");
            }
            check(ship.shipScanTarget==hulls["DEST"] && ship.shipSituTarget==hulls["DEST"].objSS,"Continuation preserves the actual cargo destination");
        }
        Setup();var p=StagePilot();var first=Leg(p.Ship,start,600,0,385,100,100);p.Ship.objSS.NavData=first;
        stagedPlan=(planner,a,b)=>Leg(p.Ship,a.ArrivalTime,1,385,385,a.FuelLevel,a.TorchFuelLevel);
        var bounded=new FreightRoute(p,hulls["DEST"],start,start+21600,new SffEnvironment(),first);
        check(!bounded.Prepare() && bounded.StagesPlanned==8 && planCalls==8,"Repeated intermediate legs have a fixed planning budget");
        check(ReferenceEquals(p.Ship.objSS.NavData,first) && p.Ship.objSS.vPosx==0,"Rejected stage chain leaves live flight untouched");
        foreach(string failure in new[]{"null","zero","throw"}) {
            Setup();p=StagePilot();first=Leg(p.Ship,start,600,0,385,100,100);p.Ship.objSS.NavData=first;
            stagedPlan=(planner,a,b)=> failure=="throw" ? throw new InvalidOperationException("stage fixture") : failure=="null" ? null : Leg(p.Ship,a.ArrivalTime,0,385,385,a.FuelLevel,a.TorchFuelLevel);
            bool rejected=false;
            try { rejected=!new FreightRoute(p,hulls["DEST"],start,start+21600,new SffEnvironment(),first).Prepare(); } catch(InvalidOperationException) { rejected=failure=="throw"; }
            check(rejected && FreightPlanningScope.Current==null && ReferenceEquals(p.Ship.objSS.NavData,first) && p.Ship.objSS.vPosx==0,"Failed continuation restores state: "+failure);
        }
        Setup();p=StagePilot();p.Ship.objSS.vPosx=1000/OrbitalMotion.KmPerAU;
        first=Leg(p.Ship,start-600,599,0,385,100,100);
        stagedPlan=(planner,a,b)=>{
            check(a.ArrivalTime==start && Math.Abs(a.ObjSS.vPosx*OrbitalMotion.KmPerAU-1000)<1e-6,"Expired course replans from current position, never rewinds to old endpoint");
            return Leg(p.Ship,a.ArrivalTime,3600,1000,.01*OrbitalMotion.KmPerAU,a.FuelLevel,a.TorchFuelLevel);
        };
        check(new FreightRoute(p,hulls["DEST"],start,start+21600,new SffEnvironment(),first).Prepare(),"Expired course can continue");
        Setup();p=StagePilot();first=Leg(p.Ship,start,600,0,385,100,100);
        stagedPlan=(planner,a,b)=>planCalls==1 ? Leg(p.Ship,a.ArrivalTime,600,385,700,a.FuelLevel,a.TorchFuelLevel) :
            Leg(p.Ship,a.ArrivalTime,3600,700,.01*OrbitalMotion.KmPerAU,a.FuelLevel,a.TorchFuelLevel);
        var multiple=new FreightRoute(p,hulls["DEST"],start,start+21600,new SffEnvironment(),first);
        check(multiple.Prepare() && multiple.StagesPlanned==2 && multiple.Arrival==start+4800,"Several intermediate legs can precede interplanetary arrival");
        multiple.Apply();check(p.Ship.fShallowRCSRemass==70 && p.Ship.fShallowFusionRemain==85,"Multiple stages carry cumulative fuel without resets");
        Setup();p=StagePilot();
        stagedPlan=(planner,a,b)=>Leg(p.Ship,a.ArrivalTime,3600,0,.01*OrbitalMotion.KmPerAU,a.FuelLevel,a.TorchFuelLevel);
        var absent=new FreightRoute(p,hulls["DEST"],start,start+21600,new SffEnvironment(),null);
        check(absent.Prepare() && absent.StagesPlanned==1 && absent.Arrival==start+3600,"Torch-capable freight with no stored course plans rather than using interplanetary RCS");
        Setup();
    }
}
