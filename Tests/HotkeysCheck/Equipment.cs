using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using HarmonyLib;
using LitJson;
using Ostrabus.Hotkeys;
using Ostranauts.Core.Models;
using Ostranauts.Events;
using Ostranauts.Inventory;
using UnityEngine;
using UnityEngine.InputSystem;

static partial class Program {
    static readonly Dictionary<CondOwner, HashSet<string>> equipmentConditions = new Dictionary<CondOwner, HashSet<string>>();
    static readonly Dictionary<CondOwner, double> equipmentMass = new Dictionary<CondOwner, double>();
    static readonly Dictionary<CondOwner, double> ambientOxygen = new Dictionary<CondOwner, double>();
    static readonly Dictionary<Ship, Room> compartments = new Dictionary<Ship, Room>();
    static int itemSerial, effectsApplied, effectsRemoved;
    static bool denyNextSlot;
    static bool DenySlot(ref bool __result) {
        if (!denyNextSlot) return true;
        denyNextSlot = false; __result = false; return false;
    }
    static bool NoOp() => false;
    static bool ParentSet(CondOwner __instance, CondOwner value) { Field(__instance, "_objCOParent", value); return false; }
    static bool ShipSet(CondOwner __instance, Ship value) { Field(__instance, "_objShip", value); return false; }
    static bool ShipAdd(Ship __instance, CondOwner __0) { Field(__0, "_objShip", __instance); return false; }
    static bool ShipRemove(CondOwner __0) { Field(__0, "_objShip", null); return false; }
    static bool MassRead(CondOwner __instance, string strName, ref double __result) {
        if (strName == "StatMass") __result = equipmentMass[__instance];
        else if (strName == "StatGasPpO2") ambientOxygen.TryGetValue(__instance, out __result);
        else __result = 0;
        return false;
    }
    static bool Compartment(Ship __instance, bool bAllowDocked, ref Room __result) {
        Check(bAllowDocked, "Environment lookup cannot resolve across docking-port boundaries");
        compartments.TryGetValue(__instance, out __result); return false;
    }
    static Room Atmosphere(CondOwner crew, double oxygen) {
        var ship = Empty<Ship>(); Field(crew, "_objShip", ship);
        var room = Empty<Room>(); var air = Gear(); Field(room, "coRoom", air);
        ambientOxygen[air] = oxygen; compartments[ship] = room; return room;
    }
    static bool MassAdd(CondOwner __instance, double fMass) { for (var co = __instance; co != null; co = co.objCOParent) equipmentMass[co] += fMass; return false; }
    static bool Width(ref PairXY __result) { __result = new PairXY(1, 1); return false; }
    static bool NoItem(ref Item __result) { __result = null; return false; }
    static bool Position(ref Vector3 __result) { __result = default; return false; }
    static bool Rotation(ref Quaternion __result) { __result = Quaternion.identity; return false; }
    static bool Effects(bool bRemove) { if (bRemove) effectsRemoved++; else effectsApplied++; return false; }
    static bool False(ref bool __result) { __result = false; return false; }
    static CondOwner Gear(string condition = null, double mass = 1) {
        var co = Empty<CondOwner>(); Field(co, "_strID", "gear-" + ++itemSerial); co.strCODef = condition ?? co.strID;
        co.tf = Empty<Transform>(); co.aStack = new TrackingCollection<CondOwner>(); co.aLot = new TrackingCollection<CondOwner>();
        co.mapSlotEffects = new Dictionary<string, JsonSlotEffects>(); co.nStackLimit = 1;
        equipmentConditions[co] = new HashSet<string>(); equipmentMass[co] = mass;
        if (condition != null) equipmentConditions[co].Add(condition);
        return co;
    }
    static Slot Slot(CondOwner co, string name, bool hold = false) {
        Slots slots = co.compSlots;
        if (slots == null) {
            slots = Empty<Slots>(); Field(slots, "coUs", co);
            Field(slots, "aSlots", new TrackingCollection<Slot>()); Field(slots, "aSlotNames", new List<string>());
            Field(co, "_compSlots", slots);
        }
        var slot = new Slot(new JsonSlot { strName = name, nItems = 1, bHoldSlot = hold, bCarried = true });
        slot.compSlots = slots;
        ((TrackingCollection<Slot>)AccessTools.Field(typeof(Slots), "aSlots").GetValue(slots)).Add(slot);
        ((List<string>)AccessTools.Field(typeof(Slots), "aSlotNames").GetValue(slots)).Add(name);
        return slot;
    }
    static void Fits(CondOwner item, params string[] names) { foreach (string name in names) item.mapSlotEffects[name] = new JsonSlotEffects { strSlotPrimary = name }; }
    static void Place(Slot slot, CondOwner co) { slot.aCOs[0] = co; co.slotNow = slot; Field(co, "_objCOParent", AccessTools.Field(typeof(Slots), "coUs").GetValue(slot.compSlots)); }
    static Container Box(CondOwner co, int width, int height) {
        var container = Empty<Container>(); Field(container, "co", co); Field(container, "aCOs", new TrackingCollection<CondOwner>());
        container.gridLayout = new GridLayout(width, height); co.objContainer = container; return container;
    }
    static void Put(Container box, CondOwner item) { box.AddCOSimple(item, box.gridLayout.FindFirstUnoccupiedTile(1, 1, item.strID)); }
    static void EquipmentChecks() {
        var definitions = JsonMapper.ToObject<JsonCondOwner[]>(File.ReadAllText(Ostrabus.Development.InstallationPaths.Data + "condowners/condowners.json"));
        foreach (string name in new[] { "OutfitHelmet02", "OutfitHelmet02Dmg", "OutfitHelmet03", "OutfitHelmet03Dmg" }) {
            var definition = definitions.Single(co => co.strName == name);
            Check(definition.aStartingConds.Any(cond => cond.StartsWith(name.StartsWith("OutfitHelmet02") ? "IsPSHelmet=" : "IsEVAHelmet=")), "Installed helmet classification changed: " + name);
            Check(definition.mapSlotEffects.Contains("head_out") && definition.mapSlotEffects.Contains("heldL") && definition.mapSlotEffects.Contains("heldR"), "Installed helmet slot support changed: " + name);
        }
        Check(definitions.Single(co => co.strName == "PocketEVABatt01").strContainerCT == "TIsFitContainerEVABattery", "Native battery compartment filter changed");
        foreach (string text in new[] { "Shift+F", "Ctrl+G", "Alt+F2", "Meta+Space", "f", "ctrl+shift+h" })
            Check(CrewShortcut.TryParse(text, out var shortcut) && CrewShortcut.TryParse(shortcut.ToString(), out _), "Shortcut round trip failed: " + text);
        foreach (string text in new[] { "Shift+R", "Shift+T", "Escape", "LeftShift", "Shift", "Shift+Bogus", "Ctrl+F+G", "" })
            Check(!CrewShortcut.TryParse(text, out var shortcut) && shortcut.Key == Key.None, "Invalid shortcut remains active: " + text);
        var h = new Harmony("offline.hotkeys.equipment");
        void Patch(Type type, string method, string prefix, Type[] args = null) => h.Patch(AccessTools.Method(type, method, args), prefix:new HarmonyMethod(typeof(Program), prefix));
        try {
            EquipmentShortcut.Binding = null;
            // Unity scene/render/UI and condition effects are explicit boundaries.
            // Actual native slots, container grid, parent removal and stack splitting execute.
            h.Patch(AccessTools.PropertySetter(typeof(CondOwner), "objCOParent"), prefix:new HarmonyMethod(typeof(Program), nameof(ParentSet)));
            h.Patch(AccessTools.PropertySetter(typeof(CondOwner), "ship"), prefix:new HarmonyMethod(typeof(Program), nameof(ShipSet)));
            Patch(typeof(CondOwner), "GetCondAmount", nameof(MassRead), new[] { typeof(string) });
            Patch(typeof(CondOwner), "AddMass", nameof(MassAdd));
            foreach (string name in new[] { "ValidateParent", "ValidateParentRecursive", "AddCondAmount", "ZeroCondAmount", "UpdateAppearance" }) Patch(typeof(CondOwner), name, nameof(NoOp));
            h.Patch(AccessTools.PropertyGetter(typeof(CondOwner), "Item"), prefix:new HarmonyMethod(typeof(Program), nameof(NoItem)));
            h.Patch(AccessTools.PropertySetter(typeof(CondOwner), "Visible"), prefix:new HarmonyMethod(typeof(Program), nameof(NoOp)));
            h.Patch(AccessTools.PropertyGetter(typeof(CondOwner), "Visible"), prefix:new HarmonyMethod(typeof(Program), nameof(False)));
            h.Patch(AccessTools.PropertyGetter(typeof(Transform), "position"), prefix:new HarmonyMethod(typeof(Program), nameof(Position)));
            h.Patch(AccessTools.PropertyGetter(typeof(Transform), "rotation"), prefix:new HarmonyMethod(typeof(Program), nameof(Rotation)));
            h.Patch(AccessTools.PropertyGetter(typeof(Transform), "parent"), prefix:new HarmonyMethod(typeof(Program), nameof(NoOp)));
            foreach (string prop in new[] { "position", "localPosition" }) h.Patch(AccessTools.PropertySetter(typeof(Transform), prop), prefix:new HarmonyMethod(typeof(Program), nameof(NoOp)));
            Patch(typeof(Transform), "SetParent", nameof(NoOp), new[] { typeof(Transform) });
            Patch(typeof(Transform), "SetParent", nameof(NoOp), new[] { typeof(Transform), typeof(bool) });
            Patch(typeof(Slots), "ApplySlotEffects", nameof(Effects));
            Patch(typeof(Slots), "SlotItem", nameof(DenySlot));
            Patch(typeof(Ship), "GetRoomAtWorldCoords1", nameof(Compartment));
            Patch(typeof(Ship), "AddCO", nameof(ShipAdd), new[] { typeof(CondOwner), typeof(bool) });
            Patch(typeof(Ship), "RemoveCO", nameof(ShipRemove), new[] { typeof(CondOwner), typeof(bool) });
            Patch(typeof(Container), "Redraw", nameof(NoOp), Type.EmptyTypes);
            Patch(typeof(GUIInventoryItem), "GetWidthHeightForCO", nameof(Width));
            Patch(typeof(GUIInventory), "IsCOShown", nameof(False));
            Patch(typeof(GUIInventory), "RemoveAndDestroy", nameof(NoOp));
            CrewSim.inventoryGUI = Empty<GUIInventory>(); Slots.OnSlotContentUpdated = new SlotUpdatedEvent();

            var crew = Gear(); var room = Atmosphere(crew, 20.95); var head = Slot(crew, "head_out"); var left = Slot(crew, "heldL", true); var right = Slot(crew, "heldR", true);
            var suit = Gear("IsEVASuit"); Place(Slot(crew, "shirt_out"), suit);
            var socketCO = Gear(); Place(Slot(suit, "pocket_EVABatt"), socketCO); socketCO.bSlotLocked = true;
            var socket = Box(socketCO, 1, 1);
            var helmet = Gear("IsEVAHelmet", 3); Fits(helmet, "head_out", "heldL", "heldR"); Place(head, helmet);
            var battery = Gear("IsBatteryEVA", 11.5); Fits(battery, "heldL", "heldR"); Put(socket, battery);
            foreach (double oxygen in new[] { 0d, 16.999999d, 17d, 20.95d, double.NaN }) {
                ambientOxygen[room.CO] = oxygen;
                Check(CrewEquipmentToggle.CanStow(crew) == (oxygen >= 17), "Safety gate differs from native oxygen survival threshold: " + oxygen);
            }
            // A sealed suit can contain breathable gas while the room is vacuum.
            ambientOxygen[crew] = 50; ambientOxygen[room.CO] = 0;
            var staleSafeRoom = Empty<Room>(); var staleAir = Gear(); Field(staleSafeRoom, "coRoom", staleAir);
            ambientOxygen[staleAir] = 21; crew.currentRoom = staleSafeRoom;
            int refusalBefore = messages, beforeEffects = effectsRemoved;
            CrewEquipmentToggle.Toggle(crew);
            Check(head.GetOutermostCO() == helmet && socket.ContainedCOs.Contains(battery) && effectsRemoved == beforeEffects, "Unsafe stow altered installed gear");
            Check(messages == refusalBefore + 1 && messageColor == "Bad" && messageText.Contains("oxygen"), "Unsafe stow should explain refusal in one action-log message");
            ambientOxygen[room.CO] = 20.95; Field(room, "bVoid", true);
            Check(!CrewEquipmentToggle.CanStow(crew), "Void room with stale gas readings accepted"); Field(room, "bVoid", false);
            compartments.Remove(crew.ship); Check(!CrewEquipmentToggle.CanStow(crew), "Missing compartment was treated as breathable"); compartments[crew.ship] = room;
            room.CO.bDestroyed = true; Check(!CrewEquipmentToggle.CanStow(crew), "Destroyed room atmosphere accepted"); room.CO.bDestroyed = false;
            var airCO = room.CO; Field(room, "coRoom", null); Check(!CrewEquipmentToggle.CanStow(crew), "Missing atmosphere was treated as breathable"); Field(room, "coRoom", airCO);
            double mass = equipmentMass[crew]; int before = messages;
            CrewEquipmentToggle.Toggle(crew);
            Check(head.GetOutermostCO() == null && left.GetOutermostCO() == helmet && right.GetOutermostCO() == battery && socket.ContainedCOs.Count == 0, "Worn equipment did not move to free hands");
            Check(messages == before + 1 && messageText.Contains("removed helmet") && messageText.Contains("removed battery"), "Expected one combined equipment action message");
            Check(equipmentMass[crew] == mass, "Stowing changed carried mass");
            ambientOxygen[room.CO] = 0;
            CrewEquipmentToggle.Toggle(crew);
            Check(head.GetOutermostCO() == helmet && socket.ContainedCOs.Contains(battery) && left.GetOutermostCO() == null && right.GetOutermostCO() == null, "Native equip round trip failed");
            Check(equipmentMass[crew] == mass && effectsApplied >= 3 && effectsRemoved >= 3, "Equipment bypassed mass/effect hooks");
            ambientOxygen[room.CO] = 20.95;
            Place(left, Gear()); Place(right, Gear());
            CrewEquipmentToggle.Toggle(crew);
            Check(head.GetOutermostCO() == helmet && socket.ContainedCOs.Contains(battery) && messageText.Contains("no room to remove helmet") && messageText.Contains("no room to remove battery"), "Full inventory should refuse removal");
            var bag = Gear(); Place(Slot(crew, "back"), bag); var bagBox = Box(bag, 1, 1);
            CrewEquipmentToggle.Toggle(crew);
            Check(bagBox.ContainedCOs.Contains(helmet) && socket.ContainedCOs.Contains(battery) && messageText.Contains("no room to remove battery"), "Partial stow lost equipment or overfilled storage");
            ambientOxygen[room.CO] = 0; CrewEquipmentToggle.Toggle(crew);
            Check(socket.ContainedCOs.Contains(battery) && bagBox.ContainedCOs.Contains(helmet) && messageText.Contains("oxygen"), "Battery-only stow bypassed ambient safety"); ambientOxygen[room.CO] = 20.95;
            var carried = new List<CondOwner>(); var seen = new HashSet<CondOwner>();
            CrewEquipmentToggle.Gather(crew, carried, seen);
            Check(carried.Contains(helmet) && !carried.Contains(battery) && !carried.Contains(socketCO), "Carried search rummaged in installed EVA components");
            equipmentConditions[bag].Add("IsLocked"); carried.Clear(); seen.Clear(); CrewEquipmentToggle.Gather(crew, carried, seen);
            Check(!carried.Contains(helmet), "Carried search entered locked storage"); equipmentConditions[bag].Remove("IsLocked");
            var drag = Gear(); var draggedHelmet = Gear("IsPSHelmet"); Fits(draggedHelmet, "head_out"); Put(Box(drag, 1, 1), draggedHelmet); Place(Slot(crew, "drag"), drag);
            carried.Clear(); seen.Clear(); CrewEquipmentToggle.Gather(crew, carried, seen);
            Check(!carried.Contains(draggedHelmet) && !carried.Contains(drag), "Search includes dragged world inventory");
            helmet.bSlotLocked = true;
            Check(!CrewEquipmentToggle.Move(helmet, head, null, default) && bagBox.ContainedCOs.Contains(helmet), "Locked item was moved"); helmet.bSlotLocked = false;
            // Destination refusal restores original container using the native path.
            denyNextSlot = true;
            Check(!CrewEquipmentToggle.Move(helmet, head, null, default) && bagBox.ContainedCOs.Contains(helmet) && helmet.objCOParent == bag, "Rejected destination lost original inventory membership");
            head.aCOs[0] = null;
            // Native PopHeadFromStack leaves the remaining battery in storage.
            var extra = Gear("IsBatteryEVA", 11.5); Fits(extra, "heldL", "heldR");
            battery.aStack.Add(extra); extra.coStackHead = battery; battery.nStackLimit = extra.nStackLimit = 10;
            Check(CrewEquipmentToggle.Move(battery, left, null, default) == false && socket.ContainedCOs.Count == 1, "Blocked stack transfer failed to restore remainder");
            left.aCOs[0] = null;
            Check(CrewEquipmentToggle.Move(battery, left, null, default) && left.GetOutermostCO() == battery && battery.StackCount == 1 && socket.ContainedCOs.Contains(extra) && extra.StackCount == 1, "Native battery stack split did not preserve both objects");
            battery.aStack.Add(extra); extra.coStackHead = battery;
            carried.Clear(); seen.Clear(); CrewEquipmentToggle.Gather(crew, carried, seen);
            Check(carried.Contains(battery) && !carried.Contains(extra) && !CrewEquipmentToggle.Move(extra, head, null, default), "Stack child was independently selected or moved");
            battery.aStack.Clear(); extra.coStackHead = null;
            var pressureCrew = Gear(); var pressureRoom = Atmosphere(pressureCrew, 20.95); var pressureHead = Slot(pressureCrew, "head_out"); Slot(pressureCrew, "heldR", true);
            var pressureHelmet = Gear("IsPSHelmet"); Fits(pressureHelmet, "head_out", "heldL", "heldR"); Place(Slot(pressureCrew, "heldL", true), pressureHelmet);
            CrewEquipmentToggle.Toggle(pressureCrew);
            Check(pressureHead.GetOutermostCO() == pressureHelmet && !messageText.Contains("battery"), "Pressure helmet equip incorrectly requires EVA battery: " + messageText + "; mounted=" + (pressureHead.GetOutermostCO() == pressureHelmet));
            ambientOxygen[pressureRoom.CO] = 16;
            CrewEquipmentToggle.Toggle(pressureCrew);
            Check(pressureHead.GetOutermostCO() == pressureHelmet && messageText.Contains("oxygen"), "Pressure helmet removal bypassed ambient safety"); ambientOxygen[pressureRoom.CO] = 20.95;
            CrewEquipmentToggle.Toggle(pressureCrew);
            Check(pressureHead.GetOutermostCO() == null && pressureHelmet.objCOParent == pressureCrew, "Pressure helmet stow failed");
        } finally { h.UnpatchSelf(); equipmentConditions.Clear(); equipmentMass.Clear(); ambientOxygen.Clear(); compartments.Clear(); CrewSim.inventoryGUI = null; }
    }
}
