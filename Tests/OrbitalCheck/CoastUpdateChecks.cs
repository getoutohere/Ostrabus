using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Ostranauts.ShipGUIs.Utilities;
using Ostrabus.Orbital;
using UnityEngine;

static class CoastUpdateChecks {
    static bool throwAfterMotion;
    static int playbackCalls;
    static bool clearPlayback;
    static bool Skip() => false;
    static bool AfterMotion() {
        if (throwAfterMotion) throw new InvalidOperationException("coast-cleanup-probe");
        return false;
    }
    static bool Playback(ShipSitu situUs,ref bool __result) {
        playbackCalls++;situUs.vPosx=.123;situUs.vVelX=.00001;
        if (clearPlayback) situUs.NavData=null;
        __result=true;return false;
    }
    static long Count() => (long)((Dictionary<string,object>)CoastIntegration.Snapshot())["integratedSteps"];
    internal static void Run(Action<bool,string> check,StarSystem world,BodyOrbit planet,Ship ship) {
        var harmony=new Harmony("orbital.coast.native-update.check");
        var update=AccessTools.Method(typeof(StarSystem),"UpdateShip");
        var s=ship.objSS;
        MotionState initial=OrbitalMotion.Read(s);
        float rot=s.fRot,w=s.fW,a=s.fA;
        double epoch=StarSystem.fEpoch;
        try {
            // Only leaf UI/shipboard effects are stubbed. UpdateShip, force selection,
            // gravity injection/restoration and TimeAdvance execute their shipped IL.
            foreach(string name in new[]{"DamageOverTime","Sparks","UpdateCrewSkills","GravApplyCrew"})
                harmony.Patch(AccessTools.Method(typeof(Ship),name),prefix:new HarmonyMethod(typeof(CoastUpdateChecks),nameof(Skip)));
            harmony.Patch(AccessTools.PropertySetter(typeof(Ship),nameof(Ship.Gravity)),prefix:new HarmonyMethod(typeof(CoastUpdateChecks),nameof(Skip)));
            harmony.Patch(AccessTools.Method(typeof(Ship),"UpdateGravAndAtmo",new[]{typeof(BodyOrbit),typeof(BodyOrbit),typeof(Vector2)}),prefix:new HarmonyMethod(typeof(CoastUpdateChecks),nameof(Skip)));
            harmony.Patch(AccessTools.Method(typeof(Ship),"PostUpdate"),prefix:new HarmonyMethod(typeof(CoastUpdateChecks),nameof(AfterMotion)));
            harmony.Patch(AccessTools.Method(typeof(NavData),nameof(NavData.TimeAdvance)),prefix:new HarmonyMethod(typeof(CoastUpdateChecks),nameof(Playback)));
            ship.aWPs=new List<WaypointShip>();
            s.aPathRecent=new List<Ostranauts.Core.Models.Tuple<double,Ostranauts.Utils.Models.Point>>();
            void Restore() {
                OrbitalMotion.Write(s,initial);s.fRot=rot;s.fW=w;s.fA=a;
                s.vAccEx=s.vAccIn=s.vAccRCS=s.vAccLift=s.vAccDrag=default;
                s.NavData=null;s.bIgnoreGrav=s.bBOLocked=s.bIsBO=s.bOrbitLocked=false;s.strBOPORShip=null;
                s.aPathRecent.Clear();s.aPathRecent.Add(new Ostranauts.Core.Models.Tuple<double,Ostranauts.Utils.Models.Point>(epoch+1,default));
                StarSystem.fEpoch=epoch+1;planet.UpdateTime(epoch+1);
            }
            void Step() => update.Invoke(world,new object[]{1d,ship});
            Restore();long before=Count();Step();
            check(Count()==before+1,"Actual UpdateShip failed to enter coast path: "+Newtonsoft.Json.JsonConvert.SerializeObject(CoastIntegration.Snapshot())+" enabled="+OrbitalSimulation.Enabled+" r="+((new DVector(s.vPosx,s.vPosy)-new DVector(planet.dXReal,planet.dYReal)).Length*OrbitalMotion.KmPerAU));
            var released=(CoastIntegration.Context)AccessTools.Field(typeof(CoastIntegration),"current").GetValue(null);
            check(released.Situation==null && released.Body==null,"Normal UpdateShip retained object references");
            check(s.vAccEx.x==0 && s.vAccEx.y==0,"Native gravity restoration changed");
            MotionState improved=OrbitalMotion.Read(s);
            // Off must be precisely native. Forces/flags which exclude coasting
            // must be precisely equivalent whether the coast toggle is on or off.
            foreach(string mode in new[]{"rcs","torch","lift","drag","external","ignore","docked"}) {
                MotionState off=default;
                for(int enabled=0;enabled<2;enabled++) {
                    Restore();CoastIntegration.Enabled=enabled!=0;
                    if (mode=="rcs") s.vAccRCS=new Vector2(1e-12f,-2e-12f);
                    if (mode=="torch") s.vAccIn=new Vector2(1e-11f,2e-11f);
                    if (mode=="lift") s.vAccLift=new Vector2(1e-12f,0);
                    if (mode=="drag") s.vAccDrag=new Vector2(0,-1e-12f);
                    if (mode=="external") s.vAccEx=new Vector2(1e-12f,0);
                    if (mode=="ignore") s.bIgnoreGrav=true;
                    if (mode=="docked") s._dockGroup=new SolarSystem.DockGroup();
                    long count=Count();Step();
                    MotionState actual=OrbitalMotion.Read(s);
                    check(Count()==count,"Excluded UpdateShip integrated: "+mode);
                    if (enabled==0) off=actual;
                    else check(actual.Position.X==off.Position.X && actual.Position.Y==off.Position.Y &&
                        actual.Velocity.X==off.Velocity.X && actual.Velocity.Y==off.Velocity.Y,"Native equivalence failed: "+mode);
                    if (mode=="external") check(s.vAccEx.x==1e-12f,"External acceleration not restored");
                    s._dockGroup=null;
                }
            }
            CoastIntegration.Enabled=true;
            foreach(bool clear in new[]{false,true}) {
                Restore();s.NavData=(NavData)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(NavData));
                clearPlayback=clear;playbackCalls=0;before=Count();Step();
                check(playbackCalls==1 && Count()==before && s.vPosx==.123 && s.vVelX==.00001,"Native plotted playback overwritten or invoked twice");
            }
            // Native exception after motion must preserve the exception and release
            // the scoped ship reference. A later direct prediction must stay native.
            Restore();throwAfterMotion=true;
            bool failed=false;
            try { Step(); }
            catch(TargetInvocationException ex) { failed=ex.InnerException is InvalidOperationException && ex.InnerException.Message=="coast-cleanup-probe"; }
            finally { throwAfterMotion=false; }
            check(failed,"UpdateShip exception was swallowed/changed");
            released=(CoastIntegration.Context)AccessTools.Field(typeof(CoastIntegration),"current").GetValue(null);
            check(released.Situation==null && released.Body==null,"Failed UpdateShip retained object references");
            check(!CoastIntegration.Prepare(s,1,false).Valid,"UpdateShip finalizer retained failed ship context");
            before=Count();s.TimeAdvance(1);
            check(Count()==before,"Failed update leaked scope into prediction");
            Restore();Step();
            check(Count()==before+1,"Update after exception did not recover");
            check((OrbitalMotion.Read(s).Position-improved.Position).Length==0,"Recovery changed deterministic integration");
            Console.WriteLine("Real UpdateShip: coast dispatch, powered/external/docked equivalence, NavData, restoration and exception cleanup passed.");
        } finally {
            throwAfterMotion=false;harmony.UnpatchSelf();CoastIntegration.Enabled=true;
            OrbitalMotion.Write(s,initial);s.NavData=null;s._dockGroup=null;
            StarSystem.fEpoch=epoch;planet.UpdateTime(epoch);
        }
    }
}
