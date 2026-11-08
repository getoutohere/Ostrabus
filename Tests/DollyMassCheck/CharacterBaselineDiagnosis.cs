using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ostranauts.Core.Models;
using Ostranauts.Events;
using Ostranauts.Inventory;
using UnityEngine;

static partial class Program {
    static bool CharacterHidden(ref bool __result) { __result = false; return false; }
    static bool CharacterCrew(ref Crew __result) { __result = null; return false; }
    static Dictionary<string, JObject> CharacterAssets(string folder) {
        var rows = new Dictionary<string, JObject>();
        foreach (string file in Directory.GetFiles(Ostrabus.Development.InstallationPaths.Data + folder, "*.json"))
            foreach (JObject row in JArray.Parse(File.ReadAllText(file))) rows[(string)row["strName"]] = row;
        return rows;
    }
    static double CharacterTare(JObject definition) {
        string equation = definition["aStartingConds"].Values<string>().Single(e => e.StartsWith("StatMass=1.0x", StringComparison.Ordinal));
        return double.Parse(equation.Substring(equation.IndexOf('x') + 1), System.Globalization.CultureInfo.InvariantCulture);
    }
    static void CharacterSlots(CondOwner owner, JObject definition) {
        var slots = Empty<Slots>(); StackField(slots, "coUs", owner);
        var names = definition["aSlotsWeHave"]?.Values<string>().ToList() ?? new List<string>();
        var all = new TrackingCollection<Slot>();
        foreach (string name in names) { var slot = new Slot(new JsonSlot { strName = name, nItems = 1 }); slot.compSlots = slots; all.Add(slot); }
        StackField(slots, "aSlots", all); StackField(slots, "aSlotNames", names); StackField(owner, "_compSlots", slots);
    }
    static void CheckCharacterBaseline(Ship ship) {
        var defs = CharacterAssets("condowners"); var loots = CharacterAssets("loot");
        var hooks = new Harmony("offline.character.mass.slot-engine");
        var oldEvent = Slots.OnSlotContentUpdated;
        try {
            // Native slot search, ownership, mass propagation and removal stay
            // live. Rendering/slot-action effects and inventory UI are adapters.
            hooks.Patch(AccessTools.Method(typeof(Slots), "ApplySlotEffects"), prefix: new HarmonyMethod(typeof(Program), nameof(StackSkip)));
            hooks.Patch(AccessTools.Method(typeof(GUIInventory), "IsCOShown"), prefix: new HarmonyMethod(typeof(Program), nameof(CharacterHidden)));
            hooks.Patch(AccessTools.PropertyGetter(typeof(CondOwner), "Crew"), prefix: new HarmonyMethod(typeof(Program), nameof(CharacterCrew)));
            Slots.OnSlotContentUpdated = new SlotUpdatedEvent();
            double core = CharacterTare(defs["Crew01"]);
            var partNames = loots["ItmCrewSlots"]["aCOs"].Values<string>().Select(e => e.Split('=')[0]).Where(n => n.StartsWith("Body", StringComparison.Ordinal)).ToArray();
            foreach (string trait in new[] { "none", "CONDStrongPer", "CONDFeeblePer" }) {
                var actor = CO("Crew01", core); actor.tf = Empty<Transform>(); actor.nStackLimit = 1;
                StackField(actor, "_objShip", ship); values[actor.strID]["IsHuman"] = 1;
                CharacterSlots(actor, defs["Crew01"]);
                var parts = new List<CondOwner>(); double limbTares = 0;
                foreach (string name in partNames) {
                    JObject def = defs[name]; double tare = CharacterTare(def); limbTares += tare;
                    var part = CO(name, tare); part.tf = Empty<Transform>(); part.nStackLimit = 1;
                    CharacterSlots(part, def);
                    string primary = def["mapSlotEffects"].Values<string>().First();
                    part.mapSlotEffects = new Dictionary<string, JsonSlotEffects> { [primary] = new JsonSlotEffects { strSlotPrimary = primary } };
                    Check(actor.compSlots.SlotItem(primary, part, false), "Native limb slot failed: " + name);
                    parts.Add(part);
                }
                Check(parts.Count == 12, "Default anatomy does not have twelve limb components");
                Check(Math.Abs(actor.GetTotalMass() - core - limbTares) < 1e-9, "Native complete body mass differs from definition/ownership reconstruction");
                double modifier = trait == "CONDStrongPer" ? 15 : trait == "CONDFeeblePer" ? -10 : 0;
                if (trait != "none") Loot.CloneFromJSON(loots[trait].ToObject<JsonLoot>()).ApplyCondLoot(actor, 1);
                Check(Math.Abs(actor.GetTotalMass() - (core + limbTares + modifier)) < 1e-9, "Trait body mass reconstruction disagrees with native loot");
                var upper = parts.First(p => p.strCODef == "BodyarmUpperLA");
                double arm = upper.GetTotalMass();
                Check(arm > CharacterTare(defs[upper.strCODef]), "Distal anatomy not nested under upper arm");
                Check(actor.compSlots.UnSlotItem("armUpperL", upper, true) == upper, "Native limb removal failed");
                Check(Math.Abs(actor.GetTotalMass() - (core + limbTares + modifier - arm)) < 1e-9, "Missing limb mass was replaced with a fixed naked weight");
                Check(actor.compSlots.SlotItem("armUpperL", upper, false), "Native limb reattachment failed");
                Check(Math.Abs(actor.GetTotalMass() - (core + limbTares + modifier)) < 1e-9, "Limb reattachment counted mass twice");
                // Full-hand ownership is hierarchical, like anatomical slots.
                var held = CO("held-fixture", 7); held.tf = Empty<Transform>(); held.nStackLimit = 1;
                held.mapSlotEffects = new Dictionary<string, JsonSlotEffects> { ["heldL"] = new JsonSlotEffects { strSlotPrimary = "heldL" } };
                Check(actor.compSlots.SlotItem("heldL", held, false), "Held equipment did not resolve through the hand's slot");
                Check(Math.Abs(actor.GetTotalMass() - (core + limbTares + modifier + 7)) < 1e-9, "Equipment on anatomical children was omitted or counted twice");
                Console.WriteLine("CHARACTER BASELINE: " + trait + "; core=" + core + "; twelve limb tares=" + limbTares + "; native body=" + (actor.GetTotalMass() - 7) + "; equipment=7kg.");
                if (liveWrites) CheckCharacterRepair(actor, parts, held, trait, defs, loots);
            }
        } finally { Slots.OnSlotContentUpdated = oldEvent; hooks.UnpatchSelf(); }
    }
}
