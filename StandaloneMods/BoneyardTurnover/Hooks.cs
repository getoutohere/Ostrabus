using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Ionic.Zip;
using Ostranauts.Core.Models;
using SolarSystem;

namespace Ostrabus.BoneyardTurnover
{
    [HarmonyPatch(typeof(AIShipManager), "RegionCleanup")]
    internal static class RegionHook
    {
        private static void Prefix(out Departure __state)
        {
            __state = null;
            try { __state = Runtime.BeforeDeparture(); }
            catch (Exception error) { Plugin.Warn("Departure tracking skipped: " + error); }
        }
        private static void Postfix(Departure __state)
        {
            try { Runtime.AfterDeparture(__state); }
            catch (Exception error) { Plugin.Warn("Departure commit skipped: " + error); }
        }
    }

    [HarmonyPatch(typeof(EnvironmentEntities), nameof(EnvironmentEntities.LoadEnvironmentForRegion))]
    internal static class EntryHook
    {
        private static void Prefix(string __0)
        {
            try { Runtime.Enter(__0); }
            catch (Exception error) { Plugin.Warn("Turnover stopped; native environment loading continues: " + error); }
        }
    }

    [HarmonyPatch]
    internal static class LoadStartHook
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var method in AccessTools.GetDeclaredMethods(typeof(CrewSim)))
                if (method.Name == nameof(CrewSim.LoadGame)) yield return method;
        }
        private static void Prefix() => Persistence.ResetLoad();
    }

    [HarmonyPatch(typeof(DataHandler), nameof(DataHandler.LoadSaveFile), new[] { typeof(string), typeof(Dictionary<string, byte[]>) })]
    internal static class ReadHook
    {
        private static void Postfix(string __0, Dictionary<string, byte[]> __1, JsonGameSave __result)
        {
            try { Persistence.Read(__0, __1, __result); }
            catch (Exception error) { Plugin.Warn("Optional turnover metadata was ignored: " + error.Message); }
        }
    }

    [HarmonyPatch]
    internal static class SaveStartHook
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(SavingJob), nameof(SavingJob.Start));
            yield return AccessTools.Method(typeof(SavingJob), nameof(SavingJob.StartWithoutThreading));
        }
        private static void Prefix(SavingJob __instance)
        {
            try { Persistence.Capture(__instance.SaveDto); }
            catch (Exception error) { Plugin.Warn("Turnover metadata omitted; native save continues: " + error.Message); }
        }
    }

    [HarmonyPatch(typeof(SavingJob), "WriteSaveImagesToZip")]
    internal static class SaveWriteHook
    {
        private static void Postfix(SavingJob __instance, ZipFile zip)
        {
            try { Persistence.Write(__instance, zip); }
            catch (Exception error) { Plugin.Warn("Optional turnover metadata write failed: " + error.Message); }
        }
    }
}
