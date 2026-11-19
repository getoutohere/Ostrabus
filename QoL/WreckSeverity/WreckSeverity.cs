using System;
using BepInEx.Configuration;

namespace Ostrabus.QoL.WreckSeverity;

internal static class WreckSeverity {
    internal const float DefaultReduction = 0.25f;
    internal static ConfigEntry<float> Reduction;
    internal static ConfigEntry<bool> LogDamageRolls;

    internal static void Initialize(ConfigFile config) {
        Reduction = config.Bind("QoL.WreckSeverity", "SeverityReduction", DefaultReduction,
            "Soften initial derelict damage with a smooth severity curve. Default 0.25 maps severity 1 to 0.75 and 0.4 to 0.3. Zero preserves vanilla; use a finite value below 1. Existing damaged wrecks are not repaired. Invalid values disable the adjustment.");
        LogDamageRolls = config.Bind("QoL.WreckSeverity", "LogDamageRolls", false,
            "Print initial wreck severity before/after the curve and native bonus, hole/cull/wear passes, and actual randomized incoming damage budgets per ray to the player console. Diagnostic only; false installs no ray-roll observer. Restart to apply.");
        WreckDamageLog.Enabled = LogDamageRolls.Value;
        Reduction.SettingChanged += Validate;
        Validate(null, EventArgs.Empty);
    }

    private static void Validate(object sender, EventArgs args) {
        if (!ValidReduction(Reduction.Value))
            OstrabusPlugin.Logger?.LogWarning("Wreck severity adjustment is inactive: SeverityReduction must be finite, at least 0 and less than 1.");
    }

    private static bool ValidReduction(float value) =>
        !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0 && value < 1;

    internal static float Adjust(float severity, float reduction) {
        if (!ValidReduction(reduction) || reduction == 0 ||
            float.IsNaN(severity) || float.IsInfinity(severity) || severity <= 0 || severity > 1)
            return severity;

        // Use double intermediates near r = 1; the native stored value is a float.
        double s = severity, r = reduction, remaining = 1 - r;
        double result = s * remaining / (remaining + r * (3.5 * s - 2.5 * s * s));
        return Math.Min(severity, (float)result);
    }

    internal static bool Eligible(Ship ship, bool prefill) =>
        ship != null && prefill && ship.DMGStatus == Ship.Damage.Derelict &&
        ship.Classification != Ship.TypeClassification.Asteroid;

    internal static void ApplyInitial(Ship ship, bool prefill) {
        if (!Eligible(ship, prefill)) return;
        ship.fBreakInMultiplier = Adjust(ship.fBreakInMultiplier, Reduction?.Value ?? 0);
    }
}
