using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using Ostrabus.Sensors.Model;
using Ostrabus.EMPropagation;
using Ostranauts.Ships.Sensors;
using Ostranauts.ShipGUIs.Utilities;
using Ostranauts.Utils.Models;

static class Program {
    const double AuM = 149597863936;
    static int checks;
    static Ship observer;
    static readonly Type Contacts = typeof(ContactModel).Assembly.GetType("Ostrabus.Sensors.SensorContacts",true);
    static readonly Type EMModule = typeof(ContactModel).Assembly.GetType("Ostrabus.EMPropagation.EmPropagation",true);
    delegate bool Offset(ShipDraw draw, Ship ship, double now, out Point offset);
    static readonly Offset Compute = AccessTools.MethodDelegate<Offset>(AccessTools.Method(Contacts,"TryOffset",new[]{typeof(ShipDraw),typeof(Ship),typeof(double),typeof(Point).MakeByRefType()}));
    static void Check(bool ok,string reason) { checks++; if(!ok)throw new Exception(reason); }
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Back(object instance,string name,object value) => AccessTools.Field(instance.GetType(),"<"+name+">k__BackingField").SetValue(instance,value);
    static void Invoke(Type type,string name,params object[] args) => AccessTools.Method(type,name).Invoke(null,args);
    static bool Equal(UnityEngine.Object __0,UnityEngine.Object __1,ref bool __result) { __result=ReferenceEquals(__0,__1);return false; }
    static bool Unequal(UnityEngine.Object __0,UnityEngine.Object __1,ref bool __result) { __result=!ReferenceEquals(__0,__1);return false; }
    static bool SkipSceneDestroy() => false;
    static int rngCalls;
    static bool Rng(float __0,float __1,ref float __result) { rngCalls++; __result=__0+(__1-__0)*.5f; return false; }
    static Ship MakeShip(string id,double km) {
        var ship=Empty<Ship>();ship.strRegID=id;AccessTools.Field(typeof(Ship),"nLoadState").SetValue(ship,Ship.Loaded.Full);ship.nCols=20;ship.nRows=20;
        var situ=Empty<ShipSitu>();situ.vPosx=km*1000/AuM;
        Back(ship,"objSS",situ);Back(ship,"ElectronicSystems",new ElectronicSystems(ship));
        return ship;
    }
    static T Sensor<T>(double range,double strength=0) where T:ShipSensor {
        var sensor=Empty<T>();Back(sensor,"RangeKm",range);sensor.SensorState=1;
        AccessTools.Field(typeof(ShipSensor),"_sensorStrength").SetValue(sensor,strength);
        return sensor;
    }
    static int TrackCount => ((IDictionary)AccessTools.Field(Contacts,"tracks").GetValue(null)).Count;
    static void Main() {
        var harness=new Harmony("offline.sensor-runtime");
        harness.Patch(AccessTools.Method(typeof(UnityEngine.Object),"op_Equality"),prefix:new HarmonyMethod(typeof(Program),nameof(Equal)));
        harness.Patch(AccessTools.Method(typeof(UnityEngine.Object),"op_Inequality"),prefix:new HarmonyMethod(typeof(Program),nameof(Unequal)));
        harness.Patch(AccessTools.Method(typeof(UnityEngine.Object),"Destroy",new[]{typeof(UnityEngine.Object)}),prefix:new HarmonyMethod(typeof(Program),nameof(SkipSceneDestroy)));
        try {
            var em=Sensor<EM>(500);var signature=Empty<ShipSignature>();signature.EMEmissions=.6;
            double native=em.GetSignalStrength(signature,1000,1);
            Check(Math.Abs(native-.075)<1e-12,"Native EM fixture");
            Invoke(EMModule,"SetEnabled",true);
            Check(Math.Abs(em.GetSignalStrength(signature,1000,1)-.15)<1e-12,"Actual EM Harmony signal patch");
            Check(Math.Abs(em.GetDetectionDistance(signature,1,.3f)-PropagationModel.DetectionDistance(500,.6,.3f))<1e-9,"Actual EM Harmony range patch");
            em.SensorState=0;Check(em.GetSignalStrength(signature,1000,1)==0,"Powered-off receiver preserved");em.SensorState=1;
            Invoke(EMModule,"SetEnabled",false);
            Check(em.GetSignalStrength(signature,1000,1)==native,"EM disabling restores shipped method");
            var emInfo=Harmony.GetPatchInfo(AccessTools.Method(typeof(EM),nameof(EM.GetSignalStrength)));
            Check(emInfo==null || emInfo.Prefixes.Count==0,"No disabled EM hook");

            observer=MakeShip("OBSERVER",0);observer.ElectronicSystems.aElectronicSystems.Add(Sensor<Optical>(50));
            var target=MakeShip("TARGET",1000);var draw=new ShipDraw(target) { SignalVisibility=SignalVisibility.Visible };
            var nav=Empty<GUIOrbitDraw>();AccessTools.Field(typeof(GUIOrbitDraw),"_instance").SetValue(null,nav);
            CrewSim.system=Empty<StarSystem>();
            Invoke(Contacts,"SetEnabled",true);
            Check(draw.GetPositionOffset(0).X==target.objSS.vPosx,"Uninitialized nav console safely uses native behavior");
            var navOwner=Empty<CondOwner>();navOwner.ship=observer;
            AccessTools.Field(typeof(GUIOrbitDraw),"coSelfTemp").SetValue(nav,navOwner);
            Point absolute=draw.GetPositionOffset(0);Point relative=draw.GetPositionOffset();
            Check(relative.magnitude>0,"Visible distant contact receives model uncertainty despite zero range argument");
            Check(Math.Abs(absolute.X-target.objSS.vPosx-relative.X)<1e-16 && Math.Abs(absolute.Y-target.objSS.vPosy-relative.Y)<1e-16,"Native offset overloads agree");
            Check(target.objSS.vPosx==1000000/AuM && target.objSS.NavData==null,"Authoritative position and routes unchanged");
            Point again=draw.GetPositionOffset();Check(again.X==relative.X && again.Y==relative.Y,"Repeated rendering consumes no fresh noise");
            Check(TrackCount==1,"One cached record per contact");
            var poi=new NavPOI(0,0) { SensorOffset=relative };AccessTools.Field(typeof(NavPOI),"_ship").SetValue(poi,target);
            poi.GetSXY(out double x,out double y);Check(Math.Abs(x-absolute.X)<1e-16 && Math.Abs(y-absolute.Y)<1e-16,"Native selected marker uses same cached offset");
            observer.ElectronicSystems.aElectronicSystems.Add(Sensor<EM>(500));target.bXPDRAntenna=true;target.strXPDR="TARGET";
            target.objSS.vPosx=700000/AuM;
            Check(Compute(draw,observer,10,out Point cubic),"Sensor-only configuration accepts vanilla EM propagation");
            Invoke(EMModule,"SetEnabled",true);
            Check(Compute(draw,observer,10,out Point square) && square.magnitude<cubic.magnitude*.1,"Combined configuration acquires timestamp using revised EM propagation");
            Invoke(EMModule,"SetEnabled",false);
            Check(Compute(draw,observer,10,out Point restored) && Math.Abs(restored.magnitude-cubic.magnitude)<1e-16,"EM toggle independently invalidates cached measurements");
            target.objSS.vPosx=1000000/AuM;
            Check(Compute(draw,observer,100,out _),"Sampling accepts explicit native fixture");
            Check(Compute(draw,observer,1000000,out Point jumped) && !double.IsNaN(jumped.X),"SFF gap samples once without replay");
            target.objSS.vPosx=120000/AuM;
            Check(Compute(draw,observer,1000000,out Point close) && close.magnitude==0 && TrackCount==0,"Combat clamp applies immediately after travel");
            target.objSS.vPosx=1000000/AuM;
            Check(Compute(draw,observer,1000003,out _),"Distant record recreated");
            draw.SignalVisibility=SignalVisibility.None;
            Check(!Compute(draw,observer,1000004,out _) && TrackCount==0,"Hidden contact removed without disclosure");draw.SignalVisibility=SignalVisibility.Visible;
            foreach(var sensor in observer.ElectronicSystems.aElectronicSystems) sensor.SensorState=0;
            Check(!Compute(draw,observer,1000007,out _),"No sensor does not manufacture estimate");
            foreach(var sensor in observer.ElectronicSystems.aElectronicSystems) sensor.SensorState=1;
            Check(Compute(draw,observer,1000010,out _),"Receiver restored");
            observer.objSS.bIsBO=false;target.objSS.bIsBO=true;draw.shipInfo=new ShipInfo(target,true);
            Check(Compute(draw,observer,1000011,out Point station) && station.magnitude==0,"Known station remains exact");target.objSS.bIsBO=false;
            Check(Compute(draw,observer,1000014,out _),"Record before destroy");draw.Destroy();Check(TrackCount==0,"ShipDraw destruction releases record");
            draw=new ShipDraw(target) { SignalVisibility=SignalVisibility.Visible };Check(Compute(draw,observer,1000017,out _),"Record before disable");
            var nextObserver=MakeShip("NEXT OBSERVER",0);nextObserver.ElectronicSystems.aElectronicSystems.Add(Sensor<Optical>(50));
            Check(Compute(draw,nextObserver,1000020,out _) && TrackCount==1,"Changing observer replaces cache rather than retaining prior observer");
            Invoke(Contacts,"Reset");Check(TrackCount==0 && ((Array)AccessTools.Field(Contacts,"package").GetValue(null)).Length==0,"Scene/reset cleanup releases contact and package state");
            Invoke(Contacts,"SetEnabled",false);
            Check(TrackCount==0 && draw.GetPositionOffset().magnitude==0,"Disable clears cache and native offset");
            Check(draw.GetPositionOffset(0).X==target.objSS.vPosx,"Native visible-position behavior restored");
            var displayInfo=Harmony.GetPatchInfo(AccessTools.Method(typeof(ShipDraw),nameof(ShipDraw.GetPositionOffset),new[]{typeof(double)}));
            Check(displayInfo==null || displayInfo.Prefixes.Count==0,"No disabled display hook");
            harness.Patch(AccessTools.Method(typeof(UnityEngine.Random),"Range",new[]{typeof(float),typeof(float)}),prefix:new HarmonyMethod(typeof(Program),nameof(Rng)));
            Invoke(Contacts,"SetEnabled",true);
            StarSystem.fEpoch=65632487698;
            draw=new ShipDraw(target){ SignalVisibility=SignalVisibility.Partial };
            foreach (double rangeKm in new[]{999999999d,1000000001d,1494119044d,6e9,15e9}) {
                target.objSS.vPosx=rangeKm*1000/AuM; StarSystem.fEpoch+=20;
                Point first=draw.GetPositionOffset(target.objSS.vPosx); int before=rngCalls;
                for(int frame=0;frame<100;frame++) {
                    Point next=draw.GetPositionOffset(target.objSS.vPosx);
                    Check(next.X==first.X && next.Y==first.Y,"Paused distant redraw stable at "+rangeKm+" km");
                }
                Check(rngCalls==before,"Distant model never falls through to random native positions");
                Check(TrackCount==1,"Outer-system contacts retain a cached model estimate");
            }
            // No valid channel: fallback must preserve an existing native sample,
            // and repeated rejections must reuse their own bounded cache record.
            observer.ElectronicSystems.aElectronicSystems.Clear();
            var badSensor=Sensor<Optical>(double.NaN); observer.ElectronicSystems.aElectronicSystems.Add(badSensor);
            Invoke(Contacts,"Reset");
            AccessTools.Field(typeof(ShipDraw),"_posOffset").SetValue(draw,new Point(.01,.02));
            double scheduled=StarSystem.fEpoch+60;
            AccessTools.Field(typeof(ShipDraw),"_posTimeStamp").SetValue(draw,scheduled);
            int fallbackRng=rngCalls;
            Point fallback=draw.GetPositionOffset(target.objSS.vPosx);
            Check(fallback.X==target.objSS.vPosx+.01 && fallback.Y==.02,"First rejection preserves native cached position");
            var records=(IDictionary)AccessTools.Field(Contacts,"tracks").GetValue(null); object rejected=records[draw];
            for(int frame=0;frame<100;frame++) {
                Point next=draw.GetPositionOffset(target.objSS.vPosx);
                Check(next.X==fallback.X && next.Y==fallback.Y,"Rejected paused redraw preserves native sample");
            }
            Check(rngCalls==fallbackRng && (double)AccessTools.Field(typeof(ShipDraw),"_posTimeStamp").GetValue(draw)==scheduled,"Rejected samples never reset native timer");
            Check(ReferenceEquals(records[draw],rejected),"Rejection reuses track instead of allocating every redraw");
            StarSystem.fEpoch+=2; draw.GetPositionOffset(target.objSS.vPosx);
            Check(rngCalls==fallbackRng,"Rejection retry also leaves native timer intact");
            Back(badSensor,"RangeKm",50d); StarSystem.fEpoch+=2;
            draw.GetPositionOffset(target.objSS.vPosx);
            Check(Compute(draw,observer,StarSystem.fEpoch,out _),"Native fallback recovers to modeled estimate");
            Back(badSensor,"RangeKm",double.NaN); StarSystem.fEpoch+=2;
            draw.GetPositionOffset(target.objSS.vPosx); int transitionRng=rngCalls;
            Check(transitionRng==fallbackRng+3,"Valid-to-rejected transition lets native sample once");
            for(int frame=0;frame<100;frame++)draw.GetPositionOffset(target.objSS.vPosx);
            Check(rngCalls==transitionRng,"Further rejected redraws never reroll noise");
            Invoke(Contacts,"SetEnabled",false);
            Console.WriteLine(checks+" shipped-assembly runtime sensor/EM checks passed.");
        } finally {
            Invoke(Contacts,"Stop");Invoke(EMModule,"Stop");harness.UnpatchSelf();
        }
    }
}
