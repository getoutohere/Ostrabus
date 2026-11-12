using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Ostranauts.ShipGUIs.Trade;
using Ostranauts.ShipGUIs.Trade.Models;
using TMPro;

namespace Ostrabus.QoL.BarterZones;

internal static class BarterZones {
    static readonly IComparer<TradeRowData> order = Comparer<TradeRowData>.Create(Compare);
    static int Compare(TradeRowData a, TradeRowData b) {
        string aShip = a.JsonZone.strRegID, bShip = b.JsonZone.strRegID;
        if (string.IsNullOrEmpty(aShip) != string.IsNullOrEmpty(bShip)) return string.IsNullOrEmpty(aShip) ? 1 : -1;
        int result = StringComparer.OrdinalIgnoreCase.Compare(aShip, bShip);
        if (result == 0) result = StringComparer.Ordinal.Compare(aShip, bShip);
        if (result == 0) result = StringComparer.OrdinalIgnoreCase.Compare(a.JsonZone.strName, b.JsonZone.strName);
        return result != 0 ? result : StringComparer.Ordinal.Compare(a.JsonZone.strName, b.JsonZone.strName);
    }
    internal static string Key(JsonZone zone) {
        string ship = zone.strRegID ?? "";
        // Length-prefix separates ship and zone names; the leading control
        // character keeps zone keys out of the native item-ID namespace.
        return "\u0001zone:" + ship.Length + ":" + ship + zone.strName;
    }
    internal static string Header(JsonZone zone) => string.IsNullOrEmpty(zone.strRegID) ? zone.strName : zone.strRegID + " / " + zone.strName;
    internal static void Sort(List<TradeRowData> rows) {
        int zoneCount = 0;
        foreach (var row in rows) if (row.CoItem == null && row.JsonZone != null) zoneCount++;
        if (zoneCount == 0) return;
        // UI rebuild only, never a simulation tick. Preserve personal-item
        // ordering and retain no row/ship references after the call.
        var zones = new List<TradeRowData>(zoneCount);
        int write = 0;
        for (int read = 0; read < rows.Count; read++) {
            var row = rows[read];
            if (row.CoItem == null && row.JsonZone != null) zones.Add(row);
            else rows[write++] = row;
        }
        rows.RemoveRange(write, rows.Count - write);
        zones.Sort(order);
        rows.AddRange(zones);
    }
}

[HarmonyPatch(typeof(GUITrade), "BuildSellList")]
internal static class BarterZoneListPatch {
    static void Prefix(List<TradeRowData> tradeRows) { BarterZones.Sort(tradeRows); }
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var name = AccessTools.PropertyGetter(typeof(JsonZone), nameof(JsonZone.strName));
        int matches = 0;
        foreach (var instruction in instructions) {
            if (instruction.Calls(name)) {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(BarterZones), nameof(BarterZones.Key));
                matches++;
            }
            yield return instruction;
        }
        if (matches != 1) throw new InvalidOperationException("Barter sell-list IL changed; expected one zone row key.");
    }
}

[HarmonyPatch(typeof(GUITradeRowContainer), "SetHeader")]
internal static class BarterZoneHeaderPatch {
    static readonly AccessTools.FieldRef<GUITradeRowContainer, TMP_Text> Name = AccessTools.FieldRefAccess<GUITradeRowContainer, TMP_Text>("txtCoName");
    static void Postfix(GUITradeRowContainer __instance, TradeRowData trd, JsonZone jZone) {
        if (jZone != null && trd?.CoItem == null) Name(__instance).text = BarterZones.Header(jZone);
    }
}
