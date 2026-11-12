using System;
using System.Collections.Generic;
using HarmonyLib;
using Ostranauts.ShipGUIs.Utilities;
using Ostranauts.Ships.Commands;
using UnityEngine;

static partial class Program {
    static bool PlanningGravity(ref BodyOrbit boClosest, ref BodyOrbit __result) {
        boClosest = Empty<BodyOrbit>(); __result = boClosest; return false;
    }
    static bool OutsideNoWake(ref bool __result) { __result = false; return false; }
    static bool PlayerAtNav(ref bool __result) { __result = true; return false; }
    static NavData FullPlot(Ship vessel, float coast) {
        var planner = Empty<FlyToPath>(); Set(planner, "_shipUs", vessel);
        vessel.bFusionReactorRunning = true;
        Set(vessel, "fRCSCount", 1f);
        var origin = new ShipSitu(false) { vPosx = 1, vPosy = 2 };
        var target = new ShipSitu(false) { vPosx = 1.001, vPosy = 2 };
        var course = planner.PlanTrip4(new NavDataPoint(1000, origin, 100, 0, 100000),
            new NavDataPoint(1000, target, 100, 0, 100000), .2f, coast);
        vessel.objSS.NavData = course;
        return course;
    }
    static void FullPlannerChecks() {
        var environment = new Harmony("offline.torch-tow.planner-environment");
        try {
            environment.Patch(AccessTools.Method(typeof(StarSystem), nameof(StarSystem.GetGreatestGravBO)), prefix: new HarmonyMethod(typeof(Program), nameof(PlanningGravity)));
            environment.Patch(AccessTools.Method(typeof(StarSystem), nameof(StarSystem.IsWithinNoWakeRangeOfAnyStation)), prefix: new HarmonyMethod(typeof(Program), nameof(OutsideNoWake)));
            environment.Patch(AccessTools.PropertyGetter(typeof(Ship), nameof(Ship.NavPlayerManned)), prefix: new HarmonyMethod(typeof(Program), nameof(PlayerAtNav)));
            CrewSim.system = Empty<StarSystem>(); CrewSim.system.dictShips = new Dictionary<string, Ship>();
            foreach (float coast in new[] { .4f, .8f, 1f }) {
                StarSystem.fEpoch = 1000;
                var tug = Make("FULL-PLANNER-TUG", 25000); var tow = Make("FULL-PLANNER-TOW", 75000);
                var group = Join(tug, tow);
                var solo = Make("FULL-PLANNER-SOLO", 25000);
                var reference = Make("FULL-PLANNER-REFERENCE", 100000);
                var course = FullPlot(tug, coast); var soloCourse = FullPlot(solo, coast); var referenceCourse = FullPlot(reference, coast);
                Check(course != null && soloCourse != null && referenceCourse != null, "Native planner rejected a feasible braced tow.");
                double duration = course.Destination.ArrivalTime - 1000;
                Near(duration, referenceCourse.Destination.ArrivalTime - 1000, "Tow planning differs from the same total mass", .001);
                Check(duration > (soloCourse.Destination.ArrivalTime - 1000) * 1.9, "Heavy tow retained solo travel time.");
                double last = 1000;
                foreach (double fraction in new[] { .01, .1, .3, .5, .75, 1d }) {
                    StarSystem.fEpoch = 1000 + duration * fraction;
                    reference.objSS.TimeAdvance(StarSystem.fEpoch - last);
                    Step(tug, tow, group, StarSystem.fEpoch - last);
                    Pose(tug, reference); Rigid(group);
                    last = StarSystem.fEpoch;
                }
                Near(tug.objSS.vPosx, course.Destination.ObjSS.vPosx, "Native plotted tow missed its endpoint");
                Near(tug.objSS.vVelX, course.Destination.ObjSS.vVelX, "Native plotted tow missed its braking velocity");
            }
            Console.WriteLine("Full native PlanTrip4: feasible tow accepted; mass-dependent ETA, drift settings, playback and braking match the equivalent total-mass ship.");
        } finally { environment.UnpatchSelf(); }
    }
}
