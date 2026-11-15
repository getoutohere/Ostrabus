using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Ostranauts.Ships.Commands;
using Ostranauts.Core;

namespace Ostrabus.ShipNavigation;

// Course playback owns shallow torch acceleration. When FlyTo gives control to
// its RCS/pause path, release that command rather than integrating it forever.
// Physical reactor controls and player manual thrust retain their native paths.
internal static class FlightControlHandoff {
    static readonly Func<BaseCommand, CommandStatus> Running = AccessTools.MethodDelegate<Func<BaseCommand, CommandStatus>>(AccessTools.PropertyGetter(typeof(BaseCommand), "Running"));
    static readonly Func<BaseCommand, CommandStatus> Finished = AccessTools.MethodDelegate<Func<BaseCommand, CommandStatus>>(AccessTools.PropertyGetter(typeof(BaseCommand), "Success"));
    internal static CommandStatus AwaitPilot(FlyToPath command) {
        var ship = command.ShipUs;
        // Called only by the native no-pilot branch, after any existing course
        // playback has been handled. Player takeover keeps its native success.
        if (ship.IsAIShip && !ship.NavPlayerManned && !ReferenceEquals(ship, CrewSim.coPlayer?.ship)) {
            ship.StopManeuver(false, false);
            return Running(command);
        }
        return Finished(command);
    }
    internal static void StopShallowTorch(Ship ship) {
        if (ship?.objSS != null && ship.IsAIShip && ship.LoadState == Ship.Loaded.Shallow)
            NavigationRoute.StopTorch(ship);
    }
    internal static void ResetForRcs(ShipSitu situation, FlyTo command) {
        situation.ResetNavData();
        var ship = command.ShipUs;
        if (ReferenceEquals(ship?.objSS, situation)) StopShallowTorch(ship);
    }
    internal static void RcsCommand(Ship ship, float seconds) {
        // Combat/undock/tow steering can bypass FlyTo. Do not interfere with
        // RCS alongside a still-owned course, physical reactors or player input.
        if (seconds > 0 && ship?.objSS != null && ship.objSS.NavData == null) StopShallowTorch(ship);
    }
}

[HarmonyPatch(typeof(FlyToPath), nameof(FlyToPath.RunCommand))]
internal static class FlightPilotAvailabilityPatch {
    [HarmonyTranspiler]
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var code = new List<CodeInstruction>(instructions);
        var available = AccessTools.PropertyGetter(typeof(Ship), nameof(Ship.NavAIManned));
        var success = AccessTools.PropertyGetter(typeof(BaseCommand), "Success");
        var awaitPilot = AccessTools.Method(typeof(FlightControlHandoff), nameof(FlightControlHandoff.AwaitPilot));
        int gate = code.FindIndex(c => c.Calls(available));
        if (gate < 0 || code.FindLastIndex(c => c.Calls(available)) != gate)
            throw new InvalidOperationException("Expected one flight pilot-availability gate.");
        int result = code.FindIndex(gate, c => c.Calls(success));
        if (result < 0 || result > gate + 10 || result + 1 >= code.Count || code[result + 1].opcode != OpCodes.Ret)
            throw new InvalidOperationException("Flight pilot refusal return changed.");
        code[result].opcode = OpCodes.Call;
        code[result].operand = awaitPilot;
        return code;
    }
}

[HarmonyPatch(typeof(FlyTo), nameof(FlyTo.RunCommand))]
internal static class FlightControlHandoffPatch {
    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var code = new List<CodeInstruction>(instructions);
        var reset = AccessTools.Method(typeof(ShipSitu), nameof(ShipSitu.ResetNavData));
        var handoff = AccessTools.Method(typeof(FlightControlHandoff), nameof(FlightControlHandoff.ResetForRcs));
        int sites = 0;
        for (int i = 0; i < code.Count; i++) {
            if (!code[i].Calls(reset)) continue;
            var owner = new CodeInstruction(OpCodes.Ldarg_0);
            code[i].MoveLabelsTo(owner); code[i].MoveBlocksTo(owner);
            code.Insert(i++, owner);
            code[i].opcode = OpCodes.Call; code[i].operand = handoff;
            sites++;
        }
        if (sites != 2) throw new InvalidOperationException("Expected two FlyTo RCS/pause route exits; found " + sites);
        return code;
    }
}

[HarmonyPatch(typeof(FlyToPath), "TorchOff")]
internal static class ShallowTorchShutdownPatch {
    [HarmonyPrefix]
    static void Prefix(FlyToPath __instance) => FlightControlHandoff.StopShallowTorch(__instance.ShipUs);
}

[HarmonyPatch(typeof(Ship), nameof(Ship.Maneuver))]
internal static class ShallowRcsTorchReleasePatch {
    [HarmonyPrefix]
    static void Prefix(Ship __instance, float fDeltaTime) => FlightControlHandoff.RcsCommand(__instance, fDeltaTime);
}
