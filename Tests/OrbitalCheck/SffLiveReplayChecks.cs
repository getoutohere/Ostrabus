using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ostrabus.Orbital;

static class SffLiveReplayChecks {
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static double D(JToken j,string key) => (double)j[key];
    internal static void Run(Action<bool,string> check) {
        const string path = "SFF-6H/65631773269-533958-BEFORE.json";
        var capture=JObject.Parse(FixtureFiles.Read(path));
        var data=capture["excursion"]["data"];
        var recorded=JObject.Parse(FixtureFiles.Read("SFF-6H/DOCKED-END-ORBITAL.json"))["data"]["lastSff"];
        double epoch=D(recorded,"startEpoch"),seconds=D(recorded,"skipSeconds"),savedEpoch=StarSystem.fEpoch;
        BodyOrbit Body(string id,BodyOrbit parent=null) {
            var j=data["bodies"][id];
            return new BodyOrbit(new JsonBodyOrbitSave { strName=id,fAxis1=D(j,"majorAxisAU"),fAxis2=D(j,"minorAxisAU"),
                fPerh=D(j,"periapsisAU"),fAph=D(j,"apoapsisAU"),fPeriod=D(j,"periodSeconds"),fPeriodShift=D(j,"phaseSeconds"),
                fEcc=D(j,"eccentricity"),fAngle=D(j,"angleDegrees"),fMass=D(j,"massKg"),fRadius=D(j,"radiusAU"),nOrbitDirection=(int)j["direction"] }) { boParent=parent };
        }
        var sun=Body("Sol");var venus=Body("Venus",sun);sun.boChildren=new List<BodyOrbit>{venus};
        var shipped=JToken.Parse(File.ReadAllText(Ostrabus.Development.InstallationPaths.Data + "star_systems/star_system.json"));
        if (shipped is JArray) shipped=shipped[0];
        foreach(var body in shipped["aSpawnBodies"]) if ((string)body["strName"]=="Venus") venus.aAtmospheres=body["aAtmosphericValues"].ToObject<JsonAtmosphere[]>();
        check(venus.aAtmospheres!=null && venus.aAtmospheres[venus.aAtmospheres.Length-1].fMaxAltitude==6402,"Replay omitted shipped atmosphere");
        JToken subject=null;
        foreach(var ship in data["ships"]) if ((string)ship["id"]=="M-5N4") subject=ship;
        check((string)subject["loadState"]=="Shallow","Replay lost captured ship load state");
        var m=subject["motion"];
        check(D(m,"captureEpoch")==epoch,"Replay baseline does not match actual SFF start");
        var initial=new MotionState(new DVector(D(m,"xAU"),D(m,"yAU"))*OrbitalMotion.KmPerAU,
            new DVector(D(m,"vxAUPerSecond"),D(m,"vyAUPerSecond"))*OrbitalMotion.KmPerAU);
        Ship Make(string id,MotionState state,Ship.Loaded load=Ship.Loaded.Shallow) {
            var ship=Empty<Ship>();ship.strRegID=id;ship.nCols=ship.nRows=20;
            AccessTools.Property(typeof(Ship),nameof(Ship.objSS)).SetValue(ship,Empty<ShipSitu>());
            OrbitalMotion.Write(ship.objSS,state);ship.objSS.Size=200;
            AccessTools.Field(typeof(Ship),"nLoadState").SetValue(ship,load);
            AccessTools.Field(typeof(Ship),"_mass").SetValue(ship,10000d);
            AccessTools.Field(typeof(Ship),"fShallowMass").SetValue(ship,10000d);
            return ship;
        }
        var subjectShip=Make("M-5N4",initial);
        var world=Empty<StarSystem>();world.boStar=sun;world.aBOs=new Dictionary<string,BodyOrbit>{{"Sol",sun},{"Venus",venus}};
        world.dictShips=new Dictionary<string,Ship>{{subjectShip.strRegID,subjectShip}};
        try {
            var aero=SffSimulation.CaptureAero(new List<Ship>{subjectShip},out int models);
            check(aero==null && models==0,"Shallow replay received invented aero forces");
            var emptyFull=Make("full-empty",initial,Ship.Loaded.Full);
            check(SffSimulation.CaptureAero(new List<Ship>{emptyFull},out models)==null && models==0,"Full ship with no tiles differs from native gate");
            var full=Make("full",initial,Ship.Loaded.Full);full.aTiles=new List<Tile>{null};
            // Null is only a placeholder for the native count gate; no tiles or Unity
            // behaviours are queried by aerodynamic capture.
            var fullAero=SffSimulation.CaptureAero(new List<Ship>{full},out models);
            check(fullAero!=null && models==1 && fullAero[0].Mass==10000,"Full ship lost native aerodynamic path");
            var environment=new SffEnvironment();var bodySun=new SffBody(sun);var bodyVenus=new SffBody(venus);
            environment.Bodies.Add(bodySun);environment.Bodies.Add(bodyVenus);
            check(bodyVenus.Density(6388)>0,"Replay has no actual exosphere density");
            var predicted=SffDynamics.Propagate(environment,initial,epoch,seconds,default,aero,.0001,null);
            check(predicted.Impact==null && !predicted.BudgetFallback && predicted.Switches==0,"Fixed captured coast still crashes or falls back");
            check(predicted.Atmosphere,"Replay did not cross the atmosphere-data tail");
            var center=bodyVenus.At(epoch);var end=bodyVenus.At(epoch+seconds);
            var relative=new MotionState(initial.Position-center.Position,initial.Velocity-center.Velocity);
            double e0=relative.Velocity.Dot(relative.Velocity)/2-bodyVenus.Mu/relative.Position.Length;
            var finalR=predicted.State.Position-end.Position;var finalV=predicted.State.Velocity-end.Velocity;
            double e1=finalV.Dot(finalV)/2-bodyVenus.Mu/finalR.Length;
            check(e1<0 && Math.Abs(e1-e0)<1e-7,"Replay shallow coast gained energy");
            check(OrbitalMotion.Coast(relative,seconds,bodyVenus.Mu,out var conic),"Replay independent conic failed");
            double residual=(finalR-conic.Position).Length;
            check(residual<.0001,"Replay diverges from independent conic: "+residual+" km");
            // Exercise the actual capture and motion dispatch, not just propagation.
            StarSystem.fEpoch=epoch;OrbitalSimulation.ObserveUpdate(world,seconds);
            using(var context=SffSimulation.Begin(world,seconds,true)) {
                check(context!=null,"Replay failed to prepare actual capture");
                StarSystem.fEpoch=epoch+seconds;
                check(context.Advance(subjectShip.objSS,seconds,false),"Replay failed actual dispatch");
                check((OrbitalMotion.Read(subjectShip.objSS).Position-predicted.State.Position).Length<.0001,"Actual capture differs from replay solver");
            }
            var report=(Dictionary<string,object>)SffSimulation.Snapshot();
            check((int)report["prepared"]==1 && (int)report["failures"]==0 && (int)report["impactChecks"]==0,"Replay reported unexpected fallback/impact");
            // All variants rejected before propagation and retained untouched.
            var badSun=Make("bad-sun",new MotionState(default,default));
            var badVenus=Make("bad-venus",bodyVenus.At(epoch));
            var badFinite=Make("bad-finite",new MotionState(new DVector(double.NaN,0),default));
            world.dictShips.Clear();foreach(var ship in new[]{badSun,badVenus,badFinite}) world.dictShips.Add(ship.strRegID,ship);
            StarSystem.fEpoch=epoch;
            using(var context=SffSimulation.Begin(world,seconds,true)) {
                StarSystem.fEpoch=epoch+seconds;
                foreach(var ship in new[]{badSun,badVenus,badFinite}) check(!context.Advance(ship.objSS,seconds,false),"Invalid start registered for replacement");
                context.BeforeAi();
                check(badSun.objSS.vPosx==0 && badSun.objSS.vPosy==0,"Invalid Sun origin was moved to its surface");
                check(double.IsNaN(badFinite.objSS.vPosx),"Invalid finite state was silently repaired");
            }
            var invalid=(Dictionary<string,object>)SffSimulation.Snapshot();
            check((int)invalid["nativeInvalidStart"]==3 && (int)invalid["prepared"]==0 && (int)invalid["impactChecks"]==0,"Invalid starts counted as new collisions");
            // Real pooled hauler snapshots: hidden, no crew/controller, stale VORB
            // target. Origin storage must be excluded before the inside-body guard.
            var pooled=new List<Ship>();
            foreach(string id in new[]{"O-APJH","O-D5GM"}) {
                var raw=JObject.Parse(FixtureFiles.Read("SFF-HAULER-BASELINE/"+id+"-CREW.json"))["data"];
                check((bool)raw["ship"]["hiddenFromSystem"] && (int)raw["crewProfileCount"]==0,"Pooled regression omitted observed inactive flags");
                var position=raw["ship"]["position"];
                var state=new MotionState(new DVector(D(position,"xAU"),D(position,"yAU"))*OrbitalMotion.KmPerAU,
                    new DVector(D(position,"vxAUPerSecond"),D(position,"vyAUPerSecond"))*OrbitalMotion.KmPerAU);
                var hull=Make(id,state);hull.IsAIShip=true;hull.HideFromSystem=true;pooled.Add(hull);
            }
            var origin=Make("hidden-origin",default);origin.HideFromSystem=true;pooled.Add(origin);
            world.dictShips.Clear();foreach(var ship in pooled) world.dictShips.Add(ship.strRegID,ship);
            StarSystem.fEpoch=epoch;
            using(var context=SffSimulation.Begin(world,seconds,true)) {
                StarSystem.fEpoch=epoch+seconds;
                foreach(var ship in pooled) {
                    MotionState before=OrbitalMotion.Read(ship.objSS);
                    check(!context.Advance(ship.objSS,seconds,false),"Pooled hull entered SFF replacement");
                    UnityEngine.Vector2 gravity=default;BodyOrbit nearest=null,selected=null;
                    check(!context.Gravity(ship.objSS,ref gravity,ref nearest,ref selected),"Pooled hull received replacement gravity");
                    check((OrbitalMotion.Read(ship.objSS).Position-before.Position).Length==0,"Pooled hull was moved by replacement");
                }
                context.BeforeAi();
            }
            var hidden=(Dictionary<string,object>)SffSimulation.Snapshot();
            check((int)hidden["nativeHidden"]==3 && (int)hidden["nativeInvalidStart"]==0 &&
                (int)hidden["prepared"]==0 && (int)hidden["impactChecks"]==0,"Pooled hulls classified as invalid starts or impacts");
            // Reuse is allowed on a later skip. If it becomes hidden again after
            // capture, dispatch/gravity must not apply the stale planned trajectory.
            origin.HideFromSystem=false;OrbitalMotion.Write(origin.objSS,initial);
            world.dictShips.Clear();world.dictShips.Add(origin.strRegID,origin);StarSystem.fEpoch=epoch;
            using(var context=SffSimulation.Begin(world,seconds,true)) {
                StarSystem.fEpoch=epoch+seconds;
                origin.HideFromSystem=true;
                check(!context.Advance(origin.objSS,seconds,false),"Newly pooled hull received stale trajectory");
                UnityEngine.Vector2 gravity=default;BodyOrbit nearest=null,selected=null;
                check(!context.Gravity(origin.objSS,ref gravity,ref nearest,ref selected),"Newly pooled hull received stale gravity");
                origin.HideFromSystem=false;
                check(context.Advance(origin.objSS,seconds,false),"Visible reactivated hull could not advance");
            }
            var reused=(Dictionary<string,object>)SffSimulation.Snapshot();
            check((int)reused["prepared"]==1 && (int)reused["nativeHidden"]==0,"Visible reused hull remains excluded");
            check(SffSimulation.Current==null,"Replay retained context");
            var replay=new { capturePath=path,epoch,seconds,loadState="Shallow",aerodynamicModels=0,atmosphereTailCrossed=predicted.Atmosphere,
                impact=predicted.Impact?.Native.strName,steps=predicted.Steps,initialEnergyKm2S2=e0,finalEnergyKm2S2=e1,
                conicPositionResidualKm=residual,finalAltitudeKm=finalR.Length-bodyVenus.Radius,
                invalidStartsExcluded=(int)invalid["nativeInvalidStart"],
                hiddenPooledHullsExcluded=(int)hidden["nativeHidden"],
                note="Captured M-5N4 start state, shipped Venus atmosphere with real native gas-density formula; initial/final conic energy uses current SFF ephemeris convention. No live game state changed." };
            File.WriteAllText("obj/ORBIT-SFF-FIXED-REPLAY.json",JsonConvert.SerializeObject(replay,Formatting.Indented));
            Console.WriteLine("Captured six-hour SFF replay: Shallow wreck stays bound, no impact; conic residual "+residual+" km; 3 invalid starts excluded.");
        } finally { SffSimulation.Current?.Dispose();StarSystem.fEpoch=savedEpoch; }
    }
}
