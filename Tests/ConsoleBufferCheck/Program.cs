using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using HarmonyLib;
using Ostrabus.MiscBugFixes;
using UnityEngine;

static class Program {
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static void Set(ConsoleToGUI console, string field, object value) => AccessTools.Field(typeof(ConsoleToGUI), field).SetValue(console, value);
    static string Text(ConsoleToGUI console) => (string)AccessTools.Field(typeof(ConsoleToGUI), "myLog").GetValue(console);
    static ConsoleToGUI ConsoleFixture(int limit = 16382) {
        var console = (ConsoleToGUI)FormatterServices.GetUninitializedObject(typeof(ConsoleToGUI));
        Set(console, "myLog", "seed"); Set(console, "kChars", 700); Set(console, "mChars", limit);
        foreach (string field in new[] { "doLogs", "doWarning", "doError", "doException", "doAssert", "doUnknown" }) Set(console, field, true);
        Set(console, "bugLevel", -1);
        return console;
    }
    static List<CodeInstruction> Original(MethodBase method) {
        var reader = typeof(PatchProcessor).GetMethods().Single(m => m.Name == "GetOriginalInstructions" && m.GetParameters().Length == 2 && !m.GetParameters()[1].ParameterType.IsByRef);
        return ((IEnumerable<CodeInstruction>)reader.Invoke(null, new object[] { method, null })).ToList();
    }
    static void Shape() {
        foreach (string method in new[] { "Log", "LogInfo", "DrawConsole" }) {
            var target = AccessTools.Method(typeof(ConsoleToGUI), method);
            var original = Original(target);
            var result = ConsoleBufferPatch.Transpiler(original.Select(c => new CodeInstruction(c)), target).ToList();
            Check(result.Count(c => c.Calls(AccessTools.Method(typeof(ConsoleBuffer), nameof(ConsoleBuffer.Store)))) == 2, method + " missing a buffer write");
            Check(!result.Any(c => c.opcode == OpCodes.Stfld && Equals(c.operand, AccessTools.Field(typeof(ConsoleToGUI), "myLog"))), "Unbounded write retained");
            var altered = original.Where(c => c.opcode != OpCodes.Stfld || !Equals(c.operand, AccessTools.Field(typeof(ConsoleToGUI), "myLog"))).ToList();
            bool refused = false;
            try { ConsoleBufferPatch.Transpiler(altered, target).ToList(); } catch (InvalidOperationException) { refused = true; }
            Check(refused, "Changed write sites accepted");
            if (method != "DrawConsole") continue;
            Check(result.Count(c => c.Calls(AccessTools.Method(typeof(ConsoleBuffer), nameof(ConsoleBuffer.ContentHeight)))) == 1, "Wrong display height scope");
            Check(result.Count(c => c.opcode == OpCodes.Call && c.operand is MethodInfo m && m.DeclaringType == typeof(GUI)) ==
                original.Count(c => c.opcode == OpCodes.Call && c.operand is MethodInfo m && m.DeclaringType == typeof(GUI)), "Native GUI calls changed");
            Check(result[0].opcode == OpCodes.Ldarg_0 && result[1].Calls(AccessTools.Method(typeof(ConsoleBuffer), nameof(ConsoleBuffer.Normalize))), "Existing oversized buffer not repaired before rendering");
        }
    }
    static void Pure() {
        Check(ConsoleBuffer.Recent(null, 10) == "" && ConsoleBuffer.Recent("a", 0) == "", "Empty/zero budget");
        string original = "unchanged";
        Check(ReferenceEquals(original, ConsoleBuffer.Recent(original, original.Length)), "Unnecessary allocation below limit");
        Check(ConsoleBuffer.Recent("old\nnew", 3) == "new", "Boundary newline discarded newest line");
        Check(ConsoleBuffer.Recent(new string('x', 20000) + "new", 100).EndsWith("new"), "Oversized single line lost its tail");
        Check(ConsoleBuffer.Recent("old\n" + new string('x', 20000) + "\nnew", 100) == "new", "Oversized stack line not removed");
        string emoji = "old" + char.ConvertFromUtf32(0x1f642) + "end";
        Check(ConsoleBuffer.Recent(emoji, 4) == "end", "Surrogate pair split");
        foreach (string label in new[] { "Log", "Warning", "Error", "Exception", "Assert", "Unknown", "Info", "Command" }) {
            string entry = "<color=#abcdef><b>[" + label + "]</b></color>: newest\nstack frame";
            string result = ConsoleBuffer.Recent(new string('x', 1000) + "\n" + entry, entry.Length + 30);
            Check(result == entry, "Complete native entry not preserved: " + label);
        }
        // Stress retained bounds, leading long lines, and repeated mixed records.
        string buffer = "";
        for (int i = 0; i < 3000; i++) {
            string newest = "<b>[Log]</b>: message " + i;
            buffer = ConsoleBuffer.Recent(buffer + "\n" + newest + "\n" + new string('x', i % 3 == 0 ? 2000 : 20), 1024);
            Check(buffer.Length <= 1024 && (i % 3 == 0 || buffer.Contains(newest)), "Stress retention/bound");
        }
    }
    sealed class RenderReached : Exception { }
    static int measureCalls, screenWidth = 1920, screenHeight = 1080, fontSize = 16;
    static Rect renderedRect, scrollContent;
    static string renderedText;
    static int renderedLimit;
    static float wantedHeight = 12000f;
    static bool False(ref bool __result) { __result = false; return false; }
    static bool Width(ref int __result) { __result = screenWidth; return false; }
    static bool Height(ref int __result) { __result = screenHeight; return false; }
    static bool Font(ref int __result) { __result = fontSize; return false; }
    static bool Measure(ref float __result) { measureCalls++; __result = wantedHeight; return false; }
    static bool Begin(Rect __2, Vector2 __1, ref Vector2 __result) { scrollContent = __2; __result = __1; return false; }
    static bool Area(Rect __0, string __1, int __2) {
        renderedRect = __0; renderedText = __1; renderedLimit = __2;
        throw new RenderReached();
    }
    static void Draw(ConsoleToGUI console) {
        try { AccessTools.Method(typeof(ConsoleToGUI), "DrawConsole").Invoke(console, new object[] { 1 }); }
        catch (TargetInvocationException e) when (e.InnerException is RenderReached) { return; }
        throw new Exception("Native console render boundary not reached");
    }
    static void Display(Harmony environment, ConsoleToGUI console) {
        // Only engine/input/measurement boundaries are stubbed. Execute native
        // DrawConsole through its real scroll-view and TextArea call sites.
        foreach (var pair in new[] {
            Tuple.Create(AccessTools.Method(typeof(ConsoleToGUI), "HandleInput"), nameof(False)),
            Tuple.Create(AccessTools.PropertyGetter(typeof(Screen), "width"), nameof(Width)),
            Tuple.Create(AccessTools.PropertyGetter(typeof(Screen), "height"), nameof(Height)),
            Tuple.Create(AccessTools.PropertyGetter(typeof(GUIStyle), "fontSize"), nameof(Font)),
            Tuple.Create(AccessTools.Method(typeof(GUIStyle), "CalcHeight", new[] { typeof(GUIContent), typeof(float) }), nameof(Measure)),
            Tuple.Create(AccessTools.Method(typeof(GUI), "BeginScrollView", new[] { typeof(Rect), typeof(Vector2), typeof(Rect), typeof(bool), typeof(bool) }), nameof(Begin)),
            Tuple.Create(AccessTools.Method(typeof(GUI), "TextArea", new[] { typeof(Rect), typeof(string), typeof(int), typeof(GUIStyle) }), nameof(Area))
        }) environment.Patch(pair.Item1, prefix: new HarmonyMethod(typeof(Program), pair.Item2));
        Set(console, "logStyle", (GUIStyle)FormatterServices.GetUninitializedObject(typeof(GUIStyle)));
        Set(console, "_textActual", 16f); Set(console, "scrollPos", new Vector2(0f, 4000f));
        Set(console, "myLog", new string('x', 30000));
        Draw(console);
        Check(renderedText.Length <= 16382 && renderedLimit == 16382, "Oversized existing buffer not bounded at display");
        Check(renderedRect.height == 12000f && scrollContent.height == renderedRect.height, "Measured height not shared by text and scroll content");
        float bottom = 12000f + 20f - (1080f / 2f - 71f);
        Check(((Vector2)AccessTools.Field(typeof(ConsoleToGUI), "scrollPos").GetValue(console)).y == bottom, "Initial newest output not visible");
        int measured = measureCalls; Draw(console);
        Check(measureCalls == measured, "Unchanged text remeasured every GUI event");
        wantedHeight = 13000f; console.LogInfo("new output"); Draw(console);
        Check(renderedRect.height == 13000f && ((Vector2)AccessTools.Field(typeof(ConsoleToGUI), "scrollPos").GetValue(console)).y == bottom + 1000f, "New output did not follow bottom");
        Set(console, "scrollPos", new Vector2(0f, 100f));
        wantedHeight = 14000f; console.LogInfo("more output"); Draw(console);
        Check(((Vector2)AccessTools.Field(typeof(ConsoleToGUI), "scrollPos").GetValue(console)).y == 100f, "Reader dragged away from history");
        measured = measureCalls; screenWidth = 1600; Draw(console);
        Check(measureCalls == measured + 1 && renderedRect.width == 760f, "Resize did not remeasure at actual native width");
        measured = measureCalls; fontSize = 24; Draw(console);
        Check(measureCalls == measured + 1, "Changed font size did not remeasure");
        wantedHeight = 100f; console.LogInfo("short measurement"); Draw(console);
        Check(renderedRect.height == 4000f, "Native minimum content height lost");
    }
    static void Native() {
        var baseline = ConsoleFixture();
        Set(baseline, "myLog", new string('x', 20000));
        baseline.Log("latest", "", LogType.Warning);
        Check(Text(baseline).Length > 16382, "Native long-line trim failure not reproduced");
        var h = new Harmony("offline.console-buffer.production");
        var filter = new Harmony("offline.console-buffer.filter");
        var environment = new Harmony("offline.console-buffer.render-boundaries");
        try {
            h.CreateClassProcessor(typeof(ConsoleBufferPatch)).Patch();
            var console = ConsoleFixture();
            Set(console, "myLog", new string('x', 20000));
            console.Log("latest", "", LogType.Warning);
            Check(Text(console).Length <= 16382 && Text(console).EndsWith("latest"), "Native Log repair failed");
            Set(console, "myLog", new string('x', 20000));
            console.LogInfo("Info survives");
            Check(Text(console).EndsWith("Info survives") && Text(console).Length <= 16382, "LogInfo hangs or loses newest message");
            console.LogInfo(new string('z', 50000) + "last");
            Check(Text(console).Length <= 16382 && Text(console).EndsWith("last"), "Huge info entry exceeded budget");
            string file = Path.GetTempFileName();
            try {
                Set(console, "filename", file); Set(console, "saveToFile", true);
                string full = new string('f', 30000) + "file tail";
                console.LogInfo(full);
                Check(File.ReadAllText(file).Contains(full), "GUI trimming truncated native file output");
                Check(Text(console).Length <= 16382 && Text(console).EndsWith("file tail"), "File logging bypassed GUI bound");
            } finally { Set(console, "saveToFile", false); File.Delete(file); }
            foreach (LogType type in new[] { LogType.Log, LogType.Warning, LogType.Error, LogType.Exception, LogType.Assert, (LogType)99 }) {
                console.Log("message " + type, "frame 1\nframe 2\n", type);
                Check(Text(console).Contains("message " + type) && Text(console).EndsWith("frame 2"), "Message/trace lost: " + type);
            }
            Check(console.ErrorCount == 3, "Native error counters changed");
            string before = Text(console);
            Set(console, "doWarning", false); console.Log("hidden", "", LogType.Warning);
            Check(Text(console) == before, "Native visibility settings changed");
            Set(console, "doWarning", true);
            foreach (int limit in new[] { -1, 0, 1, 100, 16382, int.MaxValue }) {
                Set(console, "mChars", limit); Set(console, "myLog", new string('x', 30000));
                console.LogInfo("new");
                Check(Text(console).Length <= Math.Max(1, Math.Min(16382, limit)), "Invalid budget loops or oversized result");
            }
            filter.CreateClassProcessor(typeof(MissingGlyphConsoleFilter)).Patch();
            console = ConsoleFixture();
            console.Log("ERROR: null gascontainer dict on NPC. Setting to empty for now.", "", LogType.Warning);
            Check(Text(console) == "seed", "Existing GUI filter defeated");
            console.Log("ordinary warning", "", LogType.Warning);
            Check(Text(console).Contains("ordinary warning"), "Ordinary warning lost with filter");
            Display(environment, console);
            // Removal restores native behavior; no persistent state needs migration.
            h.UnpatchSelf();
            console = ConsoleFixture(); Set(console, "myLog", new string('x', 20000));
            console.Log("latest", "", LogType.Warning);
            Check(Text(console).Length > 16382, "Repair did not unpatch cleanly");
        } finally { environment.UnpatchSelf(); filter.UnpatchSelf(); h.UnpatchSelf(); }
    }
    static int Main() {
        Shape(); Pure(); Native();
        Console.WriteLine(checks + " console buffer checks passed.");
        return 0;
    }
}
