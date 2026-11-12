using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;
using Ostrabus.NavConsole.Outlines;
using Ostrabus.ShipNavigation;
using Ostrabus.ShipNavigation.Collisions;
using Ostranauts.Ships.Commands;
using Ostranauts.Ships.Comms;
using UnityEngine;

static partial class Program {
    static bool dockingPortsOpen = true;
    static bool DockingPorts(ref bool __result) { __result=dockingPortsOpen;return false; }
    static bool DockingMass(ref double __result) { __result=1000;return false; }
    static bool NoDockingCrew(ref CondOwner __result) { __result=null;return false; }
    static bool DockingShader(ref int __result) { __result=1;return false; }
    static readonly BodyOrbit dockingFarBody=Empty<BodyOrbit>();
    static bool DockingNearest(ref BodyOrbit __result) { dockingFarBody.dXReal=1;dockingFarBody.dYReal=1;__result=dockingFarBody;return false; }
    static float ringFraction;
    static float RingCurve(AnimationCurve curve,float fraction) { ringFraction=fraction;return fraction; }
    static IEnumerable<CodeInstruction> RingBoundary(IEnumerable<CodeInstruction> instructions) {
        foreach(var instruction in instructions) {
            if(instruction.Calls(AccessTools.Method(typeof(AnimationCurve),nameof(AnimationCurve.Evaluate)))) {
                instruction.opcode=OpCodes.Call;instruction.operand=AccessTools.Method(typeof(Program),nameof(RingCurve));
            }
            yield return instruction;
        }
    }
    static GUIDockSys DockPanel(Ship source,Ship target) {
        var owner=Empty<CondOwner>();Field(owner,"_objShip",source);
        var panel=Empty<GUIDockSys>();Field(panel,"coSelfTemp",owner);Field(panel,"objSS",new ShipSitu(false));
        var comms=Empty<Comms>();comms.Clearance=new Clearance { TargetRegId=target.strRegID };
        Field(source,"<Comms>k__BackingField",comms);
        Field(source,"aDocked",new Dictionary<string,Ship>());Field(target,"aDocked",new Dictionary<string,Ship>());
        var curve=Empty<AnimationCurveAsset>();curve.curve=Empty<AnimationCurve>();AccessTools.Field(typeof(GUIDockSys),"curveDockingRingSize").SetValue(null,curve);
        return panel;
    }
    static bool CanClamp(GUIDockSys panel) => (bool)AccessTools.Method(typeof(GUIDockSys),"CanDock").Invoke(panel,null);
    static void SetApproach(Ship a,Ship b,double distance) {a.objSS.vPosx=b.objSS.vPosx;a.objSS.vPosy=b.objSS.vPosy+distance*HullZoom.MetresToAU;}
    static void DockingCases(Ship source=null,Ship target=null,Envelope first=null,Envelope second=null,string sourcePort="source-port",string targetPort="target-port") {
        int before=checks;
        var originalWorld=CrewSim.system;var originalFlag=PhysicalCollisions.Flag;
        var originalStationFlag=StationDockedCollisions.Flag;
        var production=new Harmony("offline.hull.docking.production");var environment=new Harmony("offline.hull.docking.environment");
        var ringMethod=AccessTools.Method(typeof(GUIDockSys),"ConvertShipToRing");
        var savedCurve=AccessTools.Field(typeof(GUIDockSys),"curveDockingRingSize").GetValue(null);
        try {
            HullDisplay.Reset();
            PhysicalCollisions.Flag=Empty<ConfigEntry<bool>>();Field(PhysicalCollisions.Flag,"_typedValue",true);
            source=source??CollisionHull("DOCK-A",0,0);target=target??CollisionHull("DOCK-B",0,0);
            if(source.json.aItems==null||!source.json.aItems.Any(i=>i.strID==sourcePort))source.json.aItems=new[]{new JsonItem {strID=sourcePort,fRotation=180}};
            if(target.json.aItems==null||!target.json.aItems.Any(i=>i.strID==targetPort))target.json.aItems=new[]{new JsonItem {strID=targetPort,fRotation=90}};
            first=first??new EnvelopeBuilder().Build(new[]{new HullPoint(-2,-20),new HullPoint(2,-20),new HullPoint(2,20),new HullPoint(-2,20)},4,null);
            second=second??new EnvelopeBuilder().Build(new[]{new HullPoint(-12,-3),new HullPoint(12,-3),new HullPoint(12,3),new HullPoint(-12,3)},4,null);
            // These are completed production envelopes, not raw socket quads
            // to submit through the builder a second time.
            WarmCollision(source,first,true);WarmCollision(target,second,true);
            CrewSim.system=Empty<StarSystem>();CrewSim.system.dictShips=new Dictionary<string,Ship>{{source.strRegID,source},{target.strRegID,target}};
            var panel=DockPanel(source,target);
            environment.Patch(AccessTools.Method(typeof(Ship),nameof(Ship.HasOpenDockingPorts)),prefix:new HarmonyMethod(typeof(Program),nameof(DockingPorts)));
            environment.Patch(AccessTools.PropertyGetter(typeof(Ship),nameof(Ship.Mass)),prefix:new HarmonyMethod(typeof(Program),nameof(DockingMass)));
            environment.Patch(AccessTools.Method(typeof(CrewSim),nameof(CrewSim.GetSelectedCrew)),prefix:new HarmonyMethod(typeof(Program),nameof(NoDockingCrew)));
            environment.Patch(AccessTools.Method(typeof(Shader),nameof(Shader.PropertyToID),new[]{typeof(string)}),prefix:new HarmonyMethod(typeof(Program),nameof(DockingShader)));
            environment.Patch(AccessTools.Method(typeof(StarSystem),nameof(StarSystem.GetNearestBO)),prefix:new HarmonyMethod(typeof(Program),nameof(DockingNearest)));
            environment.Patch(ringMethod,transpiler:new HarmonyMethod(typeof(Program),nameof(RingBoundary)));
            dockingPortsOpen=true;
            double native=CollisionManager.GetCollisionDistanceAU(source,target)/HullZoom.MetresToAU;
            double corrected=(double)first.MapRadius+second.MapRadius;
            SetApproach(source,target,corrected);
            Check(corrected>native*1.1&&!CanClamp(panel),"Native clamp range reproduces resized-hull obstruction");
            source.PlaceAtAirlocks(sourcePort,targetPort,target);
            double nativePlacement=source.objSS.GetDistance(target.objSS)/HullZoom.MetresToAU;
            float nativeRotation=source.objSS.fRot;
            SetApproach(source,target,corrected+10);
            source.objSS.PushbackFrom(target);
            Check(Math.Abs(source.objSS.GetDistance(target.objSS)/HullZoom.MetresToAU-nativePlacement)<.02,"Native pushback reproduces inward reset to old radius");
            production.CreateClassProcessor(typeof(HullDockingGeometryPatch)).Patch();
            production.CreateClassProcessor(typeof(HullDockingHandoffPatch)).Patch();
            SetApproach(source,target,corrected);
            Check(CanClamp(panel),"Native CanDock accepts corrected hull contact range without identity/outline information");
            Check(!PhysicalCollisions.Overlap(source,target,NativeDistance(source,target),CollisionManager.GetCollisionDistanceAU(source,target)),"Bounding-circle contact range does not penetrate hulls");
            SetApproach(source,target,corrected*1.101);Check(!CanClamp(panel),"Native 10-percent clamp margin remains bounded");
            SetApproach(source,target,corrected);
            Field(panel,"fObjPosX",11f);Check(!CanClamp(panel),"Native lateral alignment restriction remains authoritative");Field(panel,"fObjPosX",0f);
            var mini=(ShipSitu)AccessTools.Field(typeof(GUIDockSys),"objSS").GetValue(panel);
            mini.vPosy=11;Check(!CanClamp(panel),"Native docking-ring alignment remains authoritative");mini.vPosy=0;
            dockingPortsOpen=false;Check(!CanClamp(panel),"Unavailable docking ports still reject docking");dockingPortsOpen=true;
            var clearance=source.Comms.Clearance;source.Comms.Clearance=null;Check(!CanClamp(panel),"Native clearance is still mandatory");source.Comms.Clearance=clearance;
            source.PlaceAtAirlocks(sourcePort,targetPort,target);
            double placement=source.objSS.GetDistance(target.objSS)/HullZoom.MetresToAU;
            Check(Math.Abs(placement-(corrected+10))<.02,"Native dock placement uses corrected bounds plus its existing 10m gap");
            Check(Math.Abs(source.objSS.fRot-nativeRotation)<1e-6,"Selected port orientation was changed");
            Check((source.objSS.vPos-target.objSS.vPos-new Ostranauts.Utils.Models.Point(source.objSS.vBOOffsetx,source.objSS.vBOOffsety)).magnitude<1e-14,"Docked world position and stored offset disagree");
            Check(!PhysicalCollisions.Overlap(source,target,NativeDistance(source,target),CollisionManager.GetCollisionDistanceAU(source,target)),"Corrected native placement leaves visible hulls separated");
            PushbackChecks(source,target,first,second,corrected,nativePlacement);
            ringMethod.Invoke(panel,new object[]{target});
            float correctedRing=(float)AccessTools.Field(typeof(GUIDockSys),"fObjScale").GetValue(panel);
            Check(correctedRing>.99f&&correctedRing<=1,"Docking ring agrees with corrected dock placement");
            var command=Empty<FlyToPath>();Field(command,"_shipUs",source);
            double handoff=(double)AccessTools.Method(typeof(BaseCommand),"GetHandOffDistance").Invoke(command,new object[]{native*HullZoom.MetresToAU,target});
            Check(Math.Abs(handoff/HullZoom.MetresToAU-corrected*3)<.01,"AI flight/docking handoff stays outside enlarged hull contact");
            Check(CollisionManager.GetCollisionDistanceAU(source,target)/HullZoom.MetresToAU==native,"Shared radius helper was modified");
            Field(PhysicalCollisions.Flag,"_typedValue",false);
            SetApproach(source,target,corrected);Check(!CanClamp(panel),"Disabling hull collisions immediately restores native clamp range");
            source.PlaceAtAirlocks(sourcePort,targetPort,target);
            Check(Math.Abs(source.objSS.GetDistance(target.objSS)/HullZoom.MetresToAU-nativePlacement)<.01,"Disabled docking placement is native");
            double offHandoff=(double)AccessTools.Method(typeof(BaseCommand),"GetHandOffDistance").Invoke(command,new object[]{native*HullZoom.MetresToAU,target});
            Check(Math.Abs(offHandoff/HullZoom.MetresToAU-native*3)<.01,"Disabled AI handoff is native");
            Field(PhysicalCollisions.Flag,"_typedValue",true);
            foreach(var kind in new[]{Ship.TypeClassification.OrbitalStation,Ship.TypeClassification.Asteroid,Ship.TypeClassification.Projectile,Ship.TypeClassification.Buoy}) {
                target.Classification=kind;
                Check(HullDocking.DistanceAU(source,target)==CollisionManager.GetCollisionDistanceAU(source,target),"Excluded object docking radius changed: "+kind);
                source.PlaceAtAirlocks(sourcePort,targetPort,target);
                Check(Math.Abs(source.objSS.GetDistance(target.objSS)/HullZoom.MetresToAU-nativePlacement)<.01,"Excluded object native placement changed");
            }
            target.Classification=Ship.TypeClassification.Default;
            DockingLoadStates(source,target,first,second,sourcePort,targetPort,corrected);
            DockingGroups(source,target,sourcePort,targetPort,corrected);
            LargeDockingRing(environment,panel,source,target);
            Console.WriteLine("Hull docking: "+(checks-before)+" native UI/placement/toggle/handoff/group checks passed; native clamp "+(native*1.1).ToString("F2")+"m, corrected clamp "+(corrected*1.1).ToString("F2")+"m, corrected placement "+placement.ToString("F2")+"m.");
        } finally {
            production.UnpatchSelf();environment.UnpatchSelf();PhysicalCollisions.Flag=originalFlag;StationDockedCollisions.Flag=originalStationFlag;CrewSim.system=originalWorld;
            AccessTools.Field(typeof(GUIDockSys),"curveDockingRingSize").SetValue(null,savedCurve);HullDisplay.Reset();
        }
    }
    static void PushbackChecks(Ship a,Ship b,Envelope ga,Envelope gb,double radius,double nativePlacement) {
        a.objSS.bBOLocked=a.objSS.bOrbitLocked=false;a.objSS.strBOPORShip=null;
        var solver=new HullContact();
        foreach(double angle in new[]{0d,.7,Math.PI/2,Math.PI,4.3}) {
            a.objSS.vPosx=b.objSS.vPosx+Math.Cos(angle)*(radius+10)*HullZoom.MetresToAU;
            a.objSS.vPosy=b.objSS.vPosy+Math.Sin(angle)*(radius+10)*HullZoom.MetresToAU;
            var velocity=a.objSS.vVel;
            a.objSS.PushbackFrom(b); // Actual native kick, placement and safe-coordinate scan.
            double distance=a.objSS.GetDistance(b.objSS)/HullZoom.MetresToAU;
            Check(Math.Abs(distance-radius-10)<.05,"Native undock pushback reintroduced old docking overlap");
            Check(Math.Abs((a.objSS.vVel-velocity).magnitude/HullZoom.MetresToAU-5)<.001,"Pushback changed the native 5m/s separation kick");
            var p=a.objSS.vPos-b.objSS.vPos;var pos=new HullContact.Vec(p.X/HullZoom.MetresToAU,p.Y/HullZoom.MetresToAU);
            double ra=a.objSS.fRot-a.nGridRotation*Math.PI/180,rb=b.objSS.fRot-b.nGridRotation*Math.PI/180;
            var motion=new HullContact.Motion(pos,pos,ra,ra,rb,rb,ga.MapRadius,gb.MapRadius,false);
            Check(solver.TrySolve(ga.Collision,gb.Collision,motion,out var contact)&&!contact.Hit,"Undocked outlines overlap after actual pushback");
        }
        // Temporary predicted points are not live hulls, even if their numeric
        // fields match. Native callers must not gain a guessed actor identity.
        var anonymous=new ShipSitu(a.objSS);
        float native=CollisionManager.GetCollisionDistanceAU(a,b);
        Check(HullDocking.OrbitPlacementRadius(a.objSS.GetRadiusAU(),b.objSS.GetRadiusAU(),anonymous,b.objSS)==native,"Anonymous trajectory point inherited a ship radius");
        long allocated=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<10000;i++)HullDocking.OrbitPlacementRadius(a.objSS.GetRadiusAU(),b.objSS.GetRadiusAU(),a.objSS,b.objSS);
        Check(GC.GetAllocatedBytesForCurrentThread()==allocated,"Rare placement identity lookup allocates per call");
        Field(PhysicalCollisions.Flag,"_typedValue",false);
        a.objSS.PushbackFrom(b);
        Check(Math.Abs(a.objSS.GetDistance(b.objSS)/HullZoom.MetresToAU-nativePlacement)<.05,"Feature-off pushback did not restore native placement");
        Field(PhysicalCollisions.Flag,"_typedValue",true);
    }
    static void DockingLoadStates(Ship a,Ship b,Envelope ga,Envelope gb,string portA,string portB,double radius) {
        void Ports(Ship ship,string id) {
            var saved=ship.json.aItems.Single(i=>i.strID==id);
            var co=Empty<CondOwner>();Field(co,"_strID",id);
            var item=Empty<Item>();Field(item,"fRotLast",saved.fRotation);
            Field(co,"_itemComponentReference",item);Field(co,"bTriedGettingItemRef",true);
            ship.aDocksys=new List<CondOwner>{co};
        }
        Ports(a,portA);Ports(b,portB);
        var solver=new HullContact();
        foreach(var loadA in new[]{Ship.Loaded.Shallow,Ship.Loaded.Full})foreach(var loadB in new[]{Ship.Loaded.Shallow,Ship.Loaded.Full})
            foreach(int grid in new[]{0,90,180,270})foreach(float rotation in new[]{0f,.7f,2.1f,4.3f}) {
                Field(a,"nLoadState",loadA);Field(b,"nLoadState",loadB);
                a.nGridRotation=grid;b.nGridRotation=(360-grid)%360;b.objSS.fRot=rotation;
                a.PlaceAtAirlocks(portA,portB,b);
                Check(Math.Abs(a.objSS.GetDistance(b.objSS)/HullZoom.MetresToAU-radius-10)<.02,"Loading/grid rotation changed docking separation");
                var p=a.objSS.vPos-b.objSS.vPos;
                var pos=new HullContact.Vec(p.X/HullZoom.MetresToAU,p.Y/HullZoom.MetresToAU);
                double ra=a.objSS.fRot-a.nGridRotation*Math.PI/180,rb=b.objSS.fRot-b.nGridRotation*Math.PI/180;
                var motion=new HullContact.Motion(pos,pos,ra,ra,rb,rb,ga.MapRadius,gb.MapRadius,false);
                Check(solver.TrySolve(ga.Collision,gb.Collision,motion,out var contact)&&!contact.Hit,"Native placement penetrates visible geometry after load/rotation");
            }
        Field(a,"nLoadState",Ship.Loaded.Shallow);Field(b,"nLoadState",Ship.Loaded.Shallow);a.nGridRotation=b.nGridRotation=0;
    }
    static void DockingGroups(Ship a,Ship b,string portA,string portB,double radius) {
        var station=CollisionHull("DOCK-STATION",0,0);station.Classification=Ship.TypeClassification.OrbitalStation;station.objSS.bIsBO=true;
        var group=new SolarSystem.DockGroup { Anchor=new ShipSitu(false) };
        group.DockedShips.Add(new SolarSystem.DockGroupMember(b,false));group.DockedShips.Add(new SolarSystem.DockGroupMember(station,false));
        b.objSS._dockGroup=group;station.objSS._dockGroup=group;
        Check(!HullDocking.Applies(a,b),"Station-connected chain was resized");
        b.objSS._dockGroup=null;station.objSS._dockGroup=null;
        CrewSim.system.dictShips.Add(station.strRegID,station);
        ((Dictionary<string,Ship>)AccessTools.Field(typeof(Ship),"aDocked").GetValue(b)).Add("station",station);
        ((Dictionary<string,Ship>)AccessTools.Field(typeof(Ship),"aDocked").GetValue(station)).Add("ship",b);
        StationDockedCollisions.Flag=Empty<ConfigEntry<bool>>();Field(StationDockedCollisions.Flag,"_typedValue",false);
        Check(!HullDocking.Applies(a,b),"Restored station links without a DockGroup were resized when station-collision exemption was off");
        ((Dictionary<string,Ship>)AccessTools.Field(typeof(Ship),"aDocked").GetValue(b)).Clear();CrewSim.system.dictShips.Remove(station.strRegID);
        Field(b,"_subStation",true);Check(!HullDocking.Applies(a,b),"Default-classified substation was resized");Field(b,"_subStation",false);
        a.PlaceAtAirlocks(portA,portB,b);
        double expected=(radius+10)*HullZoom.MetresToAU;
        Check(Math.Abs(a.objSS.GetDistance(b.objSS)-expected)<1e-12,"Formation rebuild returned to old radius");
        a.PlaceAtAirlocks(portA,portB,b);
        Check(Math.Abs(a.objSS.GetDistance(b.objSS)-expected)<1e-12,"Formation rebuild compounded separation");
        var free=new SolarSystem.DockGroup();free.AddShip(a);free.AddShip(b);
        double originalDistance=a.objSS.GetDistance(b.objSS);
        Check(HullDocking.Applies(a,b),"Ordinary free dock group was treated as a station");
        Check(a.objSS._dockOffsetPos.magnitude+b.objSS._dockOffsetPos.magnitude>=originalDistance-1e-12,"Native dock group lost corrected spatial offsets");
        free.RemoveShip(a);
        Check(a.objSS._dockGroup==null&&Math.Abs(a.objSS.GetDistance(b.objSS)-originalDistance)<1e-12,"Native group separation changed corrected hull positions");
        free.RemoveShip(b);
        var child=CollisionHull("DOCK-DEPARTING-CHILD",0,0);
        child.objSS.vPosx=a.objSS.vPosx+500*HullZoom.MetresToAU;child.objSS.vPosy=a.objSS.vPosy;
        var childHull=new Envelope(new[]{new HullPoint(-2,-2),new HullPoint(2,-2),new HullPoint(2,2),new HullPoint(-2,2)},4);
        WarmCollision(child,childHull,true);CrewSim.system.dictShips.Add(child.strRegID,child);
        var departing=new SolarSystem.DockGroup();departing.AddShip(a);departing.AddShip(child);
        double groupBound=Math.Max(a.objSS._dockOffsetPos.magnitude/HullZoom.MetresToAU+HullDisplay.Radius(a),
            child.objSS._dockOffsetPos.magnitude/HullZoom.MetresToAU+HullDisplay.Radius(child));
        double bound=groupBound+HullDisplay.Radius(b);
        departing.Anchor.PlaceOrbitPosition(b.objSS);
        Check(departing.Anchor.GetDistance(b.objSS)/HullZoom.MetresToAU>=bound+9.99,"Anonymous dock-group anchor did not use complete hull bounds");
        departing.TimeAdvance(0);
        Check(a.objSS.GetDistance(b.objSS)/HullZoom.MetresToAU>=HullDisplay.Radius(a)+HullDisplay.Radius(b)+9.99,"Native group propagation placed the lead hull inside its undock target");
        Check(child.objSS.GetDistance(b.objSS)/HullZoom.MetresToAU>=HullDisplay.Radius(child)+HullDisplay.Radius(b)+9.99,"Native group propagation placed an attached hull inside its undock target");
        departing.RemoveShip(a);departing.RemoveShip(child);HullDisplay.Retire(child);CrewSim.system.dictShips.Remove(child.strRegID);
    }
    static void LargeDockingRing(Harmony environment,GUIDockSys panel,Ship a,Ship b) {
        // A cold large hull uses header dimensions; this also exercises the
        // nonblocking native loading boundary without a second geometry scan.
        HullDisplay.Retire(a);HullDisplay.Retire(b);
        a.json.nCols=1000;a.json.nRows=600;b.json.nCols=600;b.json.nRows=300;
        double radius=HullDocking.DistanceAU(a,b)/HullZoom.MetresToAU;
        Check(radius>10000,"Large-ring fixture did not exceed native 10km normalization");
        SetApproach(a,b,radius*1.25);
        AccessTools.Method(typeof(GUIDockSys),"ConvertShipToRing").Invoke(panel,new object[]{b});
        Check(!float.IsNaN(ringFraction)&&ringFraction>0&&ringFraction<1,"Large hull docking ring has a positive monotonic approach window");
        HullDocking.DistanceAU(a,b);
        Check(AccessTools.Field(typeof(HullDisplay),"capture").GetValue(null)==null,"Docking query started an unbudgeted geometry capture");
        long allocated=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<10000;i++)HullDocking.DistanceAU(a,b);
        Check(GC.GetAllocatedBytesForCurrentThread()==allocated,"Cached docking bounds allocate per query");
        Field(a,"nLoadState",Ship.Loaded.Full);a.nCols=2000;a.nRows=1000;
        Check(HullDisplay.Radius(a)>Math.Sqrt(1000d*1000+600d*600)*20,"Cold materialized bounds used stale saved dimensions");
        Field(a,"nLoadState",Ship.Loaded.Shallow);
    }
}
