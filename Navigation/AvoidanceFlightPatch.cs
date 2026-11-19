using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Ostranauts.Ships.Commands;

namespace Ostrabus.ShipNavigation;

[HarmonyPatch(typeof(FlyTo), nameof(FlyTo.RunCommand))]
internal static class AvoidanceFlightPatch
{
    static void Prefix(FlyTo __instance, out AvoidanceQuery.State __state) => __state = AvoidanceQuery.BeginFlight(__instance.ShipUs);
    static Exception Finalizer(Exception __exception, AvoidanceQuery.State __state) { AvoidanceQuery.EndFlight(__state); return __exception; }
    internal static bool RejectCourse(CommandStatus status, FlyTo command)
    {
        var ship = command.ShipUs;
        if (AiRendezvous.ManualFallback(command, status)) return true;
        if (ship == null || !ship.IsAIShip || ship.NavPlayerManned || ship.IsUsingTorchDrive ||
            !AvoidanceQuery.Candidate(ship, ship.objSS?.NavData)) return false;
        // Keep this candidate as the forecast through the native reset/manual
        // handoff too. Otherwise its second collision check would evaluate the
        // old evasive acceleration instead of the course it is declining.
        return AIShipManager.IsOnCollisionCourse(ship, out _);
    }
    // Inspect native reset sites before FlightControlHandoff replaces them.
    // The existing handoff then rewrites our fallback's reset normally, in
    // either patch installation order.
    [HarmonyPriority(Priority.First)]
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        var code = new List<CodeInstruction>(instructions);
        var run = AccessTools.Method(typeof(ICommand), nameof(ICommand.RunCommand));
        var path = AccessTools.Field(typeof(FlyTo), "_flyToPath");
        var reset = AccessTools.Method(typeof(ShipSitu), nameof(ShipSitu.ResetNavData));
        int call = -1, manual = -1, runs = 0, resets = 0;
        for (int i = 0; i < code.Count; i++)
        {
            if (code[i].Calls(run)) { runs++; if (i >= 2 && code[i - 2].opcode == OpCodes.Ldfld && Equals(code[i - 2].operand, path)) call = i; }
            if (code[i].Calls(reset)) { resets++; manual = i - 3; }
        }
        // There are two native resets (paused and ordinary manual handoff).
        // The latter is after the planned branch. Its existing fuel accounting
        // and actual manual command remain authoritative.
        if (runs != 2 || resets != 2 || call < 0 || manual <= call || code[manual].opcode != OpCodes.Ldarg_0 ||
            code[call + 1].opcode != OpCodes.Call || code[call + 1].operand is not System.Reflection.MethodInfo method || method.Name != "LocalizedResult")
            throw new InvalidOperationException("Native FlyTo path/manual handoff changed; course admission requires review.");
        object accepted = generator.DefineLabel(), fallback = generator.DefineLabel();
        // BepInEx's framework Harmony exposes Label through mscorlib; avoid a
        // netstandard reference-assembly Label identity mismatch at compile time.
        var labels = AccessTools.Field(typeof(CodeInstruction), "labels");
        ((System.Collections.IList)labels.GetValue(code[call + 1])).Add(accepted);
        ((System.Collections.IList)labels.GetValue(code[manual])).Add(fallback);
        for (int i = 0; i < code.Count; i++)
        {
            yield return code[i];
            if (i != call) continue;
            yield return new CodeInstruction(OpCodes.Dup);
            yield return new CodeInstruction(OpCodes.Ldarg_0);
            yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(AvoidanceFlightPatch), nameof(RejectCourse)));
            yield return new CodeInstruction(OpCodes.Brfalse, accepted);
            // The native LocalizedResult receiver and path status are still on
            // the stack. Its ordinary manual branch builds its own result.
            yield return new CodeInstruction(OpCodes.Pop);
            yield return new CodeInstruction(OpCodes.Pop);
            yield return new CodeInstruction(OpCodes.Br, fallback);
        }
    }
}
