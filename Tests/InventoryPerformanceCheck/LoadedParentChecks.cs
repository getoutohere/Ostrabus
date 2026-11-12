using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ostrabus.MiscBugFixes;

static partial class Program {
    static JsonItem Record(string id, string parent = null, string type = null, bool slot = false) => new JsonItem {
        strID = id, strName = type, strParentID = slot ? null : parent,
        strSlotParentID = slot ? parent : null, bForceLoad = true
    };

    static void LoadedParentChecks(Harmony production) {
        var chain = new[] {
            Record("suit", type: "destroyed"), Record("pocket", "suit"), Record("tool", "pocket"),
            Record("destroyed-leaf", type: "destroyed"), Record("good-leaf"), Record("unresolved", "missing")
        };
        string baseline = LoadCase(chain, false);
        Check(!events.Contains("create:suit") && !events.Contains("create:tool"), "Native parent-skipping reproduction changed.");
        string source = Json(chain);
        production.CreateClassProcessor(typeof(LoadedInventoryParentPatch)).Patch();
        LoadCase(chain, false);
        Check(events.Contains("create:suit") && events.Contains("create:pocket") && events.Contains("create:tool"), "Destroyed parent's saved contents were not reconstructed.");
        Check(!events.Contains("create:destroyed-leaf") && events.Contains("create:good-leaf"), "Leaf damage handling changed.");
        Check(!events.Contains("create:unresolved"), "Missing-parent items were invented or relocated.");
        Check(Json(chain) == source, "Loader changed caller-owned saved records.");
        Check(LoadCase(chain, false, template: true) == baseline, "Template loot generation changed.");
        LoadCase(new[] { Record("slot-parent", type: "destroyed-slots"), Record("battery", "slot-parent", slot: true) }, false);
        Check(events.Contains("slot:battery"), "Slotted contents were not restored through native slotting.");
        LoadCase(new[] { Record("root"), Record("destroyed-child", "root", "destroyed"), Record("grandchild", "destroyed-child") }, false);
        Check(events.Contains("create:destroyed-child") && events.Contains("contained:grandchild"), "Damaged nested parent still skipped descendants.");

        DataHandler.dictCOSaves.Clear();
        var outerRecords = new List<JsonItem> { Record("outer-child", "outer") };
        DataHandler.dictCOSaves["outer-child"] = new JsonCondOwnerSave();
        LoadedInventoryParentPatch.Prefix(outerRecords, false, out var outer);
        Check(LoadedInventoryParentPatch.KeepParent(Record("outer")), "Outer restoration scope not active.");
        LoadedInventoryParentPatch.Prefix(outerRecords, true, out var nested);
        Check(!LoadedInventoryParentPatch.KeepParent(Record("outer")), "Nested template inherited outer restoration scope.");
        LoadedInventoryParentPatch.Finalizer(nested);
        Check(LoadedInventoryParentPatch.KeepParent(Record("outer")), "Nested return lost outer scope.");
        LoadedInventoryParentPatch.Finalizer(outer);
        Check(outer.Parents.Count == 0 && outer.Previous == null, "Scope retained saved IDs or outer references.");
        Check(!LoadedInventoryParentPatch.KeepParent(Record("outer")), "Restoration scope leaked beyond loading.");
        DataHandler.dictCOSaves.Clear();
        LoadedInventoryParentPatch.Prefix(outerRecords, false, out var missing);
        Check(!LoadedInventoryParentPatch.KeepParent(Record("outer")), "Missing-save child retained a destroyed parent.");
        LoadedInventoryParentPatch.Finalizer(missing);
        try {
            LoadCase(new[] { Record("parent", type: "destroyed"), Record("child", "parent"), Record("throw-create") }, false);
            throw new Exception("Expected fixture exception did not occur.");
        } catch (System.Reflection.TargetInvocationException) {
            Check(!LoadedInventoryParentPatch.KeepParent(Record("parent")), "Exception leaked restoration scope.");
        }

        // Optional local evidence: actual dependency IDs from the read-only save
        // audit, with engine creation/slotting controlled by existing fixtures.
        string reportPath = "obj/ConsoleIssues/inventory-newship.json";
        if (File.Exists(reportPath)) {
            var report = JObject.Parse(File.ReadAllText(reportPath));
            int restored = 0;
            foreach (var row in report["rows"]) {
                var records = new Dictionary<string, JsonItem>();
                foreach (var unresolved in row["unresolved"]) {
                    var ancestors = unresolved["ancestors"].ToArray();
                    for (int i = 0; i < ancestors.Length; i++) {
                        string id = (string)ancestors[i]["Id"];
                        string parent = i + 1 < ancestors.Length ? (string)ancestors[i + 1]["Id"] : null;
                        records[id] = Record(id, parent, i == ancestors.Length - 1 ? "destroyed" : (string)ancestors[i]["Name"]);
                    }
                }
                LoadCase(records.Values.ToArray(), false);
                foreach (var unresolved in row["unresolved"]) {
                    Check(events.Contains("create:" + (string)unresolved["Id"]), "Real saved dependency was still omitted.");
                    restored++;
                }
            }
            Console.WriteLine("Restored {0} previously unresolved saved dependencies in native loader fixtures.", restored);
        }
    }
}
