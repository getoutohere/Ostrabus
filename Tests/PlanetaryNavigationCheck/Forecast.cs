using System;
using System.IO;
using System.Collections.Generic;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ostranauts.Core;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots.Interfaces;
using Ostranauts.Ships.Commands;
using Ostranauts.ShipGUIs.Utilities;
using SolarSystem;
using Ostranauts.Utils.Models;
using UnityEngine;
using Ostrabus.ShipNavigation;

static partial class Program {
    static bool forecasting;
    static double physicalMax=1.9016982118751132E-10;
    static BodyOrbit planet;
    static bool Closest(double dfEpoch, ref BodyOrbit __result) {planet.UpdateTime(dfEpoch); __result=planet;return false;}
    static bool Grav(double dfEpoch,ref BodyOrbit boClosest,ref BodyOrbit __result) {planet.UpdateTime(dfEpoch); boClosest=planet;__result=CrewSim.system.aBOs["Sol"];return false;}
    static bool KnownBody(string strName,ref BodyOrbit __result) { __result=strName!=null && CrewSim.system.aBOs.TryGetValue(strName,out var b)?b:null;return false; }
    static bool Yes(ref bool __result) {__result=true;return false;}
    static bool No(ref bool __result) {__result=false;return false;}
    static bool Nothing() => false;
    static bool Gpm(ref string __result) {__result=null;return false;}
    static bool Limiter(ref float __result) {__result=1;return false;}
    static bool Gas(Ship __instance,float __0,ref float __result) { __result=__0;__instance.fShallowRCSRemass-=__0;return false; }
    static void Forecast(Harmony h, Ship ship, DockGroup group,string snapshot,JObject record) {
        forecasting=true;
        void Patch(Type t,string name,string fn,Type[] types=null) => h.Patch(AccessTools.Method(t,name,types),prefix:new HarmonyMethod(typeof(Program),fn));
        h.Unpatch(AccessTools.Method(typeof(StarSystem),"GetBO"),HarmonyPatchType.Prefix,h.Id);
        Patch(typeof(StarSystem),"GetBO",nameof(KnownBody));
        Patch(typeof(StarSystem),"GetNearestBO",nameof(Closest));
        Patch(typeof(StarSystem),"GetGreatestGravBO",nameof(Grav));
        Patch(typeof(Ship),"get_NavAIManned",nameof(Yes));
        Patch(typeof(Ship),"get_NavPlayerManned",nameof(No));
        Patch(typeof(Ship),"GetReactorGPMValue",nameof(Gpm));
        Patch(typeof(Ostranauts.ShipGUIs.NavStation.NavModTorchDrive),"GetLimiterSafetyMax",nameof(Limiter));
        Patch(typeof(Ship),"RemoveGasMass",nameof(Gas));
        foreach(var method in AccessTools.GetDeclaredMethods(typeof(AIShipManager))) if(method.Name=="PrioritizeShip") h.Patch(method,prefix:new HarmonyMethod(typeof(Program),nameof(Nothing)));
        Patch(typeof(Ostranauts.Ships.Comms.Comms),"AIAnnounceEvasion",nameof(Nothing));
        var e=JObject.Parse(File.ReadAllText(snapshot));
        var bodies=new Dictionary<string,BodyOrbit>();
        foreach(var prop in ((JObject)e["data"]["bodies"]).Properties()) {
            var r=prop.Value;
            var b=Empty<BodyOrbit>();b.strName=prop.Name;
            b.fAxis1=D(r,"majorAxisAU");b.fAxis2=D(r,"minorAxisAU");b.fPerh=D(r,"periapsisAU");b.fAph=D(r,"apoapsisAU");
            b.fEcc=D(r,"eccentricity");b.fAngle=D(r,"angleDegrees");b.fPeriod=D(r,"periodSeconds");b.fPeriodShift=D(r,"phaseSeconds");
            b.nOrbitDirection=(int)r["direction"];b.fRadius=D(r,"radiusAU");b.fMass=D(r,"massKg");
            if(prop.Name=="OKLG" || prop.Name.StartsWith("VORB"))b.nDrawFlagsBody=1;
            b.dXReal=D(r,"xAU");b.dYReal=D(r,"yAU");b.dVelX=D(r,"vxAUPerSecond");b.dVelY=D(r,"vyAUPerSecond");
            b.dTimeCalcLast=D(r,"positionCalculationEpoch");b.dVelCalcLast=D(r,"velocityCalculationEpoch");bodies[prop.Name]=b;
        }
        foreach(var prop in ((JObject)e["data"]["bodies"]).Properties()) {string parent=(string)prop.Value["parentId"];if(parent!=null && bodies.ContainsKey(parent))bodies[prop.Name].boParent=bodies[parent];}
        CrewSim.system.aBOs=bodies;planet=bodies["1036 Ganymed"];
        // Only local known obstacles are material to this encounter. Keep their
        // saved rigid offsets; use native body ephemerides and target locking.
        var local=new Dictionary<string,Ship>();
        foreach(string id in new[]{"O-ENB","OKLG","OKLG|RES_1","O-GXD","E-7HAP","O-F4C6","J-2VC"})local[id]=CrewSim.system.dictShips[id];
        CrewSim.system.dictShips=local;
        var anchorStart=new ShipSitu(group.Anchor,false);
        var originals=new Dictionary<Ship,ShipSitu>();foreach(var s in local.Values)originals[s]=new ShipSitu(s.objSS,false);
        bool hauler=record["objSS"]["jnd"]!=null;
        var target=local[hauler?"OKLG":"J-2VC"];
        AccessTools.Field(typeof(Ship),"<Comms>k__BackingField").SetValue(ship,Empty<Ostranauts.Ships.Comms.Comms>());
        ship.shipSituTarget=target.objSS;ship.fAIPauseTimer=0;ship.fShallowRCSRemass=3000;
        AccessTools.Field(typeof(Ship),"fRCSCount").SetValue(ship,16f);
        AccessTools.Field(typeof(Ship),"fFuelEfficiencyMod").SetValue(ship,1d);
        var ss=record["objSS"];var times=ss["aPathRecentT"];
        int index=13;double begin=hauler?(double)e["epoch"]:(double)times[index];
        double initialVx=((double)ss["aPathRecentX"][index-1]-(double)ss["aPathRecentX"][index+1])/((double)times[index-1]-(double)times[index+1]);
        double initialVy=((double)ss["aPathRecentY"][index-1]-(double)ss["aPathRecentY"][index+1])/((double)times[index-1]-(double)times[index+1]);
        if(hauler){initialVx=(double)ss["vVelX"];initialVy=(double)ss["vVelY"];}
        for(int variant=0;variant<5;variant++) {
            var production=new Harmony("offline.planetary.production");
            h.Unpatch(AccessTools.Method(typeof(AIShipManager),"IsOnCollisionCourse"),HarmonyPatchType.Transpiler,h.Id);
            if(variant==1)h.Patch(AccessTools.Method(typeof(AIShipManager),"IsOnCollisionCourse"),transpiler:new HarmonyMethod(typeof(Program),nameof(FutureOnly)));
            if(variant>=2)production.CreateClassProcessor(typeof(AIShipAvoidancePatch)).Patch();
            foreach(var pair in originals)pair.Key.objSS.CopyFrom(pair.Value,false);
            group.Anchor.CopyFrom(anchorStart,false);
            string anchorId=variant==3?"O-GXD":variant==4?"E-7HAP":"OKLG|RES_1";
            if(variant>=3) {
                group.Anchor.CopyFrom(local[anchorId].objSS,false);
                group.Anchor.Size=(int)AccessTools.Method(typeof(DockGroup),"CalculateAnchorSize").Invoke(group,null);
            }
            var variantAnchor=new ShipSitu(group.Anchor,false);
            ship.objSS.NavData=null;ship.objSS.ResetAccelerations();ship.objSS.fRot=0;ship.objSS.fW=0;
            ship.objSS.vPosx=hauler?(double)ss["vPosx"]:(double)ss["aPathRecentX"][index];ship.objSS.vPosy=hauler?(double)ss["vPosy"]:(double)ss["aPathRecentY"][index];
            ship.objSS.vVelX=initialVx;ship.objSS.vVelY=initialVy;ship.fShallowRCSRemass=3000;
            if(hauler)ship.objSS.NavData=new NavData(ss["jnd"].ToObject<JsonNavData>());
            var fly=new FlyTo(new TestPilot{ShipUs=ship,Role=hauler?AIType.HaulerDeployer:AIType.Police,Speed=hauler?5.013440329548757E-9:1.0026880659097515E-8});var bb=new Blackboard();
            double minimum=double.MaxValue,wreckMinimum=double.MaxValue;int evades=0,plans=0;bool impact=false;double elapsed=0;CommandCode lastStatus=CommandCode.Running;
            for(int i=0;i<1600;i++) {
                double epoch=begin+i*.125;StarSystem.fEpoch=epoch;
                elapsed=i*.125;
                foreach(var b in bodies.Values)b.UpdateTime(epoch);
                group.Anchor.UpdateTime(epoch);
                foreach(var pair in originals)if(pair.Key!=ship && pair.Key!=target) {
                    if(pair.Key.objSS.bBOLocked || pair.Key.objSS.bIsBO)pair.Key.objSS.UpdateTime(epoch);
                    else {pair.Key.objSS.vPosx=pair.Value.vPosx+group.Anchor.vPosx-variantAnchor.vPosx;pair.Key.objSS.vPosy=pair.Value.vPosy+group.Anchor.vPosy-variantAnchor.vPosy;pair.Key.objSS.vVelX=group.Anchor.vVelX;pair.Key.objSS.vVelY=group.Anchor.vVelY;}
                }
                target.objSS.UpdateTime(epoch);
                double clearance=(ship.objSS.GetDistance(planet.dXReal,planet.dYReal)-planet.fRadius-ship.objSS.GetRadiusAU())*AU;
                wreckMinimum=Math.Min(wreckMinimum,(ship.objSS.GetDistance(local["O-F4C6"].objSS)-ship.objSS.GetRadiusAU()-local["O-F4C6"].objSS.GetRadiusAU())*AU);
                minimum=Math.Min(minimum,clearance);if(clearance<0){impact=true;break;}
                if(i%8==0) {
                    if(AIShipManager.IsOnCollisionCourse(ship,out _))evades++;
                    var old=ship.objSS.NavData;lastStatus=fly.RunCommand(bb).Status;if(old==null && ship.objSS.NavData!=null)plans++;
                    if(lastStatus==CommandCode.Finished)break;
                }
                StarSystem.fEpoch=epoch+.125;ship.objSS.TimeAdvance(.125);
            }
            Console.WriteLine("FORECAST hauler="+hauler+"; variant="+variant+"; anchor="+anchorId+"; impact="+impact+"; elapsed="+elapsed+"; minimum hull clearance km="+minimum+"; wreck clearance km="+wreckMinimum+"; evasion ticks="+evades+"; plans="+plans+"; status="+lastStatus+"; target range km="+ship.objSS.GetDistance(target.objSS)*AU+"; target retained="+(ship.shipScanTarget==target));
            if(variant<2)Check(impact,"Native/partial-fix impact reproduction changed.");
            if(variant>=2)Check(!impact && lastStatus==CommandCode.Finished && minimum>0 && wreckMinimum>0 && ship.shipScanTarget==target,"Production avoidance did not safely complete native FlyTo.");
            production.UnpatchSelf();
        }
        forecasting=false;
    }
    sealed class TestPilot:IAICharacter {
        internal double Speed;
        internal AIType Role=AIType.Police;
        public Ship ShipUs{get;set;}public CondOwner CaptainUs=>null;public QueuedCommandsNode PriorityQueue{get;set;}public ICommand BehaviorTree{get;set;}public Blackboard Blackboard{get;set;}=new Blackboard();public AIType AIType=>Role;
        public TargetData GetTarget()=>null;public TargetData GetTargetHome()=>null;public double MaxSpeed(bool? atmo=null)=>Speed;public ICommand FFWD(ICommand c,Blackboard b)=>c;
    }
}
