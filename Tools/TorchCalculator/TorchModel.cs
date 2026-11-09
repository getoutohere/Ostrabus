using System;
using System.Collections.Generic;
using System.Linq;

namespace Ostrabus.TorchCalculator;

public sealed class ReactorFit {
    public int Feeders { get; set; } = 1;
    public int Lasers { get; set; } = 1;
    public int Capacitors { get; set; } = 1;
    public int Regulators { get; set; } = 1;
    public int Cryopumps { get; set; } = 1;
    public double FeederHealth { get; set; } = 1;
    public double LaserHealth { get; set; } = 1;
    public double CryoHealth { get; set; } = 1;
    public double ExhaustVelocity { get; set; } = 70500000;
    public double MassKg { get; set; } = 32000;
    public double DesiredG { get; set; } = 3;
    public double BurnHours { get; set; } = 12;
    public double He3Kg { get; set; } = 2000;
    public double D2OKg { get; set; } = 2000;
    public bool SkilledPilot { get; set; }
    public bool Safety { get; set; }
    public double Reserve { get; set; } = .05;
    public void Validate() {
        void Range(double value, double min, double max, string name) {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < min || value > max) throw new ArgumentException(name + " must be between " + min + " and " + max + ".");
        }
        Range(Feeders, 0, 4, nameof(Feeders)); Range(Lasers, 0, 4, nameof(Lasers));
        Range(Capacitors, 0, 4, nameof(Capacitors)); Range(Regulators, 0, 4, nameof(Regulators)); Range(Cryopumps, 0, 4, nameof(Cryopumps));
        Range(FeederHealth, 0, 1, nameof(FeederHealth)); Range(LaserHealth, 0, 1, nameof(LaserHealth)); Range(CryoHealth, 0, 1, nameof(CryoHealth));
        Range(ExhaustVelocity, 1, 1e9, nameof(ExhaustVelocity)); Range(MassKg, 1, 1e10, nameof(MassKg));
        Range(DesiredG, .001, 1000, nameof(DesiredG)); Range(BurnHours, 0, 10000, nameof(BurnHours));
        Range(He3Kg, 0, 1e10, nameof(He3Kg)); Range(D2OKg, 0, 1e10, nameof(D2OKg)); Range(Reserve, 0, .5, nameof(Reserve));
    }
}

