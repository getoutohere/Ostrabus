using System;
using System.Runtime.Serialization;
using HarmonyLib;
using Ostrabus.QoL.CoursePlot;

static class Program {
    static int checks;
    static double mass;
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = ReferenceEquals(__0, __1); return false; }
    static bool Unequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = !ReferenceEquals(__0, __1); return false; }
    static bool NoReactor(ref CondOwner __result) { __result = null; return false; }
    static bool Mass(ref double __result) { __result = mass; return false; }
    static void Check(bool ok, string why) { checks++; if (!ok) throw new Exception(why); }
    static void Main() {
        var fixture = new Harmony("offline.course.fine.fixture");
        fixture.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Equality"), prefix: new HarmonyMethod(typeof(Program), nameof(Equal)));
        fixture.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Inequality"), prefix: new HarmonyMethod(typeof(Program), nameof(Unequal)));
        fixture.Patch(AccessTools.PropertyGetter(typeof(Ship), "Reactor"), prefix: new HarmonyMethod(typeof(Program), nameof(NoReactor)));
        fixture.Patch(AccessTools.PropertyGetter(typeof(Ship), "Mass"), prefix: new HarmonyMethod(typeof(Program), nameof(Mass)));
        var production = new Harmony("offline.course.fine.production");
        try {
            // Actual shipped Ship.GetMaxTorchThrust; only mass and absent scene
            // reactor discovery are fixture adapters. No thrust-law replacement.
            foreach (double kg in new[] { 16000d, 32000, 100000 }) foreach (int pairs in new[] { 1, 2, 3, 4 }) {
                mass = kg;
                var ship = Empty<Ship>(); ship.fFusionPelletMax = pairs * 2;
                ship.fFusionThrustMax = 332499980926.5137 / 70500000 * ship.fFusionPelletMax * 393.06358381502895;
                float cycle = 0;
                for (int i = 1; i <= 250; i++) {
                    float next = CourseFineControls.NextCycle(ship, cycle, 0, 1, 1);
                    double actual = CourseFineControls.G(ship, next), cap = CourseFineControls.G(ship, 1);
                    Check(next >= cycle && next <= 1, "Increasing wheel violated slider bounds");
                    Check(Math.Abs(actual - Math.Min(i * .01, cap)) < 2e-6, "Wheel G increment drifted from 0.01 G grid");
                    Check(actual <= i * .01 + 1e-9, "Wheel setting overshot requested G ceiling");
                    cycle = next;
                }
                for (int i = 249; i >= 0; i--) {
                    cycle = CourseFineControls.NextCycle(ship, cycle, 0, 1, -1);
                    Check(Math.Abs(CourseFineControls.G(ship, cycle) - i * .01) < 2e-6, "Reverse G increments drifted");
                }
                float limited = CourseFineControls.NextCycle(ship, .2f, .1f, .2f, 1);
                Check(limited == .2f, "Wheel exceeded native safety maximum");
                Check(CourseFineControls.NextCycle(ship, .1f, .1f, .2f, -1) == .1f, "Wheel went below native minimum");
                ship.fFusionThrustMax = 0;
                Check(CourseFineControls.NextCycle(ship, .3f, 0, 1, 1) == .3f, "Zero thrust invented acceleration setting");
            }
            float drift = 0;
            for (int i = 1; i <= 100; i++) {
                drift = CourseFineControls.NextDrift(drift, 0, 1, 1);
                Check(Math.Abs(drift - i / 100d) < 1e-7, "Drift increments accumulated error");
            }
            for (int i = 99; i >= 0; i--) {
                drift = CourseFineControls.NextDrift(drift, 0, 1, -1);
                Check(Math.Abs(drift - i / 100d) < 1e-7, "Reverse drift increments accumulated error");
            }
            Check(CourseFineControls.NextDrift(1, 0, 1, 1) == 1 && CourseFineControls.NextDrift(0, 0, 1, -1) == 0, "Drift endpoint clamping failed");
            Check(CourseFineControls.NextDrift(.333f, 0, 1, 1) == .34f, "Off-grid drift did not snap in wheel direction");
            Check(CourseFineControls.NextDrift(.333f, 0, 1, -1) == .32f, "Off-grid reverse drift incorrect");
            Check(CourseFineControls.NextDrift(.5f, 0, 1, 0) == .5f, "Horizontal/zero wheel changed drift");
            Check(CourseFineControls.NextCycle(null, .3f, 0, 1, 1) == .3f, "Missing ship changed acceleration");
            Check(!CourseSliderScroll.OwnsHover(null), "No hovered slider suppressed unrelated scrolling");
            production.CreateClassProcessor(typeof(CourseFineInitPatch)).Patch();
            production.CreateClassProcessor(typeof(CourseFineReadoutPatch)).Patch();
            production.CreateClassProcessor(typeof(CourseFineLogScrollPatch)).Patch();
            Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(GUIOrbitDraw), "ScrollLog")).Prefixes.Count > 0, "Native log-scroll hook did not bind");
            System.Console.WriteLine(checks + " native thrust/increment/bounds/binding checks passed. Unity pointer routing and layout require a live scene.");
        } finally { production.UnpatchSelf(); fixture.UnpatchSelf(); }
    }
}
