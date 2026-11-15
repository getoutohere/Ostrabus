using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Ostranauts.ShipGUIs.Utilities;
using UnityEngine;

static partial class Program {
    static int playbackCalls, completionStops, completionThrustStops;
    static bool rejectPlayback, throwPlayback;
    static ShipSitu claimedPlayback, nestedPlayback;
    static readonly Dictionary<string,string> completionControls = new Dictionary<string,string>();
    static bool PlaybackGate(ShipSitu situUs, ref bool __result) {
        playbackCalls++;
        if (throwPlayback) throw new InvalidOperationException("fixture playback failure");
        if (ReferenceEquals(situUs,claimedPlayback)) {
            var nested=nestedPlayback; nestedPlayback=null;
            nested?.TimeAdvance(600);
            situUs.vPosx=.123; situUs.NavData=null; __result=true; return false;
        }
        if (!rejectPlayback) return true;
        situUs.ResetNavData(); __result = false; return false;
    }
    static void CompletionStopped() { completionStops++; }
    static bool CompletionThrust(Ship __instance, double fAmount) {
        Check(fAmount == 0, "Completion requested positive thrust"); completionThrustStops++;
        __instance.objSS.vAccIn = default;
        Set(__instance,"bTorchDriveThrusting",false);
        return false; // Engine scene/audio services are a boundary, not course physics.
    }
    static bool ReadCompletionControl(string __0, ref string __result) {
        __result = completionControls.TryGetValue(__0,out var value) ? value : "0"; return false;
    }
    static bool WriteCompletionControl(string __0, string __1) { completionControls[__0] = __1; return false; }
    static void Close(double actual, double expected, string why, double tolerance = 2e-14) => Check(Math.Abs(actual-expected) <= tolerance, why + ": " + actual + " != " + expected);
    static NavData BrakingCourse(Ship ship, double start, double end, bool torch = false) {
        Set(ship,"aCores",new List<CondOwner>());
        var first = new ShipSitu(false) { vPosx = .1, vVelX = 2e-10 };
        var finish = new ShipSitu(false) { vPosx = .1001, vPosy = -.02, vVelX = 1e-10, vVelY = -2e-10, fRot = .4f };
        finish.vAccRCS = new Vector2(-1e-12f,3e-13f);
        if (torch) finish.vAccIn = new Vector2(4e-13f,-2e-13f);
        var route = new NavData(ship);
        route.AddNavDataPoint(new NavDataPoint(start,first,0,torch ? .3 : 0,torch ? 1000 : 0));
        route.AddNavDataPoint(new NavDataPoint(end,finish,0,torch ? .3 : 0,torch ? 900 : 0));
        ship.objSS.NavData = route;
        return route;
    }
    static void PlaybackCompletion(Harmony boundary, Harmony production) {
        int before = checks;
        var oldBody = mercury;
        realLockedMotion = true;
        boundary.Patch(AccessTools.Method(typeof(NavData),nameof(NavData.TimeAdvance)),prefix:new HarmonyMethod(typeof(Program),nameof(PlaybackGate)));
        boundary.Patch(AccessTools.Method(typeof(Ship),nameof(Ship.StopManeuver)),postfix:new HarmonyMethod(typeof(Program),nameof(CompletionStopped)));
        boundary.Patch(AccessTools.Method(typeof(Ship),nameof(Ship.SetThrust)),prefix:new HarmonyMethod(typeof(Program),nameof(CompletionThrust)));
        boundary.Patch(AccessTools.Method(typeof(Ship),nameof(Ship.GetReactorGPMValue)),prefix:new HarmonyMethod(typeof(Program),nameof(ReadCompletionControl)));
        boundary.Patch(AccessTools.Method(typeof(Ship),nameof(Ship.SetReactorGPMValue)),prefix:new HarmonyMethod(typeof(Program),nameof(WriteCompletionControl)));
        try {
            CrewSim.system = Empty<StarSystem>(); CrewSim.system.dictShips = new Dictionary<string,Ship>();
            CrewSim.coPlayer.ship = null; manuallyPiloted = null;
            AccessTools.Field(typeof(GUIFFWD),"bFFWDActive").SetValue(null,false);
            var original = Vessel("completion-baseline"); var route = BrakingCourse(original,1000,1300);
            StarSystem.fEpoch = 1600; playbackCalls = 0;
            original.objSS.TimeAdvance(600);
            double extra = Math.Abs(original.objSS.vVelX-route.Destination.VelX)*149597872000;
            Close(extra,89.758722841341836,"Native 600-second phantom-thrust reproduction changed",.00001);
            Check(playbackCalls == 1 && original.objSS.NavData == null,"Baseline was confounded by duplicate playback");

            var type = typeof(Ostrabus.OstrabusPlugin).Assembly.GetType("Ostrabus.ShipNavigation.RouteCompletionPatch",true);
            production.CreateClassProcessor(type).Patch();
            foreach (double seconds in new[] { .25,1,600,1500,7200,172800d })
            foreach (bool ignore in new[] { false,true }) foreach (bool gravity in new[] { false,true }) {
                var ship = Vessel("completion"); route = BrakingCourse(ship,1000,1000+seconds/2);
                var target = new ShipSitu(false); ship.shipSituTarget = target; ship.targetRegID = "mission";
                ship.objSS.vAccEx = new Vector2(2e-14f,-1e-14f);
                ship.objSS.vAccLift = new Vector2(1e-14f,0);
                ship.objSS.vAccDrag = new Vector2(-.5e-14f,0);
                ship.objSS.bIgnoreGrav = !gravity;
                double x = route.Destination.ObjSS.vPosx, y = route.Destination.ObjSS.vPosy;
                double vx = route.Destination.VelX, vy = route.Destination.VelY, remaining = seconds/2;
                float ax = ignore ? 0 : ship.objSS.vAccLift.x+ship.objSS.vAccDrag.x+(gravity ? ship.objSS.vAccEx.x : 0);
                float ay = ignore || !gravity ? 0 : ship.objSS.vAccEx.y;
                StarSystem.fEpoch = 1000+seconds; playbackCalls = completionStops = 0;
                ship.objSS.TimeAdvance(seconds,ignore);
                Check(ship.objSS.NavData == null && playbackCalls == 1 && completionStops == 1,"Completion did not perform one native playback/stop");
                Close(ship.objSS.vPosx,x+vx*remaining+ax*.5*remaining*remaining,"Completion x coast duration");
                Close(ship.objSS.vPosy,y+vy*remaining+ay*.5*remaining*remaining,"Completion y coast duration");
                Close(ship.objSS.vVelX,vx+ax*remaining,"Residual RCS/torch or wrong gravity interval",1e-17);
                Close(ship.objSS.vVelY,vy+ay*remaining,"Residual y acceleration",1e-17);
                Check(ship.objSS.vAccRCS == Vector2.zero && ship.objSS.vAccIn == Vector2.zero,"Completed engines remain commanded");
                Check(ship.objSS.vAccEx.x == 2e-14f && ship.objSS.vAccLift.x == 1e-14f && ship.objSS.vAccDrag.x == -.5e-14f,"Completion erased passive forces");
                Check(ReferenceEquals(ship.shipSituTarget,target) && ship.targetRegID == "mission","Ordinary completion changed the mission target");
                double previousX = ship.objSS.vPosx, previousV = ship.objSS.vVelX;
                ship.objSS.TimeAdvance(1,ignore);
                Close(ship.objSS.vPosx,previousX+previousV+ax*.5,"Following frame integrated completed route twice");
                Check(playbackCalls == 1,"Finished route played again");
            }
            foreach (bool anchored in new[] { false,true }) foreach (bool fast in new[] { false,true }) {
                mercury = new BodyOrbit(new JsonBodyOrbitSave { strName="Mercury",fAxis1=5.5,fAxis2=5.5,fPerh=2.75,fAph=2.75,fPeriod=145161856,nOrbitDirection=1 });
                var ship = Vessel("ai"); route = BrakingCourse(ship,1000000,1000300);
                var end = route.Destination.ObjSS;
                if (anchored) { end.bBOLocked = true; end.bIsBO = true; end.strBOPORShip="Mercury"; end.vBOOffsetx=.00001; }
                StarSystem.fEpoch=1000600; mercury.UpdateTime(StarSystem.fEpoch);
                AccessTools.Field(typeof(GUIFFWD),"bFFWDActive").SetValue(null,fast);
                playbackCalls=completionStops=0;
                ship.objSS.TimeAdvance(600);
                Check(playbackCalls==1 && ship.objSS.NavData==null,"Locked/GUI course did not complete once");
                if (anchored) Close(ship.objSS.vPosx,mercury.dXReal+end.vBOOffsetx,"Locked endpoint translated twice");
                else Close(ship.objSS.vPosx,end.vPosx+end.vVelX*300,"Free endpoint did not coast once");
                Check(completionStops==(fast ? 0 : 1),"GUI SFF's completion ownership changed");
            }
            AccessTools.Field(typeof(GUIFFWD),"bFFWDActive").SetValue(null,false);
            foreach (string mode in new[] { "relative", "orbit", "unbound", "missing-body" }) {
                var ship=Vessel("locked-endpoint"); route=BrakingCourse(ship,1000000,1000300);
                var point=route.Destination.ObjSS;
                point.bBOLocked=mode!="orbit"; point.bOrbitLocked=mode=="orbit";
                point.strBOPORShip=mode=="unbound" ? null : mode=="missing-body" ? "Missing" : "Mercury"; point.vBOOffsetx=.00001;
                StarSystem.fEpoch=1000600; mercury.UpdateTime(StarSystem.fEpoch);
                ship.objSS.TimeAdvance(600);
                double x=mode=="unbound" || mode=="missing-body" ? point.vPosx+point.vVelX*300 : mercury.dXReal+point.vBOOffsetx;
                Close(ship.objSS.vPosx,x,"Bound endpoint refresh/coast ownership: "+mode);
                Check(!ship.objSS.bBOLocked && !ship.objSS.bOrbitLocked && !ship.objSS.bIsBO,"Endpoint projection locked the actual ship");
            }
            foreach (bool borrowed in new[] { false,true }) {
                var ship=Vessel("torch-finish"); route=BrakingCourse(ship,1000,1300,true);
                var situation=borrowed ? new ShipSitu(false) {NavData=route} : ship.objSS;
                if (borrowed) ship.objSS.vAccIn=new Vector2(8e-12f,1e-12f);
                completionControls.Clear(); completionControls["slidFlow"]=".2"; completionControls["slidCycle"]=".3";
                StarSystem.fEpoch=1600; completionThrustStops=completionStops=0;
                situation.TimeAdvance(600);
                Check(situation.NavData==null && situation.vAccIn==Vector2.zero && situation.vAccRCS==Vector2.zero,"Completed torch retained force");
                Check(completionThrustStops==(borrowed ? 0 : 1) && completionStops==(borrowed ? 0 : 1),"Borrowed situation stopped the physical owner");
                if (!borrowed) Check(double.Parse(completionControls["slidCycle"])==0 && double.Parse(completionControls["slidFlow"])==0,"Completed physical torch controls stayed open");
                else Check(ship.objSS.vAccIn.x==8e-12f && ReferenceEquals(ship.objSS.NavData,route),"Borrowed completion changed the physical owner's course/force");
                Close(situation.vPosx,route.Destination.ObjSS.vPosx+route.Destination.VelX*300,"Torch completion coast interval");
            }
            foreach (bool rejected in new[] { false,true }) {
                var ship=Vessel("manual");
                if (rejected) { BrakingCourse(ship,1000,1300); rejectPlayback=true; }
                ship.objSS.vAccRCS=new Vector2(1e-12f,0); ship.objSS.vVelX=2e-10;
                StarSystem.fEpoch=1600; completionStops=0;
                ship.objSS.TimeAdvance(600);
                Close(ship.objSS.vVelX,2e-10+(double)1e-12f*600,"Manual/aborted acceleration changed",1e-17);
                Check(completionStops==0 && ship.objSS.vAccRCS.x==1e-12f,"Manual/abort path was stopped as successful completion");
                rejectPlayback=false;
            }
            var active=Vessel("pending"); route=BrakingCourse(active,1000,2000); StarSystem.fEpoch=1300;
            completionStops=0; active.objSS.TimeAdvance(300);
            Check(ReferenceEquals(active.objSS.NavData,route) && completionStops==0,"Unfinished course stopped");
            StarSystem.fEpoch=2000; active.objSS.TimeAdvance(700);
            Check(ReferenceEquals(active.objSS.NavData,route) && completionStops==0,"Native exact-endpoint comparison changed");
            foreach (double dt in new[] {0d,-1d}) {
                var ship=Vessel("zero-negative-step"); route=BrakingCourse(ship,1000,1300); StarSystem.fEpoch=1600;
                playbackCalls=completionStops=0; ship.objSS.TimeAdvance(dt);
                Check(playbackCalls==0 && completionStops==0 && ReferenceEquals(ship.objSS.NavData,route),"Zero/backward step unexpectedly completed a course");
            }
            var overdue=Vessel("late-loaded-route"); route=BrakingCourse(overdue,900,1000); StarSystem.fEpoch=1600; overdue.objSS.TimeAdvance(1);
            Close(overdue.objSS.vPosx,route.Destination.ObjSS.vPosx+route.Destination.VelX*600,"Overdue route lost absolute endpoint-to-now coast time");
            var destroyed=Vessel("destroyed-owner"); BrakingCourse(destroyed,1000,1300); destroyed.bDestroyed=true;
            destroyed.objSS.vAccRCS=new Vector2(1e-12f,0); completionStops=0; destroyed.objSS.TimeAdvance(10);
            Check(destroyed.objSS.NavData==null && completionStops==0 && destroyed.objSS.vVelX>0,"Destroyed-owner rejection was treated as successful completion");
            var player=Vessel("player-completion",false); Set(player,"nLoadState",Ship.Loaded.Full); CrewSim.coPlayer.ship=player;
            route=BrakingCourse(player,1000,1300,true); StarSystem.fEpoch=1600; completionThrustStops=0; player.objSS.TimeAdvance(600);
            Check(completionThrustStops==1 && player.objSS.NavData==null && player.objSS.vAccIn==Vector2.zero,"Materialized player course completion was excluded");
            Close(player.objSS.vPosx,route.Destination.ObjSS.vPosx+route.Destination.VelX*300,"Player completion replayed elapsed trajectory time");
            CrewSim.coPlayer.ship=null;
            var invalid=Vessel("single-point"); route=new NavData(invalid); route.AddNavDataPoint(new NavDataPoint(1000,new ShipSitu(false))); invalid.objSS.NavData=route;
            invalid.objSS.vAccRCS=new Vector2(1e-12f,0); completionStops=0; invalid.objSS.TimeAdvance(10);
            Check(ReferenceEquals(invalid.objSS.NavData,route) && completionStops==0 && invalid.objSS.vVelX>0,"Invalid playback lost its native physics fallback");
            var throwing=Vessel("throwing"); BrakingCourse(throwing,1000,1300); throwPlayback=true;
            bool threw=false; try {throwing.objSS.TimeAdvance(600);} catch (InvalidOperationException) {threw=true;}
            throwPlayback=false; Check(threw,"Playback exception was swallowed");
            var next=Vessel("after-exception"); BrakingCourse(next,1000,1300); StarSystem.fEpoch=1600; next.objSS.TimeAdvance(600);
            Close(next.objSS.vVelX,1e-10,"Exception leaked completion ownership",1e-17);
            var claimed=Vessel("claimed-playback"); BrakingCourse(claimed,1000,1300);
            var inner=Vessel("nested-playback"); route=BrakingCourse(inner,1000,1300);
            claimedPlayback=claimed.objSS; nestedPlayback=inner.objSS;
            completionStops=0; claimed.objSS.TimeAdvance(600); claimedPlayback=null;
            Close(claimed.objSS.vPosx,.123,"Another playback handler's completed motion was integrated again");
            Close(inner.objSS.vPosx,route.Destination.ObjSS.vPosx+route.Destination.VelX*300,"Nested route completion lost its own interval");
            Check(completionStops==1,"Nested native completion was attributed to its outer handler");
            var helper=type.Assembly.GetType("Ostrabus.ShipNavigation.RouteCompletion",true);
            var scope=AccessTools.Field(helper,"current").GetValue(null);
            Check(AccessTools.Field(scope.GetType(),"Course").GetValue(scope)==null && AccessTools.Field(scope.GetType(),"Situation").GetValue(scope)==null,
                "Playback scope retained a course or situation after return");
            var reader=typeof(PatchProcessor).GetMethods().Single(m=>m.Name=="GetOriginalInstructions" && m.GetParameters().Length==2 && !m.GetParameters()[1].ParameterType.IsByRef);
            var transform=AccessTools.Method(type,"Transpiler");
            foreach (var target in new[] {AccessTools.Method(typeof(NavData),nameof(NavData.TimeAdvance)),AccessTools.Method(typeof(ShipSitu),nameof(ShipSitu.TimeAdvance))}) {
                var originalCode=((IEnumerable<CodeInstruction>)reader.Invoke(null,new object[]{target,null})).Select(c=>new CodeInstruction(c)).ToList();
                var fixedCode=((IEnumerable<CodeInstruction>)transform.Invoke(null,new object[]{originalCode.Select(c=>new CodeInstruction(c)).ToList(),target})).ToList();
                Check(fixedCode.Count==originalCode.Count+(target.DeclaringType==typeof(NavData) ? 3 : 1),"Unexpected route-completion IL footprint");
                if (target.DeclaringType==typeof(NavData)) {
                    int reset=originalCode.FindLastIndex(c=>c.Calls(AccessTools.Method(typeof(ShipSitu),nameof(ShipSitu.ResetNavData))));
                    originalCode[reset+1].opcode=OpCodes.Ldc_I4_0;
                } else originalCode.Single(c=>c.Calls(AccessTools.Method(typeof(NavData),nameof(NavData.TimeAdvance)))).operand=AccessTools.Method(typeof(ShipSitu),nameof(ShipSitu.ResetNavData));
                bool refused=false;
                try {transform.Invoke(null,new object[]{originalCode,target});} catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) {refused=true;}
                Check(refused,"Changed native completion shape was silently patched");
            }
            Console.WriteLine((checks-before)+" shared playback completion checks passed.");
        } finally {
            realLockedMotion=false; mercury=oldBody; rejectPlayback=throwPlayback=false;
            claimedPlayback=nestedPlayback=null;
            AccessTools.Field(typeof(GUIFFWD),"bFFWDActive").SetValue(null,false);
        }
    }
}
