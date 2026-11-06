using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Ostranauts.Core.Models;
using Ostranauts.Electrical;
using Ostranauts.Ships;
using Ostrabus.MiscBugFixes;
using UnityEngine;

static partial class Program {
    static JsonItem ElectricalLayout(Electrical electrical) => new JsonItem {
        strID = electrical.coSelf.strID, strName = "ItmElectricalBox01",
        aGPMSettings = new[] { new JsonGUIPropMap { strName = "Electrical", dictGUIPropMap = DataHandler.ConvertDictToStringArray(Gpm(electrical)) } }
    };
    static Dictionary<string, string> LayoutWiring(JsonShip ship, string id) => DataHandler.ConvertStringArrayToDict(ship.aItems.Single(item => item.strID == id).aGPMSettings.Single(map => map.strName == "Electrical").dictGUIPropMap);
    static void RestoreLayout(Electrical device, JsonShip ship, string id) {
        var wiring = LayoutWiring(ship, id);
        Set(device, "mapGPM", wiring); device.coSelf.mapGUIPropMaps["Electrical"] = wiring;
        device.inputConnections.Clear(); device.outputConnections.Clear();
        Init(device); // Native Electrical.InitGPMValues, not a substitute decoder.
    }
    static void CheckOffscreenPersistence() {
        var fixture = new Harmony("offline.electrical.offscreen.fixture");
        var production = new Harmony("offline.electrical.offscreen.logging");
        var oldSystem = CrewSim.system;
        var oldCOs = DataHandler.mapCOs;
        var oldSaves = DataHandler.dictCOSaves;
        try {
            fixture.Patch(AccessTools.Method(typeof(Debug), "Log", new[] { typeof(object) }), prefix: new HarmonyMethod(typeof(Program), nameof(CaptureDiagnostic)));
            production.CreateClassProcessor(typeof(ElectricalAuditLogging)).Patch();
            var source = Device(); var target = Device();
            source.coSelf.strCODef = "ItmElectricalBox01";
            source.outputConnections[target.coSelf.strID] = new ElectricalConnection(target.coSelf.strID, SignalType.On);
            source.SetGPM();
            string sourceId = source.coSelf.strID, targetId = target.coSelf.strID;
            var original = Empty<Ship>(); original.strRegID = "offscreen-test";
            original.json = new JsonShip { strRegID = original.strRegID, aItems = new[] { ElectricalLayout(source), ElectricalLayout(target) } };
            string originalOutputs = LayoutWiring(original.json, sourceId)["outputConnections"];
            Check(originalOutputs.Contains(targetId), "Offscreen persistence fixture lacks saved connection");
            var originalConditionRecord = new JsonCondOwnerSave { strID = sourceId };
            DataHandler.dictCOSaves = new Dictionary<string, JsonCondOwnerSave> { [sourceId] = originalConditionRecord, [targetId] = new JsonCondOwnerSave { strID = targetId } };
            DataHandler.mapCOs = new Dictionary<string, CondOwner>();
            var barter = Empty<BarterZoneShip>(); barter.strRegID = original.strRegID;
            barter.json = original.json.Clone(); // The native loader's actual deep layout clone.
            var mapped = new DictionaryArray<string, CondOwner>(); mapped[sourceId] = source.coSelf;
            Set(barter, "mapICOs", mapped);
            Set(source.coSelf, "_objShip", barter); source.coShip = barter;
            Check(barter.LoadState == Ship.Loaded.None && ((Ship)barter).json == null && barter.json != null, "Native offscreen clone no longer matches logged None/no-json signature");
            Check(!ReferenceEquals(original.json.aItems[0].aGPMSettings[0].dictGUIPropMap, barter.json.aItems[0].aGPMSettings[0].dictGUIPropMap), "Native ship/layout clone aliased saved wiring arrays");
            registry.Remove(targetId); diagnosticLogs.Clear();
            source.AuditConnections(); // Real audit removes saved-but-not-yet-materialized endpoint.
            Check(source.outputConnections.Count == 0 && Gpm(source)["outputConnections"] == "", "Native audit failed to prune temporary runtime wiring");
            Check(diagnosticLogs.Any(line => line.Contains("load=None") && line.Contains("savedPresent=unknown(no-json)") && line.Contains("savedCO=True")), "Did not reproduce live diagnostic signature");
            Check(LayoutWiring(original.json, sourceId)["outputConnections"] == originalOutputs && LayoutWiring(barter.json, sourceId)["outputConnections"] == originalOutputs, "Audit directly modified original or cloned layout wiring");
            // Native public save route refuses a still-loading copy.
            var loader = Empty<AsyncShipLoader>();
            Set(loader, "_loadStateDict", new Dictionary<string, IAsyncLoadable> { [original.strRegID] = barter });
            CrewSim.system = Empty<StarSystem>();
            CrewSim.system.dictShips = new Dictionary<string, Ship> { [original.strRegID] = original };
            loader.SaveChangedCO(original.strRegID);
            Check(LayoutWiring(original.json, sourceId)["outputConnections"] == originalOutputs, "Incomplete barter copy reached original save state");
            // Finish loading the missing endpoint and run the actual finalization helper.
            mapped[targetId] = target.coSelf; registry[targetId] = target.coSelf;
            AccessTools.Method(typeof(BarterZoneShip), "AddToLocalInstance").Invoke(barter, null);
            Set(barter, "_fullyLoaded", true);
            for (int i = 0; i < 2; i++) loader.SaveChangedCO(original.strRegID);
            Check(source.outputConnections.Count == 0, "Persistence test accidentally repaired runtime wiring");
            Check(LayoutWiring(original.json, sourceId)["outputConnections"] == originalOutputs, "Native barter save-back exported pruned wiring for an existing item");
            Check(ReferenceEquals(DataHandler.dictCOSaves[sourceId], originalConditionRecord), "Existing saved CO record changed during barter save-back");
            var restored = Device(); RestoreLayout(restored, original.json, sourceId);
            Check(restored.outputConnections.ContainsKey(targetId), "Normal reconstruction did not restore the original wiring");
            // The other offscreen subtype sharing the no-json signature saves nothing.
            var preview = Empty<AsyncPreviewShip>(); preview.json = original.json.Clone(); preview.strRegID = original.strRegID;
            source.coShip = preview; Set(source.coSelf, "_objShip", preview);
            RestoreLayout(source, preview.json, sourceId); registry.Remove(targetId);
            source.AuditConnections();
            Check(source.outputConnections.Count == 0, "Preview control did not reproduce temporary pruning");
            preview.SaveChangedCOs(ref original);
            Check(LayoutWiring(original.json, sourceId)["outputConnections"] == originalOutputs, "Native preview save unexpectedly persisted runtime pruning");
            Console.WriteLine("OFFSCREEN PERSISTENCE: native audit prunes clone runtime; native barter finalization/save-back twice and preview save preserve existing-item wiring; native reload restores connection.");
        } finally {
            CrewSim.system = oldSystem; DataHandler.mapCOs = oldCOs; DataHandler.dictCOSaves = oldSaves;
            production.UnpatchSelf(); fixture.UnpatchSelf(); diagnosticLogs.Clear();
        }
    }
}
