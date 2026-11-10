using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using LitJson;
using Ostrabus.NavConsole;

static class Program {
    static int checks;
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Field(object target, string name, object value) => AccessTools.Field(target.GetType(), name).SetValue(target, value);
    static void Check(bool ok, string reason) { checks++; if (!ok) throw new Exception(reason); }
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = ReferenceEquals(__0, __1); return false; }
    static bool Unequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = !ReferenceEquals(__0, __1); return false; }
    static CondOwner Console() {
        var co = Empty<CondOwner>();
        co.mapGUIPropMaps = new Dictionary<string, Dictionary<string, string>> { ["Panel A"] = new Dictionary<string, string> { ["nativeSentinel"] = "keep" } };
        return co;
    }
    static Ship Ship(string id, params CondOwner[] navs) {
        var ship = Empty<Ship>(); Field(ship, "strRegID", id); Field(ship, "nLoadState", global::Ship.Loaded.Full);
        ship.aNavs = new List<CondOwner>(navs); ship.aProxIgnores = new List<string>(); return ship;
    }
    static object Enter(Ship ship) {
        var args = new object[] { ship, null }; AccessTools.Method(typeof(ProximityCollisionPatch), "Prefix").Invoke(null, args); return args[1];
    }
    static void Exit(object state) { AccessTools.Method(typeof(ProximityCollisionPatch), "Finalizer").Invoke(null, new object[] { null, state }); }
    static int Main() {
        var h = new Harmony("offline.navconsole");
        h.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Equality"), prefix: new HarmonyMethod(typeof(Program), nameof(Equal)));
        h.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Inequality"), prefix: new HarmonyMethod(typeof(Program), nameof(Unequal)));
        var a = Console(); var b = Console(); var ship = Ship("TEST", a, b);
        Check(ProximityRange.Get(ship) == 5, "Existing ship does not default to 5");
        foreach (int km in new[] { 5, 10, 15, 20 }) {
            ship.aProxIgnores.Add("old"); ProximityRange.Set(ship, km);
            Check(ProximityRange.Get(ship) == km && ship.aProxIgnores.Count == 0, "Shared range or acknowledgment reset failed");
            Check(a.mapGUIPropMaps["Panel A"][ProximityRange.RangeKey] == km.ToString() && b.mapGUIPropMaps["Panel A"][ProximityRange.RangeKey] == km.ToString(), "Console replicas differ");
            Check(a.mapGUIPropMaps["Panel A"]["nativeSentinel"] == "keep", "Native console property altered");
            var state = Enter(ship);
            foreach (float factor in new[] { 1f, 2f, 5f, 10f }) {
                float native = ProximityRange.NativeShipAU * factor;
                float actual = ProximityCollisionPatch.ShipBuffer(native);
                Check(actual == (km == 5 ? native : Math.Max(native, (float)(km / ProximityRange.KmPerAU))), "Warning buffer incorrect");
                Check(actual >= native && actual < 51 / ProximityRange.KmPerAU, "Speed warning shrank or selected range multiplied by speed");
            }
            Check(ProximityCollisionPatch.BodyBuffer() == (km == 5 ? ProximityRange.NativeBodyAU : km / ProximityRange.KmPerAU), "Body warning differs");
            Exit(state);
        }
        foreach (int invalid in new[] { 0, 6, 25 }) {
            bool rejected = false; try { ProximityRange.Set(ship, invalid); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected && ProximityRange.Get(ship) == 20, "Invalid range accepted");
        }
        var saved = new JsonItem { strName = "ItmNavStation01", strID = "nav", aGPMSettings = new[] { new JsonGUIPropMap { strName = "Panel A", dictGUIPropMap = DataHandler.ConvertDictToStringArray(a.mapGUIPropMaps["Panel A"]) } } };
        var restored = JsonMapper.ToObject<JsonItem>(JsonMapper.ToJson(saved));
        var reloaded = Console(); reloaded.mapGUIPropMaps["Panel A"] = DataHandler.ConvertStringArrayToDict(restored.aGPMSettings[0].dictGUIPropMap);
        ProximityRange.Reset(default);
        Check(ProximityRange.Get(Ship("TEST", reloaded)) == 20, "Native property-map JSON roundtrip lost range");
        Check(ProximityRange.Get(Ship("OTHER", reloaded)) == 5, "Transferred console changed another ship's range");
        reloaded.mapGUIPropMaps["Panel A"][ProximityRange.RangeKey] = "bad";
        ProximityRange.Reset(default);
        Check(ProximityRange.Get(Ship("TEST", reloaded)) == 5, "Malformed saved range did not default");
        var outer = Enter(Ship("OUTER", a)); // Owner doesn't match: 5.
        var innerShip = Ship("INNER", Console()); ProximityRange.Set(innerShip, 20);
        var inner = Enter(innerShip); Check(ProximityCollisionPatch.BodyBuffer() > ProximityRange.NativeBodyAU, "Nested scope not set");
        Exit(inner); Check(ProximityCollisionPatch.BodyBuffer() == ProximityRange.NativeBodyAU, "Nested scope did not restore"); Exit(outer);

        foreach (var direction in new[] { new UnityEngine.Vector2(-20, -16), new UnityEngine.Vector2(-20, 16), new UnityEngine.Vector2(20, 16), new UnityEngine.Vector2(20, -16), new UnityEngine.Vector2(-3, 8) }) {
            double radians = RotaryKnob.Angle(direction) * Math.PI / 180;
            double length = Math.Sqrt(direction.x * direction.x + direction.y * direction.y);
            Check(Math.Abs(-Math.Sin(radians) - direction.x / length) < 0.00001 && Math.Abs(Math.Cos(radians) - direction.y / length) < 0.00001, "Rotated orange index does not point at detent label");
        }
        foreach (int state in new[] { 0, 1, 2, 3 }) {
            var position = RotaryKnob.DetentPosition(state);
            var next = RotaryKnob.DetentPosition((state + 1) % 4);
            Check(Math.Abs(position.magnitude - 20) < 0.0001f, "Detent label radius is not consistent");
            double separation = state == 3 ? 150 : 50;
            Check(Math.Abs(UnityEngine.Vector2.Dot(position, next) - 400 * Math.Cos(separation * Math.PI / 180)) < 0.0002f, "Detent label angles are not evenly spaced on the upper arc");
            Check(position.y > 0, "Detent label entered the lower caption space");
            double markerAngle = RotaryKnob.Angle(position) * Math.PI / 180;
            Check(Math.Abs(-Math.Sin(markerAngle) - position.x / 20) < 0.00001 && Math.Abs(Math.Cos(markerAngle) - position.y / 20) < 0.00001, "Index does not align with fixed numeric label");
            Check(RotaryKnob.DragState(state, 23, 0) == state, "Small drag changed detent");
            Check(RotaryKnob.DragState(state, 24, 0) == Math.Min(3, state + 1), "Right drag/clamp incorrect");
            Check(RotaryKnob.DragState(state, 0, -24) == Math.Max(0, state - 1), "Down drag/clamp incorrect");
            Check(RotaryKnob.DragState(state, 240, 0) == 3 && RotaryKnob.DragState(state, -240, 0) == 0, "Drag wrapped past end detents");
        }
        using (var image = typeof(RotaryKnob).Assembly.GetManifestResourceStream("Ostrabus.NavConsole.RoundKnob.png")) {
            var signature = new byte[8];
            Check(image != null && image.Read(signature, 0, signature.Length) == 8 && signature.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }), "Embedded knob PNG missing or corrupt");
        }

        var target = AccessTools.Method(typeof(CollisionManager), nameof(CollisionManager.CheckCollisions));
        var reader = typeof(PatchProcessor).GetMethods().Single(m => m.Name == "GetOriginalInstructions" && m.GetParameters().Length == 2 && !m.GetParameters()[1].ParameterType.IsByRef);
        var original = ((IEnumerable<CodeInstruction>)reader.Invoke(null, new object[] { target, null })).ToList();
        var copy = original.Select(i => new CodeInstruction(i)).ToList();
        var patched = ((IEnumerable<CodeInstruction>)AccessTools.Method(typeof(ProximityCollisionPatch), "Transpiler").Invoke(null, new object[] { copy })).ToList();
        Check(patched.Count == original.Count + 2, "Expected only two inserted instructions");
        Check(patched.Count(i => i.Calls(AccessTools.Method(typeof(ProximityCollisionPatch), "ShipBuffer"))) == 2, "Ship warning comparison/normalization not both patched");
        Check(patched.Count(i => i.Calls(AccessTools.Method(typeof(ProximityCollisionPatch), "BodyBuffer"))) == 2, "Body warning comparison/normalization not both patched");
        Check(patched.Count(i => i.Calls(AccessTools.Method(typeof(Ostrabus.ShipNavigation.StationDockedCollisions), "IgnoreAsObstacle"))) == 1, "Station-docked candidate filter not installed exactly once");
        int n = 0;
        foreach (var instruction in patched) {
            if (instruction.Calls(AccessTools.Method(typeof(ProximityCollisionPatch), "ShipBuffer"))) continue;
            var before = original[n++];
            if (instruction.Calls(AccessTools.Method(typeof(Ostrabus.ShipNavigation.StationDockedCollisions), "IgnoreAsObstacle"))) {
                Check(instruction.opcode == System.Reflection.Emit.OpCodes.Call && before.Calls(AccessTools.PropertyGetter(typeof(global::Ship), nameof(global::Ship.HideFromSystem))), "Station filter replaced a different collision instruction");
                continue;
            }
            bool replaced = instruction.Calls(AccessTools.Method(typeof(ProximityCollisionPatch), "BodyBuffer"));
            Check(replaced ? before.operand is double d && d == ProximityRange.NativeBodyAU : instruction.opcode == before.opcode && Equals(instruction.operand, before.operand), "Unrelated collision instruction changed");
        }
        h.CreateClassProcessor(typeof(ProximityCollisionPatch)).Patch();
        h.CreateClassProcessor(typeof(ProximityDialInitPatch)).Patch();
        h.CreateClassProcessor(typeof(ProximityDialRefreshPatch)).Patch();
        Check(Harmony.GetPatchInfo(target).Transpilers.Count == 1, "Native collision hook not installed");
        ScrollbackChecks.Run(h);
        h.UnpatchSelf();
        System.Console.WriteLine(checks + " nav-console range/save/native-IL checks passed. Native scene layout remains a live check.");
        return 0;
    }
}

namespace Ostrabus {
    internal static class OstrabusPlugin { internal static readonly TestLog Logger = new TestLog(); }
    internal sealed class TestLog { internal void LogWarning(string message) { throw new Exception(message); } }
}
