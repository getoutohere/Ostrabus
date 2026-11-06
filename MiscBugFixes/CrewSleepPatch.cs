using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;

namespace Ostrabus.MiscBugFixes;

internal static class CrewRestPolicy {
    // Native GetMove2 can make autonomous choices while IsDrafted remains set.
    // A sleep-target filter must not inherit the sitting interruption exclusions.
    internal static bool AutonomousSleepChoice(CondOwner crew) => crew != null && crew.bAlive &&
        (crew == CrewSim.coPlayer || crew.HasCond("IsPlayerCrew")) && !crew.HasCond("IsAIManual") && !crew.HasCond("Unconscious") &&
        !crew.HasCond("IsSubdued");
    internal static bool Autonomous(CondOwner crew) => AutonomousSleepChoice(crew) &&
        !crew.HasCond("IsTempDrafted") && !crew.HasCond("IsTempDraftedLong") && !crew.HasCond("IsDrafted");
    internal static bool BedSearch(CondTrigger trigger) => trigger != null &&
        (trigger.strName == "TIsBedFree" || trigger.strName == "TIsBed" || trigger.strName == "TIsBedComfortable" || trigger.strName == "TIsBedMedical");
    internal static bool Bed(CondOwner target) => target != null && !target.HasCond("IsContainer") && !target.HasCond("IsEVASuit");
    internal static void Filter(List<CondOwner> targets) {
        if (targets == null) return;
        for (int i = targets.Count - 1; i >= 0; i--) if (!Bed(targets[i])) targets.RemoveAt(i);
    }
}

// Scope only the autonomous choice coroutine; restore the scope before every
// yield so another character or a manual action cannot inherit this policy.
[HarmonyPatch(typeof(CondOwner), "GetMove2")]
internal static class CrewSleepPatch {
    [ThreadStatic] internal static CondOwner Choosing;
    [HarmonyPostfix]
    static void Postfix(CondOwner __instance, ref IEnumerator __result) {
        if (__result != null && CrewRestPolicy.AutonomousSleepChoice(__instance)) __result = new Choice(__result, __instance);
    }
    internal sealed class Choice : IEnumerator, IDisposable {
        IEnumerator inner;
        CondOwner crew;
        internal Choice(IEnumerator inner, CondOwner crew) { this.inner = inner; this.crew = crew; }
        public object Current => inner?.Current;
        public bool MoveNext() {
            if (inner == null) return false;
            CondOwner previous = Choosing;
            Choosing = CrewRestPolicy.AutonomousSleepChoice(crew) ? crew : null;
            try { if (inner.MoveNext()) return true; Dispose(); return false; }
            finally { Choosing = previous; }
        }
        public void Reset() => throw new NotSupportedException();
        public void Dispose() { var old = inner; inner = null; crew = null; (old as IDisposable)?.Dispose(); }
    }
}

[HarmonyPatch(typeof(CondOwner), nameof(CondOwner.GetCOsSafe))]
internal static class CrewSleepInventoryPatch {
    [HarmonyPostfix]
    static void Postfix(CondTrigger objCondTrig, List<CondOwner> __result) {
        if (CrewSleepPatch.Choosing != null && CrewRestPolicy.BedSearch(objCondTrig)) CrewRestPolicy.Filter(__result);
    }
}

[HarmonyPatch(typeof(Ship), nameof(Ship.GetCosDeferred))]
internal static class CrewSleepShipPatch {
    [HarmonyPostfix]
    static void Postfix(CondTrigger objCondTrig, List<CondOwner> results, ref IEnumerator __result) {
        if (__result != null && CrewSleepPatch.Choosing != null && CrewRestPolicy.BedSearch(objCondTrig)) __result = new Search(__result, results);
    }
    internal sealed class Search : IEnumerator, IDisposable {
        IEnumerator inner;
        List<CondOwner> results;
        internal Search(IEnumerator inner, List<CondOwner> results) { this.inner = inner; this.results = results; }
        public object Current => inner?.Current;
        public bool MoveNext() {
            if (inner == null) return false;
            if (inner.MoveNext()) return true;
            if (CrewSleepPatch.Choosing != null) CrewRestPolicy.Filter(results);
            Dispose(); return false;
        }
        public void Reset() => throw new NotSupportedException();
        public void Dispose() { var old = inner; inner = null; results = null; (old as IDisposable)?.Dispose(); }
    }
}
