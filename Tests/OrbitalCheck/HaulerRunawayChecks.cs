using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ostranauts.Core;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.AIPilots.Interfaces;
using Ostranauts.Ships.Commands;
using Ostrabus.Orbital;
using UnityEngine;

// Diagnosis only. Exercises shipped commands/motion with Orbital disabled.
static class HaulerRunawayChecks {
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    internal static void Run(Action<bool,string> check) {
        bool enabled=OrbitalSimulation.Enabled;
        var flag=AccessTools.Property(typeof(OrbitalSimulation),"Enabled");
        flag.SetValue(null,false);
        try {
            string assets =Ostrabus.Development.InstallationPaths.Data;
            var template=JToken.Parse(File.ReadAllText(assets+"ships/Light Tug.json"));
            if (template is JArray) template=template[0];
            check(!(bool)template["bFusionTorch"] && (double)template["fFusionThrustMax"]==0,"Light Tug asset unexpectedly torch-capable");
            check((double)template["nRCSCount"]==8 && Math.Abs((double)template["fShallowMass"]-6827.87)<.001,"Live hull does not match asset mass/thrusters");
            var ship=Empty<Ship>();ship.strRegID="hauler-proof";ship.IsAIShip=true;
            var s=Empty<ShipSitu>();AccessTools.Property(typeof(Ship),nameof(Ship.objSS)).SetValue(ship,s);
            AccessTools.Field(typeof(Ship),"nLoadState").SetValue(ship,Ship.Loaded.Shallow);
            AccessTools.Field(typeof(Ship),"fShallowRCSRemass").SetValue(ship,3364.756347659432);
            var station=Empty<Ship>();station.strRegID="OKLG";
            AccessTools.Property(typeof(Ship),nameof(Ship.objSS)).SetValue(station,Empty<ShipSitu>());
            var pilot=new Pilot {ShipUs=ship,Destination=station,Blackboard=new Blackboard()};
            pilot.PriorityQueue=new QueuedCommandsNode("PriorityQueue",pilot);
            var measured=JObject.Parse(FixtureFiles.Read("OKLG-OUTER-SURVEY/O-X5GYK-ACCEL.json"));
            JToken motion=null;
            foreach(var item in measured["data"]["ships"]) if ((string)item["id"]=="O-X5GYK") motion=item["motion"];
            s.vAccRCS=new Vector2((float)motion["rcsAcceleration"]["x"],(float)motion["rcsAcceleration"]["y"]);
            Vector2 latched=s.vAccRCS;
            new TargetData(station).ApplyToShip(ship);
            var undock=new Undock(pilot);
            check(undock.ResolveInstantly(pilot.Blackboard).Status==CommandCode.Finished,"Native undock cleanup did not complete");
            check(ship.shipScanTarget==null && ship.shipSituTarget==null,"Native cleanup retained return target");
            check(s.vAccRCS.x==latched.x && s.vAccRCS.y==latched.y,"Undock cleanup cleared latched thrust; hypothesis disproved");
            // This is the actual native deployer Main sequence after dropping its tow.
            var tree=new StickySequenceCommand("Main",new ICommand[]{new Dock(pilot),new HaulShip(pilot),new AnchorDockedShip(pilot),new FlyTo(pilot)});
            var ai=Empty<AIShip>();
            AccessTools.Field(typeof(AIShip),"_shipUs").SetValue(ai,ship);
            AccessTools.Field(typeof(AIShip),"_aiCharacter").SetValue(ai,pilot);
            AccessTools.Field(typeof(AIShip),"_behaviorTree").SetValue(ai,tree);
            AccessTools.Property(typeof(AIShip),"Blackboard").SetValue(ai,pilot.Blackboard);
            for(int i=0;i<100;i++) ai.RunAI();
            check(pilot.TargetCalls==100,"Empty HaulShip did not repeatedly request home target");
            check(ship.shipScanTarget==null && ship.shipSituTarget==null,"Empty HaulShip did not discard home target");
            check(ai.ActiveCommandName==null,"Native failed behavior tree retained active command");
            check(s.vAccRCS.x==latched.x && s.vAccRCS.y==latched.y,"Native command failure stopped RCS; hypothesis disproved");
            double fuel=ship.fShallowRCSRemass;
            check(ship.RemoveGasMass(10,true)==10 && ship.fShallowRCSRemass==fuel,"Shallow AI fuel bypass not reproduced");
            check(ship.RemoveGasMass(10,false)==10 && Math.Abs(ship.fShallowRCSRemass-(fuel-10))<1e-6,"Ordinary fuel deduction control failed");
            double before=ship.fShallowRCSRemass;
            s.TimeAdvance(3600);
            double speed=new DVector(s.vVelX,s.vVelY).Length*OrbitalMotion.KmPerAU;
            check(speed>102 && speed<103,"Latched native RCS did not reproduce impossible speed: "+speed);
            check(ship.fShallowRCSRemass==before,"Native motion integrated fuel consumption unexpectedly");
            var report=new { orbitalEnabled=false,asset="ships/Light Tug.json",nativeAiCycles=100,homeTargetRequests=pilot.TargetCalls,
                activeCommand=ai.ActiveCommandName,target=ship.shipScanTarget?.strRegID,rcsX=latched.x,rcsY=latched.y,
                seconds=3600,speedGainKmPerS=speed,fuelBeforeNativeMotionKg=before,fuelAfterNativeMotionKg=ship.fShallowRCSRemass,
                note="Shipped Undock cleanup, complete deployer Main command sequence, AIShip.RunAI, fuel methods and ShipSitu.TimeAdvance. No command/motion/fuel stubs; Orbital disabled. Seeded measured pre-existing RCS vector, not a reproduction of its original writer." };
            File.WriteAllText("obj/HAULER-VANILLA-REPRODUCTION.json",JsonConvert.SerializeObject(report,Formatting.Indented));
            Console.WriteLine("Native hauler failure reproduced with Orbital OFF: 100 failed AI cycles retain RCS, +"+speed+" km/s in one hour, zero motion fuel debit.");
        } finally { flag.SetValue(null,enabled); }
    }
    sealed class Pilot : IAICharacter {
        internal Ship Destination;
        internal int TargetCalls;
        public Ship ShipUs {get;set;}
        public CondOwner CaptainUs=>null;
        public QueuedCommandsNode PriorityQueue {get;set;}
        public ICommand BehaviorTree {get;set;}
        public Blackboard Blackboard {get;set;}
        public AIType AIType=>AIType.HaulerDeployer;
        public TargetData GetTarget() { TargetCalls++;return new TargetData(Destination); }
        public TargetData GetTargetHome()=>new TargetData(Destination);
        public double MaxSpeed(bool? inAtmo=null)=>5.013440329548757e-9;
        public ICommand FFWD(ICommand command,Blackboard blackboard)=>command;
    }
}
