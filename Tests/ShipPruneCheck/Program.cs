using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using HarmonyLib;
using Ostrabus.Inspection;

static class Program {
    static int checks,hulls,people,homeRemovals,visibility;
    static bool missingTrigger,retainCrew;
    static StarSystem world;
    static readonly Dictionary<string,List<CondOwner>> crews=new Dictionary<string,List<CondOwner>>();
    static readonly HashSet<string> allowed=new HashSet<string>();
    static readonly List<string> plotShips=new List<string>();
    static T Empty<T>()=>(T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
    static void Patch(Harmony h,Type type,string name,string prefix,Type[] args=null) => h.Patch(AccessTools.Method(type,name,args),prefix:new HarmonyMethod(typeof(Program),prefix));
    static bool Equal(UnityEngine.Object x,UnityEngine.Object y,ref bool __result){__result=ReferenceEquals(x,y);return false;}
    static bool Unequal(UnityEngine.Object x,UnityEngine.Object y,ref bool __result){__result=!ReferenceEquals(x,y);return false;}
    static bool Plots(ref List<string> __result){__result=new List<string>(plotShips);return false;}
    static bool People(Ship __instance,ref List<CondOwner> __result){__result=crews.TryGetValue(__instance.strRegID,out var list)?new List<CondOwner>(list):new List<CondOwner>();return false;}
    static bool Trigger(string strName,ref CondTrigger __result){Check(strName=="TIsNPCPrunable","Wrong native prune condition");__result=missingTrigger?null:Empty<CondTrigger>();return false;}
    static bool Triggered(CondOwner objOwner,ref bool __result){__result=allowed.Contains(objOwner.strID);return false;}
    static bool Home(ref Ship __result){homeRemovals++;__result=null;return false;}
    static bool DestroyPerson(CondOwner __instance){people++;if(!retainCrew)foreach(var crew in crews.Values)crew.Remove(__instance);return false;}
    static bool Hide(){visibility++;return false;}
    static bool DestroyHull(Ship __instance,bool isDespawning){Check(!isDespawning,"Prune skipped ordinary destruction cleanup");hulls++;__instance.bDestroyed=true;world.dictShips.Remove(__instance.strRegID);return false;}
    static Ship Make(string id,int crewCount=0) {
        var ship=Empty<Ship>();ship.strRegID=id;ship.publicName="fixture";ship.IsAIShip=true;
        AccessTools.Property(typeof(Ship),nameof(Ship.objSS)).SetValue(ship,Empty<ShipSitu>());
        AccessTools.Field(typeof(Ship),"aDockingPorts").SetValue(ship,new List<string>());
        world.dictShips[id]=ship;var crew=new List<CondOwner>();crews[id]=crew;
        for(int i=0;i<crewCount;i++){var person=Empty<CondOwner>();AccessTools.Field(typeof(CondOwner),"_strID").SetValue(person,id+"-"+i);crew.Add(person);allowed.Add(person.strID);}
        return ship;
    }
    static InspectionHttp Http(string target,string method="POST") {
        var http=new InspectionHttp();AccessTools.Method(typeof(InspectionHttp),"Parse").Invoke(http,new object[]{method+" "+target+" HTTP/1.1\r\nHost: 127.0.0.1:18764\r\nContent-Length: 0",18764});return http;
    }
    static Dictionary<string,object> Body(InspectionApi.Reply reply)=>(Dictionary<string,object>)reply.Body;
    static void Refused(string id,string label) {
        int before=hulls+people+homeRemovals+visibility;
        var dry=ShipPruning.Run(id,true,null);Check(dry.Status==200 && !(bool)Body(dry)["eligible"],"Preview missed refusal: "+label);
        Check(ShipPruning.Run(id,false,"PRUNE").Status==409,"POST missed refusal: "+label);
        Check(hulls+people+homeRemovals+visibility==before,"Refusal mutated game: "+label);
    }
    public static int Main() {
        var h=new Harmony("ship.prune.offline");
        try {
            Patch(h,typeof(UnityEngine.Object),"op_Equality",nameof(Equal));Patch(h,typeof(UnityEngine.Object),"op_Inequality",nameof(Unequal));
            Patch(h,typeof(PlotManager),nameof(PlotManager.GetShipsInPlots),nameof(Plots));
            Patch(h,typeof(Ship),nameof(Ship.GetPeople),nameof(People),new[]{typeof(bool)});
            Patch(h,typeof(DataHandler),nameof(DataHandler.GetCondTrigger),nameof(Trigger),new[]{typeof(string)});
            Patch(h,typeof(CondTrigger),nameof(CondTrigger.Triggered),nameof(Triggered),new[]{typeof(CondOwner),typeof(string),typeof(bool)});
            Patch(h,typeof(CondOwner),nameof(CondOwner.RemoveFromCurrentHome),nameof(Home));
            Patch(h,typeof(CondOwner),nameof(CondOwner.Destroy),nameof(DestroyPerson));
            Patch(h,typeof(Ship),nameof(Ship.ToggleVis),nameof(Hide));Patch(h,typeof(Ship),nameof(Ship.Destroy),nameof(DestroyHull));
            world=Empty<StarSystem>();world.dictShips=new Dictionary<string,Ship>();
            AccessTools.Field(typeof(StarSystem),"dictShipOwners").SetValue(world,new Dictionary<string,string>());
            CrewSim.system=world;CrewSim.coPlayer=Empty<CondOwner>();AccessTools.Field(typeof(CondOwner),"_strID").SetValue(CrewSim.coPlayer,"player");
            PlotManager.dictPlotsActive=new Dictionary<string,JsonPlotSave>();
            foreach(var pair in new[]{new[]{"/ships/O-X5GYK/prune?dryRun=true","O-X5GYK"},new[]{"/ships/O-X5GYK/prune?confirm=PRUNE","O-X5GYK"}}) {
                var http=Http(pair[0]);Check(http.Error==null,"HTTP rejected supported POST");
                Check(ShipPruning.Parse(http,out string id,out bool dry,out string error) && id==pair[1] && error==null,"Route parse failed");
                Check(dry==pair[0].Contains("dryRun=true"),"Wrong dry-run dispatch");
            }
            foreach(string target in new[]{"/ships/O-A/prune","/ships/O-A/prune?confirm=prune","/ships/O-A/prune?dryRun=1","/ships/O-A/prune?dryRun=true&shipId=O-B","/ships/O%2FA/prune?dryRun=true","/ships//prune?dryRun=true"})
                Check(!ShipPruning.Parse(Http(target),out _,out _,out _),"Accepted malformed/ambiguous request: "+target);
            Check(!ShipPruning.Parse(Http("/ships/O-A/prune?dryRun=true","GET"),out _,out _,out _),"GET permitted prune dispatch");
            Check(!ShipPruning.Parse(Http("/world?dryRun=true","GET"),out _,out _,out _),"Unrelated route accepted dryRun");
            Check(Http("/ships/O-A/prune?dryRun=true&dryRun=true").Error!=null,"Duplicate query accepted");
            Check(!ShipPruning.IsRoute("/ships/prune"),"Missing-ID route overlaps path segments");
            var ship=Make("O-A",2);
            Check(ShipPruning.Run("O-A",false,null).Status==400 && hulls==0 && people==0,"Missing confirmation mutated state");
            var dryRun=ShipPruning.Run("O-A",true,null);
            Check((bool)Body(dryRun)["eligible"] && !(bool)Body(dryRun)["removed"] && people==0 && hulls==0,"Dry run mutated or refused pruneable ship");
            allowed.Remove("O-A-1");Refused("O-A","one protected crew among pruneable crew");allowed.Add("O-A-1");
            missingTrigger=true;Refused("O-A","missing native condition");missingTrigger=false;
            ship.objSS.bIsBO=true;Refused("O-A","station/body");ship.objSS.bIsBO=false;
            var owners=(Dictionary<string,string>)AccessTools.Field(typeof(StarSystem),"dictShipOwners").GetValue(world);
            owners["O-A"]="player";Refused("O-A","player ownership");owners.Clear();
            plotShips.Add("O-A");Refused("O-A","plot ship");plotShips.Clear();
            ship.objSS._dockGroup=new SolarSystem.DockGroup();Refused("O-A","docking group");ship.objSS._dockGroup=null;
            AccessTools.Field(typeof(Ship),"aPeople").SetValue(ship,new List<PersonSpec>{Empty<PersonSpec>(),Empty<PersonSpec>(),Empty<PersonSpec>()});
            Refused("O-A","unresolved crew profile");AccessTools.Field(typeof(Ship),"aPeople").SetValue(ship,null);
            Check(ShipPruning.Run("O",true,null).Status==404 && ShipPruning.Run("fixture",true,null).Status==404,"Nonexact call sign selected a ship");
            var success=ShipPruning.Run("O-A",false,"PRUNE");
            Check(success.Status==200 && (bool)Body(success)["removed"] && hulls==1 && people==2 && homeRemovals==2,"Approved prune did not remove exact hull/crew");
            Check(!world.dictShips.ContainsKey("O-A") && ShipPruning.Run("O-A",false,"PRUNE").Status==404,"Repeated prune not safely absent");
            var anchored=Make("O-ANCHOR");anchored.objSS.bBOLocked=true;
            Check((bool)Body(ShipPruning.Run("O-ANCHOR",true,null))["eligible"],"Explicit pruning incorrectly imposed orbit/distance rule");
            var privateHull=Make("O-PRIVATE");privateHull.IsAIShip=false;Refused("O-PRIVATE","unmarked empty non-AI hull");
            Make("O-PARTIAL",1);retainCrew=true;int hullCount=hulls;
            var partial=ShipPruning.Run("O-PARTIAL",false,"PRUNE");
            Check(partial.Status==409 && !(bool)Body(partial)["removed"] && hulls==hullCount && world.dictShips.ContainsKey("O-PARTIAL"),"Hull removed while crew remained");
            CrewSim.coPlayer=null;Check(ShipPruning.Run("O-ANCHOR",false,"PRUNE").Status==503,"Missing player protection allowed mutation");
            Console.WriteLine(checks+" ship-pruning checks passed (parser, dry-run, refusal, complete preflight and exact-target destruction dispatch).");return 0;
        } finally {h.UnpatchSelf();}
    }
}
namespace Ostrabus.Inspection {
    internal sealed class InspectionApi {
        internal sealed class Reply {
            internal int Status;internal object Body;
            internal Reply(int status,object body){Status=status;Body=body;}
        }
    }
}
