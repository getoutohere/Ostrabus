using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using Ostranauts.Ships.AIPilots.Interfaces;

static class Program {
    static int checks,cleanupCalls,collected,runBodies;
    static readonly Dictionary<CondOwner,HashSet<string>> conditions=new Dictionary<CondOwner,HashSet<string>>();
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Set(object instance,string field,object value) => AccessTools.Field(instance.GetType(),field).SetValue(instance,value);
    static void Back(object instance,string property,object value) => Set(instance,"<"+property+">k__BackingField",value);
    static void Check(bool ok,string why) { checks++;if(!ok)throw new Exception(why); }
    static bool Equal(UnityEngine.Object __0,UnityEngine.Object __1,ref bool __result) {__result=ReferenceEquals(__0,__1);return false;}
    static bool Unequal(UnityEngine.Object __0,UnityEngine.Object __1,ref bool __result) {__result=!ReferenceEquals(__0,__1);return false;}
    static bool NoConditions(CondOwner __instance,string __0,ref bool __result) {__result=conditions.TryGetValue(__instance,out var set) && set.Contains(__0);return false;}
    static bool SetCondition(CondOwner __instance,string __0,double __1) {
        if(!conditions.TryGetValue(__instance,out var set))conditions[__instance]=set=new HashSet<string>();
        if(__1>0)set.Add(__0);else set.Remove(__0);return false;
    }
    static bool ZeroCondition(CondOwner __instance,string __0) {if(conditions.TryGetValue(__instance,out var set))set.Remove(__0);return false;}
    static bool NoBehavior(bool __runOriginal) {if(__runOriginal)runBodies++;return false;}
    static bool Quiet() => false;
    static bool False(ref bool __result) {__result=false;return false;}
    // Unity thrust/condition updates are boundary stand-ins; docking decisions and crew enumeration are native.
    static bool Maneuver(Ship __instance) {__instance.objSS.vAccRCS=default;return false;}
    static bool Cleanup(Ship shipAI) {cleanupCalls++;collected=shipAI.GetPeople(true).Count;return false;}
    static Ship MakeShip(string id) {
        var ship=Empty<Ship>();ship.strRegID=id;Back(ship,"objSS",Empty<ShipSitu>());
        Back(ship,"ShipCO",Empty<CondOwner>());ship.ShipCO.ship=ship;
        Set(ship,"nLoadState",Ship.Loaded.Shallow);Set(ship,"aPeople",new List<PersonSpec>());Set(ship,"aDocked",new Dictionary<string,Ship>());
        return ship;
    }
    static CondOwner Person(Ship ship,string name) {
        var person=Empty<CondOwner>();Set(person,"_strID",name);person.ship=ship;person.bAlive=true;
        var spec=Empty<PersonSpec>();spec.strCO=name;Set(spec,"co",person);person.pspec=spec;
        ((List<PersonSpec>)AccessTools.Field(typeof(Ship),"aPeople").GetValue(ship)).Add(spec);
        return person;
    }
    static void Connect(Ship a,Ship b) {
        ((Dictionary<string,Ship>)AccessTools.Field(typeof(Ship),"aDocked").GetValue(a))["dock"]=b;
        ((Dictionary<string,Ship>)AccessTools.Field(typeof(Ship),"aDocked").GetValue(b))["dock"]=a;
    }
    static void Bind(Ship ship,Ship station) {ship.shipScanTarget=station;ship.shipSituTarget=station.objSS;ship.targetRegID=station.strRegID;}
    static CivilianPilot Pilot(Ship ship) {
        var pilot=Empty<CivilianPilot>();Back(pilot,"ShipUs",ship);pilot.Blackboard=new Blackboard();return pilot;
    }
    static AIShip Controller(Ship ship,BaseCharacter pilot=null) {
        pilot=pilot??Pilot(ship);Back(pilot,"ShipUs",ship);
        var ai=Empty<AIShip>();Set(ai,"_shipUs",ship);Set(ai,"_aiCharacter",pilot);Back(ai,"Blackboard",new Blackboard());return ai;
    }
    static int Registered => AIShipManager.dictAIs["HQCH"].Count;
    static void Install(Harmony h,string typeName) => h.CreateClassProcessor(typeof(Ostrabus.OstrabusPlugin).Assembly.GetType("Ostrabus.MiscBugFixes."+typeName,true)).Patch();
    static void Main(string[] args) {
        var boundary=new Harmony("offline.ai-lifecycle.boundaries");var patch=new Harmony("offline.ai-lifecycle.production");
        boundary.Patch(AccessTools.Method(typeof(UnityEngine.Object),"op_Equality"),prefix:new HarmonyMethod(typeof(Program),nameof(Equal)));
        boundary.Patch(AccessTools.Method(typeof(UnityEngine.Object),"op_Inequality"),prefix:new HarmonyMethod(typeof(Program),nameof(Unequal)));
        boundary.Patch(AccessTools.Method(typeof(CondOwner),nameof(CondOwner.HasCond),new[]{typeof(string),typeof(bool)}),prefix:new HarmonyMethod(typeof(Program),nameof(NoConditions)));
        boundary.Patch(AccessTools.Method(typeof(CondOwner),nameof(CondOwner.SetCondAmount)),prefix:new HarmonyMethod(typeof(Program),nameof(SetCondition)));
        boundary.Patch(AccessTools.Method(typeof(CondOwner),nameof(CondOwner.ZeroCondAmount)),prefix:new HarmonyMethod(typeof(Program),nameof(ZeroCondition)));
        boundary.Patch(AccessTools.Method(typeof(Ship),nameof(Ship.Maneuver)),prefix:new HarmonyMethod(typeof(Program),nameof(Maneuver)));
        boundary.Patch(AccessTools.Method(typeof(Ship),nameof(Ship.SetThrust)),prefix:new HarmonyMethod(typeof(Program),nameof(Quiet)));
        boundary.Patch(AccessTools.PropertyGetter(typeof(Ship),nameof(Ship.IsLocalAuthority)),prefix:new HarmonyMethod(typeof(Program),nameof(False)));
        boundary.Patch(AccessTools.Method(typeof(Ship),nameof(Ship.TowBracesSecured)),prefix:new HarmonyMethod(typeof(Program),nameof(False)));
        boundary.Patch(AccessTools.Method(typeof(CrewSim),nameof(CrewSim.DockAndDespawn)),prefix:new HarmonyMethod(typeof(Program),nameof(Cleanup)));
        boundary.Patch(AccessTools.Method(typeof(UnityEngine.Debug),nameof(UnityEngine.Debug.LogWarning),new[]{typeof(object)}),prefix:new HarmonyMethod(typeof(Program),nameof(Quiet)));
        try {
            StarSystem.fEpoch=65632375306.5815;
            var a=MakeShip("O-JVV");var b=MakeShip("O-KFMFL");var station=MakeShip("HQCH");station.objSS.bIsBO=true;
            var hope=Person(b,"Hope");Person(a,"Alondra");Connect(a,b);
            CrewSim.coPlayer=Empty<CondOwner>();CrewSim.coPlayer.ship=MakeShip("PLAYER");
            var pilot=Pilot(a);Bind(a,station);
            Check(a.GetPeople(false).Count==1 && a.GetPeople(true).Count==2,"Actual native crew enumeration includes neighboring pilot");
            Check(new Dock(pilot).RunCommand(pilot.Blackboard).Status==CommandCode.Failure && cleanupCalls==0,"Normal docking rejects already attached ship");
            Bind(a,station);
            Check(new Dock(pilot).ResolveInstantly(pilot.Blackboard).Status==CommandCode.Finished && cleanupCalls==1 && collected==2,"Native instant docking enters station cleanup with both pilots");
            cleanupCalls=0;Bind(a,station);
            var fly=Empty<FlyTo>();Set(fly,"_shipUs",a);
            Check(pilot.FFWD(fly,pilot.Blackboard) is Hold && cleanupCalls==1 && collected==2,"Native civilian SFF recreates destructive ordering hole");
            // A passive recipient keeps its own active station-return leaf. Exercise
            // the actual controller handoff, not just a direct pilot callback.
            pilot.PriorityQueue=new QueuedCommandsNode("PriorityQueue",pilot);
            var controller=Controller(a,pilot);var scheduledFly=new FlyTo(pilot);
            Set(controller,"_activeCommand",scheduledFly);cleanupCalls=0;Bind(a,station);
            controller.FFWD();
            Check(cleanupCalls==1 && collected==2 && pilot.PriorityQueue.CanExecute(pilot.Blackboard),"Native controller dispatches stale station arrival after incoming docking");

            CrewSim.system=Empty<StarSystem>();CrewSim.system.dictShips=new Dictionary<string,Ship>{{a.strRegID,a},{b.strRegID,b},{station.strRegID,station}};
            Set(CrewSim.system,"dictShipOwners",new Dictionary<string,string>{{a.strRegID,"HQCH"}});
            AIShipManager.ShipClosestPOI=station;a.HideFromSystem=true;a.strAIDespawnedAt="HQCH";
            Check(ReferenceEquals(AIShip.FindShipForFaction("HQCH"),a),"Native reserve selector reuses still-connected hidden hull");
            Console.WriteLine("Baseline: reproduced connected-crew cleanup, civilian SFF bypass and premature hull reuse.");
            if(args.Length==0 || args[0]!="patched")return;
            Install(patch,"InstantDockStatePatch");Install(patch,"DetachedReserveHullPatch");
            cleanupCalls=0;Bind(a,station);
            Check(new Dock(pilot).ResolveInstantly(pilot.Blackboard).Status==CommandCode.Failure && cleanupCalls==0,"Fixed instant docking rejects station completion while attached");
            Check(a.shipScanTarget==null && a.shipSituTarget==null,"Rejected arrival clears stale target like normal docking");
            Bind(a,station);Check(ReferenceEquals(pilot.FFWD(fly,pilot.Blackboard),fly) && cleanupCalls==0,"Civilian SFF no longer steals neighboring pilot");
            Check(hope.ship==b && b.GetPeople(false).Count==1,"Neighbor retains its real pilot");
            pilot.PriorityQueue=new QueuedCommandsNode("PriorityQueue",pilot);
            cleanupCalls=0;Bind(a,station);controller.FFWD();
            Check(cleanupCalls==0 && a.shipScanTarget==null && pilot.PriorityQueue.CanExecute(pilot.Blackboard),"Fixed native controller rejects obsolete station arrival before queuing normal continuation");
            Check(pilot.PriorityQueue.RunCommand(pilot.Blackboard).Status==CommandCode.Finished && !pilot.PriorityQueue.CanExecute(pilot.Blackboard),"Queued continuation drains without moving or retiring connected hulls");
            Check(a.IsDocked() && b.IsDocked() && a.GetPeople(false).Count==1 && b.GetPeople(false).Count==1,"Both crews and docking links survive controller handoff");
            // The native sticky sequence can advance through the rejected Dock to
            // its timed Hold. It is not left retrying station cleanup forever.
            a.fAIDockingExpire=120;
            var main=new StickySequenceCommand("Main",new ICommand[]{new Dock(pilot),new Hold(pilot)});
            var continuation=main.RunCommand(pilot.Blackboard);
            Check(continuation.Status==CommandCode.Running && continuation.ActiveCommand is Hold && cleanupCalls==0,"Native main sequence resumes rendezvous Hold after rejecting obsolete Dock");
            Check(new Undock(pilot).CanExecute(pilot.Blackboard),"Normal undocking remains eligible after rendezvous continuation");
            Check(AIShip.FindShipForFaction("HQCH")==null && a.HideFromSystem && a.IsDocked(),"Hidden attached hull remains reserved until separation");
            ((Dictionary<string,Ship>)AccessTools.Field(typeof(Ship),"aDocked").GetValue(a)).Clear();
            ((Dictionary<string,Ship>)AccessTools.Field(typeof(Ship),"aDocked").GetValue(b)).Clear();
            Check(ReferenceEquals(AIShip.FindShipForFaction("HQCH"),a),"Detached reserve hull is reusable normally");
            a.HideFromSystem=false;cleanupCalls=0;Bind(a,station);
            Check(new Dock(pilot).ResolveInstantly(pilot.Blackboard).Status==CommandCode.Finished && cleanupCalls==1 && collected==1,"Independent station return still completes with its own pilot");
            Install(patch,"CrewedAIRegistrationPatch");Install(patch,"CrewedAIExecutionPatch");Install(patch,"ShallowUndockCrewPatch");
            boundary.Patch(AccessTools.Method(typeof(AIShip),nameof(AIShip.RunAI)),prefix:new HarmonyMethod(typeof(Program),nameof(NoBehavior)){priority=Priority.Last});
            boundary.Patch(AccessTools.Method(typeof(AIShip),nameof(AIShip.FFWD)),prefix:new HarmonyMethod(typeof(Program),nameof(NoBehavior)){priority=Priority.Last});
            AIShipManager.dictAIs=new Dictionary<string,List<AIShip>>{{"HQCH",new List<AIShip>()}};
            var empty=MakeShip("CREWLESS");empty.objSS.vAccRCS=new UnityEngine.Vector2(1,2);empty.objSS.vAccIn=new UnityEngine.Vector2(3,4);empty.objSS.bIgnoreGrav=true;
            var emptyAI=Controller(empty);AIShipManager.AddAIToDict(emptyAI,"HQCH");
            Check(Registered==0 && empty.objSS.vAccRCS.magnitude==0 && empty.objSS.vAccIn.magnitude==0 && !empty.objSS.bIgnoreGrav,"Saved crewless controller cannot retain thrust after registration");
            Check(!empty.HideFromSystem && !empty.bDestroyed && !empty.bRemove && !empty.ShipCO.HasCond("IsStale"),"Stopped hull stays visible and gains no disposal marker");
            var crewed=MakeShip("CREWED");var captain=Person(crewed,"Real Pilot");var crewedAI=Controller(crewed);
            AIShipManager.AddAIToDict(crewedAI,"HQCH");Check(Registered==1,"Healthy crew survives controller registration");
            runBodies=0;crewedAI.RunAI();Check(runBodies==1 && Registered==1,"Healthy crew can execute AI");
            ((List<PersonSpec>)AccessTools.Field(typeof(Ship),"aPeople").GetValue(crewed)).Clear();captain.ship=station;
            crewed.objSS.vAccRCS=new UnityEngine.Vector2(5,6);runBodies=0;crewedAI.RunAI();
            Check(runBodies==0 && Registered==0 && crewed.objSS.vAccRCS.magnitude==0,"Pilot departure stops scheduled AI before it can command another burn");
            crewed.objSS.vAccRCS=new UnityEngine.Vector2(5,6);runBodies=0;crewedAI.FFWD();
            Check(runBodies==0 && crewed.objSS.vAccRCS.magnitude==0,"Crewless SFF execution cannot burn indefinitely");
            foreach(BaseCharacter exempt in new BaseCharacter[]{Empty<AutoPilot>(),Empty<StationPilot>(),Empty<MissilePilot>(),Empty<ArenaModePilot>()}) {
                var special=MakeShip("EXEMPT");var specialAI=Controller(special,exempt);runBodies=0;specialAI.RunAI();
                Check(runBodies==1,"Intentional uncrewed controller remains native: "+exempt.AIType);
            }
            var transfer=MakeShip("TRANSFER");var moved=Person(transfer,"Moved Pilot");moved.ship=station;
            AIShipManager.AddAIToDict(Controller(transfer),"HQCH");Check(Registered==0,"Stale roster reference to a pilot on another ship cannot authorize AI");
            var relief=MakeShip("RELIEF");var oldPilot=Person(relief,"Old Pilot");var reliefPilot=Person(relief,"Relief Pilot");oldPilot.bAlive=false;
            AIShipManager.AddAIToDict(Controller(relief),"HQCH");Check(Registered==1,"Another surviving crew member can continue control");
            conditions[reliefPilot]=new HashSet<string>{"Unconscious"};
            var reliefAI=AIShipManager.GetAIShipByRegID("RELIEF");reliefAI.RunAI();
            Check(Registered==0 && !relief.HideFromSystem,"No conscious crew stops control without erasing hull");
            var stale=MakeShip("ALREADY STALE");conditions[stale.ShipCO]=new HashSet<string>{"IsStale"};
            AIShipManager.AddAIToDict(Controller(stale),"HQCH");Check(stale.ShipCO.HasCond("IsStale"),"Pre-existing disposal policy remains unchanged");
            Connect(a,b);cleanupCalls=0;CrewSim.DockAndDespawn(a,station);
            Check(cleanupCalls==1 && collected==2,"Legitimate explicit connected-party cleanup semantics remain native");
            Console.WriteLine(checks+" native lifecycle checks passed.");
        } finally {patch.UnpatchSelf();boundary.UnpatchSelf();}
    }
}
