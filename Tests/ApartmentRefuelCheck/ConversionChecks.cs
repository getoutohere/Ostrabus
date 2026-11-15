using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Newtonsoft.Json;
using Ostrabus.ApartmentRefuel;
using UnityEngine;

static partial class Program {
    static bool NoItem(ref Item __result) { __result = null; return false; }
    static bool NoHighlight(ref bool __result) { __result = false; return false; }
    static bool Rotation(ref Quaternion __result) { __result = Quaternion.identity; return false; }
    static bool Euler(ref Vector3 __result) { __result = Vector3.zero; return false; }
    static bool Identity(CondOwner __instance, string value) { Field(__instance, "_strID", value); DataHandler.mapCOs[value] = __instance; return false; }
    static COOverlay Overlay(CondOwner item) => null;

    // Execute the native mode-switch metadata transfer, stopping at the scene
    // inventory/slot/room move boundary. This is not a Unity lifecycle test.
    static IEnumerable<CodeInstruction> MetadataBoundary(IEnumerable<CodeInstruction> instructions) {
        bool stopped = false;
        foreach (var code in instructions) {
            if (code.opcode == OpCodes.Newobj && code.operand is ConstructorInfo ctor && ctor.DeclaringType == typeof(List<CondOwner>)) {
                yield return new CodeInstruction(OpCodes.Ret).MoveLabelsFrom(code).MoveBlocksFrom(code);
                stopped = true; break;
            }
            if (code.operand is MethodInfo method && method.IsGenericMethod && method.Name == "GetComponent" && method.GetGenericArguments()[0] == typeof(COOverlay)) {
                code.opcode = OpCodes.Call; code.operand = AccessTools.Method(typeof(Program), nameof(Overlay));
            }
            yield return code;
        }
        if (!stopped) throw new Exception("Native mode-switch scene boundary changed");
    }

    static CondOwner Defined(string id, string definition) {
        var co = Owner(id, definition);
        foreach (string value in DataHandler.dictCOs[definition].aStartingConds) {
            var parts = value.Split('=');
            Value(co, parts[0], double.Parse(parts[1].Split('x')[1], CultureInfo.InvariantCulture));
        }
        co.aCondZeroes = new List<string>(); co.aLot = new Ostranauts.Core.Models.TrackingCollection<CondOwner>();
        co.mapGUIPropMaps = new Dictionary<string, Dictionary<string, string>>();
        return co;
    }

    static void ConversionChecks() {
        var boundary = new Harmony("offline.apartment-refuel.mode-boundary");
        void Prefix(Type type, string method, string callback) => boundary.Patch(AccessTools.Method(type, method), prefix: new HarmonyMethod(typeof(Program), callback));
        try {
            Prefix(typeof(CondOwner), "ValidateParentRecursive", nameof(Quiet));
            Prefix(typeof(CondOwner), "get_Item", nameof(NoItem));
            Prefix(typeof(CondOwner), "get_Highlight", nameof(NoHighlight));
            Prefix(typeof(CondOwner), "get_DimLights", nameof(NoHighlight));
            Prefix(typeof(CondOwner), "set_strID", nameof(Identity));
            Prefix(typeof(Transform), "set_position", nameof(Quiet));
            Prefix(typeof(Transform), "get_rotation", nameof(Rotation));
            Prefix(typeof(Quaternion), "get_eulerAngles", nameof(Euler));
            var native = AccessTools.Method(typeof(CondOwner), "ModeSwitchInternal");
            boundary.Patch(native, transpiler: new HarmonyMethod(typeof(Program), nameof(MetadataBoundary)));
            Check(DataHandler.GetCond("StatDamage").bPersists, "Native wear does not persist across conversions");
            foreach (string damage in new[] { "", "Dmg" }) {
                var current = Defined("original-terminal" + damage, Runtime.Item + damage + "Loose");
                Value(current, "StatDamage", 17);
                for (int cycle = 0; cycle < 12; cycle++) {
                    string suffix = damage + (cycle % 2 == 0 ? "" : "Loose");
                    var next = Defined("temporary-output" + cycle, Runtime.Item + suffix);
                    native.Invoke(current, new object[] { next, Vector3.zero });
                    double price = suffix.EndsWith("Loose", StringComparison.Ordinal) ? (damage == "" ? 250000 : 50000) : (damage == "" ? 2500 : 500);
                    Check(next.strID == "original-terminal" + damage && next.GetCondAmount("StatDamage") == 17, "Native conversion changes identity/wear");
                    Check(next.GetCondAmount("StatBasePrice") == price && (next.GetCondAmount("IsInstalled") > 0) == (cycle % 2 == 0), "Native conversion carries wrong appraisal/installed flag");
                    Check(!next.mapConds.ContainsKey("StatSolidHe3") && !next.mapConds.ContainsKey("StatLiqD2O"), "Conversion creates fuel");
                    var save = new JsonCondOwnerSave { strCODef = next.strCODef, strID = next.strID, aConds = next.mapConds.Select(c => c.Key + "=1.0x" + c.Value.fCount.ToString(CultureInfo.InvariantCulture)).ToArray() };
                    save = JsonConvert.DeserializeObject<JsonCondOwnerSave>(JsonConvert.SerializeObject(save));
                    Check(save.strID == next.strID && DataHandler.dictCOs.ContainsKey(save.strCODef) && save.aConds.Contains("StatDamage=1.0x17"), "Native save schema loses terminal identity/wear");
                    current = next;
                }
            }
            Check(!typeof(Plugin).Assembly.GetReferencedAssemblies().Any(a => a.Name == "Ostrabus"), "Standalone DLL depends on omnibus plugin");
            Check(!typeof(Plugin).Assembly.GetTypes().Any(t => t.FullName.Contains("InstallationPaths")), "Standalone embeds developer installation helper");
        } finally { boundary.UnpatchSelf(); }
    }
}
