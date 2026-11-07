using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Ostrabus.Optimizations;
using Ostranauts.Core;
using Ostranauts.Core.Models;

static partial class Program {
    static readonly Dictionary<string, List<CondOwner>> shipContents = new Dictionary<string, List<CondOwner>>();
    static JsonGameSave capturedMain;
    static string saveFailure;
    static bool Quiet() => false;
    static bool Custom(ref string[] __result) { __result = Array.Empty<string>(); return false; }
    static bool Images(ref Dictionary<string, byte[]> __result) { __result = null; return false; }
    static bool ShipCOs(Ship __instance, ref List<CondOwner> __result) { __result = shipContents[__instance.strRegID]; return false; }
    static bool ShipJson(Ship __instance, ref JsonShip __result) { __result = __instance.json; return false; }
    static bool Lots(CondOwner __instance, ref List<CondOwner> __result) { __result = __instance.aLot.ToList(); return false; }
    static void CaptureMain(JsonGameSave main, JsonCondOwnerSave[] records) { main.aCOs = records; capturedMain = main; }
    static IEnumerable<CodeInstruction> StopAfterChangedCode(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod) {
        int result = __originalMethod.GetMethodBody().LocalVariables.Single(v => v.LocalType == typeof(SaveDto)).LocalIndex;
        var setter = AccessTools.PropertySetter(typeof(JsonGameSave), "aCOs");
        foreach (var instruction in instructions) {
            if (instruction.Calls(setter)) {
                instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(Program), nameof(CaptureMain));
                yield return instruction; yield return new CodeInstruction(OpCodes.Ldloc, result); yield return new CodeInstruction(OpCodes.Ret);
            } else yield return instruction;
        }
    }
    static Ship SaveHull(string id, Ship.Loaded load, params string[] ids) {
        var ship = Empty<Ship>(); ship.strRegID = id; Field(ship, "nLoadState", load);
        ship.json = new JsonShip { strName = id, strRegID = id, aItems = ids.Select(key => new JsonItem { strID = key }).ToArray() };
        shipContents[id] = new List<CondOwner>(); CrewSim.system.dictShips[id] = ship;
        return ship;
    }
    static void OldSave(string id, string hull) => DataHandler.dictCOSaves[id] = new JsonCondOwnerSave { strID = id, strRegIDLast = hull };
    static string FullSaveCase(bool error) {
        Reset(); shipContents.Clear(); capturedMain = null; saveFailure = error ? "explode" : null;
        CrewSim.system = Empty<StarSystem>(); CrewSim.system.dictShips = new Dictionary<string, Ship>();
        var a = SaveHull("A", Ship.Loaded.Full, "a", "lot", "layout-only", "a");
        var b = SaveHull("B", Ship.Loaded.Shallow, "b");
        var waypoint = SaveHull("WAY", Ship.Loaded.Shallow, "way"); waypoint.Classification = Ship.TypeClassification.Waypoint;
        var fault = SaveHull("FAULT", Ship.Loaded.Full, "before-error", "explode");
        var ca = CO("a", ship: a); var lot = CO("lot", ship: a); ca.aLot.Add(lot);
        shipContents["A"].AddRange(new[] { ca, CO("null-save", "null-save", a), CO("moved", ship: a) });
        var cb = CO("b", ship: b); cb.jCOS = new JsonCondOwnerSave { strID = "b", strCODef = "saved-shallow" }; shipContents["B"].Add(cb);
        shipContents["FAULT"].AddRange(new[] { CO("before-error", ship: fault), CO("explode", ship: fault) });
        foreach (string id in new[] { "a", "lot", "orphan", "layout-only", "null-save" }) OldSave(id, "A");
        foreach (string id in new[] { "b", "moved", "orphan-b" }) OldSave(id, "B");
        OldSave("way", "WAY"); OldSave("before-error", "FAULT"); OldSave("explode", "FAULT"); OldSave("orphan-fault", "FAULT");
        OldSave("missing-ship", "ABSENT"); OldSave("missing-location", null);
        CrewSim.coPlayer = ca;
        var method = AccessTools.Method(typeof(LoadManager), "SaveGameData");
        var dto = (SaveDto)method.Invoke(Empty<LoadManager>(), new object[] { "fixture/" });
        Check(capturedMain != null, "Save fixture did not reach the final trim boundary");
        Check(dto.dictShips.Count == (error ? 2 : 3), "Save fixture lost a hull or failed to exercise native per-hull catch");
        return Json(new { ships = dto.dictShips, main = capturedMain.aCOs, remaining = DataHandler.dictCOSaves.Values.ToArray(), calls = events.ToArray() });
    }
    static void LargeSaveFixture(int count) {
        Reset(); shipContents.Clear(); saveFailure = null;
        CrewSim.system = Empty<StarSystem>(); CrewSim.system.dictShips = new Dictionary<string, Ship>();
        var keys = Enumerable.Range(0, count).Select(i => "item-" + i.ToString("D8")).ToArray();
        var hull = SaveHull("LARGE", Ship.Loaded.Full, keys);
        foreach (var key in keys) OldSave(key, "LARGE");
        foreach (var key in keys.Reverse()) shipContents["LARGE"].Add(CO(key, ship: hull));
        CrewSim.coPlayer = shipContents["LARGE"][0];
    }
    static double TimedSave() {
        var target = AccessTools.MethodDelegate<Func<LoadManager, string, SaveDto>>(AccessTools.Method(typeof(LoadManager), "SaveGameData"));
        var manager = Empty<LoadManager>(); target(manager, "fixture/");
        var timer = Stopwatch.StartNew(); target(manager, "fixture/"); timer.Stop();
        Check(capturedMain.aCOs.Length == 0, "Fully handled IDs remained in main save");
        return timer.Elapsed.TotalMilliseconds;
    }
    static void RemainingChecks(Harmony production, Harmony fixture) {
        Prefix(fixture, typeof(UnityEngine.Debug), "Log", nameof(Quiet), new[] { typeof(object) });
        Prefix(fixture, typeof(UnityEngine.Debug), "LogWarning", nameof(Quiet), new[] { typeof(object) });
        Prefix(fixture, typeof(UnityEngine.Debug), "LogError", nameof(Quiet), new[] { typeof(object) });
        Prefix(fixture, typeof(CrewSim), "CustomInfosString", nameof(Custom));
        Prefix(fixture, typeof(DataHandler), "GetShipImageByteArrays", nameof(Images));
        Prefix(fixture, typeof(Ship), "GetCOs", nameof(ShipCOs), new[] { typeof(CondTrigger), typeof(bool), typeof(bool), typeof(bool) });
        Prefix(fixture, typeof(Ship), "GetJSON", nameof(ShipJson)); Prefix(fixture, typeof(CondOwner), "GetLotCOs", nameof(Lots));
        fixture.Patch(AccessTools.Method(typeof(LoadManager), "SaveGameData"), transpiler: new HarmonyMethod(typeof(Program), nameof(StopAfterChangedCode)));
        var expected = new[] { FullSaveCase(false), FullSaveCase(true) };
        LargeSaveFixture(8000); double nativeMs = TimedSave();
        production.CreateClassProcessor(typeof(SaveRemainingIdsPatch)).Patch();
        Check(FullSaveCase(false) == expected[0], "Full native save cleanup/output changed");
        Check(FullSaveCase(true) == expected[1], "Partial hull failure changed save cleanup/output");
        CrewSim.coPlayer = null;
        try {
            AccessTools.Method(typeof(LoadManager), "SaveGameData").Invoke(Empty<LoadManager>(), new object[] { "fixture/" });
            throw new Exception("Top-level save error was swallowed");
        } catch (TargetInvocationException error) { Check(error.InnerException is NullReferenceException, "Top-level save exception changed"); }
        Check(AccessTools.Field(typeof(SaveRemainingIdsPatch), "current").GetValue(null) == null, "Save finalizer left an active scope after failure");
        LargeSaveFixture(8000); double patchedMs = TimedSave();
        Console.WriteLine("Native save cleanup path, 8,000 reverse-ordered fixture COs: " + nativeMs.ToString("F2") + " ms -> " + patchedMs.ToString("F2") + " ms. Serialization leaves stubbed.");
        saveFailure = null;
        var random = new Random(415);
        for (int trial = 0; trial < 100; trial++) {
            var originals = Enumerable.Range(0, 3).Select(_ => Enumerable.Range(0, 50).Select(i => "id" + random.Next(20)).ToList()).ToArray();
            var copies = originals.Select(l => new List<string>(l)).ToArray();
            var groups = copies.Select((l, i) => (l, i)).ToDictionary(p => p.i.ToString(), p => p.l);
            var scope = SaveRemainingIdsPatch.Begin();
            try {
                for (int i = 0; i < 100; i++) {
                    int which = random.Next(3); string id = random.Next(30) == 0 ? null : "id" + random.Next(25);
                    originals[which].Remove(id); SaveRemainingIdsPatch.Remove(copies[which], id);
                }
                SaveRemainingIdsPatch.Values(groups);
                for (int i = 0; i < 3; i++) Check(originals[i].SequenceEqual(copies[i]), "Deferred removal changed duplicate/missing ID/order semantics");
            } finally { SaveRemainingIdsPatch.End(scope); }
            Check(scope.Pending == null && scope.Previous == null && scope.Counts.Count == 0, "Save scratch retained lists/IDs");
        }
        var outerIds = new List<string> { "a", "b" }; var innerIds = new List<string> { "x", "y" };
        var outer = SaveRemainingIdsPatch.Begin(); SaveRemainingIdsPatch.Remove(outerIds, "a");
        var inner = SaveRemainingIdsPatch.Begin(); SaveRemainingIdsPatch.Remove(innerIds, "x");
        SaveRemainingIdsPatch.Values(new Dictionary<string, List<string>> { ["inner"] = innerIds }); SaveRemainingIdsPatch.End(inner);
        Check(outerIds.Count == 2 && innerIds.SequenceEqual(new[] { "y" }), "Nested save disturbed outer pending removals");
        SaveRemainingIdsPatch.Values(new Dictionary<string, List<string>> { ["outer"] = outerIds }); SaveRemainingIdsPatch.End(outer);
        Check(outerIds.SequenceEqual(new[] { "b" }), "Outer save scope was not restored");
        var keys = Enumerable.Range(0, 128).Select(i => "key" + i).ToArray(); var pending = new List<string>(keys);
        var map = new Dictionary<string, List<string>> { ["hull"] = pending };
        Action cycle = () => {
            pending.Clear(); pending.AddRange(keys); var scope = SaveRemainingIdsPatch.Begin();
            foreach (string key in keys) SaveRemainingIdsPatch.Remove(pending, key);
            SaveRemainingIdsPatch.Values(map); SaveRemainingIdsPatch.End(scope);
        };
        cycle(); long start = GC.GetAllocatedBytesForCurrentThread(); for (int i = 0; i < 1000; i++) cycle();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        if (allocationCounter) Check(allocated == 0, "Warmed save bookkeeping allocates: " + allocated);
        Console.WriteLine("Warmed save scratch checked; allocation counter available: " + allocationCounter);
        var oversized = SaveRemainingIdsPatch.Begin(); oversized.Peak = SaveRemainingIdsPatch.RetainedLimit + 1; SaveRemainingIdsPatch.End(oversized);
        var next = SaveRemainingIdsPatch.Begin(); Check(!ReferenceEquals(next, oversized), "Oversized save scratch was retained"); SaveRemainingIdsPatch.End(next);
    }
}
