using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using Ostrabus.ShipNavigation;
using Ostranauts.Core;
using Ostranauts.Ships;
using Ostranauts.Ships.Commands;
using Ostranauts.ShipGUIs.Utilities;
using Ostranauts.Ships.Comms;
using UnityEngine;

static partial class Program {
    static bool ReplayMass(ref double __result) {__result=37772.85;return false;}
    static bool ReplayNoRoute(ref bool hit,ref bool __result) {hit=false;__result=false;return false;}
    static bool ReplayOldEscape(Ostranauts.Utils.Models.Point original,ref Ostranauts.Utils.Models.Point __result) {__result=original;return false;}
    struct DepartureResult {
        internal CommandCode Status;
        internal int Plans,Reversals,ManualTicks;
        internal double Minimum,Fuel,Seconds;
    }
    static int DepartureReplay() {
        var broken=ReplayOne("previous-repair",0,0,0,.5);
        Check(broken.Status!=CommandCode.Finished&&broken.Plans>500&&broken.Reversals>500,"Previous sustained oscillation was not reproduced");
        void Accept(DepartureResult result,bool clear=false) {
            Check(result.Status==CommandCode.Finished,"Continuous departure did not arrive");
            Check(result.Minimum>0,"Continuous departure intersected the obstacle");
            Check(result.Plans<=(clear?1:5),"Departure kept discarding/recreating routes");
            Check(result.ManualTicks<160,"Departure remained in manual evasion");
            Check(result.Reversals<30,"Departure retained thrust reversal loop");
            Check(result.Fuel>8500,"Departure wasted planned-flight fuel");
        }
        Accept(ReplayOne("fixed",0,0,0,.5));
        Accept(ReplayOne("fixed",500,20,5,.5));
        Accept(ReplayOne("fixed",-500,20,-5,.5));
        Accept(ReplayOne("fixed",-1000,0,0,.5,obstacleVx:10,obstacleVy:20));
        Accept(ReplayOne("fixed",0,0,0,1));
        Accept(ReplayOne("fixed",20000,0,0,.5),true);
        var full=Assembly.LoadFrom(System.IO.Path.GetFullPath("bin/Debug/netstandard2.1/Ostrabus.dll"));
        Console.WriteLine("Full DLL MVID: "+full.ManifestModule.ModuleVersionId);
        Accept(ReplayOne("full-DLL",0,0,0,.5,full));
        foreach(bool enabled in new[]{false,true})foreach(bool reverse in new[]{false,true})
            Accept(ReplayOne("full-DLL",0,0,0,.5,full,enabled,reverse));
        foreach(bool reverse in new[]{false,true})Accept(ReplayOne("composed-DLL",0,0,0,.5,full,true,reverse,compose:true));
        Console.WriteLine(checks+" continuous departure assertions passed.");
        return 0;
    }
    static DepartureResult ReplayOne(string mode,double offset,double vx,double vy,double tick,Assembly full=null,bool? hullEnabled=null,bool reverse=false,
        double obstacleVx=0,double obstacleVy=0,bool compose=false) {
        var engine=new Harmony("offline.departure-replay.engine");
        var production=new Harmony("offline.departure-replay.production");
        var savedWorld=CrewSim.system;var savedPlanet=planet;
        FieldInfo flagField=null;object oldFlag=null;
        try {
            void Patch(Type type,string method,string fixture) => engine.Patch(AccessTools.Method(type,method),prefix:new HarmonyMethod(typeof(Program),fixture));
            Patch(typeof(UnityEngine.Object),"op_Equality",nameof(Equal));Patch(typeof(UnityEngine.Object),"op_Inequality",nameof(Unequal));
            Patch(typeof(Ship),"get_RCSAccelMax",nameof(Accel));Patch(typeof(Ship),"get_Mass",nameof(ReplayMass));
            Patch(typeof(Time),"get_timeScale",nameof(Scale));Patch(typeof(CrewSim),"TimeElapsedScaled",nameof(Elapsed));
            Patch(typeof(Ship),"Maneuver",nameof(Maneuver));Patch(typeof(Ship),"get_NavAIManned",nameof(Yes));Patch(typeof(Ship),"get_NavPlayerManned",nameof(No));
            Patch(typeof(Ship),"GetReactorGPMValue",nameof(Gpm));Patch(typeof(Comms),"AIAnnounceEvasion",nameof(Nothing));
            Patch(typeof(Ostranauts.ShipGUIs.NavStation.NavModTorchDrive),"GetLimiterSafetyMax",nameof(Limiter));
            Patch(typeof(Ship),"RemoveGasMass",nameof(Gas));
            Patch(typeof(StarSystem),"GetBO",nameof(KnownBody));Patch(typeof(StarSystem),"GetNearestBO",nameof(Closest));
            Patch(typeof(StarSystem),"GetGreatestGravBO",nameof(Grav));Patch(typeof(StarSystem),"GetNearestStationRegional",nameof(NearestStation));
            foreach(var method in AccessTools.GetDeclaredMethods(typeof(AIShipManager))) if(method.Name=="PrioritizeShip")engine.Patch(method,prefix:new HarmonyMethod(typeof(Program),nameof(Nothing)));
            CrewSim.system=Empty<StarSystem>();
            planet=Empty<BodyOrbit>();planet.strName="Sol";planet.fPeriod=1e20;planet.nOrbitDirection=1;
            var home=Empty<BodyOrbit>();home.strName="HOME";home.fAxis1=home.fAxis2=2*(1+500000/RepairAU);home.fPerh=home.fAph=1+500000/RepairAU;home.fPeriod=1e30;home.nOrbitDirection=1;home.boParent=planet;home.nDrawFlagsBody=1;
            CrewSim.system.aBOs=new Dictionary<string,BodyOrbit>{{"Sol",planet},{"HOME",home}};
            rcsMaximum=physicalMax=15.1679322547977/RepairAU;forecasting=true;
            station=RepairHull("HOME",500000);station.objSS.bIsBO=true;station.objSS.strBOPORShip="HOME";
            var us=RepairHull("DEPARTING",0,0,vx,vy,1100);us.IsAIShip=true;
            var obstacle=RepairHull("OBSTACLE",20000,offset,obstacleVx,obstacleVy,500);
            us.objSS.vPosx+=1;obstacle.objSS.vPosx+=1;
            RepairField(us,"<Comms>k__BackingField",Empty<Comms>());
            RepairField(us,"fRCSCount",10f);RepairField(us,"fFuelEfficiencyMod",1d);
            us.fShallowRCSRemass=10000;us.fShallowRCSRemassMax=10000;
            us.shipScanTarget=station;us.shipSituTarget=station.objSS;
            CrewSim.system.dictShips=new Dictionary<string,Ship>{{us.strRegID,us},{obstacle.strRegID,obstacle},{station.strRegID,station}};
            var fly=new FlyTo(new TestPilot{ShipUs=us,Role=AIType.Civilian,Speed=500/RepairAU});var board=new Blackboard();
            var path=(FlyToPath)AccessTools.Field(typeof(FlyTo),"_flyToPath").GetValue(fly);path.fTorchSpeedLimit=500/RepairAU;
            Type PatchType(string name)=>(full??typeof(AIShipAvoidancePatch).Assembly).GetType("Ostrabus.ShipNavigation."+name,true);
            var patches=new List<Type>{PatchType("AIShipAvoidancePatch")};
            if(mode=="previous-repair") {
                // Recreate the deployed guard: acceleration-only prediction,
                // old escape selection and no new-course admission check.
                engine.Patch(AccessTools.Method(typeof(AvoidanceRouteForecast),"TryCollision"),prefix:new HarmonyMethod(typeof(Program),nameof(ReplayNoRoute)));
                engine.Patch(AccessTools.Method(typeof(AIShipAvoidance),"CurrentMotionEscape"),prefix:new HarmonyMethod(typeof(Program),nameof(ReplayOldEscape)));
            }else patches.Add(PatchType("AvoidanceFlightPatch"));
            if(hullEnabled.HasValue) {
                flagField=AccessTools.Field(PatchType("Collisions.PhysicalCollisions"),"Flag");oldFlag=flagField.GetValue(null);
                var flag=Empty<ConfigEntry<bool>>();RepairField(flag,"_typedValue",hullEnabled.Value);flagField.SetValue(null,flag);
                patches.Add(PatchType("Collisions.HullAvoidancePatch"));patches.Add(PatchType("Collisions.HullAvoidanceClearancePatch"));
            }
            if(compose) {
                foreach(string name in new[]{"FlightControlHandoffPatch","FlightPilotAvailabilityPatch","ShallowTorchShutdownPatch","ShallowRcsTorchReleasePatch",
                    "FreightRendezvousFailurePatch","AiRcsAccelerationPatch","AiRendezvousValidationPatch"})patches.Add(PatchType(name));
                foreach(string name in new[]{"Ostrabus.MiscBugFixes.AIFlightFuelPathPatch","Ostrabus.MiscBugFixes.AIFlightFuelManualPatch","Ostrabus.Venus.VorbNavPlanGuard"})
                    patches.Add(full.GetType(name,true));
            }
            if(reverse)patches.Reverse();foreach(var patch in patches)production.CreateClassProcessor(patch).Patch();
            int plans=0,reversals=0,evades=0;double previous=0,minimum=double.MaxValue;NavData last=null;CommandCode status=CommandCode.Running;
            double elapsed=0;
            for(int i=0;i<9600;i++) {
                double now=100000+i*.125;StarSystem.fEpoch=now;station.objSS.UpdateTime(now);
                elapsed=i*.125;
                if(i%(int)(tick/.125)==0) {
                    status=fly.RunCommand(board).Status;
                    if(us.objSS.NavData!=null&&!ReferenceEquals(us.objSS.NavData,last)){plans++;last=us.objSS.NavData;}
                    if(us.objSS.NavData==null)evades++;
                    double ax=us.objSS.vAccRCS.x;
                    if(previous*ax<0)reversals++;if(ax!=0)previous=ax;
                    if(status==CommandCode.Finished)break;
                }
                minimum=Math.Min(minimum,us.objSS.GetDistance(obstacle.objSS)*RepairAU-us.objSS.Size-obstacle.objSS.Size);
                StarSystem.fEpoch=now+.125;us.objSS.TimeAdvance(.125);
                obstacle.objSS.vPosx+=obstacleVx/RepairAU*.125;obstacle.objSS.vPosy+=obstacleVy/RepairAU*.125;
            }
            Check(ReferenceEquals(us.shipScanTarget,station),"Replay changed the authored destination");
            Console.WriteLine("DEPARTURE "+mode+" offset="+offset+" velocity="+vx+","+vy+" tick="+tick+" hull="+hullEnabled+" reverse="+reverse+" result="+status+" plans="+plans+" thrust reversals="+reversals+" manual ticks="+evades+" minimum clearance m="+minimum+" planned fuel kg="+(10000-us.fShallowRCSRemass)+" seconds="+elapsed);
            return new DepartureResult{Status=status,Plans=plans,Reversals=reversals,ManualTicks=evades,Minimum=minimum,Fuel=us.fShallowRCSRemass,Seconds=elapsed};
        }finally{production.UnpatchSelf();engine.UnpatchSelf();if(flagField!=null)flagField.SetValue(null,oldFlag);CrewSim.system=savedWorld;planet=savedPlanet;forecasting=false;station=null;}
    }
}
