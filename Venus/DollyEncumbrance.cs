using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Ostranauts.Core.Models;
using SolarSystem;

namespace Ostrabus.Venus;

internal static class DollyEncumbrance {
    internal const double Multiplier = .10;
    static readonly AccessTools.FieldRef<Slots, TrackingCollection<Slot>> SlotList = AccessTools.FieldRefAccess<Slots, TrackingCollection<Slot>>("aSlots");
    static readonly AccessTools.FieldRef<DockGroupMember, Ship> MemberShip = AccessTools.FieldRefAccess<DockGroupMember, Ship>("_ship");
    static readonly AccessTools.FieldRef<Ship, List<PersonSpec>> People = AccessTools.FieldRefAccess<Ship, List<PersonSpec>>("aPeople");
    static bool VenusReference(string id) {
        var bodies = CrewSim.system?.aBOs;
        if (id == null || bodies == null || !bodies.TryGetValue(id, out var body)) return false;
        for (int depth=0;body!=null && depth<16;depth++,body=body.boParent) if (body.strName == "Venus") return true;
        return false;
    }
    static bool Aerostat(Ship ship) {
        var ships = CrewSim.system?.dictShips;
        // An apartment may reference its parent station without carrying the
        // station's conditions itself. Follow actual body/station references only.
        for (int depth=0;ship!=null && depth<8;depth++) {
            if (ship.bDestroyed || ship.bRemove || ship.objSS?.bIsBO != true) return false;
            string reference = ship.objSS.strBOPORShip;
            if (ship.ShipCO != null && ship.ShipCO.HasCond("StationHasGrav", isThreshold:false) && VenusReference(reference)) return true;
            if (ships == null || reference == null || reference == ship.strRegID || !ships.TryGetValue(reference, out ship)) return false;
        }
        return false;
    }
    internal static bool Eligible(Ship ship) {
        if (ship == null || ship.bDestroyed || ship.bRemove || ship.objSS == null) return false;
        if (Aerostat(ship)) return true;
        var group = ship.objSS._dockGroup;
        if (group == null) return false;
        bool memberFound = false, stationFound = false;
        foreach (var entry in group.DockedShips) {
            var member = entry == null ? null : MemberShip(entry);
            if (member?.objSS == null || !ReferenceEquals(member.objSS._dockGroup, group)) continue;
            if (ReferenceEquals(member, ship)) memberFound = true;
            if (!stationFound && Aerostat(member)) stationFound = true;
        }
        return memberFound && stationFound;
    }
    static bool StackMass(CondOwner item, int depth, ref int remaining, out double mass) {
        mass = 0;
        if (item == null || item.bDestroyed || depth>32 || --remaining<0) return false;
        mass = item.GetCondAmount("StatMass"); // Includes contained/slotted payload.
        if (double.IsNaN(mass) || double.IsInfinity(mass) || mass<0) return false;
        if (item.aStack != null) foreach (var child in item.aStack) {
            if (!StackMass(child, depth+1, ref remaining, out double extra)) return false;
            mass += extra;
            if (double.IsInfinity(mass)) return false;
        }
        return true;
    }
    internal static double Mass(CondOwner person) {
        double actual = person.GetCondAmount("StatMass");
        if (actual<=0 || double.IsNaN(actual) || double.IsInfinity(actual) || person.compSlots == null || !Eligible(person.ship)) return actual;
        var slots = SlotList(person.compSlots);
        if (slots == null) return actual;
        double supported = 0;
        foreach (var slot in slots) {
            if (slot?.aCOs == null) continue;
            for (int i=0;i<slot.aCOs.Length;i++) {
                var item = slot.aCOs[i];
                // Primary slot/parent checks exclude secondary hand aliases and
                // contents of a supported dolly. Its payload is counted once.
                if (item == null || !ReferenceEquals(item.objCOParent, person) || !ReferenceEquals(item.slotNow, slot) ||
                    item.coStackHead != null || !item.HasCond("IsDolly", isThreshold:false)) continue;
                for (int previous=0;previous<i;previous++) if (ReferenceEquals(slot.aCOs[previous],item)) return actual;
                int remaining = 256;
                if (!StackMass(item, 0, ref remaining, out double mass)) return actual;
                supported += mass;
            }
        }
        // Independent inventory sums can differ by a few ulps after transfers.
        // Reject meaningful corruption, not roundoff when the dolly is the
        // character's entire carried inventory. Never change either raw mass.
        if (double.IsInfinity(supported) || supported-actual>Math.Max(1e-6,actual*1e-9)) return actual;
        supported = Math.Min(supported,actual);
        return actual - (1-Multiplier)*supported;
    }
    static void Refresh(Ship ship) {
        if (ship == null || ship.bDestroyed || ship.LoadState<Ship.Loaded.Edit) return;
        var people = People(ship);
        if (people == null) return;
        foreach (var profile in people) {
            var person = profile?.GetCO();
            if (person != null && !person.bDestroyed && ReferenceEquals(person.ship,ship)) person.UpdateGravity();
        }
    }
    internal static void RefreshGroup(Ship ship) {
        Refresh(ship);
        var group = ship?.objSS?._dockGroup;
        if (group == null) return;
        foreach (var entry in group.DockedShips) {
            var member = entry == null ? null : MemberShip(entry);
            if (member != null && !ReferenceEquals(member,ship) && ReferenceEquals(member.objSS?._dockGroup,group)) Refresh(member);
        }
    }
}

