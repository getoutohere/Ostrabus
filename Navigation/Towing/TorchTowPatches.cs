using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using SolarSystem;

namespace Ostrabus.ShipNavigation.Towing;

[HarmonyPatch(typeof(ShipSitu), nameof(ShipSitu.TimeAdvance))]
internal static class DockedCourseMemberPatch {
    static bool Prefix(ShipSitu __instance, double fTime) => !DockedCoursePlayback.DeferMember(__instance, fTime);
}

[HarmonyPatch(typeof(DockGroup), nameof(DockGroup.TimeAdvance))]
internal static class DockedCourseGroupPatch {
    static void Prefix(DockGroup __instance, double timeDelta, out DockedCoursePlayback.StepScope __state) {
        __state = DockedCoursePlayback.EnterStep();
        DockedCoursePlayback.PrepareStep(__instance, timeDelta);
    }
    static void Finalizer(DockedCoursePlayback.StepScope __state) => DockedCoursePlayback.LeaveStep(__state);

    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var advance = AccessTools.Method(typeof(ShipSitu), nameof(ShipSitu.TimeAdvance));
        var aero = AccessTools.Method(typeof(DockGroup), "CalculateAverageLiftDrag");
        var code = instructions.ToList();
        var result = new List<CodeInstruction>(code.Count + 1);
        int matches = 0, aeroMatches = 0;
        for (int i = 0; i < code.Count; i++) {
            var instruction = code[i];
            if (instruction.Calls(advance)) {
                if (i == 0 || code[i - 1].opcode != OpCodes.Ldc_I4_0) throw new InvalidOperationException("Dock-group acceleration flag changed.");
                if (instruction.blocks.Count != 0) throw new InvalidOperationException("Dock-group integration exception boundary changed.");
                var group = new CodeInstruction(OpCodes.Ldarg_0);
                instruction.MoveLabelsTo(group);
                result.Add(group);
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(DockedCoursePlayback), nameof(DockedCoursePlayback.AdvanceAnchor));
                matches++;
            } else if (instruction.Calls(aero)) {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(DockedCoursePlayback), nameof(DockedCoursePlayback.AverageLiftDrag));
                aeroMatches++;
            }
            result.Add(instruction);
        }
        if (matches != 1 || aeroMatches != 1) throw new InvalidOperationException("Dock-group integration changed: " + matches + " anchor steps, " + aeroMatches + " lift/drag updates.");
        return result;
    }
}

[HarmonyPatch]
internal static class TorchTowMassPatch {
    static IEnumerable<MethodBase> TargetMethods() {
        yield return AccessTools.Method(typeof(Ship), nameof(Ship.GetMaxTorchThrust));
        yield return AccessTools.Method(typeof(Ship), nameof(Ship.SetThrust));
        yield return AccessTools.Method(typeof(FusionIC), "Fusion");
    }
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod) {
        var mass = AccessTools.PropertyGetter(typeof(Ship), nameof(Ship.Mass));
        // GetMaxTorchThrust supplies departure previews. Fusion reverses that
        // calculation to recover engine force, so it must use the same mass.
        // SetThrust remains physical: every actually attached hull still counts.
        string replacement = __originalMethod.DeclaringType == typeof(Ship) && __originalMethod.Name == nameof(Ship.SetThrust)
            ? nameof(DockedCoursePlayback.InertialMass) : nameof(DockedCoursePlayback.PredictionMass);
        var code = instructions.ToList();
        int matches = 0;
        foreach (var instruction in code) if (instruction.Calls(mass)) {
            instruction.opcode = OpCodes.Call;
            instruction.operand = AccessTools.Method(typeof(DockedCoursePlayback), replacement);
            matches++;
        }
        if (matches != 1) throw new InvalidOperationException(__originalMethod.Name + " torch mass conversion changed; expected one mass use, found " + matches + ".");
        return code;
    }
}
