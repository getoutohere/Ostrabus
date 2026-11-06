using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Ostranauts;
using Ostranauts.Core;
using Ostranauts.Core.Models;
using Ostranauts.Tools;

namespace Ostrabus.Optimizations;

[HarmonyPatch(typeof(LoadManager), "OnLoadSelectedSave")]
internal static class SaveSelectionProfilePatch {
    [HarmonyPrepare] static bool Prepare() => LoadProfiling.Enabled;
    [HarmonyPrefix]
    static void Prefix(SaveInfo saveInfo, out LoadProfiling.Sample __state) {
        __state = default;
        if (!LoadProfiling.Enabled || saveInfo == null) return;
        LoadProfiling.Begin("saveLoad");
        LoadProfiling.StageChanged("Archive extraction / scene transition");
        __state = LoadProfiling.Enter("LoadManager.OnLoadSelectedSave");
    }
    [HarmonyFinalizer]
    static void Finalizer(Exception __exception, LoadProfiling.Sample __state) {
        LoadProfiling.Exit(__state, __exception);
        if (__exception != null) LoadProfiling.Finish("saveLoad", __exception);
    }
}

[HarmonyPatch]
internal static class LoadIteratorProfilePatch {
    [HarmonyPrepare] static bool Prepare() => LoadProfiling.Enabled;
    static MethodBase root;
    static MethodBase dataRoot;
    static MethodInfo Iterator(Type type, string name, params Type[] arguments) {
        MethodInfo method = AccessTools.Method(type, name, arguments);
        Type iterator = method?.GetCustomAttribute<IteratorStateMachineAttribute>()?.StateMachineType;
        return iterator?.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(type.FullName, name + " iterator MoveNext");
    }
    static IEnumerable<MethodBase> TargetMethods() {
        root = Iterator(typeof(CrewSim), "DoLoadGame", typeof(string), typeof(string), typeof(Dictionary<string, byte[]>));
        yield return root;
        yield return Iterator(typeof(StarSystem), "Init", typeof(JsonStarSystemSave), typeof(JsonShip[]));
        dataRoot = Iterator(typeof(LoadManager), "AfterLoadThreadsFinish");
        yield return dataRoot;
    }
    [HarmonyPrefix]
    static void Prefix(MethodBase __originalMethod, out LoadProfiling.Sample __state) {
        if (__originalMethod == root && !LoadProfiling.IsActive("saveLoad")) LoadProfiling.Begin("saveLoad");
        __state = LoadProfiling.Enter(__originalMethod == root ? "CrewSim.DoLoadGame.MoveNext" :
            __originalMethod == dataRoot ? "LoadManager.AfterLoadThreadsFinish.MoveNext" : "StarSystem.Init.MoveNext");
    }
    [HarmonyFinalizer]
    static void Finalizer(MethodBase __originalMethod, object __instance, bool __result, Exception __exception, LoadProfiling.Sample __state) {
        int yielded = -1;
        if (__exception == null && __result) {
            object current = ((IEnumerator)__instance).Current;
            yielded = current == null ? 0 : current is IEnumerator ? 1 : 2;
        }
        LoadProfiling.Exit(__state, __exception, yielded);
        if (__originalMethod == root && (!__result || __exception != null)) LoadProfiling.Finish("saveLoad", __exception);
        if (__originalMethod == dataRoot && (!__result || __exception != null)) LoadProfiling.Finish("dataInit", __exception);
    }
}

[HarmonyPatch(typeof(LoadingScreen), nameof(LoadingScreen.SetProgressBar))]
internal static class LoadStageProfilePatch {
    [HarmonyPrepare] static bool Prepare() => LoadProfiling.Enabled;
    [HarmonyPrefix]
    static void Prefix(string textToDisplay) { LoadProfiling.StageChanged(textToDisplay); }
}

