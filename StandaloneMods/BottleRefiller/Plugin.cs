using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace Ostrabus.BottleRefiller
{
    [BepInPlugin(Id, "Bottle Refiller", Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Id = "com.ostrabus.bottlerefiller";
        public const string Version = "0.1.7";
        internal static ManualLogSource Log;
        private Harmony harmony;

        private void Awake()
        {
            Log = Logger;
            harmony = new Harmony(Id);
            try { harmony.PatchAll(typeof(Plugin).Assembly); }
            catch (Exception error)
            {
                harmony.UnpatchSelf();
                Logger.LogError("Bottle Refiller could not attach its CondOwner.AddCommand(string) hook: " + error);
                return;
            }
            Logger.LogInfo("Bottle Refiller " + Version + " ready (one command-loader hook). Install its native assets to acquire the device.");
        }

        private void OnDestroy() { harmony?.UnpatchSelf(); }
    }

    [HarmonyPatch(typeof(CondOwner), nameof(CondOwner.AddCommand), new[] { typeof(string) })]
    internal static class CommandPatch
    {
        internal const string Command = "OstrabusBottleRefiller";

        private static bool Prefix(CondOwner __instance, string strDef)
        {
            if (strDef != Command) return true;
            // Only our authored definitions can opt into this component.
            if (Runtime.Definition(__instance.strCODef) && __instance.objContainer != null &&
                __instance.objContainer.ContainedCOs.Count == 0)
                // SetData skips authored grid sizes while upgrading legacy saves.
                // This command runs before saved children are restored.
                __instance.objContainer.gridLayout = new Ostranauts.Inventory.GridLayout(2, 2);
            if ((__instance.strCODef == Runtime.Item || __instance.strCODef == Runtime.Item + "Dmg") &&
                __instance.GetComponent<Refiller>() == null)
                __instance.gameObject.AddComponent<Refiller>();
            return false;
        }
    }
}
