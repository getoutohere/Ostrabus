using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ostrabus.ShipNavigation;
using Ostranauts.Core;
using Ostranauts.Ships.Commands;
using Ostranauts.Ships;
using Ostranauts.ShipGUIs.Utilities;
using Ostranauts.Utils.Models;
using UnityEngine;

static partial class Program {
    const double RepairAU = AU * 1000;
    static void RepairField(object o,string name,object value) => AccessTools.Field(o.GetType(),name).SetValue(o,value);
    static bool repairThrowEscape;
    static bool RepairEscape(ref Point __result) {
        if(repairThrowEscape)throw new InvalidOperationException("fixture escape failure");
        __result=new Point(0,1/RepairAU);return false;
    }
    static bool RepairUpdate() => false;
    sealed class RepairLeaf : BaseCommand {
        internal int Calls;
        public override CommandStatus RunCommand(Blackboard _) {Calls++;return Running;}
    }
    static Ship RepairHull(string id,double x=0,double y=0,double vx=0,double vy=0,int radius=100) {
        var ship=Empty<Ship>();ship.strRegID=id;
        RepairField(ship,"<objSS>k__BackingField",new ShipSitu(false){vPosx=x/RepairAU,vPosy=y/RepairAU,vVelX=vx/RepairAU,vVelY=vy/RepairAU,Size=radius});
        RepairField(ship,"nLoadState",Ship.Loaded.Shallow);
        RepairField(ship,"aDocked",new Dictionary<string,Ship>());
        ship.json=new JsonShip{aItems=Array.Empty<JsonItem>()};return ship;
    }
    static int AvoidanceRepair() {
        var engine=new Harmony("offline.avoidance-repair.engine");var production=new Harmony("offline.avoidance-repair.production");
        var oldWorld=CrewSim.system;double oldMaximum=rcsMaximum;
        try {
            engine.Patch(AccessTools.Method(typeof(UnityEngine.Object),"op_Equality"),prefix:new HarmonyMethod(typeof(Program),nameof(Equal)));
            engine.Patch(AccessTools.Method(typeof(UnityEngine.Object),"op_Inequality"),prefix:new HarmonyMethod(typeof(Program),nameof(Unequal)));
            engine.Patch(AccessTools.Method(typeof(StarSystem),"GetNearestStationRegional"),prefix:new HarmonyMethod(typeof(Program),nameof(NearestStation)));
            engine.Patch(AccessTools.PropertyGetter(typeof(Ship),nameof(Ship.RCSAccelMax)),prefix:new HarmonyMethod(typeof(Program),nameof(Accel)));
            engine.Patch(AccessTools.Method(typeof(AIShipManager),"GetEscapePoint"),prefix:new HarmonyMethod(typeof(Program),nameof(RepairEscape)));
            engine.Patch(AccessTools.Method(typeof(ShipSitu),nameof(ShipSitu.UpdateTime)),prefix:new HarmonyMethod(typeof(Program),nameof(RepairUpdate)));
            CrewSim.system=Empty<StarSystem>();
            station=RepairHull("HOME",1e8);station.objSS.bIsBO=true;
            var us=RepairHull("DEPARTING");var them=RepairHull("OBSTACLE",1000);
            CrewSim.system.dictShips=new Dictionary<string,Ship>{{us.strRegID,us},{them.strRegID,them}};
            foreach(double speed in new[]{1d,5,10,50,100,149,150,200}) {
                us.objSS.vVelX=speed/RepairAU;
                Check(CollisionManager.WillCollide(us.objSS,them.objSS,2000)==(speed>=150),"Native low-speed control changed");
                Check(AvoidancePrediction.Collision(us.objSS,them.objSS,2000),"Slow head-on approach escaped corrected prediction");
            }
            var fixture=JObject.Parse(File.ReadAllText(Ostrabus.Development.InstallationPaths.Fixture("Navigation/AcceleratingDeparture.json")));
            us.objSS.vPosx=(double)fixture["relativePositionMetres"][0]/RepairAU;
            us.objSS.vPosy=(double)fixture["relativePositionMetres"][1]/RepairAU;
            us.objSS.vVelX=(double)fixture["relativeVelocityMetresPerSecond"][0]/RepairAU;
            us.objSS.vVelY=(double)fixture["relativeVelocityMetresPerSecond"][1]/RepairAU;
            us.objSS.Size=(int)fixture["movingNativeRadiusMetres"];
            them.objSS.vPosx=them.objSS.vPosy=0;them.objSS.Size=(int)fixture["obstacleNativeRadiusMetres"];
            rcsMaximum=(double)fixture["rcsMaximumMetresPerSecondSquared"]/RepairAU;
            Check(!AIShipManager.IsOnCollisionCourse(us,out _),"Captured native admission defect no longer reproduced");
            var expanded=new ShipSitu(us.objSS,false);expanded.Size*=2;
            Check(CollisionManager.WillCollide(expanded,them.objSS,2*(us.objSS.vVel-them.objSS.vVel).magnitude/rcsMaximum),"Captured native narrow predictor must detect the encounter");

            // Execute the actual native FlyTo branch dispatch before/after the repair.
            var fly=Empty<FlyTo>();var path=new RepairLeaf();var manual=new RepairLeaf();
            RepairField(fly,"_shipUs",us);RepairField(fly,"_flyToPath",path);RepairField(fly,"_flyToManual",manual);
            us.shipSituTarget=station.objSS;us.shipScanTarget=station;var board=new Blackboard();
            var route=Empty<NavData>();us.objSS.NavData=route;
            fly.RunCommand(board);
            Check(path.Calls==1&&manual.Calls==0&&ReferenceEquals(us.objSS.NavData,route),"Native FlyTo did not continue the unsafe route");
            production.CreateClassProcessor(typeof(AIShipAvoidancePatch)).Patch();
            Check(AIShipManager.IsOnCollisionCourse(us,out _),"Corrected AI caller missed the captured approach");
            fly.RunCommand(board);
            Check(path.Calls==1&&manual.Calls==1&&us.objSS.NavData==null,"Corrected native FlyTo failed to interrupt the unsafe route");
            us.objSS.vAccRCS=new Vector2((float)fixture["accelerationAUPerSecondSquared"][0],(float)fixture["accelerationAUPerSecondSquared"][1]);
            Check(AIShipManager.IsOnCollisionCourse(us,out _),"Applied captured acceleration hid the hazard");
            production.CreateClassProcessor(typeof(AvoidanceFlightPatch)).Patch();
            repairThrowEscape=true;
            bool escaped=false;
            try {fly.RunCommand(board);}catch(InvalidOperationException){escaped=true;}finally{repairThrowEscape=false;}
            Check(escaped&&AvoidanceQuery.Owner==null&&AvoidanceQuery.Route==null&&!AvoidanceQuery.Admission,"Exceptional FlyTo/avoidance leaked scoped actor/route state");

            GeometryRepairChecks();
            RouteRepairChecks();
            // A clear pass resumes the existing route branch; no replacement mission/target.
            us.objSS.vAccRCS=default;us.objSS.vVelX=us.objSS.vVelY=0;
            them.objSS.vPosx=1;them.objSS.vPosy=0;us.objSS.NavData=route;
            fly.RunCommand(board);
            Check(path.Calls==2&&manual.Calls==1&&ReferenceEquals(us.objSS.NavData,route)&&ReferenceEquals(us.shipScanTarget,station),"Clearance did not resume native navigation toward the original target");
            Check(!CollisionManager.WillCollide(RepairHull("A").objSS,RepairHull("B",1000,0,-50).objSS,2000),"Shared projectile/history predictor was modified");
            Console.WriteLine(checks+" avoidance repair checks passed: native failure controls, captured approach, actual FlyTo interruption/resumption, low-speed/accelerating geometry, unchanged shared predicate and zero warmed query allocation.");
            return 0;
        } finally {production.UnpatchSelf();engine.UnpatchSelf();CrewSim.system=oldWorld;rcsMaximum=oldMaximum;station=null;}
    }
    static void RouteRepairChecks() {
        var us=RepairHull("ROUTED");var obstacle=RepairHull("CROSSING",5000);
        StarSystem.fEpoch=100000;
        NavData Route(params NavDataPoint[] points) {
            var route=new NavData(us);foreach(var point in points)route.AddNavDataPoint(point);return route;
        }
        NavDataPoint At(double seconds,double x,double y=0) => new NavDataPoint(StarSystem.fEpoch+seconds,RepairHull("POINT",x,y).objSS);
        var unsafeCourse=Route(At(0,0),At(60,5000),At(120,10000));
        Check(AvoidanceRouteForecast.TryCollision(us.objSS,obstacle.objSS,30,unsafeCourse,null,out bool hit)&&!hit,"Ordinary route check exceeded bounded lookahead");
        var flight=AvoidanceQuery.BeginFlight(us);
        Check(AvoidanceQuery.Candidate(us,unsafeCourse),"New candidate was not admitted for inspection");
        var query=AvoidanceQuery.BeginQuery(us);
        try {
            Check(AvoidanceQuery.Admission&&ReferenceEquals(AvoidanceQuery.Route,unsafeCourse),"Candidate inspection lost its route");
            Check(AvoidanceRouteForecast.TryCollision(us.objSS,obstacle.objSS,30,unsafeCourse,null,out hit)&&hit,"New course was accepted with a later obstacle crossing");
            obstacle.objSS.vPosx=200000/RepairAU;
            var distantCourse=Route(At(0,0),At(1200,200000),At(2400,400000));
            Check(AvoidanceRouteForecast.TryCollision(us.objSS,obstacle.objSS,30,distantCourse,null,out hit)&&!hit,"Distant speculative encounter triggered local evasion");
            obstacle.objSS.vPosx=5000/RepairAU;
            var anchor=new ShipSitu(us.objSS,false);anchor.vPosy=5000/RepairAU;
            Check(AvoidanceRouteForecast.TryCollision(anchor,obstacle.objSS,30,unsafeCourse,null,out hit,us.objSS)&&!hit,"Group-anchor offset was replaced by controlling-hull route coordinates");
            var clear=Route(At(0,0),At(10,2000,4000),At(60,30000,4000),At(120,40000));
            us.objSS.vAccRCS=new Vector2((float)(20/RepairAU),0);
            Check(AvoidanceRouteForecast.TryCollision(us.objSS,obstacle.objSS,30,clear,null,out hit)&&!hit,"Old acceleration overrode a clear native route");
            for(int i=0;i<20;i++)AvoidanceRouteForecast.TryCollision(us.objSS,obstacle.objSS,30,clear,null,out _);
            long allocated=GC.GetAllocatedBytesForCurrentThread();
            var clock=System.Diagnostics.Stopwatch.StartNew();
            allocated=GC.GetAllocatedBytesForCurrentThread();
            for(int i=0;i<1000;i++)AvoidanceRouteForecast.TryCollision(us.objSS,obstacle.objSS,30,clear,null,out _);
            Check(GC.GetAllocatedBytesForCurrentThread()==allocated,"Route inspection allocated per prediction");
            clock.Stop();Console.WriteLine("1000 complete candidate route checks: "+clock.Elapsed.TotalMilliseconds+" ms; zero warmed allocation.");
        }finally{AvoidanceQuery.EndQuery(query);AvoidanceQuery.EndFlight(flight);}
        Check(!AvoidanceQuery.Admission&&AvoidanceQuery.Route==null,"Candidate scope retained route/actor state");

        us.objSS.vAccRCS=default;us.objSS.vVelY=20/RepairAU;
        var native=new Point(0,-5000/RepairAU);
        var positive=AIShipAvoidance.CurrentMotionEscape(native,us,obstacle.objSS);
        us.objSS.vAccRCS=new Vector2(0,(float)(-20/RepairAU));
        var reversedThrust=AIShipAvoidance.CurrentMotionEscape(native,us,obstacle.objSS);
        Check(positive.Y>0&&positive.Y==reversedThrust.Y,"Escape reversed with the thrust it replaces");
        us.objSS.vVelY=-20/RepairAU;
        Check(AIShipAvoidance.CurrentMotionEscape(native,us,obstacle.objSS).Y<0,"Mirrored lateral escape did not preserve relative motion");

        // A manufactured reference is deliberately distinct from the natural
        // body's origin and the stored waypoint coordinates. Reading the wrong
        // reference or the stale stored point would miss this overlap.
        var body=Empty<BodyOrbit>();body.strName="ORBITAL-REF";body.nDrawFlagsBody=1;
        body.fAxis1=body.fAxis2=2;body.fPerh=body.fAph=1;body.fPeriod=1e30;body.nOrbitDirection=1;
        body.dTimeCalcLast=123;body.dVelCalcLast=456;
        var oldBodies=CrewSim.system.aBOs;CrewSim.system.aBOs=new Dictionary<string,BodyOrbit>{{body.strName,body}};
        try {
            var first=At(0,0);var last=At(30,0);
            foreach(var point in new[]{first,last}) {point.ObjSS.bIsBO=true;point.ObjSS.strBOPORShip=body.strName;}
            var locked=Route(first,last);obstacle.objSS.vPosx=1;obstacle.objSS.vPosy=0;
            Check(AvoidanceRouteForecast.TryCollision(us.objSS,obstacle.objSS,30,locked,null,out hit)&&hit,"Manufactured route reference identity was replaced/ignored");
            Check(body.dTimeCalcLast==123&&body.dVelCalcLast==456,"Route prediction modified live body caches");
        }finally{CrewSim.system.aBOs=oldBodies;}
    }
    static void GeometryRepairChecks() {
        bool Hit(double x,double y,double vx,double vy,double ax=0,double ay=0,double time=30) {
            var a=RepairHull("A",x,y,vx,vy);var b=RepairHull("B");
            a.objSS.vAccRCS=new Vector2((float)(ax/RepairAU),(float)(ay/RepairAU));
            return AvoidancePrediction.Collision(a.objSS,b.objSS,time);
        }
        Check(Hit(1000,0,0,0,-5),"Departure from rest was not forecast");
        Check(Hit(1000,0,10,0,-5),"Acceleration reversing an initially separating encounter was discarded");
        Check(!Hit(1000,0,10,0,5),"Accelerating away produced a false warning");
        Check(!Hit(1000,0,0,0),"Separated stationary hulls produced a warning");
        Check(Hit(100,0,10,0),"Existing overlap lost its warning");
        Check(!Hit(1000,0,50,0),"Past-only encounter was restored");
        Check(Hit(-1000,0,50,0),"Slow closing encounter was missed");
        Check(!Hit(-1000,250,50,0),"Harmless crossing was flagged");
        Check(!Hit(-1000,0,50,0,0,10),"Acceleration turning a crossing clear was ignored");
        Check(Hit(200.01,0,-.001,0),"Numerically small relative speed was treated as zero");
        Check(Hit(-100000,0,1000,0,0,0,200),"Native long braking-time horizon was shortened");
        Check(Hit(-100000,0,50,0,0,0,double.PositiveInfinity),"Zero available braking acceleration lost its native unlimited coasting forecast");
        var a=RepairHull("A",1000,0,0,0);var b=RepairHull("B");
        a.objSS.vAccRCS=new Vector2((float)(-5/RepairAU),0);
        var acceleration=a.objSS.vAccRCS;
        for(int i=0;i<100;i++)AvoidancePrediction.Collision(a.objSS,b.objSS,30);
        long allocated=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<10000;i++)AvoidancePrediction.Collision(a.objSS,b.objSS,30);
        Check(GC.GetAllocatedBytesForCurrentThread()==allocated,"Warmed acceleration prediction allocates");
        Check(a.objSS.vPosx==1000/RepairAU&&a.objSS.vVelX==0&&a.objSS.vAccRCS==acceleration&&a.objSS.Size==100,"Prediction mutated actor state");
        a.objSS.vVelX=b.objSS.vVelX=50000/RepairAU;
        Check(AvoidancePrediction.Collision(a.objSS,b.objSS,30),"Common heliocentric velocity changed the acceleration forecast");
    }
}
