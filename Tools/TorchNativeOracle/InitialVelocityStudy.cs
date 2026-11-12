using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using HarmonyLib;
using Ostrabus.TorchCalculator;
using Ostranauts.ShipGUIs.Utilities;
using Ostranauts.Ships.Commands;
using Ostranauts.Utils.Models;

static partial class Program {
    // Investigation only: native PlanTrip4Sub and waypoint generation, inertial
    // stationary target. No production planner or playback patches are installed.
    static void RunInitialVelocityStudy() {
        const double metersPerAu = 149597872000;
        var fit = new ReactorFit { MassKg = 26798, Feeders = 1, Lasers = 1, Capacitors = 2, Regulators = 2 };
        var fusion = Make(fit,TorchModel.Ideal,.3,.8);
        AccessTools.Method(typeof(FusionIC),"Run").Invoke(fusion,new object[] { TorchModel.TickSeconds });
        ship.fFusionThrustMax = TorchModel.RatedThrust(TorchModel.PelletMax(fit));
        Field(ship,"fFuelEfficiencyMod",1d);
        float lo = 0, hi = 1;
        for (int i = 0; i < 32; i++) {
            float mid = (lo + hi) * .5f;
            if (ship.GetMaxTorchThrust(mid) * metersPerAu < 3 * 9.81) lo = mid;
            else hi = mid;
        }
        float limiter = hi;
        double aAu = ship.GetMaxTorchThrust(limiter), a = aAu * metersPerAu;
        Check(Math.Abs(a - 29.43) < 1e-4,"Fixture supplies 3G using native thrust law");
        var rows = new List<string> { "distanceKm,initialClosingMps,initialCrossMps,accelerationMps2,nativeFirstMatchSeconds,nativeDurationSeconds,nativeDeltaVMps,analyticDurationSeconds,analyticDeltaVMps,nativeEndpointErrorM,analyticEndpointErrorM" };
        var kernel = AccessTools.Method(typeof(FlyToPath),"PlanTrip4Sub");
        var burn = AccessTools.Method(typeof(FlyToPath),"PlotBurnLoop");
        foreach (double distanceKm in new[] { 10000d,100000d }) foreach (var velocity in new[] {
            (0d,0d),(1000d,0d),(10000d,0d),(-10000d,0d),(30000d,0d),(0d,10000d),(10000d,10000d)
        }) {
            double u = velocity.Item1, cross = velocity.Item2, distance = distanceKm * 1000;
            var path = Empty<FlyToPath>(); Field(path,"_shipUs",ship);
            Field(path,"fTorchSpeedLimit",1d); // No drift/peak-speed cap in this kernel comparison.
            var origin = new NavDataPoint(fixtureEpoch,new ShipSitu(false) { vVelX = u / metersPerAu, vVelY = cross / metersPerAu },1e9,0,1e9);
            var target = new NavDataPoint(fixtureEpoch,new ShipSitu(false) { vPosx = distance / metersPerAu },1e9,0,1e9);
            double expectedMatch = Math.Sqrt(u*u + cross*cross) / a;
            double estimate = 2 * Math.Sqrt(distance / a) + expectedMatch;
            var native = (NavData)kernel.Invoke(path,new object[] { origin,target,limiter,estimate,true,false });
            var matched = StudyPoints(native)[19]; // Native initial easing: 10 fine + 9 coarse waypoints.
            Near(matched.ArrivalTime - fixtureEpoch,expectedMatch,"Native first burn duration",1e-7);
            Check(matched.ObjSS.vVel.magnitude * metersPerAu < 1e-6,"Native first burn cancels ALL incoming relative velocity");
            double error = (native.Destination.ObjSS.vPos - target.ObjSS.vPos).magnitude * metersPerAu;
            Check(error < .01,"Native inertial fixture arrives at its target");
            Check(native.Destination.ObjSS.vVel.magnitude * metersPerAu < 1e-6,"Native arrival velocity matches target");
            double duration = native.Destination.ArrivalTime - fixtureEpoch;
            double deltaV = InitialVelocityDeltaV(native) * metersPerAu;
            double candidateTime = double.NaN, candidateDv = double.NaN, candidateError = double.NaN;
            if (cross == 0) {
                // Exact 1D minimum-time rendezvous with bounded |acceleration|.
                // If stopping would overshoot, brake through zero, reverse, then brake again.
                bool overshoot = u > 0 && u*u > 2*a*distance;
                double peak = Math.Sqrt(overshoot ? .5*u*u - a*distance : a*distance + .5*u*u);
                double t1 = overshoot ? (u + peak) / a : (peak - u) / a;
                double t2 = peak / a;
                double sign = overshoot ? -1 : 1;
                Check(t1 >= 0 && t2 >= 0,"Analytic phase durations are physical");
                var candidate = new NavData(ship);
                candidate.AddNavDataPoint(origin.Clone());
                foreach (var phase in new[] { (t1,sign), (t2,-sign) })
                    burn.Invoke(path,new object[] { candidate,10,phase.Item1 / 10,new Point(phase.Item2*aAu,0),true,false,limiter,target,":momentum-study",0,0 });
                candidateTime = candidate.Destination.ArrivalTime - fixtureEpoch;
                candidateDv = InitialVelocityDeltaV(candidate) * metersPerAu;
                candidateError = (candidate.Destination.ObjSS.vPos - target.ObjSS.vPos).magnitude * metersPerAu;
                Check(candidateError < .01,"Analytic course reaches target through native waypoint generator");
                Check(candidate.Destination.ObjSS.vVel.magnitude * metersPerAu < 1e-6,"Analytic arrival velocity matches target");
                Check(candidateTime <= duration + 1e-6,"Analytic course takes no longer than cancellation-first course");
                if (u == 0) Near(candidateTime,duration,"Rest-start control agrees with native",1e-10);
                if (u > 0 && !overshoot) Check(candidateDv < deltaV,"Useful closing momentum saves delta-v");
            }
            rows.Add(string.Join(",",Array.ConvertAll(new[] { distanceKm,u,cross,a,expectedMatch,duration,deltaV,candidateTime,candidateDv,error,candidateError },x => x.ToString("R",CultureInfo.InvariantCulture))));
        }
        Directory.CreateDirectory("obj");
        File.WriteAllLines("obj/torch-initial-velocity.csv",rows);
        Console.WriteLine("PASS: " + checks + " initial-velocity investigation checks; native kernel and native-generated analytic courses. Results: obj/torch-initial-velocity.csv");
    }
    static double InitialVelocityDeltaV(NavData nav) {
        double result = 0;
        var points = StudyPoints(nav);
        for (int i = 1; i < points.Count; i++) result += (points[i].ObjSS.vVel - points[i-1].ObjSS.vVel).magnitude;
        return result;
    }
}
