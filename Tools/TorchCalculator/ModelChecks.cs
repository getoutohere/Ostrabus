using System.Text.Json;

namespace Ostrabus.TorchCalculator;

static class ModelChecks {
    internal static void Run() {
        int checks = 0;
        void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
        JsonElement Result(ReactorFit fit) => JsonSerializer.SerializeToElement(TorchModel.Evaluate(fit));
        double Number(JsonElement row, string key) => row.GetProperty(key).GetDouble();
        var fit = new ReactorFit(); var one = Result(fit);
        Check(Number(one, "maximumG") > 3.3 && Number(one, "maximumG") < 3.31, "32 tonne / one pair anchor changed");
        Check(one.GetProperty("attainable").GetBoolean(), "32 tonne / 3 G must be feasible");
        Check(Number(one, "extraMass") == (Number(one, "newHe3Tanks") + Number(one, "newD2OTanks")) * TorchModel.TankTareKg, "Fuel contents incorrectly add ship mass");
        var fullTanks = Result(new ReactorFit { He3Kg = TorchModel.He3TankKg, D2OKg = TorchModel.D2OTankKg });
        Check(Number(fullTanks, "extraMass") == 0 && Number(fullTanks, "maximumG") == Number(one, "maximumG"), "Full tank inventory must not change ship mass or acceleration");
        Check(fullTanks.GetProperty("warnings").GetArrayLength() == 0, "Fuel quantities larger than ship mass are legitimate native behavior");
        fit.Feeders = fit.Lasers = 2; var two = Result(fit);
        Check(Math.Abs(Number(two, "he3KgHour") / Number(one, "he3KgHour") - 2) < 1e-10, "Second pair must double plotted fuel at the same mass/G");
        Check(Math.Abs(Number(two, "d2oKgHour") / Number(one, "d2oKgHour") - 2) < 1e-10, "D2O must scale with He-3");
        Check(Number(two, "maximumG") > Number(one, "maximumG"), "More pairs must raise ceiling");
        fit.SkilledPilot = true; var skilled = Result(fit);
        Check(Math.Abs(Number(skilled, "he3KgHour") / Number(two, "he3KgHour") - .75) < 1e-10, "Pilot saving changed");
        fit.Safety = true; var safe = Result(fit);
        Check(Number(safe, "maximumG") <= 2.00001 && !safe.GetProperty("attainable").GetBoolean(), "Native safety ceiling failed");
        Check(safe.GetProperty("maxMassAtDesiredG").ValueKind == JsonValueKind.Null && safe.GetProperty("thermallyFeasible").GetBoolean(), "Safety must not invent a mass upgrade or thermal failure");
        fit = new ReactorFit { Feeders = 4, Lasers = 4 };
        Check(TorchModel.PelletMax(fit) == 4, "Single capacitor/regulator must cap four installed pairs");
        fit.Capacitors = fit.Regulators = 2;
        Check(TorchModel.PelletMax(fit) == 8, "Supporting modules did not unlock four pairs");
        fit.FeederHealth = .5;
        Check(TorchModel.PelletMax(fit) == 4, "Wear not applied");
        fit = new ReactorFit { MassKg = 40000 };
        var overloaded = Result(fit);
        Check(!overloaded.GetProperty("attainable").GetBoolean() && overloaded.GetProperty("lowTemperatureSeconds").ValueKind == JsonValueKind.Number, "Overloaded fit must cool below autopilot threshold");
        foreach (int pairs in new[] { 1, 2, 3, 4 }) {
            fit = new ReactorFit { Feeders = pairs, Lasers = pairs, Capacitors = 2, Regulators = 2 };
            double p = TorchModel.PelletMax(fit), cycle = Math.Min(1, p * .2);
            double rate = 5 * cycle;
            float minimum = TorchModel.Lerp((float)p, .001f, (float)((TorchModel.Ideal - cycle * .2 * TorchModel.TickSeconds) / TorchModel.Ideal));
            double flow = Math.Clamp((rate - minimum) / (p - minimum), 0, 1);
            double temp = TorchModel.Ideal;
            for (int i = 0; i < 2000; i++) temp = TorchModel.Step(temp, cycle, flow, p, .6, TorchModel.TickSeconds).Temperature;
            Check(Math.Abs(temp - TorchModel.Ideal) < 1e-5, "Thermal equilibrium drift at pair count " + pairs);
        }
        fit = new ReactorFit { Feeders = 0 };
        var empty = Result(fit);
        Check(Number(empty, "maximumG") == 0 && Number(empty, "he3KgHour") == 0 && !empty.GetProperty("attainable").GetBoolean(), "No-pair fit not rejected");
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, 0d, -1d }) {
            bool rejected = false;
            try { TorchModel.Evaluate(new ReactorFit { MassKg = invalid }); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "Invalid mass accepted");
        }
        Console.WriteLine(checks + " calculator model checks passed.");
        Console.WriteLine("32,000 kg, one pair: " + Number(one, "maximumG").ToString("F4") + " G; " + Number(one, "he3KgHour").ToString("F3") + " kg He-3/h; " + Number(one, "d2oKgHour").ToString("F3") + " kg D2O/h (unskilled plotted burn).");
    }
}
