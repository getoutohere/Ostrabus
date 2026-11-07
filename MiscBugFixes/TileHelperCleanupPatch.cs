using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Ostranauts.Core.Models;
using UnityEngine;

namespace Ostrabus.MiscBugFixes;

internal static class TileHelperCleanup {
    static bool prepared;
    static readonly AccessTools.FieldRef<Ship, DictionaryArray<string, CondOwner>> Items = AccessTools.FieldRefAccess<Ship, DictionaryArray<string, CondOwner>>("mapICOs");
    internal static void Prepare() {
        if (prepared) return;
        Harmony.ReversePatch(AccessTools.Method(typeof(CondOwner), nameof(CondOwner.Destroy)),
            new HarmonyMethod(AccessTools.Method(typeof(TileHelperCleanup), nameof(DestroyManaged))),
            AccessTools.Method(typeof(TileHelperCleanup), nameof(KeepNativeObject)), null);
        prepared = true;
    }
    // Reuse the native lifecycle, including scheduler/index/condition cleanup.
    // Only the final physical GameObject deletion is left to the native caller.
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void DestroyManaged(CondOwner co) => throw new InvalidOperationException("Tile helper cleanup reverse patch was not installed.");
    static IEnumerable<CodeInstruction> KeepNativeObject(IEnumerable<CodeInstruction> instructions) {
        var code = instructions.ToList();
        var destroy = AccessTools.Method(typeof(UnityEngine.Object), nameof(UnityEngine.Object.DestroyImmediate), new[] { typeof(UnityEngine.Object) });
        int changed = 0;
        foreach (var instruction in code) {
            if (!instruction.Calls(destroy)) continue;
            instruction.opcode = OpCodes.Pop; instruction.operand = null; changed++;
        }
        if (changed != 1) throw new InvalidOperationException("CondOwner.Destroy physical teardown IL changed; expected one DestroyImmediate call.");
        return code;
    }
    internal static CondOwner Helper(Tile tile, Ship owner = null) {
        if (tile == null || !tile.bShipTile) return null;
        var co = tile.coProps;
        if (co == null || co.bDestroyed || co.strName != "TIL") return null;
        if (owner != null && !ReferenceEquals(co.ship, owner)) return null;
        return co.gameObject == tile.gameObject ? co : null;
    }
    internal static void Clean(CondOwner helper) {
        if (helper != null && !helper.bDestroyed) DestroyManaged(helper);
    }
    internal static bool RemoveOwned(Dictionary<string, CondOwner> registry, string id, Ship owner) {
        if (id == null || owner == null || !registry.TryGetValue(id, out var registered)) return false;
        var items = Items(owner);
        return items != null && items.TryGetValue(id, out var owned) && !ReferenceEquals(owned, null) &&
            ReferenceEquals(owned.ship, owner) && ReferenceEquals(registered, owned) && registry.Remove(id);
    }
    internal static void DestroyAsyncHierarchy(UnityEngine.Object target, Ship owner) {
        if (owner != null && target != null && target == owner.gameObject && owner.aTiles != null) {
            foreach (var tile in owner.aTiles) {
                var helper = Helper(tile, owner);
                if (helper == null) continue;
                tile.Destroy();
                Clean(helper); // Idempotent if Tile.Destroy's postfix already cleaned it.
            }
        }
        UnityEngine.Object.DestroyImmediate(target);
    }
}

[HarmonyPatch(typeof(Tile), nameof(Tile.Destroy))]
internal static class TileHelperCleanupPatch {
    static void Prepare() { TileHelperCleanup.Prepare(); }
    static void Prefix(Tile __instance, out CondOwner __state) { __state = TileHelperCleanup.Helper(__instance); }
    static void Postfix(CondOwner __state) { TileHelperCleanup.Clean(__state); }
}
