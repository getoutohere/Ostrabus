using System;
using System.Diagnostics;
using HarmonyLib;
using Ostranauts.Ships;
using UnityEngine.SceneManagement;

namespace Ostrabus.Inspection;

// Intentionally no HarmonyPatch attribute: PatchAll never installs these hooks.
internal static class MemoryDiagnosticHooks {
    const string Owner = "com.ostranauts.template.inspection.memory";
    static readonly Harmony harmony = new Harmony(Owner);
    static volatile bool installed;
    internal static bool Installed => installed;
    internal sealed class Scope {
        internal long Start;
        internal string Method, ShipId, ShipType;
        internal int? Items, Tiles, Registry;
    }
    static HarmonyMethod Hook(string name) => new HarmonyMethod(typeof(MemoryDiagnosticHooks), name);
    internal static void Install() {
        if (Installed) return;
        try {
            harmony.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.Destroy)), prefix: Hook(nameof(ShipBegin)), finalizer: Hook(nameof(End)));
            foreach (var type in new[] { typeof(BarterZoneShip), typeof(AsyncPreviewShip), typeof(AsyncRatingShip) }) {
                foreach (var method in AccessTools.GetDeclaredMethods(type))
                    if (method.Name == "Destroy") harmony.Patch(method, prefix: Hook(nameof(ShipBegin)), finalizer: Hook(nameof(End)));
            }
            harmony.Patch(AccessTools.Method(typeof(CrewSim), nameof(CrewSim.EmptyScene)), prefix: Hook(nameof(BoundaryBegin)), finalizer: Hook(nameof(BoundaryEnd)));
            harmony.Patch(AccessTools.Method(typeof(DataHandler), nameof(DataHandler.Init)), prefix: Hook(nameof(BoundaryBegin)), finalizer: Hook(nameof(BoundaryEnd)));
            foreach (var method in AccessTools.GetDeclaredMethods(typeof(AsyncShipLoader))) {
                if (method.Name == "Unload") harmony.Patch(method, prefix: Hook(nameof(BoundaryBegin)), finalizer: Hook(nameof(BoundaryEnd)));
            }
            harmony.Patch(AccessTools.Method(typeof(CondOwner), nameof(CondOwner.Destroy)), prefix: Hook(nameof(ManagedDestroy)));
            harmony.Patch(AccessTools.Method(typeof(CondOwner), "OnDestroy"), prefix: Hook(nameof(UnityDestroy)));
            harmony.Patch(AccessTools.Method(typeof(Tile), nameof(Tile.Destroy)), prefix: Hook(nameof(TileDestroy)));
            SceneManager.sceneLoaded += Loaded;
            SceneManager.sceneUnloaded += Unloaded;
            installed = true;
        } catch { harmony.UnpatchSelf(); throw; }
    }
    internal static void Remove() {
        if (!Installed) return;
        harmony.UnpatchSelf();
        SceneManager.sceneLoaded -= Loaded;
        SceneManager.sceneUnloaded -= Unloaded;
        installed = false;
    }
    static void Loaded(Scene scene, LoadSceneMode mode) { SceneEvent("scene-loaded", scene); }
    static void Unloaded(Scene scene) { SceneEvent("scene-unloaded", scene); }
    static void SceneEvent(string kind, Scene scene) {
        try { MemoryDiagnostics.Record(kind, SnapshotJson.Map("scene", scene.name)); }
        catch (Exception ex) { MemoryDiagnostics.Failure(ex); }
    }
    static void ManagedDestroy() { MemoryDiagnostics.ManagedDestroyCalls++; }
    static void UnityDestroy(CondOwner __instance) {
        try { MemoryDiagnostics.UnityDestroyed(__instance); }
        catch (Exception ex) { MemoryDiagnostics.Failure(ex); }
    }
    static void TileDestroy() { MemoryDiagnostics.TileDestroyCalls++; }
    static void ShipBegin(Ship __instance, System.Reflection.MethodBase __originalMethod, out Scope __state) {
        __state = null;
        try {
            __state = new Scope { Start = Stopwatch.GetTimestamp(), Method = __originalMethod.DeclaringType.Name + ".Destroy", ShipId = __instance.strRegID,
                ShipType = __instance.GetType().Name, Items = MemoryDiagnostics.ItemCount(__instance), Tiles = __instance.aTiles?.Count,
                Registry = DataHandler.mapCOs?.Count };
            MemoryDiagnostics.SampleShip(__instance);
        } catch (Exception ex) { MemoryDiagnostics.Failure(ex); }
    }
    static void BoundaryBegin(System.Reflection.MethodBase __originalMethod, out Scope __state) {
        __state = new Scope { Start = Stopwatch.GetTimestamp(), Method = __originalMethod.DeclaringType.Name + "." + __originalMethod.Name };
        try { MemoryDiagnostics.Record(__state.Method + ":before", null, census: true, process: true); }
        catch (Exception ex) { MemoryDiagnostics.Failure(ex); }
    }
    static Exception BoundaryEnd(Exception __exception, Scope __state) {
        try { MemoryDiagnostics.Record((__state?.Method ?? "boundary") + ":after", SnapshotJson.Map("exception", __exception?.GetType().Name), census: true, process: true); }
        catch (Exception ex) { MemoryDiagnostics.Failure(ex); }
        return __exception;
    }
    static Exception End(Ship __instance, Exception __exception, Scope __state) {
        if (__state == null) return __exception;
        try {
            MemoryDiagnostics.Record(__state.Method, SnapshotJson.Map("shipId", __state.ShipId, "shipType", __state.ShipType,
                "mappedItemsBefore", __state.Items, "tilesBefore", __state.Tiles, "registryBefore", __state.Registry,
                "mappedItemsAfter", MemoryDiagnostics.ItemCount(__instance), "shipDestroyedAfter", __instance.bDestroyed,
                "unityObjectAliveAfter", __instance.gameObject != null,
                "elapsedMs", (Stopwatch.GetTimestamp() - __state.Start) * 1000d / Stopwatch.Frequency,
                "exception", __exception?.GetType().Name));
        } catch (Exception ex) { MemoryDiagnostics.Failure(ex); }
        return __exception;
    }
}
