using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using SolarSystem;
using UnityEngine;

static partial class Program
{
    static double ForceMagnitude(Vector2 force) => Math.Sqrt((double)force.x * force.x + (double)force.y * force.y);

    // Compare real native SetThrust + current mass repair + native tow loading.
    // Earlier release tests assigned acceleration directly and skipped wear.
    static void CollateralChecks(Harmony engine)
    {
        var production = new Harmony("offline.docking-orbit.collateral-production");
        var plugin = typeof(Ostrabus.OstrabusPlugin).Assembly;
        var wear = AccessTools.Method(typeof(Ship), nameof(Ship.AccrueTowGroupLoad));
        engine.Unpatch(wear, HarmonyPatchType.Prefix, engine.Id);
        try
        {
            production.CreateClassProcessor(plugin.GetType("Ostrabus.ShipNavigation.Towing.TorchTowMassPatch", true)).Patch();
            foreach (bool enabled in new[] { false, true })
            {
                if (enabled)
                    foreach (string type in new[] { "PassiveDockAttachmentPatch", "PassiveAttachmentInputPatch", "HaulAttachmentReleasePatch" })
                        production.CreateClassProcessor(plugin.GetType("Ostrabus.ShipNavigation." + type, true)).Patch();

                foreach (bool reverse in new[] { false, true })
                    foreach (bool driveOnLockedMember in new[] { false, true })
                        foreach (bool braced in new[] { false, true })
                        {
                            var (visitor, wreck) = Pair(reverse);
                            var group = visitor.DockGroup;
                            Check(group.Anchor.bBOLocked == enabled, "Control fixture did not distinguish native and preserved attachment.");
                            var drive = driveOnLockedMember ? wreck : visitor;
                            if (braced)
                                Field(visitor, "aSecuredTowBraces", new Dictionary<string, string> { { "brace", "VISITOR-port" } });

                            drive.SetThrust(0);
                            drive.StopManeuver(false, false);
                            Check(group.Anchor.bBOLocked == enabled, "Zero torch/RCS commands changed passive attachment.");

                            // Each fixture hull weighs 1000 kg; force remains
                            // divided by both hulls, even before release.
                            drive.SetThrust(1000);
                            double acceleration = ForceMagnitude(group.Anchor.vAccIn);
                            double expected = .5 / 149597872d / 1000;
                            Check(Math.Abs(acceleration / expected - 1) < 1e-6, "Attachment changed physical group thrust/mass calculation.");
                            Check(!group.Anchor.bBOLocked && !wreck.objSS.bBOLocked && !visitor.objSS.bBOLocked,
                                "Actual SetThrust did not release every soft attachment.");
                            Check(ForceMagnitude(drive.objSS.vAccIn) == acceleration, "Actual torch input was lost during release.");

                            double vx = group.Anchor.vVelX, vy = group.Anchor.vVelY;
                            var applied = group.Anchor.vAccIn;
                            group.TimeAdvance(12);
                            Check(Math.Abs(group.Anchor.vVelX - vx - applied.x * 12) < 1e-14 &&
                                  Math.Abs(group.Anchor.vVelY - vy - applied.y * 12) < 1e-14,
                                "Actual released torch acceleration was applied incorrectly.");
                            foreach (var ship in new[] { visitor, wreck })
                            {
                                Check(ship.objSS.vVelX == group.Anchor.vVelX && ship.objSS.vVelY == group.Anchor.vVelY,
                                    "Payload stopped following the released group.");
                                double accumulated = (float)AccessTools.Field(typeof(Ship), "fTowWearManeuver").GetValue(ship);
                                double expectedLoad = braced ? 0 : acceleration / 6.684587E-12 / 9.81 * 12;
                                Check(Math.Abs(accumulated - expectedLoad) < 1e-5,
                                    "Attachment bypassed native unbraced tow wear or charged a braced hull.");
                            }
                            drive.SetThrust(0);
                            Check(group.Anchor.vAccIn == Vector2.zero, "Native torch shutdown left residual group thrust.");
                            var saved = new ShipSitu(wreck.objSS.GetJSON());
                            Check(!saved.bBOLocked && saved.fOrbitW == 0, "Post-release saved situation retained an old attachment.");
                            group.UpdateAnchor();
                            Check(!group.Anchor.bBOLocked, "Rebuilding the released anchor restored a stale member attachment.");
                        }
            }
            // Existing towing preview protection recognizes native station
            // identity independently of bIsBO/bGrounded. Release must agree.
            foreach (string permanent in new[] { "substation", "ground-classification", "skyscraper" })
            {
                var (visitor, wreck) = Pair();
                if (permanent == "substation") Field(wreck, "_subStation", true);
                else wreck.Classification = permanent == "skyscraper" ? Ship.TypeClassification.Skyscraper : Ship.TypeClassification.GroundStation;
                Check(wreck.IsSubStation() || wreck.IsGroundStation(), "Permanent native identity fixture was invalid.");
                visitor.objSS.vAccRCS = new Vector2(1e-10f, 0);
                visitor.UpdateDockedShips(visitor.objSS);
                Check(wreck.objSS.bBOLocked && visitor.DockGroup.Anchor.bBOLocked,
                    "Applied input released a native permanent station identity: " + permanent);
                var course = Course(visitor);
                course.IsTorching = true;
                var release = AccessTools.Method(plugin.GetType("Ostrabus.ShipNavigation.Towing.DockedCoursePlayback", true), "ReleaseAttachmentForCourse");
                release.Invoke(null, new object[] { visitor.DockGroup });
                Check(wreck.objSS.bBOLocked && visitor.DockGroup.Anchor.bBOLocked && ReferenceEquals(visitor.objSS.NavData, course),
                    "Course release/brace abort changed a permanently fixed group: " + permanent);
            }
            Console.WriteLine("Independent collateral checks: actual torch control, physical mass, native unbraced wear, braced load, shutdown and post-release save state pass.");
        }
        finally
        {
            production.UnpatchSelf();
            engine.Patch(wear, prefix: new HarmonyMethod(typeof(Program), nameof(Skip)));
        }
    }

