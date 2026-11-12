using System;
using HarmonyLib;
using Ostrabus.MiscBugFixes;
using Ostrabus.TorchCalculator;
using Ostranauts.ShipGUIs.Utilities;

static partial class Program {
    static double handoffFlow, handoffCycle;
    static bool handoffObserved;
    static void ObserveAppliedControls() {
        handoffFlow = double.Parse(Controls["slidFlow"],System.Globalization.CultureInfo.InvariantCulture);
        handoffCycle = double.Parse(Controls["slidCycle"],System.Globalization.CultureInfo.InvariantCulture);
        handoffObserved = true;
    }
    static void RunControlHandoffChecks() {
        int before = checks;
        var probe = new Harmony("offline.torch.handoff.probe");
        probe.Patch(AccessTools.Method(typeof(FusionIC),"Fusion"),
            prefix:new HarmonyMethod(typeof(Program),nameof(ObserveAppliedControls)) { priority = Priority.Last });
        try {
            foreach (double pilot in new[] { 1d,.75 }) {
                var preview = SetupCourse(false,pilot);
                ship.objSS.NavData = null;
                Controls["knobRatio"] = "0";
                string flow = Controls["slidFlow"], cycle = Controls["slidCycle"];
                double waterBefore = Read(d2o[0],"StatLiqD2O"), heliumBefore = Read(he3[0],"StatSolidHe3");
                AccessTools.Method(typeof(TorchCoursePlanningPatch),"Postfix").Invoke(null,new object[] { preview });
                Check(TorchCourseFuel.Find(preview,false) != null,"Power-mode preview receives corrected fuel accounting");
                Check(Controls["knobRatio"] == "0" && Controls["slidFlow"] == flow && Controls["slidCycle"] == cycle,
                    "Preview leaves physical reactor mode and controls unchanged");
                Check(Read(d2o[0],"StatLiqD2O") == waterBefore && Read(he3[0],"StatSolidHe3") == heliumBefore,
                    "Preview does not debit or refill tanks");
                Controls["knobRatio"] = "1";
                var engaged = MakeStudyCourse(.2,14400);
                AccessTools.Method(typeof(TorchCoursePlanningPatch),"Postfix").Invoke(null,new object[] { engaged });
                Near(preview.Destination.TorchFuelLevel,engaged.Destination.TorchFuelLevel,"Power/torch mode previews predict identical fuel",1e-10);
                Near(preview.fFlowMultPlot,engaged.fFlowMultPlot,"Preview and engagement agree on balanced FLOW",1e-10);
                ship.objSS.NavData = preview;
                Controls["knobRatio"] = "0";
                Check(!AdvanceCourse(preview,500) && !ship.objSS.HasNavData(),"Preview eligibility does not permit flight in power mode");
            }
            foreach (bool fastForward in new[] { false,true }) {
                var nav = SetupCourse();
                var active = AccessTools.Field(typeof(GUIFFWD),"bFFWDActive");
                active.SetValue(null,fastForward);
                try {
                    // Reproduce the live stale read before CatchUp: native holds
                    // this old value while Run computes/applies the new FLOW.
                    Controls["slidFlow"] = ".0820409506559372";
                    foreach (double epoch in new[] { 101d,500,2500,nav.Destination.ArrivalTime-10 }) {
                        handoffObserved = false;
                        Check(AdvanceCourse(nav,epoch),"Native flight continues across burn/coast/braking");
                        Check(handoffObserved,"Native reactor Fusion executed during CatchUp");
                        Near(double.Parse(Controls["slidFlow"]),handoffFlow,"Displayed FLOW remains the applied reactor FLOW",1e-10);
                        Near(double.Parse(Controls["slidCycle"]),handoffCycle,"Displayed CYCLE remains the applied reactor CYCLE",1e-10);
                    }
                    AdvanceCourse(nav,nav.Destination.ArrivalTime+3600);
                    Check(Controls["slidFlow"] == "0.0" || double.Parse(Controls["slidFlow"]) == 0,"Native arrival FLOW reset remains effective");
                    Check(double.Parse(Controls["slidCycle"]) == 0 && !ship.objSS.HasNavData(),"Native arrival CYCLE/course reset remains effective");
                } finally { active.SetValue(null,false); }
            }
            foreach (string unsupported in new[] { "cold", "unknownMode" }) {
                var preview = SetupCourse(false);
                ship.objSS.NavData = null;
                Controls["knobRatio"] = unsupported == "cold" ? "0" : "invalid";
                if (unsupported == "cold") Values[core]["StatICCoreTemp"] = TorchModel.Ideal*.9;
                double originalFuel = preview.Destination.TorchFuelLevel;
                AccessTools.Method(typeof(TorchCoursePlanningPatch),"Postfix").Invoke(null,new object[] { preview });
                Check(TorchCourseFuel.Find(preview,false) == null && preview.Destination.TorchFuelLevel == originalFuel,
                    "Unsupported preview retains native policy: " + unsupported);
            }
            var savedPreview = SetupCourse(false);
            ship.objSS.NavData = null; Controls["knobRatio"] = "0";
            AccessTools.Method(typeof(TorchCoursePlanningPatch),"Postfix").Invoke(null,new object[] { savedPreview });
            var restored = new NavData(LitJson.JsonMapper.ToObject<JsonNavData>(LitJson.JsonMapper.ToJson(savedPreview.GetJSON())));
            ship.objSS.NavData = restored;
            Check(TorchCourseFuel.Find(restored) != null,"Power-mode reconstruction recognizes its own corrected budget");
            Check(!AdvanceCourse(restored,500) && !ship.objSS.HasNavData(),"Restored corrected course cannot execute in power mode");
            var legacy = SetupCourse(false);
            Controls["slidFlow"] = ".0820409506559372";
            Check(AdvanceCourse(legacy,101),"Uncorrected native course still advances");
            var pair = legacy.GetCurrentNavPoints(fixtureEpoch);
            double fraction = (fixtureEpoch-pair.Item1.ArrivalTime)/(pair.Item2.ArrivalTime-pair.Item1.ArrivalTime);
            double expectedCycle = pair.Item1.TorchCycle+(pair.Item2.TorchCycle-pair.Item1.TorchCycle)*fraction;
            Near(double.Parse(Controls["slidCycle"]),expectedCycle,"Legacy native CYCLE interpolation remains authoritative",1e-6);
            Check(TorchCourseFuel.Find(legacy,false) == null,"Legacy playback did not acquire corrected controls");
            ship.objSS.ResetNavData();
            Controls["slidFlow"] = ".123"; Controls["slidCycle"] = ".07";
            TorchCourseFuel.Control(playbackReactor,.27);
            Check(Controls["slidFlow"] == ".123" && Controls["slidCycle"] == ".07","Manual operation retains its own controls");
            Console.WriteLine("Preview/control handoff: " + (checks-before) + " checks passed.");
        } finally { probe.UnpatchSelf(); }
    }
}
