using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Ostranauts.Electrical;
using Ostrabus.MiscBugFixes;
using UnityEngine;

static partial class Program {
    static int lookupCalls, visibleInfoCalls;
    static readonly List<string> diagnosticLogs = new List<string>();
    static bool VisibleInfo(string logString) { visibleInfoCalls++; return false; }
    static bool CaptureDiagnostic(object message) { if (message is string text) diagnosticLogs.Add(text); return false; }
    static void CheckAuditLogging() {
        var patch = new Harmony("offline.signalbox.audit.logging.production");
        var fixture = new Harmony("offline.signalbox.audit.logging.capture");
        var previousMap = DataHandler.mapCOs;
        var previousSaves = DataHandler.dictCOSaves;
        try {
            fixture.Patch(AccessTools.Method(typeof(Debug), "Log", new[] { typeof(object) }), prefix: new HarmonyMethod(typeof(Program), nameof(CaptureDiagnostic)));
            patch.CreateClassProcessor(typeof(ElectricalAuditLogging)).Patch();
            patch.CreateClassProcessor(typeof(ElectricalAuditConsoleFilter)).Patch();
            var box = Device(); var valid = Device(); var noElectrical = Device(); var moved = Device();
            Set(box.coShip, "nLoadState", Ship.Loaded.Shallow); box.coShip.strRegID = "audit-ship";
            moved.coShip.strRegID = "elsewhere";
            registry.Remove(moved.coSelf.strID);
            Set(noElectrical.coSelf, "elec", null);
            string absentOutput = "unmaterialized-output";
            Input(box, moved.coSelf.strID, true); Input(box, noElectrical.coSelf.strID, false);
            box.outputConnections[absentOutput] = new ElectricalConnection(absentOutput, SignalType.On);
            box.outputConnections[valid.coSelf.strID] = new ElectricalConnection(valid.coSelf.strID, SignalType.On);
            box.coShip.json = new JsonShip { aItems = new[] {
                new JsonItem { strID = moved.coSelf.strID, strName = "ItmElectricalBox01", bForceLoad = true },
                new JsonItem { strID = absentOutput, strName = "ItmFusionLaserArray01Off", strParentID = "saved-parent" }
            } };
            DataHandler.mapCOs = new Dictionary<string, CondOwner> { [moved.coSelf.strID] = moved.coSelf };
            DataHandler.dictCOSaves = new Dictionary<string, JsonCondOwnerSave> { [absentOutput] = new JsonCondOwnerSave { strID = absentOutput } };
            diagnosticLogs.Clear(); int oldLookups = lookupCalls, oldVisible = visibleInfoCalls;
            box.status = false;
            box.AuditConnections(); // Actual native audit with production transpiler installed.
            Check(lookupCalls - oldLookups == 4, "Diagnostics reran ship lookups or changed native traversal");
            Check(box.inputConnections.Count == 0 && box.outputConnections.Count == 1 && box.outputConnections.ContainsKey(valid.coSelf.strID), "Logging patch changed native removal decisions");
            Check(!box.status && !Get<bool>(box, "bNeedsCheck"), "Logging patch changed gate behavior");
            Check(Gpm(box)["inputConnections"] == "" && Gpm(box)["outputConnections"].Contains(valid.coSelf.strID), "Native persistence changed");
            Check(visibleInfoCalls == oldVisible, "Faulty-connection summary still reached ConsoleToGUI.LogInfo");
            var logs = diagnosticLogs.Where(x => x.StartsWith(ElectricalAuditLogging.Tag)).ToArray();
            Check(logs.Length == 4, "Expected three prune diagnostics and one summary");
            Check(logs.Count(x => x.Contains("direction=input")) == 2 && logs.Count(x => x.Contains("direction=output")) == 1, "Lookup direction logging incorrect");
            Check(logs.Any(x => x.Contains("reason=no-Electrical-component") && x.Contains(noElectrical.coSelf.strID)), "Missing-component reason not identified");
            Check(logs.Any(x => x.Contains(moved.coSelf.strID) && x.Contains("savedPresent=true") && x.Contains("globalCO=live") && x.Contains("globalShip=elsewhere")), "Saved/moved endpoint context missing");
            Check(logs.Any(x => x.Contains(absentOutput) && x.Contains("savedParent=saved-parent") && x.Contains("savedCO=True")), "Saved-only endpoint context missing");
            Check(logs.All(x => x.Contains("source=" + box.coSelf.strID) && x.Contains("load=Shallow")), "Source or ship load-state context missing");
            Check(logs.Any(x => x.Contains("summary") && x.Contains("Removed 3 faulty connections!")), "Native summary count not retained in diagnostic log");
            int logged = diagnosticLogs.Count;
            box.AuditConnections();
            Check(diagnosticLogs.Count == logged, "No-op audit generated removal messages");
            Set(ConsoleToGUI.instance, "doLogs", true); Set(ConsoleToGUI.instance, "bugLevel", -1); Set(ConsoleToGUI.instance, "kChars", 4096);
            ConsoleToGUI.instance.Log(logs[0], "", LogType.Log);
            Check(Get<int>(ConsoleToGUI.instance, "bugLevel") == -1, "Unity diagnostic was echoed to GUI with doLogs enabled");
            Check(ElectricalAuditConsoleFilter.Prefix("ordinary log", LogType.Log), "Unrelated Unity logs suppressed");
            Check(ElectricalAuditConsoleFilter.Prefix(ElectricalAuditLogging.Tag + "exception", LogType.Exception), "Exceptions were suppressed");
        } finally {
            DataHandler.mapCOs = previousMap; DataHandler.dictCOSaves = previousSaves;
            fixture.UnpatchSelf(); patch.UnpatchSelf(); diagnosticLogs.Clear();
        }
    }
}
