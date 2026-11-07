using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using Ostrabus.Optimizations;
using Ostranauts.Core.Models;
using Ostranauts.Ships;

static class Program {
    static int checks;
    static readonly List<string> serialized = new List<string>();
    static readonly MethodInfo Native = AccessTools.Method(typeof(BarterZoneShip), "AddToLocalInstance");
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Check(bool ok, string reason) { checks++; if (!ok) throw new Exception(reason); }
    static bool Serialize(CondOwner co, ref JsonItem __result) { serialized.Add(co.strID); __result = new JsonItem { strID = co.strID, strName = co.strName }; return false; }
    static BarterZoneShip Copy(JsonItem[] saved, params string[] runtime) {
        var ship = Empty<BarterZoneShip>(); ship.json = new JsonShip { aItems = saved };
        var items = new DictionaryArray<string, CondOwner>();
        for (int i = 0; i < runtime.Length; i++) {
            var co = Empty<CondOwner>(); AccessTools.Field(typeof(CondOwner), "_strID").SetValue(co, runtime[i]); co.strName = "item-" + i;
            items[i.ToString()] = co;
        }
        AccessTools.Field(typeof(Ship), "mapICOs").SetValue(ship, items);
        return ship;
    }
    static string Signature(BarterZoneShip ship) => string.Join("|", ship.json.aItems.Select(x => x.strID + ":" + x.strName));
    static void Main() {
        var fixture = new Harmony("offline.barter.index.fixture");
        var production = new Harmony("offline.barter.index.production");
        fixture.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.GetJsonItem)), prefix: new HarmonyMethod(typeof(Program), nameof(Serialize)));
        try {
            var cases = new[] {
                (Array.Empty<JsonItem>(), Array.Empty<string>()),
                (Array.Empty<JsonItem>(), new[] { "new", "new", "other" }),
                (new[] { new JsonItem { strID = "a", strName = "original" } }, new[] { "a", "A", "a", "new" }),
                (new[] { new JsonItem { strID = "x" }, new JsonItem { strID = "x" }, new JsonItem { strID = null } }, new string[] { null, "x", "extra" }),
                (new[] { new JsonItem { strID = "backpack", strParentID = "crate" }, new JsonItem { strID = "crate" }, new JsonItem { strID = "stack", strParentID = "backpack" } }, new[] { "stack", "crate", "backpack", "spawned-loot" })
            };
            var expected = new List<(string Output, string[] Calls)>();
            foreach (var test in cases) {
                serialized.Clear(); var copy = Copy(test.Item1, test.Item2); Native.Invoke(copy, null);
                expected.Add((Signature(copy), serialized.ToArray()));
            }
            int n = 9892;
            var saved = Enumerable.Range(0, n).Select(i => new JsonItem { strID = "item-" + i.ToString("D8") }).ToArray();
            var runtime = saved.Select(i => i.strID).ToArray();
            var nativeCopy = Copy(saved, runtime);
            var timer = Stopwatch.StartNew(); Native.Invoke(nativeCopy, null); timer.Stop(); double nativeMs = timer.Elapsed.TotalMilliseconds;

            production.CreateClassProcessor(typeof(BarterLocalIndexPatch)).Patch();
            for (int i = 0; i < cases.Length; i++) {
                serialized.Clear(); var copy = Copy(cases[i].Item1, cases[i].Item2); Native.Invoke(copy, null);
                Check(Signature(copy) == expected[i].Output, "Native JSON contents/order changed in case " + i);
                Check(serialized.SequenceEqual(expected[i].Calls), "Native serialization count/order changed in case " + i);
                for (int j = 0; j < cases[i].Item1.Length; j++) Check(ReferenceEquals(copy.json.aItems[j], cases[i].Item1[j]), "Existing JSON item was replaced");
            }
            var fastCopy = Copy(saved, runtime); serialized.Clear();
            timer.Restart(); Native.Invoke(fastCopy, null); timer.Stop();
            Check(ReferenceEquals(fastCopy.json.aItems, saved) && serialized.Count == 0, "Existing items serialized or array copied");
            Check(!fastCopy.FullyLoaded, "Index publication bypassed native readiness gate");
            Console.WriteLine("9,892 existing items: native " + nativeMs.ToString("F2") + " ms; indexed " + timer.Elapsed.TotalMilliseconds.ToString("F2") + " ms. Offline method benchmark, not a live transit measurement.");
            Console.WriteLine(checks + " native/indexed barter JSON equivalence checks passed.");
        } finally { production.UnpatchSelf(); fixture.UnpatchSelf(); }
    }
}
