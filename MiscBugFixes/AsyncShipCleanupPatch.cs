using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Ostranauts.Ships;

namespace Ostrabus.MiscBugFixes;

[HarmonyPatch]
internal static class AsyncShipCleanupPatch {
    static void Prepare() { TileHelperCleanup.Prepare(); }
    static IEnumerable<MethodBase> TargetMethods() {
        foreach (var type in new[] { typeof(BarterZoneShip), typeof(AsyncPreviewShip), typeof(AsyncRatingShip) })
            yield return AccessTools.DeclaredMethod(type, "Destroy", new[] { typeof(bool) });
    }
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var code = instructions.ToList();
        var remove = AccessTools.Method(typeof(Dictionary<string, CondOwner>), nameof(Dictionary<string, CondOwner>.Remove), new[] { typeof(string) });
        var destroy = AccessTools.Method(typeof(UnityEngine.Object), nameof(UnityEngine.Object.DestroyImmediate), new[] { typeof(UnityEngine.Object) });
        int removes = 0, destroys = 0;
        var output = new List<CodeInstruction>(code.Count + 2);
        foreach (var instruction in code) {
            MethodInfo replacement = null;
            if (instruction.Calls(remove)) { replacement = AccessTools.Method(typeof(TileHelperCleanup), nameof(TileHelperCleanup.RemoveOwned)); removes++; }
            if (instruction.Calls(destroy)) { replacement = AccessTools.Method(typeof(TileHelperCleanup), nameof(TileHelperCleanup.DestroyAsyncHierarchy)); destroys++; }
            if (replacement != null) {
                var owner = new CodeInstruction(OpCodes.Ldarg_0);
                instruction.MoveLabelsTo(owner); instruction.MoveBlocksTo(owner);
                output.Add(owner);
                instruction.opcode = OpCodes.Call; instruction.operand = replacement;
            }
            output.Add(instruction);
        }
        if (removes != 1 || destroys != 1) throw new InvalidOperationException("Async ship destruction IL changed; expected one registry removal and one hierarchy destruction.");
        return output;
    }
}
