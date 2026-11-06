using System;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace Ostrabus.Inspection;

internal sealed class InspectionLogHooks : ILogListener {
    internal InspectionLogHooks() {
        Application.logMessageReceivedThreaded += UnityLog;
        BepInEx.Logging.Logger.Listeners.Add(this);
    }

    static void UnityLog(string message, string stack, LogType type) { InspectionLog.Add("Unity", type.ToString(), message, stack); }
    public void LogEvent(object sender, LogEventArgs args) {
        string source = args.Source?.SourceName;
        if (source == "Unity Log") { return; }
        string text = args.Data as string;
        if (args.Data != null && text == null) { text = "[Non-string log payload: " + args.Data.GetType().FullName + "]"; }
        InspectionLog.Add(source, args.Level.ToString(), text);
    }

    public void Dispose() {
        Application.logMessageReceivedThreaded -= UnityLog;
        BepInEx.Logging.Logger.Listeners.Remove(this);
    }
}

// LogInfo writes directly to the game's GUI log rather than through Unity logging.
[HarmonyPatch(typeof(ConsoleToGUI), nameof(ConsoleToGUI.LogInfo))]
internal static class ConsoleInfoInspectionPatch {
    [HarmonyPrefix]
    static void Prefix(string logString) { InspectionLog.Add("GameConsole", "Info", logString); }
}
