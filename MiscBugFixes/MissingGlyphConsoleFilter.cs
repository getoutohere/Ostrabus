using System;
using HarmonyLib;
using UnityEngine;

namespace Ostrabus.MiscBugFixes;

// Filter only the GUI's Unity-log subscriber. Player.log and inspection logging
// still receive the original warning; font rendering and fallbacks are unchanged.
[HarmonyPatch(typeof(ConsoleToGUI), nameof(ConsoleToGUI.Log))]
internal static class MissingGlyphConsoleFilter {
    [HarmonyPrefix]
    internal static bool Prefix(string logString, LogType type) {
        if (type != LogType.Warning || logString == null) return true;
        if (logString.StartsWith("The character used for Ellipsis is not available in font asset [", StringComparison.Ordinal) &&
            logString.EndsWith("] or any potential fallbacks. Switching Text Overflow mode to Truncate.", StringComparison.Ordinal)) return false;
        if (!logString.StartsWith("The character with Unicode value \\u", StringComparison.Ordinal)) return true;
        const string tail = "] font asset or any potential fallbacks. It was replaced by Unicode character \\u";
        return !(logString.IndexOf(" was not found in the [robotocondensed SDF" + tail, StringComparison.Ordinal) >= 0 ||
            logString.IndexOf(" was not found in the [robotocondensed SDF No Outline" + tail, StringComparison.Ordinal) >= 0);
    }
}
