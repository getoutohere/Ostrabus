using System;
using Ostrabus.AlternativeWork;
using Ostrabus.Diagnostics;
using Ostrabus.Hotkeys;
using Ostrabus.Orbital;

namespace Ostrabus.PdaSettings;

internal sealed class Setting {
    internal readonly string Id, Title, Description, Lifetime, Suffix;
    internal readonly int Min, Max;
    readonly Func<int> read;
    readonly Action<int> write;
    readonly Func<string> readBinding;
    readonly Action<string> writeBinding;
    internal bool IsKeyBinding => readBinding != null;
    internal string BindingValue => readBinding();
    internal void SetBinding(string value) => writeBinding(value);
    internal bool IsToggle => !IsKeyBinding && Min == 0 && Max == 1;
    internal bool IsPercentage => Suffix == "%";
    internal int Value => read();
    internal Setting(string id, string title, string description, string lifetime, Func<int> read, Action<int> write, int min = 0, int max = 1, string suffix = "") {
        Id = id; Title = title; Description = description; Lifetime = lifetime;
        this.read = read; this.write = write; Min = min; Max = max; Suffix = suffix;
    }
    internal Setting(string id, string title, string description, Func<string> read, Action<string> write) {
        Id = id; Title = title; Description = description; Lifetime = "Saved"; Suffix = "";
        readBinding = read; writeBinding = write;
    }
    internal string Format(int value) => IsToggle ? value == 0 ? "OFF" : "ON" : value.ToString() + Suffix;
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
        new Setting("venus_wreck_percent", "Venus-specific wrecks", "Share of new VORB wrecks using Venus designs; the rest use the general pool.", "Saved", () => Venus.VenusDerelicts.WreckPercent, Venus.VenusDerelicts.SetWreckPercent, 0, 100, "%"),
        new Setting("autovents", "Kang auto-vents", "Tolerate brief O2 dips; seal quickly in emergencies.", "Saved", () => QoL.AutoVents.AutoVentControl.Enabled ? 1 : 0, value => QoL.AutoVents.AutoVentControl.SetEnabled(value != 0)),
        new Setting("hotkeys", "Crew hotkeys", "Shift+R: airlock. Shift+T: disembark.", "Saved", () => FunctionalHotkeys.Flag?.Value == true ? 1 : 0, value => {
            if (FunctionalHotkeys.Flag == null) throw new InvalidOperationException("Crew hotkeys are not initialized.");
            FunctionalHotkeys.Flag.Value = value != 0;
        }),
        new Setting("equipment_shortcut", "Helmet and EVA battery shortcut", "Equip or stow carried gear. Click the shortcut to rebind; Esc cancels.", () => EquipmentShortcut.Value, EquipmentShortcut.Set),
        new Setting("station_docked_collisions", "Station-docked hull exemption", "Parked visiting hulls do not obstruct traffic or trigger proximity alarms.", "Saved", () => ShipNavigation.StationDockedCollisions.Enabled ? 1 : 0, value => ShipNavigation.StationDockedCollisions.SetEnabled(value != 0)),
        new Setting("hull_collisions", "Hull-shaped collisions", "Match ship impacts to hull outlines; weapon accuracy stays unchanged.", "Saved", () => ShipNavigation.Collisions.PhysicalCollisions.Enabled ? 1 : 0, value => ShipNavigation.Collisions.PhysicalCollisions.SetEnabled(value != 0)),
        new Setting("orbital", "Orbital correction", "Correct gravity around moving bodies.", "This session", () => OrbitalSimulation.Enabled ? 1 : 0, value => OrbitalSimulation.SetEnabled(value != 0)),
        new Setting("coast", "Orbital coasting", "Reduce orbit drift at high time acceleration. Requires Orbital.", "This session", () => CoastIntegration.Enabled ? 1 : 0, value => CoastIntegration.SetEnabled(value != 0)),
        new Setting("sff", "Super fast forward", "Preserve orbits and limit AI travel during SFF. Requires Orbital.", "This session", () => SffSimulation.Enabled ? 1 : 0, value => SffSimulation.Enabled = value != 0),
        new Setting("worktrace", "Work diagnostics", "Trace work selection and execution.", "This session", () => WorkTrace.Enabled ? 1 : 0, value => WorkTrace.SetEnabled(value != 0)),
        new Setting("torchtrace", "Torch burn diagnostics", "Record reactor controls, heat, thrust and fuel during flight.", "This session", () => TorchTrace.Enabled ? 1 : 0, value => TorchTrace.SetEnabled(value != 0))
    };
}
