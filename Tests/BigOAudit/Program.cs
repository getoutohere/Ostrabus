using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Serialization;
using HarmonyLib;
using Ostranauts.Core.Models;
using Ostranauts.Ships;

// Audit harness only: no production patches or persistent game state.
static class Program {
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static readonly AccessTools.FieldRef<Ship, DictionaryArray<string, CondOwner>> Items = AccessTools.FieldRefAccess<Ship, DictionaryArray<string, CondOwner>>("mapICOs");
    static double Median(Action action) {
        action(); var samples = new double[3];
        for (int i = 0; i < samples.Length; i++) {
            var timer = Stopwatch.StartNew(); action(); timer.Stop(); samples[i] = timer.Elapsed.TotalMilliseconds;
        }
        Array.Sort(samples); return samples[1];
    }
    static void Main() {
        Console.WriteLine("method,n,nativeMedianMs,prototypeMedianMs");
        foreach (int count in new[] { 1000, 2000, 4000, 8000 }) {
            var saved = Enumerable.Range(0, count).Select(i => new JsonItem { strID = "item-" + i.ToString("D8") }).ToArray();
            Ship original = Empty<Ship>(); original.json = new JsonShip { aItems = saved };
            var copy = Empty<BarterZoneShip>(); copy.json = new JsonShip { aItems = saved };
            Items(copy) = new DictionaryArray<string, CondOwner>();
            foreach (var item in saved) {
                var co = Empty<CondOwner>(); AccessTools.Field(typeof(CondOwner), "_strID").SetValue(co, item.strID);
                Items(copy)[item.strID] = co;
            }
            // Every item already exists: no Unity transforms/serialization branches execute.
            double native = Median(() => copy.SaveChangedCOs(ref original));
            double indexed = Median(() => {
                var ids = new HashSet<string>(original.json.aItems.Select(i => i.strID), StringComparer.Ordinal);
                foreach (var co in Items(copy).Values) if (!ids.Contains(co.strID)) throw new Exception("Fixture unexpectedly requires serialization");
                // Match the unavoidable final snapshot in the native method.
                var array = new List<JsonItem>(copy.json.aItems).ToArray();
                if (array.Length != count) throw new Exception("Wrong prototype output size");
            });
            if (!original.json.aItems.Select(i => i.strID).SequenceEqual(saved.Select(i => i.strID))) throw new Exception("Native write-back changed IDs");
            Console.WriteLine("BarterZoneShip.SaveChangedCOs," + count + "," + native.ToString("F3") + "," + indexed.ToString("F3"));
            double removeByValue = Median(() => {
                var pending = new List<JsonItem>(saved);
                for (int i = pending.Count - 1; i >= 0; i--) pending.Remove(pending[i]);
            });
            double removeByIndex = Median(() => {
                var pending = new List<JsonItem>(saved);
                for (int i = pending.Count - 1; i >= 0; i--) pending.RemoveAt(i);
            });
            Console.WriteLine("MODEL:contained-list-removal," + count + "," + removeByValue.ToString("F3") + "," + removeByIndex.ToString("F3"));
            var keys = saved.Select(i => i.strID).ToArray();
            double trim = Median(() => { var remaining = new List<string>(keys); for (int i = keys.Length - 1; i >= 0; i--) remaining.Remove(keys[i]); });
            double setTrim = Median(() => { var remaining = new HashSet<string>(keys, StringComparer.Ordinal); foreach (var key in keys) remaining.Remove(key); });
            Console.WriteLine("MODEL:save-trim-reverse-order," + count + "," + trim.ToString("F3") + "," + setTrim.ToString("F3"));
        }
        // Execute the native task collector; only Unity's time-scale getter is stubbed.
        var fixture = new Harmony("ostrabus.offline.bigo");
        try {
            fixture.Patch(AccessTools.PropertyGetter(typeof(UnityEngine.Time), "timeScale"), prefix: new HarmonyMethod(typeof(Program), nameof(TimeScale)));
            var collect = AccessTools.MethodDelegate<Func<WorkManager, string, CondOwner, bool, Ostranauts.WorkManager.WorkClaimJob, List<Task2>>>(AccessTools.Method(typeof(WorkManager), "CollectTasks"));
            var demote = AccessTools.MethodDelegate<Action<WorkManager, string, Task2>>(AccessTools.Method(typeof(WorkManager), "DemoteTask"));
            var worker = Empty<CondOwner>(); AccessTools.Field(typeof(CondOwner), "_strID").SetValue(worker, "worker");
            foreach (int count in new[] { 1000, 4000, 16000 }) {
                var manager = Empty<WorkManager>();
                var tasks = Enumerable.Range(0, count).Select(i => new Task2 { fLastCheck = double.NegativeInfinity, aOwnerIDs = i < count / 2 ? null : new[] { "worker" } }).ToList();
                AccessTools.Field(typeof(WorkManager), "dictTasks2").SetValue(manager, new Dictionary<string, List<Task2>> { ["duty"] = tasks });
                AccessTools.Field(typeof(WorkManager), "_taskReservations").SetValue(manager, new Dictionary<Task2, Ostranauts.WorkManager.WorkClaimJob>());
                double native = Median(() => { if (collect(manager, "duty", worker, false, null).Count != count) throw new Exception("Collector fixture failed"); });
                double prototype = Median(() => {
                    var owned = new List<Task2>(); var allowed = new List<Task2>();
                    foreach (var task in tasks) { if (task.GetOwnership("worker") == Task2.Allowed.Owned) owned.Add(task); else allowed.Add(task); }
                    owned.AddRange(allowed);
                });
                var actual = collect(manager, "duty", worker, false, null);
                if (!actual.SequenceEqual(tasks.Where(t => t.aOwnerIDs != null).Concat(tasks.Where(t => t.aOwnerIDs == null)))) throw new Exception("Collector changed stable partition order");
                Console.WriteLine("WorkManager.CollectTasks(mixed-owner)," + count + "," + native.ToString("F3") + "," + prototype.ToString("F3"));
                var sequence = tasks.ToArray();
                double demotion = Median(() => { foreach (var task in sequence) demote(manager, "duty", task); });
                Console.WriteLine("WorkManager.DemoteTask(full-cycle)," + count + "," + demotion.ToString("F3") + ",not-tested");
            }
        } finally { fixture.UnpatchSelf(); }
        Console.WriteLine("Audit fixture assertions passed. Prototype timings are not shipped fixes or whole-frame predictions.");
    }
    static bool TimeScale(ref float __result) { __result = 1; return false; }
}