public static class TorchModel {
    public const double Ideal = .7250000238418579;
    public const double AuAccelerationPerMps2 = 6.6845869117759804e-12;
    public const double G = 9.81;
    public const double TickSeconds = .27000001072883606;
    public const double D2ORatio = (double).667f;
    public const double He3TankKg = 5216;
    public const double D2OTankKg = 44722.8;
    public const double TankTareKg = 1500;
    public static float Lerp(float a, float b, float t) => a + (b - a) * Math.Max(0f, Math.Min(1f, t));
    // FusionIC.Run sums condition-weighted modules in single precision before applying supply caps.
    public static double PelletMax(ReactorFit f) {
        float feeders = 0, lasers = 0;
        for (int i = 0; i < f.Feeders; i++) feeders += 1f - (float)(1 - f.FeederHealth);
        for (int i = 0; i < f.Lasers; i++) lasers += 1f - (float)(1 - f.LaserHealth);
        return Math.Min(Math.Min(feeders, f.Regulators * 2f), Math.Min(lasers, f.Capacitors * 2f)) * 2f;
    }
    public static double RatedThrust(double pellets, double ve = 70500000) => 332499980926.5137 * (ve / 70500000) / 70500000 * pellets * 393.06358381502895;
    public static float AccelerationAu(double mass, double pellets, double ratedThrust, double cycle) {
        if (pellets <= 0) return 0;
        float c = (float)cycle, p = (float)pellets;
        return (float)((double)(Lerp(1f, p, c) / p * c) * ratedThrust / mass * AuAccelerationPerMps2);
    }
    public static double AccelerationG(ReactorFit f, double pellets, double cycle) => AccelerationAu(f.MassKg, pellets, RatedThrust(pellets, f.ExhaustVelocity), cycle) / AuAccelerationPerMps2 / G;
    public static double MassUsageMax(double ratedThrust) => 2 * ratedThrust * 70500000 / .949999988079071 / 70500000 / 70500000;
    public static double ManualHe3Rate(double temperature, double pelletRate, double ve = 70500000) {
        double power = temperature > Ideal ? .3499999940395355 : .3499999940395355 * temperature / Ideal;
        return 2 * (power * 1e12) * (ve / 70500000) / 70500000 / 70500000 * 393.06358381502895 * pelletRate;
    }
    // Thermal order is deliberate: cycle removes heat before pellet-feed feedback reads it.
    public static (double Temperature, double PelletRate) Step(double temperature, double cycle, double flow, double pellets, double cryo, double seconds) {
        double dt = Math.Min(seconds, 1);
        temperature -= cycle * .2 * dt;
        float minimum = Lerp((float)pellets, .001f, (float)Math.Min(temperature / Ideal, 1));
        double rate = pellets <= 0 ? 0 : Lerp(minimum, (float)pellets, (float)flow);
        temperature += rate * .04 * dt;
        if (cryo > 0 && temperature > Ideal) temperature = Math.Max(Ideal, temperature - .08 * dt * cryo);
        return (Math.Max(0, temperature), rate);
    }
    public static double RequiredCycle(ReactorFit f, double pellets) {
        if (pellets <= 0) return 0;
        // Stable positive quadratic root, then refine against the native float thrust expression.
        double lo = 0, hi = 1;
        for (int i = 0; i < 55; i++) {
            double mid = (lo + hi) / 2;
            if (AccelerationG(f, pellets, mid) < f.DesiredG) lo = mid; else hi = mid;
        }
        return (lo + hi) / 2;
    }
    public static double SafetyCycle(ReactorFit f, double pellets) {
        float cycle = 1;
        if (f.Safety) while (cycle > 0 && AccelerationG(f, pellets, cycle) > 2) cycle = Math.Max(0, cycle - .01f);
        return cycle;
    }
    public static object Evaluate(ReactorFit f) {
        f.Validate();
        double p = PelletMax(f), rated = RatedThrust(p, f.ExhaustVelocity);
        double efficiency = f.SkilledPilot ? .75 : 1;
        double cycle = RequiredCycle(f, p), maxCycle = Math.Min(1, p * .2), safety = SafetyCycle(f, p);
        double limit = Math.Min(maxCycle, safety), recommendedCycle = Math.Min(maxCycle * (1 - f.Reserve), safety);
        double thermalMaxG = AccelerationG(f, p, maxCycle), maxG = AccelerationG(f, p, limit), recommendedG = AccelerationG(f, p, recommendedCycle);
        bool thermallyFeasible = p > 0 && f.DesiredG <= thermalMaxG + 1e-6;
        bool attainable = p > 0 && f.DesiredG <= maxG + 1e-6;
        double he3 = MassUsageMax(rated) * efficiency * 3600, d2o = he3 * D2ORatio;
        double equilibriumRate = cycle * 5;
        float minimum = Lerp((float)p, .001f, (float)Math.Min((Ideal - cycle * .2 * TickSeconds) / Ideal, 1));
        double flow = p > minimum ? Math.Max(0, Math.Min(1, (equilibriumRate - minimum) / (p - minimum))) : 1;
        double he3Need = he3 * f.BurnHours, d2oNeed = d2o * f.BurnHours;
        double extraHe3 = Math.Max(0, he3Need - f.He3Kg), extraD2O = Math.Max(0, d2oNeed - f.D2OKg);
        double newHe3Tanks = Math.Ceiling(extraHe3 / He3TankKg), newD2OTanks = Math.Ceiling(extraD2O / D2OTankKg);
        // Native He-3/D2O quantities are separate from StatMass. Only tank hardware adds ship mass.
        double extraMass = TankTareKg * (newHe3Tanks + newD2OTanks);
        double endurance = he3 > 0 ? Math.Min(f.He3Kg / he3, f.D2OKg / d2o) : 0;
        var warnings = new List<string>();
        if (p <= 0) warnings.Add("No usable feeder/laser capacity. Fusion cannot be sustained with this fit.");
        if (!attainable && p > 0) warnings.Add("Requested acceleration exceeds the sustainable limit for this mass and fit.");
        if (f.Safety && f.DesiredG > maxG) warnings.Add("The nav torch-safety switch caps acceleration at approximately 2 G in 1% cycle steps.");
        if (extraMass > 0) warnings.Add("Available fuel does not cover the entered powered-burn duration.");
        if (f.Cryopumps == 0 || f.CryoHealth == 0) warnings.Add("No effective cryogenic cooling: this ideal-state limit offers no protection against overheating transients.");
        if (p < Math.Min(f.Feeders, f.Lasers) * 2 - 1e-5) warnings.Add("Wear, capacitors or fuel regulators limit effective pellet throughput.");
        var trace = new List<object>(); double temp = Ideal, elapsed = 0; double? lowAt = null;
        float cryo = 0;
        for (int i = 0; i < f.Cryopumps; i++) cryo += (1f - (float)(1 - f.CryoHealth)) * .6f;
        double traceFlow = thermallyFeasible ? flow : 1;
        while (elapsed <= 600) {
            if (trace.Count == 0 || elapsed >= trace.Count * 5) trace.Add(new { seconds = elapsed, temperature = temp, idealFraction = temp / Ideal });
            var step = Step(temp, cycle, traceFlow, p, cryo, TickSeconds);
            temp = step.Temperature; elapsed += TickSeconds;
            if (lowAt == null && temp < Ideal * .8) lowAt = elapsed;
        }
        var curve = Enumerable.Range(0, 101).Select(i => new { cycle = i / 100d, g = AccelerationG(f, p, i / 100d), sustainable = i / 100d <= limit + 1e-8 }).ToArray();
        return new { pellets = p, ratedThrustN = rated, maximumCycle = limit, maximumG = maxG, recommendedG, recommendedCycle,
            maxMassAtDesiredG = f.Safety && f.DesiredG > 2 ? (double?)null : thermalMaxG * f.MassKg / f.DesiredG,
            requestedCycle = cycle, attainable, thermallyFeasible, balancedFlow = thermallyFeasible ? (double?)flow : null,
            he3KgHour = he3, d2oKgHour = d2o, manualHe3KgHour = thermallyFeasible ? (double?)(ManualHe3Rate(Ideal, equilibriumRate, f.ExhaustVelocity) * 3600) : null,
            manualD2OKgHour = thermallyFeasible ? (double?)(ManualHe3Rate(Ideal, equilibriumRate, f.ExhaustVelocity) * D2ORatio * 3600) : null,
            enduranceHours = endurance, he3Need, d2oNeed, extraHe3, extraD2O, newHe3Tanks, newD2OTanks, extraMass,
            massAfterAddingFuelAndNewTanks = f.MassKg + extraMass, lowTemperatureSeconds = lowAt, warnings, trace, curve };
    }
}
