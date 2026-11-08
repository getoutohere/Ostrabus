using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ostrabus.MiscBugFixes;
using Ostranauts.Core.Models;
using UnityEngine;
using SolarSystem;

static partial class Program {
    static string massWriteFailureTarget;
    static string massRepairGraphPath;
    static void CorruptMass(CondOwner co, double mass) {
        values[co.strID]["StatMass"] = mass;
        if (massConditions.TryGetValue(co.strID, out var condition)) condition.fCount = mass;
    }
    static void CheckCharacterRepair(CondOwner actor, List<CondOwner> parts, CondOwner held, string trait,
        Dictionary<string, JObject> defs, Dictionary<string, JObject> loots) {
        var savedDefinitions = new Dictionary<string, JsonCondOwner>(definitions);
        var oldOverlays = DataHandler.dictCOOverlays; var oldLoot = DataHandler.dictLoot;
        try {
            foreach (var def in defs) definitions[def.Key] = def.Value.ToObject<JsonCondOwner>();
            DataHandler.dictCOOverlays = CharacterAssets("cooverlays").ToDictionary(p => p.Key, p => p.Value.ToObject<JsonCOOverlay>());
            foreach (var pair in DataHandler.dictCOOverlays) if (definitions.TryGetValue(pair.Value.strCOBase, out var basis)) definitions[pair.Key] = basis;
            DataHandler.dictLoot = loots.ToDictionary(p => p.Key, p => Loot.CloneFromJSON(p.Value.ToObject<JsonLoot>()));
            string traitName = trait == "CONDStrongPer" ? "IsStrong" : trait == "CONDFeeblePer" ? "IsFeeble" : null;
            if (traitName != null) {
                values[actor.strID][traitName] = 1;
                actor.mapConds[traitName] = new Condition(new JsonCond { strName = traitName, aPer = new[] { trait } }) { fCount = 1 };
            }
            var outer = CO("ItmCrate01", 999); var inner = CO("ItmCrate01", 714);
            var head = CO("preserved-cargo", 20); var member = CO("preserved-cargo", 30);
            head.aStack.Add(member); member.coStackHead = head;
            Contents(inner, head); StackField(member, "_objCOParent", inner); Contents(outer, inner);
            values[outer.strID]["IsContainer"] = values[inner.strID]["IsContainer"] = 1;
            var right = parts.First(p => p.strCODef == "BodyhandRA");
            Slot primary = right.compSlots.GetSlot("heldR"); primary.aCOs[0] = outer; outer.slotNow = primary;
            StackField(outer, "_objCOParent", right);
            foreach (var co in new[] { outer, inner, head, member }) { co.tf = Empty<Transform>(); StackField(co, "_objShip", actor.ship); }
            // A secondary alias on the same hand must not count the crate twice.
            var alias = new Slot(new JsonSlot { strName = "alias", nItems = 1 }); alias.aCOs[0] = outer;
            ((TrackingCollection<Slot>)AccessTools.Field(typeof(Slots), "aSlots").GetValue(right.compSlots)).Add(alias);
            var targets = new List<CondOwner> { actor, outer, inner }; targets.AddRange(parts); targets.Reverse(); targets.Add(actor);
            foreach (var part in parts) CorruptMass(part, 0);
            CorruptMass(actor, 0);
            double expected = 70.4345 + (traitName == "IsStrong" ? 15 : traitName == "IsFeeble" ? -10 : 0) + 7 + 70;
            var result = MassRepairBatch.Repair(targets);
            Check(result.Skipped == 0 && result.People == 1 && result.Anatomy == 12 && result.Containers == 2, "Repair coverage or eligibility incorrect: " + string.Join(";", result.Reasons.Keys));
            Check(inner.GetTotalMass() == 60 && outer.GetTotalMass() == 70, "Nested crates/stacks not repaired before character totals");
            Check(Math.Abs(actor.GetTotalMass() - expected) < 1e-8, "Character reconstruction differs from native body/equipment oracle");
            Check(Math.Abs(actor.GetCondAmount("StatEncumbrance") - expected * actor.ship.Gravity) < 1e-8, "Repair left stale character burden");
            Check(held.GetTotalMass() == 7 && head.GetTotalMass() == 50 && member.coStackHead == head, "Repair changed equipment or stack contents");
            var again = MassRepairBatch.Repair(targets);
            Check(again.Changes.Count == 0 && again.Skipped == 0, "Second massfix was not idempotent");
            // Only actual anatomy contributes: do not restore a missing arm.
            var arm = parts.First(p => p.strCODef == "BodyarmUpperLA"); double armMass = arm.GetTotalMass();
            actor.compSlots.UnSlotItem("armUpperL", arm, true);
            result = MassRepairBatch.Repair(targets);
            Check(result.Changes.Count == 0 && actor.compSlots.GetSlot("armUpperL").aCOs[0] == null && Math.Abs(actor.GetTotalMass() - expected + armMass) < 1e-8,
                "Mass repair recreated or counted detached anatomy");
            actor.compSlots.SlotItem("armUpperL", arm, false);
            // Reviewed anatomical overlays preserve a known base, not a new tare.
            var overlay = DataHandler.dictCOOverlays.Values.First(o => o.strCOBase == "BodyhandLA" && string.IsNullOrEmpty(o.strCondLoot));
            var hand = parts.First(p => p.strCODef == "BodyhandLA"); string originalName = hand.strCODef; hand.strCODef = overlay.strName;
            result = MassRepairBatch.Repair(targets); Check(result.Skipped == 0 && result.Changes.Count == 0, "Reviewed anatomical overlay was not accepted");
            hand.strCODef = originalName;
            var overrides = new HashSet<string> { actor.strID, hand.strID };
            result = MassRepairBatch.Repair(targets, overrides); Check(result.Skipped == 2 && result.Changes.Count == 0, "Explicit crew/anatomical overrides were changed");
            values[actor.strID]["IsStrong"] = 2;
            result = MassRepairBatch.Repair(new[] { actor }); Check(result.Skipped == 1 && result.Changes.Count == 0, "Noncanonical trait count accepted");
            values[actor.strID]["IsStrong"] = traitName == "IsStrong" ? 1 : 0;
            actor.mapConds["ModdedHeavy"] = new Condition(new JsonCond { strName = "ModdedHeavy", aPer = new[] { "AuditHeavy" } }) { fCount = 1 };
            DataHandler.dictLoot["AuditHeavy"] = Loot.CloneFromJSON(new JsonLoot { strName = "AuditHeavy", strType = "condition", aCOs = new[] { "StatMass=1.0x9" } });
            result = MassRepairBatch.Repair(new[] { actor }); Check(result.Skipped == 1 && result.Changes.Count == 0, "Unreviewed mass-changing condition accepted");
            DataHandler.dictLoot["AuditHeavy"] = Loot.CloneFromJSON(new JsonLoot { strName = "AuditHeavy", strType = "condition", aCOs = new[] { "OtherStat=0.5x1|StatMass=0.5x9" } });
            result = MassRepairBatch.Repair(new[] { actor }); Check(result.Skipped == 1 && result.Changes.Count == 0, "Mass-changing random alternative was overlooked");
            actor.mapConds.Remove("ModdedHeavy");
            // Failure after descendants have changed restores the entire tree.
            foreach (var part in parts) CorruptMass(part, 0); CorruptMass(actor, 0);
            var before = targets.Distinct().ToDictionary(p => p, p => p.GetCondAmount("StatMass"));
            massWriteFailureTarget = actor.strID;
            result = MassRepairBatch.Repair(targets);
            Check(result.Changes.Count == 0 && result.Skipped > 0 && before.All(p => p.Key.GetCondAmount("StatMass") == p.Value), "Failed repair did not restore the ownership tree");
            Check(LiveMass.Refreshing == null && LiveMass.Writer == null, "Batch repair retained a game reference after failure");
            result = MassRepairBatch.Repair(targets);
            Check(result.Skipped == 0 && Math.Abs(actor.GetTotalMass() - expected) < 1e-8, "Repair after an exception remained broken");
            var unknown = CO("unreviewed-owner", 1); var blocked = CO("ItmCrate01", 1000); Contents(unknown, blocked);
            values[blocked.strID]["IsContainer"] = 1; StackField(blocked, "_objShip", actor.ship); StackField(unknown, "_objShip", actor.ship);
            var independent = CO("ItmCrate01", 50); values[independent.strID]["IsContainer"] = 1; StackField(independent, "_objShip", actor.ship);
            result = MassRepairBatch.Repair(new[] { blocked, independent });
            Check(result.Skipped == 1 && blocked.GetTotalMass() == 1000 && unknown.GetTotalMass() == 1 && independent.GetTotalMass() == 10,
                "Invalid ancestor was clamped or blocked an independent valid tree");
            var oldSystem = CrewSim.system; string oldShipId = actor.ship.strRegID;
            try {
                CrewSim.system = Empty<StarSystem>(); actor.ship.strRegID = "massfix-world";
                CrewSim.system.dictShips = new Dictionary<string, Ship> { [actor.ship.strRegID] = actor.ship };
                CorruptMass(independent, 50); result = MassRepairBatch.Repair(new[] { independent }, worldOnly: true);
                Check(result.Skipped == 0 && independent.GetTotalMass() == 10, "Registered materialized world target was excluded");
                var ghost = Empty<Ship>(); ghost.strRegID = actor.ship.strRegID; StackField(ghost, "nLoadState", Ship.Loaded.Full);
                Contents(unknown, independent); CorruptMass(independent, 20);
                StackField(unknown, "_objShip", ghost); result = MassRepairBatch.Repair(new[] { independent }, worldOnly: true);
                Check(result.Skipped == 1 && independent.GetTotalMass() == 20, "Stale owner ship instance was mutated");
                StackField(unknown, "_objShip", null); result = MassRepairBatch.Repair(new[] { independent }, worldOnly: true);
                Check(result.Skipped == 1 && independent.GetTotalMass() == 20, "Unplaced owner was mutated");
                StackField(ghost, "nLoadState", Ship.Loaded.Shallow); StackField(unknown, "_objShip", ghost);
                result = MassRepairBatch.Repair(new[] { independent }, worldOnly: true);
                Check(result.Skipped == 1 && independent.GetTotalMass() == 20, "Shallow owner was mutated");
            } finally { actor.ship.strRegID = oldShipId; CrewSim.system = oldSystem; }
            Console.WriteLine("CHARACTER REPAIR: " + trait + "; restored " + expected + "kg; second run unchanged; detached limb preserved; failure rollback passed.");
            if (trait == "none" && massRepairGraphPath != null) CheckSavedMassRepair(actor.ship);
        } finally {
            massWriteFailureTarget = null; DataHandler.dictCOOverlays = oldOverlays; DataHandler.dictLoot = oldLoot;
            definitions.Clear(); foreach (var pair in savedDefinitions) definitions[pair.Key] = pair.Value;
        }
    }
}
