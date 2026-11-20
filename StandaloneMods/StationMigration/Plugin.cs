using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Newtonsoft.Json.Linq;

namespace Ostrabus.StationMigration;

[BepInPlugin(Id, "Station Kiosk Migration", "0.1.4")]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Id = "com.ostrabus.stationmigration";
    internal const string NativeHash1101 = "967927e609d93e5233fe16260d61de3cb4aa5d753b738ed8a6ba1dba4cf9fde0";
    internal const string NativeHash1102 = "133a9cc94ad4f4d5db951b65249b6cdf78f53063419d836b8871b92960ec3c37";
    internal const string NativeHash1103 = "e9e7b8796423701c157ea5718cb407e6517ac0435ef947062c9a27464f4bc82f";
    internal const string NativeHash1104 = "904c72870f55124bbe3f9ee924a4a650279c362541b5795174d0cf940b97c474";
    internal static int RespawnSitesFor(string hash) => hash == NativeHash1101 ? 1 : hash == NativeHash1102 || hash == NativeHash1103 || hash == NativeHash1104 ? 2 : 0;
    internal static readonly int NativeRespawnSites = RespawnSitesFor(AssemblyHash());
    static string AssemblyHash()
    {
        using var stream = File.OpenRead(typeof(CrewSim).Assembly.Location);
        using var hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }
    internal static ManualLogSource Log;
    Harmony harmony;

    void Awake()
    {
        Log = Logger;
        if (NativeRespawnSites == 0)
        {
            Logger.LogError("Unsupported game assembly. Station protection is NOT active. This package requires a reviewed Ostranauts 1.1.0.1, 1.1.0.2, 1.1.0.3 or 1.1.0.4 assembly.");
            return;
        }
        harmony = new Harmony(Id);
        harmony.PatchAll(typeof(Plugin).Assembly);
        Logger.LogInfo("Non-destructive station migration installed. Native kiosk regeneration is intercepted; changes persist through ordinary saving.");
    }

    void OnDestroy() => harmony?.UnpatchSelf();
}

[HarmonyPatch]
internal static class LoaderPatch
{
    internal static MethodBase TargetMethod() => AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(CrewSim), "DoLoadGame"));

    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        var init = AccessTools.Method(typeof(StarSystem), nameof(StarSystem.Init), new[] { typeof(JsonStarSystemSave), typeof(JsonShip[]) });
        var respawn = AccessTools.Method(typeof(DebugRespawnShip), nameof(DebugRespawnShip.RespawnShip));
        int initializations = 0, protectedRespawns = 0;
        bool kioskBlock = false;
        foreach (var instruction in code)
        {
            if (instruction.Calls(init))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(LoaderPatch), nameof(Init));
                initializations++;
            }
            if (instruction.opcode == OpCodes.Ldstr && (string)instruction.operand == "Applying " + Migration.Marker) kioskBlock = true;
            if (kioskBlock && instruction.Calls(respawn))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(LoaderPatch), nameof(PreventRegeneration));
                protectedRespawns++;
            }
        }
        if (initializations != 1 || Plugin.NativeRespawnSites == 0 || protectedRespawns != Plugin.NativeRespawnSites)
            throw new InvalidOperationException("Station migration loader contract changed: init=" + initializations + ", kiosk respawn=" + protectedRespawns);
        return code;
    }

    internal static IEnumerator Init(StarSystem world, JsonStarSystemSave system, JsonShip[] ships)
    {
        bool missingSubstationUpgrade = ships.Any(ship => ship != null && Migration.CorrectedTargets.Contains(ship.strRegID) &&
            !Migration.Completed(ship.shipCO?.aConds));
        // 1.1.0.2 retries the two substations for saves originating in 1.1.0.1
        // too. Complete their saved-item upgrades before that new respawn gate.
        var versionGate = new[] { 1, 1, 0, Plugin.NativeRespawnSites == 2 ? 2 : 1 };
        if (CrewSim.VersionIsOlderThan(CrewSim.aSaveVersion, versionGate) &&
            (!CrewSim.aPatchesApplied.Contains(Migration.Marker) || missingSubstationUpgrade))
        {
            try
            {
                using var stream = typeof(Plugin).Assembly.GetManifestResourceStream("StationMigration.Manifest");
                using var reader = new StreamReader(stream);
                var manifest = (JArray)JObject.Parse(reader.ReadToEnd())["templates"];
                var plans = NativeAdapter.Prepare(ships, manifest, NativeAdapter.Assets(),
                    id => DataHandler.dictCOSaves.TryGetValue(id, out var record) ? JObject.FromObject(record) : null,
                    ResolveTemplate, system.dfEpoch);
                NativeAdapter.Commit(ships, CrewSim.aPatchesApplied, DataHandler.dictCOSaves, plans);
                foreach (var plan in plans) Plugin.Log?.LogInfo(plan.ToString());
                Plugin.Log?.LogInfo("Station migration complete. Save normally; this plugin can then be removed. Native completion flags have been set.");
            }
            catch (Exception error)
            {
                Plugin.Log?.LogError("Station migration stopped before station construction. Native regeneration remains blocked. " + error);
                throw;
            }
        }
        return world.Init(system, ships);
    }

    static string ResolveTemplate(string name)
    {
        if (DataHandler.dictStarSystems.TryGetValue("NewGame", out var defaults))
        {
            var station = defaults.aSpawnStations?.FirstOrDefault(value => value.strName == name);
            if (station != null) return station.strShipType;
        }
        return name;
    }

    internal static void PreventRegeneration(string strRegID, bool bNPCs, string strShipTemplate = null) =>
        throw new InvalidOperationException("Refusing native kiosk station regeneration after pre-load migration: " + strRegID);
}
