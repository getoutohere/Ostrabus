using System;
using Ostrabus.AlternativeWork;
using Ostrabus.Diagnostics;
using Ostrabus.Hotkeys;
using Ostrabus.Orbital;

namespace Ostrabus.PdaSettings;

internal sealed class Setting {
    internal readonly string Id, Title, Description, Lifetime;
    internal readonly int Min, Max;
    readonly Func<int> read;
    readonly Action<int> write;
    internal bool IsToggle => Min == 0 && Max == 1;
    internal int Value => read();
    internal Setting(string id, string title, string description, string lifetime, Func<int> read, Action<int> write, int min = 0, int max = 1) {
        Id = id; Title = title; Description = description; Lifetime = lifetime;
        this.read = read; this.write = write; Min = min; Max = max;
    }
    internal void Set(int value) {
        if (value < Min || value > Max) throw new ArgumentOutOfRangeException(nameof(value));
        if (value != Value) write(value);
    }
}

internal static class SettingsRegistry {
    internal const string AppId = "template_settings";
    internal static readonly Setting[] All = {
        new Setting("hide_nav_note", "Hide nav tutorial note", "Remove the Don't crash! paper from nav consoles.", "Saved", () => NavConsole.TutorialNote.Hidden ? 1 : 0, value => NavConsole.TutorialNote.SetHidden(value != 0)),
        new Setting("em_propagation", "EM propagation", "Extend radio reception with inverse-square signal falloff.", "Saved", () => EMPropagation.EmPropagation.Enabled ? 1 : 0, value => EMPropagation.EmPropagation.SetEnabled(value != 0)),
        new Setting("sensors", "Contact position estimates", "Replace distant contact jitter with sensor-based uncertainty.", "Saved", () => Sensors.SensorContacts.Enabled ? 1 : 0, value => Sensors.SensorContacts.SetEnabled(value != 0)),
        new Setting("altwork", "Alternative work", "Choose nearby work first.", "Saved", () => AlternativeWorkSystem.Enabled ? 1 : 0, value => AlternativeWorkSystem.SetEnabled(value != 0)),
        new Setting("supply_window", "Supply window", "Nearby jobs to supply ahead (1-30).", "Saved", () => WorkSupplyPlan.Window, WorkSupplyPlan.SetWindow, 1, WorkSupplyPlan.MaxWindow),
        new Setting("autovents", "Kang auto-vents", "Tolerate brief O2 dips; seal quickly in emergencies.", "Saved", () => QoL.AutoVents.AutoVentControl.Enabled ? 1 : 0, value => QoL.AutoVents.AutoVentControl.SetEnabled(value != 0)),
        new Setting("hotkeys", "Crew hotkeys", "Shift+R: airlock. Shift+T: disembark.", "Saved", () => FunctionalHotkeys.Flag?.Value == true ? 1 : 0, value => {
            if (FunctionalHotkeys.Flag == null) throw new InvalidOperationException("Crew hotkeys are not initialized.");
            FunctionalHotkeys.Flag.Value = value != 0;
        }),
        new Setting("orbital", "Orbital correction", "Correct gravity around moving bodies.", "This session", () => OrbitalSimulation.Enabled ? 1 : 0, value => OrbitalSimulation.SetEnabled(value != 0)),
        new Setting("coast", "Orbital coasting", "Reduce orbit drift at high time acceleration. Requires Orbital.", "This session", () => CoastIntegration.Enabled ? 1 : 0, value => CoastIntegration.SetEnabled(value != 0)),
        new Setting("sff", "Super fast forward", "Preserve orbits and limit AI travel during SFF. Requires Orbital.", "This session", () => SffSimulation.Enabled ? 1 : 0, value => SffSimulation.Enabled = value != 0),
        new Setting("worktrace", "Work diagnostics", "Trace work selection and execution.", "This session", () => WorkTrace.Enabled ? 1 : 0, value => WorkTrace.SetEnabled(value != 0))
    };
}
