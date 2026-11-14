using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;

namespace Ostrabus.ShipNavigation;

// Replace only the semantic local-coordinate call sites. Physical nearest-body
// checks (LOS, gravity, atmosphere, impact avoidance) in these methods stay native.
[HarmonyPatch]
internal static class LocalNavigationFramePatch {
    internal static IEnumerable<MethodBase> Targets() {
        yield return AccessTools.Method(typeof(NavyPilot), nameof(NavyPilot.GetTarget));
        yield return AccessTools.Method(typeof(NavyPilot), "GetRandomSpotsWithinInfluenceRange");
        yield return AccessTools.Method(typeof(BlockadePilot), nameof(BlockadePilot.GetTarget));
        yield return AccessTools.Method(typeof(BlockadePilot), "GetRandomSpotsWithinInfluenceRange");
        yield return AccessTools.Method(typeof(Lurk), nameof(Lurk.RunCommand));
        yield return AccessTools.Method(typeof(Guard), nameof(Guard.RunCommand));
        yield return AccessTools.Method(typeof(LockPirate), nameof(LockPirate.RunCommand));
        yield return AccessTools.Method(typeof(AIShipManager), nameof(AIShipManager.CheckLocalAuthorityScenario));
        yield return AccessTools.Method(typeof(AIShipManager), nameof(AIShipManager.SpawnNewBeaconResponse));
        yield return AccessTools.Method(typeof(BeatManager), nameof(BeatManager.GenerateTutorialDerelict));
        yield return AccessTools.Method(typeof(BeatManager), "Pirate");
        yield return AccessTools.Method(typeof(BeatManager), "FactionShip");
        yield return AccessTools.Method(typeof(BeatManager), "FactionBattle");
    }
    [HarmonyTargetMethods]
    static IEnumerable<MethodBase> TargetMethods() => Targets();
    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod) {
        var implicitLock = AccessTools.Method(typeof(ShipSitu), nameof(ShipSitu.LockToBO), new[] { typeof(double), typeof(bool) });
        var explicitLock = AccessTools.Method(typeof(ShipSitu), nameof(ShipSitu.LockToBO), new[] { typeof(BodyOrbit), typeof(double) });
        bool explicitSite = __originalMethod.DeclaringType == typeof(Lurk) || __originalMethod.DeclaringType == typeof(Guard);
        var old = explicitSite ? explicitLock : implicitLock;
        var replace = AccessTools.Method(typeof(LocalNavigationFrame), explicitSite ? nameof(LocalNavigationFrame.BindExplicit) : nameof(LocalNavigationFrame.Bind));
        int count = 0;
        foreach (var instruction in instructions) {
            if (instruction.Calls(old)) { instruction.opcode = OpCodes.Call; instruction.operand = replace; count++; }
            yield return instruction;
        }
        if (count != 1) throw new InvalidOperationException("Expected one local reference binding in " + __originalMethod + "; found " + count);
    }
}

[HarmonyPatch]
internal static class LocalNavigationPlacementPatch {
    [HarmonyTargetMethods]
    internal static IEnumerable<MethodBase> Targets() {
        yield return AccessTools.Method(typeof(PiratePilot), nameof(PiratePilot.GetTarget));
        yield return AccessTools.Method(typeof(PolicePilot), nameof(PolicePilot.FFWD));
    }
    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var nearest = AccessTools.Method(typeof(StarSystem), nameof(StarSystem.GetNearestBO));
        var replace = AccessTools.Method(typeof(LocalNavigationFrame), nameof(LocalNavigationFrame.Nearest));
        int count = 0;
        foreach (var instruction in instructions) {
            if (instruction.Calls(nearest)) { instruction.opcode = OpCodes.Call; instruction.operand = replace; count++; }
            yield return instruction;
        }
        if (count != 1) throw new InvalidOperationException("Expected one local placement reference; found " + count);
    }
}
