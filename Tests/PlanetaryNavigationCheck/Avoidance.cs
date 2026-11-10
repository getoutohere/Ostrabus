using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SolarSystem;
using Ostrabus.ShipNavigation;

static partial class Program {
    static Ship station;
    static bool NearestStation(ref Ship __result) { __result=station; return false; }
    static bool NoBody(ref BodyOrbit __result) { __result=null; return false; }
    static bool FutureCollision(ShipSitu a,ShipSitu b,double horizon) {
        return AIShipAvoidance.FutureCollision(a,b,horizon);
    }
    static IEnumerable<CodeInstruction> FutureOnly(IEnumerable<CodeInstruction> instructions) {
        var native=AccessTools.Method(typeof(CollisionManager),"WillCollide");
        foreach(var code in instructions) {
            if(code.Calls(native)) {var copy=new CodeInstruction(code);copy.operand=AccessTools.Method(typeof(Program),nameof(FutureCollision));yield return copy;}
            else yield return code;
        }
    }
    static void CollisionObserved(ShipSitu objssA,ShipSitu objssB,double maxTimeFrame, bool __result) {
        if (__result && !forecasting) {
            var p=objssA.vPos-objssB.vPos; var v=objssA.vVel-objssB.vVel;
            double time=-p.Dot(v)/v.Dot(v);
            Console.WriteLine("Native WillCollide true: radius km="+(objssA.GetRadiusAU()+objssB.GetRadiusAU())*AU+"; closest time="+time+"; radial dot="+p.Dot(v)+"; window="+maxTimeFrame);
        }
    }
    static JObject ReadShip(string path) {
        var record=new JObject();
        using(var stream=new StreamReader(path)) using(var reader=new JsonTextReader(stream){MaxDepth=null}) {
            while(reader.Read()) if(reader.TokenType==JsonToken.PropertyName && reader.Depth==2) {
                string name=(string)reader.Value; reader.Read();
                if(name=="objSS"||name=="strRegID"||name=="aDocked"||name=="bShipHidden"||name=="strScanTargetID"||name=="ShipType") record[name]=JToken.ReadFrom(reader);
                else reader.Skip();
            }
        }
        return record;
    }
    static int Avoidance(Harmony h,string zip,string snapshot) {
        h.Patch(AccessTools.Method(typeof(StarSystem),"GetNearestStationRegional"),prefix:new HarmonyMethod(typeof(Program),nameof(NearestStation)));
        h.Patch(AccessTools.Method(typeof(StarSystem),"GetBO"),prefix:new HarmonyMethod(typeof(Program),nameof(NoBody)));
        h.Patch(AccessTools.Method(typeof(CollisionManager),"WillCollide"),postfix:new HarmonyMethod(typeof(Program),nameof(CollisionObserved)));
        var records=new Dictionary<string,JObject>();
        CrewSim.system=Empty<StarSystem>(); CrewSim.system.dictShips=new Dictionary<string,Ship>();
        foreach(JObject r in JObject.Parse(File.ReadAllText(zip))["ships"]) {
            string id=(string)r["strRegID"]; if(id==null)continue;
            var ship=Empty<Ship>();ship.strRegID=id;
            var ss=new ShipSitu(r["objSS"].ToObject<JsonShipSitu>());
            AccessTools.Field(typeof(Ship),"<objSS>k__BackingField").SetValue(ship,ss);
            AccessTools.Field(typeof(Ship),"nLoadState").SetValue(ship,Ship.Loaded.Shallow);
            AccessTools.Field(typeof(Ship),"bShipHidden").SetValue(ship,(bool?)r["bShipHidden"]??false);
            AccessTools.Field(typeof(Ship),"aDocked").SetValue(ship,new Dictionary<string,Ship>());
            CrewSim.system.dictShips[id]=ship; records[id]=r;
        }
        foreach(var pair in records) {
            var ship=CrewSim.system.dictShips[pair.Key];
            string target=(string)pair.Value["strScanTargetID"];
            if(target!=null && CrewSim.system.dictShips.TryGetValue(target,out var tgt)) ship.shipScanTarget=tgt;
            if(pair.Value["aDocked"] is JObject links) foreach(var link in links.Properties()) {
                if(CrewSim.system.dictShips.TryGetValue((string)link.Value,out var peer))
                    ((Dictionary<string,Ship>)AccessTools.Field(typeof(Ship),"aDocked").GetValue(ship)).Add(link.Name,peer);
            }
        }
        var us=CrewSim.system.dictShips["O-ENB"]; station=CrewSim.system.dictShips["OKLG"];
        var anchorShip=CrewSim.system.dictShips["OKLG|RES_1"];
        var group=new DockGroup { Anchor=new ShipSitu(anchorShip.objSS,false) };
        foreach(string id in new[]{"OKLG|RES_1","O-GXD","E-7HAP","O-F4C6"}) {
            var s=CrewSim.system.dictShips[id]; group.DockedShips.Add(new DockGroupMember(s,false));s.objSS._dockGroup=group;
        }
        group.Anchor.Size=(int)AccessTools.Method(typeof(DockGroup),"CalculateAnchorSize").Invoke(group,null);
        if(records["O-ENB"]["objSS"]["jnd"]!=null) {us.objSS.NavData=null;Forecast(h,us,group,snapshot,records["O-ENB"]);return 0;}
        Console.WriteLine("Loaded native ship geometries="+records.Count+"; station group radius km="+group.Anchor.GetRadiusAU()*AU);
        bool collision=AIShipManager.IsOnCollisionCourse(us,out var escape);
        Check(collision,"Saved native evasion was not reproduced.");
        Console.WriteLine("Native avoidance result="+collision+"; escape delta km="+(escape.X-us.objSS.vPosx)*AU+","+(escape.Y-us.objSS.vPosy)*AU);
        if(collision) AIShipManager.AIIntercept2(us,escape.X,escape.Y,us.objSS.vVelX,us.objSS.vVelY,0,null,2.0053761318195E-8);
        var acceleration=us.objSS.vAccRCS;
        Near(lastDirectionX,acceleration.x/Math.Sqrt((double)acceleration.x*acceleration.x+(double)acceleration.y*acceleration.y),"Native evasion did not match captured acceleration X",1e-6);
        Near(lastDirectionY,acceleration.y/Math.Sqrt((double)acceleration.x*acceleration.x+(double)acceleration.y*acceleration.y),"Native evasion did not match captured acceleration Y",1e-6);
        var hair=CrewSim.system.dictShips["O-F4C6"];
        Console.WriteLine("Hair unanchored field="+!hair.objSS.bBOLocked+"; anchor locked="+group.Anchor.bBOLocked);
        h.Patch(AccessTools.Method(typeof(AIShipManager),"IsOnCollisionCourse"),transpiler:new HarmonyMethod(typeof(Program),nameof(FutureOnly)));
        Check(!AIShipManager.IsOnCollisionCourse(us,out escape),"Past-only saved warning survived the future filter.");
        foreach(var scenario in new[]{"past","future","overlap","miss"}) {
            var a=new ShipSitu(false){Size=200,vPosx=0,vPosy=0};
            var b=new ShipSitu(false){Size=200,vPosx=1e-6,vPosy=scenario=="miss"?1e-6:0};
            if(scenario=="overlap")b.vPosx=0;
            a.vVelX=scenario=="past"?-1e-8:1e-8;
            bool native=CollisionManager.WillCollide(a,b,200),future=FutureCollision(a,b,200);
            Console.WriteLine("Control "+scenario+": native="+native+"; future-only="+future);
            bool wanted=scenario=="future"||scenario=="overlap";
            Check(future==wanted,"Future collision control failed: "+scenario);
            if(scenario=="past")Check(native,"Shared historical collision behavior changed");
        }
        h.Unpatch(AccessTools.Method(typeof(AIShipManager),"IsOnCollisionCourse"),HarmonyPatchType.Transpiler,h.Id);
        var production=new Harmony("offline.planetary.contracts");
        try {
            production.CreateClassProcessor(typeof(AIShipAvoidancePatch)).Patch();
            Check(!AIShipManager.IsOnCollisionCourse(us,out _),"Production IL did not filter saved past encounter");
            Check(!AIShipManager.IsOnCollisionCourse(null,out _),"Native null exclusion changed");
            us.objSS.NavData=new Ostranauts.ShipGUIs.Utilities.NavData(us){IsTorching=true};
            Check(!AIShipManager.IsOnCollisionCourse(us,out _),"Native torch exclusion changed");us.objSS.NavData=null;
            var savedWorld=CrewSim.system.dictShips;
            try {
                var mover=GeometryShip(0,0);mover.strRegID="MOVER";
                var traffic=GeometryShip(1,0);traffic.strRegID="TRAFFIC";
                CrewSim.system.dictShips=new Dictionary<string,Ship>{{mover.strRegID,mover},{traffic.strRegID,traffic}};
                mover.objSS.vVelX=.2/AU;
                Check(AIShipManager.IsOnCollisionCourse(mover,out _),"Production method lost a genuine future ship warning");
                mover.objSS.vVelX=-.2/AU;
                Check(!AIShipManager.IsOnCollisionCourse(mover,out _),"Production method evaded a separated past-only encounter");
                traffic.objSS.vPosx=.1/AU;
                Check(AIShipManager.IsOnCollisionCourse(mover,out _),"Production method lost an existing overlap warning");
            } finally {CrewSim.system.dictShips=savedWorld;}
            var originalMethod=AccessTools.Method(typeof(AIShipManager),"IsOnCollisionCourse");
            var reader=typeof(PatchProcessor).GetMethods().Single(m=>m.Name=="GetOriginalInstructions" && m.GetParameters().Length==2 && !m.GetParameters()[1].ParameterType.IsByRef);
            var original=((IEnumerable<CodeInstruction>)reader.Invoke(null,new object[]{originalMethod,null})).ToList();
            var patched=AIShipAvoidancePatch.Transpiler(original,originalMethod).ToList();
            Check(patched.Count(c=>c.Calls(AccessTools.Method(typeof(AIShipAvoidance),"FutureCollision")))==1 && patched.Count(c=>c.Calls(AccessTools.Method(typeof(AIShipAvoidance),"SelectEscape")))==1,"Production transpiler interception count differs");
            bool refused=false;try{AIShipAvoidancePatch.Transpiler(Array.Empty<CodeInstruction>(),originalMethod).ToList();}catch(InvalidOperationException){refused=true;}
            Check(refused,"Unknown IL shape was accepted");
        } finally {production.UnpatchSelf();}
        if(snapshot!=null) Forecast(h,us,group,snapshot,records["O-ENB"]);
        return 0;
    }
}
