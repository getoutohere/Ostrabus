using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace Ostrabus.MiscBugFixes;

internal static class ConsoleBuffer {
    const int MaxChars = 16382;
    static readonly AccessTools.FieldRef<ConsoleToGUI, string> Text = AccessTools.FieldRefAccess<ConsoleToGUI, string>("myLog");
    static readonly AccessTools.FieldRef<ConsoleToGUI, int> Limit = AccessTools.FieldRefAccess<ConsoleToGUI, int>("mChars");
    static readonly AccessTools.FieldRef<ConsoleToGUI, GUIStyle> Style = AccessTools.FieldRefAccess<ConsoleToGUI, GUIStyle>("logStyle");
    static readonly AccessTools.FieldRef<ConsoleToGUI, Vector2> Scroll = AccessTools.FieldRefAccess<ConsoleToGUI, Vector2>("scrollPos");
    static readonly AccessTools.FieldRef<ConsoleToGUI, float> FontHeight = AccessTools.FieldRefAccess<ConsoleToGUI, float>("_textActual");

    // One display measurement, overwritten on text/width/font changes. No history,
    // per-message objects, update hook, or save state.
    static GUIContent measuredContent;
    static GUIStyle measuredStyle;
    static float measuredWidth, measuredHeight, previousViewport;
    static int measuredFont;

    internal static void Store(ConsoleToGUI console, string text) {
        int limit = Math.Max(1, Math.Min(MaxChars, Limit(console)));
        Limit(console) = limit;
        Text(console) = Recent(text, limit);
    }

    internal static void Normalize(ConsoleToGUI console) => Store(console, Text(console));

    internal static string Recent(string text, int limit) {
        if (string.IsNullOrEmpty(text) || limit <= 0) return "";
        if (text.Length <= limit) return text;
        int minimum = text.Length - limit;
        // Prefer a complete native entry, including its prefix and stack trace.
        // Command colours are configurable; only the native header shape matters.
        for (int line = text.IndexOf('\n', minimum - 1); line >= 0; line = text.IndexOf('\n', line + 1)) {
            int start = line + 1;
            if (EntryStart(text, start)) return text.Substring(start);
        }
        // A single oversized entry must still make progress. Keep complete trailing
        // lines where possible; a line longer than the entire budget keeps its tail.
        int newline = text.IndexOf('\n', minimum - 1);
        int cut = newline >= 0 && newline + 1 < text.Length ? newline + 1 : minimum;
        if (cut > 0 && char.IsLowSurrogate(text[cut]) && char.IsHighSurrogate(text[cut - 1])) cut++;
        return text.Substring(cut);
    }

    static bool EntryStart(string text, int start) {
        if (start >= text.Length || text[start] != '<') return false;
        int label;
        if (At(text, start, "<b>[")) label = start + 4;
        else if (At(text, start, "<color=")) {
            int end = text.IndexOf('>', start + 7);
            if (end < 0 || end - start > 40 || !At(text, end + 1, "<b>[")) return false;
            label = end + 5;
        } else return false;
        foreach (string name in Headers)
            if (At(text, label, name)) return true;
        return false;
    }

    static readonly string[] Headers = { "Log]</b>", "Warning]</b>", "Error]</b>", "Exception]</b>", "Assert]</b>", "Unknown]</b>", "Info]</b>", "Command]</b>" };
    static bool At(string text, int offset, string value) => offset + value.Length <= text.Length &&
        string.CompareOrdinal(text, offset, value, 0, value.Length) == 0;

    internal static float ContentHeight(float nativeHeight, ConsoleToGUI console) {
        float width = Screen.width / 2 - 40;
        float viewport = Screen.height / 2 - (FontHeight(console) + 55f);
        var style = Style(console);
        string text = Text(console);
        float previousHeight = Math.Max(nativeHeight, measuredHeight);
        if (measuredContent == null) measuredContent = new GUIContent();
        if (!ReferenceEquals(measuredContent.text, text) || !ReferenceEquals(measuredStyle, style) ||
            measuredWidth != width || measuredFont != style.fontSize) {
            measuredContent.text = text;
            measuredStyle = style;
            measuredWidth = width;
            measuredFont = style.fontSize;
            measuredHeight = style.CalcHeight(measuredContent, width);
        }
        float height = Math.Max(nativeHeight, measuredHeight);
        // Follow new output only when already at the bottom. Keep the reader's
        // scroll position while they inspect older output, including on resize.
        var scroll = Scroll(console);
        if (scroll.y >= previousHeight + 20f - (previousViewport > 0f ? previousViewport : viewport) - 1f) {
            scroll.y = Math.Max(0f, height + 20f - viewport);
            Scroll(console) = scroll;
        }
        previousViewport = viewport;
        return height;
    }
}

[HarmonyPatch]
internal static class ConsoleBufferPatch {
    static IEnumerable<MethodBase> TargetMethods() {
        yield return AccessTools.Method(typeof(ConsoleToGUI), nameof(ConsoleToGUI.Log));
        yield return AccessTools.Method(typeof(ConsoleToGUI), nameof(ConsoleToGUI.LogInfo));
        yield return AccessTools.Method(typeof(ConsoleToGUI), "DrawConsole");
    }

    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod) {
        var code = new List<CodeInstruction>(instructions);
        var text = AccessTools.Field(typeof(ConsoleToGUI), "myLog");
        bool draw = __originalMethod.Name == "DrawConsole";
        int stores = 0, rectangles = 0;
        for (int i = 0; i < code.Count; i++) {
            if (code[i].opcode == OpCodes.Stfld && Equals(code[i].operand, text)) {
                code[i].opcode = OpCodes.Call;
                code[i].operand = AccessTools.Method(typeof(ConsoleBuffer), nameof(ConsoleBuffer.Store));
                stores++;
            }
            // Only the content Rect has a 4000-pixel height; viewport/input Rects
            // and native BeginScrollView/TextArea/command handling remain intact.
            if (draw && (code[i].opcode == OpCodes.Newobj || code[i].opcode == OpCodes.Call) && Equals(code[i].operand,
                AccessTools.Constructor(typeof(Rect), new[] { typeof(float), typeof(float), typeof(float), typeof(float) })) &&
                i > 0 && code[i - 1].opcode == OpCodes.Ldc_R4 && Equals(code[i - 1].operand, 4000f)) {
                var call = code[i];
                code.Insert(i, new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(call).MoveBlocksFrom(call));
                code.Insert(i + 1, new CodeInstruction(OpCodes.Call,
                    AccessTools.Method(typeof(ConsoleBuffer), nameof(ConsoleBuffer.ContentHeight))));
                i += 2;
                rectangles++;
            }
        }
        if (stores != 2 || rectangles != (draw ? 1 : 0))
            throw new InvalidOperationException("Console buffer/display shape changed in " + __originalMethod.Name +
                " (writes=" + stores + ", content rectangles=" + rectangles + "); refusing a partial repair.");
        if (draw) {
            code.InsertRange(0, new[] { new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ConsoleBuffer), nameof(ConsoleBuffer.Normalize))) });
        }
        return code;
    }
}
