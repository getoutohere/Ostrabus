using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine.SceneManagement;

namespace Ostrabus {
    /// <summary>
    /// Ostrabus bug fixes, quality-of-life features and inspection tools.
    /// </summary>
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class OstrabusPlugin : BaseUnityPlugin {
        internal static new ManualLogSource Logger;
        private Harmony _harmony;
        private global::Ostrabus.Inspection.InspectionApi _inspectionApi;
        private global::Ostrabus.Inspection.InspectionLogHooks _inspectionLogs;

        private void Awake() {
            SceneManager.sceneUnloaded += global::Ostrabus.ShipboardAction.ShipboardActionSimulation.OnSceneUnloaded;
            SceneManager.sceneUnloaded += global::Ostrabus.CeresNavigation.CeresNavigationGuardrails.OnSceneUnloaded;
            SceneManager.sceneUnloaded += global::Ostrabus.Haulers.HaulerRecovery.SceneUnloaded;
            SceneManager.sceneUnloaded += global::Ostrabus.NavConsole.ProximityRange.Reset;
            SceneManager.sceneUnloaded += global::Ostrabus.Venus.VorbNavPlanGuard.ResetRuntimeState;
            Logger = base.Logger;
            global::Ostrabus.EMPropagation.EmPropagation.Initialize(Config);
            global::Ostrabus.Sensors.SensorContacts.Initialize(Config);
            global::Ostrabus.NavConsole.TutorialNote.HideFlag = Config.Bind("NavConsole", "HideTutorialNote", false, "Hide the clickable Don't crash! tutorial paper on nav consoles. Change live in the PDA Mod Settings app.");
            global::Ostrabus.QoL.AutoVents.AutoVentControl.Initialize(Config);
            SceneManager.sceneUnloaded += global::Ostrabus.QoL.AutoVents.AutoVentControl.SceneUnloaded;
            global::Ostrabus.Hotkeys.FunctionalHotkeys.Flag = Config.Bind("Hotkeys", "Enabled", true, "Enable functional crew hotkeys in normal crew view. Shift+R toggles Open Airlock; Shift+T toggles Disembark for the selected controllable crew member.");
            global::Ostrabus.Orbital.OrbitalSimulation.Initialize(Config.Bind("Orbital", "Enabled", false, "Launch default for parent-frame acceleration correction on numerically simulated free ships/docked groups. Console orbital on/off overrides for this session without changing this setting. Experimental; live AI/navigation validation required."));
            global::Ostrabus.Orbital.SffSimulation.Enabled = Config.Bind("Orbital", "SuperFastForward", true, "With Orbital enabled, replace SFF numerical jumps with orbital coasting and bounded AI travel. Player plotted courses and combat retain vanilla behavior. Console orbital sff on/off overrides for this session.").Value;
            global::Ostrabus.Orbital.Economy.SffEconomy.Enabled = Config.Bind("Orbital", "SffEconomy", true, "With Orbital and SuperFastForward enabled, process production, cargo dispatch and eligible shallow freight arrivals chronologically during SFF.").Value;
            SceneManager.sceneUnloaded += global::Ostrabus.Orbital.OrbitalSimulation.SceneUnloaded;
            global::Ostrabus.AlternativeWork.AlternativeWorkSystem.Flag = Config.Bind("AlternativeWork", "Enabled", false, "Experimental crew work-order selector and cooperative travel planning. Toggle live with altwork on/off; existing work continues.");
            global::Ostrabus.AlternativeWork.WorkSupplyPlan.WindowConfig = Config.Bind("AlternativeWork", "SupplyWindow", 8, new BepInEx.Configuration.ConfigDescription("Rolling nearby supply target window. Console altwork window N changes it live; existing native work continues.", new BepInEx.Configuration.AcceptableValueRange<int>(1, 30)));
            SceneManager.sceneUnloaded += global::Ostrabus.AlternativeWork.AlternativeWorkSystem.SceneUnloaded;
            _inspectionLogs = new global::Ostrabus.Inspection.InspectionLogHooks();
            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
            global::Ostrabus.Optimizations.LoadProfiling.Enabled = Config.Bind("LoadProfiling", "Enabled", true, "Capture bounded startup/save loading timings for GET /profiling/load. Disabled skips installing profiling hooks; experimental JSON may retain its shared file-reader routing. Restart to apply.").Value;
            global::Ostrabus.Optimizations.SavedJsonLoading.Enabled = Config.Bind("Optimizations", "ExperimentalSaveJson", false, "Use the experimental LitJson-compatible Newtonsoft mapper for archived world/ship saves. Offline validated; enable for an in-game load/save/reload test. Saving is unchanged. Restart to apply.").Value;
            global::Ostrabus.Optimizations.SystemInitBatchPatch.Enabled = Config.Bind("Optimizations", "BatchSystemInit", true, "Batch cheap saved-system restoration yields on the main thread. Restart to apply.").Value;
            global::Ostrabus.Optimizations.SystemInitBatchPatch.BudgetMs = Config.Bind("Optimizations", "SystemInitBudgetMs", 4d, "Cooperative work budget per frame for cheap saved-system restoration; clamped to 0.5-8 ms. Restart to apply.").Value;

            // Apply Harmony patches
            _harmony = new Harmony(PluginInfo.PLUGIN_GUID);
            _harmony.PatchAll(typeof(OstrabusPlugin).Assembly);
            SceneManager.sceneUnloaded += global::Ostrabus.Diagnostics.WorkTrace.SceneUnloaded;
            global::Ostrabus.Diagnostics.WorkTrace.SetEnabled(Config.Bind("Diagnostics", "WorkTrace", false, "Launch default for temporary player/crew work/suit diagnostics. worktrace on/off installs/removes its Harmony hooks live; GET /workorders/trace. Restart to apply the launch setting.").Value);
            
            Logger.LogInfo("Harmony patches applied successfully!");
            bool apiEnabled = Config.Bind("InspectionApi", "Enabled", true, "Enable the localhost inspection API; derelict pruning requires an explicit POST.").Value;
            int apiPort = Config.Bind("InspectionApi", "Port", 18764, "HTTP port bound to 127.0.0.1 only.").Value;
            if (apiEnabled) {
                try {
                    _inspectionApi = new global::Ostrabus.Inspection.InspectionApi(apiPort);
                    Logger.LogInfo($"Inspection API listening at http://127.0.0.1:{apiPort}/");
                } catch (System.Exception ex) { Logger.LogError($"Inspection API could not start: {ex.Message}"); }
            }
        }

        private void Update() {
            global::Ostrabus.Hotkeys.FunctionalHotkeys.Tick();
            global::Ostrabus.AlternativeWork.AlternativeWorkSystem.Tick();
            _inspectionApi?.Pump();
        }

        private void OnDestroy() {
            global::Ostrabus.Sensors.SensorContacts.Stop();
            global::Ostrabus.EMPropagation.EmPropagation.Stop();
            global::Ostrabus.PdaSettings.SettingsPanel.Stop();
            SceneManager.sceneUnloaded -= global::Ostrabus.QoL.AutoVents.AutoVentControl.SceneUnloaded;
            global::Ostrabus.QoL.AutoVents.AutoVentControl.Stop();
            global::Ostrabus.Diagnostics.WorkTrace.Stop();
            SceneManager.sceneUnloaded -= global::Ostrabus.Diagnostics.WorkTrace.SceneUnloaded;
            global::Ostrabus.Optimizations.LoadProfiling.Stop();
            SceneManager.sceneUnloaded -= global::Ostrabus.Orbital.OrbitalSimulation.SceneUnloaded;
            global::Ostrabus.Orbital.OrbitalSimulation.Reset();
            SceneManager.sceneUnloaded -= global::Ostrabus.AlternativeWork.AlternativeWorkSystem.SceneUnloaded;
            global::Ostrabus.AlternativeWork.AlternativeWorkSystem.Reset();
            _inspectionApi?.Dispose();
            SceneManager.sceneUnloaded -= global::Ostrabus.ShipboardAction.ShipboardActionSimulation.OnSceneUnloaded;
            SceneManager.sceneUnloaded -= global::Ostrabus.CeresNavigation.CeresNavigationGuardrails.OnSceneUnloaded;
            SceneManager.sceneUnloaded -= global::Ostrabus.Haulers.HaulerRecovery.SceneUnloaded;
            SceneManager.sceneUnloaded -= global::Ostrabus.NavConsole.ProximityRange.Reset;
            global::Ostrabus.NavConsole.ProximityRange.Reset(default);
            global::Ostrabus.NavConsole.RoundKnobArt.Release();
            SceneManager.sceneUnloaded -= global::Ostrabus.Venus.VorbNavPlanGuard.ResetRuntimeState;
            global::Ostrabus.ShipboardAction.ShipboardActionSimulation.ResetRuntimeState();
            global::Ostrabus.CeresNavigation.CeresNavigationGuardrails.ResetRuntimeState();
            global::Ostrabus.Haulers.HaulerRecovery.SceneUnloaded(default);
            global::Ostrabus.Venus.VorbNavPlanGuard.ResetRuntimeState(default);
            _harmony?.UnpatchSelf();
            _inspectionLogs?.Dispose();
        }
    }

    public static class PluginInfo {
        // Stable installation identity: keeps the existing BepInEx config and
        // Harmony ownership. The project, assembly and displayed name are Ostrabus.
        public const string PLUGIN_GUID = "com.ostranauts.template";
        public const string PLUGIN_NAME = "Ostrabus";
        public const string PLUGIN_VERSION = "0.2.1";
    }
}


