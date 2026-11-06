using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ostrabus.Orbital;
using UnityEngine;

static class Program {
    const double Meters=149597870700d;
    static int checks;
    static void Check(bool ok,string name) { checks++; if (!ok) throw new Exception(name); }
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Enable(bool value) { typeof(OrbitalSimulation).GetProperty("Enabled",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,value); }
    static BodyOrbit Circular(double radius,double period,BodyOrbit parent=null) => new BodyOrbit(new JsonBodyOrbitSave {
        strName="test", fAxis1=2*radius, fAxis2=2*radius, fPerh=radius, fAph=radius, fPeriod=period,nOrbitDirection=1
    }) { boParent=parent };
    static double Error(OrbitAcceleration.Acceleration a,OrbitAcceleration.Acceleration b) => Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Y-b.Y)*(a.Y-b.Y));
    static double D(JToken node,string key) => (double)node[key];
    public static int Main(string[] args) {
        EphemerisBoundaryChecks.Run(Check);
        if (args.Length != 0 && args[0] == "ephemeris") { Console.WriteLine(checks + " ephemeris boundary checks passed."); return 0; }
        Check(!OrbitalSimulation.Enabled,"Launch should default off until initialized");
        var root=Empty<BodyOrbit>();root.strName="Sol";root.fPeriod=31556926;
        var planet=Circular(.72,19407509.79,root);
        double rate=2*Math.PI/planet.fPeriod, expected=.72*rate*rate;
        Check(OrbitAcceleration.TryFrame(planet,0,out var a,out int depth) && depth==2,"Planet hierarchy");
        Check(Math.Abs(a.X+expected)<1e-26 && Math.Abs(a.Y)<1e-26,"Circular analytic acceleration");
        Check(OrbitAcceleration.TryFrame(planet,planet.fPeriod/4,out a,out _) && Math.Abs(a.Y+expected)<1e-26 && Math.Abs(a.X)<1e-26,"Quarter-period direction");
        var moon=Circular(.00001,100000,planet);
        OrbitAcceleration.TryLocal(moon,123,out var local);OrbitAcceleration.TryFrame(planet,123,out var parent,out _);
        Check(OrbitAcceleration.TryFrame(moon,123,out a,out depth) && depth==3 && Error(a,new OrbitAcceleration.Acceleration(local.X+parent.X,local.Y+parent.Y))<1e-25,"Nested moon correction");
        var placeholder=Empty<BodyOrbit>(); placeholder.boParent=planet;
        Check(OrbitAcceleration.TryFrame(placeholder,123,out a,out _) && Error(a,parent)<1e-25,"Stationary placeholder loses parent acceleration");
        moon.boParent=moon;
        Check(!OrbitAcceleration.TryFrame(moon,0,out _,out _),"Hierarchy cycle not rejected");
        moon.boParent=planet;moon.fEcc=1;
        Check(!OrbitAcceleration.TryFrame(moon,0,out _,out _),"Invalid eccentricity not rejected");
        moon.fEcc=0;moon.fPeriod=double.NaN;
        Check(!OrbitAcceleration.TryFrame(moon,0,out _,out _),"Invalid period not rejected");
        Check(!OrbitAcceleration.TryFrame(planet,double.PositiveInfinity,out _,out _),"Invalid epoch not rejected");

        // Derivative check against real shipped BodyOrbit.UpdateTime, several phases/directions.
        for (int sign=-1;sign<=1;sign+=2) for (int i=0;i<80;i++) {
            var orbit=new BodyOrbit(new JsonBodyOrbitSave { strName="ecc",fAxis1=.001,fAxis2=.001*Math.Sqrt(1-.45*.45),fPerh=.0005*.55,
                fAph=.0005*1.45,fPeriod=500000,fEcc=.45,fAngle=71,nOrbitDirection=sign });
            // A wider finite-difference interval avoids amplifying the game's 1e-6
            // Kepler solver stopping discontinuities into a spurious acceleration.
            double t=10000+i*5317d,h=100;
            orbit.UpdateTime(t-h,bCalcV:false); double xm=orbit.dXReal,ym=orbit.dYReal;
            orbit.UpdateTime(t,bCalcV:false); double x0=orbit.dXReal,y0=orbit.dYReal;
            orbit.UpdateTime(t+h,bCalcV:false);
            var finite=new OrbitAcceleration.Acceleration((orbit.dXReal-2*x0+xm)/(h*h),(orbit.dYReal-2*y0+ym)/(h*h));
            Check(OrbitAcceleration.TryLocal(orbit,t,out a),"Eccentric derivative failed");
            double distance=Math.Sqrt(x0*x0+y0*y0),n=2*Math.PI/orbit.fPeriod;
            double factor=-n*n*Math.Pow(.0005/distance,3);
            var independent=new OrbitAcceleration.Acceleration(x0*factor,y0*factor);
            Check(Error(a,independent)/a.Magnitude<.00001,"Analytic acceleration differs from independent central-force reference");
            // The game's early-stop solver also perturbs local derivatives; don't
            // reproduce that jitter in the parent-frame correction.
            Check(Error(a,finite)/a.Magnitude<.002,"Does not match real BodyOrbit derivative: sign="+sign+" phase="+i+" relative error="+(Error(a,finite)/a.Magnitude));
        }
        var captured=JObject.Parse(FixtureFiles.Read("VORB-BOUND-CURRENT.json"))["data"]["bodies"];
        var savedVenus=captured["Venus"];
        var venus=new BodyOrbit(new JsonBodyOrbitSave { strName="Venus",fAxis1=D(savedVenus,"majorAxisAU"),fAxis2=D(savedVenus,"minorAxisAU"),fPerh=D(savedVenus,"periapsisAU"),
            fAph=D(savedVenus,"apoapsisAU"),fEcc=D(savedVenus,"eccentricity"),fPeriod=D(savedVenus,"periodSeconds"),fAngle=D(savedVenus,"angleDegrees"),nOrbitDirection=1 });
        double epoch=D(savedVenus,"positionCalculationEpoch");
        OrbitAcceleration.TryFrame(venus,epoch,out a,out _);
        Check(a.Magnitude*Meters>.011 && a.Magnitude*Meters<.012,"Actual Venus correction magnitude");

        // Exercise actual Harmony hook and private gravity selector with real game types.
        var system=Empty<StarSystem>(); system.boStar=root;
        AccessTools.Field(typeof(StarSystem),"fGravAccelConstant").SetValue(system,2e-44f);
        root.fMass=1.9891e30;root.boChildren=new List<BodyOrbit>{venus};
        venus.boParent=root;venus.fMass=4.87e24;venus.boChildren=null;
        venus.UpdateTime(epoch);StarSystem.fEpoch=epoch;
        var situation=Empty<ShipSitu>();situation.vPosx=venus.dXReal+.0001;situation.vPosy=venus.dYReal;
        var fast=AccessTools.Method(typeof(StarSystem),"GetGreatestGravBOFast");
        Vector2 Query() {
            object[] arguments={situation,default(Vector2),null};
            Check(ReferenceEquals(fast.Invoke(system,arguments),venus),"Selected gravity body changed");
            return (Vector2)arguments[1];
        }
        var harmony=new Harmony("orbital.offline.check");
        try {
            Check(harmony.CreateClassProcessor(typeof(OrbitalGravityPatch)).Patch().Count==1,"Gravity patch binding");
            Check(harmony.CreateClassProcessor(typeof(OrbitalConsolePatch)).Patch().Count==1,"Console patch binding");
            Check(harmony.CreateClassProcessor(typeof(OrbitalStepObservationPatch)).Patch().Count==1,"Update observer patch binding");
            Vector2 original=Query();
            Enable(true);Vector2 corrected=Query();
            Check(Math.Abs((corrected.x-original.x)-a.X)<1e-18 && Math.Abs((corrected.y-original.y)-a.Y)<1e-18,"Actual hook failed to add correction");
            Vector2 publicGravity=system.GetGravAccel(venus,situation);
            Check(publicGravity.x==original.x && publicGravity.y==original.y,"Planner/local-gravity function patched globally");
            foreach (string flag in new[]{"bIsBO","bBOLocked","bOrbitLocked","bIgnoreGrav"}) {
                typeof(ShipSitu).GetField(flag).SetValue(situation,true);Vector2 skipped=Query();
                Check(skipped.x==original.x && skipped.y==original.y,"Double correction or ignored-gravity override: "+flag);
                typeof(ShipSitu).GetField(flag).SetValue(situation,false);
            }
            var stats=(Dictionary<string,object>)OrbitalSimulation.Snapshot();
            Query();stats=(Dictionary<string,object>)OrbitalSimulation.Snapshot();
            Check((long)stats["cacheHits"]>0 && (long)stats["ephemerisCalculations"]==1,"Per-epoch cache failed");
            StarSystem.fEpoch=epoch+500;Vector2 next=Query();
            Check(next.x!=corrected.x || next.y!=corrected.y,"Correction reused across epochs");
            Enable(false);Vector2 disabled=Query();
            Check(disabled.x==original.x && disabled.y==original.y,"Disabled module changes vanilla output");
            // Actual native six-hour step, not just our offline integrator: the
            // planet has advanced before gravity is sampled at the old ship position.
            var wreck=((JArray)JObject.Parse(FixtureFiles.Read("VORB-BOUND-CURRENT.json"))["data"]["ships"]);
            JToken motion=null;
            foreach (var item in wreck) if ((string)item["id"]=="M-5N4") motion=item["motion"];
            var jump=Empty<ShipSitu>();jump.vPosx=D(motion,"xAU");jump.vPosy=D(motion,"yAU");
            jump.vVelX=D(motion,"vxAUPerSecond");jump.vVelY=D(motion,"vyAUPerSecond");
            StarSystem.fEpoch=D(motion,"captureEpoch")+21600;venus.UpdateTime(StarSystem.fEpoch);
            object[] jumpArgs={jump,default(Vector2),null};
            Enable(true);var selected=fast.Invoke(system,jumpArgs);
            Check(ReferenceEquals(selected,root),"Large jump did not select Sun at old ship position");
            jump.vAccEx=(Vector2)jumpArgs[1];jump.TimeAdvance(21600);
            double km=149597870.7;
            double rx=(jump.vPosx-venus.dXReal)*km,ry=(jump.vPosy-venus.dYReal)*km;
            double vx=(jump.vVelX-venus.dVelX)*km,vy=(jump.vVelY-venus.dVelY)*km;
            double mu=(double)(float)2e-44*venus.fMass*Math.Pow(km,3);
            double energy=(vx*vx+vy*vy)/2-mu/Math.Sqrt(rx*rx+ry*ry);
            Check(energy>0,"Native six-hour numerical step should reproduce escape corruption");
            Console.WriteLine("Native six-hour jump reproduced escape energy: "+energy+" km²/s², even with parent correction enabled.");
            CoastChecks.Run(Check);
            SffChecks.Run(Check);
            HaulerRunawayChecks.Run(Check);
            HaulerRecoveryChecks.Run(Check);
            OrbitalSimulation.Reset();stats=(Dictionary<string,object>)OrbitalSimulation.Snapshot();
            Check((long)stats["correctionQueries"]==0,"Reset retained session state");
        } finally { harmony.UnpatchSelf();Enable(false); }
        Console.WriteLine(checks+" orbital checks passed.");return 0;
    }
}
namespace Ostrabus {
    internal static class OstrabusPlugin { internal static BepInEx.Logging.ManualLogSource Logger=null; }
}
