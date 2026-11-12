using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using BepInEx.Configuration;
using HarmonyLib;

// Execute the real full-plugin AI composition, in both installation orders.
// The hull suite separately tests geometry; this catches transpiler ordering,
// scope cleanup and the existing planet-safe evasion's radius handoff.
static class HullInterop {
    static Ship station;
    static int radiusReads;
    static float lastRadius;
    static bool throwEscape;
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Set(object instance,string name,object value) => AccessTools.Field(instance.GetType(),name).SetValue(instance,value);
    static bool Equal(UnityEngine.Object __0,UnityEngine.Object __1,ref bool __result) {__result=ReferenceEquals(__0,__1);return false;}
    static bool Unequal(UnityEngine.Object __0,UnityEngine.Object __1,ref bool __result) {__result=!ReferenceEquals(__0,__1);return false;}
    static bool Acceleration(ref double __result) {__result=1e-10;return false;}
    static bool Station(ref Ship __result) {__result=station;return false;}
    static void Radius(float __result) {radiusReads++;lastRadius=__result;}
    static bool EscapeBoundary() {if (throwEscape) throw new InvalidOperationException("fixture evasion exception");return true;}
    static Ship Hull(string id,double x,double y) {
        var ship=Empty<Ship>();ship.strRegID=id;ship.json=new JsonShip{nCols=80,nRows=8};
        Set(ship,"nLoadState",Ship.Loaded.Shallow);Set(ship,"aDocked",new Dictionary<string,Ship>());
        Set(ship,"<objSS>k__BackingField",new ShipSitu(false){Size=100,vPosx=x*6.684587e-12,vPosy=y*6.684587e-12});
        return ship;
    }
    internal static int Run() {
        var assembly=typeof(Ostrabus.OstrabusPlugin).Assembly;
        Type Type(string name) => assembly.GetType("Ostrabus.ShipNavigation."+name,true);
        var physical=Type("Collisions.PhysicalCollisions");var avoidance=Type("Collisions.HullAvoidance");
        var flagField=AccessTools.Field(physical,"Flag");var oldFlag=flagField.GetValue(null);var world=CrewSim.system;
        var boundary=new Harmony("offline.hull.interop.environment");var production=new Harmony("offline.hull.interop.production");
        try {
            boundary.Patch(AccessTools.Method(typeof(UnityEngine.Object),"op_Equality"),prefix:new HarmonyMethod(typeof(HullInterop),nameof(Equal)));
            boundary.Patch(AccessTools.Method(typeof(UnityEngine.Object),"op_Inequality"),prefix:new HarmonyMethod(typeof(HullInterop),nameof(Unequal)));
            boundary.Patch(AccessTools.PropertyGetter(typeof(Ship),nameof(Ship.RCSAccelMax)),prefix:new HarmonyMethod(typeof(HullInterop),nameof(Acceleration)));
            boundary.Patch(AccessTools.Method(typeof(StarSystem),nameof(StarSystem.GetNearestStationRegional)),prefix:new HarmonyMethod(typeof(HullInterop),nameof(Station)));
            boundary.Patch(AccessTools.Method(avoidance,"EscapeRadius"),postfix:new HarmonyMethod(typeof(HullInterop),nameof(Radius)));
            boundary.Patch(AccessTools.Method(typeof(AIShipManager),"GetEscapePoint"),prefix:new HarmonyMethod(typeof(HullInterop),nameof(EscapeBoundary)));
            var flag=Empty<ConfigEntry<bool>>();Set(flag,"_typedValue",true);flagField.SetValue(null,flag);
            var a=Hull("INTEROP-A",-10000,0);var b=Hull("INTEROP-B",0,2500);station=b;
            a.objSS.vVelX=1000*6.684587e-12;
            CrewSim.system=Empty<StarSystem>();CrewSim.system.dictShips=new Dictionary<string,Ship>{{a.strRegID,a},{b.strRegID,b}};CrewSim.system.aBOs=new Dictionary<string,BodyOrbit>();
            var native=Type("AIShipAvoidancePatch");var hull=Type("Collisions.HullAvoidancePatch");var clearance=Type("Collisions.HullAvoidanceClearancePatch");
            foreach(bool reverse in new[]{false,true}) {
                foreach(var patch in reverse?new[]{clearance,hull,native}:new[]{native,hull,clearance})production.CreateClassProcessor(patch).Patch();
                radiusReads=0;
                if (!AIShipManager.IsOnCollisionCourse(a,out _) || radiusReads!=1 || lastRadius<a.objSS.GetRadiusAU()*10)
                    throw new Exception("Hull prediction failed to feed the existing native/planet-safe AI chain. Reverse order: "+reverse);
                object state=AccessTools.Field(avoidance,"current").GetValue(null);
                if (AccessTools.Field(state.GetType(),"Us").GetValue(state)!=null || AccessTools.Field(state.GetType(),"Them").GetValue(state)!=null)
                    throw new Exception("AI composition retained live situation references.");
                if (CollisionManager.WillCollide(a.objSS,b.objSS,30) || a.objSS.Size!=100 || b.objSS.Size!=100)
                    throw new Exception("AI composition changed the shared combat predictor or saved sizes.");
                throwEscape=true;bool threw=false;
                try {AIShipManager.IsOnCollisionCourse(a,out _);} catch(InvalidOperationException error) {threw=error.Message=="fixture evasion exception";}
                finally {throwEscape=false;}
                state=AccessTools.Field(avoidance,"current").GetValue(null);
                if (!threw || AccessTools.Field(state.GetType(),"Us").GetValue(state)!=null || AccessTools.Field(state.GetType(),"Them").GetValue(state)!=null)
                    throw new Exception("AI evasion exception retained actor situation references.");
                production.UnpatchSelf();
            }
            Console.WriteLine("Hull interop: both real full-plugin AI patch installation orders predict enlarged hulls, hand off matching escape clearance, release scope references on success/exception and preserve shared combat sizes.");
            return 0;
        } finally {production.UnpatchSelf();boundary.UnpatchSelf();flagField.SetValue(null,oldFlag);CrewSim.system=world;station=null;}
    }
}
