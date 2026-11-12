using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Newtonsoft.Json;
using Ostrabus.MiscBugFixes;
using Ostrabus.TorchCalculator;
using Ostranauts.ShipGUIs.Utilities;

static partial class Program {
    static bool clockStudyWatching;
    static double clockStudyWear;
    static void ClockStudyWear(CondOwner co, double fDeltaTime) {
        if (clockStudyWatching && ReferenceEquals(co, core)) clockStudyWear += fDeltaTime;
    }

    // The old fixture advanced every CO clock with the global ephemeris. SFF
    // advances these independently; keep the thermal/wear interval distinct
    // from fuel debit while exercising native playback and native Run.
    static void RunSffClockStudy() {
        int beforeChecks = checks;
        var observations = new List<object>();
        var observer = new Harmony("offline.torch.clock-wear-observer");
        observer.Patch(AccessTools.Method(typeof(FusionIC), "WearModule"),
            prefix: new HarmonyMethod(typeof(Program), nameof(ClockStudyWear)));
        try {
            foreach (int pairs in new[] { 1, 2, 4 }) foreach (double pilot in new[] { 1d, .75 })
                foreach (double seconds in new[] { 600d, 21600d }) foreach (bool lagged in new[] { false, true }) {
                SetupCourse(pilot: pilot, pairs: pairs);
                var nav = MakeStudyCourse(.2, 14400, 28800);
                Check(TorchCourseFuel.Attach(nav, false), "Powered clock-study course did not attach");
                ship.objSS.NavData = nav;
                var fusion = playbackReactor;
                double begin = fixtureEpoch, beforeHe = Read(he3[0], "StatSolidHe3"), beforeD = Read(d2o[0], "StatLiqD2O");
                var profile = TorchCourseFuel.Find(nav, false);
                double predicted = (TorchCourseFuel.Budget(profile, begin)-TorchCourseFuel.Budget(profile, begin+seconds))*profile.Usage;
                Field(fusion, "fTimeLastRun", begin); Field(fusion, "fTimeNextRun", begin+.27);
                StarSystem.fEpoch = begin+seconds;
                if (!lagged) fixtureEpoch = StarSystem.fEpoch;
                clockStudyWear = 0; clockStudyWatching = true;
                Check(nav.TimeAdvance(ship.objSS), "Clock study course did not advance");
                double afterPlayback = Read(he3[0], "StatSolidHe3");
                fixtureEpoch = StarSystem.fEpoch;
                fusion.CatchUp();
                clockStudyWatching = false;
                double actual = beforeHe-Read(he3[0], "StatSolidHe3");
                observations.Add(new { pairs, pilot, lagged, seconds, predictedHe3Kg = predicted,
                    chargedAtPlayback = beforeHe-afterPlayback, totalHe3Kg = actual, ratio = actual/predicted,
                    profileInvalid = profile.Invalid, coreWearSeconds = clockStudyWear });
                Near(beforeHe-afterPlayback, predicted, "Playback reconciles scheduled fuel", 1e-6);
                Near(actual, predicted, "Independent clock charged a settled interval twice", 1e-6);
                Near(beforeD-Read(d2o[0], "StatLiqD2O"), predicted*TorchModel.D2ORatio, "Independent clock D2O accounting", 1e-6);
                Near(clockStudyWear, seconds, "Fuel correction lost native core wear time", 1e-10);
                Near(Read(core, "StatICCoreTemp"), TorchModel.Ideal, "Fuel correction changed native thermal control", 1e-6);
                Check(!profile.Invalid, "Clock accounting unexpectedly aborted the course");
            }
            RunSplitClockChecks();
            RunClockSettlementIsolation();
            RunClockSettlementBounds();
            var weak = DiscardSettledReactor();
            SetupCourse();
            for (int i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
            Check(!weak.IsAlive, "Pending settlement retained a discarded reactor");
            System.IO.File.WriteAllText("obj/torch-sff-clock-fixed.json", JsonConvert.SerializeObject(observations, Formatting.Indented));
            Console.WriteLine("Independent-clock native regression: " + (checks-beforeChecks) + " checks passed.");
        } finally { clockStudyWatching = false; observer.UnpatchSelf(); }
    }

    static void RunSplitClockChecks() {
        foreach (bool restored in new[] { false, true }) {
            var nav = SetupCourse(pairs: 2);
            var fusion = playbackReactor;
            var profile = TorchCourseFuel.Find(nav, false);
            double begin = fixtureEpoch, beforeHe = Read(he3[0], "StatSolidHe3");
            double finalBudget = nav.Destination.TorchFuelLevel;
            var active = AccessTools.Field(typeof(GUIFFWD), "bFFWDActive"); active.SetValue(null, true);
            try {
                // Powered -> coast -> braking -> arrival -> post-arrival idle.
                foreach (double seconds in new[] { 600d, 1200, 9000, 15700, 16500, 16800, 18000 }) {
                    double oldClock = fixtureEpoch;
                    StarSystem.fEpoch = begin+seconds;
                    Check(nav.TimeAdvance(ship.objSS), "Lagged mixed-phase course playback failed");
                    double settledHe = Read(he3[0], "StatSolidHe3");
                    // Catch up partway first, then finish the pending interval.
                    fixtureEpoch = (oldClock+StarSystem.fEpoch)*.5;
                    fusion.CatchUp();
                    if (seconds <= 16800) Near(Read(he3[0], "StatSolidHe3"), settledHe, "Partial catch-up recharged settled fuel", 1e-10);
                    fixtureEpoch = StarSystem.fEpoch;
                    fusion.CatchUp();
                    Check(!profile.Invalid, "Mixed-phase catch-up invalidated the course");
                    if (seconds <= 16800) Near(Read(he3[0], "StatSolidHe3"), settledHe, "Late catch-up recharged settled fuel", 1e-10);
                    if (restored && seconds == 9000) {
                        nav = new NavData(LitJson.JsonMapper.ToObject<JsonNavData>(LitJson.JsonMapper.ToJson(nav.GetJSON())));
                        ship.objSS.NavData = nav;
                        profile = TorchCourseFuel.Find(nav);
                        Check(profile != null, "Corrected course did not restore after completed clock catch-up");
                    }
                }
                Check(!ship.objSS.HasNavData(), "Overrun did not detach native course");
                double idleUse = TorchModel.ManualHe3Rate(TorchModel.Ideal, TorchModel.Lerp((float)profile.Capacity, .001f, 1))*1200;
                Near(beforeHe-Read(he3[0], "StatSolidHe3"),
                    (nav.Origin.TorchFuelLevel-finalBudget)*profile.Usage+idleUse, "Arrival waived real post-course idle fuel", 1e-6);
            } finally { active.SetValue(null, false); }
        }
    }

    static void RunClockSettlementIsolation() {
        var nav = SetupCourse();
        var fusion = playbackReactor;
        StarSystem.fEpoch = fixtureEpoch+600;
        Check(nav.TimeAdvance(ship.objSS), "Isolation course did not settle fuel");
        fixtureEpoch = StarSystem.fEpoch;
        fusion.CatchUp();
        ship.objSS.ResetNavData();
        Controls["slidCycle"] = ".1"; Controls["slidFlow"] = ".25";
        double beforeHe = Read(he3[0], "StatSolidHe3");
        fixtureEpoch += 60; StarSystem.fEpoch = fixtureEpoch;
        fusion.CatchUp();
        double manualRate = TorchModel.ManualHe3Rate(Read(core, "StatICCoreTemp"), Read(core, "StatICPellRate"));
        Near(beforeHe-Read(he3[0], "StatSolidHe3"), manualRate*60, "Completed settlement discounted later manual operation", 1e-6);
        var other = Make(new ReactorFit(), TorchModel.Ideal, .1, .25);
        Near(TorchCourseFuelConsumption.ChargeSeconds(123, other), 123, "Settlement leaked into another reactor");
        playbackReactor = null;
    }

    static void RunClockSettlementBounds() {
        var nav = SetupCourse();
        var fusion = playbackReactor;
        var profile = TorchCourseFuel.Find(nav, false);
        double begin = fixtureEpoch, beforeHe = Read(he3[0], "StatSolidHe3");
        // This reactor still owes time from before plotting. A course settlement
        // cannot waive that earlier fuel, even when one Run spans both periods.
        Field(fusion, "fTimeLastRun", begin-60); Field(fusion, "fTimeNextRun", begin+.27);
        StarSystem.fEpoch = begin+600;
        Check(nav.TimeAdvance(ship.objSS), "Pre-course clock case did not settle");
        double expected = (TorchCourseFuel.Budget(profile, begin)-TorchCourseFuel.Budget(profile, StarSystem.fEpoch))*profile.Usage;
        fixtureEpoch = StarSystem.fEpoch;
        fusion.CatchUp();
        double rate = TorchModel.ManualHe3Rate(Read(core, "StatICCoreTemp"), Read(core, "StatICPellRate"));
        Near(beforeHe-Read(he3[0], "StatSolidHe3"), expected+60*rate, "Settlement waived pre-course fuel", 1e-6);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference DiscardSettledReactor() {
        var nav = SetupCourse();
        StarSystem.fEpoch = fixtureEpoch+600;
        Check(nav.TimeAdvance(ship.objSS), "Collectibility case did not settle");
        return new WeakReference(playbackReactor);
    }
}
