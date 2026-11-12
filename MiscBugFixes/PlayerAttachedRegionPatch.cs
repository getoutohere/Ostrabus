using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;

namespace Ostrabus.MiscBugFixes;

// Regional cleanup already exempts player connections from crew recycling and
// undocking. Apply that exemption to hull destruction, too. The native global
// queue keeps the existing controller alive until it can be regional again.
internal static class PlayerAttachedRegion {
    internal const string OriginKey = "Ostrabus.PlayerAttachedRegion";
    const string Interregional = "INTERREGIONAL";
    static readonly AccessTools.FieldRef<StarSystem,Dictionary<string,string>> Owners =
        AccessTools.FieldRefAccess<StarSystem,Dictionary<string,string>>("dictShipOwners");
    static List<string> spareOwnerIds;

    // Same ownership rule as StarSystem.GetShipsForOwner, with reusable storage.
    // Borrow the list to allow reentry; clear it so no save's IDs are retained.
    static List<string> BorrowOwnerIds() {
        var ids = spareOwnerIds ?? new List<string>();
        spareOwnerIds = null;
        string player = CrewSim.coPlayer?.strID;
        if (!string.IsNullOrEmpty(player) && CrewSim.system != null)
            foreach (var entry in Owners(CrewSim.system)) if (entry.Value == player) ids.Add(entry.Key);
        return ids;
    }
    static void ReturnOwnerIds(List<string> ids) { ids.Clear(); spareOwnerIds = ids; }

    internal static bool Retain(AIShip ai, Ship atcLast) {
        var ship = ai?.Ship;
        if (ship == null || ship.bDestroyed || ship.objSS == null || ship.objSS.bIsBO || CrewSim.coPlayer == null) return false;
        var ids = BorrowOwnerIds();
        try {
            if (!ship.IsDockedWithPlayer(ids)) return false;
        } finally { ReturnOwnerIds(ids); }
        // All native controllers have a blackboard. Even malformed controllers
        // must not cause a player-connected hull to be destroyed.
        if (ai.Blackboard == null) return true;
        if (!ai.Blackboard.HasKey(OriginKey)) ai.Blackboard.Set(OriginKey, atcLast?.strRegID ?? AIShipManager.strATCLast);
        foreach (var bucket in AIShipManager.dictAIs.Values) bucket.Remove(ai);
        AIShipManager.AddAIToDict(ai, Interregional);
        return true;
    }

    // Called only at native queue refill, not on every Update. Unmarked freight
    // and other genuine interregional controllers keep their native membership.
    internal static List<AIShip> LocalForScheduling(string region, AIType type) {
        if (AIShipManager.dictAIs.TryGetValue(Interregional, out var global)) {
            List<string> ids = null;
            try {
                for (int i = global.Count - 1; i >= 0; i--) {
                    var ai = global[i];
                    if (ai?.Blackboard?.HasKey(OriginKey) != true) continue;
                    var ship = ai.Ship;
                    if (ship == null || ship.bDestroyed || ship.objSS == null || CrewSim.system == null || CrewSim.coPlayer == null) continue;
                    ids = ids ?? BorrowOwnerIds();
                    if (ship.IsDockedWithPlayer(ids)) continue;
                    var station = CrewSim.system.GetNearestStationRegional(ship.objSS.vPosx, ship.objSS.vPosy);
                    if (station == null) continue;
                    global.RemoveAt(i);
                    ai.Blackboard.Remove(OriginKey);
                    AIShipManager.AddAIToDict(ai, station.strRegID);
                }
            } finally { if (ids != null) ReturnOwnerIds(ids); }
        }
        return AIShipManager.GetShipsOfTypeForRegion(region, type);
    }
}

[HarmonyPatch(typeof(AIShipManager), "AIShipCleanup")]
internal static class PlayerAttachedRegionCleanupPatch {
    static bool Prefix(AIShip aiShip, Ship atcLast) => !PlayerAttachedRegion.Retain(aiShip, atcLast);
}

[HarmonyPatch(typeof(AIShipManager), "RunAIQueue")]
internal static class PlayerAttachedRegionSchedulingPatch {
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var code = new List<CodeInstruction>(instructions);
        var lookup = AccessTools.Method(typeof(AIShipManager), nameof(AIShipManager.GetShipsOfTypeForRegion), new[] { typeof(string), typeof(AIType) });
        var replacement = AccessTools.Method(typeof(PlayerAttachedRegion), nameof(PlayerAttachedRegion.LocalForScheduling));
        var localRegion = AccessTools.Field(typeof(AIShipManager), nameof(AIShipManager.strATCLast));
        int matches = 0;
        for (int i = 2; i < code.Count; i++) {
            if (!code[i].Calls(lookup)) continue;
            if (matches++ != 0) continue;
            if (!code[i - 2].LoadsField(localRegion)) throw new InvalidOperationException("Unknown regional AI queue refill; expected the local ATC lookup first.");
            code[i].opcode = OpCodes.Call;
            code[i].operand = replacement;
        }
        if (matches != 2) throw new InvalidOperationException("Expected two native AI queue refill lookups; found " + matches);
        return code;
    }
}
