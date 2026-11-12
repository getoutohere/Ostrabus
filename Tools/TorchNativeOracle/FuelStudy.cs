using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using HarmonyLib;
using Ostrabus.TorchCalculator;
using Ostranauts.ShipGUIs.Utilities;

static partial class Program {
    static void RunFuelStudy() {
        var rows = new List<string> { "pairs,cycle,mode,flow,temperatureIdealFraction,minimumTemperatureIdealFraction,firstThermalAbortSeconds,he3KgHour,vanillaUnskilledKgHour,equilibriumCandidateKgHour,normalStepModelRelativeError" };
        var run = AccessTools.Method(typeof(FusionIC), "Run");
        var burn = AccessTools.Method(typeof(FusionIC), "Fusion");
        foreach (int pairs in new[] { 1, 2, 4 }) foreach (double fraction in new[] { 0d, .25, .5, .95 }) {
            var fit = new ReactorFit { Feeders = pairs, Lasers = pairs, Capacitors = 2, Regulators = 2 };
            double p = TorchModel.PelletMax(fit), cycle = Math.Min(1, .2 * p) * fraction;
            double dt = TorchModel.TickSeconds;
            double desiredRate = Math.Max(TorchModel.Lerp((float)p, .001f, 1), 5 * cycle);
            double minimum = TorchModel.Lerp((float)p, .001f, (float)Math.Min((TorchModel.Ideal - cycle * .2 * dt) / TorchModel.Ideal, 1));
            double balancedFlow = Math.Max(0, Math.Min(1, (desiredRate - minimum) / (p - minimum)));
            double candidate = TorchModel.ManualHe3Rate(TorchModel.Ideal, desiredRate) * 3600;
            foreach (string mode in new[] { "balanced", "nativeFeedback", "heldSffFallback" }) {
                var fusion = Make(fit, TorchModel.Ideal, cycle, balancedFlow);
                // Warm-up initializes native module counts and theoretical capacity.
                run.Invoke(fusion, new object[] { dt });
                double flow = mode == "balanced" ? balancedFlow : NavData.GetFLOWforCYCLE(core, cycle);
                double modelTemp = Read(core, "StatICCoreTemp"), measured = 0, predicted = 0;
                double minTemp = 10, elapsed = 0;
                double? firstAbort = null;
                for (int i = 0; i < 6000; i++) {
                    if (mode == "nativeFeedback") {
                        double target = ship.GetMaxTorchThrust((float)cycle);
                        double actual = target * Read(core, "StatICCoreTemp") / TorchModel.Ideal;
                        flow = NavData.GetAdjustedFLOW(core, flow, actual, target);
                    }
                    Controls["slidFlow"] = flow.ToString(CultureInfo.InvariantCulture);
                    var step = TorchModel.Step(modelTemp, cycle, flow, p, (double).6f, dt);
                    modelTemp = step.Temperature;
                    run.Invoke(fusion, new object[] { dt });
                    burn.Invoke(fusion, new object[] { dt, new JsonPowerInfo(), 0d });
                    double fractionNow = Read(core, "StatICCoreTemp") / TorchModel.Ideal;
                    minTemp = Math.Min(minTemp, fractionNow);
                    if (cycle > 0 && firstAbort == null && (fractionNow < .8 || fractionNow > 1.2)) firstAbort = (i + 1) * dt;
                    // Last 810 seconds measure settled operation; the first half is warm-up.
                    if (i >= 3000) {
                        measured += Read(core, "StatICHe3Rate") * dt;
                        predicted += TorchModel.ManualHe3Rate(modelTemp, step.PelletRate) * dt;
                        elapsed += dt;
                    }
                }
                double rate = measured / elapsed * 3600;
                double error = Math.Abs(predicted - measured) / Math.Max(1e-9, measured);
                Check(error < 3e-5, "Tick model agrees with native Run/Fusion: " + pairs + "/" + cycle + "/" + mode);
                if (mode == "balanced") Near(rate, candidate, "Equilibrium candidate agrees with native balanced burn", 3e-5);
                double vanilla = fusion.MassUsageMax * 3600;
                rows.Add(string.Join(",", new[] { pairs.ToString(), cycle.ToString("R", CultureInfo.InvariantCulture), mode,
                    flow.ToString("R", CultureInfo.InvariantCulture), (Read(core, "StatICCoreTemp") / TorchModel.Ideal).ToString("R", CultureInfo.InvariantCulture),
                    minTemp.ToString("R", CultureInfo.InvariantCulture), firstAbort?.ToString("R", CultureInfo.InvariantCulture) ?? "", rate.ToString("R", CultureInfo.InvariantCulture),
                    vanilla.ToString("R", CultureInfo.InvariantCulture), candidate.ToString("R", CultureInfo.InvariantCulture), error.ToString("R", CultureInfo.InvariantCulture) }));
            }
        }
        Directory.CreateDirectory("obj");
        File.WriteAllLines("obj/torch-fuel-study.csv", rows);
        var jumps = new List<string> { "pairs,cycle,seconds,flow,mode,he3ConsumedKg,finalTemperatureIdealFraction" };
        foreach (int pairs in new[] { 1, 2, 4 }) foreach (double fraction in new[] { .5, .95 }) foreach (double seconds in new[] { 600d, 21600d }) {
            var fit = new ReactorFit { Feeders = pairs, Lasers = pairs, Capacitors = 2, Regulators = 2 };
            double p = TorchModel.PelletMax(fit), cycle = Math.Min(1, .2 * p) * fraction;
            foreach (bool coarse in new[] { false, true }) {
                var fusion = Make(fit, TorchModel.Ideal, cycle, 0);
                run.Invoke(fusion, new object[] { TorchModel.TickSeconds });
                double flow = NavData.GetFLOWforCYCLE(core, cycle);
                Controls["slidFlow"] = flow.ToString(CultureInfo.InvariantCulture);
                double total = 0, elapsed = 0;
                double modelTemp = Read(core, "StatICCoreTemp"), predicted = 0;
                while (elapsed < seconds) {
                    double dt = coarse ? seconds : Math.Min(TorchModel.TickSeconds, seconds - elapsed);
                    var step = TorchModel.Step(modelTemp, cycle, flow, p, (double).6f, dt);
                    modelTemp = step.Temperature;
                    run.Invoke(fusion, new object[] { dt });
                    burn.Invoke(fusion, new object[] { dt, new JsonPowerInfo(), 0d });
                    total += Read(core, "StatICHe3Rate") * dt;
                    predicted += TorchModel.ManualHe3Rate(modelTemp, step.PelletRate) * dt;
                    elapsed += dt;
                }
                Near(total, predicted, "Coarse/fine native thermal and fuel parity", 3e-5);
                jumps.Add(string.Join(",", new[] { pairs.ToString(), cycle.ToString("R", CultureInfo.InvariantCulture), seconds.ToString(CultureInfo.InvariantCulture),
                    flow.ToString("R", CultureInfo.InvariantCulture), coarse ? "oneNativeCatchup" : "normalTicksHeldFlow",
                    total.ToString("R", CultureInfo.InvariantCulture), (Read(core, "StatICCoreTemp") / TorchModel.Ideal).ToString("R", CultureInfo.InvariantCulture) }));
            }
        }
        File.WriteAllLines("obj/torch-fuel-step-study.csv", jumps);
        Console.WriteLine("Fuel study: " + (rows.Count - 1) + " native reactor scenarios; results in obj/torch-fuel-study.csv");
        Console.WriteLine("Step study: " + (jumps.Count - 1) + " native catch-up comparisons; results in obj/torch-fuel-step-study.csv");
    }
}
