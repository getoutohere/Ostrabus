using System;
using System.Collections.Generic;
using HarmonyLib;
using Ostrabus.MiscBugFixes;
using Ostrabus.ShipNavigation.Towing;
using Ostranauts.ShipGUIs.Utilities;
using SolarSystem;
using UnityEngine;

static partial class Program {
    static int gravityKnockdowns;
    static readonly List<double> crewGravity = new List<double>();
    static bool GravityPerson(ref CondOwner __result) { __result=CrewSim.coPlayer;return false; }
    static bool GravityKnockdown() { gravityKnockdowns++;return false; }
    static void GravityObserved(double value) { crewGravity.Add(value); }
    static NavData CoastGravityCourse(bool attach = true, string boundary = null) {
        SetupCourse(pairs:2);
        // The long first coast interval retains the last burn acceleration at
        // its first point, as observed live. Velocities are equal throughout
        // that interval. Other points make this a complete burn/coast/brake trip.
        double begin=100, burn=1200, coast=7828.692771911621;
        double ax=1.8181511851622645e-10, ay=7.5132663979182723e-11;
        double cycle=.50711679458618164;
        var nav=new NavData(ship) { IsTorching=true };
        double fuel=ship.fShallowFusionRemain;
        for(int i=0;i<4;i++) {
            double time=i==0?begin:i==1?begin+burn:i==2?begin+burn+coast:begin+burn+coast+burn;
            double powered=i==0?0:i==1?burn:i==2?burn:burn*2;
            double vx=i==0?0:i==3?0:ax*burn,vy=i==0?0:i==3?0:ay*burn;
            var situation=new ShipSitu(false) { vVelX=vx,vVelY=vy,
                vAccIn=new Vector2((float)(i<2?ax:i==2?0:-ax),(float)(i<2?ay:i==2?0:-ay)) };
            nav.AddNavDataPoint(new NavDataPoint(time,situation,0,i==2?0:cycle,fuel-powered,rcsCount:0F));
        }
        ship.objSS.NavData=nav;
        if(boundary=="rcs-force")nav.GetNavDataPointAtIndex(2).ObjSS.vAccRCS=new Vector2(1e-11f,0);
        if(boundary=="rcs-fuel")nav.GetNavDataPointAtIndex(2).FuelLevel=-1;
        if(boundary=="analytical")nav.GetNavDataPointAtIndex(2).ObjSS.bBOLocked=true;
        if(attach)Check(TorchCourseFuel.Attach(nav,false),"Gravity fixture could not attach corrected course");
        Field(nav,"fEpochLast",begin);
        return nav;
    }
    static void RunCoastGravityChecks(Harmony playback) {
        int before=checks;
        var fixture=new Harmony("offline.torch.coast-gravity");
        try {
            fixture.Patch(AccessTools.Method(typeof(PersonSpec),nameof(PersonSpec.GetCO)),prefix:new HarmonyMethod(typeof(Program),nameof(GravityPerson)));
            fixture.Patch(AccessTools.Method(typeof(Ship),nameof(Ship.KnockDownCrew)),prefix:new HarmonyMethod(typeof(Program),nameof(GravityKnockdown)));
            fixture.Patch(AccessTools.PropertySetter(typeof(Ship),nameof(Ship.Gravity)),prefix:new HarmonyMethod(typeof(Program),nameof(GravityObserved)));
            fixture.Patch(AccessTools.Method(typeof(CondOwner),nameof(CondOwner.UpdateGravity)),prefix:new HarmonyMethod(typeof(Program),nameof(Skip)));
            fixture.Patch(AccessTools.Method(typeof(TileUtils),nameof(TileUtils.EVAFallAwayCheck)),prefix:new HarmonyMethod(typeof(Program),nameof(Skip)));
            fixture.Patch(AccessTools.Method(typeof(CrewSim),nameof(CrewSim.CamShake)),prefix:new HarmonyMethod(typeof(Program),nameof(Skip)));
            fixture.Patch(AccessTools.Method(typeof(Ship),nameof(Ship.AccrueTowGroupLoad)),prefix:new HarmonyMethod(typeof(Program),nameof(Skip)));
            foreach(var method in AccessTools.GetDeclaredMethods(typeof(Ship)))
                if(method.Name=="WearManeuver")fixture.Patch(method,prefix:new HarmonyMethod(typeof(Program),nameof(Skip)));
            foreach(var method in AccessTools.GetDeclaredMethods(typeof(AudioManager)))
                if(method.Name=="PlayCreakAudio" || method.Name=="PlayAudioEmitter" || method.Name=="TweakAudioEmitter")
                    fixture.Patch(method,prefix:new HarmonyMethod(typeof(Program),nameof(Skip)));
            fixture.Patch(AccessTools.Method(typeof(GUIOrbitDraw),nameof(GUIOrbitDraw.TriggerShipRedraw)),prefix:new HarmonyMethod(typeof(Program),nameof(Skip)));
            var oldAudio=AudioManager.am;
            AudioManager.am=Empty<AudioManager>();
            var target=AccessTools.Method(typeof(NavData),nameof(NavData.TimeAdvance));
            var postfix=AccessTools.Method(typeof(TorchCoursePlaybackPatch),"Postfix");
            try {
                playback.Unpatch(target,postfix);
                foreach(bool repaired in new[]{false,true}) {
                    if(repaired)playback.Patch(target,postfix:new HarmonyMethod(postfix));
                    var nav=CoastGravityCourse();
                    Field(ship,"aPeople",new List<PersonSpec>{Empty<PersonSpec>()});
                    ship.aWPs=new List<WaypointShip>();
                    Values[CrewSim.coPlayer]["IsHuman"]=1;
                    const double progress=.63742590346815142;
                    double epoch=nav.GetNavDataPointAtIndex(1).ArrivalTime+7828.692771911621*progress;
                    StarSystem.fEpoch=fixtureEpoch=epoch;
                    Field(playbackReactor,"fTimeLastRun",epoch);
                    Field(playbackReactor,"fTimeNextRun",epoch+.27);
                    ship.Gravity=0;gravityKnockdowns=0;crewGravity.Clear();
                    double water=Read(d2o[0],"StatLiqD2O"),helium=Read(he3[0],"StatSolidHe3");
                    double reported=0;
                    // Model the independent reactor and navigation update
                    // orders, using actual SetThrust and Gravity. Actor queues,
                    // audiovisual effects and damage application are boundaries.
                    nativeThrust=true;
                    for(int i=0;i<5;i++) {
                        Check(nav.TimeAdvance(ship.objSS),"Coast gravity fixture failed native playback");
                        reported=ship.objSS.vAccIn.magnitude*149597872000/9.81;
                        ship.Gravity=reported;
                        ship.SetThrust(0);
                        ship.Gravity=ship.objSS.vAccIn.magnitude*149597872000/9.81;
                    }
                    Check(repaired?reported==0:reported>1,"Coast acceleration did not distinguish original and repaired playback");
                    Check(gravityKnockdowns==(repaired?0:10),"Native Gravity setter did not distinguish contradictory thrust cycles");
                    Check(ship.objSS.vVelX==nav.GetNavDataPointAtIndex(1).ObjSS.vVelX && ship.objSS.vVelY==nav.GetNavDataPointAtIndex(1).ObjSS.vVelY,
                        "Acceleration correction changed coasting velocity");
                    Check(Read(d2o[0],"StatLiqD2O")<=water && Read(he3[0],"StatSolidHe3")<=helium,"Coast metadata correction created fuel");
                    Console.WriteLine("Coast gravity A/B: repaired="+repaired+"; input="+reported+" G; native knockdown requests="+gravityKnockdowns);
                    nativeThrust=false;
                }
                MotionBoundaries(fixture);
            } finally { nativeThrust=false;AudioManager.am=oldAudio; }
        } finally { fixture.UnpatchSelf(); }
        Console.WriteLine("Coast gravity: "+(checks-before)+" checks passed.");
    }

