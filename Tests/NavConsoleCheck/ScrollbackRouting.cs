using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using HarmonyLib;
using Ostrabus.NavConsole;
using Ostranauts.ShipGUIs.NavStation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

static partial class ScrollbackChecks {
    static float position, alpha = 1;
    static int writes;
    static float ReadScroll(ScrollRect _) => position;
    static void WriteScroll(ScrollRect _, float value) { position = value; writes++; }
    static float ReadAlpha(CanvasGroup _) => alpha;
    static Mouse ReadMouse() => null;
    static void NoSound() { }
    static IEnumerable<CodeInstruction> EngineBoundary(IEnumerable<CodeInstruction> instructions) {
        foreach (var instruction in instructions) {
            string method = null;
            if (instruction.Calls(AccessTools.PropertyGetter(typeof(ScrollRect), nameof(ScrollRect.verticalNormalizedPosition)))) method = nameof(ReadScroll);
            if (instruction.Calls(AccessTools.PropertySetter(typeof(ScrollRect), nameof(ScrollRect.verticalNormalizedPosition)))) method = nameof(WriteScroll);
            if (instruction.Calls(AccessTools.PropertyGetter(typeof(CanvasGroup), nameof(CanvasGroup.alpha)))) method = nameof(ReadAlpha);
            if (instruction.Calls(AccessTools.PropertyGetter(typeof(Mouse), nameof(Mouse.current)))) method = nameof(ReadMouse);
            if (instruction.Calls(AccessTools.Method(typeof(Scrollbacks), "Sound"))) method = nameof(NoSound);
            if (method != null) { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(ScrollbackChecks), method); }
            yield return instruction;
        }
    }
    static void RoutingChecks(Harmony harmony) {
        // Only Unity property accesses are replaced. Execute the real production
        // routing, native-bounds reader, scroll setter and follow completion.
        foreach (string method in new[] { "ContentHeight", "ViewportHeight", "Set", "LegacyNavWheel", "CommsInput" })
            harmony.Patch(AccessTools.Method(typeof(Scrollbacks), method), transpiler:new HarmonyMethod(typeof(ScrollbackChecks), nameof(EngineBoundary)));
        var display = (GUIMessageDisplay)FormatterServices.GetUninitializedObject(typeof(GUIMessageDisplay));
        var scroll = (ScrollRect)FormatterServices.GetUninitializedObject(typeof(ScrollRect));
        var group = (CanvasGroup)FormatterServices.GetUninitializedObject(typeof(CanvasGroup));
        var nav = (GUIOrbitDraw)FormatterServices.GetUninitializedObject(typeof(GUIOrbitDraw));
        AccessTools.Field(typeof(ScrollRect), "m_Content").SetValue(scroll, FormatterServices.GetUninitializedObject(typeof(RectTransform)));
        AccessTools.Field(typeof(ScrollRect), "m_ContentBounds").SetValue(scroll, new Bounds(Vector3.zero, new Vector3(500, 10000, 0)));
        AccessTools.Field(typeof(ScrollRect), "m_ViewBounds").SetValue(scroll, new Bounds(Vector3.zero, new Vector3(500, 500, 0)));
        AccessTools.Field(typeof(GUIMessageDisplay), "srLog").SetValue(display, scroll);
        AccessTools.Field(typeof(GUIMessageDisplay), "cgComms").SetValue(display, group);
        AccessTools.Field(typeof(GUIOrbitDraw), "srLog").SetValue(nav, scroll);
        AccessTools.Field(typeof(GUIOrbitDraw), "cgStatus").SetValue(nav, group);
        AccessTools.Field(typeof(GUIOrbitDraw), "bNoteOpen").SetValue(nav, true);
        nav.bActive = false;
        var state = (ScrollbackFollow)AccessTools.Method(typeof(Scrollbacks), "Follow").Invoke(null, new object[] { display });
        var instance = AccessTools.Field(typeof(GUIOrbitDraw), "_instance");
        object previous = instance.GetValue(null);
        try {
            position = .5f;
            // No lossyScale dependency: even uninitialized engine transforms
            // must not be touched when native ScrollRect bounds are available.
            Near(Scrollbacks.ContentHeight(scroll), 10000, "Native content bounds ignored");
            Near(Scrollbacks.ViewportHeight(scroll), 500, "Native viewport bounds ignored");
            Near(Scrollbacks.ContentRange(scroll), 9500, "Native overflow range ignored");
            Scrollbacks.LegacyNavWheel(nav, -.1f);
            Near(position, .5f - 100f / 9500, "Native fallback wheel failed");
            Check(Scrollbacks.NavInput(nav), "Unbound nav input was suppressed");
            var table = AccessTools.Field(typeof(Scrollbacks), "panels").GetValue(null);
            table.GetType().GetMethod("Add").Invoke(table, new object[] { nav, new Scrollbacks.Surface() });
            Check(Scrollbacks.NavInput(nav), "Unavailable bound surface swallowed native nav input");

            foreach (object map in new object[] { null, nav }) {
                instance.SetValue(null, map);
                state.Open(); state.BeforeAppend(1, 9500); state.Appended(-100);
                writes = 0; position = 1;
                Check(Scrollbacks.CommsInput(display), "Unbound comms input was suppressed");
                Check(!state.Pending && writes == 1, "Initial follow depended on the map being open or its tutorial flag");
                Near(position, 0, "Unbound comms log remained at oldest entry");
            }
            state.Appended(-100); alpha = 0;
            Check(!Scrollbacks.CommsInput(display) && !state.Pending, "Closed comms retained a pending jump");
            alpha = 1;
            // Binding must explicitly include the separate docking/comms prefab.
            var targets = (IEnumerable<System.Reflection.MethodBase>)AccessTools.Method(typeof(ScrollbackBindingPatch), "TargetMethods").Invoke(null, null);
            bool docking = false, mapBinding = false;
            foreach (var target in targets) {
                docking |= target.DeclaringType == typeof(GUIDockSys) && target.Name == "Init";
                mapBinding |= target.DeclaringType == typeof(GUIOrbitDraw) && target.Name == "Start";
            }
            Check(docking && mapBinding, "Separate comms prefab lifecycle was not bound");
        } finally { instance.SetValue(null, previous); alpha = 1; Scrollbacks.Stop(); }
    }
}
