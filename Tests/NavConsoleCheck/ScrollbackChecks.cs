using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using HarmonyLib;
using Ostrabus.NavConsole;
using Ostranauts.ShipGUIs.NavStation;
using TMPro;

// Run inside the shipped Mono runtime. Only text/engine boundaries are fixtures;
// the native append method and the production Harmony transpilers execute here.
static partial class ScrollbackChecks {
    static int checks, before, after;
    static string text;
    static readonly List<string> order = new List<string>();
    static void Check(bool ok, string reason) { checks++; if (!ok) throw new Exception(reason); }
    static void Near(float actual, float expected, string reason) => Check(Math.Abs(actual - expected) < .00001f, reason + ": " + actual + " != " + expected);
    static string ReadText(TMP_Text _) => text;
    static void WriteText(TMP_Text _, string value) { text = value; order.Add("text"); }
    static bool Before() { before++; order.Add("before"); return false; }
    static bool After() { after++; order.Add("after"); return false; }
    static IEnumerable<CodeInstruction> TextBoundary(IEnumerable<CodeInstruction> instructions) {
        foreach (var instruction in instructions) {
            if (instruction.Calls(AccessTools.PropertyGetter(typeof(TMP_Text), nameof(TMP_Text.text)))) {
                instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(ScrollbackChecks), nameof(ReadText));
            } else if (instruction.Calls(AccessTools.PropertySetter(typeof(TMP_Text), nameof(TMP_Text.text)))) {
                instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(ScrollbackChecks), nameof(WriteText));
            }
            yield return instruction;
        }
    }
    static List<CodeInstruction> Original(MethodBase target) {
        var reader = typeof(PatchProcessor).GetMethods().Single(m => m.Name == "GetOriginalInstructions" && m.GetParameters().Length == 2 && !m.GetParameters()[1].ParameterType.IsByRef);
        return ((IEnumerable<CodeInstruction>)reader.Invoke(null, new object[] { target, null })).ToList();
    }
    static List<CodeInstruction> Transpile(Type patch, List<CodeInstruction> original) => ((IEnumerable<CodeInstruction>)AccessTools.Method(patch, "Transpiler").Invoke(null, new object[] { original.Select(i => new CodeInstruction(i)).ToList() })).ToList();
    static void MathChecks() {
        // Each notch moves 20% of the viewport even when the transcript is huge.
        foreach (float height in new[] { 501f, 1000f, 10000f, 10000000f }) {
            float range = height - 500;
            float moved = ScrollbackPolicy.Wheel(.5f, height, 500, 120);
            Near(moved, Math.Min(1, .5f + 100 / range), "Viewport-based wheel amount");
            Near(ScrollbackPolicy.Wheel(.5f, height, 500, 1), moved, "Platform wheel magnitude changed notch size");
            Near(ScrollbackPolicy.Wheel(.5f, height, 500, -120), Math.Max(0, .5f - 100 / range), "Down wheel sign");
        }
        Near(ScrollbackPolicy.Wheel(0, 500, 500, 120), 0, "Non-overflowing text moved");
        Near(ScrollbackPolicy.Wheel(.5f, float.PositiveInfinity, 500, 120), .5f, "Invalid content scrolled");
        Near(ScrollbackPolicy.Wheel(.5f, 10000, 0, 120), .5f, "Zero viewport scrolled");
        Near(ScrollbackPolicy.Wheel(.5f, 10000, 500, float.NaN), .5f, "Invalid input scrolled");
        Near(ScrollbackPolicy.Wheel(1, 10000, 500, 120), 1, "Top overscroll");
        Near(ScrollbackPolicy.Wheel(0, 10000, 500, -120), 0, "Bottom overscroll");
        foreach (float size in new[] { 0f, .0001f, .1f, .5f, .99f })
            foreach (bool reversed in new[] { false, true })
                foreach (float value in new[] { 0f, .2f, .5f, .8f, 1f }) {
                    float center = ScrollbackPolicy.ThumbCenter(value, size, reversed);
                    Near(ScrollbackPolicy.Drag(center, size, reversed), value, "Thumb grip jumped on initial click");
                    Near(ScrollbackPolicy.Drag(-1, size, reversed), reversed ? 1 : 0, "Drag below track clamp");
                    Near(ScrollbackPolicy.Drag(2, size, reversed), reversed ? 0 : 1, "Drag above track clamp");
                }
        Near(ScrollbackPolicy.Drag(.5f, 1, false), 0, "Full-sized thumb moved");
        Near(ScrollbackPolicy.Drag(float.NaN, .1f, false), 0, "Invalid drag not rejected");
    }
    static void FollowChecks() {
        var state = new ScrollbackFollow();
        state.Open();
        state.BeforeAppend(.9f, 10000); // A different channel was scrolled up.
        state.Appended(10);
        Check(state.Following, "Opening inherited the previous channel's reading position");
        Check(!state.Apply(11, 10000, false, out _), "Follow ran before layout settled");
        Check(state.Apply(12, 10000, false, out float position), "Follow waited on scaled game time instead of frames");
        Near(position, 0, "Opening did not show latest messages");
        Check(!state.Apply(13, 10000, false, out _), "Follow repeated after consumption");

        state.User(.5f, 1000); // Read halfway through an existing channel.
        Check(!state.Following, "Manual scroll did not suspend following");
        state.BeforeAppend(.5f, 1000); state.Appended(20);
        state.BeforeAppend(.6f, 1200); state.Appended(21); // Layout is stale during a batch.
        Check(state.DueFrame == 22 && state.Top == 500, "Batch replaced its anchor or deferred indefinitely");
        Check(!state.Apply(22, 2000, true, out _), "Append fought an active scrollbar drag");
        Check(state.Apply(23, 2000, false, out position), "Pending anchor was lost during drag");
        Near(position, .75f, "New messages displaced the reading anchor");

        state.BeforeAppend(.75f, 2000); state.Appended(30);
        state.User(.9f, 2000);
        Check(!state.Apply(32, 3000, false, out _), "Delayed scroll undid newer manual input");
        state.User(0, 3000); Check(state.Following, "Reaching bottom did not resume following");
        state.BeforeAppend(0, 3000); state.Appended(40);
        Check(state.Apply(42, 4000, false, out position), "Following did not resume"); Near(position, 0, "Following missed bottom");
        state.User(.0001f, 10000); Check(state.Following, "Pixel-rounding tolerance did not recognize bottom");
        state.User(.001f, 10000); Check(!state.Following, "Real scroll-away mistaken for bottom");
        state.BeforeAppend(.001f, 10000); state.Appended(50); state.Cancel();
        Check(!state.Apply(52, 10000, false, out _), "Hidden channel retained an automatic jump");
        state.Open(); state.BeforeAppend(1, 10000); state.Appended(60);
        Check(state.Apply(62, 0, false, out position), "Short new channel did not complete follow"); Near(position, 0, "Short channel scroll position");
        state.User(.5f, 10000); state.BeforeAppend(0, 10000);
        Check(state.Following, "Native fallback reaching bottom did not resume following");
    }
    static void NativeChecks(Harmony h) {
        var nav = AccessTools.Method(typeof(GUIOrbitDraw), "MouseHandler");
        var original = Original(nav); var patched = Transpile(typeof(NavScrollbackInputPatch), original);
        var nativeScroll = AccessTools.Method(typeof(GUIOrbitDraw), "ScrollLog");
        var replacement = AccessTools.Method(typeof(Scrollbacks), nameof(Scrollbacks.LegacyNavWheel));
        Check(original.Count == patched.Count && original.Count(i => i.Calls(nativeScroll)) == 4, "Native nav call structure changed");
        for (int i = 0; i < original.Count; i++) {
            if (original[i].Calls(nativeScroll)) Check(patched[i].Calls(replacement), "Native nav scroll call not replaced");
            else Check(original[i].opcode == patched[i].opcode && Equals(original[i].operand, patched[i].operand), "Unrelated nav input instruction altered");
        }
        var append = AccessTools.Method(typeof(GUIMessageDisplay), "LogConversation");
        var appended = Transpile(typeof(CommsScrollbackFollowPatch), Original(append));
        Check(!appended.Any(i => i.Calls(AccessTools.Method(typeof(CrewSim), nameof(CrewSim.ScrollBottom)))), "Shared delayed jump remains in comms append");
        Check(appended.Count(i => i.Calls(AccessTools.Method(typeof(Scrollbacks), nameof(Scrollbacks.BeforeAppend)))) == 1 && appended.Count(i => i.Calls(AccessTools.Method(typeof(Scrollbacks), nameof(Scrollbacks.AfterAppend)))) == 1, "Append hooks not scoped exactly once");
        foreach (var patch in new[] { typeof(NavScrollbackInputPatch), typeof(CommsScrollbackFollowPatch) }) {
            bool refused = false;
            try { Transpile(patch, new List<CodeInstruction> { new CodeInstruction(OpCodes.Ret) }); }
            catch (TargetInvocationException error) { refused = error.InnerException is InvalidOperationException; }
            Check(refused, "Unexpected native IL was accepted by " + patch.Name);
        }
        h.Patch(AccessTools.Method(typeof(Scrollbacks), nameof(Scrollbacks.BeforeAppend)), prefix:new HarmonyMethod(typeof(ScrollbackChecks), nameof(Before)));
        h.Patch(AccessTools.Method(typeof(Scrollbacks), nameof(Scrollbacks.AfterAppend)), prefix:new HarmonyMethod(typeof(ScrollbackChecks), nameof(After)));
        h.Patch(append, transpiler:new HarmonyMethod(typeof(ScrollbackChecks), nameof(TextBoundary)));
        h.CreateClassProcessor(typeof(CommsScrollbackFollowPatch)).Patch();
        var display = (GUIMessageDisplay)FormatterServices.GetUninitializedObject(typeof(GUIMessageDisplay));
        text = "history"; order.Clear();
        append.Invoke(display, new object[] { "new message" });
        Check(text == "history\nnew message", "Native message text or formatting changed");
        Check(before == 1 && after == 1 && string.Join(",", order) == "before,text,after", "Anchoring did not surround the actual native text append");
    }
    internal static void Run(Harmony harmony) {
        MathChecks(); FollowChecks(); RoutingChecks(harmony); NativeChecks(harmony);
        Console.WriteLine(checks + " scrollback math/follow/native-IL checks passed. Rendered pointer hit testing remains a live-scene check.");
    }
}
