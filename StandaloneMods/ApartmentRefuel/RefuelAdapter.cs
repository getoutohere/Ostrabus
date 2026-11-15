using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ostrabus.ApartmentRefuel;

// Lives only with the native refuel window. It stores no inventory references,
// runs no Update, and is destroyed when native IGUIHarness closes the window.
internal sealed class RefuelQuote : MonoBehaviour {
    internal bool Valid;
    internal ulong Stamp;
    internal string VesselId;
    void OnDisable() { Valid = false; VesselId = null; }
}

internal static class RefuelAdapter {
    internal static readonly AccessTools.FieldRef<GUIStationRefuel, Ship> Selected = AccessTools.FieldRefAccess<GUIStationRefuel, Ship>("ship");
    static readonly AccessTools.FieldRef<GUIStationRefuel, Button> Submit = AccessTools.FieldRefAccess<GUIStationRefuel, Button>("btnSubmit");
    static readonly AccessTools.FieldRef<GUIStationRefuel, TMP_Text> Message = AccessTools.FieldRefAccess<GUIStationRefuel, TMP_Text>("txtMessage");
    static readonly AccessTools.FieldRef<GUIStationRefuel, List<CondOwner>> Nitrogen = AccessTools.FieldRefAccess<GUIStationRefuel, List<CondOwner>>("aAirpumpN2Cans");
    static readonly FieldInfo[] PurchaseRows = Rows("rowFuelHe3", "rowFuelH2", "rowFuelRCS", "rowFuelABL", "rowLifeO2", "rowLifeN2", "rowLifeBlack", "rowPower");
    static readonly FieldInfo LedgerPayors = AccessTools.Field(typeof(Ledger), "dictPayors");
    static readonly string[] ContainerTriggers = { "TIsCanisterLHe02Installed", "TIsCanisterLH02Installed", "TIsFusionReactorCore01Installed", "TIsRTAO2Installed", "TIsShipBatteryInstalled" };
    static readonly string[] TankValues = { "StatVolume", "StatSolidHe3", "StatLiqD2O", "StatICABLWall", "StatGasPressure", "StatGasPressureMax", "StatGasTemp", "StatPower" };

    static FieldInfo[] Rows(params string[] names) {
        var result = new FieldInfo[names.Length];
        for (int i = 0; i < names.Length; i++) result[i] = AccessTools.Field(typeof(GUIStationRefuel), names[i]);
        return result;
    }

    internal static bool Private(GUIStationRefuel panel) => Runtime.Private(panel.COSelf);
    internal static CondOwner Payer(GUIStationRefuel panel) => panel.COSelf?.GetInteractionCurrent()?.objThem;
    static bool Connected(GUIStationRefuel panel) => Runtime.Ready && Runtime.Working(panel.COSelf) &&
        Eligibility.Apartment(panel.COSelf.ship, out _) && Eligibility.User(Payer(panel));

    static RefuelQuote Quote(GUIStationRefuel panel) => (RefuelQuote)panel.GetComponent(typeof(RefuelQuote)) ??
        (RefuelQuote)panel.gameObject.AddComponent(typeof(RefuelQuote));

    internal static List<Ship> Vessels(Ship apartment, GUIStationRefuel panel) {
        var ships = apartment.GetDockedShips();
        if (!Private(panel)) return ships;
        for (int i = ships.Count - 1; i >= 0; i--)
            if (!Connected(panel) || !Eligibility.Vessel(apartment, ships[i]) || ships.IndexOf(ships[i]) != i) ships.RemoveAt(i);
        return ships;
    }

    internal static string ProviderId(Ship nativeProvider, GUIStationRefuel panel) =>
        Private(panel) ? Eligibility.Provider(panel.COSelf.ship)?.strRegID : nativeProvider.strRegID;

    internal static List<CondOwner> Filter(List<CondOwner> items, Ship target) {
        for (int i = items.Count - 1; i >= 0; i--) {
            var item = items[i];
            if (item == null || item.bDestroyed || !ReferenceEquals(item.ship, target) ||
                item.GetCondAmount("IsInstalled") <= 0 || items.IndexOf(item) != i) items.RemoveAt(i);
        }
        return items;
    }

    internal static List<CondOwner> Query(Ship ship, CondTrigger trigger, bool sub, bool docked, bool locked, GUIStationRefuel panel) {
        var items = ship.GetICOs1(trigger, sub, docked, locked);
        return Private(panel) ? Filter(items, Selected(panel)) : items;
    }

