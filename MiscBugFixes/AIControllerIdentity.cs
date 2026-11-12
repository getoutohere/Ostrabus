using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Ostranauts.Ships.AIPilots;

namespace Ostrabus.MiscBugFixes;

// Unsaved AddAIToShip already means "reuse or create", not "replace this
// mission". Its native lookup accidentally searches only the requested region.
internal static class AIControllerIdentity {
    static readonly AccessTools.FieldRef<AIShip,Ship> Hull = AccessTools.FieldRefAccess<AIShip,Ship>("_shipUs");
    internal static AIShip Existing(Ship ship) {
        if (ship == null || ship.bDestroyed || AIShipManager.dictAIs == null) return null;
        foreach (var region in AIShipManager.dictAIs.Values) {
            if (region == null) continue;
            foreach (var ai in region) if (ai != null && ai.AICharacter != null && ReferenceEquals(Hull(ai),ship)) return ai;
        }
        return null;
    }
    static bool CargoRecord(JsonAIShipSave record) => record != null && record.enumAIType == AIType.HaulerCargo &&
        record.strATCLast == AIShipManager.INTERREGIONAL_ATC && !string.IsNullOrEmpty(record.strHomeStation);
    static bool CollisionFallback(JsonAIShipSave record) => record != null &&
        (record.enumAIType == AIType.Scav || record.enumAIType == AIType.Police) &&
        !string.IsNullOrEmpty(record.strATCLast) && record.strATCLast != AIShipManager.INTERREGIONAL_ATC &&
        record.strHomeStation == record.strATCLast;
    internal static int RepairSavedCargoDuplicates(JsonAIShipManagerSave save) {
        var records=save?.aAIShips;
        var world=CrewSim.system;
        if (records == null || world?.dictShips == null) return 0;
        HashSet<JsonAIShipSave> discard=null;
        foreach (var group in records.Where(r=>r != null && !string.IsNullOrEmpty(r.strRegId)).GroupBy(r=>r.strRegId,StringComparer.Ordinal)) {
            if (group.Count()<2) continue;
            var cargo=group.Where(CargoRecord).ToArray();
            if (cargo.Length!=1 || group.Any(r=>!ReferenceEquals(r,cargo[0]) && !CollisionFallback(r))) continue;
            if (!world.dictShips.TryGetValue(group.Key,out var ship) || ship == null || ship.bDestroyed || ship.objSS == null ||
                ship.objSS.bIsBO || !ship.IsAIShip || ship.origin != cargo[0].strHomeStation ||
                world.GetShipOwner(group.Key) != cargo[0].strHomeStation+"CargoHauler") continue;
            // This exact source-of-truth combination matches native cargo
            // creation and the collision bug. Never infer a new role/route from
            // a market, discard ambiguous records or revive a missing hull.
            if (discard == null) discard=new HashSet<JsonAIShipSave>();
            foreach (var record in group) if (!ReferenceEquals(record,cargo[0])) discard.Add(record);
        }
        if (discard == null) return 0;
        save.aAIShips=records.Where(r=>r == null || !discard.Contains(r)).ToArray();
        return discard.Count;
    }
}

[HarmonyPatch(typeof(AIShipManager),nameof(AIShipManager.AddAIToShip),new[]{typeof(Ship),typeof(AIType),typeof(string)})]
internal static class AIControllerReusePatch {
    static bool Prefix(Ship ship, AIType at, ref AIShip __result) {
        if (at == AIType.NA) return true;
        var existing=AIControllerIdentity.Existing(ship);
        if (existing == null) return true;
        __result=existing;
        return false;
    }
}

[HarmonyPatch(typeof(AIShipManager),"ApplySaveData")]
internal static class CargoControllerSavePatch {
    static void Prefix(JsonAIShipManagerSave ss) {
        int removed=AIControllerIdentity.RepairSavedCargoDuplicates(ss);
        if (removed>0) OstrabusPlugin.Logger?.LogInfo("[Freight] Removed "+removed+" duplicate regional collision controllers; retained original cargo missions.");
    }
}
