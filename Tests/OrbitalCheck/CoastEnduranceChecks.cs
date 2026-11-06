using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ostrabus.Orbital;
using UnityEngine;

static class CoastEnduranceChecks {
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static double D(JToken j,string key) => (double)j[key];
    internal static void Run(Action<bool,string> check) {
        var data=JObject.Parse(FixtureFiles.Read("VORB-BOUND-CURRENT.json"))["data"];
        BodyOrbit Body(string name,BodyOrbit parent=null) {
            var j=data["bodies"][name];
            return new BodyOrbit(new JsonBodyOrbitSave { strName=name,fAxis1=D(j,"majorAxisAU"),fAxis2=D(j,"minorAxisAU"),
                fPerh=D(j,"periapsisAU"),fAph=D(j,"apoapsisAU"),fPeriod=D(j,"periodSeconds"),fPeriodShift=D(j,"phaseSeconds"),
                fEcc=D(j,"eccentricity"),fAngle=D(j,"angleDegrees"),nOrbitDirection=(int)j["direction"],fMass=D(j,"massKg"),fRadius=D(j,"radiusAU") }) { boParent=parent };
        }
        var sun=Body("Sol");var planet=Body("Venus",sun);sun.boChildren=new List<BodyOrbit>{planet};
        var world=Empty<StarSystem>();world.boStar=sun;
        AccessTools.Field(typeof(StarSystem),"fGravAccelConstant").SetValue(world,2e-44f);
        var fast=AccessTools.Method(typeof(StarSystem),"GetGreatestGravBOFast");
        double savedEpoch=StarSystem.fEpoch,start=D(data["bodies"]["Venus"],"positionCalculationEpoch");
        double km=OrbitalMotion.KmPerAU,mu=(double)(float)2e-44*planet.fMass;
        var watch=Stopwatch.StartNew();
        try {
            foreach(string name in new[]{"circular","eccentric","retrograde","escape","native-control"}) {
                double radius=(name=="circular"?10000:17556)/km;
                double speed=name=="circular"?Math.Sqrt(mu/radius):name=="escape"?7/km:3.122/km;
                if (name=="retrograde") speed=-speed;
                double duration=name=="escape"?7200:name=="native-control"?8192:172800;
                CoastIntegration.Enabled=name!="native-control";
                var ship=Empty<Ship>();var s=Empty<ShipSitu>();AccessTools.Property(typeof(Ship),nameof(Ship.objSS)).SetValue(ship,s);
                CoastIntegration.NativePosition(planet,start,out var center);
                CoastIntegration.NativePosition(planet,start-32,out var minus);
                CoastIntegration.NativePosition(planet,start+32,out var plus);
                var referenceVelocity=(plus-minus)*(1d/64);
                s.vPosx=center.X+radius;s.vPosy=center.Y;s.vVelX=referenceVelocity.X;s.vVelY=referenceVelocity.Y+speed;
                var initial=new MotionState(new DVector(radius,0)*km,new DVector(0,speed)*km);
                double energy0=.5*speed*speed-mu/radius;
                StarSystem.fEpoch=start;planet.UpdateTime(start);
                object[] args={s,default(Vector2),null};fast.Invoke(world,args);
                double elapsed=0;int steps=0;
                while(elapsed<duration) {
                    // Include the normal maximum, small frames and variable frame times.
                    double proposed=name=="native-control"?5.333333333333333:(steps%4==0?.1:steps%4==1?.533333333333333:steps%4==2?2:5.333333333333333);
                    double dt=Math.Min(proposed,duration-elapsed);dt=(start+elapsed+dt)-(start+elapsed);
                    StarSystem.fEpoch=start+elapsed+dt;planet.UpdateTime(StarSystem.fEpoch);
                    var previous=CoastIntegration.Enter(ship,dt);
                    try {
                        args[1]=default(Vector2);args[2]=null;
                        if (!ReferenceEquals(fast.Invoke(world,args),planet)) throw new Exception("Endurance unexpectedly left Venus gravity: "+name);
                        s.vAccEx=(Vector2)args[1];s.TimeAdvance(dt);s.vAccEx=default;
                    } finally { CoastIntegration.Leave(previous); }
                    elapsed+=dt;steps++;
                }
                CoastIntegration.NativePosition(planet,StarSystem.fEpoch,out center);
                CoastIntegration.NativePosition(planet,StarSystem.fEpoch-32,out minus);
                CoastIntegration.NativePosition(planet,StarSystem.fEpoch+32,out plus);
                var relative=new DVector(s.vPosx,s.vPosy)-center;
                var velocity=new DVector(s.vVelX,s.vVelY)-(plus-minus)*(1d/64);
                double energy=velocity.Dot(velocity)/2-mu/relative.Length;
                double error=Math.Abs((energy-energy0)/energy0)*100;
                if (name=="native-control") check(error>1,"Native control failed to reproduce timestep drift");
                else {
                    check(error<.01,"Accumulated energy drift: "+name+" "+error+"%");
                    check((energy>0)==(energy0>0),"Bound/escape classification changed: "+name);
                    check(OrbitalMotion.Coast(initial,duration,mu*km*km*km,out var exact),"Independent conic solution failed");
                    double positionError=(relative*km-exact.Position).Length;
                    check(positionError<20,"Conic endpoint discrepancy: "+name+" "+positionError+" km");
                    Console.WriteLine("Coast endurance "+name+": "+duration/3600+" game hours; energy error "+error+"%; conic endpoint error "+positionError+" km.");
                }
                if (name=="native-control") Console.WriteLine("Native control energy error: "+error+"%.");
            }
            Console.WriteLine("Coast endurance elapsed offline: "+watch.Elapsed.TotalSeconds+" seconds.");
        } finally { CoastIntegration.Enabled=true;CoastIntegration.Reset();StarSystem.fEpoch=savedEpoch; }
    }
}
