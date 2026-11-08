using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ostrabus.ShipNavigation;
using Ostranauts.Core;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots.Interfaces;
using Ostranauts.Ships.Commands;
using Ostranauts.ShipGUIs.Utilities;

static class Program {
    const double KmAU=149597872;
    static int checks;
    static Ship home;
    static bool playerManned=false;
    static T Empty<T>()=>(T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Check(bool value,string why){checks++;if(!value)throw new Exception(why);}
    static bool Equal(UnityEngine.Object __0,UnityEngine.Object __1,ref bool __result){__result=ReferenceEquals(__0,__1);return false;}
    static bool Unequal(UnityEngine.Object __0,UnityEngine.Object __1,ref bool __result){__result=!ReferenceEquals(__0,__1);return false;}
    static bool Nearest(ref Ship __result){__result=home;return false;}
    static bool Body(ShipSitu __0,double __1,ref BodyOrbit __result){__result=CrewSim.system.aBOs["Earth"];__result.UpdateTime(__1);return false;}
    static bool Gravity(ref BodyOrbit boClosest,ref BodyOrbit __result){boClosest=CrewSim.system.aBOs["Earth"];__result=boClosest;return false;}
    static bool NoWake(ref bool __result){__result=true;return false;}
    static bool Player(ref bool __result){__result=playerManned;return false;}
    static bool Accel(ref double __result){__result=1.05408240382365e-10;return false;}
    static bool Fuel(ref double __result){__result=0;return false;}
    static bool Torch(ref float __result){__result=1e-8f;return false;}
    static bool NoDetour(ref ShipSitu __result){__result=null;return false;}
    static bool Collision(ref float __result){__result=0;return false;}
    static Ship Make(string id,string reference,double epoch) {
        var ship=Empty<Ship>();ship.strRegID=id;ship.IsAIShip=true;ship.bFusionReactorRunning=true;
        var s=new ShipSitu(false){strBOPORShip=reference,bIsBO=true};s.UpdateTime(epoch);
        AccessTools.Field(typeof(Ship),"<objSS>k__BackingField").SetValue(ship,s);
        CrewSim.system.dictShips[id]=ship;return ship;
    }
    static NavData Plot(Ship ship,ShipSitu origin,ShipSitu destination,double epoch) {
        home?.objSS.UpdateTime(epoch); destination.UpdateTime(epoch);
        var planner=new FlyToPath(new Pilot{ShipUs=ship});
        return planner.PlanTrip4(new NavDataPoint(epoch,new ShipSitu(origin,false),2000,0,47122),new NavDataPoint(epoch,new ShipSitu(destination,false),2000,0,47122),1,1);
    }
    static void Main() {
        var h=new Harmony("offline.departure.boundaries");var production=new Harmony("offline.departure.production");
        void Patch(System.Reflection.MethodBase method,string prefix)=>h.Patch(method,prefix:new HarmonyMethod(typeof(Program),prefix));
        Patch(AccessTools.Method(typeof(UnityEngine.Object),"op_Equality"),nameof(Equal));
        Patch(AccessTools.Method(typeof(UnityEngine.Object),"op_Inequality"),nameof(Unequal));
        Patch(AccessTools.Method(typeof(StarSystem),nameof(StarSystem.GetNearestStation)),nameof(Nearest));
        Patch(AccessTools.Method(typeof(StarSystem),nameof(StarSystem.GetNearestBO)),nameof(Body));
        Patch(AccessTools.Method(typeof(StarSystem),nameof(StarSystem.GetGreatestGravBO)),nameof(Gravity));
        Patch(AccessTools.Method(typeof(StarSystem),nameof(StarSystem.IsWithinNoWakeRangeOfAnyStation)),nameof(NoWake));
        Patch(AccessTools.PropertyGetter(typeof(Ship),nameof(Ship.NavPlayerManned)),nameof(Player));
        Patch(AccessTools.PropertyGetter(typeof(Ship),nameof(Ship.RCSAccelMax)),nameof(Accel));
        Patch(AccessTools.Method(typeof(Ship),nameof(Ship.GetMaxTorchThrust)),nameof(Torch));
        foreach(var m in AccessTools.GetDeclaredMethods(typeof(Ship)))if(m.Name=="CalculateRCSFuelConsumption")Patch(m,nameof(Fuel));
        Patch(AccessTools.Method(typeof(FlightCPU),nameof(FlightCPU.GetDockingNavPoint),new[]{typeof(ShipSitu),typeof(ShipSitu),typeof(double),typeof(bool)}),nameof(NoDetour));
        Patch(AccessTools.Method(typeof(CollisionManager),nameof(CollisionManager.GetCollisionDistanceAU),new[]{typeof(ShipSitu),typeof(ShipSitu)}),nameof(Collision));
        try {
            var fixture=JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"orbits.json")));
            var world=CrewSim.system=Empty<StarSystem>();world.aBOs=new Dictionary<string,BodyOrbit>();world.dictShips=new Dictionary<string,Ship>();
            foreach(var token in fixture["bodies"]){var b=new BodyOrbit(token.ToObject<JsonBodyOrbitSave>());world.aBOs[b.strName]=b;}
            foreach(string pair in fixture["parents"].Values<string>()){var kv=pair.Split('=');if(world.aBOs.TryGetValue(kv[0],out var child)&&world.aBOs.TryGetValue(kv[1],out var parent))child.boParent=parent;}
            double epoch=(double)fixture["origin"]["epoch"];StarSystem.fEpoch=epoch;
            home=Make("MHNG","MHNG",epoch);
            var ship=Make("AI","Sol",epoch);ship.objSS.bIsBO=false;
            var o=fixture["origin"];ship.objSS.vPosx=(double)o["x"];ship.objSS.vPosy=(double)o["y"];ship.objSS.vVelX=(double)o["vx"];ship.objSS.vVelY=(double)o["vy"];
            var target=Make("MTRS","MTRS",epoch).objSS;
            var old=Plot(ship,ship.objSS,target,epoch);
            double oldHours=(old.Destination.ArrivalTime-epoch)/3600;
            Check(oldHours>1,"Native branch reproduces hours-long local departure");
            ship.IsAIShip=false;var playerBefore=Plot(ship,ship.objSS,target,epoch);ship.IsAIShip=true;
            playerManned=true;var mannedBefore=Plot(ship,ship.objSS,target,epoch);playerManned=false;
            Check(production.CreateClassProcessor(typeof(DepartureWaypointPatch)).Patch().Count==1,"Production transpiler binds native planner");
            var corrected=Plot(ship,ship.objSS,target,epoch);
            double minutes=(corrected.Destination.ArrivalTime-epoch)/60;
            home.objSS.UpdateTime(corrected.Destination.ArrivalTime);
            Check(minutes>0 && minutes<30,"Patched native branch creates a minutes-long local departure");
            Check(Math.Abs((corrected.Destination.ObjSS.vPos-home.objSS.vPos).magnitude*KmAU-385)<5,"Patched native endpoint remains near departure station");
            ship.IsAIShip=false;var playerAfter=Plot(ship,ship.objSS,target,epoch);
            Check(Math.Abs(playerAfter.Destination.ArrivalTime-playerBefore.Destination.ArrivalTime)<.01 && (playerAfter.Destination.ObjSS.vPos-playerBefore.Destination.ObjSS.vPos).magnitude<1e-10,"Unattended player hull retains native plotting");ship.IsAIShip=true;
            playerManned=true;var mannedAfter=Plot(ship,ship.objSS,target,epoch);playerManned=false;
            Check(Math.Abs(mannedAfter.Destination.ArrivalTime-mannedBefore.Destination.ArrivalTime)<.01 && (mannedAfter.Destination.ObjSS.vPos-mannedBefore.Destination.ObjSS.vPos).magnitude<1e-10,"Player-manned course remains native");
            Console.WriteLine("Native departure branch: "+oldHours+" hours -> "+minutes+" minutes.");
            foreach(string id in new[]{"MHNG","BCRS","VORB","MTRS","OKLG"}) foreach(double phase in new[]{0d,.25,.5,.75}) {
                var body=world.aBOs[id];double at=epoch+body.fPeriod*phase;StarSystem.fEpoch=at;
                home=Make(id,id,at);
                var point=new ShipSitu(home.objSS,false);point.bIsBO=false;point.vPosx+=385/KmAU;
                var planner=new FlyToPath(ship,false);var origin=new NavDataPoint(at,new ShipSitu(home.objSS,false),100,0,100);
                var original=point.vPos;
                DepartureWaypointPatch.Bind(point,at,false,planner,origin);
                Check(point.strBOPORShip==id && (point.vPos-original).magnitude<1e-15,"Correct station reference without teleport: "+id);
                point.UpdateTime(at+600);home.objSS.UpdateTime(at+600);
                Check(Math.Abs((point.vPos-home.objSS.vPos).magnitude*KmAU-385)<.01,"Moving waypoint keeps local offset: "+id);
                Check((point.vVel-home.objSS.vVel).magnitude<1e-15,"Waypoint follows station velocity: "+id);
            }
            Check(world.aBOs["BCRS"].boParent.strName=="BCRS_Anchor","BCRS own hierarchy retained");
            home=null;var fallback=new ShipSitu(false){vPosx=1};
            DepartureWaypointPatch.Bind(fallback,epoch,false,new FlyToPath(ship,false),new NavDataPoint(epoch,new ShipSitu(false),0,0,0));
            Check(fallback.strBOPORShip=="Earth","Missing departure depot preserves native fallback");
            home=Make("BROKEN","missing",epoch);fallback=new ShipSitu(false){vPosx=1};
            DepartureWaypointPatch.Bind(fallback,epoch,false,new FlyToPath(ship,false),new NavDataPoint(epoch,new ShipSitu(false),0,0,0));
            Check(fallback.strBOPORShip=="Earth","Missing station orbit preserves native fallback");
            Console.WriteLine(checks+" departure waypoint checks passed.");
        } finally {production.UnpatchSelf();h.UnpatchSelf();}
    }
    sealed class Pilot:IAICharacter {
        public Ship ShipUs{get;set;}
        public CondOwner CaptainUs=>null;
        public QueuedCommandsNode PriorityQueue{get;set;}
        public ICommand BehaviorTree{get;set;}
        public Blackboard Blackboard{get;set;}
        public AIType AIType=>AIType.HaulerCargo;
        public TargetData GetTarget()=>null;
        public TargetData GetTargetHome()=>null;
        public double MaxSpeed(bool? inAtmo=null)=>4.75619971619945e-9;
        public ICommand FFWD(ICommand command,Blackboard blackboard)=>command;
    }
}
