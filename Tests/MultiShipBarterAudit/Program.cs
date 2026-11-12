using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using HarmonyLib;
using Ostranauts.Core.Models;
using Ostranauts.Ships;
using Ostranauts.ShipGUIs.Trade;
using Ostranauts.ShipGUIs.Trade.Models;
using UnityEngine;

static partial class Program {
    static int checks, rowsCreated, warnings;
    static readonly List<Coroutine> stopped = new List<Coroutine>();
    static T Empty<T>() {
        var value = (T)FormatterServices.GetUninitializedObject(typeof(T));
        if (value is Coroutine) GC.SuppressFinalize(value); // There is no native Unity coroutine handle in this fixture.
        return value;
    }
    static void Set(object target, string field, object value) => AccessTools.Field(target.GetType(), field).SetValue(target, value);
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static bool Skip() => false;
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = ReferenceEquals(__0, __1); return false; }
    static bool Unequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = !ReferenceEquals(__0, __1); return false; }
    static bool Warning() { warnings++; return false; }
    static bool Stop(Coroutine routine) { stopped.Add(routine); return false; }
    static GUITradeRowContainer CreateRow(GUITradeRowContainer prefab, Transform parent) { rowsCreated++; return Empty<GUITradeRowContainer>(); }
    static IEnumerable<CodeInstruction> RowFactory(IEnumerable<CodeInstruction> instructions) {
        int matches = 0;
        foreach (var instruction in instructions) {
            if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(UnityEngine.Object) &&
                method.Name == "Instantiate" && method.IsGenericMethod && method.GetGenericArguments()[0] == typeof(GUITradeRowContainer)) {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(Program), nameof(CreateRow));
                matches++;
            }
            yield return instruction;
        }
        Check(matches == 1, "Row fixture must replace exactly one Unity prefab instantiation.");
    }
    static BarterZoneShip Copy(string id, bool ready = false) {
        var ship = Empty<BarterZoneShip>(); ship.strRegID = id;
        ship.json = new JsonShip { strRegID = id, aItems = Array.Empty<JsonItem>(), aZones = Array.Empty<JsonZone>() };
        Set(ship, "mapICOs", new DictionaryArray<string, CondOwner>());
        Set(ship, "_fullyLoaded", ready);
        return ship;
    }
    static void CheckRows() {
        var gui = Empty<GUITrade>();
        var rows = new Dictionary<string, GUITradeRowBase>();
        Set(gui, "_dictTradeRows", rows);
        var a = new JsonZone { strName = "Barter", strRegID = "SHIP-A" };
        var b = new JsonZone { strName = "Barter", strRegID = "SHIP-B" };
        var input = new List<TradeRowData> { new TradeRowData(a, null, null), new TradeRowData(b, null, null) };
        AccessTools.Method(typeof(GUITrade), "BuildSellList").Invoke(gui, new object[] { input });
        Check(rows.Count == 1 && rowsCreated == 1 && warnings == 1, "Native duplicate zone-name failure did not reproduce.");
        Check(rows.ContainsKey("Barter"), "Native row key is not the zone name.");
        b.strName = "Other cargo";
        rows.Clear(); rowsCreated = 0; warnings = 0;
        AccessTools.Method(typeof(GUITrade), "BuildSellList").Invoke(gui, new object[] { input });
        Check(rows.Count == 2 && rowsCreated == 2 && warnings == 0, "Distinct-name control did not show both ships.");
        Console.WriteLine("CONFIRMED: two ships with the same barter-zone name produce only one sell-list zone row; distinct names show both.");
    }
    static void CheckTargetedUnload() {
        var loader = Empty<AsyncShipLoader>();
        var a = Copy("SHIP-A"); var b = Copy("SHIP-B");
        var jobs = new List<Coroutine> { Empty<Coroutine>(), Empty<Coroutine>() };
        var loading = new Dictionary<string, IAsyncLoadable> { [a.strRegID] = a, [b.strRegID] = b };
        Set(loader, "_loadStateDict", loading); Set(loader, "_loadingCoroutine", jobs);
        loader.Unload(a.strRegID);
        Check(stopped.Count == 2 && jobs.Count == 0, "Targeted unload did not cancel both native loading jobs.");
        Check(!loading.ContainsKey(a.strRegID) && loading.ContainsKey(b.strRegID), "Native unload did not retain the unrelated copy.");
        Check(loader.IsShipLoading(b.strRegID) && !loader.GetShip(b.strRegID, out _), "Unrelated unfinished copy did not remain unavailable.");
        Console.WriteLine("CONFIRMED: unloading SHIP-A stops both loading jobs but retains unfinished SHIP-B as permanently loading.");
    }
    static void Main(string[] args) {
        if (args.Length > 0 && args[0] == "--dump-il") {
            foreach (var method in new[] { AccessTools.Method(typeof(BarterZoneShip), "SaveChangedCOs"), AccessTools.Method(typeof(AsyncShipLoader), "LoadBarterZoneShip") }) {
                Console.WriteLine(method);
                int index = 0;
                MethodInfo read = null;
                foreach (var candidate in typeof(PatchProcessor).GetMethods())
                    if (candidate.Name == "GetOriginalInstructions" && !candidate.GetParameters()[1].ParameterType.IsByRef) read = candidate;
                var instructions = (IEnumerable<CodeInstruction>)read.Invoke(null, new object[] { method, null });
                foreach (var instruction in instructions) Console.WriteLine(index++ + ": " + instruction);
            }
            return;
        }
        var engine = new Harmony("offline.multi-barter.engine");
        engine.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Equality"), prefix: new HarmonyMethod(typeof(Program), nameof(Equal)));
        engine.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Inequality"), prefix: new HarmonyMethod(typeof(Program), nameof(Unequal)));
        engine.Patch(AccessTools.Method(typeof(Debug), nameof(Debug.LogWarning), new[] { typeof(object) }), prefix: new HarmonyMethod(typeof(Program), nameof(Warning)));
        engine.Patch(AccessTools.Method(typeof(UnityEngine.Object), nameof(UnityEngine.Object.DestroyImmediate), new[] { typeof(UnityEngine.Object) }), prefix: new HarmonyMethod(typeof(Program), nameof(Skip)));
        engine.Patch(AccessTools.Method(typeof(MonoBehaviour), nameof(MonoBehaviour.StopCoroutine), new[] { typeof(Coroutine) }), prefix: new HarmonyMethod(typeof(Program), nameof(Stop)));
        engine.Patch(AccessTools.Method(typeof(GUITradeRowContainer), nameof(GUITradeRowContainer.SetCOs)), prefix: new HarmonyMethod(typeof(Program), nameof(Skip)));
        engine.Patch(AccessTools.Method(typeof(GUITrade), "BuildSellList"), transpiler: new HarmonyMethod(typeof(Program), nameof(RowFactory)));
        try {
            CheckRows(); CheckTargetedUnload(); CheckCrossShipWriteback();
            CheckRepairedRows(); CheckRepairedCancellation();
            Console.WriteLine("Multi-ship barter audit: " + checks + " native checks passed; no live game or save modified.");
        } finally { engine.UnpatchSelf(); stopped.Clear(); }
    }
}
