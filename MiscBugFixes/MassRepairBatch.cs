using System;
using System.Collections.Generic;
using HarmonyLib;

namespace Ostrabus.MiscBugFixes;

// Command-local planning only. Never retain world objects or poll inventories.
internal static class MassRepairBatch {
    internal sealed class Change {
        internal CondOwner Item;
        internal double Before, After;
        internal int Depth;
    }
    internal sealed class Result {
        internal int Containers, Anatomy, People, Skipped;
        internal readonly List<Change> Changes = new List<Change>();
        internal readonly Dictionary<string, int> Reasons = new Dictionary<string, int>();
        internal void Skip(CondOwner item, string reason) {
            Skipped++; string key = item.strCODef + ": " + reason;
            Reasons[key] = Reasons.TryGetValue(key, out int n) ? n + 1 : 1;
        }
    }
    sealed class Entry { internal CondOwner Item; internal double Delta; }
    static bool Finite(double value) => MassRepairRules.Finite(value);
    static bool Path(CondOwner co, HashSet<CondOwner> seen, out CondOwner root, out string error, bool worldOnly = false) {
        root = null; error = null; seen.Clear();
        for (; co != null; co = co.objCOParent) {
            if (seen.Count >= 128 || !seen.Add(co)) { error = "cyclic or excessive ownership depth"; return false; }
            if (co.bFreezeConds || co.bFreezeCondRules || co.bDestroyed) { error = "frozen/destroyed owner"; return false; }
            if (co.ship != null && (!MassRepair.InventoryMaterialized(co.ship.LoadState) || co.ship is Ostranauts.Ships.BarterZoneShip)) { error = "nonmaterialized owner"; return false; }
            if (worldOnly && (co.ship?.strRegID == null || CrewSim.system?.dictShips == null ||
                !CrewSim.system.dictShips.TryGetValue(co.ship.strRegID, out Ship registered) || !ReferenceEquals(registered, co.ship))) {
                error = "owner is not on a registered materialized world ship"; return false;
            }
            root = co;
        }
        return root != null;
    }
    static void SetOwnMass(CondOwner co, double value) {
        CondOwner previous = LiveMass.Refreshing;
        LiveMass.Refreshing = co;
        try { co.AddMass(value - co.GetCondAmount("StatMass"), true); co.ship?.ResetMass(); }
        finally { LiveMass.Refreshing = previous; }
        if (Math.Abs(co.GetCondAmount("StatMass") - value) > Math.Max(1e-6, Math.Abs(value) * 1e-10)) throw new InvalidOperationException("Native mass write did not reach its planned value");
    }
    internal static Result Repair(IEnumerable<CondOwner> candidates, HashSet<string> massOverrides = null, bool worldOnly = false) {
        var patch = Harmony.GetPatchInfo(AccessTools.Method(typeof(CondOwner), nameof(CondOwner.AddMass)));
        bool ready = false;
        if (patch != null) foreach (var transpiler in patch.Transpilers) if (transpiler.PatchMethod.DeclaringType == typeof(LiveMassAddPatch)) ready = true;
        if (!ready) throw new InvalidOperationException("Native mass accounting patch is unavailable; batch repair cancelled");
        var result = new Result(); var policy = new MassRepairPolicy();
        var groups = new Dictionary<CondOwner, List<Entry>>(MassRepair.Identity.Instance);
        var seen = new HashSet<CondOwner>(MassRepair.Identity.Instance);
        var unique = new HashSet<CondOwner>(MassRepair.Identity.Instance);
        foreach (var item in candidates) {
            if (item == null || !unique.Add(item)) continue;
            var kind = MassRepairPolicy.Classify(item);
            if (kind == MassRepairPolicy.Kind.None) continue;
            if (kind == MassRepairPolicy.Kind.Person) result.People++;
            else if (kind == MassRepairPolicy.Kind.Anatomy) result.Anatomy++;
            else result.Containers++;
            try {
                if (!MassRepair.Measure(item, policy, out double before, out double after, out string error, massOverrides) || !Path(item, seen, out CondOwner root, out error, worldOnly)) {
                    result.Skip(item, error); continue;
                }
                if (!groups.TryGetValue(root, out var entries)) groups[root] = entries = new List<Entry>();
                entries.Add(new Entry { Item = item, Delta = Math.Abs(after - before) < 1e-6 ? 0 : after - before });
            } catch (Exception error) { result.Skip(item, error.Message); }
        }
        foreach (var entries in groups.Values) {
            var states = new Dictionary<CondOwner, Change>(MassRepair.Identity.Instance);
            try {
                // A local residual excludes descendants' errors. Accumulating
                // these residuals up the tree gives the final total at each node.
                foreach (var entry in entries) {
                    Path(entry.Item, seen, out _, out _); int depth = seen.Count - 1;
                    for (var co = entry.Item; co != null; co = co.objCOParent, depth--) {
                        if (!states.TryGetValue(co, out var state)) {
                            double mass = co.GetCondAmount("StatMass");
                            states[co] = state = new Change { Item = co, Before = mass, After = mass, Depth = depth };
                        }
                        state.After += entry.Delta;
                    }
                }
                bool changed = false;
                foreach (var state in states.Values) {
                    if (!Finite(state.Before) || state.Before < 0 || !Finite(state.After) || state.After < 0) throw new InvalidOperationException("Invalid final mass in ownership tree");
                    if (state.Item.mapConds != null && state.Item.mapConds.TryGetValue("StatMass", out var condition) && state.After > condition.fClampMax)
                        throw new InvalidOperationException("Planned mass exceeds native condition clamp");
                    if (Math.Abs(state.After - state.Before) >= 1e-6) changed = true;
                }
                if (!changed) continue;
                var order = new List<Change>(states.Values); order.Sort((a, b) => b.Depth.CompareTo(a.Depth));
                try {
                    // Apply each planned ancestor exactly once, rather than
                    // clamping corrupt intermediate parents during propagation.
                    // AddMass still owns native labels and effective burden.
                    foreach (var state in order) SetOwnMass(state.Item, state.After);
                } catch (Exception failure) {
                    string rollback = null;
                    foreach (var state in order) try { SetOwnMass(state.Item, state.Before); } catch (Exception error) { rollback = error.Message; }
                    throw new InvalidOperationException(failure.Message + (rollback == null ? " (tree restored)" : "; restore failed: " + rollback), failure);
                }
                foreach (var state in order) if (Math.Abs(state.Item.GetCondAmount("StatMass") - state.Before) >= 1e-6) {
                    state.After = state.Item.GetCondAmount("StatMass"); result.Changes.Add(state);
                }
            } catch (Exception error) { foreach (var entry in entries) result.Skip(entry.Item, error.Message); }
        }
        return result;
    }
}
