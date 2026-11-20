using System;
using System.IO;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace Ostrabus.BoneyardTurnover
{
    [BepInPlugin(Id, "Boneyard Turnover", Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Id = "com.ostrabus.boneyardturnover";
        public const string Version = "0.1.5";
        internal const string NativeHash = "133A9CC94AD4F4D5DB951B65249B6CDF78F53063419D836B8871B92960EC3C37";
        internal const string NativeHash1103 = "E9E7B8796423701C157EA5718CB407E6517AC0435EF947062C9A27464F4BC82F";
        internal const string NativeHash1104 = "904C72870F55124BBE3F9EE924A4A650279C362541B5795174D0CF940B97C474";
        internal static bool Supports(string hash) => hash == NativeHash || hash == NativeHash1103 || hash == NativeHash1104;
        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> EnableSetting;
        internal static ConfigEntry<double> HalfLifeSetting, ShapeSetting;
        internal static bool Enabled => EnableSetting?.Value ?? false;
        internal static double HalfLife => HalfLifeSetting?.Value ?? 7;
        internal static double Shape => ShapeSetting?.Value ?? 1;
        private Harmony harmony;

        private void Awake()
        {
            Log = Logger;
            EnableSetting = Config.Bind("Turnover", "Enabled", true, "Enable turnover on future departures and returns. Disabling clears departure tracking.");
            HalfLifeSetting = Config.Bind("Turnover", "HalfLifeDays", 7d, "Accumulated in-game days outside the region for a 50% replacement chance. Must be finite and positive. Captured at departure.");
            ShapeSetting = Config.Bind("Turnover", "CurveShape", 1d, "1 = exponential; below 1 = faster early turnover; above 1 = delayed turnover. Must be finite and positive. Captured at departure.");
            EnableSetting.SettingChanged += OnEnabledChanged;
            using var file = File.OpenRead(typeof(CrewSim).Assembly.Location);
            using var hash = SHA256.Create();
            if (!Supports(BitConverter.ToString(hash.ComputeHash(file)).Replace("-", "")))
            {
                Logger.LogError("Boneyard Turnover is inactive: this build requires a reviewed Ostranauts 1.1.0.2, 1.1.0.3 or 1.1.0.4 assembly.");
                return;
            }
            harmony = new Harmony(Id);
            try { harmony.PatchAll(typeof(Plugin).Assembly); }
            catch { harmony.UnpatchSelf(); throw; }
            CrewSim.OnGameFinishedLoading.AddListener(FinishedLoading);
            Logger.LogInfo("Boneyard Turnover " + Version + " ready. Tracking begins after loading and a qualifying departure; Earth, OKLG and VORB are excluded.");
        }

        private static void OnEnabledChanged(object sender, EventArgs args)
        {
            if (!Enabled) Runtime.State.Fields.Clear();
        }

        private static void FinishedLoading()
        {
            try { Runtime.Loaded(Persistence.TakeLoaded()); }
            catch (Exception error) { Runtime.Reset(); Warn("Tracking initialization skipped: " + error); }
        }

        private void OnDestroy()
        {
            CrewSim.OnGameFinishedLoading.RemoveListener(FinishedLoading);
            if (EnableSetting != null) EnableSetting.SettingChanged -= OnEnabledChanged;
            harmony?.UnpatchSelf();
            Runtime.Reset(); Persistence.ResetLoad();
        }

        internal static void Warn(string message) => Log?.LogWarning(message);
        internal static void LogDeparture(string message) => Log?.LogInfo(message);
        internal static void Summary(string region, int replaced, int retained, int skipped) =>
            Log?.LogInfo(region + ": " + replaced + " wrecks replaced; " + retained + " eligible wrecks retained; " + skipped + " selected replacements skipped.");
    }
}
