using System;
using System.Collections.Generic;
using HarmonyLib;
using Ostrabus.ShipNavigation.Towing;
using Ostranauts.ShipGUIs.Utilities;
using SolarSystem;
using UnityEngine;

static partial class Program {
    static void Pose(Ship actual, Ship expected) {
        Near(actual.objSS.vPosx, expected.objSS.vPosx, "Tug course X changed");
        Near(actual.objSS.vPosy, expected.objSS.vPosy, "Tug course Y changed");
        Near(actual.objSS.vVelX, expected.objSS.vVelX, "Tug course VX changed");
        Near(actual.objSS.vVelY, expected.objSS.vVelY, "Tug course VY changed");
        Near(actual.objSS.fRot, expected.objSS.fRot, "Tug course orientation changed", 1e-7);
        Near(actual.fShallowFusionRemain, expected.fShallowFusionRemain, "Tow changed native fuel playback", .02);
    }
    static void Rigid(DockGroup group) {
        foreach (var member in group.DockedShips) {
            var ship = member.Ship;
            double x = ship.objSS._dockOffsetPos.X, y = ship.objSS._dockOffsetPos.Y;
            double cosine = Math.Cos(group.Anchor.fRot), sine = Math.Sin(group.Anchor.fRot);
            Near(ship.objSS.vPosx, group.Anchor.vPosx + x * cosine - y * sine, "Tow X offset changed");
            Near(ship.objSS.vPosy, group.Anchor.vPosy + x * sine + y * cosine, "Tow Y offset changed");
            Near(ship.objSS.fRot, group.Anchor.fRot + ship.objSS._dockOffsetRot, "Tow angular offset changed", 1e-7);
            Near(ship.objSS.vVelX, group.Anchor.vVelX, "Tow velocity separated");
        }
    }
    static void RegressionChecks() {
        foreach (bool reverse in new[] { false, true }) foreach (bool sff in new[] { false, true }) foreach (double duration in new[] { 100d, 21600d }) {
            fastForward = sff;
            var tug = Make("COURSE-TUG", 25000); var tow = Make("COURSE-TOW", 75000);
            var group = Join(tug, tow);
            if (reverse) group.DockedShips.Reverse();
            var reference = Make("REFERENCE", 25000);
            Course(tug, duration: duration); Course(reference, duration: duration);
            group.Anchor.vAccIn = new Vector2(7e-12f, -5e-12f);
            group.Anchor.fA = .03f;
            double last = 1000;
            foreach (double fraction in new[] { .01, .1, .3, .5, .75, 1d, 1.1 }) {
                StarSystem.fEpoch = 1000 + duration * fraction;
                double seconds = StarSystem.fEpoch - last;
                int before = playbackCalls;
                reference.objSS.TimeAdvance(seconds);
                Step(tug, tow, group, seconds);
                Check(playbackCalls - before == 2, "Course was lost or executed more than once per group step.");
                Pose(tug, reference); Rigid(group);
                Check(group.Anchor.NavData == null, "Course ownership was copied to the synthetic anchor.");
                last = StarSystem.fEpoch;
            }
            Check(tug.objSS.NavData == null, "Completed course did not retire natively.");
        }
        fastForward = false;

        var coastingTug = Make("COAST-LOAD-TUG", 25000); var coastingTow = Make("COAST-LOAD-TOW", 75000);
        var coastingGroup = Join(coastingTug, coastingTow);
        var coastingCourse = Course(coastingTug);
        foreach (var point in coastingCourse.GetNavDataPoints()) point.ObjSS.vAccIn = Vector2.zero;
        coastingGroup.Anchor.vAccIn = new Vector2(1e-9f, 0);
        StarSystem.fEpoch = 1050;
        Step(coastingTug, coastingTow, coastingGroup, 50);
        Near(loads[coastingTug], 0, "Group crew/tow load retained the old burn during a plotted coast", 1e-12);

        foreach (double step in new[] { 1d, 53, 3600, 21600 }) {
            var tug = Make("GENERATED-TUG", 25000); var tow = Make("GENERATED-TOW", 75000);
            var group = Join(tug, tow);
            var reference = Make("GENERATED-REFERENCE", 100000);
            var course = BurnCoastBrake(tug); BurnCoastBrake(reference);
            double last = 1000, end = course.Destination.ArrivalTime;
            int coastChecks = 0;
            while (last < end) {
                StarSystem.fEpoch = Math.Min(end, last + step);
                reference.objSS.TimeAdvance(StarSystem.fEpoch - last);
                Step(tug, tow, group, StarSystem.fEpoch - last);
                Pose(tug, reference); Rigid(group);
                if (StarSystem.fEpoch > 10000 && StarSystem.fEpoch < 20000 && coastChecks++ == 0)
                    Near(group.Anchor.vAccIn.magnitude, 0, "Coasting inherited stale group thrust", 1e-16);
                last = StarSystem.fEpoch;
            }
        }

        var powered = Make("MASS-TUG", 25000); var cargo = Make("MASS-TOW", 75000);
        double solo = powered.GetMaxTorchThrust(1);
        var massGroup = Join(powered, cargo);
        Near(powered.GetMaxTorchThrust(1), solo / 4, "Planning ignored attached inertial mass", 1e-17);
        powered.SetThrust(1000000);
        Near(powered.objSS.vAccIn.magnitude, 10 / 149597872000d, "Manual torch thrust ignored attached inertial mass", 1e-17);
        Near(massGroup.Anchor.vAccIn.magnitude, powered.objSS.vAccIn.magnitude, "Manual thrust was not propagated to the group", 1e-17);
        cargo.bDestroyed = true;
        Near(powered.GetMaxTorchThrust(1), solo, "Destroyed tow still reduced thrust", 1e-17);
        cargo.bDestroyed = false;
        Set(powered, "<objSS>k__BackingField", new ShipSitu(false));
        Near(powered.GetMaxTorchThrust(1), solo, "Released tug retained the tow mass", 1e-17);

        var restored = Make("SAVE-TUG", 10000); var restoredTow = Make("SAVE-TOW", 10000);
        var restoredGroup = Join(restored, restoredTow);
        var saved = Course(restored).GetJSON();
        CrewSim.system = Empty<StarSystem>(); CrewSim.system.dictShips = new Dictionary<string, Ship> { [restored.strRegID] = restored };
        restored.objSS.NavData = new NavData(saved);
        StarSystem.fEpoch = 1050;
        Step(restored, restoredTow, restoredGroup, 50);
        Near(restored.objSS.vPosx, 1.0000005, "Native restored course did not move the tow");
        Rigid(restoredGroup);

        var first = Make("CONFLICT-A", 10000); var second = Make("CONFLICT-B", 10000);
        var conflict = Join(first, second);
        Course(first); Course(second);
        Check(DockedCoursePlayback.FindSource(conflict) == null, "Two active courses were silently arbitrated.");
        Check(!DockedCoursePlayback.DeferMember(first.objSS, 1), "Ambiguous group suppressed ordinary native movement.");
        second.objSS.NavData = null;
        conflict.Anchor.bBOLocked = true;
        Check(DockedCoursePlayback.FindSource(conflict) == null, "A planet-locked group was made movable.");
        conflict.Anchor.bBOLocked = false; second.objSS.bOrbitLocked = true;
        Check(DockedCoursePlayback.FindSource(conflict) == null, "An orbit-locked group member was made movable.");
        second.objSS.bOrbitLocked = false;
        conflict.DockedShips[1] = new DockGroupMember(second, true);
        Check(DockedCoursePlayback.FindSource(conflict) == null, "A mooring target was made movable.");
        conflict.DockedShips[1] = new DockGroupMember(second, false);
        conflict.Anchor.NavData = first.objSS.NavData;
        Check(DockedCoursePlayback.FindSource(conflict) == null, "An existing anchor course was overridden.");
        conflict.Anchor.NavData = null;
        Check(!DockedCoursePlayback.DeferMember(first.objSS, 0), "Zero-time lookup was deferred.");

        var failing = Make("FAIL-TUG", 10000); var failingTow = Make("FAIL-TOW", 10000);
        var failingGroup = Join(failing, failingTow);
        throwCourse = Course(failing);
        var originalExternal = new Vector2(1e-12f, 2e-12f);
        failing.objSS.vAccEx = originalExternal;
        bool threw = false;
        try { Step(failing, failingTow, failingGroup, 1); }
        catch (InvalidOperationException error) when (error.Message == "fixture playback failure") { threw = true; }
        finally { throwCourse = null; }
        Check(threw, "Native playback exception was swallowed.");
        Check(failing.objSS.vAccEx == originalExternal, "External acceleration was not restored after an exception.");
        Check(AccessTools.Field(typeof(DockedCoursePlayback), "executing").GetValue(null) == null, "Execution scope retained a ship after failure.");
        Check(AccessTools.Field(typeof(DockedCoursePlayback), "integrated").GetValue(null) == null, "Group scope retained a docking group after failure.");
        Check(DockedCoursePlayback.DeferMember(failing.objSS, 1), "Exception cleanup broke subsequent deferral.");
        Console.WriteLine("Patched native playback: normal/SFF steps, reverse membership, rigid offsets, fuel, completion, reconstruction, mass, negative controls and exception cleanup passed.");
    }
}
