using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using BepInEx.Configuration;
using Ostrabus.NavConsole.Outlines;
using Ostrabus.ShipNavigation.Collisions;
using Ostranauts.ShipGUIs.NavStation;
using Ostranauts.ShipGUIs.Utilities;
using Ostranauts.Utils.Models;
using UnityEngine;

static partial class Program {
    static int placementRandomCalls;
    static bool placementThrow;
    static Ship avoidanceStation;
    static bool PlacementRandom(float minInclusive,float maxInclusive,ref float __result) {
        placementRandomCalls++;__result=(minInclusive+maxInclusive)*.5f;return false;
    }
    static bool PlacementNearest(ref BodyOrbit __result) {
        if (placementThrow) throw new InvalidOperationException("fixture safe-position exception");
        __result=dockingFarBody;return false;
    }
    static bool AvoidanceStation(ref Ship __result) {__result=avoidanceStation;return false;}
    static bool AvoidanceAcceleration(ref double __result) {__result=1e-10;return false;}
    static bool IgnoreFixtureLog() => false;
    static bool NativePlacementRadius(ShipSitu situ,float nativeOther,float nativeMoving,ref float __result) {__result=nativeOther+nativeMoving;return false;}
    static void ConsumerCases() {
        int before=checks;
        var world=CrewSim.system;var flag=PhysicalCollisions.Flag;var selected=GUIOrbitDraw.CrossHairTarget;
        var production=new Harmony("offline.hull.consumers.production");var environment=new Harmony("offline.hull.consumers.environment");
        try {
            HullDisplay.Reset();PhysicalCollisions.Flag=Empty<ConfigEntry<bool>>();Field(PhysicalCollisions.Flag,"_typedValue",true);
            var a=CollisionHull("CONSUMER-A",0,0);var b=CollisionHull("CONSUMER-B",0,0);
            var ga=new EnvelopeBuilder().Build(new[]{new HullPoint(-80,-4),new HullPoint(80,-4),new HullPoint(80,4),new HullPoint(-80,4)},4,null);
            var gb=new EnvelopeBuilder().Build(new[]{new HullPoint(-4,-20),new HullPoint(4,-20),new HullPoint(4,20),new HullPoint(-4,20)},4,null);
            WarmCollision(a,ga,true);WarmCollision(b,gb,true);
            CrewSim.system=Empty<StarSystem>();CrewSim.system.dictShips=new Dictionary<string,Ship>{{a.strRegID,a},{b.strRegID,b}};
            dockingFarBody.dXReal=dockingFarBody.dYReal=1;
            environment.Patch(AccessTools.Method(typeof(StarSystem),nameof(StarSystem.GetNearestBO)),prefix:new HarmonyMethod(typeof(Program),nameof(PlacementNearest)));
            environment.Patch(AccessTools.Method(typeof(UnityEngine.Random),nameof(UnityEngine.Random.Range),new[]{typeof(float),typeof(float)}),prefix:new HarmonyMethod(typeof(Program),nameof(PlacementRandom)));
            environment.Patch(AccessTools.Method(typeof(Debug),nameof(Debug.Log),new[]{typeof(object)}),prefix:new HarmonyMethod(typeof(Program),nameof(IgnoreFixtureLog)));
            environment.Patch(AccessTools.PropertyGetter(typeof(Ship),nameof(Ship.Mass)),prefix:new HarmonyMethod(typeof(Program),nameof(DockingMass)));
            environment.Patch(AccessTools.Method(typeof(CrewSim),nameof(CrewSim.GetSelectedCrew)),prefix:new HarmonyMethod(typeof(Program),nameof(NoDockingCrew)));
            double radius=(double)ga.MapRadius+gb.MapRadius;
            SetApproach(a,b,radius*.75);var old=a.objSS.vPos;
            CrewSim.system.SetSituToSafeCoords(a.objSS);
            Check((a.objSS.vPos-old).magnitude==0,"Native safe-placement fixture already recognized enlarged bounds");
            Check(CrewSim.system.SetSituToRandomSafeCoords(a.objSS,radius*.75*HullZoom.MetresToAU,radius*.75*HullZoom.MetresToAU,0,0,suppressLog:true),"Native spawn fixture did not reproduce an undersized safe search");
            production.CreateClassProcessor(typeof(HullSafePlacementPatch)).Patch();
            SetApproach(a,b,radius*.75);placementRandomCalls=0;
            CrewSim.system.SetSituToSafeCoords(a.objSS);
            Check(a.objSS.GetDistance(b.objSS)/HullZoom.MetresToAU>radius,"Native safe search accepted enlarged circles overlapping");
            Check(placementRandomCalls>0,"Native safe search retry was not executed");
            Check(!CrewSim.system.SetSituToRandomSafeCoords(a.objSS,radius*.75*HullZoom.MetresToAU,radius*.75*HullZoom.MetresToAU,0,0,suppressLog:true),"Random spawn accepted only unsafe candidates");
            Check(!CrewSim.system.SetSituToRandomSafeCoordsWithinBelt(a.objSS,radius*.75*HullZoom.MetresToAU,radius*.75*HullZoom.MetresToAU,new Point(0,0)),"Belt spawn accepted only unsafe candidates");
            foreach(var kind in new[]{Ship.TypeClassification.OrbitalStation,Ship.TypeClassification.Asteroid,Ship.TypeClassification.Projectile}) {
                b.Classification=kind;SetApproach(a,b,radius*.75);old=a.objSS.vPos;
                CrewSim.system.SetSituToSafeCoords(a.objSS);
                Check((a.objSS.vPos-old).magnitude==0,"Safe search changed an excluded mixed pair: "+kind);
                Check(CrewSim.system.SetSituToRandomSafeCoords(a.objSS,radius*.75*HullZoom.MetresToAU,radius*.75*HullZoom.MetresToAU,0,0,suppressLog:true),"Spawn search changed an excluded mixed pair: "+kind);
            }
            b.Classification=Ship.TypeClassification.Default;b.objSS.bIsBO=true;SetApproach(a,b,radius*.75);old=a.objSS.vPos;
            CrewSim.system.SetSituToSafeCoords(a.objSS);Check((a.objSS.vPos-old).magnitude==0,"Safe search changed a body object's mixed pair");b.objSS.bIsBO=false;
            a.Classification=Ship.TypeClassification.Projectile;SetApproach(a,b,radius*.75);old=a.objSS.vPos;
            CrewSim.system.SetSituToSafeCoords(a.objSS);Check((a.objSS.vPos-old).magnitude==0,"Safe search enlarged an excluded moving object");a.Classification=Ship.TypeClassification.Default;
            Field(PhysicalCollisions.Flag,"_typedValue",false);SetApproach(a,b,radius*.75);old=a.objSS.vPos;
            CrewSim.system.SetSituToSafeCoords(a.objSS);Check((a.objSS.vPos-old).magnitude==0,"Disabled native safe search changed position");
            Field(PhysicalCollisions.Flag,"_typedValue",true);
            placementThrow=true;bool threw=false;
            try {CrewSim.system.SetSituToSafeCoords(a.objSS);} catch(InvalidOperationException error) {threw=error.Message=="fixture safe-position exception";}
            finally {placementThrow=false;}
            var state=(HullPlacement.State)AccessTools.Field(typeof(HullPlacement),"current").GetValue(null);
            var radii=(Dictionary<ShipSitu,float>)AccessTools.Field(typeof(HullPlacement),"radii").GetValue(null);
            Check(threw&&!state.Active&&state.Moving==null&&state.Group==null&&radii.Count==0,"Safe-search exception retained ship/group references");
            production.CreateClassProcessor(typeof(HullDockingGeometryPatch)).Patch();
            production.CreateClassProcessor(typeof(HullDockingHandoffPatch)).Patch();
            ConsumerGroupPushback(a,b,radius,environment);
            ConsumerMerge(a,b);
            ConsumerPlacementHistory(a,b,environment);
            ConsumerMooring(a,b,radius);
            ConsumerInspection(a,b);
            ConsumerAvoidance(a,b,ga,gb,production,environment);
            Console.WriteLine("Hull consumer audit: "+(checks-before)+" native placement/spawn/group-pushback/mooring/AI-prediction checks passed.");
        } finally {
            placementThrow=false;avoidanceStation=null;production.UnpatchSelf();environment.UnpatchSelf();
            GUIOrbitDraw.CrossHairTarget=selected;CrewSim.system=world;PhysicalCollisions.Flag=flag;HullDisplay.Reset();
        }
    }
    static void ConsumerInspection(Ship a,Ship b) {
        var inspection=Empty<Ostranauts.Ships.Commands.ShakeDown>();Field(inspection,"_shipUs",a);
        var handoff=AccessTools.Method(typeof(Ostranauts.Ships.Commands.BaseCommand),"GetHandOffDistance");
        double accepted=(double)handoff.Invoke(inspection,new object[]{(double)CollisionManager.GetCollisionDistanceAU(a,b),b});
        float native=(float)Math.Max(2*CollisionManager.GetCollisionDistanceAU(a,b),3.3422936e-8f);
        Check(accepted>native,"Large-hull police fixture did not reproduce the native inspection/flight handoff gap");
        float range=HullDocking.InspectionRange(native,inspection,b);
        Check(accepted-6.684587e-14<range,"Police inspection cannot accept the FlyTo handoff for a large hull");
        Field(PhysicalCollisions.Flag,"_typedValue",false);
        Check(HullDocking.InspectionRange(native,inspection,b)==native,"Disabled police inspection range was modified");
        Field(PhysicalCollisions.Flag,"_typedValue",true);
    }
    static void ConsumerPlacementHistory(Ship a,Ship b,Harmony environment) {
        double epoch=StarSystem.fEpoch;
        try {
            PhysicalCollisions.Reset();a.PlaceAtAirlocks("merge-a","merge-b",b);
            var end=a.objSS.vPos;var difference=end-b.objSS.vPos;
            a.objSS.vPosx=b.objSS.vPosx-difference.X;a.objSS.vPosy=b.objSS.vPosy-difference.Y;
            a.objSS.vVelX=a.objSS.vVelY=b.objSS.vVelX=b.objSS.vVelY=0;
            StarSystem.fEpoch=1000;
            Check(!PhysicalCollisions.Overlap(a,b,NativeDistance(a,b),CollisionManager.GetCollisionDistanceAU(a,b)),"Instant-placement history fixture begins inside a hull");
            StarSystem.fEpoch++;
            var invalidation=AccessTools.Method(typeof(PhysicalCollisions),nameof(PhysicalCollisions.Displaced));
            environment.Patch(invalidation,prefix:new HarmonyMethod(typeof(Program),nameof(Skip)));
            a.PlaceAtAirlocks("merge-a","merge-b",b);
            Check(PhysicalCollisions.Overlap(a,b,NativeDistance(a,b),CollisionManager.GetCollisionDistanceAU(a,b)),"Missing-invalidation control did not reproduce an artificial swept impact");
            environment.Unpatch(invalidation,HarmonyPatchType.Prefix,environment.Id);
            PhysicalCollisions.Reset();
            a.objSS.vPosx=b.objSS.vPosx-difference.X;a.objSS.vPosy=b.objSS.vPosy-difference.Y;
            PhysicalCollisions.Overlap(a,b,NativeDistance(a,b),CollisionManager.GetCollisionDistanceAU(a,b));StarSystem.fEpoch++;
            a.PlaceAtAirlocks("merge-a","merge-b",b);
            Check((a.objSS.vPos-end).magnitude<1e-14,"Native instant placement changed destination between replays");
            Check(!PhysicalCollisions.Overlap(a,b,NativeDistance(a,b),CollisionManager.GetCollisionDistanceAU(a,b)),"Native placement was replayed as a swept journey through its target");
        } finally {StarSystem.fEpoch=epoch;PhysicalCollisions.Reset();}
    }
    static void ConsumerMerge(Ship a,Ship b) {
        a.json.aItems=new[]{new JsonItem{strID="merge-a",fRotation=180}};
        b.json.aItems=new[]{new JsonItem{strID="merge-b",fRotation=90}};
        var left=CollisionHull("MERGE-LEFT",0,0);var right=CollisionHull("MERGE-RIGHT",0,0);
        var hull=new Envelope(new[]{new HullPoint(-20,-2),new HullPoint(20,-2),new HullPoint(20,2),new HullPoint(-20,2)},4);
        WarmCollision(left,hull,true);WarmCollision(right,hull,true);
        left.objSS.vPosx=a.objSS.vPosx-3000*HullZoom.MetresToAU;left.objSS.vPosy=a.objSS.vPosy;
        right.objSS.vPosx=b.objSS.vPosx+3000*HullZoom.MetresToAU;right.objSS.vPosy=b.objSS.vPosy;
        CrewSim.system.dictShips.Add(left.strRegID,left);CrewSim.system.dictShips.Add(right.strRegID,right);
        var first=new SolarSystem.DockGroup();first.AddShip(a);first.AddShip(left);
        var second=new SolarSystem.DockGroup();second.AddShip(b);second.AddShip(right);
        double ourDistance=a.objSS.GetDistance(left.objSS),theirDistance=b.objSS.GetDistance(right.objSS);
        a.objSS.PushbackFrom(b);first.TimeAdvance(0);
        foreach(var us in new[]{a,left})foreach(var them in new[]{b,right})
            Check(us.objSS.GetDistance(them.objSS)/HullZoom.MetresToAU>HullDisplay.Radius(us)+HullDisplay.Radius(them),"Group undocking overlaps a member of the other remaining group");
        first.MergeTargetGroupIntoUs(b,"merge-b",a,"merge-a");
        foreach(var us in new[]{a,left})foreach(var them in new[]{b,right})
            Check(us.objSS.GetDistance(them.objSS)/HullZoom.MetresToAU>HullDisplay.Radius(us)+HullDisplay.Radius(them),"Native formation merge overlaps an attached hull");
        Check(Math.Abs(a.objSS.GetDistance(left.objSS)-ourDistance)<1e-14&&Math.Abs(b.objSS.GetDistance(right.objSS)-theirDistance)<1e-14,"Hull bounds changed pre-existing intra-group offsets");
        Check(ReferenceEquals(a.DockGroup,b.DockGroup)&&a.DockGroup.DockedShips.Count==4,"Native merge did not retain all four members");
        float radius=HullDocking.DistanceAU(a,b);
        for(int i=0;i<10;i++)Check(HullDocking.DistanceAU(a,b)==radius,"Shared group radius compounded between queries");
        foreach(var ship in new[]{a,b,left,right})first.RemoveShip(ship);
        HullDisplay.Retire(left);HullDisplay.Retire(right);CrewSim.system.dictShips.Remove(left.strRegID);CrewSim.system.dictShips.Remove(right.strRegID);
    }
    static void ConsumerGroupPushback(Ship a,Ship b,double radius,Harmony environment) {
        SetApproach(a,b,radius+10);
        var child=CollisionHull("CONSUMER-CHILD",0,0);child.objSS.vPosx=a.objSS.vPosx+500*HullZoom.MetresToAU;child.objSS.vPosy=a.objSS.vPosy;
        WarmCollision(child,new Envelope(new[]{new HullPoint(-2,-2),new HullPoint(2,-2),new HullPoint(2,2),new HullPoint(-2,2)},4),true);
        CrewSim.system.dictShips.Add(child.strRegID,child);
        var group=new SolarSystem.DockGroup();group.AddShip(a);group.AddShip(child);
        group.Anchor.PlaceOrbitPosition(b.objSS);group.TimeAdvance(0);
        double bound=HullDocking.Bound(a,group.Anchor)+HullDisplay.Radius(b);
        var saved=group.Anchor.vPos;
        var radiusRead=AccessTools.Method(typeof(HullPlacement),nameof(HullPlacement.RadiusSum));
        environment.Patch(radiusRead,prefix:new HarmonyMethod(typeof(Program),nameof(NativePlacementRadius)));
        placementRandomCalls=0;a.objSS.PushbackFrom(b);
        Check(placementRandomCalls>0,"Native safe search control did not retry against its own group members");
        environment.Unpatch(radiusRead,HarmonyPatchType.Prefix,environment.Id);
        group.Anchor.vPosx=saved.X;group.Anchor.vPosy=saved.Y;group.TimeAdvance(0);
        var position=group.Anchor.vPos;placementRandomCalls=0;
        a.objSS.PushbackFrom(b);
        Check(placementRandomCalls==0,"Native group pushback randomly avoids its own attached members");
        Check((group.Anchor.vPos-position).magnitude/HullZoom.MetresToAU<.05,"Group safe search or stale member positions moved an already-clear anchor");
        Check(Math.Abs(group.Anchor.GetDistance(b.objSS)/HullZoom.MetresToAU-bound-10)<.05,"Group pushback did not preserve its complete cached bounds");
        group.TimeAdvance(0);
        foreach(var member in group.DockedShips)Check(member.Ship.objSS.GetDistance(b.objSS)/HullZoom.MetresToAU>HullDisplay.Radius(member.Ship)+HullDisplay.Radius(b),"Real pushback/propagation overlaps an attached hull");
        group.RemoveShip(a);group.RemoveShip(child);CrewSim.system.dictShips.Remove(child.strRegID);HullDisplay.Retire(child);
    }
    static void ConsumerMooring(Ship a,Ship b,double radius) {
        var owner=Empty<CondOwner>();Field(owner,"_objShip",a);var panel=Empty<NavModMooringControl>();Field(panel,"COSelf",owner);
        var poi=Empty<NavPOI>();Field(poi,"_ship",b);GUIOrbitDraw.CrossHairTarget=poi;
        a.objSS.vVelX=a.objSS.vVelY=b.objSS.vVelX=b.objSS.vVelY=0;
        var canMoor=AccessTools.Method(typeof(NavModMooringControl),"CanMoor");
        SetApproach(a,b,radius+100);
        Field(PhysicalCollisions.Flag,"_typedValue",false);Check(!(bool)canMoor.Invoke(panel,null),"Native tether fixture was already in range");
        Field(PhysicalCollisions.Flag,"_typedValue",true);Check((bool)canMoor.Invoke(panel,null),"Tethering still requires entering enlarged hulls");
        SetApproach(a,b,radius+2501);Check(!(bool)canMoor.Invoke(panel,null),"Native 2.5km tether margin was expanded");
        SetApproach(a,b,radius+100);a.objSS.vVelX=51*HullZoom.MetresToAU;
        Check(!(bool)canMoor.Invoke(panel,null),"Native tether relative-velocity restriction was bypassed");
    }
    static void ConsumerAvoidance(Ship a,Ship b,Envelope ga,Envelope gb,Harmony production,Harmony environment) {
        a.objSS.vPosx=-10000*HullZoom.MetresToAU;a.objSS.vPosy=0;
        b.objSS.vPosx=0;b.objSS.vPosy=(ga.MapRadius*2+gb.MapRadius)*.5*HullZoom.MetresToAU;
        a.objSS.vVelX=1000*HullZoom.MetresToAU;a.objSS.vVelY=b.objSS.vVelX=b.objSS.vVelY=0;
        avoidanceStation=b;
        environment.Patch(AccessTools.Method(typeof(StarSystem),nameof(StarSystem.GetNearestStationRegional)),prefix:new HarmonyMethod(typeof(Program),nameof(AvoidanceStation)));
        environment.Patch(AccessTools.PropertyGetter(typeof(Ship),nameof(Ship.RCSAccelMax)),prefix:new HarmonyMethod(typeof(Program),nameof(AvoidanceAcceleration)));
        Check(!AIShipManager.IsOnCollisionCourse(a,out _),"Native AI avoidance fixture already caught the enlarged hull");
        production.CreateClassProcessor(typeof(HullAvoidancePatch)).Patch();
        Check(AIShipManager.IsOnCollisionCourse(a,out _),"Native AI predictor still steers through enlarged hull bounds");
        var state=(HullAvoidance.State)AccessTools.Field(typeof(HullAvoidance),"current").GetValue(null);
        Check(state.Us==null&&state.Them==null,"AI prediction scope retained live actor situations");
        Check(!CollisionManager.WillCollide(a.objSS,b.objSS,30)&&a.objSS.Size==100&&b.objSS.Size==100,"AI hull prediction changed shared combat radius/state");
        Field(PhysicalCollisions.Flag,"_typedValue",false);Check(!AIShipManager.IsOnCollisionCourse(a,out _),"Disabled AI avoidance did not restore native bounds");
        Field(PhysicalCollisions.Flag,"_typedValue",true);
        var copy=new ShipSitu(a.objSS,false);copy.Size*=2;
        Check(HullAvoidance.PredictNative(copy,b.objSS,30,a,b),"Native wrapper missed approaching hull");
        a.objSS.vVelX=copy.vVelX=-1000*HullZoom.MetresToAU;
        Check(!HullAvoidance.PredictFuture(copy,b.objSS,30,a,b),"Expanded hull predictor revived a past-only encounter");
        a.objSS.vVelX=copy.vVelX=1000*HullZoom.MetresToAU;
        for(int i=0;i<100;i++)HullAvoidance.PredictFuture(copy,b.objSS,30,a,b);
        long allocated=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<10000;i++)HullAvoidance.PredictFuture(copy,b.objSS,30,a,b);
        Check(GC.GetAllocatedBytesForCurrentThread()==allocated,"Warmed hull prediction allocates per candidate");
        HullAvoidance.End(default);
    }
}
