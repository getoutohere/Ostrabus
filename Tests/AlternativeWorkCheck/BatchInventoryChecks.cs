using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using Ostrabus.AlternativeWork;
using UnityEngine;

static partial class Program {
    static List<CondOwner> batchCarried, batchWorld;
    static int batchScans, batchLootReads, batchCapacity = 100;
    static bool batchThrow;
    static bool BatchShipItems(bool bSubObjects, bool bAllowDocked, bool bAllowLocked, ref List<CondOwner> __result) {
        Check(bSubObjects && bAllowDocked && !bAllowLocked, "Batch fallback changed native source permissions");
        batchScans++;
        if (batchThrow) throw new InvalidOperationException("fixture world scan failure");
        __result = new List<CondOwner>(batchWorld); return false;
    }
    static bool BatchCarriedItems(bool bAllowLocked, ref List<CondOwner> __result) {
        Check(!bAllowLocked, "Batch looked inside locked carried containers");
        __result = new List<CondOwner>(batchCarried); return false;
    }
    static bool BatchDocked(ref List<string> __result) { __result = new List<string> { "batch-ship" }; return false; }
    static bool BatchRoom(ref Room __result) { __result = null; return false; }
    static bool BatchCondition(ref bool __result) { __result = true; return false; }
    static bool BatchInteraction(string strName, ref Interaction __result) {
        __result = Empty<Interaction>(); __result.strName = strName; __result.strLootCTsGive = "floor-feed"; return false;
    }
    static bool BatchLoot(ref List<CondTrigger> __result) { batchLootReads++; __result = new List<CondTrigger> { new CondTrigger { strName = "floor" } }; return false; }
    static bool BatchNearest(List<CondOwner> aCOsHayStack, ref CondOwner __result, out List<string> aOutUseFails) {
        aOutUseFails = new List<string>(); __result = null;
        foreach (var co in aCOsHayStack) if (co.strID.StartsWith("floor", StringComparison.Ordinal)) { __result = co; break; }
        return false;
    }
    static bool BatchCapacity(List<CondOwner> aItems, ref bool __result) { __result = aItems.Count <= batchCapacity; return false; }
    static void CheckBatchInventory() {
        var fixture = new Harmony("altwork.batch.inventory.fixture");
        var oldFlag = AlternativeWorkSystem.Flag;
        var oldCOs = DataHandler.mapCOs;
        try {
            fixture.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Equality"), prefix: new HarmonyMethod(typeof(Program), nameof(Equal)));
            fixture.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Inequality"), prefix: new HarmonyMethod(typeof(Program), nameof(Unequal)));
            fixture.Patch(AccessTools.PropertyGetter(typeof(Transform), "position"), prefix: new HarmonyMethod(typeof(Program), nameof(SupplyPosition)));
            fixture.Patch(AccessTools.Method(typeof(AlternativeWorkSystem), "Eligible"), prefix: new HarmonyMethod(typeof(Program), nameof(SupplyEligible)));
            fixture.Patch(AccessTools.Method(typeof(AlternativeWorkSystem), "Allowed"), prefix: new HarmonyMethod(typeof(Program), nameof(SupplyAllowed)));
            fixture.Patch(AccessTools.Method(typeof(Ship), "GetCOs", new[] { typeof(CondTrigger), typeof(bool), typeof(bool), typeof(bool) }), prefix: new HarmonyMethod(typeof(Program), nameof(BatchShipItems)));
            fixture.Patch(AccessTools.Method(typeof(CondOwner), "GetCOsSafe"), prefix: new HarmonyMethod(typeof(Program), nameof(BatchCarriedItems)));
            fixture.Patch(AccessTools.Method(typeof(WorkManager), "GetDockedships"), prefix: new HarmonyMethod(typeof(Program), nameof(BatchDocked)));
            fixture.Patch(AccessTools.Method(typeof(Ship), "GetRoomAtWorldCoords1"), prefix: new HarmonyMethod(typeof(Program), nameof(BatchRoom)));
            fixture.Patch(AccessTools.Method(typeof(DataHandler), "GetCondTrigger", new[] { typeof(string) }), prefix: new HarmonyMethod(typeof(Program), nameof(UsTrigger)));
            fixture.Patch(AccessTools.Method(typeof(DataHandler), "GetInteraction"), prefix: new HarmonyMethod(typeof(Program), nameof(BatchInteraction)));
            fixture.Patch(AccessTools.Method(typeof(DataHandler), "GetLoot"), prefix: new HarmonyMethod(typeof(Program), nameof(InputLoot)));
            fixture.Patch(AccessTools.Method(typeof(Loot), "GetCTLootFlat"), prefix: new HarmonyMethod(typeof(Program), nameof(BatchLoot)));
            fixture.Patch(AccessTools.Method(typeof(CondOwner), "GetNearestTriggeredSync"), prefix: new HarmonyMethod(typeof(Program), nameof(BatchNearest)));
            fixture.Patch(AccessTools.Method(typeof(CondTrigger), "Triggered"), prefix: new HarmonyMethod(typeof(Program), nameof(BatchCondition)));
            fixture.Patch(AccessTools.Method(typeof(CondOwner), "CanTakeItemsSimulated"), prefix: new HarmonyMethod(typeof(Program), nameof(BatchCapacity)));
            AlternativeWorkSystem.Flag = Empty<ConfigEntry<bool>>(); MaterialModuleEnabled(true);
            DataHandler.mapCOs = new Dictionary<string, CondOwner>();
            var ship = Empty<Ship>(); ship.strRegID = "batch-ship";
            var co = SupplyCO("batch-worker", ship, 0);
            var center = SupplyCO("batch-primary", ship, 0);
            var target1 = SupplyCO("batch-target-1", ship, 1);
            var target2 = SupplyCO("batch-target-2", ship, 2);
            var primary = new Task2 { strTargetCOID = center.strID, strInteraction = "ACTFeedItemFloor" };
            var first = new Task2 { strTargetCOID = target1.strID, strInteraction = "ACTFeedItemFloor" };
            var second = new Task2 { strTargetCOID = target2.strID, strInteraction = "ACTFeedItemFloor" };
            var wm = Empty<WorkManager>();
            var active = new List<Task2>();
            Field(wm, "aTasksActive", active);
            Field(wm, "dictTasks2ByCOID", new Dictionary<string, List<Task2>> { [center.strID] = new List<Task2> { primary }, [target1.strID] = new List<Task2> { first }, [target2.strID] = new List<Task2> { second } });
            var primaryItem = SupplyCO("floor-primary", ship, 0);
            var item1 = SupplyCO("floor-1", ship, 0);
            var item2 = SupplyCO("floor-2", ship, 0);
            var excluded = new List<CondOwner> { primaryItem };
            batchCarried = new List<CondOwner> { primaryItem, item1, item2 }; batchWorld = new List<CondOwner>();
            batchScans = batchLootReads = 0;
            var result = wm.CollectBatchFeedTasks(co, primary, center, excluded);
            Check(result.Count == 2 && batchScans == 0 && batchLootReads == 2, "Fully carried native batch performed a world scan or rerolled loot");
            Check(ReferenceEquals(result[first], item1) && ReferenceEquals(result[second], item2), "Native batch reused/excluded the wrong carried sources");
            // The same source list expands mid-batch; already assigned and excluded
            // world entries must not be introduced again.
            batchCarried.Remove(item2); batchWorld.AddRange(new[] { primaryItem, item1, item2 });
            batchScans = batchLootReads = 0;
            result = wm.CollectBatchFeedTasks(co, primary, center, excluded);
            Check(result.Count == 2 && batchScans == 1 && batchLootReads == 2, "Partial carried batch did not expand exactly once without rerolling loot");
            Check(ReferenceEquals(result[first], item1) && ReferenceEquals(result[second], item2), "Fallback reused an already assigned/primary source");
            batchCarried.Clear(); batchScans = batchLootReads = 0;
            result = wm.CollectBatchFeedTasks(co, primary, center, excluded);
            Check(result.Count == 2 && batchScans == 1 && batchLootReads == 2, "World-only batch lost native fallback");
            batchWorld.Clear(); batchScans = 0;
            result = wm.CollectBatchFeedTasks(co, primary, center, excluded);
            Check(result.Count == 0 && batchScans == 1, "Unavailable materials repeatedly expanded or created false deliveries");
            batchCarried.AddRange(new[] { primaryItem, item1, item2 }); batchScans = 0; batchCapacity = 2;
            result = wm.CollectBatchFeedTasks(co, primary, center, excluded);
            Check(result.Count == 1 && batchScans == 0, "Carried fast path bypassed native capacity checks");
            batchCapacity = 100; active.Add(second);
            result = wm.CollectBatchFeedTasks(co, primary, center, excluded);
            Check(result.Count == 1 && result.ContainsKey(first), "Carried fast path took an active delivery"); active.Clear();
            MaterialModuleEnabled(false); batchScans = 0;
            result = wm.CollectBatchFeedTasks(co, primary, center, excluded);
            Check(result.Count == 2 && batchScans == 1, "Disabled altwork changed native inventory enumeration");
            MaterialModuleEnabled(true); batchCarried.Clear(); batchThrow = true;
            bool threw = false;
            try { wm.CollectBatchFeedTasks(co, primary, center, excluded); } catch (InvalidOperationException) { threw = true; }
            Check(threw && AccessTools.Field(typeof(WorkBatchInventoryPatch), "current").GetValue(null) == null, "Batch exception leaked inventory scope");
            batchThrow = false; batchCarried.AddRange(new[] { item1, item2 }); batchScans = 0;
            result = wm.CollectBatchFeedTasks(co, primary, center, excluded);
            Check(result.Count == 2 && batchScans == 0, "Failed fallback contaminated subsequent batches");
            Check(excluded.Count == 1 && batchCarried.Count == 2 && active.Count == 0, "Batch optimization mutated caller inventory/exclusions or claimed tasks");
        } finally {
            AlternativeWorkSystem.Flag = oldFlag; DataHandler.mapCOs = oldCOs;
            batchCarried = batchWorld = null; batchCapacity = 100; batchThrow = false; supplyPositions.Clear();
            fixture.UnpatchSelf();
        }
    }
    static bool SetWindowValue(ConfigEntry<int> __instance, int value) { AccessTools.Field(typeof(ConfigEntry<int>), "_typedValue").SetValue(__instance, value); return false; }
    static void CheckWindowControls() {
        var fixture = new Harmony("altwork.window.fixture");
        var old = WorkSupplyPlan.WindowConfig;
        try {
            fixture.Patch(AccessTools.PropertySetter(typeof(ConfigEntry<int>), "Value"), prefix: new HarmonyMethod(typeof(Program), nameof(SetWindowValue)));
            WorkSupplyPlan.WindowConfig = Empty<ConfigEntry<int>>();
            var command = AccessTools.Method(typeof(WorkConsolePatch), "Prefix");
            foreach (int window in new[] { 1, 4, 8, 30 }) {
                Check(!(bool)command.Invoke(null, new object[] { "altwork window " + window }), "Window command escaped mod console handling");
                Check(WorkSupplyPlan.Window == window && WorkSupplyPlan.WindowConfig.Value == window, "Window console command failed to update configuration");
            }
            foreach (string argument in new[] { "0", "31", "-4", "nonsense" }) {
                command.Invoke(null, new object[] { "altwork window " + argument });
                Check(WorkSupplyPlan.Window == 30, "Invalid window command altered planning limit");
            }
        } finally { WorkSupplyPlan.WindowConfig = old; fixture.UnpatchSelf(); }
    }
}
