using System;
using System.Collections.Generic;
using HarmonyLib;
using Ostrabus.Orbital;
using Ostrabus.ShipNavigation;
using Ostranauts.Utils.Models;
using SolarSystem;

static partial class Program {
    static void Near(double value,double wanted,string reason,double tolerance=1e-7) => Check(Math.Abs(value-wanted)<=tolerance,reason+": "+value+" != "+wanted);
    static double Minimum(double px,double py,double vx,double vy,double ax,double ay,double seconds) => ConstantAccelerationApproach.MinimumSquared(new DVector(px,py),new DVector(vx,vy),new DVector(ax,ay),seconds);
    static Ship GeometryShip(double x,double y,int size=200) {
        var ship=Empty<Ship>();ship.strRegID="TEST";
        AccessTools.Field(typeof(Ship),"<objSS>k__BackingField").SetValue(ship,new ShipSitu(false){vPosx=x/AU,vPosy=y/AU,Size=size});
        return ship;
    }
    static void GeometryChecks() {
        Near(Minimum(5,2,-1,0,0,0,10),4,"Straight closest approach");
        Near(Minimum(5,2,1,0,0,0,10),29,"Separating object must not use past minimum");
        Near(Minimum(1,2,0,0,0,0,60),5,"Stationary point");
        Near(Minimum(3,0,-4,0,2,0,4),0,"Multiple extrema/zero crossings");
        Near(Minimum(3,2,-4,0,2,0,4),4,"Offset quadratic closest approach");
        Near(Minimum(3,2,-4,0,2,0,0),13,"Zero horizon");
        var random=new Random(7321);
        for(int scenario=0;scenario<800;scenario++) {
            double px=random.NextDouble()*200-100,py=random.NextDouble()*200-100;
            double vx=random.NextDouble()*20-10,vy=random.NextDouble()*20-10;
            double ax=random.NextDouble()-0.5,ay=random.NextDouble()-0.5,seconds=random.NextDouble()*60;
            double exact=Minimum(px,py,vx,vy,ax,ay,seconds),sampled=double.MaxValue;
            const int samples=2048;
            for(int i=0;i<=samples;i++) {double t=seconds*i/samples,x=px+vx*t+.5*ax*t*t,y=py+vy*t+.5*ay*t*t;sampled=Math.Min(sampled,x*x+y*y);}
            double speed=Math.Sqrt(vx*vx+vy*vy),acc=Math.Sqrt(ax*ax+ay*ay),r=Math.Sqrt(px*px+py*py);
            double curvature=Math.Pow(speed+acc*seconds,2)+(r+speed*seconds+.5*acc*seconds*seconds)*acc;
            double error=curvature*Math.Pow(seconds/samples,2)+1e-7*Math.Max(1,sampled);
            Check(exact>=0 && exact<=sampled+1e-7*Math.Max(1,sampled) && sampled-exact<=error,"Analytic minimum disagrees with dense independent oracle");
        }
        var world=Empty<StarSystem>();CrewSim.system=world;
        var body=Empty<BodyOrbit>();body.strName="TEST-BODY";body.fRadius=10/AU;body.fPeriod=1;body.nOrbitDirection=1;
        world.aBOs=new Dictionary<string,BodyOrbit>{{body.strName,body}};StarSystem.fEpoch=0;
        var ship=GeometryShip(12,0);var obstacle=new ShipSitu(false){vPosx=12/AU,vPosy=5/AU,Size=200};
        var inward=new Point(7/AU,0);var outward=new Point(17/AU,0);
        var chosen=AIShipAvoidance.SelectEscape(inward,ship,obstacle,30);
        Near(chosen.X*AU,17,"Unsafe planetary dodge was not reversed");
        Near(AIShipAvoidance.SelectEscape(outward,ship,obstacle,30).X*AU,17,"Safe native dodge changed");
        obstacle.vPosx=20/AU;obstacle.vPosy=0;
        Near(AIShipAvoidance.SelectEscape(inward,ship,obstacle,30).X*AU,7,"Alternate dodge into a real obstacle was accepted");
        obstacle.vPosx=12/AU;obstacle.vPosy=5/AU;
        obstacle.vPosx=20/AU;obstacle.vPosy=20/AU;
        obstacle.vVelX=(12+.5*(rcsMaximum*AU*.5)*400-20)/20/AU;obstacle.vVelY=-1/AU;
        Near(AIShipAvoidance.SelectEscape(inward,ship,obstacle,30).X*AU,7,"Alternate dodge into moving traffic was accepted");
        obstacle.vPosx=12/AU;obstacle.vPosy=5/AU;obstacle.vVelX=obstacle.vVelY=0;
        rcsMaximum=0;
        Near(AIShipAvoidance.SelectEscape(inward,ship,obstacle,30).X*AU,7,"Unpowered steering was replaced");rcsMaximum=1.9016982118751132E-10;
        var moon=Empty<BodyOrbit>();moon.strName="MOON";moon.fAxis1=moon.fAxis2=40/AU;moon.fPerh=20/AU;moon.fPeriod=1e9;moon.fRadius=1/AU;moon.nOrbitDirection=1;
        world.aBOs.Add(moon.strName,moon);
        Near(AIShipAvoidance.SelectEscape(inward,ship,obstacle,30).X*AU,7,"Alternative into a second body was accepted");world.aBOs.Remove(moon.strName);
        body.aAtmospheres=new[]{new JsonAtmosphere{fMaxAltitude=1000}};
        Near(AIShipAvoidance.SelectEscape(outward,ship,obstacle,30).X*AU,17,"Atmosphere boundary was treated as solid surface");body.aAtmospheres=null;
        ship.objSS.bGrounded=true;
        Near(AIShipAvoidance.SelectEscape(inward,ship,obstacle,30).X*AU,7,"Grounded flight changed");ship.objSS.bGrounded=false;
        body.nDrawFlagsBody=1;
        Near(AIShipAvoidance.SelectEscape(inward,ship,obstacle,30).X*AU,7,"Station placeholder treated as solid planet");body.nDrawFlagsBody=0;
        ship.objSS.vPosx=0;
        Near(AIShipAvoidance.SelectEscape(new Point(-5/AU,0),ship,obstacle,30).X*AU,-5,"Both unsafe alternatives should retain native handling");
        ship.objSS.vPosx=12/AU;
        var group=new DockGroup{Anchor=new ShipSitu(false){vPosx=12/AU,Size=5000}};ship.objSS._dockGroup=group;
        Near(AIShipAvoidance.SelectEscape(inward,ship,obstacle,30).X*AU,7,"Connected hull envelope ignored");ship.objSS._dockGroup=null;
        Near(AIShipAvoidance.SelectEscape(inward,ship,obstacle,float.NaN).X*AU,7,"Nonfinite prediction changed steering");
        double epoch=body.dTimeCalcLast,velocityEpoch=body.dVelCalcLast;
        for(int i=0;i<20;i++)AIShipAvoidance.SelectEscape(inward,ship,obstacle,30);
        long allocated=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<1000;i++)AIShipAvoidance.SelectEscape(inward,ship,obstacle,30);
        Check(GC.GetAllocatedBytesForCurrentThread()==allocated,"Evasion helpers allocate in steady state");
        Check(body.dTimeCalcLast==epoch && body.dVelCalcLast==velocityEpoch,"Read-only ephemeris prediction changed native body caches");
        body.fAxis1=body.fAxis2=.002;body.fPerh=.001;body.fPeriod=1e6;
        NativeEphemeris.State(body,0,out var center,out var bodyVelocity);
        ship.objSS.vPosx=center.X+12/AU;ship.objSS.vVelX=bodyVelocity.X;ship.objSS.vVelY=bodyVelocity.Y;
        obstacle.vPosx=center.X+12/AU;obstacle.vVelX=bodyVelocity.X;obstacle.vVelY=bodyVelocity.Y;
        var moving=AIShipAvoidance.SelectEscape(new Point(center.X+7/AU,0),ship,obstacle,30);
        Near((moving.X-center.X)*AU,17,"Moving-body frame was ignored",1e-5);
        // Clear-space behavior remains native, even at legitimate long distances.
        ship.objSS.vPosx=100000000/AU;ship.objSS.vVelY=1e-5;
        var far=new Point(ship.objSS.vPosx+5/AU,0);
        Check(AIShipAvoidance.SelectEscape(far,ship,obstacle,30).X==far.X,"Distant traffic changed without body risk");
        Console.WriteLine(checks+" geometry, selection, cache and allocation checks passed.");
    }
}
