using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Ostrabus.ShipNavigation;
using SolarSystem;

static partial class Program {
    static int physicalCalls;
    static Dictionary<string,Ship> PolicyLinks(Ship s)=>(Dictionary<string,Ship>)AccessTools.Field(typeof(Ship),"aDocked").GetValue(s);
    static Ship PolicyHull(string id,double x) {
        var s=GeometryShip(x,0);s.strRegID=id;
        AccessTools.Field(typeof(Ship),"aDocked").SetValue(s,new Dictionary<string,Ship>());
        AccessTools.Field(typeof(Ship),"nLoadState").SetValue(s,Ship.Loaded.Shallow);
        s.aProxCurrent=new List<string>();s.aProxIgnores=new List<string>();
        CrewSim.system.dictShips.Add(id,s);return s;
    }
    static void Couple(Ship a,Ship b,bool moored=false){PolicyLinks(a).Add((moored?"MP|":"DP|")+b.strRegID,b);PolicyLinks(b).Add((moored?"MP|":"DP|")+a.strRegID,a);}
    static void Separate(Ship a,Ship b){foreach(var key in PolicyLinks(a).Where(p=>ReferenceEquals(p.Value,b)).Select(p=>p.Key).ToArray())PolicyLinks(a).Remove(key);foreach(var key in PolicyLinks(b).Where(p=>ReferenceEquals(p.Value,a)).Select(p=>p.Key).ToArray())PolicyLinks(b).Remove(key);}
    static void PhysicalBody(Ship a,Ship b){physicalCalls++;}
    static IEnumerable<CodeInstruction> PhysicalBoundary(IEnumerable<CodeInstruction> ignored) => new[]{new CodeInstruction(OpCodes.Ldarg_0),new CodeInstruction(OpCodes.Ldarg_1),new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(Program),nameof(PhysicalBody))),new CodeInstruction(OpCodes.Ret)};
    static bool NotPlayer(ref bool __result){__result=false;return false;}
    static bool Ports(ref bool __result){__result=true;return false;}
    static bool NoTrigger(ref CondTrigger __result){__result=null;return false;}

    static void StationPolicyChecks(Harmony boundary) {
        CrewSim.system=Empty<StarSystem>();CrewSim.system.dictShips=new Dictionary<string,Ship>();CrewSim.system.aBOs=new Dictionary<string,BodyOrbit>();
        var station=PolicyHull("PORT",50);station.objSS.bIsBO=true;station.objSS.bIsRegion=true;
        var tug=PolicyHull("TUG",25);var wreck=PolicyHull("WRECK",0);var free=PolicyHull("FREE",.1);var traffic=PolicyHull("TRAFFIC",.3);
        Couple(station,tug);Couple(tug,wreck,true);
        var policyFlag=Empty<BepInEx.Configuration.ConfigEntry<bool>>();
        AccessTools.Field(policyFlag.GetType(),"_typedValue").SetValue(policyFlag,false);
        StationDockedCollisions.Flag=policyFlag;
        Check(!StationDockedCollisions.IsExempt(wreck) && !StationDockedCollisions.IgnoreAsObstacle(tug),"Disabled policy still filters station-docked traffic");
        Check(StationDockedCollisions.ShipCollision(wreck,free),"Disabled policy still suppresses impacts");
        AccessTools.Field(policyFlag.GetType(),"_typedValue").SetValue(policyFlag,true);
        Check(StationDockedCollisions.IsExempt(wreck),"Live policy enable requires a docking transition");
        StationDockedCollisions.Flag=null;
        Check(StationDockedCollisions.IsExempt(wreck) && StationDockedCollisions.IsExempt(tug),"Station docking chain was not exempt");
        Check(!StationDockedCollisions.IsExempt(station) && !StationDockedCollisions.IsExempt(free),"Station or free hull received immunity");
        Check(!StationDockedCollisions.ShipCollision(free,wreck) && !StationDockedCollisions.ShipCollision(wreck,free),"Ship contact immunity is not symmetric");
        Check(StationDockedCollisions.ShipCollision(station,free),"Station's own physical collision changed");
        free.Classification=Ship.TypeClassification.Projectile;
        Check(StationDockedCollisions.ShipCollision(free,wreck),"Projectile collision was suppressed");free.Classification=Ship.TypeClassification.Default;
        station.Classification=Ship.TypeClassification.Waypoint;
        Check(!StationDockedCollisions.IsExempt(wreck),"Waypoint counted as a station");station.Classification=Ship.TypeClassification.Default;
        foreach(var kind in new[]{Ship.TypeClassification.Asteroid,Ship.TypeClassification.Projectile,Ship.TypeClassification.Buoy,Ship.TypeClassification.SignalBeacon}) {
            station.Classification=kind;Check(!StationDockedCollisions.IsExempt(wreck),"Nonstation body granted station immunity: "+kind);
        }
        station.Classification=Ship.TypeClassification.Default;
        station.objSS.bIsBO=false;AccessTools.Field(typeof(Ship),"_subStation").SetValue(station,true);
        Check(StationDockedCollisions.IsExempt(wreck),"Substation docking chain was not covered");AccessTools.Field(typeof(Ship),"_subStation").SetValue(station,false);
        station.Classification=Ship.TypeClassification.GroundStation;
        Check(StationDockedCollisions.IsExempt(wreck),"Ground station docking chain was not covered");station.Classification=Ship.TypeClassification.Default;station.objSS.bIsBO=true;
        Separate(station,tug);
        wreck.objSS._dockGroup=new DockGroup{Anchor=station.objSS};
        Check(!StationDockedCollisions.IsExempt(wreck) && !StationDockedCollisions.IsExempt(tug),"Free-space tow or stale group anchor retained immunity");wreck.objSS._dockGroup=null;
        Check(StationDockedCollisions.ShipCollision(free,wreck),"Free-space towing collision was suppressed");
        Couple(station,tug);
        PolicyLinks(station).Clear();
        Check(!StationDockedCollisions.IsExempt(wreck),"Unreciprocated station link granted immunity");PolicyLinks(station).Add("DP|TUG",tug);
        station.bDestroyed=true;Check(!StationDockedCollisions.IsExempt(wreck),"Destroyed station granted immunity");station.bDestroyed=false;
        CrewSim.system.dictShips.Remove(station.strRegID);Check(!StationDockedCollisions.IsExempt(wreck),"Unregistered station granted immunity");CrewSim.system.dictShips.Add(station.strRegID,station);
        // Shallow JSON links are supported; current runtime links override stale JSON.
        var saved=PolicyHull("SAVED",15);
        AccessTools.Field(typeof(Ship),"aDocked").SetValue(saved,null);
        saved.json=new JsonShip{aDocked=new Dictionary<string,string>{{"MP|PORT",station.strRegID}}};PolicyLinks(station).Add("MP|SAVED",saved);
        Check(StationDockedCollisions.IsExempt(saved),"Shallow saved docking chain was not recognized");
        AccessTools.Field(typeof(Ship),"aDocked").SetValue(saved,new Dictionary<string,Ship>());
        Check(!StationDockedCollisions.IsExempt(saved),"Stale JSON overrode current undocked state");PolicyLinks(station).Remove("MP|SAVED");
        Couple(wreck,free);Couple(free,tug);
        Check(StationDockedCollisions.IsExempt(wreck),"Connected cycle with a station was not handled");Separate(wreck,free);Separate(free,tug);
        for(int i=0;i<20;i++)StationDockedCollisions.IsExempt(wreck);
        long allocated=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<1000;i++)StationDockedCollisions.IsExempt(wreck);
        Check(GC.GetAllocatedBytesForCurrentThread()==allocated,"Station-chain checks allocate in steady state");
        var buffer=(List<Ship>)AccessTools.Field(typeof(StationDockedCollisions),"pending").GetValue(null);
        var refs=(Ship[])AccessTools.Field(typeof(List<Ship>),"_items").GetValue(buffer);
        Check(buffer.Count==0 && refs.All(s=>ReferenceEquals(s,null)),"Scratch buffer retains live hull references");

        var method=AccessTools.Method(typeof(CollisionManager),"ProcessCollision",new[]{typeof(Ship),typeof(Ship),typeof(double)});
        boundary.Patch(method,transpiler:new HarmonyMethod(typeof(Program),nameof(PhysicalBoundary)));
        boundary.Patch(AccessTools.Method(typeof(Ship),"IsPlayerShip"),prefix:new HarmonyMethod(typeof(Program),nameof(NotPlayer)));
        boundary.Patch(AccessTools.PropertyGetter(typeof(Ship),"HasDockingPorts"),prefix:new HarmonyMethod(typeof(Program),nameof(Ports)));
        boundary.Patch(AccessTools.Method(typeof(DataHandler),"GetCondTrigger",new[]{typeof(string)}),prefix:new HarmonyMethod(typeof(Program),nameof(NoTrigger)));
        var production=new Harmony("offline.station-collision.production");
        try {
            production.CreateClassProcessor(typeof(StationDockedCollisionPatch)).Patch();
            production.CreateClassProcessor(typeof(Ostrabus.NavConsole.ProximityCollisionPatch)).Patch();
            int before=physicalCalls;
            method.Invoke(null,new object[]{free,wreck,1d});method.Invoke(null,new object[]{wreck,free,1d});
            Check(physicalCalls==before,"Production collision prefix executed body for exempt contact");
            method.Invoke(null,new object[]{free,traffic,1d});Check(physicalCalls==before+1,"Production collision prefix suppressed ordinary contact");
            free.Classification=Ship.TypeClassification.Projectile;method.Invoke(null,new object[]{free,wreck,1d});free.Classification=Ship.TypeClassification.Default;
            Check(physicalCalls==before+2,"Production collision prefix suppressed weapon contact");
            CollisionManager.CheckCollisions(free);
            Check(!free.aProxCurrent.Contains(wreck.strRegID) && !free.aProxCurrent.Contains(tug.strRegID),"Parked hull still produces proximity warnings");
            Check(free.aProxCurrent.Contains(traffic.strRegID),"Ordinary proximity warning was removed");
            Check(!StationDockedCollisions.IgnoreAsObstacle(station),"Station entry/ATC detection was hidden");
            StationDockedCollisions.Flag=policyFlag;
            AccessTools.Field(policyFlag.GetType(),"_typedValue").SetValue(policyFlag,false);
            free.aProxCurrent.Clear();CollisionManager.CheckCollisions(free);
            Check(free.aProxCurrent.Contains(wreck.strRegID),"Disabling PDA policy did not restore native proximity candidates");
            int beforeDisabledCall=physicalCalls;
            method.Invoke(null,new object[]{free,wreck,1d});
            Check(physicalCalls==beforeDisabledCall+1,"Disabling PDA policy did not restore native collision entry");
            AccessTools.Field(policyFlag.GetType(),"_typedValue").SetValue(policyFlag,true);
            free.aProxCurrent.Clear();CollisionManager.CheckCollisions(free);
            Check(!free.aProxCurrent.Contains(wreck.strRegID),"Re-enabling policy requires re-docking");
            StationDockedCollisions.Flag=null;
            Separate(station,tug);free.aProxCurrent.Clear();CollisionManager.CheckCollisions(free);
            Check(free.aProxCurrent.Contains(wreck.strRegID),"Detached tow did not immediately become a proximity hazard again");
            var bodyMethod=AccessTools.Method(typeof(CollisionManager),"ProcessCollision",new[]{typeof(Ship),typeof(BodyOrbit),typeof(double)});
            Check(!(Harmony.GetPatchInfo(bodyMethod)?.Owners.Contains(production.Id)??false),"Planetary collisions were patched");
            Check(!(Harmony.GetPatchInfo(AccessTools.Method(typeof(CollisionManager),"CheckProjectileCollisions"))?.Owners.Contains(production.Id)??false),"Projectile collision scan was patched");
        } finally {production.UnpatchSelf();boundary.Unpatch(method,HarmonyPatchType.Transpiler,boundary.Id);}
        Console.WriteLine(checks+" cumulative station-chain, physical-gate and native proximity checks passed.");
    }
}
