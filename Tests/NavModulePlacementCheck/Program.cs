using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using HarmonyLib;
using Ostrabus.NavConsole;
using UnityEngine;
using NavDraggable = Ostranauts.ShipGUIs.NavStation.Draggable;

static class Program
{
    static int checks;
    static readonly RectTransform Child = Empty<RectTransform>();
    static readonly RectTransform ParentRect = Empty<RectTransform>();
    static readonly NavDraggable Drag = Empty<NavDraggable>();
    static readonly MethodInfo Move = AccessTools.Method(typeof(NavDraggable), "MoveRectTransformUsingAnchors");
    static Vector2 min, max, pivot, size, anchored, delta;
    static float scale;
    static bool parentAbsent;
    static readonly Vector2 Origin = new Vector2(41.07331f, 1.63812f);
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static bool Near(Vector2 a, Vector2 b) => Math.Abs(a.x - b.x) < .000002 && Math.Abs(a.y - b.y) < .000002;
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result)
    { __result = ReferenceEquals(__0, __1); return false; }
    static bool Transform(Component __instance, ref UnityEngine.Transform __result)
    { if (!ReferenceEquals(__instance, Drag)) throw new Exception("Unexpected transform access"); __result = Child; return false; }
    static bool Parent(UnityEngine.Transform __instance, ref UnityEngine.Transform __result)
    { if (!ReferenceEquals(__instance, Child)) throw new Exception("Unexpected parent access"); __result = parentAbsent ? null : ParentRect; return false; }
    static bool Rectangle(ref Rect __result)
    { __result = new Rect(-pivot.x * size.x, -pivot.y * size.y, size.x, size.y); return false; }
    static bool Pivot(ref Vector2 __result) { __result = pivot; return false; }
    static Vector3 World(Vector2 anchor) => new Vector3(
        Origin.x + (anchor.x - pivot.x) * size.x * scale,
        Origin.y + (anchor.y - pivot.y) * size.y * scale, 0);
    static bool Corners(Vector3[] __0)
    {
        __0[0] = World(min); __0[1] = World(new Vector2(min.x, max.y));
        __0[2] = World(max); __0[3] = World(new Vector2(max.x, min.y));
        return false;
    }
    static bool Local(Vector2 __1, ref Vector2 __3, ref bool __result)
    { __3 = new Vector2((__1.x - Origin.x) / scale, (__1.y - Origin.y) / scale); __result = true; return false; }
    static bool SetMin(Vector2 __0) { min = __0; return false; }
    static bool SetMax(Vector2 __0) { max = __0; return false; }
    static bool SetPosition(Vector2 __0) { anchored = __0; return false; }
    static bool SetSize(Vector2 __0) { delta = __0; return false; }
    static void Reset(Vector2 parentPivot, Vector2 start, Vector2 end, float worldScale)
    {
        pivot = parentPivot; min = start; max = end; scale = worldScale;
        size = new Vector2(1207.2756f, 614.1011f);
        anchored = delta = new Vector2(99, 99); parentAbsent = false;
    }
    static void Hook(Harmony fixture, MethodInfo method, string prefix) =>
        fixture.Patch(method, prefix: new HarmonyMethod(typeof(Program), prefix));
    static int Main()
    {
        var fixture = new Harmony("offline.nav-placement.engine-boundaries");
        var production = new Harmony("offline.nav-placement.production");
        try
        {
            Hook(fixture, AccessTools.Method(typeof(UnityEngine.Object), "op_Equality"), nameof(Equal));
            Hook(fixture, AccessTools.PropertyGetter(typeof(Component), "transform"), nameof(Transform));
            Hook(fixture, AccessTools.PropertyGetter(typeof(UnityEngine.Transform), "parent"), nameof(Parent));
            Hook(fixture, AccessTools.PropertyGetter(typeof(RectTransform), "rect"), nameof(Rectangle));
            Hook(fixture, AccessTools.PropertyGetter(typeof(RectTransform), "pivot"), nameof(Pivot));
            Hook(fixture, AccessTools.Method(typeof(RectTransform), "GetWorldCorners"), nameof(Corners));
            Hook(fixture, AccessTools.Method(typeof(RectTransformUtility), "ScreenPointToLocalPointInRectangle"), nameof(Local));
            Hook(fixture, AccessTools.PropertySetter(typeof(RectTransform), "anchorMin"), nameof(SetMin));
            Hook(fixture, AccessTools.PropertySetter(typeof(RectTransform), "anchorMax"), nameof(SetMax));
            Hook(fixture, AccessTools.PropertySetter(typeof(RectTransform), "anchoredPosition"), nameof(SetPosition));
            Hook(fixture, AccessTools.PropertySetter(typeof(RectTransform), "sizeDelta"), nameof(SetSize));

            // Reproduce the two native conversions on initial palette dragging.
            // Geometry/scale match the live capture. Only engine projection and
            // property access are adapted; the shipped normalization is executed.
            var start = new Vector2(0, .02f); var end = new Vector2(.25f, .42f);
            Reset(new Vector2(.5f, 0), start, end, .03125f);
            Move.Invoke(Drag, null);
            Check(Near(min, new Vector2(0, .52f)), "Native bottom-pivot negative control did not drift half a panel");
            Move.Invoke(Drag, null);
            Check(Near(min, new Vector2(0, 1.02f)) && Near(max, new Vector2(.25f, 1.42f)),
                "Native repeat did not reproduce the observed offscreen bounds");
            Reset(new Vector2(.5f, .5f), start, end, .03125f);
            Move.Invoke(Drag, null);
            Check(Near(min, start) && Near(max, end), "Native centered-pivot control unexpectedly moved");

            production.CreateClassProcessor(typeof(ModulePlacementPatch)).Patch();
            foreach (var p in new[] { new Vector2(.5f, .5f), new Vector2(.5f, 0), new Vector2(0, 0),
                new Vector2(1, 1), new Vector2(.25f, .75f), new Vector2(-.25f, 1.25f) })
            foreach (float worldScale in new[] { .03125f, 1f, 2f })
            foreach (var bounds in new[] { new[] { start, end }, new[] { new Vector2(.65f, .4f), new Vector2(.75f, .8f) },
                new[] { Vector2.zero, Vector2.one }, new[] { new Vector2(-.3f, .8f), new Vector2(.2f, 1.2f) } })
            {
                Reset(p, bounds[0], bounds[1], worldScale);
                for (int repeat = 0; repeat < 5; repeat++)
                {
                    Move.Invoke(Drag, null);
                    Check(Near(min, bounds[0]) && Near(max, bounds[1]), "Repaired native normalization drifted or clamped bounds");
                    Check(Near(anchored, Vector2.zero) && Near(delta, Vector2.zero), "Native layout reset was lost");
                }
            }
            Reset(new Vector2(.5f, 0), start, end, 1);
            parentAbsent = true; Move.Invoke(Drag, null);
            Check(Near(min, start) && anchored.x == 99, "Native absent-parent return changed");
            parentAbsent = false; size.x = 0; Move.Invoke(Drag, null);
            Check(Near(min, start) && anchored.x == 99, "Native zero-size return changed");

            var reader = typeof(PatchProcessor).GetMethods().Single(method => method.Name == "GetOriginalInstructions"
                && method.GetParameters().Length == 2 && !method.GetParameters()[1].ParameterType.IsByRef);
            var original = ((System.Collections.Generic.IEnumerable<CodeInstruction>)reader.Invoke(null, new object[] { Move, null })).ToList();
            bool rejected = false;
            try { ModulePlacementPatch.Transpiler(Array.Empty<CodeInstruction>(), Move).ToArray(); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Missing native parent binding was accepted");
            var changed = original.Select(code => new CodeInstruction(code)).ToList();
            var half = changed.First(code => code.opcode == OpCodes.Ldc_R4 && Equals(code.operand, .5f));
            half.operand = .6f; rejected = false;
            try { ModulePlacementPatch.Transpiler(changed, Move).ToArray(); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Changed native anchor arithmetic was accepted");
            Console.WriteLine(checks + " native nav placement/pivot, repeated-drag, centered-layout, bounds and IL-guard checks passed.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { production.UnpatchSelf(); fixture.UnpatchSelf(); }
    }
}
