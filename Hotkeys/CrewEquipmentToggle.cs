using System;
using System.Collections.Generic;

namespace Ostrabus.Hotkeys;

// A keypress uses the same slot/container operations as inventory manipulation.
// No simulation hooks, world searches, dropped items, or retained crew references.
internal static class CrewEquipmentToggle {
    internal static bool Helmet(CondOwner item) => item != null && (item.HasCond("IsEVAHelmet") || item.HasCond("IsPSHelmet"));
    internal static Slot SlotOf(CondOwner owner, string name) => owner?.compSlots?.GetSlot(name);
    internal static CondOwner Worn(CondOwner owner, string name) => SlotOf(owner, name)?.GetOutermostCO();
    internal static Container BatteryCompartment(CondOwner crew) {
        CondOwner suit = Worn(crew, "shirt_out");
        return suit != null && suit.HasCond("IsEVASuit") ? Worn(suit, "pocket_EVABatt")?.objContainer : null;
    }
    internal static bool CanSlot(Slot slot, CondOwner item) {
        if (slot == null || item == null || item.mapSlotEffects == null || !item.mapSlotEffects.TryGetValue(slot.strName, out var effects) ||
            effects.strSlotPrimary != slot.strName || slot.OpenSpaces(item, bAuto:false) < 1) return false;
        if (effects.aSlotsSecondary != null) foreach (string name in effects.aSlotsSecondary) {
            Slot secondary = slot.compSlots.GetSlot(name);
            if (secondary == null || secondary.OpenSpaces(item, bAuto:false) < 1) return false;
        }
        return true;
    }
    internal static void Gather(CondOwner crew, List<CondOwner> carried, HashSet<CondOwner> visited) {
        carried.Add(crew); visited.Add(crew);
        for (int i = 0; i < carried.Count; i++) {
            CondOwner owner = carried[i];
            // Do not rummage inside spare suits or EVA hardware compartments.
            if (owner != crew && owner.HasCond("IsEVASuit")) continue;
            if (owner.HasCond("IsLocked")) continue;
            if (owner.compSlots != null) foreach (Slot slot in owner.GetSlots(bDeep:false)) {
                if (slot.strName == "drag" || slot.strName.StartsWith("pocket_EVA", StringComparison.Ordinal)) continue;
                foreach (CondOwner child in slot.aCOs) Add(child, carried, visited);
            }
            if (owner.objContainer != null && !owner.objContainer.Locked)
                foreach (CondOwner child in owner.objContainer.ContainedCOs) Add(child, carried, visited);
        }
    }
    static void Add(CondOwner item, List<CondOwner> carried, HashSet<CondOwner> visited) {
        if (item != null && !item.bDestroyed && item.coStackHead == null && visited.Add(item)) carried.Add(item);
    }
    internal static bool FindStorage(CondOwner crew, CondOwner item, List<CondOwner> carried, out Slot slot, out Container container, out PairXY position) {
        slot = null; container = null; position = default;
        foreach (string hand in Hands) {
            Slot candidate = SlotOf(crew, hand);
            if (CanSlot(candidate, item) && candidate.GetOutermostCO() == null) { slot = candidate; return true; }
        }
        foreach (CondOwner owner in carried) {
            if (owner == item || owner.HasCond("IsEVASuit")) continue;
            Container candidate = owner.objContainer;
            if (candidate == null || candidate.Locked || !candidate.AllowedCO(item) || !candidate.CanAddSimple(item, out position)) continue;
            container = candidate; return true;
        }
        return false;
    }
    static readonly string[] Hands = { "heldL", "heldR" };
    internal static bool CanStow(CondOwner crew) {
        if (crew == null || crew.ship == null || crew.tf == null) return false;
        // Read the actual compartment, not the character's sealed-suit gas or
        // a cached currentRoom left over from crossing a docking port.
        Room room = crew.ship.GetRoomAtWorldCoords1(crew.tf.position, bAllowDocked:true);
        return room != null && !room.Void && room.CO != null && !room.CO.bDestroyed && PledgeSurviveO2.CoHasO2(room.CO);
    }
    // Allocate only on activation. Locals cannot retain an NPC after save unload.
    internal static void Toggle(CondOwner crew) {
        Slot head = SlotOf(crew, "head_out");
        CondOwner helmet = head?.GetOutermostCO();
        Container socket = BatteryCompartment(crew);
        CondOwner battery = null;
        if (socket != null) foreach (CondOwner item in socket.ContainedCOs)
            if (item != null && item.HasCond("IsBatteryEVA")) { battery = item; break; }
        bool stow = Helmet(helmet) || battery != null;
        if (stow && !CanStow(crew)) {
            crew.LogMessage("Equipment unchanged: compartment oxygen is unsafe or unknown.", "Bad", crew.strID);
            return;
        }
        var carried = new List<CondOwner>(32);
        var visited = new HashSet<CondOwner>();
        Gather(crew, carried, visited);
        string helmetResult, batteryResult = null;
        if (stow) {
            helmetResult = Helmet(helmet) ? Stow(crew, helmet, carried, "helmet") : "no helmet worn";
            if (battery != null) batteryResult = Stow(crew, battery, carried, "battery");
        } else {
            helmetResult = "no carried helmet";
            if (helmet != null) helmetResult = "head slot occupied";
            else foreach (CondOwner item in carried) {
                if (item.bSlotLocked || !Helmet(item) || !CanSlot(head, item)) continue;
                helmetResult = Move(item, head, null, default) ? "equipped helmet" : "could not equip helmet";
                break;
            }
            if (socket != null) {
                batteryResult = "no carried EVA battery";
                foreach (CondOwner item in carried) {
                    if (item.bSlotLocked || !item.HasCond("IsBatteryEVA") || !socket.AllowedCO(item) || !socket.CanAddSimple(item, out var position)) continue;
                    batteryResult = Move(item, null, socket, position) ? "inserted EVA battery" : "could not insert EVA battery";
                    break;
                }
            }
        }
        crew.LogMessage("Equipment: " + helmetResult + (batteryResult == null ? "." : "; " + batteryResult + "."), "Neutral", crew.strID);
    }
    static string Stow(CondOwner crew, CondOwner item, List<CondOwner> carried, string label) {
        if (item.bSlotLocked) return label + " is locked in place";
        if (!FindStorage(crew, item, carried, out var slot, out var container, out var position)) return "no room to remove " + label;
        return Move(item, slot, container, position) ? "removed " + label : "could not remove " + label;
    }
    internal static bool Move(CondOwner item, Slot slot, Container container, PairXY position) {
        if (item == null || item.bSlotLocked || item.coStackHead != null || item.objCOParent == null) return false;
        if (slot != null) {
            if (!CanSlot(slot, item) || slot.GetOutermostCO() != null) return false;
        } else if (container == null || container.Locked || !container.AllowedCO(item) || !container.CanAddSimple(item, out position)) return false;
        Slot originSlot = item.slotNow;
        Container originContainer = item.objCOParent.objContainer;
        PairXY originPosition = item.pairInventoryXY;
        CondOwner remainder = null;
        try {
            if (item.StackCount > 1) remainder = item.PopHeadFromStack();
            else item.RemoveFromCurrentHome();
            if (item.objCOParent != null) return false; // Locked or otherwise refused by native removal.
            if (slot != null) {
                if (slot.compSlots.SlotItem(slot.strName, item, bAuto:false)) return true;
            } else {
                container.AddCOSimple(item, position);
                if (item.objCOParent == container.CO) { container.Redraw(); return true; }
            }
        } finally {
            // A rejected destination must never spill or destroy the item.
            if (item.objCOParent == null) {
                if (remainder != null) remainder.StackCO(item);
                else if (originSlot != null) originSlot.compSlots.SlotItem(originSlot.strName, item, bAuto:false);
                else if (originContainer != null) { originContainer.AddCOSimple(item, originPosition); originContainer.Redraw(); }
            }
        }
        return false;
    }
}
