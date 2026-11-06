using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;

namespace Ostrabus.AlternativeWork;

[HarmonyPatch(typeof(Interaction), "ValidateRequiredInputsDeferred")]
internal static class WorkCarriedInputsPatch {
    internal delegate void CountInputs(CondOwner target, List<CondTrigger> required);
    internal delegate List<CondTrigger> RemainingInputs(List<CondTrigger> required);
    internal delegate IEnumerator MatchInputs(Interaction interaction, IteratorResult<List<CondOwner>> result, List<CondTrigger> required,
        List<CondOwner> available, CondOwner crew, bool seek, string persistent, bool useTest);
    internal delegate void QueueInputs(Interaction interaction, CondOwner target, List<CondTrigger> required);
    internal delegate void PrioritizeInputs(CondOwner target, List<CondOwner> available);
    static readonly CountInputs Count = AccessTools.MethodDelegate<CountInputs>(AccessTools.Method(typeof(Interaction), "CountStoredInputs"));
    static readonly RemainingInputs Remaining = AccessTools.MethodDelegate<RemainingInputs>(AccessTools.Method(typeof(Interaction), "GetRemainingInputTriggers"));
    static readonly MatchInputs Match = AccessTools.MethodDelegate<MatchInputs>(AccessTools.Method(typeof(Interaction), "CheckItemsAvailableDeferred"));
    static readonly QueueInputs Queue = AccessTools.MethodDelegate<QueueInputs>(AccessTools.Method(typeof(Interaction), "QueueMissingInputDeliveries"));
    static readonly PrioritizeInputs Prioritize = AccessTools.MethodDelegate<PrioritizeInputs>(AccessTools.Method(typeof(Interaction), "PrioritizePersistentInput"));
    internal static void QueuePrepared(Interaction interaction, CondOwner target, List<CondTrigger> required) => Queue(interaction, target, required);
    internal static IEnumerator ProbeCarried(Interaction interaction, CondOwner crew, CondOwner target, IteratorResult<List<CondTrigger>> result) {
        var required = DataHandler.GetLoot(interaction.strLootItmInputs).GetCTLoot(null);
        Count(target, required);
        if (required.Count == 0) yield break;
        var found = new CondOwnerVisitorAddToHashSet();
        crew.VisitCOs(new CondOwnerVisitorLazyOrCondTrigger(found, Remaining(required)), bAllowLocked: false);
        if (found.aHashSet.Count == 0) yield break;
        var available = new List<CondOwner>(found.aHashSet);
        if (!string.IsNullOrEmpty(target.strPersistentCO)) {
            bool persistent = false;
            foreach (var item in available) if (item.strID == target.strPersistentCO) { persistent = true; break; }
            if (!persistent) yield break;
        }
        Prioritize(target, available);
        var matched = new IteratorResult<List<CondOwner>>();
        var steps = Match(interaction, matched, required, available, crew, false, target.strPersistentCO, false);
        try {
            while (true) {
                bool verbose = interaction.bVerboseTrigger;
                bool more;
                try { interaction.bVerboseTrigger = false; more = steps.MoveNext(); }
                finally { interaction.bVerboseTrigger = verbose; }
                if (!more) break;
                yield return steps.Current;
            }
            if (matched.Value != null) result.Value = required;
        } finally { (steps as IDisposable)?.Dispose(); }
    }
    internal static long CarriedMatches, WiderSearches;
    [HarmonyPostfix]
    static void Postfix(Interaction __instance, IteratorResult<bool> result, CondOwner objUs, CondOwner objThem, bool bStats, bool bFetchItems, ref IEnumerator __result) {
        var context = AlternativeWorkSystem.Context;
        if (!AlternativeWorkSystem.Enabled || context == null || context.VanillaCollection || context.Crew != objUs || bStats ||
            __instance.strLootItmInputs == null || objUs == null || objThem == null || __result == null) return;
        __result = Validate(__result, __instance, result, objUs, objThem, bFetchItems);
    }
    static IEnumerator Validate(IEnumerator original, Interaction interaction, IteratorResult<bool> result, CondOwner crew, CondOwner target, bool fetch) {
        try {
            var required = DataHandler.GetLoot(interaction.strLootItmInputs).GetCTLoot(null);
            Count(target, required);
            if (required.Count == 0) { result.Value = true; yield break; }
            var found = new CondOwnerVisitorAddToHashSet();
            var visitor = new CondOwnerVisitorLazyOrCondTrigger(found, Remaining(required));
            crew.VisitCOs(visitor, bAllowLocked: false);
            var carried = new List<CondOwner>(found.aHashSet);
            // Honor persistent input selection when its preferred item is outside inventory.
            bool persistentAvailable = string.IsNullOrEmpty(target.strPersistentCO);
            if (!persistentAvailable) foreach (var item in found.aHashSet)
                if (item.strID == target.strPersistentCO) { persistentAvailable = true; break; }
            if (persistentAvailable && found.aHashSet.Count > 0) {
                Prioritize(target, carried);
                var matched = new IteratorResult<List<CondOwner>>();
                var steps = Match(interaction, matched, required, new List<CondOwner>(carried), crew, false, target.strPersistentCO, false);
                try {
                    while (true) {
                        // WorkManager enables verbose failure reporting for every work order.
                        // A carried-only miss is provisional: preserve native messages for
                        // the wider search, without leaking this flag across a coroutine yield.
                        bool verbose = interaction.bVerboseTrigger;
                        bool more;
                        try { interaction.bVerboseTrigger = false; more = steps.MoveNext(); }
                        finally { interaction.bVerboseTrigger = verbose; }
                        if (!more) break;
                        yield return steps.Current;
                    }
                }
                finally { (steps as IDisposable)?.Dispose(); }
                if (matched.Value != null) {
                    CarriedMatches++;
                    // Keep the native lot-delivery contract and prerequisite counter. Having
                    // materials in a pocket does not mean the blueprint has consumed them.
                    if (fetch) Queue(interaction, target, required);
                    result.Value = !fetch;
                    yield break;
                }
            }
            WiderSearches++;
            // Continue the native wider search with the SAME rolled requirements.
            // Re-entering the original validator would roll input loot twice on a miss.
            var visit = crew.ship.VisitCOsDeferred(visitor, bSubObjects: true, bAllowDocked: true, bAllowLocked: false);
            try { while (visit.MoveNext()) yield return visit.Current; }
            finally { (visit as IDisposable)?.Dispose(); }
            var available = new List<CondOwner>(found.aHashSet);
            foreach (var item in carried) available.Remove(item);
            available.InsertRange(0, carried);
            Prioritize(target, available);
            var wider = new IteratorResult<List<CondOwner>>();
            var search = Match(interaction, wider, required, available, crew, false, target.strPersistentCO, false);
            try { while (search.MoveNext()) yield return search.Current; }
            finally { (search as IDisposable)?.Dispose(); }
            if (wider.Value != null) {
                if (fetch) Queue(interaction, target, required);
                result.Value = !fetch;
            }
        } finally { (original as IDisposable)?.Dispose(); }
    }
}

