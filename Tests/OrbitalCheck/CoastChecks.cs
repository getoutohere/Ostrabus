using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using Ostrabus.Orbital;
using UnityEngine;
using Ostranauts.ShipGUIs.Utilities;

static class CoastChecks {
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    internal static void Run(Action<bool,string> check) {
        var harmony = new Harmony("orbital.coast.check");
        double savedEpoch = StarSystem.fEpoch;
        try {
            check(harmony.CreateClassProcessor(typeof(CoastScopePatch)).Patch().Count == 1,"Coast UpdateShip scope binding");
            check(harmony.CreateClassProcessor(typeof(CoastMotionPatch)).Patch().Count == 1,"Coast TimeAdvance binding");
            var sun = new BodyOrbit(new JsonBodyOrbitSave { strName="Sol",fMass=1.9891e30,fPeriod=31556926 });
            var planet = new BodyOrbit(new JsonBodyOrbitSave { strName="Venus",fMass=4.87e24,fRadius=6052/OrbitalMotion.KmPerAU,
                fAxis1=1.44,fAxis2=1.44*Math.Sqrt(1-.0068*.0068),fEcc=.0068,fPerh=.72*(1-.0068),
                fPeriod=19407509.79,fPeriodShift=123456,fAngle=71,nOrbitDirection=1 }) { boParent=sun };
            sun.boChildren=new List<BodyOrbit>{planet};
            var world=Empty<StarSystem>();world.boStar=sun;
            AccessTools.Field(typeof(StarSystem),"fGravAccelConstant").SetValue(world,2e-44f);
            var fast=AccessTools.Method(typeof(StarSystem),"GetGreatestGravBOFast");
            const double start=65631764416.3972;
            // Compare the pure native-tolerance evaluator to the shipped ephemeris,
            // including negative epochs and high eccentricity/retrograde children.
            foreach(double ecc in new[]{0,.0068,.45,.9}) foreach(double epoch in new[]{-12345d,0d,start,start+8192}) {
                var child=new BodyOrbit(new JsonBodyOrbitSave { strName="moon",fAxis1=.0002,fAxis2=.0002*Math.Sqrt(1-ecc*ecc),
                    fPerh=.0001*(1-ecc),fEcc=ecc,fPeriod=10000,fAngle=33,nOrbitDirection=-1 }) { boParent=planet };
                child.UpdateTime(epoch-1,bCalcV:false);child.UpdateTime(epoch,bCalcV:false);
                double cached=child.dTimeCalcLast;
                check(CoastIntegration.NativePosition(child,epoch,out var p) &&
                    (p-new DVector(child.dXReal,child.dYReal)).Length*OrbitalMotion.KmPerAU<.000002,"Native ephemeris coordinate mismatch ecc="+ecc+" epoch="+epoch+" error km="+(p-new DVector(child.dXReal,child.dYReal)).Length*OrbitalMotion.KmPerAU);
                CoastIntegration.NativePosition(child,epoch+1,out _);
                check(child.dTimeCalcLast==cached,"Coast ephemeris mutated native cache");
            }
            planet.UpdateTime(start);
            StarSystem.fEpoch=start;
            CoastIntegration.NativePosition(planet,start,out var center);
            CoastIntegration.NativePosition(planet,start-32,out var minus);
            CoastIntegration.NativePosition(planet,start+32,out var plus);
            DVector centerVelocity=(plus-minus)*(1d/64);
            double radius=17556/OrbitalMotion.KmPerAU,mu=(double)(float)2e-44*planet.fMass;
            var ship=Empty<Ship>();AccessTools.Property(typeof(Ship),nameof(Ship.objSS)).SetValue(ship,Empty<ShipSitu>());
            var s=ship.objSS;
            s.vPosx=center.X+radius;s.vPosy=center.Y;
            s.vVelX=centerVelocity.X;s.vVelY=centerVelocity.Y+3.122/OrbitalMotion.KmPerAU;
            // Warm the parent-correction system identity before opening a ship scope.
            object[] args={s,default(Vector2),null};fast.Invoke(world,args);
            double energy0=.5*Math.Pow(3.122/OrbitalMotion.KmPerAU,2)-mu/radius;
            double elapsed=0;
            while(elapsed<8192) {
                double dt=Math.Min(5.333333333333333,8192-elapsed);
                dt=(start+elapsed+dt)-(start+elapsed);
                StarSystem.fEpoch=start+elapsed+dt;planet.UpdateTime(StarSystem.fEpoch);
                var previous=CoastIntegration.Enter(ship,dt);
                try {
                    args[1]=default(Vector2);args[2]=null;
                    check(ReferenceEquals(fast.Invoke(world,args),planet),"Coast gravity selector changed");
                    s.vAccEx=(Vector2)args[1];
                    s.TimeAdvance(dt); // Actual native method with the Harmony prefix/postfix.
                    s.vAccEx=default;
                } finally { CoastIntegration.Leave(previous); }
                elapsed+=dt;
            }
            CoastIntegration.NativePosition(planet,StarSystem.fEpoch,out center);
            CoastIntegration.NativePosition(planet,StarSystem.fEpoch-32,out minus);
            CoastIntegration.NativePosition(planet,StarSystem.fEpoch+32,out plus);
            var relative=new DVector(s.vPosx,s.vPosy)-center;
            var velocity=new DVector(s.vVelX,s.vVelY)-(plus-minus)*(1d/64);
            double energy=velocity.Dot(velocity)/2-mu/relative.Length;
            double error=Math.Abs((energy-energy0)/energy0)*100;
            check(error<.001,"Patched native coast energy drift too large: "+error+"%");
            Console.WriteLine("Coast actual TimeAdvance: 136.53 game minutes, 5.333s steps, energy error "+error+"%.");
            var stats=(Dictionary<string,object>)CoastIntegration.Snapshot();
            check((long)stats["integratedSteps"]>1500 && (long)stats["invalidSteps"]==0,"Coast path not exercised");

            // Scope, exclusions and native angular updates are independently tested.
            check(!CoastIntegration.Prepare(s,1,false).Valid,"Unscoped prediction changed");
            var scope=CoastIntegration.Enter(ship,1);
            CoastIntegration.Selected(s,planet,default);
            try {
                check(CoastIntegration.Prepare(s,1,false).Valid,"Eligible coast rejected");
                check(!CoastIntegration.Prepare(s,1,true).Valid,"Ignore acceleration overridden");
                check(!CoastIntegration.Prepare(s,2,false).Valid,"Different-duration prediction overridden");
                foreach(string field in new[]{"vAccIn","vAccRCS","vAccLift","vAccDrag","vAccEx"}) {
                    var f=typeof(ShipSitu).GetField(field);f.SetValue(s,new Vector2(1e-12f,0));
                    check(!CoastIntegration.Prepare(s,1,false).Valid,"Force exclusion failed: "+field);f.SetValue(s,default(Vector2));
                }
                foreach(string field in new[]{"bBOLocked","bOrbitLocked","bIsBO","bIgnoreGrav"}) {
                    var f=typeof(ShipSitu).GetField(field);f.SetValue(s,true);
                    check(!CoastIntegration.Prepare(s,1,false).Valid,"Lock exclusion failed: "+field);f.SetValue(s,false);
                }
                s.NavData=Empty<NavData>();check(!CoastIntegration.Prepare(s,1,false).Valid,"NavData exclusion failed");s.NavData=null;
                CoastIntegration.Enabled=false;check(!CoastIntegration.Prepare(s,1,false).Valid,"Coast toggle ignored");CoastIntegration.Enabled=true;
                var untouched=Empty<ShipSitu>();untouched.fW=.1f;untouched.fA=-.01f;
                untouched.TimeAdvance(1);s.fW=.1f;s.fA=-.01f;s.fRot=0;
                MotionState allocationStart=OrbitalMotion.Read(s);
                s.TimeAdvance(1);
                check(s.fRot==untouched.fRot && s.fW==untouched.fW && s.fA==untouched.fA,"Native angular motion changed");
                // Cached ordinary preparation must not allocate per ship/frame.
                CoastIntegration.Prepare(s,1,false);
                long before=GC.GetAllocatedBytesForCurrentThread();
                for(int i=0;i<100;i++) CoastIntegration.Prepare(s,1,false);
                check(GC.GetAllocatedBytesForCurrentThread()==before,"Cached coast preparation allocates");
                s.TimeAdvance(1);
                before=GC.GetAllocatedBytesForCurrentThread();
                for(int i=0;i<100;i++) s.TimeAdvance(1);
                check(GC.GetAllocatedBytesForCurrentThread()==before,"Patched TimeAdvance allocates");
                OrbitalMotion.Write(s,allocationStart);
            } finally { CoastIntegration.Leave(scope); }
            s._dockGroup=new SolarSystem.DockGroup();scope=CoastIntegration.Enter(ship,1);
            CoastIntegration.Selected(s,planet,default);
            check(!CoastIntegration.Prepare(s,1,false).Valid,"Docked ship integrated individually");CoastIntegration.Leave(scope);s._dockGroup=null;
            scope=CoastIntegration.Enter(ship,21600);CoastIntegration.Selected(s,planet,default);
            check(!CoastIntegration.Prepare(s,21600,false).Valid,"Large jump entered ordinary coast path");CoastIntegration.Leave(scope);
            // Exercise atmosphere/collision/invalid-ephemeris fallback without
            // depending on a nonzero drag force from the preceding native frame.
            var savedAtmosphere=planet.aAtmospheres;
            planet.aAtmospheres=new[]{new JsonAtmosphere { fMaxAltitude=100000 }};
            CoastIntegration.Reset();scope=CoastIntegration.Enter(ship,1);CoastIntegration.Selected(s,planet,default);
            check(!CoastIntegration.Prepare(s,1,false).Valid,"Atmosphere guard relies only on stale drag");
            CoastIntegration.Leave(scope);planet.aAtmospheres=savedAtmosphere;
            double savedPeriod=planet.fPeriod;planet.fPeriod=double.NaN;
            CoastIntegration.Reset();scope=CoastIntegration.Enter(ship,1);CoastIntegration.Selected(s,planet,default);
            check(!CoastIntegration.Prepare(s,1,false).Valid,"Malformed ephemeris not rejected");
            CoastIntegration.Leave(scope);planet.fPeriod=savedPeriod;
            CoastIntegration.Reset();
            CoastUpdateChecks.Run(check,world,planet,ship);
            CoastEnduranceChecks.Run(check);
            CoastIntegration.Reset();
            check(!CoastIntegration.Prepare(s,1,false).Valid,"Reset retained ship context");
        } finally { harmony.UnpatchSelf();CoastIntegration.Reset();CoastIntegration.Enabled=true;StarSystem.fEpoch=savedEpoch; }
    }
}