// Instrument just the two ship-file read call sites, avoiding Harmony patches on shared generic JsonToData code.
[HarmonyPatch]
internal static class ShipFileProfilePatch {
    // Experimental JSON replacement also uses these concrete file-reader call sites.
    [HarmonyPrepare] static bool Prepare() => LoadProfiling.Enabled || SavedJsonLoading.Enabled;
    static MethodBase TargetMethod() {
        MethodInfo method = AccessTools.Method(typeof(CrewSim), "DoLoadGame", new[] { typeof(string), typeof(string), typeof(Dictionary<string, byte[]>) });
        return method.GetCustomAttribute<IteratorStateMachineAttribute>().StateMachineType.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    }
    [HarmonyTranspiler]
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var list = new List<CodeInstruction>(instructions);
        var matches = new List<CodeInstruction>();
        foreach (CodeInstruction instruction in list) {
            if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(DataHandler) && method.Name == "JsonToData" &&
                method.IsGenericMethod && method.GetGenericArguments()[0] == typeof(JsonShip)) matches.Add(instruction);
        }
        // Fail closed on game changes; no partial instrumentation of unexpected call signatures.
        bool compatible = matches.Count == 2;
        foreach (CodeInstruction match in matches) {
            ParameterInfo[] p = ((MethodInfo)match.operand).GetParameters();
            compatible &= (p.Length == 2 || p.Length == 3) && p[0].ParameterType == typeof(string) && p[1].ParameterType == typeof(Dictionary<string, JsonShip>);
            if (p.Length == 3) compatible &= p[2].ParameterType == typeof(Dictionary<string, byte[]>);
        }
        if (!compatible) Ostrabus.OstrabusPlugin.Logger?.LogWarning("[LoadProfile] Ship-file call sites changed; file timings disabled, original calls retained.");
        else foreach (CodeInstruction match in matches) match.operand = AccessTools.Method(typeof(ShipFileProfilePatch),
            ((MethodInfo)match.operand).GetParameters().Length == 2 ? nameof(ReadDisk) : nameof(ReadMemory));
        return list;
    }
    static void ReadDisk(string path, Dictionary<string, JsonShip> ships) {
        var sample = LoadProfiling.Enter("JsonToData.JsonShip.disk", LoadProfiling.IsActive() ? Path.GetFileName(path) : null);
        Exception error = null;
        try { DataHandler.JsonToData(path, ships); LoadProfiling.Count("shipFilesParsed", 1); }
        catch (Exception ex) { error = ex; throw; }
        finally { LoadProfiling.Exit(sample, error); }
    }
    static void ReadMemory(string path, Dictionary<string, JsonShip> ships, Dictionary<string, byte[]> files) {
        var sample = LoadProfiling.Enter("JsonToData.JsonShip.memory", LoadProfiling.IsActive() ? Path.GetFileName(path) : null);
        Exception error = null;
        try {
            DataHandler.JsonToData(path, ships, files);
            LoadProfiling.Count("shipFilesParsed", 1);
            if (files.TryGetValue(path, out byte[] bytes)) LoadProfiling.Count("shipJsonBytes", bytes.Length);
        }
        catch (Exception ex) { error = ex; throw; }
        finally { LoadProfiling.Exit(sample, error); }
    }
}

[HarmonyPatch]
internal static class LoadMethodProfilePatch {
    [HarmonyPrepare] static bool Prepare() => LoadProfiling.Enabled;
    static IEnumerable<MethodBase> TargetMethods() {
        foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(DataHandler))) if (method.Name == "LoadSaveFile") yield return method;
        foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(DotNetZipCompressor))) if (method.Name == "ExtractArchive") yield return method;
        yield return AccessTools.Method(typeof(CrewSim), "EmptyScene");
        yield return AccessTools.Method(typeof(StarSystem), "_SpawnShip");
        yield return AccessTools.Method(typeof(Ship), "InitDocking");
        yield return AccessTools.Method(typeof(LoadManager), "LoadImageFolder");
        yield return AccessTools.Method(typeof(LoadManager), "DeleteTempFolder");
        yield return AccessTools.Method(typeof(LoadManager), "LoadDataHandlerDelegates");
        yield return AccessTools.Method(typeof(DataHandler), "AllPostLoadAsync");
    }
    [HarmonyPrefix]
    static void Prefix(MethodBase __originalMethod, out LoadProfiling.Sample __state) {
        __state = default;
        if (!LoadProfiling.IsActive()) return;
        string name = __originalMethod.DeclaringType.Name + "." + __originalMethod.Name;
        if (__originalMethod.Name == "LoadSaveFile") name += __originalMethod.GetParameters().Length == 1 ? ".disk" : ".dispatch";
        if (__originalMethod.Name == "ExtractArchive") name += __originalMethod.GetParameters().Length == 1 ? ".memory" : ".disk";
        __state = LoadProfiling.Enter(name);
    }
    [HarmonyFinalizer]
    static void Finalizer(Exception __exception, LoadProfiling.Sample __state) { LoadProfiling.Exit(__state, __exception); }
}

[HarmonyPatch(typeof(DataHandler), nameof(DataHandler.LoadSaveFile), new[] { typeof(string), typeof(Dictionary<string, byte[]>) })]
internal static class SaveSizeProfilePatch {
    [HarmonyPrepare] static bool Prepare() => LoadProfiling.Enabled;
    [HarmonyPrefix]
    static void Prefix(string strFileName, Dictionary<string, byte[]> dictFiles) {
        if (LoadProfiling.IsActive() && dictFiles != null && dictFiles.TryGetValue(strFileName, out byte[] bytes)) LoadProfiling.Count("mainSaveJsonBytes", bytes.Length);
    }
    [HarmonyPostfix]
    static void Postfix(JsonGameSave __result) {
        if (__result == null || !LoadProfiling.IsActive()) return;
        LoadProfiling.Count("savedCOs", __result.aCOs?.Length ?? 0);
        LoadProfiling.Count("embeddedSavedShips", __result.aShips?.Length ?? 0);
    }
}

