using System;
using System.Diagnostics;
using Ostrabus.ShipNavigation.TorchCourses;
using Ostranauts.Utils.Models;

static partial class Program {
    static void RunTransferSolverChecks() {
        int scenarios = 0;
        var clock = Stopwatch.StartNew();
        foreach (double distance in new[] { 1d,1e7,1e11 }) foreach (double a in new[] { .01,29.43 })
            foreach (double relativeSpeed in new[] { 0d,.01,.5,1,Math.Sqrt(2),2d,10,100 })
            foreach (double angle in new[] { 0d,.01,.5,Math.PI/2,2.4,Math.PI })
            foreach (double speedFraction in new[] { .001,.1,.5,1,2,1e4 }) {
                Point d = new Point(distance,0);
                double scale = Math.Sqrt(a*distance);
                Point u = new Point(Math.Cos(angle),Math.Sin(angle)) * (relativeSpeed*scale);
                double cap = scale*speedFraction;
                Check(TransferSolver.Solve(d,u,a,cap,out var plan),"Transfer solves " + distance + "/" + a + "/" + relativeSpeed + "/" + angle + "/" + speedFraction);
                CheckTransfer(d,u,a,cap,plan);
                if (relativeSpeed == 0 && speedFraction >= 1) Near(plan.Duration,2*Math.Sqrt(distance/a),"Rest-start analytic duration",1e-9);
                if (angle == 0 && speedFraction == 1e4) {
                    bool overshoot = relativeSpeed > Math.Sqrt(2);
                    double peak = Math.Sqrt(overshoot ? .5*u.X*u.X-a*distance : a*distance+.5*u.X*u.X);
                    double expected = overshoot ? (u.X+2*peak)/a : (2*peak-u.X)/a;
                    Near(plan.Duration,expected,"Collinear minimum-time solution",1e-7);
                }
                scenarios++;
            }
        var random = new Random(4087);
        for (int i = 0; i < 3000; i++) {
            double distance = Math.Pow(10,random.NextDouble()*11);
            double a = Math.Pow(10,random.NextDouble()*4-2);
            double scale = Math.Sqrt(a*distance), heading = random.NextDouble()*Math.PI*2;
            Point d = new Point(Math.Cos(heading),Math.Sin(heading))*distance;
            double speed = scale*Math.Pow(10,random.NextDouble()*4-2);
            double bearing = random.NextDouble()*Math.PI*2;
            Point u = new Point(Math.Cos(bearing),Math.Sin(bearing))*speed;
            double cap = scale*Math.Pow(10,random.NextDouble()*5-3);
            Check(TransferSolver.Solve(d,u,a,cap,out var plan),"Random transfer solves " + i);
            CheckTransfer(d,u,a,cap,plan);
            scenarios++;
        }
        Check(!TransferSolver.Solve(default,default,1,1,out _),"Zero distance excluded");
        Check(!TransferSolver.Solve(new Point(1,0),default,0,1,out _),"Zero acceleration excluded");
        Check(!TransferSolver.Solve(new Point(1,0),default,1,double.NaN,out _),"Nonfinite cap excluded");
        Check(!TransferSolver.Solve(new Point(double.PositiveInfinity,0),default,1,1,out _),"Nonfinite displacement excluded");
        TransferSolver.Solve(new Point(1e8,2e7),new Point(10000,5000),29.43,1000,out _);
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) TransferSolver.Solve(new Point(1e8,2e7),new Point(10000,5000),29.43,1000,out _);
        long allocated = GC.GetAllocatedBytesForCurrentThread()-allocatedBefore;
        Check(allocated == 0,"Solver allocates no managed memory per calculation: " + allocated);
        Console.WriteLine("PASS: " + checks + " transfer solver checks across " + scenarios + " scenarios in " + clock.ElapsedMilliseconds + " ms.");
    }
    static void CheckTransfer(Point d, Point u, double a, double cap, TransferSolver.Transfer plan) {
        Check(plan.FirstSeconds >= 0 && plan.CoastSeconds >= 0 && plan.LastSeconds >= 0 && TransferSolver.Finite(plan.Duration),"Finite nonnegative phases");
        Check(plan.FirstAcceleration.magnitude <= a*(1+1e-9) && plan.LastAcceleration.magnitude <= a*(1+1e-9),"Acceleration bounded");
        Point position = default, velocity = u;
        foreach (var stage in new[] { (plan.FirstSeconds,plan.FirstAcceleration),(plan.CoastSeconds,default(Point)),(plan.LastSeconds,plan.LastAcceleration) }) {
            // Independent step integration, rather than the solver's endpoint formula.
            for (int j = 0; j < 17; j++) {
                double dt = stage.Item1/17;
                position += velocity*dt + stage.Item2*(.5*dt*dt);
                velocity += stage.Item2*dt;
            }
        }
        double scale = Math.Max(d.magnitude,u.sqrMagnitude/a);
        Check((position-d).magnitude <= Math.Max(1e-7,scale*1e-9),"Integrated endpoint position");
        Check(velocity.magnitude <= Math.Max(1e-8,Math.Max(u.magnitude,plan.Cruise.magnitude)*1e-9),"Integrated arrival velocity");
        Check(plan.Cruise.magnitude <= cap*(1+1e-8),"Cruise respects cap (inherited overspeed may brake down to it)");
    }
}
