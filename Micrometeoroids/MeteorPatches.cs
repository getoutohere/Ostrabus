using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using Ostranauts.Ships;

namespace Ostrabus.Micrometeoroids;

[HarmonyPatch(typeof(StarSystem), nameof(StarSystem.SpawnMicroMeteoroid))]
internal static class MeteorBypassPatch {
    // Native "roll < 5 || !defense" must always evaluate defense. Preserve the
    // downstream native damage/success branches rather than copying the method.
    static int AllowDefense(int min, int max) => 5;
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var code = instructions.ToList();
        var range = AccessTools.Method(typeof(UnityEngine.Random), nameof(UnityEngine.Random.Range), new[] { typeof(int), typeof(int) });
        var calls = code.Where(i => i.Calls(range)).ToArray();
        if (calls.Length != 1) throw new InvalidOperationException("Micrometeoroid bypass IL changed; expected one integer random roll.");
        calls[0].operand = AccessTools.Method(typeof(MeteorBypassPatch), nameof(AllowDefense));
        return code;
    }
}

[HarmonyPatch(typeof(WeaponsSystem), nameof(WeaponsSystem.TriggerMicroMeteoroidDefense))]
internal static class MeteorDefensePatch {
    static bool Prefix(WeaponsSystem __instance, float angle, ref bool __result) {
        __result = MeteorDefense.Defend(__instance, angle);
        return false;
    }
}

[HarmonyPatch(typeof(WeaponsSystem), "Shoot")]
internal static class MeteorBurstCapturePatch {
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var code = instructions.ToList();
        var getAmmo = AccessTools.Method(typeof(WeaponsSystem), "GetAmmoShot");
        int found = 0;
        for (int i = 0; i < code.Count; i++) {
            if (!code[i].Calls(getAmmo)) continue;
            // The preceding load is the CondOwner passed to GetAmmoShot.
            var load = code[i - 1];
            if (load.opcode != OpCodes.Ldloc && load.opcode != OpCodes.Ldloc_S && load.opcode != OpCodes.Ldloc_0 &&
                load.opcode != OpCodes.Ldloc_1 && load.opcode != OpCodes.Ldloc_2 && load.opcode != OpCodes.Ldloc_3)
                throw new InvalidOperationException("PDC ammunition selection IL changed.");
            code.Insert(++i, new CodeInstruction(load.opcode, load.operand));
            code.Insert(++i, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(MeteorDefense), nameof(MeteorDefense.CaptureAmmo))));
            found++;
        }
        if (found != 1) throw new InvalidOperationException("Expected one native burst ammunition selection.");
        return code;
    }
}

[HarmonyPatch(typeof(BeatManager), "Micrometeoroid")]
internal static class MeteorTargetPatch {
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator) {
        var code = instructions.ToList();
        var spawn = AccessTools.Method(typeof(StarSystem), nameof(StarSystem.SpawnMicroMeteoroid));
        int call = code.FindIndex(i => i.Calls(spawn));
        if (call < 0 || code.Count(i => i.Calls(spawn)) != 1) throw new InvalidOperationException("BeatManager meteor spawn IL changed.");
        var shipGetter = AccessTools.PropertyGetter(typeof(CondOwner), "ship");
        int logShip = code.FindIndex(call + 1, i => i.Calls(shipGetter));
        if (logShip < 0) throw new InvalidOperationException("BeatManager meteor log target IL changed.");
        var selected = generator.DeclareLocal(typeof(Ship));
        code[call].operand = AccessTools.Method(typeof(MeteorTargets), nameof(MeteorTargets.Spawn));
        code.Insert(logShip + 1, new CodeInstruction(OpCodes.Pop));
        code.Insert(logShip + 2, new CodeInstruction(OpCodes.Ldloc, selected));
        code.Insert(call + 1, new CodeInstruction(OpCodes.Stloc, selected));
        return code;
    }
}