[HarmonyPatch(typeof(DotNetZipCompressor), nameof(DotNetZipCompressor.ExtractArchive), new[] { typeof(string) })]
internal static class ArchiveSizeProfilePatch {
    [HarmonyPrepare] static bool Prepare() => LoadProfiling.Enabled;
    [HarmonyPostfix]
    static void Postfix(Dictionary<string, byte[]> __result) {
        if (!LoadProfiling.IsActive() || __result == null) return;
        long bytes = 0, imageBytes = 0;
        foreach (var entry in __result) {
            bytes += entry.Value?.Length ?? 0;
            if (entry.Key.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) imageBytes += entry.Value?.Length ?? 0;
        }
        LoadProfiling.Count("archiveEntries", __result.Count);
        LoadProfiling.Count("archiveUncompressedBytes", bytes);
        LoadProfiling.Count("archivePngBytes", imageBytes);
    }
}

[HarmonyPatch(typeof(Ship), nameof(Ship.InitShip))]
internal static class ShipInitProfilePatch {
    [HarmonyPrepare] static bool Prepare() => LoadProfiling.Enabled;
    [HarmonyPrefix]
    static void Prefix(Ship __instance, Ship.Loaded nLoad, string strRegIDNew, out LoadProfiling.Sample __state) {
        __state = default;
        if (!LoadProfiling.IsActive()) return;
        __state = LoadProfiling.Enter("Ship.InitShip." + nLoad, __instance.strRegID ?? strRegIDNew, __instance.json?.aItems?.Length ?? 0);
    }
    [HarmonyFinalizer]
    static void Finalizer(Exception __exception, LoadProfiling.Sample __state) { LoadProfiling.Exit(__state, __exception); }
}

[HarmonyPatch(typeof(Ship), "SpawnItems")]
internal static class ShipItemsProfilePatch {
    [HarmonyPrepare] static bool Prepare() => LoadProfiling.Enabled;
    [HarmonyPrefix]
    static void Prefix(Ship __instance, List<JsonItem> aItemsPlusCrew, out LoadProfiling.Sample __state) {
        __state = LoadProfiling.IsActive() ? LoadProfiling.Enter("Ship.SpawnItems", __instance.strRegID, aItemsPlusCrew?.Count ?? 0) : default;
    }
    [HarmonyFinalizer]
    static void Finalizer(Exception __exception, LoadProfiling.Sample __state) { LoadProfiling.Exit(__state, __exception); }
}

[HarmonyPatch(typeof(CrewSim), "Awake")]
internal static class CrewInitProfilePatch {
    [HarmonyPrepare] static bool Prepare() => LoadProfiling.Enabled;
    internal struct Scope { internal bool OwnSession; internal LoadProfiling.Sample Work; }
    [HarmonyPrefix]
    static void Prefix(out Scope __state) {
        __state = default;
        if (!LoadProfiling.Enabled) return;
        __state.OwnSession = !LoadProfiling.IsActive();
        if (__state.OwnSession) LoadProfiling.Begin("sceneInit");
        LoadProfiling.StageChanged("CrewSim.Awake");
        __state.Work = LoadProfiling.Enter("CrewSim.Awake");
    }
    [HarmonyFinalizer]
    static void Finalizer(Exception __exception, Scope __state) {
        LoadProfiling.Exit(__state.Work, __exception);
        if (__state.OwnSession) LoadProfiling.Finish("sceneInit", __exception);
    }
}

[HarmonyPatch(typeof(DataHandler), nameof(DataHandler.Init))]
internal static class DataInitProfilePatch {
    [HarmonyPrepare] static bool Prepare() => LoadProfiling.Enabled;
    [HarmonyPrefix]
    static void Prefix(out LoadProfiling.Sample __state) {
        LoadProfiling.Begin("dataInit");
        __state = LoadProfiling.Enter("DataHandler.Init");
    }
    [HarmonyFinalizer]
    static void Finalizer(Exception __exception, LoadProfiling.Sample __state) {
        LoadProfiling.Exit(__state, __exception);
        if (__exception != null) LoadProfiling.Finish("dataInit", __exception);
        else LoadProfiling.StageChanged("Background data loading / wait");
    }
}

[HarmonyPatch(typeof(DataHandler), nameof(DataHandler.PostModLoadMainThread))]
internal static class DataInitCompleteProfilePatch {
    [HarmonyPrepare] static bool Prepare() => LoadProfiling.Enabled;
    [HarmonyPrefix]
    static void Prefix(out LoadProfiling.Sample __state) {
        LoadProfiling.StageChanged("Data post-load main thread");
        __state = LoadProfiling.Enter("DataHandler.PostModLoadMainThread");
    }
    [HarmonyFinalizer]
    static void Finalizer(Exception __exception, LoadProfiling.Sample __state) {
        LoadProfiling.Exit(__state, __exception);
        if (__exception != null) LoadProfiling.Finish("dataInit", __exception);
    }
}
