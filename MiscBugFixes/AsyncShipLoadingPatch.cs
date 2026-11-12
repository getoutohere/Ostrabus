using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Ostranauts.Ships;
using UnityEngine;

namespace Ostrabus.MiscBugFixes;

internal static class AsyncShipJobs {
    static readonly ConditionalWeakTable<AsyncShipLoader, Dictionary<Coroutine, string>> owners = new ConditionalWeakTable<AsyncShipLoader, Dictionary<Coroutine, string>>();
    static readonly Dictionary<Type, FieldInfo> iteratorOwners = new Dictionary<Type, FieldInfo>();

    internal static Coroutine Start(AsyncShipLoader loader, IEnumerator iterator) {
        var type = iterator.GetType();
        if (!iteratorOwners.TryGetValue(type, out var field)) {
            foreach (var candidate in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)) {
                if (!typeof(Ship).IsAssignableFrom(candidate.FieldType)) continue;
                if (field != null) throw new InvalidOperationException("Async ship iterator has ambiguous ownership: " + type.FullName);
                field = candidate;
            }
            if (field == null) throw new InvalidOperationException("Async ship iterator has no owner: " + type.FullName);
            iteratorOwners.Add(type, field);
        }
        var owner = (Ship)field.GetValue(iterator);
        var coroutine = loader.StartCoroutine(iterator);
        if (coroutine != null) Remember(loader, coroutine, owner.strRegID);
        return coroutine;
    }
    internal static void Remember(AsyncShipLoader loader, Coroutine coroutine, string id) =>
        owners.GetOrCreateValue(loader)[coroutine] = id;

    internal static void Stop(AsyncShipLoader loader, Coroutine coroutine, string id) {
        if (string.IsNullOrEmpty(id) || (owners.TryGetValue(loader, out var jobs) && jobs.TryGetValue(coroutine, out string owner) && owner == id))
            loader.StopCoroutine(coroutine);
    }
    internal static void Clear(List<Coroutine> coroutines, AsyncShipLoader loader, string id) {
        if (string.IsNullOrEmpty(id)) {
            coroutines.Clear();
            owners.Remove(loader);
            return;
        }
        if (!owners.TryGetValue(loader, out var jobs)) return;
        for (int i = coroutines.Count - 1; i >= 0; i--) {
            var coroutine = coroutines[i];
            if (coroutine == null || !jobs.TryGetValue(coroutine, out string owner) || owner != id) continue;
            coroutines.RemoveAt(i);
            jobs.Remove(coroutine);
        }
        if (jobs.Count == 0) owners.Remove(loader);
    }
    internal static int Count(AsyncShipLoader loader) => owners.TryGetValue(loader, out var jobs) ? jobs.Count : 0;
}

[HarmonyPatch]
internal static class AsyncShipJobStartPatch {
    static IEnumerable<MethodBase> TargetMethods() {
        foreach (string name in new[] { "LoadBarterZoneShip", "LoadShipPreview", "LoadRatingShip" })
            yield return AccessTools.DeclaredMethod(typeof(AsyncShipLoader), name);
    }
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var start = AccessTools.Method(typeof(MonoBehaviour), nameof(MonoBehaviour.StartCoroutine), new[] { typeof(IEnumerator) });
        int matches = 0;
        foreach (var instruction in instructions) {
            if (instruction.Calls(start)) {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(AsyncShipJobs), nameof(AsyncShipJobs.Start));
                matches++;
            }
            yield return instruction;
        }
        if (matches != 1) throw new InvalidOperationException("Async ship load IL changed; expected one coroutine start.");
    }
}

[HarmonyPatch(typeof(AsyncShipLoader), nameof(AsyncShipLoader.Unload), new[] { typeof(string) })]
internal static class AsyncShipUnloadPatch {
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var stop = AccessTools.Method(typeof(MonoBehaviour), nameof(MonoBehaviour.StopCoroutine), new[] { typeof(Coroutine) });
        var clear = AccessTools.Method(typeof(List<Coroutine>), nameof(List<Coroutine>.Clear));
        int stops = 0, clears = 0;
        foreach (var instruction in instructions) {
            if (instruction.Calls(stop)) {
                yield return new CodeInstruction(OpCodes.Ldarg_1);
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(AsyncShipJobs), nameof(AsyncShipJobs.Stop));
                stops++;
            } else if (instruction.Calls(clear)) {
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Ldarg_1);
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(AsyncShipJobs), nameof(AsyncShipJobs.Clear));
                clears++;
            }
            yield return instruction;
        }
        if (stops != 1 || clears != 1) throw new InvalidOperationException("Async ship unload IL changed; cancellation sites were not recognized.");
    }
}
