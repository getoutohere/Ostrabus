using System;
using HarmonyLib;

namespace Ostrabus.Inspection;

internal static class ConsoleInspection {
    static readonly AccessTools.FieldRef<ConsoleToGUI, string> ConsoleText = AccessTools.FieldRefAccess<ConsoleToGUI, string>("myLog");

    // Main thread only. Read the actual GUI buffer, including command echoes/results.
    internal static InspectionApi.Reply Read(int offset) {
        var console = ConsoleToGUI.instance;
        if (console == null) { return new InspectionApi.Reply(503, SnapshotJson.Map("error", "Game console is not available.")); }
        return new InspectionApi.Reply(200, Page(ConsoleText(console), offset));
    }

    internal static object Page(string text, int offset) {
        text = text ?? "";
        int end = text.Length - Math.Min(offset, text.Length);
        int start = Math.Max(0, end - 1024);
        return SnapshotJson.Map("text", text.Substring(start, end - start), "totalChars", text.Length,
            "startChar", start, "endCharExclusive", end, "offsetFromEnd", offset,
            "nextOffset", start > 0 ? (object)(text.Length - start) : null,
            "capturedUtc", DateTime.UtcNow.ToString("O"),
            "note", "Raw GUI console text, including rich-text markup. Newest 1024 characters first; follow nextOffset for older text. Buffer can change between requests.");
    }
}
