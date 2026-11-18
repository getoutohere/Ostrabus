using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Ostranauts.ShipGUIs.NavStation;
using Ostranauts.ShipGUIs.Utilities;

namespace Ostrabus.MiscBugFixes;

[HarmonyPatch(typeof(NavModCoursePlot), "PlanTrip")]
internal static class TorchCoursePlanningPatch {
    static void Prefix(out ShipNavigation.TorchCourses.TorchTransferPlanning.Scope __state) =>
        __state = ShipNavigation.TorchCourses.TorchTransferPlanning.Enter();
    static void Postfix(ref NavData __result) {
        __result = ShipNavigation.TorchCourses.TorchTransferPlanning.Complete(__result);
        TorchCourseFuel.Attach(__result, false);
    }
    static void Finalizer(ShipNavigation.TorchCourses.TorchTransferPlanning.Scope __state) =>
        ShipNavigation.TorchCourses.TorchTransferPlanning.Leave(__state);
}

[HarmonyPatch(typeof(NavData), nameof(NavData.TimeAdvance))]
internal static class TorchCoursePlaybackPatch {
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var code = new List<CodeInstruction>(instructions);
        var write = AccessTools.Method(typeof(Ship),nameof(Ship.SetReactorGPMValue));
        var controlled = AccessTools.Method(typeof(TorchCourseFuel),nameof(TorchCourseFuel.WritePlaybackControl));
        int cycles = 0, flows = 0;
        for (int i = 3; i < code.Count; i++) {
            if (!code[i].Calls(write) || !(code[i-1].operand is MethodInfo convert) ||
                convert.Name != nameof(ToString) || convert.GetParameters().Length != 0 ||
                (convert.DeclaringType != typeof(float) && convert.DeclaringType != typeof(double))) continue;
            if (code[i-3].opcode != OpCodes.Ldstr) throw new InvalidOperationException("Unknown computed course control key.");
            string key = code[i-3].operand as string;
            if (key == "slidCycle") cycles++;
            else if (key == "slidFlow") flows++;
            else throw new InvalidOperationException("Unknown computed course control: " + key);
            code[i].opcode = OpCodes.Call;
            code[i].operand = controlled;
        }
        if (cycles != 1 || flows != 1) throw new InvalidOperationException("Expected one computed CYCLE and one FLOW write in NavData.TimeAdvance.");
        return code;
    }
    static bool Prefix(NavData __instance, ref bool __result, out TorchCourseFuel.PlaybackScope __state) {
        __state = new TorchCourseFuel.PlaybackScope { Profile = TorchCourseFuel.Playing, Nav = TorchCourseFuel.PlayingNav };
        var profile = TorchCourseFuel.Find(__instance);
        TorchCourseFuel.Playing = profile;
        TorchCourseFuel.PlayingNav = __instance;
        if (profile == null || TorchCourseFuel.Valid(profile)) return true;
        TorchCourseFuel.Abort(profile);
        __result = false;
        return false;
    }
    static void Postfix(ShipSitu __0, ref bool __result) {
        var profile = TorchCourseFuel.Playing;
        if (profile == null) return;
        if (profile.Invalid) {
            TorchCourseFuel.Abort(profile);
            __result = false;
        } else if (__result) TorchCourseFuel.PlaybackInput(profile,__0);
    }
    static Exception Finalizer(Exception __exception, TorchCourseFuel.PlaybackScope __state) {
        TorchCourseFuel.Playing = __state.Profile;
        TorchCourseFuel.PlayingNav = __state.Nav;
        return __exception;
    }
}

[HarmonyPatch(typeof(FusionIC), nameof(FusionIC.SetReactants))]
internal static class TorchCourseReactantsPatch {
    static bool Prefix(FusionIC __instance, ref double fShallowFusionRemain) {
        var profile = TorchCourseFuel.Playing;
        bool adopted = false;
        if (profile == null) { profile = TorchCourseFuel.AdoptNative(__instance); adopted = profile != null; }
        if (profile == null || __instance.COSelf != profile.Reactor) return true;
        if (!TorchCourseFuel.Valid(profile)) { TorchCourseFuel.Abort(profile); return false; }
        if (adopted) TorchCourseFuel.Control(__instance, .27000001072883606);
        double usage = __instance.MassUsageMax;
        if (!TorchCourseFuel.Finite(usage) || usage <= 0) { TorchCourseFuel.Abort(profile); return false; }
        fShallowFusionRemain = TorchCourseFuel.Budget(profile, StarSystem.fEpoch)*profile.Usage/usage;
        return true;
    }
    static void Postfix(FusionIC __instance) {
        var profile = TorchCourseFuel.Playing;
        if (profile == null || profile.Invalid || __instance.COSelf != profile.Reactor) return;
        TorchCourseFuel.Observe(profile);
        TorchCourseFuelConsumption.Reconciled(__instance, profile);
    }
}

[HarmonyPatch(typeof(FusionIC), "Run")]
internal static class TorchCourseReactorPatch {
    static void Prefix(FusionIC __instance, double fDeltaTime) => TorchCourseFuel.Control(__instance, fDeltaTime);
    static void Postfix(FusionIC __instance) {
        var profile = TorchCourseFuel.Find(__instance.COSelf?.ship?.objSS?.NavData, false);
        if (profile == null) return;
        if (!TorchCourseFuel.Valid(profile, false)) TorchCourseFuel.Abort(profile);
        else TorchCourseFuel.Observe(profile);
    }
}
