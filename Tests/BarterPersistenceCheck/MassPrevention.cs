using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Ostranauts.Core.Models;
using Ostranauts.Ships;
using Ostranauts.Trading;
using Ostrabus.MiscBugFixes;
using UnityEngine;

partial class Program {
    static bool MassDefinition(string strName, ref JsonCondOwner __result) {
        __result = new JsonCondOwner { strName = strName, aStartingConds = new[] { "StatMass=1.0x103", "KeepDefault=1.0x7" } }; return false;
    }
    static bool RegisterMassItem(Ship __instance, CondOwner objICO) { Set(objICO, "_objShip", __instance); return false; }
    static void MassPrevention() {
        var patch = new Harmony("offline.barter.mass.prevention");
        var fixture = new Harmony("offline.barter.mass.prevention.engine");
        try {
            foreach (var type in new[] { typeof(StackMassPatch), typeof(BarterMassTransferScope), typeof(BarterMassDeltaPatch), typeof(BarterMassSavePatch), typeof(BarterMassDestroyPatch),
                typeof(StackMergeMassPatch), typeof(LiveMassConditionPatch), typeof(LiveMassAddPatch), typeof(LiveMassInitializationPatch) }) {
                var bound = patch.CreateClassProcessor(type).Patch(); Check(bound != null && bound.Count > 0, "Mass prevention failed to bind " + type.Name);
            }
            fixture.Patch(AccessTools.Method(typeof(DataHandler), "GetCondOwnerDef"), prefix: new HarmonyMethod(typeof(Program), nameof(MassDefinition)));
            fixture.Patch(AccessTools.PropertySetter(typeof(CondOwner), "Visible"), prefix: new HarmonyMethod(typeof(Program), nameof(Skip)));
            fixture.Patch(AccessTools.PropertySetter(typeof(Transform), "localPosition"), prefix: new HarmonyMethod(typeof(Program), nameof(Skip)));
            fixture.Patch(AccessTools.Method(typeof(Container), "SetIsInContainer"), prefix: new HarmonyMethod(typeof(Program), nameof(ContainMassItem)));
            fixture.Patch(AccessTools.Method(typeof(Ship), "AddCO", new[] { typeof(CondOwner), typeof(bool) }), prefix: new HarmonyMethod(typeof(Program), nameof(RegisterMassItem)));
            fixture.Patch(AccessTools.Method(typeof(Item), "ResetTransforms"), prefix: new HarmonyMethod(typeof(Program), nameof(Skip)));
            // No electrical/GPM data from a temporary copy should be exported.
            foreach (bool addition in new[] { false, true }) foreach (bool frozen in new[] { false, true }) {
                masses.Clear();
                var parentItem = new JsonItem { strID = "fixed-mass-parent", strName = "ItmDolly01", aGPMSettings = new[] { new JsonGUIPropMap { strName = "Electrical", dictGUIPropMap = new[] { "outputConnections=saved-wire" } } }, aCondOverrides = new[] {
                    new DataCond { CondName = "StatMass", Amount = addition ? 103 : 714, Chance = 1 }, new DataCond { CondName = "StatDamage", Amount = 12.5, Chance = .25f }
                } };
                var cargoItem = new JsonItem { strID = "fixed-mass-cargo", strName = "fixture-cargo", strParentID = addition ? null : parentItem.strID };
                var source = new[] { parentItem, cargoItem };
                var barter = Fixture(source, source); barter.json = Json(source).Clone();
                Set(barter, "_fullyLoaded", true); Set(barter, "nLoadState", Ship.Loaded.None);
                var map = barter.GetMappedCos(); var parent = map[parentItem.strID]; var cargo = map[cargoItem.strID];
                parent.mapInfo = new Dictionary<string, string>(); parent.bFreezeConds = frozen;
                parent.mapGUIPropMaps = new Dictionary<string, Dictionary<string, string>> { ["Electrical"] = new Dictionary<string, string> { ["outputConnections"] = "" } };
                cargo.tf = Empty<Transform>(); parent.tf = Empty<Transform>();
                masses[parent.strID] = addition ? 103 : 714; masses[cargo.strID] = 611;
                var container = Contents(parent, addition ? new CondOwner[0] : new[] { cargo });
                var saved = new JsonCondOwnerSave { strID = parent.strID, strCODef = parent.strCODef, aConds = new[] { "KeepSaved=1.0x9", "StatMass=1.0x" + masses[parent.strID] } };
                DataHandler.dictCOSaves = new Dictionary<string, JsonCondOwnerSave> { [parent.strID] = saved };
                if (addition) container.AddCOSimple(cargo, PairXY.GetInvalid());
                else container.RemoveCOSimple(cargo);
                double expected = addition ? 714 : 103;
                Check(parent.GetTotalMass() == expected && parent.bFreezeConds == frozen, "Transfer mass or freeze restoration wrong");
                var original = Empty<Ship>(); original.json = Json(source).Clone();
                // Adding this existing item should not enter native new-item serialization.
                if (addition) { barter.json.aItems.Single(item => item.strID == cargo.strID).strParentID = parent.strID; }
                barter.SaveChangedCOs(ref original);
                Check(saved.aConds.Contains("StatMass=1.0x" + expected) && saved.aConds.Contains("KeepSaved=1.0x9"), "Mass-only save-back lost state or retained stale mass");
                Check(original.json.aItems.Single(item => item.strID == parent.strID).aGPMSettings.Single().dictGUIPropMap.Single() == "outputConnections=saved-wire", "Mass save-back exported temporary electrical pruning");
                var overrides = original.json.aItems.Single(item => item.strID == parent.strID).aCondOverrides;
                Check(overrides[0].Amount == expected && overrides[0].Chance == 1 && !overrides[0].NegativeValue && overrides[1].Amount == 12.5 && overrides[1].Chance == .25f, "Reload mass override remained stale or another override was changed");
                var snapshot = saved.aConds.ToArray(); barter.SaveChangedCOs(ref original);
                Check(saved.aConds.SequenceEqual(snapshot), "Repeated mass save-back accumulated adjustments");
                if (frozen) {
                    parent.AddMass(123); Check(parent.GetTotalMass() == expected && parent.bFreezeConds, "Unscoped offscreen mass simulation was unfrozen");
                }
                Console.WriteLine("PREVENTED REMOTE " + (addition ? "ADD" : "REMOVE") + " frozen=" + frozen + ": runtime and saved mass=" + expected + "; wiring preserved.");
            }
            var defaults = BarterMassChanges.Conditions(new[] { "DEFAULT", "KeepSaved=1.0x9" }, "ItmDolly01", 714);
            Check(!defaults.Contains("DEFAULT") && defaults.Contains("KeepDefault=1.0x7") && defaults.Contains("KeepSaved=1.0x9") && defaults.Last() == "StatMass=1.0x714", "DEFAULT expansion would override corrected mass or lose other conditions");
            Check(defaults.Count(entry => entry.StartsWith("StatMass=")) == 1, "Mass serialization retained competing default values");
            // Incomplete copies remain frozen. This also checks exception cleanup
            // without weakening mass updates outside actual transfer scopes.
            foreach (bool fail in new[] { false, true }) {
                masses.Clear();
                var parentItem = new JsonItem { strID = "boundary-parent", strName = "ItmDolly01" };
                var cargoItem = new JsonItem { strID = "boundary-cargo", strName = "fixture-cargo", strParentID = parentItem.strID };
                var barter = Fixture(new[] { parentItem, cargoItem }, parentItem, cargoItem);
                Set(barter, "_fullyLoaded", fail);
                var parent = barter.GetMappedCos()[parentItem.strID]; var cargo = barter.GetMappedCos()[cargoItem.strID];
                parent.mapInfo = new Dictionary<string, string>(); parent.bFreezeConds = true;
                masses[parent.strID] = 714; masses[cargo.strID] = 611;
                var container = Contents(parent, cargo);
                massWriteThrows = fail ? parent.strID : null;
                bool threw = false;
                try { container.RemoveCOSimple(cargo); } catch (InvalidOperationException) { threw = true; }
                Check(threw == fail && parent.bFreezeConds && BarterMassChanges.TransferDepth == 0, "Remote transfer failure leaked freeze/scope state or changed exception behavior");
                if (!fail) Check(parent.GetTotalMass() == 714, "Incomplete copy's reconstruction mass was changed");
                massWriteThrows = null;
            }
            // A mass delta must reach and persist through every nested ancestor.
            masses.Clear();
            var rootItem = new JsonItem { strID = "remote-root", strName = "ItmCrate01" };
            var bagItem = new JsonItem { strID = "remote-bag", strName = "ItmBackpack01", strParentID = rootItem.strID };
            var insideItem = new JsonItem { strID = "remote-inside", strName = "fixture-cargo", strParentID = bagItem.strID };
            var nestedSource = new[] { rootItem, bagItem, insideItem };
            var nested = Fixture(nestedSource, nestedSource); Set(nested, "_fullyLoaded", true);
            var mapped = nested.GetMappedCos(); var root = mapped[rootItem.strID]; var bag = mapped[bagItem.strID]; var inside = mapped[insideItem.strID];
            Contents(root, bag); var bagContents = Contents(bag, inside);
            foreach (var co in new[] { root, bag }) { co.bFreezeConds = true; co.mapInfo = new Dictionary<string, string>(); }
            masses[root.strID] = 52; masses[bag.strID] = 42; masses[inside.strID] = 40;
            DataHandler.dictCOSaves = new Dictionary<string, JsonCondOwnerSave> {
                [root.strID] = new JsonCondOwnerSave { strID = root.strID, aConds = new[] { "StatMass=1.0x52" } },
                [bag.strID] = new JsonCondOwnerSave { strID = bag.strID, aConds = new[] { "StatMass=1.0x42" } }
            };
            bagContents.RemoveCOSimple(inside);
            var originalNested = Empty<Ship>(); originalNested.json = Json(nestedSource).Clone(); nested.SaveChangedCOs(ref originalNested);
            Check(root.GetTotalMass() == 12 && bag.GetTotalMass() == 2 && root.bFreezeConds && bag.bFreezeConds, "Remote nested ancestors did not update/restore correctly");
            Check(DataHandler.dictCOSaves[root.strID].aConds.Single() == "StatMass=1.0x12" && DataHandler.dictCOSaves[bag.strID].aConds.Single() == "StatMass=1.0x2", "Remote nested ancestor masses were not saved");
            // Both fixes must compose with the existing Ship -> BarterZoneShip
            // dispatch correction, including a non-head member removal.
            stackItem = Empty<Item>();
            foreach (bool viaHead in new[] { false, true }) {
                masses.Clear();
                var parentItem = new JsonItem { strID = "remote-stack-parent", strName = "ItmDolly01" };
                var headItem = new JsonItem { strID = "remote-stack-head", strName = "fixture-floor", strParentID = parentItem.strID };
                var memberItem = new JsonItem { strID = "remote-stack-member", strName = "fixture-floor", strParentID = parentItem.strID };
                var source = new[] { parentItem, headItem, memberItem };
                var barter = Fixture(source, source); Set(barter, "_fullyLoaded", true);
                var entries = barter.GetMappedCos(); var parent = entries[parentItem.strID]; var head = entries[headItem.strID]; var member = entries[memberItem.strID];
                parent.mapInfo = new Dictionary<string, string>(); parent.bFreezeConds = true;
                head.aStack.Add(member); member.coStackHead = head; var container = Contents(parent, head); Set(member, "_objCOParent", parent);
                masses[parent.strID] = 143; masses[head.strID] = masses[member.strID] = 20;
                DataHandler.dictCOSaves = new Dictionary<string, JsonCondOwnerSave> { [parent.strID] = new JsonCondOwnerSave { strID = parent.strID, aConds = new[] { "StatMass=1.0x143" } } };
                if (viaHead) head.RemoveCO(member); else container.RemoveCOSimple(member);
                var original = Empty<Ship>(); original.json = Json(source).Clone(); barter.SaveChangedCOs(ref original);
                Check(parent.GetTotalMass() == 123 && parent.bFreezeConds, "Remote stack removal applied wrong mass or left conditions unfrozen");
                Check(!original.json.aItems.Any(item => item.strID == member.strID) && head.aStack.Count == 0, "Remote stack fix regressed item removal persistence");
                Check(DataHandler.dictCOSaves[parent.strID].aConds.Single() == "StatMass=1.0x123", "Remote stack mass was not persisted");
            }
            Check(BarterMassChanges.TransferDepth == 0, "Mass transfer scope leaked after operations");
        } finally { stackItem = null; massWriteThrows = null; patch.UnpatchSelf(); fixture.UnpatchSelf(); }
    }
    static bool ContainMassItem(Container __instance, CondOwner co) { Set(co, "_objCOParent", __instance.CO); return false; }
}
