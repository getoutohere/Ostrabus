using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.Ships.Commands;

namespace Ostrabus.MiscBugFixes;

// Planned NavData pays for its own travel. Returning to manual flight must advance
// the manual progress watermark without charging that intervening travel again.
// Weak keys own no ships/controllers; the optional string payload survives saves
// and is ignored by vanilla FlyTo's first-five-fields-only SaveData setter.
internal static class AIFlightFuelHandoff {
    const string SaveTag = "ostrabus.flightfuel.v1";
    sealed class State {
        public State() {}
        internal bool Planned;
        internal string Prediction, TripId;
    }
    static readonly ConditionalWeakTable<FlyTo, State> States = new ConditionalWeakTable<FlyTo, State>();
    static readonly FieldInfo Trip = AccessTools.Field(typeof(FlyTo), "_tripFuelDTO");
    static readonly FieldInfo Id = AccessTools.Field(Trip.FieldType, "TripID");
    static readonly FieldInfo Cost = AccessTools.Field(Trip.FieldType, "FuelCost");
    static readonly FieldInfo Progress = AccessTools.Field(Trip.FieldType, "Progress");
    static readonly AccessTools.FieldRef<FlyTo, object> TripValue = AccessTools.FieldRefAccess<FlyTo, object>(Trip);
    static readonly AccessTools.FieldRef<object, double> CostValue = AccessTools.FieldRefAccess<object, double>(Cost);
    static readonly AccessTools.FieldRef<object, double> ProgressValue = AccessTools.FieldRefAccess<object, double>(Progress);
    static readonly Action<FlyTo, Blackboard> Fetch = (Action<FlyTo, Blackboard>)AccessTools.Method(typeof(FlyTo), "FetchFuelData").CreateDelegate(typeof(Action<FlyTo, Blackboard>));
    static readonly Func<FlyTo, double> GetProgress = (Func<FlyTo, double>)AccessTools.Method(typeof(FlyTo), "GetTripFuelProgress").CreateDelegate(typeof(Func<FlyTo, double>));
    static bool Finite(double n) => !double.IsNaN(n) && !double.IsInfinity(n);

    internal static void Planned(FlyTo flight, Blackboard blackboard) {
        string prediction = blackboard.Get<string>("TripFuelPrediction");
        if (string.IsNullOrEmpty(prediction)) return;
        State state = States.GetOrCreateValue(flight);
        if (state.Planned && state.Prediction == prediction) return;
        state.Planned = false;
        Fetch(flight, blackboard);
        object trip = Trip.GetValue(flight);
        if (trip == null) return;
        double cost = (double)Cost.GetValue(trip), progress = GetProgress(flight);
        if (!Finite(cost) || cost <= 0 || !Finite(progress) || progress < 0) return;
        state.TripId = (string)Id.GetValue(trip);
        state.Prediction = prediction;
        state.Planned = true;
    }

    internal static void Fetched(FlyTo flight, Blackboard blackboard) {
        if (!States.TryGetValue(flight, out State state) || !state.Planned) return;
        state.Planned = false;
        object trip = Trip.GetValue(flight);
        if (trip == null || state.Prediction != blackboard.Get<string>("TripFuelPrediction") || state.TripId != (string)Id.GetValue(trip)) return;
        double now = GetProgress(flight), previous = (double)Progress.GetValue(trip);
        if (Finite(now) && now >= 0 && Finite(previous) && previous >= 0) Progress.SetValue(trip, Math.Max(previous, now));
    }

    internal static void Saved(FlyTo flight, ref string[] data) {
        if (data == null || data.Length != 5 || !States.TryGetValue(flight, out State state) || !state.Planned || data[1] != state.TripId) return;
        Array.Resize(ref data, 7);
        data[5] = SaveTag;
        data[6] = state.Prediction;
    }

    internal static void Loaded(FlyTo flight, string[] data) {
        States.Remove(flight);
        if (data == null || data.Length != 7 || data[5] != SaveTag || string.IsNullOrEmpty(data[6])) return;
        object trip = Trip.GetValue(flight);
        if (trip == null) return;
        var state = States.GetOrCreateValue(flight);
        state.TripId = (string)Id.GetValue(trip);
        state.Prediction = data[6];
        state.Planned = true;
    }

    internal static void PhysicallyPaid(FlyTo flight, double progress) {
        // Native Full ships debit physical tanks rather than ApplyTripFuel.
        // Keep their serializable watermark current for later shallow execution.
        if (flight.ShipUs.LoadState != Ship.Loaded.Full) return;
        object trip = TripValue(flight);
        if (trip == null || !Finite(progress) || progress < 0) return;
        double cost = CostValue(trip), previous = ProgressValue(trip);
        if (Finite(cost) && cost > 0 && Finite(previous) && progress > previous) ProgressValue(trip) = progress;
    }
}

[HarmonyPatch(typeof(FlyTo), nameof(FlyTo.RunCommand))]
internal static class AIFlightFuelPathPatch {
    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var code = new List<CodeInstruction>(instructions);
        FieldInfo path = AccessTools.Field(typeof(FlyTo), "_flyToPath");
        MethodInfo run = AccessTools.Method(typeof(ICommand), nameof(ICommand.RunCommand));
        int matches = 0;
        for (int i = 0; i + 3 < code.Count; i++) {
            if (code[i].opcode != OpCodes.Ldarg_0 || !code[i+1].LoadsField(path) || code[i+2].opcode != OpCodes.Ldarg_1 || !code[i+3].Calls(run)) continue;
            var first = new CodeInstruction(OpCodes.Ldarg_0);
            code[i].MoveLabelsTo(first); code[i].MoveBlocksTo(first);
            code.InsertRange(i, new[] {first, new CodeInstruction(OpCodes.Ldarg_1), new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(AIFlightFuelHandoff), nameof(AIFlightFuelHandoff.Planned)))});
            matches++; i += 3;
        }
        if (matches != 1) throw new InvalidOperationException("FlyTo planned-flight dispatch changed; fuel handoff patch requires review.");
        return code;
    }
}

[HarmonyPatch(typeof(FlyTo), "FetchFuelData")]
internal static class AIFlightFuelManualPatch {
    [HarmonyPostfix]
    internal static void Postfix(FlyTo __instance, Blackboard blackboard) => AIFlightFuelHandoff.Fetched(__instance, blackboard);
}

[HarmonyPatch(typeof(FlyTo), "get_SaveData")]
internal static class AIFlightFuelSavePatch {
    [HarmonyPostfix]
    internal static void Postfix(FlyTo __instance, ref string[] __result) => AIFlightFuelHandoff.Saved(__instance, ref __result);
}

[HarmonyPatch(typeof(FlyTo), "set_SaveData")]
internal static class AIFlightFuelLoadPatch {
    [HarmonyPostfix]
    internal static void Postfix(FlyTo __instance, string[] value) => AIFlightFuelHandoff.Loaded(__instance, value);
}

[HarmonyPatch(typeof(FlyTo), "ApplyTripFuel")]
internal static class AIFlightPhysicalFuelPatch {
    [HarmonyPostfix]
    internal static void Postfix(FlyTo __instance, double progress) => AIFlightFuelHandoff.PhysicallyPaid(__instance, progress);
}
