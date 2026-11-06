using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace Ostrabus.Optimizations;

[HarmonyPatch]
internal static class SavedCoShipLookupPatch
{
    private static readonly ConditionalWeakTable<JsonShip[], HashSet<string>> ShipIdsByArray = new();
    private static readonly Dictionary<Type, FieldInfo> CapturedCoFields = new();
    private static readonly HashSet<Type> TypesWithoutCapturedCo = new();
    private static MethodInfo _optimizedAny;
    private static bool _warnedUnoptimizedPredicate;
    private static long _optimizedLookupCount;
    private static long _fallbackLookupCount;

    private static MethodBase TargetMethod()
    {
        MethodInfo iterator = AccessTools.Method(typeof(CrewSim), "DoLoadGame", new[]
        {
            typeof(string), typeof(string), typeof(Dictionary<string, byte[]>)
        });
        Type stateMachine = iterator?.GetCustomAttribute<IteratorStateMachineAttribute>()?.StateMachineType;
        return stateMachine?.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        _optimizedAny = AccessTools.Method(typeof(SavedCoShipLookupPatch), nameof(AnySavedCoShip));
        int replacements = 0;
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt)
            {
                if (instruction.operand is MethodInfo method && method.Name == "Any" && method.IsGenericMethod &&
                    method.GetGenericArguments()[0] == typeof(JsonShip))
                {
                    instruction.operand = _optimizedAny;
                    replacements++;
                }
            }
            yield return instruction;
        }
        if (replacements == 1) Ostrabus.OstrabusPlugin.Logger?.LogInfo("[Optimizations] Enabled indexed saved-CO ship lookup.");
        else Ostrabus.OstrabusPlugin.Logger?.LogWarning($"[Optimizations] Expected one saved-CO JsonShip.Any call, found {replacements}; lookup optimization is inactive.");
    }

    private static bool AnySavedCoShip(IEnumerable<JsonShip> source, Func<JsonShip, bool> predicate)
    {
        if (source is JsonShip[] ships && TryGetSavedCo(predicate, out JsonCondOwnerSave savedCo) && savedCo.strRegIDLast != null)
        {
            _optimizedLookupCount++;
            return ShipIdsByArray.GetValue(ships, BuildShipIdSet).Contains(savedCo.strRegIDLast);
        }
        _fallbackLookupCount++;
        if (!_warnedUnoptimizedPredicate)
        {
            _warnedUnoptimizedPredicate = true;
            Ostrabus.OstrabusPlugin.Logger?.LogWarning("[Optimizations] Saved-CO lookup predicate did not match the expected compiler closure; using the original scan.");
        }
        return source.Any(predicate);
    }

    private static HashSet<string> BuildShipIdSet(JsonShip[] ships)
    {
        var ids = new HashSet<string>(ships.Length, StringComparer.Ordinal);
        for (int i = 0; i < ships.Length; i++) if (ships[i]?.strRegID != null) ids.Add(ships[i].strRegID);
        return ids;
    }

    private static bool TryGetSavedCo(Func<JsonShip, bool> predicate, out JsonCondOwnerSave savedCo)
    {
        savedCo = null;
        object target = predicate?.Target;
        if (target == null) return false;
        Type closureType = target.GetType();
        FieldInfo field = null;
        if (CapturedCoFields.TryGetValue(closureType, out FieldInfo cached)) field = cached;
        else if (!TypesWithoutCapturedCo.Contains(closureType))
        {
            FieldInfo[] fields = closureType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < fields.Length; i++)
            {
                if (typeof(JsonCondOwnerSave).IsAssignableFrom(fields[i].FieldType))
                {
                    field = fields[i];
                    CapturedCoFields[closureType] = field;
                    break;
                }
            }
            if (field == null) TypesWithoutCapturedCo.Add(closureType);
        }
        savedCo = field?.GetValue(target) as JsonCondOwnerSave;
        return savedCo != null;
    }

    [HarmonyPostfix]
    private static void MoveNextPostfix(bool __result)
    {
        if (__result || _optimizedLookupCount == 0 && _fallbackLookupCount == 0) return;
        LoadProfiling.Count("indexedCoShipLookups", _optimizedLookupCount);
        LoadProfiling.Count("fallbackCoShipLookups", _fallbackLookupCount);
        Ostrabus.OstrabusPlugin.Logger?.LogInfo($"[Optimizations] Saved-CO ship lookup complete: {_optimizedLookupCount} indexed, {_fallbackLookupCount} fallback.");
        _optimizedLookupCount = 0;
        _fallbackLookupCount = 0;
        _warnedUnoptimizedPredicate = false;
    }
}
