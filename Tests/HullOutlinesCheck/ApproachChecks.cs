using System;
using System.Collections.Generic;
using System.Diagnostics;
using BepInEx.Configuration;
using HarmonyLib;
using Ostrabus.NavConsole.Outlines;
using Ostrabus.Orbital;
using Ostrabus.ShipNavigation;
using Ostrabus.ShipNavigation.Collisions;
using Ostranauts.ShipGUIs.Utilities;
using SolarSystem;

static partial class Program {
    static Envelope ApproachBox(double width, double height) {
        var points = new[] { new HullPoint(-width / 2, -height / 2), new HullPoint(width / 2, -height / 2),
            new HullPoint(width / 2, height / 2), new HullPoint(-width / 2, height / 2) };
        return new EnvelopeBuilder().Build(points, points.Length, null);
    }
    static void ApproachCases() {
        int before = checks;
        var oldWorld = CrewSim.system; double oldEpoch = StarSystem.fEpoch;
        var oldFlag = PhysicalCollisions.Flag;
        var engine = new Harmony("offline.hull.approach.engine");
        try {
            engine.Patch(AccessTools.Method(typeof(StarSystem), nameof(StarSystem.GetNearestStationRegional)),
                prefix: new HarmonyMethod(typeof(Program), nameof(AvoidanceStation)));
            engine.Patch(AccessTools.PropertyGetter(typeof(Ship), nameof(Ship.RCSAccelMax)),
                prefix: new HarmonyMethod(typeof(Program), nameof(AvoidanceAcceleration)));
            engine.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.HasOpenDockingPorts)),
                prefix: new HarmonyMethod(typeof(Program), nameof(DockingPorts)));
            engine.Patch(AccessTools.Method(typeof(CrewSim), nameof(CrewSim.GetSelectedCrew)),
                prefix: new HarmonyMethod(typeof(Program), nameof(NoDockingCrew)));
            foreach (bool reverseOrder in new[] { false, true }) {
                HullDisplay.Reset();
                PhysicalCollisions.Flag = Empty<ConfigEntry<bool>>(); Field(PhysicalCollisions.Flag, "_typedValue", true);
                CrewSim.system = Empty<StarSystem>(); CrewSim.system.aBOs = new Dictionary<string, BodyOrbit>();
                StarSystem.fEpoch = 1000;
                var a = CollisionHull("APPROACH-LEAD", -10000, 800);
                var b = CollisionHull("APPROACH-FOLLOWER", 0, 0);
                var ga = ApproachBox(40, 4); var gb = ApproachBox(40, 4);
                WarmCollision(a, ga, true); WarmCollision(b, gb, true);
                avoidanceStation = CollisionHull("APPROACH-HOME", 1e8, 0); avoidanceStation.objSS.bIsBO = true;
                a.shipScanTarget = avoidanceStation;
                a.objSS.vVelX = 400 * HullZoom.MetresToAU;
                CrewSim.system.dictShips = new Dictionary<string, Ship> { { a.strRegID, a }, { b.strRegID, b } };
                var production = new Harmony("offline.hull.approach.production." + reverseOrder);
                try {
                    production.CreateClassProcessor(reverseOrder ? typeof(HullAvoidancePatch) : typeof(AIShipAvoidancePatch)).Patch();
                    production.CreateClassProcessor(reverseOrder ? typeof(AIShipAvoidancePatch) : typeof(HullAvoidancePatch)).Patch();
                    production.CreateClassProcessor(typeof(HullAvoidanceClearancePatch)).Patch();
                    Check(!AIShipManager.IsOnCollisionCourse(a, out _), "Clear thin-hull pass still causes a full dodge");
                    a.objSS.vPosy = 0;
                    Check(AIShipManager.IsOnCollisionCourse(a, out _), "Real closing hulls lost their warning");
                    a.objSS.vVelX = -400 * HullZoom.MetresToAU;
                    Check(!AIShipManager.IsOnCollisionCourse(a, out _), "Past-only encounter returned");
                    a.objSS.vPosx = 10000 * HullZoom.MetresToAU;
                    Check(AIShipManager.IsOnCollisionCourse(a, out _), "Rear-end threat was exempted");
                    Check(ReferenceEquals(a.shipScanTarget, avoidanceStation), "Collision query changed the escape destination");

                    // Native clamp eligibility is available before physical
                    // overlap, even while padded avoidance circles overlap.
                    a.objSS.vPosx = a.objSS.vPosy = a.objSS.vVelX = 0;
                    b.objSS.vPosx = -(ga.MapRadius + gb.MapRadius) * 1.05 * HullZoom.MetresToAU;
                    var panel = DockPanel(b, a);
                    production.CreateClassProcessor(typeof(HullDockingGeometryPatch)).Patch();
                    Check(CanClamp(panel) && !AIShipManager.IsOnCollisionCourse(a, out _),
                        "A cleared/forced docking clamp position triggers phantom evasion");
                    b.Comms.Clearance = null;
                    Check(!CanClamp(panel), "Approach repair bypassed docking clearance");
                    b.objSS.vPosx = -10000 * HullZoom.MetresToAU;
                    b.objSS.vVelX = 400 * HullZoom.MetresToAU;
                    Check(AIShipManager.IsOnCollisionCourse(a, out _), "Docking approach disabled genuine rear-end protection");

                    // Independent axis-aligned rectangle oracle, sampled much
                    // more finely than the query's conservative chord bound.
                    var random = new Random(381);
                    b.objSS.vPosx = b.objSS.vVelX = 0;
                    for (int sample = 0; sample < 80; sample++) {
                        double x = random.NextDouble() * 12000 - 6000;
                        double y = random.NextDouble() * 2000 - 1000;
                        double vx = random.NextDouble() * 600 - 300;
                        double vy = random.NextDouble() * 100 - 50;
                        double ax = random.NextDouble() * 12 - 6;
                        double ay = random.NextDouble() * 12 - 6;
                        a.objSS.vPosx = x * HullZoom.MetresToAU; a.objSS.vPosy = y * HullZoom.MetresToAU;
                        a.objSS.vVelX = vx * HullZoom.MetresToAU; a.objSS.vVelY = vy * HullZoom.MetresToAU;
                        a.objSS.vAccRCS = new UnityEngine.Vector2((float)(ax * HullZoom.MetresToAU), (float)(ay * HullZoom.MetresToAU));
                        double minimumGap = double.PositiveInfinity;
                        for (int tick = 0; tick <= 15000; tick++) {
                            double t = tick / 500d;
                            minimumGap = Math.Min(minimumGap, Math.Max(Math.Abs(x + vx * t + .5 * ax * t * t) - 1600,
                                Math.Abs(y + vy * t + .5 * ay * t * t) - 160));
                        }
                        var copy = new ShipSitu(a.objSS, false); copy.Size *= 2;
                        bool hit = HullAvoidance.PredictFuture(copy, b.objSS, 30, a, b);
                        if (minimumGap < -1) Check(hit, "Accelerated exterior crossing escaped the narrow forecast");
                        if (minimumGap > 1) Check(!hit, "Accelerated clear pass acquired a phantom exterior hit");
                    }
                    a.objSS.vAccRCS = default; a.objSS.vVelY = 0;
                    a.objSS.vPosx = -10000 * HullZoom.MetresToAU;
                    a.objSS.vPosy = 800 * HullZoom.MetresToAU; a.objSS.vVelX = 400 * HullZoom.MetresToAU;
                    a.objSS.fW = .2f;
                    Check(AIShipManager.IsOnCollisionCourse(a, out _), "Rotating beam lost its swept-hull warning");
                    a.objSS.fW = 0; a.objSS.fA = .2f;
                    Check(AIShipManager.IsOnCollisionCourse(a, out _), "Unresolved angular acceleration was declared clear");
                    a.objSS.vPosy = 2200 * HullZoom.MetresToAU;
                    Check(!AIShipManager.IsOnCollisionCourse(a, out _), "Angular fallback retained inflated rather than actual enclosing circles");
                    a.objSS.fA = 0;

                    // Unknown geometry must keep the original broad warning.
                    HullDisplay.Reset(); WarmCollision(a, ga, true);
                    WarmCollision(b, new Envelope(new[] { new HullPoint(-20,-2), new HullPoint(20,-2), new HullPoint(20,2), new HullPoint(-20,2) }, 4), true);
                    a.objSS.vPosy = 800 * HullZoom.MetresToAU;
                    Check(AIShipManager.IsOnCollisionCourse(a, out _), "Unavailable collision cover was treated as a verified miss");
                    HullDisplay.Reset(); WarmCollision(a, ga, true); WarmCollision(b, gb, true);
                    ApproachGroup(a, b);
                    ApproachPlans(a, b);

                    var steady = new ShipSitu(a.objSS, false); steady.Size *= 2;
                    for (int i = 0; i < 30; i++) HullAvoidance.PredictFuture(steady, b.objSS, 30, a, b);
                    var clock = Stopwatch.StartNew(); long allocated = GC.GetAllocatedBytesForCurrentThread();
                    for (int i = 0; i < 1000; i++) Check(!HullAvoidance.PredictFuture(steady, b.objSS, 30, a, b), "Warmed clear result changed");
                    long used = GC.GetAllocatedBytesForCurrentThread() - allocated; clock.Stop();
                    Check(used == 0, "Warmed exterior confirmation allocated per query: " + used);
                    Console.WriteLine("1000 exterior approach confirmations: " + clock.Elapsed.TotalMilliseconds + "ms, zero warmed allocation.");
                    Check(steady.vPosx == a.objSS.vPosx && a.objSS.Size == 100 && b.objSS.Size == 100,
                        "Approach forecast changed native size or motion");
                } finally { production.UnpatchSelf(); }
            }
            Console.WriteLine("Hull approach/forced-docking audit: " + (checks - before) + " checks passed in both patch orders.");
        } finally {
            engine.UnpatchSelf(); HullDisplay.Reset(); PhysicalCollisions.Flag = oldFlag;
            CrewSim.system = oldWorld; StarSystem.fEpoch = oldEpoch;
        }
    }
    static void ApproachGroup(Ship a, Ship b) {
        var child = CollisionHull("APPROACH-CHILD", 0, 0);
        WarmCollision(child, ApproachBox(4, 4), true);
        var group = new DockGroup { Anchor = new ShipSitu(a.objSS, false) };
        group.DockedShips.Add(new DockGroupMember(a, false)); group.DockedShips.Add(new DockGroupMember(child, false));
        a.objSS._dockGroup = child.objSS._dockGroup = group;
        a.objSS._dockOffsetPos = new Ostranauts.Utils.Models.Point(0, 0);
        child.objSS._dockOffsetPos = new Ostranauts.Utils.Models.Point(0, -800 * HullZoom.MetresToAU);
        Check(AIShipManager.IsOnCollisionCourse(a, out _), "Attached hull crossing was ignored");
        child.objSS._dockOffsetPos.Y = 3000 * HullZoom.MetresToAU;
        Check(!AIShipManager.IsOnCollisionCourse(a, out _), "Empty space within a docking-group circle triggers a dodge");
        var route = new NavData(a);
        route.AddNavDataPoint(new NavDataPoint(StarSystem.fEpoch, new ShipSitu(a.objSS, false)));
        route.AddNavDataPoint(new NavDataPoint(StarSystem.fEpoch + 30, new ShipSitu(a.objSS, false)));
        var probe = HullApproach.Rent();
        try { Check(!probe.Prepare(a, b, route), "Unverified routed-flotilla offset was treated as exact geometry"); }
        finally { HullApproach.Release(probe); }
        a.objSS._dockGroup = child.objSS._dockGroup = null;
    }
    static void ApproachPlans(Ship a, Ship b) {
        // Exactly float PI follows the positive native tie, unlike comparing
        // float PI to double PI. Preserve authored yaw direction at that edge.
        var yawRoute = new NavData(a);
        yawRoute.AddNavDataPoint(new NavDataPoint(StarSystem.fEpoch, new ShipSitu(false)));
        yawRoute.AddNavDataPoint(new NavDataPoint(StarSystem.fEpoch + 1, new ShipSitu(false) { fRot = (float)Math.PI }));
        var yawProbe = new ApproachYaw();
        var zero = new ShipSitu(false) { Size = 100 };
        Check(AvoidanceRouteForecast.TryCollision(zero, zero, 30, yawRoute, null, out bool yawHit, probe: yawProbe) && yawHit,
            "Planned yaw fixture did not reach exterior confirmation");
        float nativeYaw = (float)AccessTools.Method(typeof(NavData), "LerpAngle").Invoke(yawRoute,
            new object[] { 0f, (float)Math.PI, 1f });
        Check(Math.Abs(yawProbe.End - nativeYaw) < 1e-6, "Future rotation disagrees with native float-PI tie direction");
        yawRoute.GetNavDataPoints()[1].ObjSS = null;
        Check(AvoidanceRouteForecast.TryCollision(zero, zero, 30, yawRoute, null, out bool unresolved, probe: yawProbe) && unresolved,
            "Unresolved route became a verified exterior miss");
        Check(!AvoidanceRouteForecast.TryCollision(zero, zero, 30, yawRoute, null, out _),
            "Original route fallback changed without exterior confirmation");
        a.objSS.vPosx = -10000 * HullZoom.MetresToAU; a.objSS.vPosy = 800 * HullZoom.MetresToAU;
        a.objSS.vVelX = 400 * HullZoom.MetresToAU; a.objSS.vVelY = 0;
        NavData Route(float yaw) {
            var value = new NavData(a);
            value.AddNavDataPoint(new NavDataPoint(StarSystem.fEpoch, new ShipSitu(a.objSS, false)));
            value.AddNavDataPoint(new NavDataPoint(StarSystem.fEpoch + 30,
                new ShipSitu(false) { vPosx = 10000 * HullZoom.MetresToAU, vPosy = a.objSS.vPosy, fRot = yaw }));
            return value;
        }
        a.objSS.NavData = Route(0);
        Check(!AIShipManager.IsOnCollisionCourse(a, out _), "A safe planned pass was rejected by padded circles");
        a.objSS.NavData = Route((float)Math.PI);
        Check(AIShipManager.IsOnCollisionCourse(a, out _), "Native planned rotation was omitted from the swept exterior");
        a.objSS.NavData = null;
        var candidate = new NavData(a);
        candidate.AddNavDataPoint(new NavDataPoint(StarSystem.fEpoch, new ShipSitu(a.objSS, false)));
        candidate.AddNavDataPoint(new NavDataPoint(StarSystem.fEpoch + 30,
            new ShipSitu(false) { vPosx = -5000 * HullZoom.MetresToAU, vPosy = a.objSS.vPosy }));
        candidate.AddNavDataPoint(new NavDataPoint(StarSystem.fEpoch + 60, new ShipSitu(false)));
        var flight = AvoidanceQuery.BeginFlight(a);
        try {
            a.objSS.NavData = candidate;
            Check(AvoidanceQuery.Candidate(a, candidate), "Candidate admission fixture failed");
            Check(AIShipManager.IsOnCollisionCourse(a, out _), "An unsafe later candidate crossing escaped geometric admission");
        } finally { a.objSS.NavData = null; AvoidanceQuery.EndFlight(flight); }
    }
    sealed class ApproachYaw : IApproachProbe {
        internal double End;
        public bool Collides(DVector p, DVector v, DVector acceleration, double elapsed, double seconds,
            double ourStart, double ourEnd, double theirStart, double theirEnd) { End = ourEnd; return true; }
    }
}
