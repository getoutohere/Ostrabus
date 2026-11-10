using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;
using Ostrabus.NavConsole.Outlines;
using Ostrabus.ShipNavigation;
using Ostrabus.ShipNavigation.Collisions;
using UnityEngine;

static partial class Program {
    static int impacts;
    static bool throwImpact;
    static double impactX,impactY,impactDisplacement;
    static bool CollisionNotPlayer(ref bool __result){__result=false;return false;}
    static bool CollisionPorts(ref bool __result){__result=true;return false;}
    static bool CollisionNoTrigger(ref CondTrigger __result){__result=null;return false;}
    static bool CollisionConfig(ConfigEntry<bool> __instance,bool value){Field(__instance,"_typedValue",value);return false;}
    static void Impact(double x,double y,double displacement){impacts++;impactX=x;impactY=y;impactDisplacement=displacement/HullZoom.MetresToAU;if(throwImpact)throw new InvalidOperationException("fixture impact");}
    // Native geometric normal inputs and bounce factor are retained. The
    // downstream world/crew/damage/Unity side effects are an explicit boundary.
    static IEnumerable<CodeInstruction> ImpactBoundary(IEnumerable<CodeInstruction> instructions) {
        var original=instructions.ToList();var normalize=AccessTools.Method(typeof(MathUtils),nameof(MathUtils.SetLength),new[]{typeof(double).MakeByRefType(),typeof(double).MakeByRefType(),typeof(double)});
        var replacement=AccessTools.Method(typeof(PhysicalCollisions),nameof(PhysicalCollisions.Normal));
        var call=original.Single(c=>c.Calls(normalize)||c.Calls(replacement));
        var situ=AccessTools.PropertyGetter(typeof(Ship),nameof(Ship.objSS));
        var code=new List<CodeInstruction>();
        foreach(string axis in new[]{"vPosx","vPosy"}) {
            code.Add(new CodeInstruction(OpCodes.Ldarg_0));code.Add(new CodeInstruction(OpCodes.Callvirt,situ));code.Add(new CodeInstruction(OpCodes.Ldfld,AccessTools.Field(typeof(ShipSitu),axis)));
            code.Add(new CodeInstruction(OpCodes.Ldarg_1));code.Add(new CodeInstruction(OpCodes.Callvirt,situ));code.Add(new CodeInstruction(OpCodes.Ldfld,AccessTools.Field(typeof(ShipSitu),axis)));
            code.Add(new CodeInstruction(OpCodes.Sub));code.Add(new CodeInstruction(axis=="vPosx"?OpCodes.Stloc_0:OpCodes.Stloc_1));
        }
        code.Add(new CodeInstruction(OpCodes.Ldloca_S,0));code.Add(new CodeInstruction(OpCodes.Ldloca_S,1));code.Add(new CodeInstruction(OpCodes.Ldc_R8,1d));code.Add(new CodeInstruction(OpCodes.Call,call.operand));
        code.Add(new CodeInstruction(OpCodes.Ldloc_0));code.Add(new CodeInstruction(OpCodes.Ldloc_1));code.Add(new CodeInstruction(OpCodes.Ldarg_2));
        code.Add(new CodeInstruction(OpCodes.Ldsfld,AccessTools.Field(typeof(CollisionManager),"fBounceRange")));code.Add(new CodeInstruction(OpCodes.Mul));code.Add(new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(Program),nameof(Impact))));code.Add(new CodeInstruction(OpCodes.Ret));return code;
    }
    static Ship CollisionHull(string id,double x,double y) {
        var ship=Hull(Ship.Loaded.Shallow);ship.strRegID=id;ship.aProxCurrent=new List<string>();ship.aProxIgnores=new List<string>();
        var situ=new ShipSitu(false){Size=100,vPosx=x*HullZoom.MetresToAU,vPosy=y*HullZoom.MetresToAU};Field(ship,"<objSS>k__BackingField",situ);
        Field(ship,"aDocked",new Dictionary<string,Ship>());return ship;
    }
    static void WarmCollision(Ship ship) {
        var worker=(HullWorker)AccessTools.Field(typeof(HullDisplay),"worker").GetValue(null);
        var source=AccessTools.Method(typeof(HullDisplay),"Create").Invoke(null,new object[]{ship});Field(source,"NextCapture",double.MaxValue);
        var entry=(HullWorker.Entry)AccessTools.Field(source.GetType(),"Entry").GetValue(source);
        var table=AccessTools.Field(typeof(HullDisplay),"sources").GetValue(null);table.GetType().GetMethod("Add").Invoke(table,new[]{(object)ship,source});
        Publish(worker,entry,new Envelope(new[]{new HullPoint(-4,-.5),new HullPoint(4,-.5),new HullPoint(4,.5),new HullPoint(-4,.5)},4));
    }
    static float NativeDistance(Ship a,Ship b)=>(float)a.objSS.GetDistance(b.objSS.vPosx,b.objSS.vPosy);
    static void ClearImpactMemory()=>((Dictionary<string,string>)AccessTools.Field(typeof(CollisionManager),"_collisionMemory").GetValue(null)).Clear();
    static void RuntimeCollisionCases(Harmony boundary) {
        HullDisplay.Reset();double originalEpoch=StarSystem.fEpoch;
        var flag=Empty<ConfigEntry<bool>>();Field(flag,"_typedValue",true);PhysicalCollisions.Flag=flag;
        StationDockedCollisions.Flag=Empty<ConfigEntry<bool>>();Field(StationDockedCollisions.Flag,"_typedValue",true);
        var originalWorld=CrewSim.system;
        CrewSim.system=Empty<StarSystem>();CrewSim.system.dictShips=new Dictionary<string,Ship>();CrewSim.system.aBOs=new Dictionary<string,BodyOrbit>();
        var a=CollisionHull("CONTACT-A",0,30);var b=CollisionHull("CONTACT-B",0,0);
        CrewSim.system.dictShips.Add(a.strRegID,a);CrewSim.system.dictShips.Add(b.strRegID,b);
        boundary.Patch(AccessTools.PropertySetter(typeof(ConfigEntry<bool>),nameof(ConfigEntry<bool>.Value)),prefix:new HarmonyMethod(typeof(Program),nameof(CollisionConfig)));
        boundary.Patch(AccessTools.Method(typeof(Ship),nameof(Ship.IsPlayerShip)),prefix:new HarmonyMethod(typeof(Program),nameof(CollisionNotPlayer)));
        boundary.Patch(AccessTools.PropertyGetter(typeof(Ship),nameof(Ship.HasDockingPorts)),prefix:new HarmonyMethod(typeof(Program),nameof(CollisionPorts)));
        boundary.Patch(AccessTools.Method(typeof(DataHandler),nameof(DataHandler.GetCondTrigger),new[]{typeof(string)}),prefix:new HarmonyMethod(typeof(Program),nameof(CollisionNoTrigger)));
        var process=AccessTools.Method(typeof(CollisionManager),"ProcessCollision",new[]{typeof(Ship),typeof(Ship),typeof(double)});
        boundary.Patch(process,transpiler:new HarmonyMethod(typeof(Program),nameof(ImpactBoundary)));
        var production=new Harmony("offline.physical.hull.collisions");
        try {
            production.CreateClassProcessor(typeof(HullCollisionScanPatch)).Patch();production.CreateClassProcessor(typeof(HullCollisionResponsePatch)).Patch();production.CreateClassProcessor(typeof(StationDockedCollisionPatch)).Patch();
            StarSystem.fEpoch=1000;WarmCollision(a);WarmCollision(b);
            float radius=CollisionManager.GetCollisionDistanceAU(a,b);
            Check(!PhysicalCollisions.Overlap(a,b,NativeDistance(a,b),radius),"Runtime still collides with clear parallel hulls");
            PhysicalCollisions.SetEnabled(false);
            Check(PhysicalCollisions.Overlap(a,b,NativeDistance(a,b),radius),"Disabled setting failed to restore native circle overlap");
            PhysicalCollisions.SetEnabled(true);
            Check(!PhysicalCollisions.Overlap(a,b,NativeDistance(a,b),radius),"Re-enabling required reloading cached hull geometry");
            impacts=0;ClearImpactMemory();CollisionManager.CheckCollisions(a);
            Check(impacts==0&&a.aProxCurrent.Contains(b.strRegID),"Hull rejection changed native proximity or still reached physical response");
            a.objSS.vPosx=30*HullZoom.MetresToAU;a.objSS.vPosy=10*HullZoom.MetresToAU;
            ClearImpactMemory();CollisionManager.CheckCollisions(a);
            Check(impacts==1&&Math.Abs(impactX)<1e-8&&impactY>.999,"Native response used the center-line rather than contact-face normal");
            Check(impactDisplacement>0&&impactDisplacement<25,"Native response retained enormous circular penetration");
            double nx=3,ny=4;PhysicalCollisions.Normal(ref nx,ref ny,1);
            Check(Math.Abs(nx-.6)<1e-8&&Math.Abs(ny-.8)<1e-8,"Response state leaked outside its native collision call");
            throwImpact=true;bool threw=false;
            try {ClearImpactMemory();CollisionManager.CheckCollisions(a);}catch(InvalidOperationException error){threw=error.Message=="fixture impact";}
            finally {throwImpact=false;}
            Check(threw,"Impact exception fixture did not reach response finalizers");
            var failedPrepared=AccessTools.Field(typeof(PhysicalCollisions),"prepared").GetValue(null);
            var failedResponse=AccessTools.Field(typeof(PhysicalCollisions),"response").GetValue(null);
            Check(AccessTools.Field(failedPrepared.GetType(),"A").GetValue(failedPrepared)==null&&!(bool)AccessTools.Field(failedResponse.GetType(),"Active").GetValue(failedResponse),"Exception left actor/contact state dangling");
            impacts--;
            a.objSS.vPosx=a.objSS.vPosy=0;ClearImpactMemory();CollisionManager.CheckCollisions(a);
            Check(impacts==2,"Coincident centers bypassed the alternative hull branch");

            PhysicalCollisions.Reset();a.objSS.vPosx=-400*HullZoom.MetresToAU;a.objSS.vPosy=0;a.objSS.vVelX=800*HullZoom.MetresToAU;StarSystem.fEpoch=1100;
            ClearImpactMemory();CollisionManager.CheckCollisions(a);int before=impacts;
            StarSystem.fEpoch++;a.objSS.vPosx=400*HullZoom.MetresToAU;ClearImpactMemory();CollisionManager.CheckCollisions(a);
            Check(impacts==before+1&&impactX<-.999&&impactDisplacement>0,"Native scan missed a swept hit outside both endpoint collision circles");
            var prepared=AccessTools.Field(typeof(PhysicalCollisions),"prepared").GetValue(null);
            Check(AccessTools.Field(prepared.GetType(),"A").GetValue(prepared)==null&&AccessTools.Field(prepared.GetType(),"B").GetValue(prepared)==null,"Scan scope retained ship references after return");
            var numeric=AccessTools.Field(typeof(PhysicalCollisions),"solver").GetValue(null);
            Check(AccessTools.Field(numeric.GetType(),"a").GetValue(numeric)==null&&AccessTools.Field(numeric.GetType(),"b").GetValue(numeric)==null,"Numeric query retained shapes after return");
            // Native separation can move a hull back to its incoming side.
            // That correction is not a fresh travelled path through the target.
            StarSystem.fEpoch++;a.objSS.vPosx=-199*HullZoom.MetresToAU;before=impacts;
            ClearImpactMemory();CollisionManager.CheckCollisions(a);
            Check(impacts==before,"Native collision separation was replayed as another swept impact");

            Field(a.objSS,"vPosx",30*HullZoom.MetresToAU);a.objSS.vPosy=30*HullZoom.MetresToAU;
            foreach(var kind in new[]{Ship.TypeClassification.Projectile,Ship.TypeClassification.Asteroid,Ship.TypeClassification.OrbitalStation,Ship.TypeClassification.Buoy}) {
                a.Classification=kind;Check(PhysicalCollisions.Overlap(a,b,NativeDistance(a,b),radius),"Non-hull collision class was narrowed: "+kind);
            }
            a.Classification=Ship.TypeClassification.Default;
            a.objSS.bIsBO=true;Check(PhysicalCollisions.Nonzero(0,a,b)==0,"Coincident-center fix changed native station handling");a.objSS.bIsBO=false;
            var station=CollisionHull("CONTACT-PORT",10000,0);station.objSS.bIsBO=true;CrewSim.system.dictShips.Add(station.strRegID,station);
            ((Dictionary<string,Ship>)AccessTools.Field(typeof(Ship),"aDocked").GetValue(a)).Add("DP|PORT",station);
            ((Dictionary<string,Ship>)AccessTools.Field(typeof(Ship),"aDocked").GetValue(station)).Add("DP|A",a);
            Check(!PhysicalCollisions.Overlap(a,b,NativeDistance(a,b),radius)&&!PhysicalCollisions.Overlap(b,a,NativeDistance(a,b),radius),"Alternative hull path broke station-docked collision exemption");
            ((Dictionary<string,Ship>)AccessTools.Field(typeof(Ship),"aDocked").GetValue(a)).Clear();((Dictionary<string,Ship>)AccessTools.Field(typeof(Ship),"aDocked").GetValue(station)).Clear();CrewSim.system.dictShips.Remove(station.strRegID);
            AccessTools.Field(typeof(GUIFFWD),"bFFWDActive").SetValue(null,true);
            Check(PhysicalCollisions.Overlap(a,b,NativeDistance(a,b),radius),"SFF collision handling did not retain native behavior");
            AccessTools.Field(typeof(GUIFFWD),"bFFWDActive").SetValue(null,false);
            PhysicalCollisions.Reset();StarSystem.fEpoch=2000;a.objSS.vPosx=-400*HullZoom.MetresToAU;a.objSS.vPosy=0;a.objSS.vVelX=0;
            ClearImpactMemory();CollisionManager.CheckCollisions(a);before=impacts;
            StarSystem.fEpoch++;a.objSS.vPosx=10000*HullZoom.MetresToAU;ClearImpactMemory();CollisionManager.CheckCollisions(a);
            Check(impacts==before,"Instant-dock/teleport discontinuity became a swept collision");
            foreach(var untouched in new[]{AccessTools.Method(typeof(CollisionManager),nameof(CollisionManager.WillCollide)),AccessTools.Method(typeof(CollisionManager),nameof(CollisionManager.CheckProjectileCollisions)),
                AccessTools.Method(typeof(CollisionManager),nameof(CollisionManager.GetCollisionDistanceAU),new[]{typeof(Ship),typeof(Ship)}),AccessTools.Method(typeof(CollisionManager),"ProcessCollision",new[]{typeof(Ship),typeof(BodyOrbit),typeof(double)})})
                Check(!(Harmony.GetPatchInfo(untouched)?.Owners.Contains(production.Id)??false),"Weapons/shared radius/planet path was patched");
            var pendingShip=Hull(Ship.Loaded.Shallow,Enumerable.Range(0,5000).Select(i=>new JsonItem{strName="ItmFloor01",fX=i%100,fY=i/100}).ToArray());
            HullDisplay.Geometry(pendingShip,out _);
            var pending=(HullSnapshot)AccessTools.Field(typeof(HullDisplay),"capture").GetValue(null);
            Check(pending!=null,"Collision-only capture did not yield across frames");
            PhysicalCollisions.SetEnabled(false);
            Check(AccessTools.Field(typeof(HullDisplay),"capture").GetValue(null)==null&&!pending.Owner.TryGetTarget(out _)&&AccessTools.Field(typeof(HullSnapshot),"saved").GetValue(pending)==null,"Disabling collisions retained a partial capture/saved item array");
        } finally {
            production.UnpatchSelf();boundary.Unpatch(process,HarmonyPatchType.Transpiler,boundary.Id);
            AccessTools.Field(typeof(GUIFFWD),"bFFWDActive").SetValue(null,false);
            PhysicalCollisions.Flag=null;StationDockedCollisions.Flag=null;HullDisplay.Reset();StarSystem.fEpoch=originalEpoch;CrewSim.system=originalWorld;
        }
    }
}
