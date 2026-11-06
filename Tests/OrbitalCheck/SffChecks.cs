using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ostranauts.ShipGUIs.Utilities;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.AIPilots.Interfaces;
using Ostranauts.Ships.Commands;
using Ostranauts.Core;
using Ostranauts.Ships;
using Ostrabus.Orbital;

static class SffChecks {
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static double D(JToken j,string name) => (double)j[name];
    static readonly BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;
    static SffSimulation Context(double start,double duration) => (SffSimulation)AccessTools.Constructor(typeof(SffSimulation),new[]{typeof(double),typeof(double)}).Invoke(new object[]{start,duration});
    static void Register(SffSimulation context,ShipSitu situation,SffSimulation.Plan plan,bool member=false,DVector offset=default,double angle=0) {
        Type type=typeof(SffSimulation).GetNestedType("Entry",BindingFlags.NonPublic);
        object entry=Activator.CreateInstance(type,true);
        type.GetField("Plan",Private).SetValue(entry,plan);type.GetField("Member",Private).SetValue(entry,member);
        type.GetField("Offset",Private).SetValue(entry,offset);type.GetField("RotationOffset",Private).SetValue(entry,angle);
        ((IDictionary)typeof(SffSimulation).GetField("entries",Private).GetValue(context)).Add(situation,entry);
    }
    public static void Run(Action<bool,string> check) {
        var snapshot=JObject.Parse(FixtureFiles.Read("VORB-BOUND-CURRENT.json"))["data"];
        var bodies=snapshot["bodies"];
        BodyOrbit Body(string id,BodyOrbit parent=null) {
            var j=bodies[id];
            return new BodyOrbit(new JsonBodyOrbitSave { strName=id,fAxis1=D(j,"majorAxisAU"),fAxis2=D(j,"minorAxisAU"),fPerh=D(j,"periapsisAU"),
                fAph=D(j,"apoapsisAU"),fPeriod=D(j,"periodSeconds"),fEcc=D(j,"eccentricity"),fAngle=D(j,"angleDegrees"),fMass=D(j,"massKg"),
                fRadius=D(j,"radiusAU"),nOrbitDirection=1 }) { boParent=parent };
        }
        var sun=Body("Sol");var venus=Body("Venus",sun);var station=Body("VORB",venus);
        var environment=new SffEnvironment();var solar=new SffBody(sun);var planet=new SffBody(venus);
        environment.Bodies.Add(solar);environment.Bodies.Add(planet);
        JToken motion=null;
        foreach (var item in snapshot["ships"]) if ((string)item["id"]=="M-5N4") motion=item["motion"];
        double epoch=D(motion,"captureEpoch"),duration=21600;
        var initial=new MotionState(new DVector(D(motion,"xAU"),D(motion,"yAU"))*OrbitalMotion.KmPerAU,
            new DVector(D(motion,"vxAUPerSecond"),D(motion,"vyAUPerSecond"))*OrbitalMotion.KmPerAU);
        var outcome=SffDynamics.Propagate(environment,initial,epoch,duration,default,null,0,null);
        var origin=planet.At(epoch);var end=planet.At(epoch+duration);
        var local=new MotionState(initial.Position-origin.Position,initial.Velocity-origin.Velocity);
        check(OrbitalMotion.Coast(local,duration,planet.Mu,out var expected),"Runtime conic solver failed");
        check((outcome.State.Position-(end.Position+expected.Position)).Length<.0001,"Segmented SFF coast changes orbit: error km="+(outcome.State.Position-(end.Position+expected.Position)).Length+" steps="+outcome.Steps+" switches="+outcome.Switches+" impact="+(outcome.Impact!=null));
        check(outcome.Body==planet && !outcome.Arrived && outcome.Impact==null,"Coaster classification wrong");
        double energy0=local.Velocity.Dot(local.Velocity)/2-planet.Mu/local.Position.Length;
        var finalLocal=new MotionState(outcome.State.Position-end.Position,outcome.State.Velocity-end.Velocity);
        double energy1=finalLocal.Velocity.Dot(finalLocal.Velocity)/2-planet.Mu/finalLocal.Position.Length;
        check(energy1<0 && Math.Abs(energy1-energy0)<1e-7,"Six-hour SFF wreck escape regression");
        // The historical study used a different Kepler tolerance. Compare the
        // handoff against actual native endpoint coordinates instead.
        station.UpdateTime(epoch+duration-1,bCalcV:false);station.UpdateTime(epoch+duration,bCalcV:false);
        var nativeStation=new DVector(station.dXReal,station.dYReal)*OrbitalMotion.KmPerAU;
        var nativePlanet=new DVector(venus.dXReal,venus.dYReal)*OrbitalMotion.KmPerAU;
        double referenceRange=(nativePlanet+expected.Position-nativeStation).Length;
        OrbitalMotion.BodyAt(station,epoch+duration,out var endStation);
        check(Math.Abs((outcome.State.Position-endStation.Position).Length-referenceRange)<.001,"Runtime coast differs from independent offline study");

        var inertial=new SffEnvironment();var tiny=Empty<BodyOrbit>();tiny.strName="reference";tiny.fMass=1;tiny.fPeriod=31556926;
        inertial.Bodies.Add(new SffBody(tiny));
        var start=new MotionState(new DVector(100000,0),default);
        var target=new FixedTarget { State=new MotionState(new DVector(100000,100),default) };
        var flight=new SffFlight { Target=target,Acceleration=.0005,MaxSpeed=.2,HandoffKm=.5,ArrivalSpeed=.02 };
        var partial=SffDynamics.Propagate(inertial,start,0,200,default,null,0,flight);
        check(!partial.Arrived && partial.State.Position.Y>1 && partial.State.Position.Y<50,"Short skip must make partial progress");
        check(partial.State.Velocity.Length<=.2+.0001,"Partial journey exceeds speed limit");
        var arrived=SffDynamics.Propagate(inertial,start,0,3600,default,null,0,flight);
        check(arrived.Arrived && arrived.ArrivalAfter>200 && arrived.ArrivalAfter<3600,"Long enough skip must reach target");
        check((arrived.State.Position-target.State.Position).Length<=flight.HandoffKm && arrived.State.Velocity.Length<=flight.ArrivalSpeed,"Arrival didn't brake");
        // A target beyond the body's opposite limb must produce the vanilla detour.
        var avoidBody=Empty<BodyOrbit>();avoidBody.strName="obstacle";avoidBody.fMass=1;avoidBody.fPeriod=31556926;avoidBody.fRadius=1000/OrbitalMotion.KmPerAU;
        var obstacle=new SffBody(avoidBody);
        flight.NavigationTarget(new MotionState(new DVector(-1500,0),default),new MotionState(new DVector(1500,0),default),obstacle,0);
        check(flight.UsedDetour,"Planetary occlusion policy bypassed");

        // A free physical impact remains an impact; it is not projected through the planet.
        var collisionEnv=new SffEnvironment();collisionEnv.Bodies.Add(obstacle);
        var impact=SffDynamics.Propagate(collisionEnv,new MotionState(new DVector(1001,0),new DVector(-1,0)),0,100,default,null,0,null);
        check(impact.Impact==obstacle && impact.State.Position.Length<1000,"Swept planetary impact lost");
        avoidBody.fMass=320000/OrbitalMotion.GravityConstant;
        var grazingEnv=new SffEnvironment();grazingEnv.Bodies.Add(new SffBody(avoidBody));
        var grazing=SffDynamics.Propagate(grazingEnv,new MotionState(new DVector(1000.01,0),new DVector(0,Math.Sqrt(320000/1000.01))),0,400,default,null,0,null);
        check(grazing.Impact==null,"Chord approximation falsely destroys grazing orbiter");
        var domainRoot=Empty<BodyOrbit>();domainRoot.strName="star";domainRoot.fMass=1e25;domainRoot.fPeriod=31556926;
        var domainPlanet=Empty<BodyOrbit>();domainPlanet.strName="planet";domainPlanet.fMass=1e24;domainPlanet.fPeriod=31556926;domainPlanet.fPerh=10000/OrbitalMotion.KmPerAU;
        var domains=new SffEnvironment();domains.Bodies.Add(new SffBody(domainRoot));domains.Bodies.Add(new SffBody(domainPlanet));
        var escape=SffDynamics.Propagate(domains,new MotionState(new DVector(11000,0),new DVector(0,13)),0,3600,default,null,0,null);
        check(escape.Switches>0 && escape.State.Finite && (escape.State.Position-new DVector(10000,0)).Length>3000,"Legitimate gravity-domain escape was clamped/lost");
        var pathTarget=new SffPathTarget { Points=new[]{
            new SffPathTarget.Point { Epoch=0,State=new MotionState(new DVector(100000,0),new DVector(0,.1)) },
            new SffPathTarget.Point { Epoch=1000,State=new MotionState(new DVector(100000,100),new DVector(0,.1)) }
        }};
        check(Math.Abs(pathTarget.At(500).Position.Y-50)<1e-9 && Math.Abs(pathTarget.At(1500).Position.Y-150)<1e-9,"Read-only moving route target prediction wrong");
        var atmoHarmony=new Harmony("orbital.atmo.check");
        try {
            atmoHarmony.Patch(AccessTools.Method(typeof(GasContainer),nameof(GasContainer.GetGasDensity),new[]{typeof(JsonAtmosphere)}),prefix:new HarmonyMethod(typeof(SffChecks),nameof(DensityProbe)));
            var air=Empty<BodyOrbit>();air.strName="air";air.fPeriod=31556926;air.fMass=1;air.fRadius=1000/OrbitalMotion.KmPerAU;
            air.aAtmospheres=new[]{new JsonAtmosphere { fMaxAltitude=2000,fTemp=300,fCO2=1 }};
            var airEnv=new SffEnvironment();airEnv.Bodies.Add(new SffBody(air));
            var damping=SffDynamics.Propagate(airEnv,new MotionState(new DVector(1500,0),new DVector(0,1)),0,100,default,
                new[]{new SffAero { Mass=10000,Front=20,Side=20,Weight=1 }},0,null);
            check(damping.State.Finite && damping.Atmosphere && damping.State.Velocity.Length<.1 && !damping.BudgetFallback,"Atmospheric damping is unstable/ignored");
        } finally { atmoHarmony.UnpatchSelf(); }
        var random=new Random(7231);
        for (int i=0;i<1000;i++) {
            double r=7000+random.NextDouble()*100000,speed=Math.Sqrt(320000/r)*(.6+random.NextDouble());
            var state=new MotionState(new DVector(r,0),new DVector(0,speed));
            check(OrbitalMotion.Coast(state,random.NextDouble()*86400,320000,out var next),"Random runtime coast failed");
            double e0=speed*speed/2-320000/r,e1=next.Velocity.Dot(next.Velocity)/2-320000/next.Position.Length;
            check(Math.Abs(e0-e1)<1e-8,"Runtime coast changes energy");
        }
        var harmony=new Harmony("orbital.sff.offline");
        try {
            foreach (Type patch in new[]{typeof(SffMotionPatch),typeof(SffBeforeCatchupPatch),typeof(SffCatchupPatch)})
                check(harmony.CreateClassProcessor(patch).Patch().Count==1,"SFF Harmony patch binding: "+patch.Name);
            var ctx=Context(epoch,duration);
            var situation=Empty<ShipSitu>();OrbitalMotion.Write(situation,initial);
            var plan=new SffSimulation.Plan { Anchor=situation,Initial=initial,Outcome=outcome,Rotation=.2,AngularVelocity=.001 };
            Register(ctx,situation,plan);SffSimulation.Current=ctx;
            situation.TimeAdvance(duration);
            check((OrbitalMotion.Read(situation).Position-outcome.State.Position).Length<.00001 && plan.Applied,"Actual TimeAdvance hook failed");
            var member=Empty<ShipSitu>();Register(ctx,member,plan,true,new DVector(.2,.1),.3);
            member.TimeAdvance(duration);
            double angle=.2+.001*duration;
            var offset=new DVector(.2*Math.Cos(angle)-.1*Math.Sin(angle),.2*Math.Sin(angle)+.1*Math.Cos(angle));
            check((OrbitalMotion.Read(member).Position-(outcome.State.Position+offset)).Length<.00001,"Docking formation offset changed");
            check(Math.Abs(member.fRot-(float)(angle+.3))<.0001,"Group rotation changed");

            // Native playback is a test double: prove exactly one delegation and no
            // numerical override, including when playback clears the route at arrival.
            harmony.Patch(AccessTools.Method(typeof(NavData),nameof(NavData.TimeAdvance)),prefix:new HarmonyMethod(typeof(NavigationProbe),nameof(NavigationProbe.Prefix)));
            foreach (bool clear in new[]{false,true}) {
                OrbitalMotion.Write(situation,initial);situation.NavData=Empty<NavData>();NavigationProbe.Clear=clear;NavigationProbe.Calls=0;
                situation.TimeAdvance(duration);
                check(NavigationProbe.Calls==1 && situation.vPosx==.123 && situation.vVelX==.00001,"Plotted course was overwritten/double-played");
            }
            NavigationProbe.Result=false;NavigationProbe.Clear=true;
            var fallback=Empty<ShipSitu>();OrbitalMotion.Write(fallback,initial);fallback.NavData=Empty<NavData>();
            SffSimulation.Current=null;NavigationProbe.Calls=0;fallback.TimeAdvance(duration);SffSimulation.Current=ctx;
            OrbitalMotion.Write(situation,initial);situation.NavData=Empty<NavData>();situation.TimeAdvance(duration);
            check(NavigationProbe.Calls==2 && (OrbitalMotion.Read(fallback).Position-OrbitalMotion.Read(situation).Position).Length<1e-8,"Native plotted-course failure/overflow behavior changed");
            NavigationProbe.Result=true;
            var prediction=Empty<ShipSitu>();prediction.vVelX=.00001;
            prediction.TimeAdvance(100);
            check(Math.Abs(prediction.vPosx-.001)<1e-12,"Temporary NavData overflow prediction was intercepted");
            var pilot=Empty<AIShip>();
            ((IDictionary)typeof(SffSimulation).GetField("pilots",Private).GetValue(ctx)).Add(pilot,plan);
            plan.Ready=false;
            check(!SffCatchupPatch.Prefix(pilot),"Partial journey allowed instant catchup");
            var pooledAfterAdvance=Empty<Ship>();pooledAfterAdvance.HideFromSystem=true;plan.Ships.Add(pooledAfterAdvance);
            check(SffCatchupPatch.Prefix(pilot),"Newly pooled ship retained custom catchup suppression");plan.Ships.Clear();
            plan.Ready=true;
            check(SffCatchupPatch.Prefix(pilot) && SffCatchupPatch.Prefix(Empty<AIShip>()),"Arrival/native combat catchup was suppressed");
            ctx.Dispose();
            check(SffSimulation.Current==null,"SFF scope retained object references");
            check(((IDictionary)typeof(SffSimulation).GetField("entries",Private).GetValue(ctx)).Count==0,"SFF scratch not released");
            FixtureCapture(check,harmony,environment,initial,epoch,duration,sun,venus);
            SffLiveReplayChecks.Run(check);
        } finally { SffSimulation.Current?.Dispose();harmony.UnpatchSelf(); }
    }
    static void FixtureCapture(Action<bool,string> check,Harmony harmony,SffEnvironment environment,MotionState initial,double epoch,double duration,BodyOrbit sun,BodyOrbit venus) {
        Ship Make(string id) {
            var ship=Empty<Ship>();ship.strRegID=id;ship.nCols=4;ship.nRows=4;
            AccessTools.Property(typeof(Ship),nameof(Ship.objSS)).SetValue(ship,Empty<ShipSitu>());
            OrbitalMotion.Write(ship.objSS,initial);
            AccessTools.Field(typeof(Ship),"nLoadState").SetValue(ship,Ship.Loaded.Edit);
            AccessTools.Field(typeof(Ship),"_mass").SetValue(ship,10000d);
            ship.objSS.Size=200;
            return ship;
        }
        var world=Empty<StarSystem>();world.boStar=sun;
        world.aBOs=new Dictionary<string,BodyOrbit>{{"Sol",sun},{"Venus",venus}};
        world.dictShips=new Dictionary<string,Ship>();
        var coast=Make("coast");var plotted=Make("plotted");var combat=Make("combat");var locked=Make("locked");
        plotted.objSS.NavData=Empty<NavData>();combat.shipCombatTarget=plotted;locked.objSS.bBOLocked=true;
        foreach (var ship in new[]{coast,plotted,combat,locked}) world.dictShips.Add(ship.strRegID,ship);
        StarSystem.fEpoch=epoch;OrbitalSimulation.ObserveUpdate(world,duration);
        check(SffSimulation.Begin(world,duration,false)==null,"Correction intercepts ordinary world update");
        var context=SffSimulation.Begin(world,duration,true);
        check(context!=null,"Real SFF fixture preparation failed");
        StarSystem.fEpoch=epoch+duration;
        coast.objSS.TimeAdvance(duration);
        var moved=OrbitalMotion.Read(coast.objSS);
        var refEnd=environment.Bodies[1].At(epoch+duration);
        var r=moved.Position-refEnd.Position;var v=moved.Velocity-refEnd.Velocity;
        check(v.Dot(v)/2-environment.Bodies[1].Mu/r.Length<0,"Captured ship did not remain bound");
        check(!context.Advance(plotted.objSS,duration,false) && !context.Advance(combat.objSS,duration,false) && !context.Advance(locked.objSS,duration,false),"Protected cases registered for correction");
        context.BeforeAi();context.Dispose();
        var report=(Dictionary<string,object>)SffSimulation.Snapshot();
        check((int)report["prepared"]==1 && (int)report["nativeCourses"]==1 && (int)report["nativeCombat"]==1 && (int)report["nativeLocked"]==1,"Capture exclusions/report mismatch");
        // Exercise the actual native docking-group wrapper; mute only its unrelated
        // UI/shipboard-gravity setter, not trajectory or formation code.
        harmony.Patch(AccessTools.PropertySetter(typeof(Ship),nameof(Ship.Gravity)),prefix:new HarmonyMethod(typeof(SffChecks),nameof(SkipSetter)));
        var a=Make("group-a");var b=Make("group-b");
        var group=new SolarSystem.DockGroup { Anchor=Empty<ShipSitu>() };
        OrbitalMotion.Write(group.Anchor,initial);group.Anchor.fRot=.3f;group.Anchor.fW=.0001f;
        a.objSS._dockGroup=group;b.objSS._dockGroup=group;
        a.objSS._dockOffsetPos=new Ostranauts.Utils.Models.Point(.2/OrbitalMotion.KmPerAU,0);
        b.objSS._dockOffsetPos=new Ostranauts.Utils.Models.Point(-.2/OrbitalMotion.KmPerAU,0);
        group.DockedShips.Add(new SolarSystem.DockGroupMember(a,false));group.DockedShips.Add(new SolarSystem.DockGroupMember(b,false));
        world.dictShips.Clear();world.dictShips.Add(a.strRegID,a);world.dictShips.Add(b.strRegID,b);
        StarSystem.fEpoch=epoch;
        var grouped=SffSimulation.Begin(world,duration,true);
        StarSystem.fEpoch=epoch+duration;
        a.objSS.TimeAdvance(duration);b.objSS.TimeAdvance(duration);group.TimeAdvance(duration);
        check(Math.Abs((OrbitalMotion.Read(a.objSS).Position-OrbitalMotion.Read(b.objSS).Position).Length-.4)<.00001,"Native group wrapper lost formation");
        check(a.objSS.vVelX==group.Anchor.vVelX && b.objSS.vVelY==group.Anchor.vVelY,"Group members don't share anchor velocity");
        grouped.BeforeAi();grouped.Dispose();report=(Dictionary<string,object>)SffSimulation.Snapshot();
        check((int)report["prepared"]==1,"Docking group was propagated more than once");
        // A hidden member cannot drag an otherwise-visible group into replacement.
        b.HideFromSystem=true;StarSystem.fEpoch=epoch;
        using(var hiddenGroup=SffSimulation.Begin(world,duration,true)) {
            StarSystem.fEpoch=epoch+duration;
            check(!hiddenGroup.Advance(group.Anchor,duration,false) && !hiddenGroup.Advance(a.objSS,duration,false),"Mixed hidden group was registered");
        }
        report=(Dictionary<string,object>)SffSimulation.Snapshot();
        check((int)report["prepared"]==0 && (int)report["nativeHidden"]>0,"Hidden group was not reported");
        b.HideFromSystem=false;
        // A plotted course on either member protects the entire group.
        b.objSS.NavData=Empty<NavData>();StarSystem.fEpoch=epoch;
        var protectedGroup=SffSimulation.Begin(world,duration,true);
        check(!protectedGroup.Advance(group.Anchor,duration,false),"Plotted group anchor was corrected");
        protectedGroup.Dispose();
        check(SffSimulation.Current==null,"Capture/finalizer scope leaked");
        // Full capture -> normal ship advance -> catch-up gate for a targetted AI.
        var oldAis=AIShipManager.dictAIs;
        try {
            var traveler=Make("traveler");var goal=Make("goal");traveler.IsAIShip=true;traveler.RCSCount=.1745f;goal.objSS.Size=1500;
            traveler.shipScanTarget=goal;traveler.shipSituTarget=goal.objSS;
            var pilot=Empty<AIShip>();var profile=new Pilot { ShipUs=traveler };
            AccessTools.Field(typeof(AIShip),"_shipUs").SetValue(pilot,traveler);
            AccessTools.Field(typeof(AIShip),"_aiCharacter").SetValue(pilot,profile);
            AccessTools.Field(typeof(AIShip),"_activeCommand").SetValue(pilot,new TravelCommand(profile));
            AIShipManager.dictAIs=new Dictionary<string,List<AIShip>>{{"fixture",new List<AIShip>{pilot}}};
            world.dictShips.Clear();world.dictShips.Add(traveler.strRegID,traveler);world.dictShips.Add(goal.strRegID,goal);
            foreach (double skip in new[]{300d,3600d}) {
                OrbitalMotion.Write(traveler.objSS,initial);OrbitalMotion.Write(goal.objSS,new MotionState(initial.Position+new DVector(0,100),initial.Velocity));
                StarSystem.fEpoch=epoch;
                var travel=SffSimulation.Begin(world,skip,true);
                StarSystem.fEpoch=epoch+skip;
                traveler.objSS.TimeAdvance(skip);goal.objSS.TimeAdvance(skip);travel.BeforeAi();
                check(travel.AllowCatchup(pilot)==(skip==3600),"Captured AI arrival/partial gate wrong at "+skip+" seconds");
                travel.Dispose();
                var trace=(Dictionary<string,object>)SffSimulation.Snapshot();
                check((int)trace["failures"]==0,"AI integration silently fell back to native");
            }
        } finally { AIShipManager.dictAIs=oldAis; }
    }
    static bool SkipSetter() => false;
    static bool DensityProbe(ref double __result) { __result=1;return false; }
    sealed class FixedTarget : ISffTarget {
        internal MotionState State;
        public MotionState At(double epoch) => State;
        public DVector Acceleration(double epoch) => default;
    }
    static class NavigationProbe {
        internal static int Calls;
        internal static bool Clear;
        internal static bool Result=true;
        internal static bool Prefix(ShipSitu situUs,ref bool __result) {
            Calls++;situUs.vPosx=.123;situUs.vVelX=.00001;
            if (Clear) situUs.NavData=null;
            __result=Result;return false;
        }
    }
    sealed class Pilot : IAICharacter {
        public Ship ShipUs { get; set; }
        public CondOwner CaptainUs => null;
        public QueuedCommandsNode PriorityQueue { get; set; }
        public ICommand BehaviorTree { get; set; }
        public Blackboard Blackboard { get; set; }
        public AIType AIType => AIType.Scav;
        public TargetData GetTarget() => null;
        public TargetData GetTargetHome() => null;
        public double MaxSpeed(bool? inAtmo=null) => .2/OrbitalMotion.KmPerAU;
        public ICommand FFWD(ICommand active,Blackboard blackboard) => active;
    }
    sealed class TravelCommand : BaseCommand {
        internal TravelCommand(IAICharacter pilot) : base(pilot) {}
        public override string ActiveCommandName => "FlyTo";
        public override CommandStatus RunCommand(Blackboard blackboard) => null;
    }
}
