using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace Ostrabus.MiscBugFixes;

// Changes diagnostics only. Native lookups, removal decisions, persistence and timing remain in place.
[HarmonyPatch(typeof(Electrical), nameof(Electrical.AuditConnections))]
internal static class ElectricalAuditLogging {
    internal const string Tag = "[ElectricalAudit] ";
    static IList Labels(CodeInstruction instruction) => (IList)AccessTools.Field(typeof(CodeInstruction), "labels").GetValue(instruction);
    [HarmonyTranspiler]
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var code = instructions.ToList();
        var lookup = AccessTools.Method(typeof(Ship), nameof(Ship.GetCOByID), new[] { typeof(string) });
        var report = AccessTools.Method(typeof(ConsoleToGUI), nameof(ConsoleToGUI.LogInfo));
        var sites = code.Where(i => i.Calls(lookup) || i.Calls(report)).ToList();
        if (code.Count(i => i.Calls(lookup)) != 2 || code.Count(i => i.Calls(report)) != 1 || sites.Any(i => i.blocks.Count != 0)) {
            Debug.LogWarning("[ElectricalAudit] Native audit call sites changed; logging redirection was not installed.");
            return code;
        }
        var rewritten = new List<CodeInstruction>(code.Count + 3);
        int lookups = 0;
        foreach (var instruction in code) {
            string replacement = instruction.Calls(lookup) ? (++lookups == 1 ? nameof(LookupInput) : nameof(LookupOutput)) :
                instruction.Calls(report) ? nameof(Report) : null;
            if (replacement != null) {
                var owner = new CodeInstruction(OpCodes.Ldarg_0);
                // Branches must enter before the newly added receiver argument.
                foreach (object label in Labels(instruction)) Labels(owner).Add(label);
                Labels(instruction).Clear();
                rewritten.Add(owner);
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(ElectricalAuditLogging), replacement);
            }
            rewritten.Add(instruction);
        }
        return rewritten;
    }
    static CondOwner LookupInput(Ship ship, string id, Electrical owner) => Lookup(ship, id, owner, "input");
    static CondOwner LookupOutput(Ship ship, string id, Electrical owner) => Lookup(ship, id, owner, "output");
    static CondOwner Lookup(Ship ship, string id, Electrical owner, string direction) {
        CondOwner target = ship.GetCOByID(id); // Exactly the original lookup; preserve its result and exceptions.
        if (target != null && target.Electrical != null) return target;
        try {
            string reason = target == null ? (ReferenceEquals(target, null) ? "not-in-runtime-map" : "Unity-object-destroyed") : "no-Electrical-component";
            string saved = "unknown(no-json)", savedDefinition = "-", parent = "-", force = "-";
            if (ship.json?.aItems != null) {
                saved = "false";
                int inspected = 0;
                foreach (JsonItem item in ship.json.aItems) {
                    if (++inspected > 100000) { saved = "unknown(scan-cap)"; break; }
                    if (item == null || item.strID != id) continue;
                    saved = "true"; savedDefinition = item.strName;
                    parent = item.strParentID ?? item.strSlotParentID; force = item.bForceLoad?.ToString() ?? "unspecified";
                    break;
                }
            }
            CondOwner global = null;
            bool globalEntry = id != null && DataHandler.mapCOs != null && DataHandler.mapCOs.TryGetValue(id, out global);
            string globalState = !globalEntry ? "absent" : global == null ? "null-or-destroyed" : "live";
            bool savedCO = id != null && DataHandler.dictCOSaves != null && DataHandler.dictCOSaves.ContainsKey(id);
            var connections = direction == "input" ? owner.inputConnections : owner.outputConnections;
            string wire = connections != null && id != null && connections.TryGetValue(id, out var connection) ?
                "enabled=" + connection.switchStatus + ",signal=" + connection.signalType : "unavailable";
            Write($"prune-decision {Context(owner)} direction={direction} endpoint={Clean(id)} reason={reason} " +
                $"savedPresent={saved} savedDefinition={Clean(savedDefinition)} savedParent={Clean(parent)} forceLoad={force} " +
                $"globalCO={globalState} globalShip={Clean(global == null ? null : global.ship?.strRegID)} savedCO={savedCO} wire=({wire}) " +
                $"gate={owner.gateMode} status={owner.status} override={owner.bOverride}.");
        } catch (Exception error) { Write("Could not format prune diagnostic: " + Clean(error.Message)); }
        return target;
    }
    static void Report(ConsoleToGUI unused, string message, Electrical owner) {
        try { Write("summary " + Context(owner) + " " + message); }
        catch (Exception error) { Write("Could not format audit summary: " + Clean(error.Message)); }
    }
    static string Context(Electrical owner) {
        CondOwner self = owner?.coSelf;
        Ship ship = owner?.coShip;
        return $"utc={DateTime.UtcNow:O} epoch={StarSystem.fEpoch:R} source={Clean(self == null ? null : self.strID)} " +
            $"type={Clean(self == null ? null : self.strCODef)} ship={Clean(ship?.strRegID)} load={ship?.LoadState} " +
            $"shipDestroyed={ship?.bDestroyed} actualSourceShip={Clean(self == null ? null : self.ship?.strRegID)}";
    }
    static string Clean(string text) {
        if (text == null) return "-";
        if (text.Length > 160) text = text.Substring(0, 160);
        return text.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
    }
    static void Write(string text) {
        try { Debug.Log(Tag + text); } catch { /* Logging must not change the audit's behavior. */ }
    }
}

// Unity logs can be echoed to the GUI when doLogs is enabled. Other subscribers
// (Player.log and our inspection listener) still receive these diagnostic entries.
[HarmonyPatch(typeof(ConsoleToGUI), nameof(ConsoleToGUI.Log))]
internal static class ElectricalAuditConsoleFilter {
    [HarmonyPrefix]
    internal static bool Prefix(string logString, LogType type) => type != LogType.Log || logString == null ||
        !logString.StartsWith(ElectricalAuditLogging.Tag, StringComparison.Ordinal);
}
