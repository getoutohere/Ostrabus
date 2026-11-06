using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Ostranauts.Ships;
using UnityEngine;

namespace Ostrabus.MiscBugFixes;

internal static class BarterMassChanges {
    // Weak copy keys; values contain scalar masses and IDs, never game references.
    static readonly ConditionalWeakTable<BarterZoneShip, Dictionary<string, double>> dirty = new ConditionalWeakTable<BarterZoneShip, Dictionary<string, double>>();
    [ThreadStatic] internal static int TransferDepth;
    internal static void Record(BarterZoneShip ship, CondOwner co, double mass) {
        if (!string.IsNullOrEmpty(co.strID) && !double.IsNaN(mass) && !double.IsInfinity(mass) && mass >= 0) dirty.GetOrCreateValue(ship)[co.strID] = mass;
    }
    internal static void Forget(BarterZoneShip ship) { dirty.Remove(ship); }
    internal static void Save(BarterZoneShip ship, Ship original) {
        if (!ship.FullyLoaded || original?.json?.aItems == null || !dirty.TryGetValue(ship, out var changes) || changes.Count == 0) return;
        var items = new Dictionary<string, JsonItem>();
        foreach (var item in original.json.aItems) if (item?.strID != null) items[item.strID] = item;
        var mapped = ship.GetMappedCos();
        foreach (var change in changes) {
            if (!items.TryGetValue(change.Key, out JsonItem item) || !mapped.ContainsKey(change.Key)) continue; // Sold/removed parents are no longer on this hull.
            if (DataHandler.dictCOSaves == null || !DataHandler.dictCOSaves.TryGetValue(change.Key, out JsonCondOwnerSave saved) || saved == null) {
                Debug.LogWarning("[BarterMass] Missing saved CO for changed mass: " + change.Key); continue;
            }
            string[] updated = Conditions(saved.aConds, saved.strCODef ?? mapped[change.Key].strCODef, change.Value);
            if (updated == null) { Debug.LogWarning("[BarterMass] Cannot expand saved defaults for " + change.Key); continue; }
            saved.aConds = updated;
            // Native full saves replace layout mass overrides with current state.
            // Keep any explicit override consistent without exporting other fields.
            if (item.aCondOverrides != null) foreach (var condition in item.aCondOverrides) if (condition?.CondName == "StatMass") {
                condition.Amount = change.Value; condition.Chance = 1; condition.NegativeValue = false;
            }
        }
        changes.Clear();
    }
    static bool IsMass(string equation) => equation != null && (equation == "StatMass" || equation == "-StatMass" || equation.StartsWith("StatMass=", StringComparison.Ordinal) || equation.StartsWith("-StatMass=", StringComparison.Ordinal));
    internal static string[] Conditions(string[] saved, string definition, double mass) {
        var result = new List<string>();
        bool defaults = false;
        if (saved != null) foreach (string equation in saved) {
            if (equation == "DEFAULT") defaults = true;
            else if (!IsMass(equation)) result.Add(equation);
        }
        // Native loading appends DEFAULT definitions AFTER explicit conditions.
        // Expand without rolling loot, then put current mass last so defaults
        // cannot overwrite it. All non-mass conditions remain native expressions.
        if (defaults) {
            var source = DataHandler.GetCondOwnerDef(definition);
            if (source?.aStartingConds == null) return null;
            foreach (string equation in source.aStartingConds) if (!IsMass(equation)) result.Add(equation);
        }
        result.Add("StatMass=1.0x" + mass.ToString("R", CultureInfo.InvariantCulture));
        return result.ToArray();
    }
}

// Permit only real transfer-time mass bookkeeping, not general offscreen
// simulation or reconstruction. Keep bFreezeConds true outside AddMass itself.
[HarmonyPatch]
internal static class BarterMassTransferScope {
    internal struct State { internal bool Started; }
    static IEnumerable<MethodBase> TargetMethods() {
        yield return AccessTools.Method(typeof(Container), nameof(Container.AddCOSimple));
        yield return AccessTools.Method(typeof(Container), nameof(Container.RemoveCOSimple));
        yield return AccessTools.Method(typeof(CondOwner), nameof(CondOwner.RemoveCO));
        yield return AccessTools.Method(typeof(Ship), nameof(Ship.RemoveCO), new[] { typeof(CondOwner), typeof(bool) });
        foreach (var method in AccessTools.GetDeclaredMethods(typeof(Slots))) if (method.Name == "SlotItem" || method.Name == "UnSlotItem") yield return method;
    }
    [HarmonyPrefix]
    static void Prefix(out State __state) { BarterMassChanges.TransferDepth++; __state = new State { Started = true }; }
    [HarmonyFinalizer]
    static void Finalizer(State __state) { if (__state.Started) BarterMassChanges.TransferDepth--; }
}

[HarmonyPatch(typeof(CondOwner), nameof(CondOwner.AddMass))]
internal static class BarterMassDeltaPatch {
    internal struct State { internal bool Thawed; internal BarterZoneShip Ship; internal double Before; }
    [HarmonyPrefix]
    static void Prefix(CondOwner __instance, out State __state) {
        __state = default;
        if (BarterMassChanges.TransferDepth <= 0 || __instance.ship is not BarterZoneShip ship || !ship.FullyLoaded) return;
        __state = new State { Ship = ship, Before = __instance.GetCondAmount("StatMass"), Thawed = __instance.bFreezeConds };
        __instance.bFreezeConds = false;
    }
    [HarmonyPostfix]
    static void Postfix(CondOwner __instance, State __state) {
        if (__state.Ship == null) return;
        double after = __instance.GetCondAmount("StatMass");
        if (after != __state.Before) BarterMassChanges.Record(__state.Ship, __instance, after);
    }
    [HarmonyFinalizer]
    static void Finalizer(CondOwner __instance, State __state) { if (__state.Thawed) __instance.bFreezeConds = true; }
}

[HarmonyPatch(typeof(BarterZoneShip), nameof(BarterZoneShip.SaveChangedCOs))]
internal static class BarterMassSavePatch {
    [HarmonyPostfix]
    static void Postfix(BarterZoneShip __instance, Ship originalShip) { BarterMassChanges.Save(__instance, originalShip); }
}

[HarmonyPatch(typeof(BarterZoneShip), nameof(BarterZoneShip.Destroy), new[] { typeof(bool) })]
internal static class BarterMassDestroyPatch {
    [HarmonyPrefix]
    static void Prefix(BarterZoneShip __instance) { BarterMassChanges.Forget(__instance); }
}