    static void MotionBoundaries(Harmony fixture) {
        fixture.CreateClassProcessor(typeof(DockedCourseMemberPatch)).Patch();
        fixture.CreateClassProcessor(typeof(DockedCourseGroupPatch)).Patch();
        fixture.CreateClassProcessor(typeof(TorchTowMassPatch)).Patch();
        var active=AccessTools.Field(typeof(GUIFFWD),"bFFWDActive");
        bool oldActive=(bool)active.GetValue(null);
        try {
            foreach(bool towed in new[]{false,true})
            foreach(string format in new[]{"fresh","native","saved"})
            foreach(bool fastForward in new[]{false,true}) {
                active.SetValue(null,fastForward);
                var nav=CoastGravityCourse(attach:format!="native");
                if(format=="saved") {
                    nav=new NavData(LitJson.JsonMapper.ToObject<JsonNavData>(LitJson.JsonMapper.ToJson(nav.GetJSON())));
                    ship.objSS.NavData=nav;
                }
                mass=80531.84349002872/(towed?2:1);
                Field(ship,"aPeople",new List<PersonSpec>{Empty<PersonSpec>()});
                ship.aWPs=new List<WaypointShip>();
                Values[CrewSim.coPlayer]["IsHuman"]=1;
                DockGroup group=towed?TowGroup():null;
                if(group!=null)foreach(var member in group.DockedShips)
                    if(member.Ship!=ship)Field(member.Ship,"aPeople",new List<PersonSpec>());
                double start=nav.GetNavDataPointAtIndex(1).ArrivalTime;
                double end=nav.GetNavDataPointAtIndex(2).ArrivalTime;
                double epoch=start+(end-start)*.63742590346815142;
                StarSystem.fEpoch=fixtureEpoch=epoch;
                Field(playbackReactor,"fTimeLastRun",epoch);
                Field(playbackReactor,"fTimeNextRun",epoch+.27);
                nativeThrust=true;
                int knocksBefore=gravityKnockdowns;
                for(int frame=0;frame<12;frame++) {
                    double next=epoch+.1;
                    StarSystem.fEpoch=next;
                    // Alternate lagged CO time and ordinary aligned catch-up.
                    if(frame%2==0)fixtureEpoch=next;
                    if(group==null)Check(nav.TimeAdvance(ship.objSS),"Solo coast stopped after motion normalization");
                    else group.TimeAdvance(.1);
                    fixtureEpoch=next;playbackReactor.CatchUp();
                    double actual=ship.objSS.vAccIn.magnitude*149597872000/9.81;
                    Check(actual==0 && (group==null || group.Anchor.vAccIn==Vector2.zero),"Coast recreated thrust across independent reactor/group updates");
                    if(group==null)ship.Gravity=actual;
                    Check(ship.Gravity==0,"Native group/crew gravity did not stay at zero during coast");
                    Check(gravityKnockdowns==knocksBefore,"Steady coast repeatedly requested native crew knockdowns");
                    Check(ship.objSS.vVelX==nav.GetNavDataPointAtIndex(1).ObjSS.vVelX && ship.objSS.vVelY==nav.GetNavDataPointAtIndex(1).ObjSS.vVelY,
                        "Coast consistency changed the prescribed velocity");
                    epoch=next;
                }
                Check(TorchCourseFuel.Find(nav,false)!=null,"Motion correction lost the course fuel profile");
                nativeThrust=false;
            }
            active.SetValue(null,false);
            foreach(double epoch in new[]{700d,10000d}) {
                var nav=CoastGravityCourse();
                var a=nav.GetNavDataPointAtIndex(epoch==700?0:2);
                var b=nav.GetNavDataPointAtIndex(epoch==700?1:3);
                StarSystem.fEpoch=fixtureEpoch=epoch;
                Field(playbackReactor,"fTimeLastRun",epoch);Field(playbackReactor,"fTimeNextRun",epoch+.27);
                Check(nav.TimeAdvance(ship.objSS),"Powered interval stopped during input normalization");
                double expected=(b.ObjSS.vVelX-a.ObjSS.vVelX)/(b.ArrivalTime-a.ArrivalTime);
                Near(ship.objSS.vAccIn.x,expected,"Powered acceleration disagrees with prescribed velocity slope",1e-6);
                Check(ship.objSS.vAccIn.magnitude>0,"Powered phase lost legitimate acceleration");
                Field(ship,"aPeople",new List<PersonSpec>{Empty<PersonSpec>()});
                Values[CrewSim.coPlayer]["IsHuman"]=1;
                ship.Gravity=0;gravityKnockdowns=0;
                ship.Gravity=ship.objSS.vAccIn.magnitude*149597872000/9.81;
                Check(gravityKnockdowns==1,"Corrected real powered maneuver lost native crew knockdown behavior");
            }
            foreach(string boundary in new[]{"rcs-force","rcs-fuel","analytical"}) {
                var nav=CoastGravityCourse(boundary:boundary);
                StarSystem.fEpoch=5000;
                ship.objSS.vAccIn=new Vector2(2e-11f,3e-11f);
                var original=ship.objSS.vAccIn;
                TorchCourseFuel.PlaybackInput(TorchCourseFuel.Find(nav,false),ship.objSS);
                Check(ship.objSS.vAccIn==original,"Torch normalization rewrote an unsupported interval: "+boundary);
            }
            // Native/AI/manual playback without an eligible corrected profile
            // retains its original interpolation rather than being generalized.
            var unsupported=CoastGravityCourse(attach:false);
            CrewSim.coPlayer=null;
            StarSystem.fEpoch=fixtureEpoch=5000;
            Field(playbackReactor,"fTimeLastRun",5000d);Field(playbackReactor,"fTimeNextRun",5000.27);
            Check(unsupported.TimeAdvance(ship.objSS) && ship.objSS.vAccIn.magnitude>0 && TorchCourseFuel.Find(unsupported,false)==null,
                "Unknown/non-player course lost native motion handling");
        } finally { active.SetValue(null,oldActive);nativeThrust=false; }
    }
}
