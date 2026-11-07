using HarmonyLib;
using UnityEngine;

namespace Ostrabus.MiscBugFixes;

// A replacement PDC inherits its existing contents in ModeSwitchInternal.
// Its creation must not roll another starting magazine before that transfer.
[HarmonyPatch(typeof(DataHandler), nameof(DataHandler.GetCondOwner), new[] {
    typeof(string), typeof(string), typeof(string), typeof(bool), typeof(string), typeof(JsonCondOwnerSave), typeof(string), typeof(Transform)
})]
internal static class PdcConversionAmmoPatch {
    static void Prefix(string strCO, ref bool bLoot, JsonCondOwnerSave jcos, string strIDOld) {
        if (!bLoot || jcos != null || strIDOld == null || strCO == null ||
            !strCO.StartsWith("ItmShipWeaponPDC", System.StringComparison.Ordinal)) return;
        if (DataHandler.dictCOs == null || !DataHandler.dictCOs.TryGetValue(strCO, out var definition) || definition == null ||
            definition.strLoot != "ItmLootSpawnPDC20mm" || DataHandler.mapCOs == null ||
            !DataHandler.mapCOs.TryGetValue(strIDOld, out var old) || old == null || old.bDestroyed || !old.HasCond("IsShipWeaponPDC")) return;
        bLoot = false;
    }
}
