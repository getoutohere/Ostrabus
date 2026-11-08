using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ostranauts.Core.Models;
using Ostrabus.MiscBugFixes;
using UnityEngine;

static partial class Program {
    static int checks, serial;
    static readonly Dictionary<string, JsonCondOwner> definitions = new Dictionary<string, JsonCondOwner>();
    static readonly Dictionary<string, Dictionary<string, double>> values = new Dictionary<string, Dictionary<string, double>>();
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = ReferenceEquals(__0, __1); return false; }
    static bool Unequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = !ReferenceEquals(__0, __1); return false; }
    static bool Definition(string strName, ref JsonCondOwner __result) { definitions.TryGetValue(strName, out __result); return false; }
    static bool Read(CondOwner __instance, string strName, ref double __result) {
        if (liveWrites && strName == "StatMass" && massConditions.TryGetValue(__instance.strID, out var condition)) __result = condition.fCount;
        else values[__instance.strID].TryGetValue(strName, out __result);
        return false;
    }
    static bool Write(CondOwner __instance, string strName, double fAmount) {
        var map = values[__instance.strID];
        if (liveWrites && strName == "StatMass") {
            if (massWriteFailureTarget == __instance.strID) { massWriteFailureTarget = null; throw new InvalidOperationException("fixture one-shot repair failure"); }
            if (throwMassWrite) throw new InvalidOperationException("fixture mass write failure");
            if (__instance.bFreezeConds) return false;
            if (!massConditions.TryGetValue(__instance.strID, out var condition)) massConditions[__instance.strID] = condition = new Condition(new JsonCond { strName = strName }) { fCount = map["StatMass"] };
            condition.AddAmount(__instance, fAmount); map[strName] = condition.fCount;
        } else map[strName] = (map.TryGetValue(strName, out double old) ? old : 0) + fAmount;
        return false;
    }
    static CondOwner CO(string def, double mass) {
        var co = Empty<CondOwner>();
        AccessTools.Field(typeof(CondOwner), "_strID").SetValue(co, "fixture-" + ++serial);
        co.strCODef = def; co.mapInfo = new Dictionary<string, string>();
        co.aStack = new TrackingCollection<CondOwner>(); co.aLot = new TrackingCollection<CondOwner>();
        values[co.strID] = new Dictionary<string, double> { ["StatMass"] = mass };
        co.mapConds = new Dictionary<string, Condition>();
        if (definitions.TryGetValue(def, out var definition) && !string.IsNullOrEmpty(definition.strContainerCT)) {
            co.mapConds["IsContainer"] = new Condition(null) { strName = "IsContainer", fCount = 1 };
            Contents(co);
        }
        return co;
    }
    static void Contents(CondOwner parent, params CondOwner[] children) {
        var container = Empty<Container>();
        AccessTools.Field(typeof(Container), "co").SetValue(container, parent);
        var list = new TrackingCollection<CondOwner>();
        foreach (var child in children) { list.Add(child); AccessTools.Field(typeof(CondOwner), "_objCOParent").SetValue(child, parent); }
        AccessTools.Field(typeof(Container), "aCOs").SetValue(container, list); parent.objContainer = container;
    }
    static bool Repair(CondOwner co) => MassRepair.TryRepair(co, out _, out _, out _);
    public static void Main(string[] args) {
        massRepairGraphPath = args.Length == 2 && args[0] == "--graph" ? args[1] : null;
        var raw = JArray.Parse(File.ReadAllText(Ostrabus.Development.InstallationPaths.Data + "condowners/condowners.json"));
        foreach (var item in raw.Where(x => x["strContainerCT"]?.Type == JTokenType.String))
            definitions[(string)item["strName"]] = new JsonCondOwner { strName = (string)item["strName"], strType = (string)item["strType"], strLoot = (string)item["strLoot"], strContainerCT = (string)item["strContainerCT"], aStartingConds = item["aStartingConds"]?.ToObject<string[]>() };
        Check(definitions.Keys.Count(x => x.StartsWith("ItmDolly")) == 4, "Did not load all installed Super/HyperHandy variants");
        Check(!MassRepair.InventoryMaterialized(Ship.Loaded.None) && !MassRepair.InventoryMaterialized(Ship.Loaded.Shallow), "Partial ship inventory accepted for repair");
        Check(MassRepair.InventoryMaterialized(Ship.Loaded.Edit) && MassRepair.InventoryMaterialized(Ship.Loaded.Full), "Complete ship inventory rejected");
        var h = new Harmony("offline.dolly.mass");
        h.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Equality"), prefix: new HarmonyMethod(typeof(Program), nameof(Equal)));
        h.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Inequality"), prefix: new HarmonyMethod(typeof(Program), nameof(Unequal)));
        h.Patch(AccessTools.Method(typeof(DataHandler), "GetCondOwnerDef"), prefix: new HarmonyMethod(typeof(Program), nameof(Definition)));
        h.Patch(AccessTools.Method(typeof(CondOwner), "GetCondAmount", new[] { typeof(string) }), prefix: new HarmonyMethod(typeof(Program), nameof(Read)));
        h.Patch(AccessTools.Method(typeof(CondOwner), "AddCondAmount"), prefix: new HarmonyMethod(typeof(Program), nameof(Write)));
        foreach (string def in definitions.Keys.Where(x => x.StartsWith("ItmDolly"))) {
            double empty = def.StartsWith("ItmDolly01") ? 103 : 153;
            var dolly = CO(def, 714);
            Check(Repair(dolly) && dolly.GetTotalMass() == empty, "Empty dolly not restored from installed definition: " + def);
            Check(!Repair(dolly), "Repeated command changed an already-correct dolly");
        }
        var loaded = CO("ItmDolly01", 999);
        var head = CO("cargo", 20); var member = CO("cargo", 30);
        head.aStack.Add(member); member.coStackHead = head;
        var nestedCrate = CO("crate", 73.5); // Preserve dynamic nested cargo mass, not its asset tare.
        Contents(loaded, head, member, nestedCrate);
        Check(Repair(loaded) && loaded.GetTotalMass() == 226.5, "Cargo stacks were omitted/double-counted or nested mass reset");
        Check(head.GetTotalMass() == 50 && nestedCrate.GetTotalMass() == 73.5, "Repair changed cargo");
        var slotted = CO("ItmDolly02", 999); var slottedCargo = CO("cargo", 25);
        Contents(slotted, slottedCargo);
        var slots = Empty<Slots>();
        var slot = new Slot(new JsonSlot { strName = "held", nItems = 1 }); slot.aCOs[0] = slottedCargo;
        var slotList = new TrackingCollection<Slot>(); slotList.Add(slot);
        AccessTools.Field(typeof(Slots), "aSlots").SetValue(slots, slotList);
        AccessTools.Field(typeof(CondOwner), "_compSlots").SetValue(slotted, slots);
        Check(Repair(slotted) && slotted.GetTotalMass() == 178, "Cargo referenced by inventory and slot was double-counted");
        var carrier = CO("human", 1200); values[carrier.strID]["IsHuman"] = 1;
        AccessTools.Field(typeof(CondOwner), "_objCOParent").SetValue(loaded, carrier);
        values[loaded.strID]["StatMass"] = 714;
        Check(Repair(loaded) && carrier.GetCondAmount("StatMass") == 712.5, "Ancestor mass did not receive exact native delta");
        Check(Math.Abs(carrier.GetCondAmount("StatEncumbrance") - 213.75) < 0.000001, "Native crew encumbrance not updated");
        var outer = CO("ItmDolly02", 1000); var inner = CO("ItmDolly01", 714);
        Contents(outer, inner);
        Check(Repair(outer) && Repair(inner) && outer.GetTotalMass() == 256, "Nested dolly repairs depend on scan order");
        var frozen = CO("ItmDolly01", 714); frozen.bFreezeConds = true;
        Check(!Repair(frozen) && frozen.GetTotalMass() == 714, "Frozen copy was mutated");
        var cycle = CO("ItmDolly01", 714); AccessTools.Field(typeof(CondOwner), "_objCOParent").SetValue(cycle, cycle);
        Check(!Repair(cycle) && cycle.GetTotalMass() == 714, "Cyclic parent graph was mutated");
        var bad = CO("ItmDolly01", 714); Contents(bad, CO("cargo", double.NaN));
        Check(!Repair(bad) && bad.GetTotalMass() == 714, "Invalid cargo caused a partial repair");
        var underweight = CO("ItmDolly01", 100.9);
        Check(Repair(underweight) && underweight.GetTotalMass() == 103, "Underweight dolly not repaired");
        var crate = CO("ItmCrate01", 714);
        Check(Repair(crate) && crate.GetTotalMass() == 10, "Fixed-mass crate was not repaired");
        Check(!Repair(CO("unrelated-item", 714)), "Non-container was repaired");
        var backpack = CO("ItmBackpack01", 714);
        Check(!Repair(backpack) && backpack.GetTotalMass() == 714, "Unreviewed definition loot was not skipped");
        Check(MassRepair.TryRepair(backpack, out _, out _, out string skip) == false && skip.Contains("loot"), "Skipped loot case has no useful reason");
        var variable = CO("ItmCrate01", 714); variable.mapConds["StatGasMolN2"] = new Condition(null);
        Check(!Repair(variable) && variable.GetTotalMass() == 714, "Variable gas contents were reset to tare");
        Check(!MassRepairRules.TryTare("Item", null, new[] { "StatMass=1.0x1-5" }, null, out _, out _), "Random mass definition accepted");
        Check(!MassRepairRules.TryTare("Item", null, new[] { "StatMass=1.0x5", "OtherStat=0.5x1|StatMass=0.5x9" }, null, out _, out _), "Mass-changing alternative definition accepted");
        Check(!MassRepairRules.TryTare("Item", null, new[] { "StatMass=1.0x5", "StatMass=1.0x7" }, null, out _, out _), "Multiple mass definitions accepted");
        Check(!MassRepairRules.TryTare("Item", null, Array.Empty<string>(), null, out _, out _), "Missing tare accepted");
        Check(!MassRepairRules.TryTare("Human", null, new[] { "StatMass=1.0x60" }, null, out _, out _), "Body mass reset to an asset default");
        var overridden = CO("ItmCrate01", 714);
        Check(!MassRepair.TryRepair(overridden, out _, out _, out string overrideReason, new HashSet<string> { overridden.strID }) && overrideReason.Contains("override"), "Explicit layout mass override was changed");
        var shallowContainer = CO("ItmCrate01", 714);
        var shallowShip = Empty<Ship>();
        AccessTools.Field(typeof(Ship), "nLoadState").SetValue(shallowShip, Ship.Loaded.Shallow);
        AccessTools.Field(typeof(CondOwner), "_objShip").SetValue(shallowContainer, shallowShip);
        Check(!MassRepair.TryRepair(shallowContainer, out _, out _, out string shallowReason) && shallowReason.Contains("instantiated") && shallowContainer.GetTotalMass() == 714, "Partial shallow inventory was treated as empty");
        CheckNativeStackRemoval();
        CheckDynamicMass();
        h.UnpatchSelf();
        Console.WriteLine("Mass bookkeeping checks passed: " + checks);
    }
}
