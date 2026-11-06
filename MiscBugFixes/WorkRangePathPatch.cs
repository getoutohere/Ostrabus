using HarmonyLib;
using UnityEngine;

namespace Ostrabus.MiscBugFixes;

// A successful in-range probe means the worker can act from the origin.
// Vanilla instead appends the candidate (which can be the work target's wall).
[HarmonyPatch(typeof(Pathfinder), "GetInRangeResult")]
internal static class WorkRangePathPatch {
    static void Postfix(Pathfinder __instance, Tile origin, Tile candidate, ref PathResult __result) {
        if (__result == null || origin == null || candidate == null || origin == candidate ||
            __instance.fRangeGoal <= 0 || __instance.coUs == null || __instance.coDest == null || __instance.coDest.ship == null) return;
        // Candidates are standing positions near the requested target. Being near
        // a candidate alone does not prove that the work target is within reach.
        Tile target = __instance.tilDest;
        PathResult accepted = __result;
        __result = null;
        if (target == null || origin.tf == null || target.tf == null || !origin.bPassable || origin.IsWall || origin.IsFixture ||
            origin.IsBurningHazard(__instance.coUs) ||
            Vector2.Distance(origin.tf.position, target.tf.position) > __instance.fRangeGoal ||
            !Visibility.IsCondOwnerLOSVisibleBlocks(__instance.coDest, origin.tf.position, target.IsWall)) return;
        // Returning null above lets the native caller perform its normal route
        // search. A one-tile result below prevents CheckWalkExecute adding Walk.
        accepted.Tiles.Clear();
        accepted.AddTile(origin);
        accepted.Dest = origin;
        accepted.Disembark = false;
        accepted.PathLength = -1;
        __result = accepted;
    }
}
