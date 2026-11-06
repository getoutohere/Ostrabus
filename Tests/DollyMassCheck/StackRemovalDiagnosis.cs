using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using Ostranauts.Inventory;
using UnityEngine;
using Ostrabus.MiscBugFixes;

static partial class Program {
    static Item stackFixtureItem;
    static bool stackRegistryThrows;
    static void StackField(object obj, string name, object value) => AccessTools.Field(obj.GetType(), name).SetValue(obj, value);
    static bool StackSkip() => false;
    static bool StackShader(ref int __result) { __result = 0; return false; }
    static bool StackHas(CondOwner __instance, string strName, ref bool __result) { __result = values[__instance.strID].TryGetValue(strName, out double amount) && amount > 0; return false; }
    static bool StackItem(ref Item __result) { __result = stackFixtureItem; return false; }
    static bool StackPosition(ref Vector3 __result) { __result = default; return false; }
    static bool StackRotation(ref Quaternion __result) { __result = Quaternion.identity; return false; }
    static bool StackTransform(ref Transform __result) { __result = null; return false; }
    static bool StackVisible(ref bool __result) { __result = false; return false; }
    static bool StackSize(ref PairXY __result) { __result = new PairXY(1, 1); return false; }
    static bool StackRegister(Ship __instance, CondOwner objICO) { StackField(objICO, "_objShip", __instance); return false; }
    static bool StackParent(CondOwner __instance, CondOwner value) { StackField(__instance, "_objCOParent", value); return false; }
    static bool StackShip(CondOwner __instance, Ship value) { StackField(__instance, "_objShip", value); return false; }
    static bool StackTile(ref Tile __result) { __result = null; return false; }
    static bool StackRemoveRegistry(CondOwner objCO) { if (stackRegistryThrows) throw new InvalidOperationException("fixture removal failure"); StackField(objCO, "_objShip", null); return false; }
    static void StackPatch(Harmony h, Type type, string method, string prefix, Type[] args = null) =>
        h.Patch(AccessTools.Method(type, method, args), prefix: new HarmonyMethod(typeof(Program), prefix));
    static void CheckNativeStackRemoval() {
        var fixture = new Harmony("offline.stack.removal.mass");
        var production = new Harmony("offline.stack.mass.prevention");
        var oldSim = CrewSim.objInstance;
        object oldInventory = AccessTools.Field(typeof(CrewSim), "inventoryGUI").GetValue(null);
        try {
            StackPatch(fixture, typeof(Shader), "PropertyToID", nameof(StackShader));
            StackPatch(fixture, typeof(CondOwner), "HasCond", nameof(StackHas), new[] { typeof(string) });
            StackPatch(fixture, typeof(CondOwner), "HasCond", nameof(StackHas), new[] { typeof(string), typeof(bool) });
            StackPatch(fixture, typeof(CondOwner), "ValidateParent", nameof(StackSkip));
            StackPatch(fixture, typeof(CondOwner), "UpdateAppearance", nameof(StackSkip));
            fixture.Patch(AccessTools.PropertySetter(typeof(CondOwner), "objCOParent"), prefix: new HarmonyMethod(typeof(Program), nameof(StackParent)));
            fixture.Patch(AccessTools.PropertySetter(typeof(CondOwner), "ship"), prefix: new HarmonyMethod(typeof(Program), nameof(StackShip)));
            fixture.Patch(AccessTools.PropertyGetter(typeof(CondOwner), "Item"), prefix: new HarmonyMethod(typeof(Program), nameof(StackItem)));
            fixture.Patch(AccessTools.PropertyGetter(typeof(Transform), "position"), prefix: new HarmonyMethod(typeof(Program), nameof(StackPosition)));
            fixture.Patch(AccessTools.PropertyGetter(typeof(Transform), "parent"), prefix: new HarmonyMethod(typeof(Program), nameof(StackTransform)));
            fixture.Patch(AccessTools.PropertySetter(typeof(Transform), "localPosition"), prefix: new HarmonyMethod(typeof(Program), nameof(StackSkip)));
            fixture.Patch(AccessTools.PropertySetter(typeof(Transform), "position"), prefix: new HarmonyMethod(typeof(Program), nameof(StackSkip)));
            fixture.Patch(AccessTools.PropertyGetter(typeof(Transform), "rotation"), prefix: new HarmonyMethod(typeof(Program), nameof(StackRotation)));
            fixture.Patch(AccessTools.PropertyGetter(typeof(Quaternion), "eulerAngles"), prefix: new HarmonyMethod(typeof(Program), nameof(StackPosition)));
            StackPatch(fixture, typeof(Transform), "SetParent", nameof(StackSkip), new[] { typeof(Transform) });
            StackPatch(fixture, typeof(Transform), "SetParent", nameof(StackSkip), new[] { typeof(Transform), typeof(bool) });
            fixture.Patch(AccessTools.PropertySetter(typeof(Item), "fLastRotation"), prefix: new HarmonyMethod(typeof(Program), nameof(StackSkip)));
            StackPatch(fixture, typeof(Item), "ResetTransforms", nameof(StackSkip));
            fixture.Patch(AccessTools.PropertyGetter(typeof(CondOwner), "Visible"), prefix: new HarmonyMethod(typeof(Program), nameof(StackVisible)));
            fixture.Patch(AccessTools.PropertySetter(typeof(CondOwner), "Visible"), prefix: new HarmonyMethod(typeof(Program), nameof(StackSkip)));
            StackPatch(fixture, typeof(GUIInventoryItem), "GetWidthHeightForCO", nameof(StackSize));
            StackPatch(fixture, typeof(Container), "Redraw", nameof(StackSkip));
            StackPatch(fixture, typeof(Ship), "AddCO", nameof(StackRegister), new[] { typeof(CondOwner), typeof(bool) });
            StackPatch(fixture, typeof(Ship), "ResetMass", nameof(StackSkip));
            StackPatch(fixture, typeof(Ship), "UpdateTiles", nameof(StackSkip));
            StackPatch(fixture, typeof(CrewSim), "ShowBlocksAndLights", nameof(StackSkip), new[] { typeof(CondOwner), typeof(bool) });
            StackPatch(fixture, typeof(Ship), "GetTileAtWorldCoords1", nameof(StackTile));
            StackPatch(fixture, typeof(Ship), "RemoveInternalLot", nameof(StackSkip));
            StackPatch(fixture, typeof(Ship), "RemoveInternal", nameof(StackRemoveRegistry));
            StackPatch(fixture, typeof(CODicts), "RemoveCO", nameof(StackSkip));
            StackPatch(fixture, typeof(GUIInventory), "RemoveAndDestroy", nameof(StackSkip));
            stackFixtureItem = Empty<Item>();
            CrewSim.objInstance = Empty<CrewSim>(); CrewSim.objInstance.coDicts = Empty<CODicts>();
            AccessTools.Field(typeof(CrewSim), "inventoryGUI").SetValue(null, Empty<GUIInventory>());
            var ship = Empty<Ship>(); StackField(ship, "nLoadState", Ship.Loaded.Full);
            foreach (bool fixedMass in new[] { false, true }) {
            if (fixedMass) production.CreateClassProcessor(typeof(StackMassPatch)).Patch();
            foreach (string entry in fixedMass ? new[] { "container", "stack-head", "direct-ship" } : new[] { "container" }) {
            foreach (bool nested in new[] { false, true }) {
                var outer = CO("crate-fixture", nested ? 52 : 50);
                var parent = nested ? CO("backpack-fixture", 42) : outer;
                var head = CO("floor-fixture", 20); var member = CO("floor-fixture", 20);
                head.aStack.Add(member); member.coStackHead = head;
                head.tf = Empty<Transform>(); member.tf = Empty<Transform>();
                Contents(parent, head); StackField(member, "_objCOParent", parent);
                if (nested) Contents(outer, parent);
                foreach (var co in new[] { outer, parent, head, member }) { co.bFreezeConds = false; co.tf = Empty<Transform>(); StackField(co, "_objShip", ship); }
                // Real native RemoveFromCurrentHome -> parent.RemoveCO ->
                // Container.RemoveCO/RemoveCOSimple -> Ship.RemoveCO -> AddMass.
                // Only Unity scene/UI/registry I/O and condition scalar storage are fixtures.
                parent.objContainer.gridLayout = new GridLayout(6, 6);
                if (entry == "container") Check(ReferenceEquals(member.RemoveFromCurrentHome(), ship), "Normal inventory transfer entry did not return original ship");
                else if (entry == "stack-head") Check(ReferenceEquals(head.RemoveCO(member), member), "Stack head did not return removed member");
                else ship.RemoveCO(member);
                double expectedParent = nested ? 22 : 30;
                double expectedOuter = nested ? 32 : 30;
                Check(head.aStack.Count == 0 && parent.objContainer.Contains(head), "Native stack-member removal changed the wrong cargo");
                Check(parent.GetCondAmount("StatMass") == expectedParent - (fixedMass ? 0 : 20), "Unexpected stack removal parent mass");
                Check(outer.GetCondAmount("StatMass") == expectedOuter - (fixedMass ? 0 : 20), "Unexpected stack removal ancestor mass");
                Check(!parent.bFreezeConds && ship.LoadState == Ship.Loaded.Full, "Reproduction depends on frozen/offscreen state");
                Console.WriteLine((fixedMass ? "FIXED " : "NORMAL ") + (nested ? "NESTED BACKPACK" : "CRATE") + " via " + entry + ": removed one 20kg stack member; parent=" + parent.GetCondAmount("StatMass") + " (expected " + expectedParent + "), outer=" + outer.GetCondAmount("StatMass") + " (expected " + expectedOuter + ").");
            }
            }
            }
            // A whole loaded backpack is an important control: moving it alone is
            // not enough to reproduce the double debit found for non-head members.
            var crate = CO("crate-control", 52); var bag = CO("backpack-control", 42);
            var floor = CO("floor-control", 20); var second = CO("floor-control", 20);
            floor.aStack.Add(second); second.coStackHead = floor;
            Contents(bag, floor); StackField(second, "_objCOParent", bag); Contents(crate, bag);
            foreach (var co in new[] { crate, bag, floor, second }) { co.tf = Empty<Transform>(); StackField(co, "_objShip", ship); }
            crate.objContainer.gridLayout = new GridLayout(6, 6);
            bag.RemoveFromCurrentHome();
            Check(crate.GetCondAmount("StatMass") == 10 && bag.GetTotalMass() == 42, "Whole loaded backpack removal unexpectedly corrupts crate/bag mass");
            Console.WriteLine("WHOLE BACKPACK CONTROL: crate=10kg, removed loaded backpack=42kg; correct.");
            foreach (bool single in new[] { false, true }) {
                var parent = CO("crate-stack-control", 50); var stack = CO("floor-stack-control", 20); var member = CO("floor-stack-control", 20);
                stack.aStack.Add(member); member.coStackHead = stack; Contents(parent, stack); StackField(member, "_objCOParent", parent);
                foreach (var co in new[] { parent, stack, member }) { co.tf = Empty<Transform>(); StackField(co, "_objShip", ship); }
                parent.objContainer.gridLayout = new GridLayout(6, 6);
                if (single) stack.PopHeadFromStack(); else stack.RemoveFromCurrentHome();
                Check(parent.GetCondAmount("StatMass") == (single ? 30 : 10), "Mass fix broke native head-pop/full-stack removal");
            }
            // Frozen ordinary loading retains native no-debit behavior, and failure
            // paths must release every scope/reference before the next operation.
            foreach (bool fail in new[] { false, true }) {
                var parent = CO("crate-boundary", 50); var stack = CO("floor-boundary", 20); var member = CO("floor-boundary", 20);
                stack.aStack.Add(member); member.coStackHead = stack; Contents(parent, stack); StackField(member, "_objCOParent", parent);
                foreach (var co in new[] { parent, stack, member }) { co.tf = Empty<Transform>(); StackField(co, "_objShip", ship); }
                parent.objContainer.gridLayout = new GridLayout(6, 6); parent.bFreezeConds = !fail; stackRegistryThrows = fail;
                bool threw = false;
                try { member.RemoveFromCurrentHome(); } catch (InvalidOperationException) { threw = true; }
                Check(threw == fail, "Mass fix swallowed or introduced a removal exception");
                if (!fail) Check(parent.GetCondAmount("StatMass") == 50 && parent.bFreezeConds, "Mass fix changed frozen loading semantics");
                Check((int)AccessTools.Field(typeof(StackMassPatch), "depth").GetValue(null) == 0, "Removal scope depth leaked");
                var pendingCharges = AccessTools.Field(typeof(StackMassPatch), "charges").GetValue(null) as System.Collections.IList;
                Check(pendingCharges == null || pendingCharges.Count == 0, "Removal scope retained game references after completion/failure");
            }
        } finally {
            CrewSim.objInstance = oldSim;
            AccessTools.Field(typeof(CrewSim), "inventoryGUI").SetValue(null, oldInventory);
            stackRegistryThrows = false; stackFixtureItem = null; production.UnpatchSelf(); fixture.UnpatchSelf();
        }
    }
}
