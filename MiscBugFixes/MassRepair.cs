using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Ostrabus.MiscBugFixes;

internal static class MassRepair {
    internal sealed class Identity : IEqualityComparer<CondOwner> {
        internal static readonly Identity Instance = new Identity();
        public bool Equals(CondOwner a, CondOwner b) => ReferenceEquals(a, b);
        public int GetHashCode(CondOwner co) => RuntimeHelpers.GetHashCode(co);
    }
    static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    internal static bool InventoryMaterialized(Ship.Loaded state) => state >= Ship.Loaded.Edit;

    internal static bool TryRepair(CondOwner item, out double before, out double after, out string error, HashSet<string> massOverrides = null) {
        if (!Measure(item, new MassRepairPolicy(), out before, out after, out error, massOverrides)) return false;
        var seen = new HashSet<CondOwner>(Identity.Instance);
        double delta = after - before;
        for (CondOwner parent = item; parent != null; parent = parent.objCOParent) {
            if (seen.Count >= 128 || !seen.Add(parent) || parent.bFreezeConds) { error = "frozen or cyclic parent chain"; return false; }
            double revised = parent.GetCondAmount("StatMass") + delta;
            if (!Finite(revised) || revised < 0) { error = "invalid resulting parent mass"; return false; }
        }
        if (Math.Abs(delta) < 0.000001) return false;
        item.AddMass(delta);
        item.ship?.ResetMass();
        return true;
    }
    internal static bool Measure(CondOwner item, MassRepairPolicy policy, out double before, out double after, out string error, HashSet<string> massOverrides) {
        before = after = 0; error = null;
        if (item == null || MassRepairPolicy.Classify(item) == MassRepairPolicy.Kind.None) { error = "not a supported mass repair target"; return false; }
        if (item.bFreezeConds || item.bFreezeCondRules) { error = "frozen condition state"; return false; }
        if (item.ship != null && (!InventoryMaterialized(item.ship.LoadState) || item.ship is Ostranauts.Ships.BarterZoneShip)) { error = "ship inventory is not fully instantiated world state"; return false; }
        if (massOverrides != null && massOverrides.Contains(item.strID)) { error = "explicit layout mass override"; return false; }
        if (!policy.Tare(item, MassRepairPolicy.Classify(item), out after, out error)) return false;
        var seen = new HashSet<CondOwner>(Identity.Instance) { item };
        if (item.objContainer != null) foreach (CondOwner child in item.objContainer.ContainedCOs) {
            if (child == null || child.objCOParent != item || child.coStackHead != null || seen.Contains(child)) continue;
            if (!AddCargo(child, seen, ref after, 0)) { error = "invalid cargo mass or stack graph"; return false; }
        }
        foreach (Slot slot in item.GetSlots(bDeep: false)) {
            if (slot?.aCOs == null) continue;
            foreach (CondOwner child in slot.aCOs) {
                if (child == null || child.objCOParent != item || child.coStackHead != null || seen.Contains(child)) continue;
                if (!AddCargo(child, seen, ref after, 0)) { error = "invalid slotted cargo"; return false; }
            }
        }
        before = item.GetCondAmount("StatMass");
        if (!Finite(before) || before < 0 || !Finite(after) || after < 0) { error = "nonfinite or negative mass"; return false; }
        return true;
    }
    static bool AddCargo(CondOwner co, HashSet<CondOwner> seen, ref double total, int depth) {
        if (co == null || depth > 128 || seen.Count >= 10000 || !seen.Add(co)) return false;
        double mass = co.GetCondAmount("StatMass");
        if (!Finite(mass) || mass < 0) return false;
        total += mass; // Current mass includes nested inventory, gas, and other legitimate changes.
        if (co.aStack != null) foreach (CondOwner member in co.aStack) if (!AddCargo(member, seen, ref total, depth + 1)) return false;
        return true;
    }
    internal static void Run() {
        if (CrewSim.objInstance == null || !CrewSim.objInstance.FinishedLoading || DataHandler.mapCOs == null || CrewSim.system?.dictShips == null) {
            ConsoleToGUI.instance?.LogInfo("[MassFix] Load a game first."); return;
        }
        int materializedShips = 0;
        var massOverrides = new HashSet<string>();
        void Overrides(JsonItem[] items) {
            if (items == null) return;
            foreach (var part in items) {
                if (part?.strID == null || part.aCondOverrides == null) continue;
                foreach (var condition in part.aCondOverrides) if (condition?.CondName == "StatMass") { massOverrides.Add(part.strID); break; }
            }
        }
        foreach (Ship ship in CrewSim.system.dictShips.Values) {
            if (ship == null || !InventoryMaterialized(ship.LoadState)) continue;
            materializedShips++;
            Overrides(ship.json?.aItems); Overrides(ship.json?.aCrew); Overrides(ship.json?.aShallowPSpecs);
        }
        var candidates = new List<CondOwner>();
        foreach (CondOwner co in DataHandler.mapCOs.Values) {
            if (co == null || co.ship == null || !InventoryMaterialized(co.ship.LoadState) || MassRepairPolicy.Classify(co) == MassRepairPolicy.Kind.None ||
                !CrewSim.system.dictShips.TryGetValue(co.ship.strRegID, out Ship ship) || !ReferenceEquals(ship, co.ship)) continue;
            candidates.Add(co);
        }
        try {
            var result = MassRepairBatch.Repair(candidates, massOverrides, worldOnly: true);
            foreach (var change in result.Changes) ConsoleToGUI.instance?.LogInfo($"[MassFix] {change.Item.strCODef} {change.Item.strID} on {change.Item.ship?.strRegID}: {change.Before:F2} -> {change.After:F2} kg.");
            foreach (var reason in result.Reasons) ConsoleToGUI.instance?.LogInfo($"[MassFix] Skipped {reason.Value} x {reason.Key}.");
            ConsoleToGUI.instance?.LogInfo($"[MassFix] Examined {result.Containers} containers, {result.Anatomy} anatomical parts and {result.People} people on {materializedShips} materialized ships; corrected {result.Changes.Count} objects; skipped {result.Skipped}.");
        } catch (Exception error) { ConsoleToGUI.instance?.LogInfo("[MassFix] Cancelled: " + error.Message); }
    }
}
