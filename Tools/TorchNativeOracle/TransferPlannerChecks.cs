using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;
using Ostrabus.MiscBugFixes;
using Ostrabus.ShipNavigation.TorchCourses;
using Ostrabus.TorchCalculator;
using Ostranauts.ShipGUIs.Utilities;
using Ostranauts.Ships.Commands;
using Ostranauts.Utils.Models;

static partial class Program {
    const double TransferAu = 149597872000;
    static double TransferDuration(NavData nav) => nav.Destination.ArrivalTime-nav.Origin.ArrivalTime;
    static BodyOrbit transferGravity;
    [MethodImpl(MethodImplOptions.NoInlining)]
    static void TransferScopeThrow() => throw new InvalidOperationException("fixture scope exception");
    static bool TransferGravity(ref BodyOrbit boClosest, ref BodyOrbit __result) { boClosest = transferGravity; __result = transferGravity; return false; }
    static bool TransferOutsideNoWake(ref bool __result) { __result = false; return false; }
    static bool TransferPlayerAtNav(ref bool __result) { __result = true; return false; }
    static FlyToPath TransferPath() {
        var path = Empty<FlyToPath>(); Field(path,"_shipUs",ship); path.fTorchSpeedLimit = 1;
        return path;
    }
    static float PrepareTransferFixture(double epoch = 65633097166.234634) {
        fixtureEpoch = epoch; StarSystem.fEpoch = epoch;
        var fit = new ReactorFit { MassKg = 26798, Feeders = 1, Lasers = 1, Capacitors = 2, Regulators = 2 };
        var fusion = Make(fit,TorchModel.Ideal,.3,.8);
        AccessTools.Method(typeof(FusionIC),"Run").Invoke(fusion,new object[] { TorchModel.TickSeconds });
        ship.fFusionThrustMax = TorchModel.RatedThrust(TorchModel.PelletMax(fit));
        Field(ship,"fFuelEfficiencyMod",1d); Field(ship,"fRCSCount",1f);
        ship.bFusionReactorRunning = true;
        float lo = 0, hi = 1;
        for (int i = 0; i < 32; i++) {
            float mid = (lo+hi)*.5f;
            if (ship.GetMaxTorchThrust(mid)*TransferAu < 29.43) lo = mid;
            else hi = mid;
        }
        return hi;
    }
    static NavData Kernel(FlyToPath path, double distanceKm, Point incoming, Point targetVelocity, float limiter, bool scoped,
        bool torch = true, bool atmosphere = false, double? cap = null) {
        if (cap.HasValue) path.fTorchSpeedLimit = cap.Value;
        var start = new ShipSitu(false) { vPosx = .7, vPosy = .02, vVelX = incoming.X+targetVelocity.X, vVelY = incoming.Y+targetVelocity.Y };
        // The native sub-kernel consumes the arrival-epoch ephemeris. This
        // stationary fixture supplies that future point without patching UpdateTime.
        double estimate = 2000;
        var destination = new ShipSitu(false) { vPosx = start.vPosx+distanceKm*1000/TransferAu+targetVelocity.X*estimate,
            vPosy = start.vPosy+targetVelocity.Y*estimate, vVelX = targetVelocity.X, vVelY = targetVelocity.Y };
        double fuel = ship.fShallowFusionRemain > 0 ? ship.fShallowFusionRemain : 1e6;
        var origin = new NavDataPoint(fixtureEpoch,start,1e6,0,fuel);
        var target = new NavDataPoint(fixtureEpoch,destination,1e6,0,fuel);
        var prior = scoped ? TorchTransferPlanning.Enter() : default;
        try {
            var course = (NavData)AccessTools.Method(typeof(FlyToPath),"PlanTrip4Sub").Invoke(path,new object[] { origin,target,limiter,estimate,torch,atmosphere });
            if (scoped && torch && !atmosphere) {
                var context = (TorchTransferPlanning.Scope)AccessTools.Field(typeof(TorchTransferPlanning),"current").GetValue(null);
                Check(context.Changed,"Representable vector transfer uses production solver");
            }
            return course;
        } finally { if (scoped) TorchTransferPlanning.Leave(prior); }
    }
    static void RunTransferPlannerChecks(Harmony engine) {
        int before = checks;
        float limiter = PrepareTransferFixture();
        var baseline = Kernel(TransferPath(),10000,new Point(10000/TransferAu,0),default,limiter,false);
        engine.CreateClassProcessor(typeof(TorchTransferKernelPatch)).Patch();
        var unchanged = Kernel(TransferPath(),10000,new Point(10000/TransferAu,0),default,limiter,false);
        Near(TransferDuration(unchanged),TransferDuration(baseline),"Unscoped native planner unchanged",1e-10);
        var improved = Kernel(TransferPath(),10000,new Point(10000/TransferAu,0),default,limiter,true);
        Check(TransferDuration(improved) < TransferDuration(baseline)*.7,"Scoped planner preserves useful incoming momentum");
        Check(InitialVelocityDeltaV(improved) < InitialVelocityDeltaV(baseline)*.7,"Scoped course reduces delta-v");
        foreach (double speed in new[] { 0d,1000,10000,30000 }) foreach (double angle in new[] { 0d,.5,Math.PI/2,Math.PI })
            foreach (double cap in new[] { 750d,5000,20000,1e8 }) foreach (double distance in new[] { 10000d,100000 }) {
                Point u = new Point(Math.Cos(angle),Math.Sin(angle))*(speed/TransferAu);
                Point targetVelocity = new Point(20000,-15000)*(1/TransferAu);
                var course = Kernel(TransferPath(),distance,u,targetVelocity,limiter,true,cap:cap/TransferAu);
                double duration = TransferDuration(course);
                Point expected = course.Origin.ObjSS.vPos+targetVelocity*duration+new Point(distance*1000/TransferAu,0);
                Check((course.Destination.ObjSS.vPos-expected).magnitude*TransferAu < 10,"Native generated moving-frame endpoint");
                Check((course.Destination.ObjSS.vVel-targetVelocity).magnitude*TransferAu < .001,"Native generated target velocity match");
                CheckWaypoints(course,ship.GetMaxTorchThrust(limiter));
            }
        // An already suitable cruise is retained, not discarded and rebuilt.
        var cruising = Kernel(TransferPath(),10000,new Point(10000/TransferAu,0),default,limiter,true,cap:10000/TransferAu);
        var first = StudyPoints(cruising)[1];
        Check((first.ObjSS.vVel-cruising.Origin.ObjSS.vVel).magnitude < 1e-15,"Already suitable incoming velocity coasts immediately");
        Check(StudyPoints(cruising).Count < StudyPoints(baseline).Count,"No duplicate zero-duration initial phase");
        var atmosphereNative = Kernel(TransferPath(),10000,new Point(10000/TransferAu,0),default,limiter,false,atmosphere:true);
        var atmosphereScoped = Kernel(TransferPath(),10000,new Point(10000/TransferAu,0),default,limiter,true,atmosphere:true);
        Near(TransferDuration(atmosphereScoped),TransferDuration(atmosphereNative),"Atmospheric torch planning excluded",1e-10);
        var rcsNative = Kernel(TransferPath(),10,new Point(10/TransferAu,0),default,limiter,false,torch:false);
        var rcsScoped = Kernel(TransferPath(),10,new Point(10/TransferAu,0),default,limiter,true,torch:false);
        Near(TransferDuration(rcsScoped),TransferDuration(rcsNative),"Native RCS planning excluded",1e-10);
        // Scope entry and exception exit use the existing nav-console patch.
        var scope = AccessTools.Method(typeof(TorchCoursePlanningPatch),"Prefix");
        var cleanup = AccessTools.Method(typeof(TorchCoursePlanningPatch),"Finalizer");
        object[] state = { null };
        scope.Invoke(null,state);
        Check(TorchTransferPlanning.ScopeDepth == 1,"Player UI enters scope");
        int workerScope = -1;
        var thread = new Thread(() => workerScope = TorchTransferPlanning.ScopeDepth); thread.Start(); thread.Join();
        Check(workerScope == 0,"Other threads do not inherit the player planning scope");
        object[] nestedState = { null }; scope.Invoke(null,nestedState);
        cleanup.Invoke(null,nestedState);
        Check(TorchTransferPlanning.ScopeDepth == 1,"Nested UI scope restores caller");
        cleanup.Invoke(null,state);
        Check(TorchTransferPlanning.ScopeDepth == 0,"Finalizer releases scope");
        engine.Patch(AccessTools.Method(typeof(Program),nameof(TransferScopeThrow)),prefix:new HarmonyMethod(scope),finalizer:new HarmonyMethod(cleanup));
        bool threw = false;
        try { TransferScopeThrow(); } catch (InvalidOperationException) { threw = true; }
        Check(threw && TorchTransferPlanning.ScopeDepth == 0,"Actual Harmony exception unwinding releases player planning scope");
        var released = (TorchTransferPlanning.Scope)AccessTools.Field(typeof(TorchTransferPlanning),"current").GetValue(null);
        Check(released.Target == null && released.Vessel == null,"Released planning scope retains no game references");
        FullTransferPlannerChecks(engine,limiter);
        Console.WriteLine("PASS: " + (checks-before) + " native transfer planner integration checks.");
    }
    static void CheckWaypoints(NavData course, double acceleration) {
        var points = StudyPoints(course);
        Check(points.Count > 1,"Nonempty native course");
        for (int i = 1; i < points.Count; i++) {
            double dt = points[i].ArrivalTime-points[i-1].ArrivalTime;
            Check(dt > 0 && TransferSolver.Finite(dt),"Strictly increasing waypoint epochs");
            double deltaV = (points[i].ObjSS.vVel-points[i-1].ObjSS.vVel).magnitude;
            double epochUlp = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(points[i].ArrivalTime)+1)-points[i].ArrivalTime;
            Check(deltaV <= acceleration*(dt+2*epochUlp)*(1+1e-8),"Generated native acceleration bounded including epoch quantization");
            Check(points[i].TorchFuelLevel <= points[i-1].TorchFuelLevel,"Fuel never generated by plotting");
        }
        Check(course.Destination.TorchCycle == 0,"Native arrival turns the torch off");
    }
    static void FullTransferPlannerChecks(Harmony engine, float limiter) {
        engine.Patch(AccessTools.Method(typeof(StarSystem),nameof(StarSystem.GetGreatestGravBO)),prefix:new HarmonyMethod(typeof(Program),nameof(TransferGravity)));
        engine.Patch(AccessTools.Method(typeof(StarSystem),nameof(StarSystem.IsWithinNoWakeRangeOfAnyStation)),prefix:new HarmonyMethod(typeof(Program),nameof(TransferOutsideNoWake)));
        engine.Patch(AccessTools.PropertyGetter(typeof(Ship),nameof(Ship.NavPlayerManned)),prefix:new HarmonyMethod(typeof(Program),nameof(TransferPlayerAtNav)));
        engine.Patch(AccessTools.Method(typeof(Ship),nameof(Ship.LogAdd)),prefix:new HarmonyMethod(typeof(Program),nameof(Skip)));
        CrewSim.system = Empty<StarSystem>(); CrewSim.system.dictShips = new Dictionary<string,Ship>();
        transferGravity = Empty<BodyOrbit>();
        var body = new BodyOrbit("TRANSFER-TARGET",.72,.72,0,0,.615,0,1,1,null);
        CrewSim.system.aBOs = new Dictionary<string,BodyOrbit> { [body.strName] = body };
        int accepted = 0, rejected = 0, nativeFailures = 0;
        foreach (double distanceKm in new[] { 10000d,1e6,1e8 }) foreach (float coast in new[] { 0f,.01f,.4f,.8f,1f })
            foreach (double angle in new[] { 0d,Math.PI/2,Math.PI }) {
                body.UpdateTime(fixtureEpoch);
                var target = new ShipSitu(false) { bBOLocked = true, strBOPORShip = body.strName };
                target.UpdateTime(fixtureEpoch);
                Point initial = target.vPos-new Point(distanceKm*1000/TransferAu,0);
                Point velocity = target.vVel+new Point(Math.Cos(angle),Math.Sin(angle))*(10000/TransferAu);
                var origin = new ShipSitu(false) { vPosx = initial.X, vPosy = initial.Y, vVelX = velocity.X, vVelY = velocity.Y };
                var path = TransferPath();
                var previous = TorchTransferPlanning.Enter();
                NavData nav;
                bool valid;
                try {
                    nav = path.PlanTrip4(new NavDataPoint(fixtureEpoch,origin,1e6,0,1e6),new NavDataPoint(fixtureEpoch,target,1e6,0,1e6),limiter,coast);
                    valid = TorchTransferPlanning.ArrivalValid(nav);
                    Check(ReferenceEquals(TorchTransferPlanning.Complete(nav),nav) == valid,"UI completion retains converged courses and rejects bad endpoints");
                    Check(body.dTimeCalcLast == fixtureEpoch,"Arrival validation restores shared BodyOrbit to current epoch");
                } finally { TorchTransferPlanning.Leave(previous); }
                Check(nav != null,"Full native ephemeris wrapper accepts vector transfer");
                target.UpdateTime(nav.Destination.ArrivalTime);
                double clearance = (nav.Destination.ObjSS.vPos-target.vPos).magnitude*TransferAu;
                if (!valid) {
                    rejected++;
                    body.UpdateTime(fixtureEpoch);
                    var nativeTarget = new ShipSitu(false) { bBOLocked = true, strBOPORShip = body.strName };
                    nativeTarget.UpdateTime(fixtureEpoch);
                    var nativePath = TransferPath();
                    var native = nativePath.PlanTrip4(new NavDataPoint(fixtureEpoch,origin,1e6,0,1e6),new NavDataPoint(fixtureEpoch,nativeTarget,1e6,0,1e6),limiter,coast);
                    nativeTarget.UpdateTime(native.Destination.ArrivalTime);
                    double nativeClearance = (native.Destination.ObjSS.vPos-nativeTarget.vPos).magnitude*TransferAu;
                    if (Math.Abs(nativeClearance-300000) > 1000) nativeFailures++;
                    else throw new Exception("Regression: native converges but replacement does not at " + distanceKm + "/" + coast + "/" + angle);
                    Check(Math.Abs(clearance-300000) > 750,"Arrival guard rejects actual misses");
                    continue;
                }
                accepted++;
                Check(Math.Abs(clearance-300000) < 1000,"Native 300km approach clearance preserved: " + distanceKm + "/" + coast + "/" + angle + " -> " + clearance);
                double arrivalError = (nav.Destination.ObjSS.vVel-target.vVel).magnitude*TransferAu;
                // BodyOrbit caches a one-second finite-difference velocity for
                // up to 1.5 seconds. Do not mistake that native quantization
                // for a trajectory error at a tens-of-km/s orbit speed.
                Check(arrivalError < .05,"Native orbital arrival velocity preserved: " + distanceKm + "/" + coast + "/" + angle + " -> " + arrivalError);
                CheckWaypoints(nav,ship.GetMaxTorchThrust(limiter));
            }
        Console.WriteLine("Full native moving-orbit wrapper: " + accepted + " valid plots; " + rejected + " rejected nonconvergent plots (" + nativeFailures + " also fail natively).");
    }
}
