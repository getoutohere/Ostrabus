using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Newtonsoft.Json;
using SolarSystem;

namespace Ostrabus.SaveMigration110;

[BepInPlugin(Id, "Optional 1.1 Save Migration", "0.1.0")]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Id = "com.ostrabus.savemigration110";
    internal const string NativeHash = "a79a5081d9c94307a45fe1d0b55ff1aa1d0af6ea8dfce1f9acc5961cf260ef07";
    internal static ManualLogSource Log;
    internal static bool Ready;
    Harmony harmony;

    void Awake()
    {
        Log = Logger;
        try
        {
            using var stream = File.OpenRead(typeof(StarSystem).Assembly.Location);
            using var hash = SHA256.Create();
            string actual = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            if (actual != NativeHash) throw new InvalidOperationException("This plugin supports only the reviewed Ostranauts 1.1.0.0 assembly.");
            harmony = new Harmony(Id);
            harmony.PatchAll(typeof(Plugin).Assembly);
            CrewSim.OnGameFinishedLoading.AddListener(Loaded);
            Ready = true;
            Logger.LogInfo("Read-only load summary enabled. Use migrate110 plan, then migrate110 apply when wanted.");
        }
        catch (Exception error)
        {
            Ready = false;
            harmony?.UnpatchSelf();
            Logger.LogError("Migration disabled: " + error);
        }
    }

    void Loaded()
    {
        if (!Ready || CrewSim.bShipEdit) return;
        // Native DoLoadGame fires this event before setting FinishedLoading.
        // This callback only observes; command-driven writes require the flag.
        try { Commands.Describe(Commands.Plan(allowLoadingSummary: true), false); }
        catch (Exception error) { Logger.LogWarning("Migration plan unavailable: " + error.Message); }
    }

    void OnDestroy()
    {
        Ready = false;
        CrewSim.OnGameFinishedLoading.RemoveListener(Loaded);
        harmony?.UnpatchSelf();
    }
}

internal static class Commands
{
    internal static MigrationPlan Plan(bool allowLoadingSummary = false)
    {
        if (!Plugin.Ready || CrewSim.system == null || CrewSim.objInstance == null || CrewSim.bShipEdit
            || !CrewSim.objInstance.FinishedLoading && !allowLoadingSummary)
            throw new InvalidOperationException("Wait for the saved game to finish loading.");
        DataHandler.dictStarSystems.TryGetValue("NewGame", out var defaults);
        return Migration.Plan(CrewSim.system, defaults, DataHandler.dictEnvironmentMaps,
            CrewSim.strSaveVersion, CrewSim.aPatchesApplied);
    }

    internal static void Describe(MigrationPlan plan, bool console)
    {
        Print("Plan: " + plan.Changes + " map assignments; " + (plan.CanApply ? "preflight passed." : "BLOCKED; no changes will be made."), console);
        foreach (var row in plan.Bodies)
            Print(row.Name + ": " + row.CurrentMap + " -> " + row.ExpectedMap + " [" + row.Action + "] " + row.Reason, console);
        Print("Existing wrecks, depletion, ships, crew, inventory and markets are preserved. No content is spawned by this command.", console);
        var counts = new SortedDictionary<int, int>();
        foreach (var stellar in CrewSim.system.dictStellarObjects)
        {
            int map = stellar is StellarDerelict wreck ? wreck.MapID : stellar is StellarShip ship ? ship.MapID : 0;
            if (map > 0) counts[map] = counts.TryGetValue(map, out int count) ? count + 1 : 1;
        }
        foreach (var ship in CrewSim.system.dictShips.Values)
        {
            if (ship == null || ship.bDestroyed || ship.ShipCO == null || !ship.ShipCO.HasCond("IsEnvironmentEntity")) continue;
            int map = EnvironmentEntities.GetMapID(ship);
            if (map > 0) counts[map] = counts.TryGetValue(map, out int count) ? count + 1 : 1;
        }
        Print("Native environment objects by map: " + string.Join(", ", counts.Select(pair => pair.Key + "=" + pair.Value))
            + ". Counts are observations, not refill targets.", console);
        Plugin.Log?.LogInfo("Structured migration plan: " + JsonConvert.SerializeObject(plan));
    }

    static void Print(string text, bool console)
    {
        string message = "[Migration110] " + text;
        Plugin.Log?.LogInfo(message);
        if (console) ConsoleToGUI.instance?.LogInfo(message);
    }

    internal static bool Handle(string input)
    {
        string[] words = (input ?? "").Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0 || !words[0].Equals("migrate110", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            if (words.Length == 1 || words.Length == 2 && (words[1] == "plan" || words[1] == "status"))
                Describe(Plan(), true);
            else if (words.Length == 2 && words[1] == "apply")
            {
                var plan = Plan();
                Describe(plan, true);
                int changed = Migration.Apply(plan, CrewSim.system, CrewSim.aPatchesApplied);
                Print(changed + " native body map assignments applied. Save normally to persist; the plugin can then be removed.", true);
            }
            else Print("Usage: migrate110 plan | apply | status", true);
        }
        catch (Exception error)
        {
            Print("Stopped: " + error.Message, true);
            Plugin.Log?.LogError(error);
        }
        return true;
    }
}

[HarmonyPatch(typeof(ConsoleResolver), nameof(ConsoleResolver.ResolveString))]
internal static class MigrationCommandPatch
{
    static bool Prefix(string strInput) => !Commands.Handle(strInput);
}