    static void CompletionDependencyChecks()
    {
        var production = new Harmony("offline.docking-orbit.completion-dependency");
        var plugin = typeof(Ostrabus.OstrabusPlugin).Assembly;
        try
        {
            foreach (string name in new[] {
                "Ostrabus.ShipNavigation.PassiveDockAttachmentPatch",
                "Ostrabus.ShipNavigation.PassiveAttachmentInputPatch",
                "Ostrabus.ShipNavigation.Towing.DockedCourseMemberPatch",
                "Ostrabus.ShipNavigation.Towing.DockedCourseGroupPatch" })
                production.CreateClassProcessor(plugin.GetType(name, true)).Patch();
            foreach (bool completionRepair in new[] { false, true })
            {
                if (completionRepair)
                    production.CreateClassProcessor(plugin.GetType("Ostrabus.ShipNavigation.RouteCompletionPatch", true)).Patch();
                var (visitor, wreck) = Pair();
                var course = Course(visitor);
                StarSystem.fEpoch = 1300;
                visitor.objSS.TimeAdvance(300);
                visitor.DockGroup.TimeAdvance(300);
                double expected = course.Destination.ObjSS.vPosx + course.Destination.ObjSS.vVelX * 200;
                double error = Math.Abs(visitor.objSS.vPosx - expected);
                Check(!wreck.objSS.bBOLocked && visitor.objSS.NavData == null, "Dependency test did not complete the released course.");
                Check(completionRepair ? error < 1e-12 : error > 1e-9,
                    "Shared completion dependency A/B did not distinguish proper remainder coasting.");
                if (completionRepair)
                    Check(visitor.DockGroup.Anchor.vAccRCS == Vector2.zero && visitor.DockGroup.Anchor.vAccIn == Vector2.zero,
                        "Shared completion dependency retained residual propulsion.");
                Console.WriteLine("Tow completion dependency: repair=" + completionRepair + "; endpoint/coast error=" + error * 149597872000d + " m.");
            }
        }
        finally { production.UnpatchSelf(); }
    }
}
