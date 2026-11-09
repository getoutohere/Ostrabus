using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using BepInEx.Configuration;
using HarmonyLib;
using LitJson;

static class Program {
    static int checks;
    static readonly Assembly Plugin = typeof(Ostrabus.OstrabusPlugin).Assembly;
    static Type Type(string name) => Plugin.GetType("Ostrabus." + name, true);
    static object Field(object obj, string name) => AccessTools.Field(obj.GetType(), name).GetValue(obj);
    static object Get(object obj) => AccessTools.Property(obj.GetType(), "Value").GetValue(obj);
    static void Set(object obj, int value) => AccessTools.Method(obj.GetType(), "Set").Invoke(obj, new object[] { value });
    static void Check(bool ok, string reason) { checks++; if (!ok) throw new Exception(reason); }
    static ConfigEntry<T> Config<T>(T value) {
        var config = (ConfigEntry<T>)FormatterServices.GetUninitializedObject(typeof(ConfigEntry<T>));
        AccessTools.Field(config.GetType(), "_typedValue").SetValue(config, value);
        return config;
    }
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = ReferenceEquals(__0, __1); return false; }
    static bool Unequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = !ReferenceEquals(__0, __1); return false; }
    static bool Quiet() => false;
    // Replace only config disk/event plumbing, not the production module setters.
    static bool WriteBool(ConfigEntry<bool> __instance, bool value) { AccessTools.Field(__instance.GetType(), "_typedValue").SetValue(__instance, value); return false; }
    static bool WriteInt(ConfigEntry<int> __instance, int value) { AccessTools.Field(__instance.GetType(), "_typedValue").SetValue(__instance, value); return false; }
    static int Main(string[] args) {
        string assets = Path.GetFullPath("PdaSettings/Assets");
        var app = JsonMapper.ToObject<JsonPDAAppIcon[]>(File.ReadAllText(assets + "/data/pda_apps/template_settings.json"));
        Check(app.Length == 1 && app[0].strName == "template_settings" && !app[0].bHidden, "Native app JSON invalid");
        Check(File.Exists(assets + "/images/" + app[0].strIcon + ".png"), "Packaged icon does not resolve");
        var info = JsonMapper.ToObject<JsonModInfo[]>(File.ReadAllText(assets + "/mod_info.json"));
        Check(info.Length == 1 && info[0].strModVersion == Ostrabus.PluginInfo.PLUGIN_VERSION, "Native metadata does not match plugin version");
        var strings = JsonMapper.ToObject<JsonSimple[]>(File.ReadAllText(assets + "/data/strings/template_settings.json"));
        Check(strings.Length == 1 && strings[0].aValues.Length == 4 && strings[0].aValues[0] == "GUI_PDA_BUTTON_TEMPLATE_SETTINGS_TITLE", "Tooltip strings invalid");

        var harmony = new Harmony("offline.pdasettings");
        harmony.Patch(AccessTools.PropertySetter(typeof(ConfigEntry<bool>), "Value"), prefix: new HarmonyMethod(typeof(Program), nameof(WriteBool)));
        harmony.Patch(AccessTools.PropertySetter(typeof(ConfigEntry<int>), "Value"), prefix: new HarmonyMethod(typeof(Program), nameof(WriteInt)));
        harmony.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Equality"), prefix: new HarmonyMethod(typeof(Program), nameof(Equal)));
        harmony.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Inequality"), prefix: new HarmonyMethod(typeof(Program), nameof(Unequal)));
        foreach (var name in new[] { "AlternativeWork.AlternativeWorkSystem", "Orbital.OrbitalSimulation" }) harmony.Patch(AccessTools.Method(Type(name), "Log"), prefix: new HarmonyMethod(typeof(Program), nameof(Quiet)));
        var flag = Config(false);
        AccessTools.Field(Type("AlternativeWork.AlternativeWorkSystem"), "Flag").SetValue(null, flag);
        var window = Config(8);
        AccessTools.Field(Type("AlternativeWork.WorkSupplyPlan"), "WindowConfig").SetValue(null, window);
        var hotkeys = Config(false);
        AccessTools.Field(Type("Hotkeys.FunctionalHotkeys"), "Flag").SetValue(null, hotkeys);
        var autovents = Config(true);
        AccessTools.Field(Type("QoL.AutoVents.AutoVentControl"), "Flag").SetValue(null, autovents);
        var emPropagation = Config(false);
        AccessTools.Field(Type("EMPropagation.EmPropagation"), "Flag").SetValue(null, emPropagation);
        var sensorPositions = Config(false);
        AccessTools.Field(Type("Sensors.SensorContacts"), "Flag").SetValue(null, sensorPositions);
        var wreckPercent = Config(50);
        AccessTools.Field(Type("Venus.VenusDerelicts"), "WreckPercentConfig").SetValue(null, wreckPercent);
        var all = (Array)AccessTools.Field(Type("PdaSettings.SettingsRegistry"), "All").GetValue(null);
        var settings = new Dictionary<string, object>();
        foreach (var setting in all) Check(settings.TryAdd((string)Field(setting, "Id"), setting), "Duplicate setting ID");
        Check(settings.Count == 12, "Settings missing");
        var wreckSetting = settings["venus_wreck_percent"];
        Check((int)Get(wreckSetting) == 50 && (string)Field(wreckSetting, "Lifetime") == "Saved", "Wreck default/lifetime incorrect");
        Check((bool)AccessTools.Property(wreckSetting.GetType(), "IsPercentage").GetValue(wreckSetting), "Wreck option must use percentage control");
        Check((string)AccessTools.Method(wreckSetting.GetType(), "Format").Invoke(wreckSetting, new object[] { 50 }) == "50%", "Percentage label missing unit");
        var chooseLoot = AccessTools.Method(Type("Venus.VenusDerelicts"), "ChooseLoot");
        foreach (int percent in new[] { 0, 1, 25, 50, 70, 99, 100 }) {
            Set(wreckSetting, percent);
            Check(wreckPercent.Value == percent && (int)Get(wreckSetting) == percent, "Wreck setting/config not applied");
            int selected = 0;
            for (int i = 0; i < 100; i++) if ((string)chooseLoot.Invoke(null, new object[] { "VORB", true, i / 100d }) == "RandomDerelictVenus") selected++;
            Check(selected == percent, "Configured wreck split incorrect");
        }
        Check((string)chooseLoot.Invoke(null, new object[] { "VORB", true, 1d }) == "RandomDerelictVenus", "100% must include Unity's inclusive RNG endpoint");
        Check((string)chooseLoot.Invoke(null, new object[] { "OKLG", true, 0d }) == "RandomShip", "Wreck option affected OKLG");
        Check((string)chooseLoot.Invoke(null, new object[] { "VORB", false, 0d }) == "RandomShip", "Unavailable Venus pool must retain native fallback");
        foreach (int invalid in new[] { -1, 101 }) {
            bool rejected = false;
            try { Set(wreckSetting, invalid); } catch (TargetInvocationException e) { rejected = e.InnerException is ArgumentOutOfRangeException; }
            Check(rejected && wreckPercent.Value == 100, "Out-of-range wreck setting changed config");
        }
        Set(wreckSetting, 50);
        foreach(var id in new[]{"em_propagation","sensors"}) {
            var config=id=="sensors"?sensorPositions:emPropagation;
            Check((int)Get(settings[id])==0,"Sensor-related option should default off");
            Set(settings[id],1); Check((int)Get(settings[id])==1 && config.Value,"Sensor-related PDA toggle/config failed");
            Set(settings[id],0); Check((int)Get(settings[id])==0 && !config.Value,"Sensor-related PDA toggle did not disable");
            Check((string)Field(settings[id],"Lifetime")=="Saved","Sensor-related PDA setting lifetime incorrect");
        }
        var hideNote = Config(false);
        AccessTools.Field(Type("NavConsole.TutorialNote"), "HideFlag").SetValue(null, hideNote);
        Check((int)Get(settings["hide_nav_note"]) == 0 && (string)Field(settings["hide_nav_note"], "Lifetime") == "Saved", "Nav tutorial default/lifetime incorrect");
        Set(settings["autovents"], 0); Check(!autovents.Value, "Auto-vents could not disable");
        Set(settings["autovents"], 1); Check(autovents.Value && (int)Get(settings["autovents"]) == 1, "Auto-vents could not enable");
        Set(settings["altwork"], 1); Check(flag.Value && (int)Get(settings["altwork"]) == 1, "Altwork setter/config not used");
        Set(settings["altwork"], 0); Check(!flag.Value, "Altwork could not disable");
        Set(settings["hotkeys"], 1); Check(hotkeys.Value, "Hotkeys setter/config not used");
        foreach (int value in new[] { 1, 8, 30 }) { Set(settings["supply_window"], value); Check(window.Value == value, "Supply window not applied"); }
        foreach (int value in new[] { 0, 31 }) {
            bool rejected = false;
            try { Set(settings["supply_window"], value); } catch (TargetInvocationException e) { rejected = e.InnerException is ArgumentOutOfRangeException; }
            Check(rejected && window.Value == 30, "Invalid window changed configuration");
        }
        foreach (var id in new[] { "orbital", "coast", "sff" }) {
            Set(settings[id], 1); Check((int)Get(settings[id]) == 1, id + " did not enable");
            Set(settings[id], 0); Check((int)Get(settings[id]) == 0, id + " did not disable");
            Check((string)Field(settings[id], "Lifetime") == "This session", "Orbital lifetime mislabeled");
        }
        Check(!(bool)AccessTools.Property(Type("Orbital.OrbitalSimulation"), "LaunchDefault").GetValue(null), "Session toggle changed orbital launch default");
        // Install actual UI Harmony patches on native GUIPDA. Native UI access is not
        // exercised here: there is no Unity scene or prefab in this Mono fixture.
        harmony.CreateClassProcessor(Type("PdaSettings.SettingsAppPatch")).Patch();
        harmony.CreateClassProcessor(Type("PdaSettings.SettingsStatePatch")).Patch();
        var open = AccessTools.Method(typeof(GUIPDA), nameof(GUIPDA.OpenApp));
        Check(Harmony.GetPatchInfo(open).Prefixes.Count > 0, "App hook did not install");
        GUIPDA.OpenApp("template_settings"); // No PDA instance: safely do nothing.
        Check(GUIPDA.instance == null, "App opening manufactured a native UI instance");
        harmony.UnpatchSelf();
        Console.WriteLine(checks + " native asset/registry/hook checks passed.");
        return 0;
    }
}
