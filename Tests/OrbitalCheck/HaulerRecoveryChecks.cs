using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.ShipGUIs.Utilities;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.AIPilots.Interfaces;
using Ostranauts.Ships.Commands;
using Ostrabus.Haulers;
using UnityEngine;

static class HaulerRecoveryChecks {
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static Ship Make(string id,Ship.Loaded load=Ship.Loaded.Shallow) {
        var ship=Empty<Ship>();ship.strRegID=id;ship.IsAIShip=true;
        AccessTools.Property(typeof(Ship),nameof(Ship.objSS)).SetValue(ship,Empty<ShipSitu>());
        AccessTools.Field(typeof(Ship),"nLoadState").SetValue(ship,load);
        AccessTools.Field(typeof(Ship),"aNavs").SetValue(ship,new List<CondOwner>());
        return ship;
    }
    internal static void Run(Action<bool,string> check) {
        var savedWorld=CrewSim.system;var savedAis=AIShipManager.dictAIs;
        var harmony=new Harmony("hauler.recovery.check");
        try {
            foreach(var type in new[]{typeof(HaulShipReturnPatch),typeof(AnchorDockedShipReturnPatch),typeof(HaulerIdleManeuverPatch)})
                check(harmony.CreateClassProcessor(type).Patch().Count==1,"Hauler patch binding: "+type.Name);
            foreach(string stationId in new[]{"OKLG","VORB","OTHER"}) {
                var ship=Make("hauler-"+stationId);var station=Make(stationId);
                var world=Empty<StarSystem>();world.dictShips=new Dictionary<string,Ship>{{ship.strRegID,ship},{stationId,station}};
                CrewSim.system=world;
                var pilot=new Pilot {ShipUs=ship,Destination=station,Blackboard=new Blackboard()};
                pilot.PriorityQueue=new QueuedCommandsNode("PriorityQueue",pilot);
                var ai=Empty<AIShip>();
                AccessTools.Field(typeof(AIShip),"_shipUs").SetValue(ai,ship);
                AccessTools.Field(typeof(AIShip),"_aiCharacter").SetValue(ai,pilot);
                AccessTools.Property(typeof(AIShip),"HomeStation").SetValue(ai,stationId);
                AccessTools.Property(typeof(AIShip),"Blackboard").SetValue(ai,pilot.Blackboard);
                AIShipManager.dictAIs=new Dictionary<string,List<AIShip>>{{stationId,new List<AIShip>{ai}}};
                var active=AccessTools.Field(typeof(AIShip),"_activeCommand");
                var reaction=AccessTools.Field(typeof(AIShip),"_activeReaction");
                var running=new ProbeCommand(pilot,CommandCode.Running);
                var failure=new ProbeCommand(pilot,CommandCode.Failure);
                var s=ship.objSS;
                void Reset() {
                    ship.ClearShipTarget();s.NavData=null;s.bBOLocked=s.bOrbitLocked=s.bIsBO=false;s._dockGroup=null;
                    s.vAccIn=default;s.vAccRCS=new Vector2(0,1.9016982e-10f);s.fA=.01f;s.fW=.03f;
                    ship.HideFromSystem=false;ship.IsAIShip=true;ship.shipCombatTarget=null;
                    AccessTools.Field(typeof(Ship),"nLoadState").SetValue(ship,Ship.Loaded.Shallow);
                    active.SetValue(ai,null);reaction.SetValue(ai,null);
                    pilot.Kind=AIType.HaulerDeployer;pilot.Blackboard.Set("InCombat",false);
                    pilot.Blackboard.Remove("SurrenderedTo");
                    pilot.PriorityQueue=new QueuedCommandsNode("PriorityQueue",pilot);
                    AccessTools.Field(typeof(AIShip),"_behaviorTree").SetValue(ai,failure);
                }
                // Real empty HaulShip: native failure status retained, target repaired.
                foreach(bool initialTarget in new[]{false,true}) {
                    Reset();s.vVelX=.000001;double fuel=ship.fShallowRCSRemass;
                    if(initialTarget) new TargetData(station).ApplyToShip(ship);
                    var result=new HaulShip(pilot).RunCommand(pilot.Blackboard);
                    check(result.Status==CommandCode.Failure,"Native haul failure changed");
                    check(ship.shipScanTarget==station && ship.shipSituTarget==station.objSS,"Home target not restored: "+stationId);
                    check(s.vAccRCS.x==0 && s.vAccRCS.y==0 && s.fA==0,"Empty-haul orphan thrust retained");
                    check(s.vVelX==.000001 && s.fW==.03f && ship.fShallowRCSRemass==fuel,"Cleanup changed momentum or fuel");
                }
                // Actual AIShip.RunAI exits with no command: idle safeguard also
                // handles already-runaway saves without needing a new drop-off.
                Reset();s.vVelX=.000001;
                ai.RunAI();
                check(ai.ActiveCommandName==null && s.vAccRCS.y==0 && ship.shipScanTarget==station,"Idle recovery failed: "+stationId);
                check(s.vVelX==.000001,"Existing runaway velocity was overwritten");
                var state=(Dictionary<string,object>)HaulerRecovery.Snapshot(ship);
                check((long)state["abandonedManeuversStopped"]>=3 && (long)state["homeTargetsRestored"]>=3,"Recovery diagnostics missing");
                // A pending/active controller owns the maneuver, even if another
                // command/root reports failure on this cycle.
                foreach(string mode in new[]{"active","queued","queued-active","reaction","combat","surrendered","navdata","torch","towed","bodylock","orbitlock","body","hidden","otherpilot","playerflag","full"}) {
                    Reset();
                    if(mode=="active") active.SetValue(ai,running);
                    if(mode=="queued") pilot.PriorityQueue.EnqueueCommand(running);
                    if(mode=="queued-active") AccessTools.Field(typeof(QueuedCommandsNode),"currentCommand").SetValue(pilot.PriorityQueue,running);
                    if(mode=="reaction") reaction.SetValue(ai,running);
                    if(mode=="combat") pilot.Blackboard.Set("InCombat",true);
                    if(mode=="surrendered") pilot.Blackboard.Set("SurrenderedTo",station);
                    if(mode=="navdata") s.NavData=Empty<NavData>();
                    if(mode=="torch") s.vAccIn=new Vector2(1e-10f,0);
                    if(mode=="towed") s._dockGroup=new SolarSystem.DockGroup();
                    if(mode=="bodylock") s.bBOLocked=true;
                    if(mode=="orbitlock") s.bOrbitLocked=true;
                    if(mode=="body") s.bIsBO=true;
                    if(mode=="hidden") ship.HideFromSystem=true;
                    if(mode=="otherpilot") pilot.Kind=AIType.Scav;
                    if(mode=="playerflag") ship.IsAIShip=false;
                    if(mode=="full") AccessTools.Field(typeof(Ship),"nLoadState").SetValue(ship,Ship.Loaded.Full);
                    HaulerRecovery.AfterIdleAI(ai);
                    check(s.vAccRCS.y!=0 && ship.shipScanTarget==null,"Legitimate control changed: "+mode);
                }
                Reset();
                // Valid steering in the same RunAI cycle survives the postfix.
                AccessTools.Field(typeof(AIShip),"_behaviorTree").SetValue(ai,running);
                ai.RunAI();check(s.vAccRCS.y!=0,"Running successor steering cancelled");
                // Missing/destroyed homes stop the orphan input but cannot invent a destination.
                foreach(bool missing in new[]{false,true}) {
                    Reset();station.bDestroyed=!missing;
                    if(missing) world.dictShips.Remove(stationId);
                    ai.RunAI();check(s.vAccRCS.y==0 && ship.shipScanTarget==null,"Invalid home created target or left thrust");
                    station.bDestroyed=false;world.dictShips[stationId]=station;
                }
                // Cross-call release state contains only weakly-owned scalar state.
                Reset();HaulerRecovery.MarkRelease(ship);
                AnchorDockedShipReturnPatch.Postfix(new AnchorDockedShip(pilot),new CommandStatus(CommandCode.Finished,null));
                check(!HaulerRecovery.PendingRelease(ship) && ship.shipScanTarget==station && s.vAccRCS.y==0,"Drop-off handoff failed");
                Reset();HaulerRecovery.MarkRelease(ship);
                var error=new Exception("release probe");
                check(ReferenceEquals(AnchorDockedShipReturnPatch.Finalizer(new AnchorDockedShip(pilot),error),error) && !HaulerRecovery.PendingRelease(ship),"Release exception retained pending state");
                Reset();new TargetData(station).ApplyToShip(ship);s.vAccRCS=default;s.fA=0;
                HaulerRecovery.AfterIdleAI(ai);
                long before=GC.GetAllocatedBytesForCurrentThread();
                for(int i=0;i<100;i++) HaulerRecovery.AfterIdleAI(ai);
                check(GC.GetAllocatedBytesForCurrentThread()==before,"Idle recovery allocates on unchanged state");
                HaulerRecovery.SceneUnloaded(default);
                check(HaulerRecovery.Snapshot(ship)==null && !HaulerRecovery.PendingRelease(ship),"Scene reset retained state");
            }
            Console.WriteLine("General hauler recovery: OKLG/VORB/other home, real haul/AI hooks, control exclusions, momentum, diagnostics and zero-allocation idle checks passed.");
        } finally { harmony.UnpatchSelf();HaulerRecovery.SceneUnloaded(default);CrewSim.system=savedWorld;AIShipManager.dictAIs=savedAis; }
    }
    sealed class Pilot:IAICharacter {
        internal AIType Kind=AIType.HaulerDeployer;
        internal Ship Destination;
        public Ship ShipUs{get;set;}
        public CondOwner CaptainUs=>null;
        public QueuedCommandsNode PriorityQueue{get;set;}
        public ICommand BehaviorTree{get;set;}
        public Blackboard Blackboard{get;set;}
        public AIType AIType=>Kind;
        public TargetData GetTarget()=>new TargetData(Destination);
        public TargetData GetTargetHome()=>new TargetData(Destination);
        public double MaxSpeed(bool? inAtmo=null)=>5.013440329548757e-9;
        public ICommand FFWD(ICommand command,Blackboard board)=>command;
    }
    sealed class ProbeCommand:BaseCommand {
        readonly CommandCode result;
        internal ProbeCommand(IAICharacter pilot,CommandCode code):base(pilot){result=code;}
        public override CommandStatus RunCommand(Blackboard board)=>new CommandStatus(result,this);
    }
}
