using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.Runtime.Serialization;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using Ostranauts.Ships.Comms;

// Native Hold/StickySequence bodies execute unchanged. Unity maneuvering,
// captain lookup, conditions and timer randomness are explicit fixture boundaries.
static partial class Program {
    static int checks, undocks, targetCalls;
    static bool help;
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Set(object o,string field,object value) => AccessTools.Field(o.GetType(),field).SetValue(o,value);
    static void Back(object o,string property,object value) => Set(o,"<"+property+">k__BackingField",value);
    static void Check(bool ok,string why) {checks++;if(!ok)throw new Exception(why);}
    static bool Equal(UnityEngine.Object __0,UnityEngine.Object __1,ref bool __result) {__result=ReferenceEquals(__0,__1);return false;}
    static bool Unequal(UnityEngine.Object __0,UnityEngine.Object __1,ref bool __result) {__result=!ReferenceEquals(__0,__1);return false;}
    static bool Quiet() => false;
    static bool Captain(ref CondOwner captain,ref bool __result) {captain=null;__result=false;return false;}
    static bool Trigger(ref bool __result) {__result=help;return false;}
    static bool Cond(string __0,ref CondTrigger __result) {__result=Empty<CondTrigger>();return false;}
    static bool Rand(ref double __result) {__result=100;return false;}
    static void ObserveUndock() {undocks++;}
    static void ObserveTarget() {targetCalls++;}
    sealed class UndockBoundary : BaseCommand {
        public override CommandStatus RunCommand(Blackboard b) {undocks++;return Success;}
    }
    static Ship MakeShip(double fuel) {
        var s=Empty<Ship>();s.strRegID="SCAV-FIXTURE";s.fShallowRCSRemass=fuel;
        Set(s,"nLoadState",Ship.Loaded.Shallow);Back(s,"objSS",Empty<ShipSitu>());Back(s,"Comms",Empty<Comms>());
        Set(s,"aDocked",new Dictionary<string,Ship>{{"port",Empty<Ship>()}});
        s.fAIDockingExpire=0;s.fAIPauseTimer=0;return s;
    }
    static void Main(string[] args) {
        var h=new Harmony("offline.scav-fuel.boundaries");
        void Patch(Type type,string name,string replacement,Type[] parameters=null) => h.Patch(AccessTools.Method(type,name,parameters),prefix:new HarmonyMethod(typeof(Program),replacement));
        Patch(typeof(UnityEngine.Object),"op_Equality",nameof(Equal));Patch(typeof(UnityEngine.Object),"op_Inequality",nameof(Unequal));
        Patch(typeof(Ship),nameof(Ship.Maneuver),nameof(Quiet));
        Patch(typeof(Comms),nameof(Comms.GetCaptain),nameof(Captain));
        Patch(typeof(DataHandler),nameof(DataHandler.GetCondTrigger),nameof(Cond),new[]{typeof(string)});
        Patch(typeof(CondTrigger),nameof(CondTrigger.Triggered),nameof(Trigger));
        Patch(typeof(MathUtils),nameof(MathUtils.Rand),nameof(Rand),new[]{typeof(double),typeof(double),typeof(MathUtils.RandType),typeof(string)});
        h.Patch(AccessTools.Method(typeof(Undock),nameof(Undock.RunCommand)),prefix:new HarmonyMethod(typeof(Program),nameof(ObserveUndock)));
        h.Patch(AccessTools.Method(typeof(TargetNewIfNone),nameof(TargetNewIfNone.RunCommand)),prefix:new HarmonyMethod(typeof(Program),nameof(ObserveTarget)));
        try {
            StarSystem.fEpoch=1000;
            CrewSim.system=Empty<StarSystem>();
            Set(CrewSim.system,"dictShipOwners",new Dictionary<string,string>{{"SCAV-FIXTURE","OKLGScav"}});
            if(args.Length>0 && args[0].StartsWith("fuel")) {StudyFlightFuel(h,args[0]=="fuel-patched");return;}
            if(args.Length>0 && args[0]=="refuel") {CheckRefuelApi(h);return;}
            if(args.Length>0 && args[0]=="sos") {CheckSosPaths(h,args.Length>1?args[1]:Ostrabus.Development.InstallationPaths.Game);return;}
            if(args.Length>0 && args[0]=="recovery") {CheckSosPaths(h,Ostrabus.Development.InstallationPaths.Game);CheckRecovery(h);return;}
            if(args.Length>0 && args[0]=="scope") {CheckNavigationScope(h);return;}
            if(args.Length>0 && args[0]=="replenish") {CheckReplenishment(h);return;}
            if(args.Length>0 && args[0]=="replenish-native") {CheckNativeReplenishment(h);return;}
            if(args.Length>0 && args[0]=="retarget") {CheckRetargeting(h);return;}
            var dispatchTarget=MakeShip(0);var notDocked=new JsonShipSpec {nIsDocked=-1};
            Check(!notDocked.Matches(dispatchTarget),"Native IsNotDocked specification accepted attached rescue target.");
            ((Dictionary<string,Ship>)AccessTools.Field(typeof(Ship),"aDocked").GetValue(dispatchTarget)).Clear();
            Check(notDocked.Matches(dispatchTarget),"Native IsNotDocked specification rejected disconnected rescue target.");
            foreach(double fuel in new[]{0d,.999,1d,10d}) foreach(bool requested in new[]{false,true}) {
                help=requested;var ship=MakeShip(fuel);var pilot=Empty<ScavPilot>();Back(pilot,"ShipUs",ship);
                var hold=new Hold(pilot);var b=new Blackboard();
                Check(hold.CanExecute(b),"Docked Hold must be executable.");
                var status=hold.RunCommand(b).Status;
                bool blocked=requested || fuel<1;
                Check(status==(blocked ? CommandCode.Running : CommandCode.Finished),"Native Hold fuel/help boundary mismatch.");
                Console.WriteLine("Hold fuel="+fuel+" help="+requested+" -> "+status);
            }
            help=false;var stuck=MakeShip(0);var scav=new ScavPilot(null,stuck);
            Check(scav.BehaviorTree.FindCommand(typeof(SosOutOfFuel))==null && scav.BehaviorTree.FindCommand(typeof(AmIOutOfFuel))==null,"Emergency fuel commands unexpectedly exist in actual scav tree.");
            var children=(List<ICommand>)AccessTools.Field(typeof(SelectorCommand),"_childCommands").GetValue(scav.BehaviorTree);
            var main=(StickySequenceCommand)children.Single(c=>(string)AccessTools.Field(typeof(BaseCommand),"_nodeName").GetValue(c)=="Main");
            var leaves=(List<ICommand>)AccessTools.Field(typeof(StickySequenceCommand),"_childCommands").GetValue(main);
            Check(string.Join(",",leaves.Select(c=>c.GetType().Name))=="Undock,TargetNewIfNone,FlyTo,Dock,Hold","Scav Main tree layout differs from graph.");
            main.SaveData=new[]{"4"};var bb=new Blackboard();
            for(int i=0;i<200;i++) {StarSystem.fEpoch+=700;Check(main.RunCommand(bb).Status==CommandCode.Running,"Empty Hold unexpectedly released Main.");}
            Check(main.SaveData[0]=="4" && undocks==0 && targetCalls==0,"Blocked Main reached downstream Undock/target entries.");
            var caughtUp=scav.FFWD(leaves[4],bb);
            Check(ReferenceEquals(caughtUp,leaves[4]) && main.SaveData[0]=="4","Docked Hold SFF unexpectedly advances Main.");
            Check(new OneShotWrapperCommand(caughtUp).RunCommand(bb).Status==CommandCode.Finished,"SFF Hold wrapper did not complete its single call.");
            Check(main.SaveData[0]=="4" && undocks==0,"SFF one-shot released actual Main hold.");
            stuck.fShallowRCSRemass=0;stuck.LiftRotorsThrustStrength=1;stuck.fAIDockingExpire=0;
            Check(main.RunCommand(bb).Status==CommandCode.Finished,"Positive rotor thrust did not bypass empty Hold mass gate.");
            main.SaveData=new[]{"4"};stuck.LiftRotorsThrustStrength=0;stuck.fAIDockingExpire=0;stuck.fAIPauseTimer=1;
            Check(main.RunCommand(bb).Status==CommandCode.Failure && main.SaveData[0]=="0","Pause/zero-timer exception failed to reset sequence.");
            main.SaveData=new[]{"4"};stuck.fAIPauseTimer=0;
            stuck.fShallowRCSRemass=10;stuck.fAIDockingExpire=0;
            Check(main.RunCommand(bb).Status==CommandCode.Finished && main.SaveData[0]=="0","Refueling should release native Hold.");
            // Only after proving the actual Main remains blocked, replace its
            // next physical undock leaf with a counting boundary stand-in.
            leaves[0]=new UndockBoundary();
            Check(main.RunCommand(bb).Status==CommandCode.Finished && undocks==1,"Next native Main tick should run Undock probe.");
            Console.WriteLine(checks+" native Hold/sequence checks passed. Probe leaves do not simulate physical undocking, comms, planning or fuel consumption.");
        } finally {h.UnpatchSelf();}
    }
}
