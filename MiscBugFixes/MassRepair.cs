using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Ostrabus.MiscBugFixes;

internal static class MassRepair {
    sealed class Identity : IEqualityComparer<CondOwner> {
        internal static readonly Identity Instance = new Identity();
        public bool Equals(CondOwner a, CondOwner b) => ReferenceEquals(a, b);
        public int GetHashCode(CondOwner co) => RuntimeHelpers.GetHashCode(co);
    }
    static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    internal static bool InventoryMaterialized(Ship.Loaded state) => state >= Ship.Loaded.Edit;

    internal static bool TryRepair(CondOwner item, out double before, out double after, out string error, HashSet<string> massOverrides = null) {
        before = after = 0; error = null;
        if (item == null || item.objContainer == null || !item.HasCond("IsContainer")) { error = "not a labeled inventory container"; return false; }
        if (item.ship != null && !InventoryMaterialized(item.ship.LoadState)) { error = "ship inventory is not fully instantiated"; return false; }
        if (massOverrides != null && massOverrides.Contains(item.strID)) { error = "explicit layout mass override"; return false; }
        var definition = DataHandler.GetCondOwnerDef(item.strCODef);
        if (definition == null || definition.strName != item.strCODef) { error = "missing definition or unaudited overlay"; return false; }
        if (!MassRepairRules.TryTare(definition?.strType, definition?.strLoot, definition?.aStartingConds, item.mapConds?.Keys, out after, out error)) return false;
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
        if (!Finite(before) || !Finite(after)) { error = "nonfinite mass"; return false; }
        double delta = after - before;
        seen.Clear();
        for (CondOwner parent = item; parent != null; parent = parent.objCOParent) {
            if (seen.Count >= 128 || !seen.Add(parent) || parent.bFreezeConds) { error = "frozen or cyclic parent chain"; return false; }
            double revised = parent.GetCondAmount("StatMass") + delta;
            if (!Finite(revised) || revised < 0) { error = "invalid resulting parent mass"; return false; }
        }
        if (Math.Abs(delta) < 0.000001) return false;
        item.AddMass(delta); // Native ancestor mass/crew encumbrance bookkeeping, not a raw condition reset.
        item.ship?.ResetMass();
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
        int materializedShips = 0, examined = 0, repaired = 0, skipped = 0;
        var reasons = new Dictionary<string, int>();
        var massOverrides = new HashSet<string>();
        foreach (Ship ship in CrewSim.system.dictShips.Values) {
            if (ship == null || !InventoryMaterialized(ship.LoadState)) continue;
            materializedShips++;
            if (ship.json?.aItems == null) continue;
            foreach (JsonItem part in ship.json.aItems) {
                if (part?.strID == null || part.aCondOverrides == null) continue;
                foreach (var condition in part.aCondOverrides) if (condition?.CondName == "StatMass") { massOverrides.Add(part.strID); break; }
            }
        }
        foreach (CondOwner co in DataHandler.mapCOs.Values) {
            if (co == null || co.objContainer == null || !co.HasCond("IsContainer") || co.ship == null || !InventoryMaterialized(co.ship.LoadState) ||
                !CrewSim.system.dictShips.TryGetValue(co.ship.strRegID, out Ship ship) || !ReferenceEquals(ship, co.ship)) continue;
            examined++;
            try {
                if (TryRepair(co, out double before, out double after, out string error, massOverrides)) {
                    repaired++;
                    ConsoleToGUI.instance?.LogInfo($"[MassFix] {co.strCODef} {co.strID} on {ship.strRegID}: {before:F2} -> {after:F2} kg.");
                } else if (error != null) {
                    skipped++;
                    string reason = co.strCODef + ": " + error;
                    reasons[reason] = reasons.TryGetValue(reason, out int count) ? count + 1 : 1;
                }
            } catch (Exception error) {
                skipped++;
                ConsoleToGUI.instance?.LogInfo($"[MassFix] Skipped {co.strID}: {error.Message}.");
            }
        }
        foreach (var reason in reasons) ConsoleToGUI.instance?.LogInfo($"[MassFix] Skipped {reason.Value} x {reason.Key}.");
        ConsoleToGUI.instance?.LogInfo($"[MassFix] Examined {examined} containers on {materializedShips} materialized ships; corrected {repaired}; skipped {skipped}.");
    }
}