[HarmonyPatch(typeof(Interaction), "QueueMissingInputDeliveries")]
internal static class WorkMaterialPrerequisitePatch {
    [HarmonyPrefix]
    static void Prefix(CondOwner objThem) {
        var context = AlternativeWorkSystem.Context;
        if (context == null || context.VanillaCollection || objThem == null) return;
        Task2 upstream = context.Manager.taskUpstream;
        if (upstream != null && upstream.strTargetCOID == objThem.strID) context.MaterialPrerequisite = upstream;
    }
}

[HarmonyPatch(typeof(Interaction), nameof(Interaction.ApplyEffects))]
internal static class WorkMaterialDeliveryPatch {
    static readonly AccessTools.FieldRef<Interaction, CondOwner> StoredThem = AccessTools.FieldRefAccess<Interaction, CondOwner>("objThemTemp");
    internal struct State { internal CondOwner Target; internal int Count; }
    [HarmonyPrefix]
    static void Prefix(Interaction __instance, bool isCancelIa, out State __state) {
        __state = default;
        if (!AlternativeWorkSystem.Enabled || isCancelIa || !__instance.bLot ||
            __instance.strName == null || !__instance.strName.StartsWith("ACTFeedItem", System.StringComparison.Ordinal)) return;
        var target = StoredThem(__instance);
        if (target != null && target.aLot != null) __state = new State { Target = target, Count = target.aLot.Count };
    }
    [HarmonyPostfix]
    static void Postfix(State __state) {
        // CompleteTask happens before material transfer. Only release a material wait after
        // normal effects return AND a new lot item is actually present. Exceptions/cancels,
        // empty give contracts, and non-lot deliveries cannot bypass the cooldown.
        var target = __state.Target;
        if (target != null && target.aLot != null && target.aLot.Count > __state.Count)
            AlternativeWorkSystem.MaterialDelivered(target.strID);
    }
}