    internal static void At(Ship ship, Vector2 position, CondTrigger trigger, bool docked, bool locked, List<CondOwner> items, GUIStationRefuel panel) {
        ship.GetCOsAtWorldCoords1(position, trigger, docked, locked, items);
        if (!Private(panel)) return;
        Filter(items, Selected(panel));
        // Native setup deduplicates its cached recipients only after adding
        // their capacity. Exclude previously visited pump inputs before that
        // arithmetic so preview and native delivery count each physical tank once.
        var seen = Nitrogen(panel);
        if (seen != null) for (int i = items.Count - 1; i >= 0; i--)
            if (seen.Contains(items[i])) items.RemoveAt(i);
    }

    internal static List<CondOwner> RcsCans(Ship ship, GUIStationRefuel panel) {
        var items = ship.GetRCSCans();
        return Private(panel) ? Filter(items, ship) : items;
    }

    internal static double GasCapacity(CondOwner can, double molMass) => can.GetCondAmount("StatVolume") *
        can.GetCondAmount("StatGasPressureMax") / .008314000442624092 / can.GetCondAmount("StatGasTemp") * molMass;

    static double RcsQuantity(Ship ship, bool maximum) {
        double total = 0;
        foreach (var can in Filter(ship.GetRCSCans(), ship)) {
            if (can.GasContainer == null) continue;
            double capacity = GasCapacity(can, .028013398870825768);
            total += maximum ? capacity : can.GetCondAmount("StatGasPressure") / can.GetCondAmount("StatGasPressureMax") * capacity;
        }
        return total;
    }

    internal static double RcsRemain(Ship ship, GUIStationRefuel panel) => Private(panel) ? RcsQuantity(ship, false) : ship.GetRCSRemain();
    internal static double RcsMax(Ship ship, GUIStationRefuel panel) => Private(panel) ? RcsQuantity(ship, true) : ship.GetRCSMax();

    internal static List<CondOwner> LifeNitrogen(Ship ship) {
        var result = new List<CondOwner>();
        var trigger = DataHandler.GetCondTrigger("TIsRTAN2Installed");
        foreach (var pump in Filter(ship.GetICOs1(DataHandler.GetCondTrigger("TIsAirPump02Installed"), false, false, false), ship)) {
            foreach (var point in pump.mapPoints) {
                if (point.Key.IndexOf("GasInput", StringComparison.Ordinal) < 0) continue;
                var found = new List<CondOwner>();
                pump.ship.GetCOsAtWorldCoords1(pump.GetPos(point.Key), trigger, true, false, found);
                foreach (var can in Filter(found, ship)) if (!result.Contains(can)) result.Add(can);
            }
        }
        return result;
    }

    // Small, process-local quote fingerprint; no journal or world history. A
    // changed tank/debt/target forces review rather than charging an old quote.
    internal static ulong Fingerprint(GUIStationRefuel panel) {
        ulong hash = 14695981039346656037UL;
        var target = Selected(panel);
        var payer = Payer(panel);
        string provider = ProviderId(null, panel) + DataHandler.GetString("GUI_REFUEL_PORT_SUFFIX");
        Hash(ref hash, target.strRegID); Hash(ref hash, payer.strID); Hash(ref hash, provider);
        foreach (string trigger in ContainerTriggers) {
            Hash(ref hash, trigger);
            var items = Filter(target.GetICOs1(DataHandler.GetCondTrigger(trigger), true, false, trigger != "TIsShipBatteryInstalled"), target);
            HashItems(ref hash, items, trigger == "TIsShipBatteryInstalled");
        }
        HashItems(ref hash, Filter(target.GetRCSCans(), target), false);
        HashItems(ref hash, LifeNitrogen(target), false);
        foreach (var line in Ledger.GetUnpaidLIs(provider, payer.strID, null, false, target.strRegID)) {
            Hash(ref hash, line.strDesc); Hash(ref hash, line.strObjId);
            Hash(ref hash, BitConverter.DoubleToInt64Bits(line.fAmount));
        }
        return hash;
    }

    static void HashItems(ref ulong hash, List<CondOwner> items, bool batteries) {
        Hash(ref hash, items.Count);
        foreach (var item in items) {
            Hash(ref hash, item.strID); Hash(ref hash, item.strCODef);
            foreach (string name in TankValues) Hash(ref hash, BitConverter.DoubleToInt64Bits(item.GetCondAmount(name)));
            if (batteries) {
                var power = (Powered)item.GetComponent(typeof(Powered));
                Hash(ref hash, power == null ? -1L : BitConverter.DoubleToInt64Bits(power.PowerStoredMax));
            }
        }
    }

    static void Hash(ref ulong hash, string value) {
        if (value != null) foreach (char c in value) { hash ^= c; hash *= 1099511628211UL; }
        hash ^= 0xffff; hash *= 1099511628211UL;
    }
    static void Hash(ref ulong hash, long value) {
        unchecked { for (int i = 0; i < 8; i++) { hash ^= (byte)value; hash *= 1099511628211UL; value >>= 8; } }
    }

