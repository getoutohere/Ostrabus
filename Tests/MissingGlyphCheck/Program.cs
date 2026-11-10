using System;
using System.Runtime.Serialization;
using HarmonyLib;
using UnityEngine;
using Ostrabus.MiscBugFixes;

static class Program {
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static void Set(object obj, string name, object value) => AccessTools.Field(obj.GetType(), name).SetValue(obj, value);
    static string Text(ConsoleToGUI console) => (string)AccessTools.Field(typeof(ConsoleToGUI), "myLog").GetValue(console);
    static string Warning(string font, string code = "9997") => "The character with Unicode value \\u" + code +
        " was not found in the [" + font + "] font asset or any potential fallbacks. It was replaced by Unicode character \\u0020 in text object [txt].";
    static int Main() {
        string message = Warning("robotocondensed SDF");
        const string ellipsis = "The character used for Ellipsis is not available in font asset [Jura-Bold SDF] or any potential fallbacks. Switching Text Overflow mode to Truncate.";
        Check(!MissingGlyphConsoleFilter.Prefix(ellipsis, LogType.Warning), "Ellipsis fallback warning not filtered");
        Check(MissingGlyphConsoleFilter.Prefix(ellipsis, LogType.Error), "Ellipsis error suppressed");
        Check(MissingGlyphConsoleFilter.Prefix("The character used for Ellipsis is not available in font asset [Jura-Bold SDF] but loading the fallback failed.", LogType.Warning), "Unrecognized ellipsis failure suppressed");
        Check(!MissingGlyphConsoleFilter.Prefix(message, LogType.Warning), "Outlined warning not filtered");
        Check(!MissingGlyphConsoleFilter.Prefix(Warning("robotocondensed SDF No Outline"), LogType.Warning), "Unoutlined warning not filtered");
        Check(!MissingGlyphConsoleFilter.Prefix(Warning("robotocondensed SDF", "949F"), LogType.Warning), "Other missing glyph not filtered");
        foreach (LogType type in new[] { LogType.Error, LogType.Exception, LogType.Assert, LogType.Log })
            Check(MissingGlyphConsoleFilter.Prefix(message, type), "Non-warning suppressed");
        Check(MissingGlyphConsoleFilter.Prefix(null, LogType.Warning), "Null changed");
        Check(MissingGlyphConsoleFilter.Prefix("Font asset failed to load", LogType.Warning), "Unrelated font warning suppressed");
        Check(MissingGlyphConsoleFilter.Prefix(Warning("other SDF"), LogType.Warning), "Unrelated font suppressed");
        var console = (ConsoleToGUI)FormatterServices.GetUninitializedObject(typeof(ConsoleToGUI));
        Set(console, "myLog", "seed"); Set(console, "kChars", 4096); Set(console, "mChars", 16382);
        Set(console, "doWarning", true); Set(console, "doError", true); Set(console, "bugLevel", -1);
        console.Log(message, "", LogType.Warning);
        Check(Text(console).Contains(message), "Native warning baseline missing");
        Set(console, "myLog", "seed"); Set(console, "bugLevel", -1);
        var h = new Harmony("offline.missingglyph"); h.CreateClassProcessor(typeof(MissingGlyphConsoleFilter)).Patch();
        console.Log(message, "", LogType.Warning);
        Check(Text(console) == "seed", "Warning reached GUI");
        Check((int)AccessTools.Field(typeof(ConsoleToGUI), "bugLevel").GetValue(console) == -1, "Suppressed warning raised GUI badge");
        console.Log(ellipsis, "", LogType.Warning);
        Check(Text(console) == "seed", "Ellipsis warning reached GUI");
        Check((int)AccessTools.Field(typeof(ConsoleToGUI), "bugLevel").GetValue(console) == -1, "Ellipsis warning raised GUI badge");
        console.Log("Other warning", "", LogType.Warning);
        Check(Text(console).Contains("Other warning"), "Other warning lost");
        console.Log(message, "", LogType.Error);
        Check(Text(console).Contains(message) && console.ErrorCount == 1, "Error lost");
        console.Log(ellipsis, "", LogType.Error);
        Check(Text(console).Contains(ellipsis) && console.ErrorCount == 2, "Ellipsis error lost");
        Console.WriteLine(checks + " missing-glyph console filter checks passed.");
        return 0;
    }
}
