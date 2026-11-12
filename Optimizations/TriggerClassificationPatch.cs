using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace Ostrabus.Optimizations;

// This is search metadata, not a replacement for CondTrigger.Triggered.
// A false result keeps the general search; an unjustified true can hide targets.
[HarmonyPatch(typeof(CondTrigger), "CheckRequiresHumans")]
internal static class TriggerClassificationPatch {
    enum Result : byte { Visiting, General, People }

    sealed class ReferenceComparer : IEqualityComparer<CondTrigger> {
        public bool Equals(CondTrigger x, CondTrigger y) => ReferenceEquals(x, y);
        public int GetHashCode(CondTrigger value) => RuntimeHelpers.GetHashCode(value);
    }

    static readonly AccessTools.FieldRef<CondTrigger, Dictionary<string, CondTrigger>> Children =
        AccessTools.FieldRefAccess<CondTrigger, Dictionary<string, CondTrigger>>("aTriggersConds");
    [ThreadStatic] static Dictionary<CondTrigger, Result> scratch;

    static bool IsPersonCondition(string condition) => condition == "IsHuman" || condition == "IsPlayer";

    internal static bool Classify(CondTrigger trigger) {
        var results = scratch ??= new Dictionary<CondTrigger, Result>(new ReferenceComparer());
        try {
            return RequiresPeople(trigger, results, 0);
        } finally {
            // Scratch capacity is reused, but no game object remains referenced.
            results.Clear();
        }
    }

    static bool RequiresPeople(CondTrigger trigger, Dictionary<CondTrigger, Result> results, int depth) {
        // Negation, unresolved links, cycles and excessive nesting retain the broad
        // search. They never authorize excluding ordinary objects.
        if (trigger == null || trigger.bNOT || depth >= 128) return false;
        if (results.TryGetValue(trigger, out var known)) return known == Result.People;
        results[trigger] = Result.Visiting;
        bool people = TestPositiveRequirements(trigger, results, depth);
        results[trigger] = people ? Result.People : Result.General;
        return people;
    }

    static bool TestPositiveRequirements(CondTrigger trigger, Dictionary<CondTrigger, Result> results, int depth) {
        var requirements = trigger.aReqs ?? Array.Empty<string>();
        var links = trigger.aTriggers ?? Array.Empty<string>();
        var children = Children(trigger);
        if (trigger.bAND) {
            foreach (string condition in requirements) if (IsPersonCondition(condition)) return true;
            foreach (string link in links) {
                if (children != null && children.TryGetValue(link, out var child) && RequiresPeople(child, results, depth + 1)) return true;
            }
            return false;
        }

        // OR requires every alternative to imply a person. Numeric comparisons
        // form another alternative in the native evaluator, so keep those broad.
        if (requirements.Length + links.Length == 0 || trigger.strHigherCond != null) return false;
        foreach (string condition in requirements) if (!IsPersonCondition(condition)) return false;
        foreach (string link in links) {
            if (children == null || !children.TryGetValue(link, out var child) || !RequiresPeople(child, results, depth + 1)) return false;
        }
        return true;
    }

    static bool Prefix(CondTrigger __instance, ref bool __result) {
        __result = Classify(__instance);
        return false;
    }
}