    internal static bool BeginSetup(GUIStationRefuel panel, Ship target) {
        if (!Private(panel)) return true;
        Quote(panel).Valid = false;
        if (!Connected(panel) || (target != null && !Eligibility.Vessel(panel.COSelf.ship, target))) {
            Selected(panel) = null;
            SetAvailable(panel, false);
            Explain(panel, "Station service connection unavailable. Reopen the terminal.");
            return false;
        }
        return true;
    }

    internal static void EndSetup(GUIStationRefuel panel) {
        if (!Private(panel)) return;
        var quote = Quote(panel);
        var target = Selected(panel);
        quote.Valid = Connected(panel) && Eligibility.Vessel(panel.COSelf.ship, target);
        quote.VesselId = quote.Valid ? target.strRegID : null;
        if (quote.Valid) quote.Stamp = Fingerprint(panel);
        SetAvailable(panel, quote.Valid);
        if (!quote.Valid) Explain(panel, Connected(panel) ? "No vessel is directly docked to this residence." : "Station service connection unavailable.");
    }

    internal static bool BeginSubmit(GUIStationRefuel panel) {
        if (!Private(panel)) return true;
        var quote = Quote(panel);
        var target = Selected(panel);
        bool valid = quote.Valid && Connected(panel) && Eligibility.Vessel(panel.COSelf.ship, target) &&
            quote.VesselId == target.strRegID && quote.Stamp == Fingerprint(panel) && ValidRows(panel);
        if (valid) {
            if (LedgerAccepts(panel)) return true;
            Explain(panel, "An identical charge exists at this game time. Choose a different amount, or close the terminal and briefly resume the game.");
            return false;
        }
        quote.Valid = false;
        if (Connected(panel)) Setup.Invoke(panel, new object[] { Eligibility.Vessel(panel.COSelf.ship, target) ? target : null });
        else SetAvailable(panel, false);
        Explain(panel, "Service connection or tank state changed. Review a fresh quote before paying.");
        return false;
    }

    static bool LedgerAccepts(GUIStationRefuel panel) {
        // Ledger.AddLI silently discards matching same-time entries, including
        // paid ones. Native OnSubmit would still deliver their resources. Guard
        // this terminal's transaction before mutation; never rewrite history,
        // invent descriptions/timestamps or alter unrelated public kiosks.
        var payer = Payer(panel);
        if (!(LedgerPayors.GetValue(null) is Dictionary<string, List<LedgerLI>> payors)) return false;
        if (!payors.TryGetValue(payer.strID, out var history)) return true;
        string provider = ProviderId(null, panel) + DataHandler.GetString("GUI_REFUEL_PORT_SUFFIX");
        foreach (var field in PurchaseRows) {
            var row = (GUIStationRow)field.GetValue(panel);
            if (Math.Abs(row.fTotal) < .005f) continue;
            var candidate = new LedgerLI(provider, payer.strID, row.fTotal, row.strService, Selected(panel).strRegID);
            foreach (var line in history) if (LedgerLI.Same(line, candidate)) return false;
        }
        return true;
    }

    internal static bool ValidRows(GUIStationRefuel panel) {
        foreach (var field in PurchaseRows) {
            var row = (GUIStationRow)field.GetValue(panel);
            if (row == null || !Finite(row.fPurchased) || !Finite(row.fMax) || !Finite(row.fOnBoard) ||
                !Finite(row.fPrice) || !Finite(row.fTotal) || row.fPrice < 0 ||
                row.fPurchased > Math.Max(0, row.fMax - row.fOnBoard) + Math.Max(.001f, row.fMax * .000001f) ||
                Math.Abs(row.fTotal - row.fPurchased * row.fPrice) > Math.Max(.01f, Math.Abs(row.fTotal) * .000001f)) return false;
            if (row.fPurchased < 0) {
                // The native slider computes max * (onboard / max) - onboard.
                // Its lower stop can round just below zero. Do not turn that
                // noise into a blocked purchase or a fractional fuel refund.
                float rounding = Math.Max(.00001f, Math.Max(Math.Abs(row.fMax), Math.Abs(row.fOnBoard)) * .0000003f);
                if (row.fPurchased < -rounding) return false;
                row.fPurchased = 0;
                row.fTotal = 0;
            }
        }
        return true;
    }

    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static readonly MethodInfo Setup = AccessTools.Method(typeof(GUIStationRefuel), "SetupFields");
    static void SetAvailable(GUIStationRefuel panel, bool available) { if (Submit(panel) != null) Submit(panel).interactable = available; }
    static void Explain(GUIStationRefuel panel, string message) { if (Message(panel) != null) Message(panel).text = message; }
}
