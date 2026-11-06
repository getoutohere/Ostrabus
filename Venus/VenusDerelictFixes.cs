using System;
using HarmonyLib;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;

namespace Ostrabus.Venus;

internal static class VenusDerelicts {
    internal const string VenusLoot = "RandomDerelictVenus";
    internal const double VenusChance = 0.70;
    internal static bool IsVorbOrbit(BodyOrbit orbit) {
        return orbit != null && (orbit.strName == "VORB" || (orbit.strName != null && orbit.strName.StartsWith("VORB|", StringComparison.Ordinal)));
    }
    internal static bool Eligible(Ship ship) {
        return ship != null && !ship.bDestroyed && ship.objSS != null && !ship.objSS.bIsBO && ship.DMGStatus == Ship.Damage.Derelict;
    }
    internal static string ChooseLoot(string station, bool poolExists, double roll) {
        return station == "VORB" && poolExists && roll < VenusChance ? VenusLoot : "RandomShip";
    }
}

[HarmonyPatch(typeof(AnchorDockedShip), nameof(AnchorDockedShip.RunCommand))]
internal static class VorbAllowsAnchoringPatch {
    static void Prefix(AnchorDockedShip __instance) {
        try {
            if (__instance?.ShipUs?.origin != "VORB") return;
            Ship station = CrewSim.system?.GetShipByRegID("VORB");
            if (station?.ShipCO == null || station.ShipCO.HasCond("StationAllowsAnchoring")) return;
            station.ShipCO.AddCondAmount("StationAllowsAnchoring", 1d, 0d, 0f);
            Ostrabus.OstrabusPlugin.Logger?.LogInfo("[Venus] Enabled VORB's vanilla derelict anchoring condition.");
        } catch (Exception ex) { Ostrabus.OstrabusPlugin.Logger?.LogWarning("[Venus] Could not enable VORB anchoring: " + ex.Message); }
    }
}

[HarmonyPatch(typeof(HaulerDeployingPilot), "SpawnDerelict")]
internal static class VorbWreckSpawnPatch {
    [ThreadStatic] internal static string CurrentLoot;
    static void Prefix(string stationID, out string __state) {
        __state = CurrentLoot;
        CurrentLoot = null;
        try {
            if (stationID != "VORB" || DataHandler.dictLoot == null || !DataHandler.dictLoot.ContainsKey(VenusDerelicts.VenusLoot)) return;
            // One choice per delivery, including vanilla's retry when its first spawn fails.
            CurrentLoot = VenusDerelicts.ChooseLoot(stationID, true, UnityEngine.Random.value);
        } catch (Exception ex) { Ostrabus.OstrabusPlugin.Logger?.LogWarning("[Venus] Could not choose wreck pool: " + ex.Message); }
    }
    static Exception Finalizer(Exception __exception, string __state) {
        CurrentLoot = __state;
        return __exception;
    }
}

[HarmonyPatch(typeof(StarSystem), nameof(StarSystem.AddDerelict), new[] { typeof(string), typeof(string), typeof(bool) })]
internal static class VorbWreckLootPatch {
    static void Prefix(ref string strLoot) {
        if (strLoot == "RandomShip" && VorbWreckSpawnPatch.CurrentLoot != null) strLoot = VorbWreckSpawnPatch.CurrentLoot;
    }
}
