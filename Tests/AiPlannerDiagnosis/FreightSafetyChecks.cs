using System;
using System.Collections.Generic;
using System.Diagnostics;
using HarmonyLib;
using Ostrabus.ShipNavigation;
using Ostranauts.ShipGUIs.Utilities;

static class FreightSafetyChecks {
    const double AU = 149597872d;
    static int checks;
    static void Check(bool condition, string reason) { checks++; if (!condition) throw new Exception(reason); }
    static BodyOrbit Body(string name, double radius) => new BodyOrbit(new JsonBodyOrbitSave {
        strName = name, fRadius = radius / AU, fPeriod = 1e9, nOrbitDirection = 1
    });
    static Ship Hull(StarSystem world) {
        var ship = FreightCourseChecks.Empty<Ship>(); FreightCourseChecks.Collections(ship);
        ship.strRegID = "clearance-control"; ship.IsAIShip = true;
        FreightCourseChecks.Set(ship, "<objSS>k__BackingField", new ShipSitu(false));
        world.dictShips[ship.strRegID] = ship;
        return ship;
    }
    static NavData Route(Ship ship, params double[] xy) {
        var route = new NavData(ship);
        for (int i = 0; i < xy.Length; i += 2) route.AddNavDataPoint(new NavDataPoint(i * 25,
            new ShipSitu(false) { vPosx = xy[i] / AU, vPosy = xy[i + 1] / AU }, 100, 0, 100, 8));
        return route;
    }
    internal static void Run() {
        var scene = new Harmony("offline.freight-safety.scene");
        try {
            FreightCourseChecks.Scene(scene);
            var world = FreightCourseChecks.Empty<StarSystem>(); FreightCourseChecks.Collections(world);
            CrewSim.system = world; StarSystem.fEpoch = 0;
            var body = Body("solid", 10); world.aBOs.Add(body.strName, body);
            var ship = Hull(world);
            var crossing = Route(ship, -20, 0, 20, 0);
            Check(!CelestialRoutes.Clear(crossing, .1 / AU, 0, out var obstruction) && obstruction.Body == body,
                "Clear endpoints concealed a between-point planetary crossing");
            Check(NativeCourseGeometry.Inspect(crossing, 0, .1 / AU).ClearanceKm < -9,
                "Independent native oracle did not reproduce the crossing");
            var safe = Route(ship, -20, 12, 0, 12, 20, 12);
            Check(CelestialRoutes.Clear(safe, .1 / AU, 0, out _), "Clear hull path rejected");
            Check(NativeCourseGeometry.Inspect(safe, 0, .1 / AU).ClearanceKm > 1.89, "Native clear control disagrees");
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            var watch = Stopwatch.StartNew();
            for (int n = 0; n < 100000; n++) if (!CelestialRoutes.Clear(safe, .1 / AU, 0, out _)) throw new Exception("Warmed clearance changed");
            watch.Stop();
            Check(GC.GetAllocatedBytesForCurrentThread() - allocated < 512, "Warmed route admission allocates");
            Console.WriteLine("100,000 warmed clearance checks: " + watch.Elapsed.TotalMilliseconds.ToString("F2") + " ms");
            safe.GetNavDataPoints()[1].ObjSS.vPosy = 0;
            Check(!CelestialRoutes.Clear(safe, .1 / AU, 0, out _), "Cached receipt concealed an edited middle waypoint");
            safe.GetNavDataPoints()[1].ObjSS.vPosy = 12 / AU;
            Check(CelestialRoutes.Clear(safe, .1 / AU, 0, out _), "Restored geometry retained stale rejection");
            Check(!CelestialRoutes.Clear(safe, 3 / AU, 0, out _), "Cache ignored a larger connected hull envelope");
            Check(CelestialRoutes.Clear(crossing, .1 / AU, 45, out _), "Past intersection incorrectly rejected a clear remaining course");
            Check(!CelestialRoutes.Clear(crossing, .1 / AU, 0, out _), "Later-only receipt was reused for an earlier interval");
            var tangent = Route(ship, -20, 10.1, 20, 10.1);
            Check(!CelestialRoutes.Clear(tangent, .1 / AU, 0, out _), "Hull tangent was considered safely separated");
            Check(!CelestialRoutes.Clear(safe, double.NaN, 0, out _), "Invalid hull admitted");
            // A full native orbit moves through a stationary course while both
            // waypoint epochs are clear. Negative periods are legal native data.
            world.aBOs.Clear();
            var moving = new BodyOrbit(new JsonBodyOrbitSave { strName = "retrograde-solid", fRadius = 2 / AU,
                fAxis1 = 40 / AU, fAxis2 = 40 / AU, fPerh = 20 / AU, fAph = 20 / AU,
                fPeriod = -100, nOrbitDirection = 1 });
            world.aBOs.Add(moving.strName, moving);
            var stationary = Route(ship, 0, 20, 0, 20, 0, 20);
            Check(!CelestialRoutes.Clear(stationary, .1 / AU, 0, out _), "Moving retrograde body crossing missed");
            Check(NativeCourseGeometry.Inspect(stationary, 0, .1 / AU).ClearanceKm < -1.9,
                "Independent native moving-body oracle disagrees");
            world.aBOs.Clear(); world.aBOs.Add(body.strName, body);
            double x = -20 / AU, y = 0;
            var low = new ShipSitu(false) { vPosx = 10.01 / AU };
            bool native = FlightCPU.GetDockingNavPoint(low, body, ref x, ref y);
            Check(native, "Native exhausted-side-candidates defect not reproduced");
            x = -20 / AU; y = 0;
            Check(!CelestialRoutes.DockingPoint(low, body, ref x, ref y) && x > low.vPosx && y == 0,
                "Low-orbit failed detour did not use the near-side outward exit");
            body.nDrawFlagsBody = 1; x = -20 / AU; y = 0;
            double oldX = x, oldY = y;
            native = FlightCPU.GetDockingNavPoint(low, body, ref oldX, ref oldY);
            Check(CelestialRoutes.DockingPoint(low, body, ref x, ref y) == native && x == oldX && y == oldY,
                "Manufactured placeholder acquired physical surface avoidance");
            body.nDrawFlagsBody = 0;
            var attached = new ShipSitu(false) { bIsBO = true, strBOPORShip = body.strName, vBOOffsetx = 15 / AU };
            Check(CelestialRoutes.PointAt(attached, 100, out var position) && Math.Abs(position.X * AU - 15) < 1e-8,
                "Body-reference station/stand-off offset disappeared");
            var data = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(
                Ostrabus.Development.InstallationPaths.Fixture("Navigation/FreightCourses/pathcheck.json")));
            world = FreightCourseChecks.World(data); ship = FreightCourseChecks.Vessel(data["ships"]["O-2P3"]);
            ship.objSS.NavData = new NavData(data["ships"]["O-2P3"]["objSS"]["jnd"].ToObject<JsonNavData>());
            double vx = ship.objSS.vVelX, vy = ship.objSS.vVelY, px = ship.objSS.vPosx, py = ship.objSS.vPosy;
            var target = ship.shipScanTarget;
            var pilot = new FreightCourseChecks.Pilot { ShipUs = ship, Blackboard = new Ostranauts.Core.Blackboard() };
            Check(!AiRendezvous.RestoreCourse(ship, pilot), "Late historical impact incorrectly claimed a recoverable course");
            Check(ship.objSS.NavData == null && ship.objSS.vVelX == vx && ship.objSS.vVelY == vy &&
                ship.objSS.vPosx == px && ship.objSS.vPosy == py && ReferenceEquals(ship.shipScanTarget, target),
                "Unsafe-course release cancelled momentum, moved a doomed ship or changed its destination");
            Console.WriteLine(checks + " freight clearance, cache, native oracle and detour checks passed.");
        } finally { scene.UnpatchSelf(); CrewSim.system = null; }
    }
}