[HarmonyPatch]
internal static class DollyEncumbranceCalculationPatch {
    static IEnumerable<MethodBase> TargetMethods() {
        yield return AccessTools.Method(typeof(CondOwner), nameof(CondOwner.UpdateGravity));
        yield return AccessTools.Method(typeof(CondOwner), nameof(CondOwner.AddMass));
        yield return AccessTools.PropertySetter(typeof(Ship), nameof(Ship.Gravity));
    }
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var code = new List<CodeInstruction>(instructions);
        var read = AccessTools.Method(typeof(CondOwner), nameof(CondOwner.GetCondAmount), new[] { typeof(string) });
        var effective = AccessTools.Method(typeof(DollyEncumbrance), nameof(DollyEncumbrance.Mass));
        int matches = 0;
        for (int i=1;i<code.Count-2;i++) {
            // Only the StatMass operand immediately multiplied by local gravity.
            // AddMass's later inventory-label mass read must remain unchanged.
            if (!code[i].Calls(read) || code[i-1].opcode!=OpCodes.Ldstr || !Equals(code[i-1].operand,"StatMass") ||
                (code[i+2].opcode!=OpCodes.Mul && (i+3>=code.Count || code[i+3].opcode!=OpCodes.Mul))) continue;
            matches++;
            code[i-1].opcode = OpCodes.Nop; code[i-1].operand = null;
            code[i].opcode = OpCodes.Call; code[i].operand = effective;
        }
        if (matches!=1) throw new InvalidOperationException("Expected one encumbrance mass operand; refusing to patch an unknown game version.");
        return code;
    }
}

[HarmonyPatch]
internal static class DollyDockingBurdenPatch {
    static IEnumerable<MethodBase> TargetMethods() {
        yield return AccessTools.Method(typeof(Ship), nameof(Ship.Dock), new[] { typeof(Ship), typeof(bool), typeof(string), typeof(string) });
        yield return AccessTools.Method(typeof(Ship), nameof(Ship.Undock), new[] { typeof(Ship) });
    }
    static void Prefix(Ship __instance, Ship __0, out bool __state) { __state=DollyEncumbrance.Eligible(__instance) || DollyEncumbrance.Eligible(__0); }
    static void Postfix(Ship __instance, Ship __0, bool __state) {
        if (!__state && !DollyEncumbrance.Eligible(__instance) && !DollyEncumbrance.Eligible(__0)) return;
        DollyEncumbrance.RefreshGroup(__instance);
        if (__0 != null && !ReferenceEquals(__0,__instance) && (__0.objSS?._dockGroup == null ||
            !ReferenceEquals(__0.objSS._dockGroup,__instance.objSS?._dockGroup))) DollyEncumbrance.RefreshGroup(__0);
    }
}
