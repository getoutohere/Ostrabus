using System;
using BepInEx;
using HarmonyLib;

namespace Ostrabus.ApartmentRefuel;

[BepInPlugin(Id, "Apartment Refuel Terminal", "0.1.0")]
public sealed class Plugin : BaseUnityPlugin {
    public const string Id = "com.ostrabus.apartmentrefuel";
    Harmony harmony;

    void Awake() {
        try {
            harmony = new Harmony(Id);
            harmony.PatchAll(typeof(Plugin).Assembly);
            CrewSim.OnGameFinishedLoading.AddListener(Loaded);
            Logger.LogInfo("Apartment Refuel Terminal hooks installed; awaiting native assets.");
        } catch (Exception error) {
            Runtime.Disable();
            harmony?.UnpatchSelf();
            Logger.LogError("Apartment terminal disabled because its native integration did not bind: " + error);
        }
    }

    void Loaded() {
        try {
            Runtime.Enable();
        } catch (Exception error) {
            Runtime.Disable();
            Logger.LogError("Apartment terminal remains disabled: " + error.Message);
        }
    }

    void OnDestroy() {
        CrewSim.OnGameFinishedLoading.RemoveListener(Loaded);
        Runtime.Disable();
        harmony?.UnpatchSelf();
    }
}

internal static class Runtime {
    internal const string Item = "ItmOstrabusApartmentRefuel";
    internal const string Marker = "IsOstrabusApartmentRefuel";
    internal const string Gate = "TOstrabusApartmentRefuelReady";
    internal const string Use = "GUIOstrabusApartmentRefuel";
    internal static bool Ready { get; private set; }

    internal static bool Private(CondOwner item) => item != null && IsItem(item.strCODef);
    internal static bool IsItem(string id) => id == Item || id == Item + "Loose" ||
        id == Item + "Dmg" || id == Item + "DmgLoose";
    internal static bool InstalledDefinition(string id) => id == Item || id == Item + "Dmg";
    internal static bool Working(CondOwner item) => Private(item) && item.strCODef == Item &&
        item.GetCondAmount("IsInstalled") > 0 && item.GetCondAmount("IsDamaged") <= 0 && !item.bDestroyed;

    internal static void Enable() {
        Ready = false;
        if (DataHandler.dictCOs == null || !DataHandler.dictCOs.ContainsKey(Item)) return;
        foreach (string id in new[] { Item, Item + "Loose", Item + "Dmg", Item + "DmgLoose" }) {
            if (!DataHandler.dictCOs.TryGetValue(id, out var co) || co.strLoot != null)
                throw new InvalidOperationException("Incomplete or unexpected terminal definition: " + id);
        }
        if (DataHandler.dictCTs == null || !DataHandler.dictCTs.TryGetValue(Gate, out var gate))
            throw new InvalidOperationException("Missing private terminal integration gate.");
        if (!DataHandler.dictInteractions.ContainsKey(Use) ||
            !DataHandler.dictInteractions.ContainsKey("ACTOstrabusApartmentRefuelInstall"))
            throw new InvalidOperationException("Private terminal work actions did not load.");
        Eligibility.ValidateTemplates();
        if (gate.aTriggers.Length != 1 || (gate.aTriggers[0] != "TNever" && gate.aTriggers[0] != "Blank"))
            throw new InvalidOperationException("Unexpected private terminal integration gate.");
        // Native CondTrigger.Clone shares its trigger-name array and cached child
        // dictionaries. Changing the existing slot updates already-cloned work
        // prerequisites too; replacing the array would leave those clones closed.
        gate.aTriggers[0] = "Blank";
        gate.ValuesWereChanged = true;
        Ready = true;
    }

    internal static void Disable() {
        Ready = false;
        if (DataHandler.dictCTs != null && DataHandler.dictCTs.TryGetValue(Gate, out var gate)) {
            if (gate.aTriggers.Length == 1) gate.aTriggers[0] = "TNever";
            else gate.aTriggers = new[] { "TNever" };
            gate.ValuesWereChanged = true;
        }
    }
}
