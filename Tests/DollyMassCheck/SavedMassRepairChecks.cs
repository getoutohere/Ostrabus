using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ostrabus.MiscBugFixes;
using Ostranauts.Core.Models;

static partial class Program {
    static void CheckSavedMassRepair(Ship ship) {
        byte[] hash = System.Security.Cryptography.SHA256.Create().ComputeHash(File.ReadAllBytes(massRepairGraphPath));
        var graph = JObject.Parse(File.ReadAllText(massRepairGraphPath));
        var conditions = CharacterAssets("conditions");
        var mapped = new Dictionary<string, CondOwner>();
        foreach (var row in graph["nodes"]) {
            Check(!(bool)row["unknownDefaults"], "Saved graph has unknown defaults");
            string id = (string)row["id"], name = (string)row["definition"];
            var state = row["values"].ToObject<Dictionary<string, double>>();
            var co = CO(name, state.TryGetValue("StatMass", out double mass) ? mass : 0);
            values[co.strID] = state; if (!state.ContainsKey("StatMass")) state["StatMass"] = 0;
            co.mapConds.Clear();
            foreach (var value in state.Where(p => p.Value > 0)) {
                var per = conditions.TryGetValue(value.Key, out var def) ? def["aPer"]?.ToObject<string[]>() : null;
                co.mapConds[value.Key] = new Condition(new JsonCond { strName = value.Key, aPer = per }) { fCount = value.Value };
            }
            StackField(co, "_objShip", ship); mapped[id] = co;
        }
        var stackMembers = new HashSet<string>();
        foreach (var row in graph["nodes"]) {
            var co = mapped[(string)row["id"]];
            if ((string)row["parent"] is string parent) StackField(co, "_objCOParent", mapped[parent]);
            foreach (string member in row["stack"].Values<string>()) { co.aStack.Add(mapped[member]); mapped[member].coStackHead = co; stackMembers.Add(member); }
        }
        // Reconstruct immediate ownership using detached slot/container records;
        // native slot propagation itself is covered by the baseline scenarios.
        foreach (var pair in mapped) {
            var co = pair.Value;
            var owned = mapped.Where(p => !stackMembers.Contains(p.Key) && ReferenceEquals(p.Value.objCOParent, co)).Select(p => p.Value).ToArray();
            if (co.objContainer != null) {
                var list = new TrackingCollection<CondOwner>(); foreach (var child in owned) list.Add(child);
                StackField(co.objContainer, "aCOs", list);
            } else if (owned.Length > 0) {
                var slots = Empty<Slots>(); var all = new TrackingCollection<Slot>();
                var slot = new Slot(new JsonSlot { strName = "saved-owned", nItems = owned.Length }); slot.aCOs = owned; slot.compSlots = slots; all.Add(slot);
                StackField(slots, "aSlots", all); StackField(co, "_compSlots", slots);
            }
        }
        var overrides = graph["nodes"].Where(r => (bool)r["massOverride"]).Select(r => mapped[(string)r["id"]].strID).ToHashSet();
        var before = graph["roots"].Values<string>().ToDictionary(id => id, id => mapped[id].GetTotalMass());
        var result = MassRepairBatch.Repair(mapped.Values, overrides);
        foreach (var root in before) Check(mapped[root.Key].GetTotalMass() > 0, "Saved character repair produced invalid mass");
        Console.WriteLine("SAVE GRAPH REPAIR: " + mapped.Count + " objects; " + result.Changes.Count + " corrections; " + result.Skipped + " conservative skips.");
        foreach (var root in before) Console.WriteLine("SAVE CHARACTER: " + root.Key + ": " + root.Value + " -> " + mapped[root.Key].GetTotalMass() + "kg.");
        foreach (var reason in result.Reasons) Console.WriteLine("SAVE SKIP: " + reason.Value + " " + reason.Key);
        var again = MassRepairBatch.Repair(mapped.Values, overrides);
        Check(again.Changes.Count == 0, "Saved graph repair was not idempotent");
        Check(System.Security.Cryptography.SHA256.Create().ComputeHash(File.ReadAllBytes(massRepairGraphPath)).SequenceEqual(hash), "Detached input graph was modified");
    }
}
