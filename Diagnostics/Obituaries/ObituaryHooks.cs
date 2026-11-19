using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Ostrabus.Diagnostics.Obituaries;

// All hooks observe; none skip/replace native code or change its exception.
[HarmonyPatch(typeof(CondOwner), "set_Kill")]
internal static class ObituaryDeathPatch {
    static bool Prepare() => ObituaryRecorder.Enabled;
    [HarmonyPrefix, HarmonyPriority(Priority.First)] static void Prefix(CondOwner __instance, bool value, out ObituaryRecorder.Observation __state) =>
        __state = value ? ObituaryRecorder.Before(__instance, true) : null;
    [HarmonyFinalizer, HarmonyPriority(Priority.Last)] static void Finalizer(ObituaryRecorder.Observation __state, Exception __exception) => ObituaryRecorder.After(__state, true, __exception);
}
[HarmonyPatch(typeof(CondOwner), nameof(CondOwner.Destroy))]
internal static class ObituaryRemovalPatch {
    static bool Prepare() => ObituaryRecorder.Enabled;
    [HarmonyPrefix, HarmonyPriority(Priority.First)] static void Prefix(CondOwner __instance, out ObituaryRecorder.Observation __state) => __state = ObituaryRecorder.Before(__instance, false);
    [HarmonyFinalizer, HarmonyPriority(Priority.Last)] static void Finalizer(ObituaryRecorder.Observation __state, Exception __exception) => ObituaryRecorder.After(__state, false, __exception);
}
[HarmonyPatch(typeof(Ship), nameof(Ship.Destroy))]
internal static class ObituaryShipPatch {
    static bool Prepare() => ObituaryRecorder.Enabled;
    [HarmonyPrefix] static void Prefix(Ship __instance, bool isDespawning, out ObituaryRecorder.Scope __state) => __state = ObituaryRecorder.BeginShip(__instance, isDespawning);
    [HarmonyFinalizer] static void Finalizer(ObituaryRecorder.Scope __state, Exception __exception) => ObituaryRecorder.EndScope(__state, __exception);
}
[HarmonyPatch(typeof(CondOwner), nameof(CondOwner.RemoveFromCurrentHome))]
internal static class ObituaryDetachPatch {
    static bool Prepare() => ObituaryRecorder.Enabled;
    [HarmonyPrefix] static void Prefix(CondOwner __instance) => ObituaryRecorder.Detaching(__instance);
}
[HarmonyPatch(typeof(CondOwner), nameof(CondOwner.SetData))]
internal static class ObituaryRestorePatch {
    static bool Prepare() => ObituaryRecorder.Enabled;
    [HarmonyPrefix] static void Prefix(CondOwner __instance, JsonCondOwner jid, JsonCondOwnerSave jCOSIn, out ObituaryRecorder.Scope __state) {
        __state = null;
        try {
            // Cheap rejection of the many non-person item initializations.
            if (ObituaryRecorder.PersonData(__instance, jid, jCOSIn))
                __state = new ObituaryRecorder.Scope("actor-initialization", __instance, jCOSIn?.bAlive);
        } catch (Exception ex) { ObituaryRecorder.Fault(ex); }
    }
    [HarmonyFinalizer] static void Finalizer(ObituaryRecorder.Scope __state) => ObituaryRecorder.EndScope(__state);
}
[HarmonyPatch(typeof(PersonSpec), nameof(PersonSpec.MakeCondOwner))]
internal static class ObituaryPersonRestorePatch {
    static bool Prepare() => ObituaryRecorder.Enabled;
    [HarmonyPrefix] static void Prefix(PersonSpec __instance, out ObituaryRecorder.Scope __state) {
        __state = null;
        try { __state = new ObituaryRecorder.Scope("person-materialization", savedAlive: __instance.bAlive, person: __instance); }
        catch (Exception ex) { ObituaryRecorder.Fault(ex); }
    }
    [HarmonyFinalizer] static void Finalizer(ObituaryRecorder.Scope __state) => ObituaryRecorder.EndScope(__state);
}
[HarmonyPatch]
internal static class ObituaryContextPatch {
    static bool Prepare() => ObituaryRecorder.Enabled;
    [HarmonyTargetMethods] static IEnumerable<MethodBase> TargetMethods() {
        yield return AccessTools.Method(typeof(AIShipManager), "SpawnPassShip");
        yield return AccessTools.Method(typeof(CrewSim), nameof(CrewSim.DockAndDespawn));
        yield return AccessTools.Method(typeof(CrewSim), "DestroyAndReload");
        yield return AccessTools.Method(typeof(CrewSim), nameof(CrewSim.EmptyScene));
        // Scope one coroutine step, never leave thread-local state across yield.
        yield return AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(CrewSim), "DoLoadGame"));
    }
    [HarmonyPrefix] static void Prefix(MethodBase __originalMethod, out ObituaryRecorder.Scope __state) {
        __state = null;
        try {
            string kind = __originalMethod.Name switch {
                "SpawnPassShip" => "station-population",
                "DockAndDespawn" => "station-arrival",
                "DestroyAndReload" => "ship-replacement",
                "EmptyScene" => "world-disposal",
                _ => "save-load-step"
            };
            if (kind == "world-disposal") ObituaryRecorder.WorldReset();
            __state = new ObituaryRecorder.Scope(kind);
        } catch (Exception ex) { ObituaryRecorder.Fault(ex); }
    }
    [HarmonyFinalizer] static void Finalizer(ObituaryRecorder.Scope __state) => ObituaryRecorder.EndScope(__state);
}
[HarmonyPatch(typeof(CrewSim), "DoLoadGame")]
internal static class ObituaryLoadPatch {
    static bool Prepare() => ObituaryRecorder.Enabled;
    [HarmonyPrefix] static void Prefix(string fileName) => ObituaryRecorder.BeginLoad(fileName);
}
