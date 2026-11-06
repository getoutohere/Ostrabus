using System;
using System.Collections.Generic;
using HarmonyLib;
using Ostrabus.AlternativeWork;

static partial class Program {
    static readonly Dictionary<string, float> floorCosts = new Dictionary<string, float>();
    static bool SkipFloorRefresh(ref bool __result) { __result = true; return false; }
    static bool AllowFloorTask(ref bool __result) { __result = true; return false; }
    static bool FloorCost(CondOwner target, ref float __result) { __result = floorCosts[target.strID]; return false; }
    static void CheckFloorBatchOrdering() {
        var fixture = new Harmony("altwork.floor.fixture");
        var oldCOs = DataHandler.mapCOs;
        var oldInteractions = DataHandler.dictInteractions;
        double oldEpoch = StarSystem.fEpoch;
        try {
            fixture.Patch(AccessTools.Method(typeof(WorkTravelPlan), "Refresh"), prefix: new HarmonyMethod(typeof(Program), nameof(SkipFloorRefresh)));
            fixture.Patch(AccessTools.Method(typeof(WorkTravelPlan), "Cost"), prefix: new HarmonyMethod(typeof(Program), nameof(FloorCost)));
            fixture.Patch(AccessTools.Method(typeof(AlternativeWorkSystem), "Allowed"), prefix: new HarmonyMethod(typeof(Program), nameof(AllowFloorTask)));
            DataHandler.mapCOs = new Dictionary<string, CondOwner>();
            DataHandler.dictInteractions = new Dictionary<string, JsonInteraction>();
            DataHandler.dictInteractions["floor-install"] = new JsonInteraction { aLootItms = new[] { "Input,floor-materials,true" } };
            StarSystem.fEpoch = 1000;
            AlternativeWorkSystem.Reset();
            var co = MaterialCO("floor-worker");
            var worker = AlternativeWorkSystem.GetWorker(co);
            var pending = new List<Task2>();
            var wm = Empty<WorkManager>();
            Field(wm, "dictTasks2", new Dictionary<string, List<Task2>> { ["Construct"] = pending });
            // Open 10x10 floor area: targets within the native two-tile use range
            // require zero walking. Stub geometry only; exercise the real collector/sort.
            for (int x = 0; x < 10; x++) for (int y = 0; y < 10; y++) {
                string id = x + "," + y;
                DataHandler.mapCOs[id] = MaterialCO(id);
                double distance = Math.Sqrt((x - 4) * (x - 4) + (y - 4) * (y - 4));
                floorCosts[id] = (float)Math.Max(0, distance - 2);
                pending.Add(new Task2 { strDuty = "Construct", strTargetCOID = id, strInteraction = "floor-install" });
            }
            var readyDelivery = new Task2 { strDuty = "Construct", strTargetCOID = "4,4", strInteraction = "ACTFeedItemFloor" };
            pending.Add(readyDelivery);
            int preparedBeforeDelivery = 0;
            while (preparedBeforeDelivery <= 100) {
                var candidates = AlternativeWorkSystem.Collect(wm, "Construct", co, false, worker);
                Check(candidates.Count > 0, "Lost the already queued floor delivery");
                var next = candidates[0];
                if (ReferenceEquals(next, readyDelivery)) break;
                Check(next.strInteraction == "floor-install", "Unexpected prerequisite ordering fixture");
                // Model native DemoteTask + material failure, without running tools,
                // material search, or pathfinding. Failed parents become ineligible.
                pending.Remove(next); pending.Add(next);
                AlternativeWorkSystem.Failed(co, next, true);
                preparedBeforeDelivery++;
            }
            Check(preparedBeforeDelivery == 0, "New blueprints delayed an existing material delivery");
            DataHandler.mapCOs["5,5"].aLot.Add(MaterialCO("delivered-floor"));
            var supplied = pending.Find(task => task.strTargetCOID == "5,5");
            Check(ReferenceEquals(AlternativeWorkSystem.Collect(wm, "Construct", co, false, worker)[0], supplied), "Supplied floor delayed by delivery or unprepared blueprints");
            AlternativeWorkSystem.Failed(co, supplied);
            Check(ReferenceEquals(AlternativeWorkSystem.Collect(wm, "Construct", co, false, worker)[0], readyDelivery), "Readiness bypassed failure cooldown");
            var owned = pending.Find(task => task.strTargetCOID == "9,9");
            owned.aOwnerIDs = new[] { co.strID };
            Check(ReferenceEquals(AlternativeWorkSystem.Collect(wm, "Construct", co, false, worker)[0], owned), "Preparation priority overrode ownership");
            var blocked = pending.Find(task => task.strTargetCOID == "8,8");
            blocked.aOwnerIDs = new[] { "other-worker" };
            Check(!AlternativeWorkSystem.Collect(wm, "Construct", co, false, worker).Contains(blocked), "Preparation priority bypassed another worker's ownership");
            Check(AlternativeWorkSystem.Collect(wm, "Construct", co, true, worker).Count == 1, "Preparation priority bypassed manual-work filtering");
            Console.WriteLine("Floor batch: delivery and supplied installation selected without preparing unrelated blueprints.");
        } finally {
            AlternativeWorkSystem.Reset();
            DataHandler.mapCOs = oldCOs;
            DataHandler.dictInteractions = oldInteractions;
            StarSystem.fEpoch = oldEpoch;
            floorCosts.Clear();
            fixture.UnpatchSelf();
        }
    }
}
