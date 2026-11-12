using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Ostranauts.Ships.AIPilots;

namespace Ostrabus.MiscBugFixes;

// IsStale describes a retired hull, not the age of its current voyage. Both new
// pilots and saved controllers pass the existing crew-validated registration.
internal static class ActiveShipRetirement {
    static readonly object Gate = new object();

    internal static void Reactivated(AIShip ai) {
        if (ai == null || (ai.AIType & CrewedAIControl.NeedsCrew) == 0) return;
        Ship ship = ai.Ship;
        if (ship == null || ship.bDestroyed || ship.objSS == null || ship.ShipCO == null ||
            !ship.IsAIShip || ship.HideFromSystem || ship.objSS.bIsBO ||
            !ReferenceEquals(AIShipManager.GetAIShipByRegID(ship.strRegID), ai)) return;
        lock (Gate) {
            // An already-committed deletion may be scuttling, arena cleanup or a
            // saved removal flag. Registration is not permission to undo it.
            if (!ship.bRemove && ship.ShipCO.GetCondAmount("IsStale", isThreshold:false) > 0) ship.ShipCO.ZeroCondAmount("IsStale");
        }
    }

    // Native eligibility and player/plot protection still run first. Revalidate
    // at the write boundary: its worker may have read the old timer before a
    // main-thread reactivation cancelled it. No AI dictionaries are read here.
    internal static bool Mark(Ship ship, bool remove) {
        lock (Gate) {
            if (!remove || ship == null || ship.bDestroyed || ship.bRemove || ship.ShipCO == null) return false;
            double stamp = ship.ShipCO.GetCondAmount("IsStale", isThreshold:false);
            if (!(stamp > 0) || !(StarSystem.fEpoch - stamp >= 10800)) return false;
            ship.bRemove = true;
            UnityEngine.Debug.Log("Marking ship as stale: " + ship.ToString());
            return true;
        }
    }
}

[HarmonyPatch]
internal static class StaleShipMarkPatch {
    static MethodBase TargetMethod() {
        // Match the owning method name, without assuming a compiler-generated
        // display-class number. The transpiler also verifies the write/log site.
        var targets = typeof(StarSystem).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
            .SelectMany(AccessTools.GetDeclaredMethods)
            .Where(m => m.Name.StartsWith("<CleanupStaleShips>b__", StringComparison.Ordinal)).ToArray();
        if (targets.Length != 1 || targets[0] == null || targets[0].ReturnType != typeof(void) ||
            targets[0].GetParameters().Length != 0)
            throw new InvalidOperationException("Unknown stale-cleanup worker; expected one parameterless Task delegate.");
        return targets[0];
    }

    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var code = new List<CodeInstruction>(instructions);
        var removal = AccessTools.Field(typeof(Ship), nameof(Ship.bRemove));
        var log = AccessTools.Method(typeof(UnityEngine.Debug), nameof(UnityEngine.Debug.Log), new[] { typeof(object) });
        int index = -1, count = 0;
        for (int i = 0; i < code.Count; i++) {
            if (code[i].opcode != OpCodes.Stfld || !Equals(code[i].operand, removal)) continue;
            index = i;
            count++;
        }
        if (count != 1 || index < 1 || code[index - 1].opcode != OpCodes.Ldc_I4_1 ||
            index + 1 >= code.Count || code[index + 1].opcode != OpCodes.Ldstr ||
            !Equals(code[index + 1].operand, "Marking ship as stale: "))
            throw new InvalidOperationException("Unknown stale-cleanup mark; refusing to replace another removal policy.");
        int end = index + 2;
        while (end < code.Count && end < index + 12 && !code[end].Calls(log)) end++;
        if (end + 1 >= code.Count || !code[end].Calls(log))
            throw new InvalidOperationException("Unknown stale-cleanup log boundary.");
        // Harmony's framework Label type is not exposed by the netstandard
        // facade. Inspect its collection through ICollection at patch time.
        var labels = AccessTools.Field(typeof(CodeInstruction), "labels");
        for (int i = index + 1; i <= end; i++)
            if (((System.Collections.ICollection)labels.GetValue(code[i])).Count != 0 || code[i].blocks.Count != 0)
                throw new InvalidOperationException("Unexpected control flow inside stale-cleanup logging.");
        code[index].opcode = OpCodes.Call;
        code[index].operand = AccessTools.Method(typeof(ActiveShipRetirement), nameof(ActiveShipRetirement.Mark));
        code.RemoveRange(index + 1, end - index);
        code.Insert(index + 1, new CodeInstruction(OpCodes.Pop));
        return code;
    }
}
