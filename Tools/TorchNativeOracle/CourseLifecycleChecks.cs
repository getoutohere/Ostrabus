using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Ostrabus.MiscBugFixes;
using Ostrabus.TorchCalculator;
using Ostranauts.ShipGUIs.Utilities;

static partial class Program {
    static NavData SetupCourse(bool attach = true, double pilot = 1, int pairs = 1) {
        fixtureEpoch = 100; StarSystem.fEpoch = fixtureEpoch;
        playbackReactor = null; candidateControls = false; componentThrows = false;
        var fusion = Make(new ReactorFit { Feeders = pairs, Lasers = pairs, Capacitors = 2, Regulators = 2 }, TorchModel.Ideal, .2, .5);
        CrewSim.coPlayer = Co(); ship.strRegID = "TEST-TORCH";
        Field(ship, "nLoadState", Ship.Loaded.Full); Field(ship, "fFuelEfficiencyMod", pilot);
        ship.aNavs = new List<CondOwner>(); Values[core]["IsTorchDrive"] = 1;
        Controls["fFlowEpochResume"] = "0"; Controls["fCrsLimMax"] = "1";
        AccessTools.Method(typeof(FusionIC), "Run").Invoke(fusion, new object[] { TorchModel.TickSeconds });
        AccessTools.Method(typeof(FusionIC), "Fusion").Invoke(fusion, new object[] { 0d, new JsonPowerInfo(), 0d });
        Values[core]["StatICCoreTemp"] = TorchModel.Ideal;
        double usage = fusion.MassUsageMax;
        Values[d2o[0]]["StatVolume"] = 1000; Values[he3[0]]["StatVolume"] = 1000;
        Values[d2o[0]]["StatLiqD2O"] = usage * TorchModel.D2ORatio * 100000;
        Values[he3[0]]["StatSolidHe3"] = usage * 100000;
        fusion.SetReactants(100000); Field(fusion, "fTimeNextNWZ", double.MaxValue);
        var nav = MakeStudyCourse(.2, 14400);
        // Exercise the production planning-hook entry, not a duplicate rebudgeter.
        if (attach) AccessTools.Method(typeof(TorchCoursePlanningPatch), "Postfix").Invoke(null, new object[] { nav });
        ship.objSS.NavData = nav;
        Field(nav, "fEpochLast", fixtureEpoch);
        playbackReactor = fusion; playbackShutdown = false;
        CrewSim.system = Empty<StarSystem>();
        CrewSim.system.dictShips = new Dictionary<string, Ship> { [ship.strRegID] = ship };
        return nav;
    }
    static bool AdvanceCourse(NavData nav, double epoch) {
        fixtureEpoch = epoch; StarSystem.fEpoch = epoch;
        return nav.TimeAdvance(ship.objSS);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference DiscardedProfile() {
        var nav = SetupCourse();
        Check(TorchCourseFuel.Find(nav, false) != null, "Preview profile attached");
        var weak = new WeakReference(nav);
        ship.objSS.ResetNavData();
        return weak;
    }
    static void RunCourseLifecycleChecks() {
        int before = checks;
        foreach (double pilot in new[] { 1d, .75 }) {
            var nav = SetupCourse(true, pilot);
            var original = TorchCourseFuel.Find(nav, false);
            Check(original != null, "Production preview attached");
            Check(AdvanceCourse(nav, 2500), "Initial production playback");
            Check(TorchCourseFuel.Playing == null, "Playback context cleared after success");
            string json = LitJson.JsonMapper.ToJson(nav.GetJSON());
            var data = LitJson.JsonMapper.ToObject<JsonNavData>(json);
            var restored = new NavData(data);
            ship.objSS.NavData = restored;
            var restoredProfile = TorchCourseFuel.Find(restored);
            Check(restoredProfile != null, "Native JSON reconstruction recognizes corrected ordinary waypoint data");
            Near(TorchCourseFuel.Budget(restoredProfile, 8500), TorchCourseFuel.Budget(original, 8500), "Restored cumulative budget unchanged", 1e-10);
            Check(AdvanceCourse(restored, 8500), "Reloaded coast playback");
            Check(AdvanceCourse(restored, restored.Destination.ArrivalTime), "Reloaded braking playback");
            Near(Read(he3[0], "StatSolidHe3"), TorchCourseFuel.Budget(original, restored.Destination.ArrivalTime) * original.Usage,
                "Reloaded fuel inventory matches original corrected course", 1e-8);
            Check(TorchCourseFuel.Playing == null, "No retained playback context after reload");
        }
        var legacy = SetupCourse(false);
        double legacyFuel = legacy.Destination.TorchFuelLevel;
        Check(TorchCourseFuel.Find(legacy) == null, "Existing vanilla course is not reinterpreted as discounted");
        Near(legacy.Destination.TorchFuelLevel, legacyFuel, "Legacy budget unchanged");
        foreach (bool attach in new[] { false, true }) {
            SetupCourse(false);
            var noCoast = MakeStudyCourse(.399, 0);
            if (attach) Check(TorchCourseFuel.Attach(noCoast, false), "New no-coast course can be corrected");
            var restored = new NavData(LitJson.JsonMapper.ToObject<JsonNavData>(LitJson.JsonMapper.ToJson(noCoast.GetJSON())));
            ship.objSS.NavData = restored;
            Check((TorchCourseFuel.Find(restored) != null) == attach, "Saved FLOW and budget distinguish corrected and near-max native no-coast courses");
        }
        foreach (bool sff in new[] { false, true }) {
            var nav = SetupCourse();
            double expectedFuel = nav.Destination.TorchFuelLevel;
            var active = AccessTools.Field(typeof(GUIFFWD), "bFFWDActive");
            active.SetValue(null, sff);
            try {
                AdvanceCourse(nav, nav.Destination.ArrivalTime + 3600);
                Check(!ship.objSS.HasNavData(), "Crossing arrival clears native course");
                Check(double.Parse(Controls["slidCycle"]) == 0 && double.Parse(Controls["slidFlow"]) == 0, "Crossing arrival leaves reactor idle");
                Near(ship.fShallowFusionRemain, expectedFuel, "Arrival overrun does not charge another hour of torch burn", 1e-9);
                Check(TorchCourseFuel.Playing == null, "Arrival overrun context unwound");
            } finally { active.SetValue(null, false); }
        }
        var old = SetupCourse();
        var replacement = MakeStudyCourse(.1, 14400);
        ship.objSS.NavData = replacement;
        string oldFlow = Controls["slidFlow"];
        TorchCourseFuel.Control(playbackReactor, .27);
        Check(Controls["slidFlow"] == oldFlow, "Old profile does not control replacement native course");
        ship.objSS.ResetNavData();
        TorchCourseFuel.Control(playbackReactor, .27);
        Check(Controls["slidFlow"] == oldFlow, "Manual operation remains outside course control");
        foreach (string mutation in new[] { "tankRemoved", "tankAdded", "volumeChanged", "reactorModulesChanged", "coolingLost", "pilotChanged", "reactantLost", "refueled", "reactorModeChanged", "exhaustChanged" }) {
            var nav = SetupCourse();
            double inventoryBefore = Read(he3[0], "StatSolidHe3");
            if (mutation == "tankRemoved") d2o.Clear();
            else if (mutation == "tankAdded") { var tank = Co(); Values[tank]["StatVolume"] = 1000; he3.Add(tank); }
            else if (mutation == "volumeChanged") Values[he3[0]]["StatVolume"] = 2000;
            else if (mutation == "coolingLost") Values[core]["StatICCryoMult"] = 0;
            else if (mutation == "pilotChanged") Field(ship, "fFuelEfficiencyMod", .75d);
            else if (mutation == "reactantLost") Values[he3[0]]["StatSolidHe3"] = 0;
            else if (mutation == "refueled") Values[he3[0]]["StatSolidHe3"] += 100;
            else if (mutation == "reactorModeChanged") Controls["knobRatio"] = "0";
            else if (mutation == "exhaustChanged") Values[core]["StatICVe"] *= .9;
            else {
                // The changed capacity is discovered inside native CatchUp, after
                // TimeAdvance has already entered its reactor branch.
                Field(playbackReactor, "aPellets", Modules(2));
                Field(playbackReactor, "aLasers", Modules(2));
            }
            Check(!AdvanceCourse(nav, 500), "Changed configuration cancels playback: " + mutation);
            Check(!ship.objSS.HasNavData(), "Changed configuration clears course: " + mutation);
            Check(double.Parse(Controls["slidFlow"]) == 0 && double.Parse(Controls["slidCycle"]) == 0, "Changed configuration leaves controls idle: " + mutation);
            Check(Read(he3[0], "StatSolidHe3") <= inventoryBefore + (mutation == "refueled" ? 100 : 0) + 1e-8, "Changed configuration does not manufacture fuel: " + mutation);
            Check(TorchCourseFuel.Playing == null, "Invalidation context cleanup: " + mutation);
        }
        var failing = SetupCourse(); componentThrows = true;
        bool threw = false;
        try { AdvanceCourse(failing, 500); } catch (InvalidOperationException) { threw = true; }
        finally { componentThrows = false; }
        Check(threw && TorchCourseFuel.Playing == null, "Native exception unwinds playback context");
        var weak = DiscardedProfile();
        for (int i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
        Check(!weak.IsAlive, "Weak profile registry does not retain discarded courses");
        playbackReactor = null; CrewSim.coPlayer = null;
        Console.WriteLine("Course lifecycle: " + (checks-before) + " checks passed.");
    }
}
