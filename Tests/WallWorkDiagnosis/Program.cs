using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using HarmonyLib;
using UnityEngine;

static class Program {
    static readonly Dictionary<Transform, Vector3> positions = new Dictionary<Transform, Vector3>();
    static Tile wall;
    static bool visible = true;
    static bool burning;
    static int checks;
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Field(object obj, string name, object value) => AccessTools.Field(obj.GetType(), name).SetValue(obj, value);
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = ReferenceEquals(__0, __1); return false; }
    static bool Unequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = !ReferenceEquals(__0, __1); return false; }
    static bool Position(Transform __instance, ref Vector3 __result) { __result = positions[__instance]; return false; }
    static bool Lookup(ref Tile __result) { __result = wall; return false; }
    static bool LOS(ref bool __result) { __result = visible; return false; }
    static bool ShaderID(ref int __result) { __result = 1; return false; }
    static bool SafeTile(ref bool __result) { __result = burning; return false; }
    static bool Quiet() => false;
    static PathResult planned;
    static bool Path(ref PathResult __result) { __result = planned; return false; }
    static Tile TileAt(float x, float y, bool isWall) {
        var tile = Empty<Tile>(); tile.tf = Empty<Transform>(); positions[tile.tf] = new Vector3(x, y);
        Field(tile, "<IsWall>k__BackingField", isWall); tile.bPassable = !isWall;
        return tile;
    }
    static void Check(bool ok, string reason) { checks++; if (!ok) throw new Exception(reason); }
    static int Main() {
        var h = new Harmony("offline.wallwork.diagnosis");
        h.Patch(AccessTools.Method(typeof(Shader), "PropertyToID"), prefix: new HarmonyMethod(typeof(Program), nameof(ShaderID)));
        h.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Equality"), prefix: new HarmonyMethod(typeof(Program), nameof(Equal)));
        h.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Inequality"), prefix: new HarmonyMethod(typeof(Program), nameof(Unequal)));
        h.Patch(AccessTools.PropertyGetter(typeof(Transform), "position"), prefix: new HarmonyMethod(typeof(Program), nameof(Position)));
        h.Patch(AccessTools.Method(typeof(Ship), "GetTileAtWorldCoords1"), prefix: new HarmonyMethod(typeof(Program), nameof(Lookup)));
        h.Patch(AccessTools.Method(typeof(Visibility), "IsCondOwnerLOSVisibleBlocks"), prefix: new HarmonyMethod(typeof(Program), nameof(LOS)));
        var pf = Empty<Pathfinder>();
        var origin = TileAt(-87, 15, false); wall = TileAt(-87, 16, true);
        var target = Empty<CondOwner>(); Field(target, "_objShip", Empty<Ship>());
        pf.coDest = target; pf.fRangeGoal = 2.5f;
        var method = AccessTools.Method(typeof(Pathfinder), "GetInRangeResult");
        PathResult Probe() => (PathResult)method.Invoke(pf, new object[] { origin, wall });
        var result = Probe();
        Check(result != null && result.HasPath, "Native shortcut did not accept wall candidate");
        Check(result.Tiles.Count == 2 && ReferenceEquals(result.Tiles[1], wall), "Native shortcut did not end on obstruction");
        Check(!result.Tiles[1].bPassable && result.Tiles[1].IsWall, "Fixture endpoint was not a solid wall");
        pf.fRangeGoal = 0.5f;
        Check(Probe() == null, "Out-of-range control should reject candidate");
        pf.fRangeGoal = 2.5f; visible = false;
        Check(Probe() == null, "Blocked-LOS control should reject candidate");
        h.Patch(AccessTools.Method(typeof(Tile), "IsBurningHazard"), prefix: new HarmonyMethod(typeof(Program), nameof(SafeTile)));
        h.CreateClassProcessor(typeof(Ostrabus.MiscBugFixes.WorkRangePathPatch)).Patch();
        var crew = Empty<CondOwner>(); Field(crew, "_objShip", target.ship); pf.coUs = crew; pf.tilCurrent = origin; pf.tilDest = wall;
        visible = true;
        result = Probe();
        Check(result != null && result.HasPath && result.Tiles.Count == 1 && ReferenceEquals(result.Tiles[0], origin), "Adjacent worker did not stay on floor");
        Check(ReferenceEquals(result.Origin, origin) && ReferenceEquals(result.Dest, origin), "Result metadata retained wall destination");
        Check(ReferenceEquals(pf.coDest, target) && ReferenceEquals(pf.tilDest, wall), "Probe changed the work target");
        visible = false;
        Check(Probe() == null, "Correction bypassed blocked LOS");
        visible = true; pf.fRangeGoal = 0.5f;
        Check(Probe() == null, "Correction bypassed work range");
        pf.fRangeGoal = 2.5f;
        var requested = TileAt(-87, 19, false); pf.tilDest = requested;
        Check(Probe() == null, "Near candidate incorrectly proved far work target reachable");
        pf.tilDest = wall;
        origin.bPassable = false;
        Check(Probe() == null, "Worker stayed on an obstructed origin");
        origin.bPassable = true;
        Field(origin, "<IsFixture>k__BackingField", true);
        Check(Probe() == null, "Worker stayed on a fixture");
        Field(origin, "<IsFixture>k__BackingField", false);
        burning = true;
        Check(Probe() == null, "Worker stayed on a burning hazard");
        burning = false;
        pf.coDest = null;
        Check(Probe().Tiles.Count == 2, "Correction changed direct movement without an interaction target");
        pf.coDest = target;
        // A walkable standing candidate is also not a reason to move when the
        // actual work target can already be reached from the current floor.
        var standing = TileAt(-86, 15, false);
        result = (PathResult)method.Invoke(pf, new object[] { origin, standing });
        Check(result.Tiles.Count == 1 && ReferenceEquals(result.Dest, origin), "Walkable candidate caused unnecessary movement");
        pf.fRangeGoal = 0;
        Check(Probe() == null, "Zero-range exact movement was relaxed");
        pf.fRangeGoal = 2.5f; pf.tilDest = null;
        Check(Probe() == null, "Missing requested target was treated as reachable");
        pf.tilDest = wall;
        // Execute native SetGoal2 with only UI and route generation substituted.
        // Its one-tile branch must adopt the origin as tilDest, preventing Walk.
        planned = Probe();
        Field(wall, "_coProps", target);
        Field(pf, "currentPath", new List<Tile>());
        h.Patch(AccessTools.Method(typeof(Pathfinder), "ShowFootprints"), prefix: new HarmonyMethod(typeof(Program), nameof(Quiet)));
        h.Patch(AccessTools.Method(typeof(Pathfinder), "GetPath"), prefix: new HarmonyMethod(typeof(Program), nameof(Path)));
        result = pf.SetGoal2(wall, 2.5f, target, -87, 16);
        Check(result.HasPath && ReferenceEquals(pf.tilDest, origin), "Native SetGoal2 retained the wall as walking destination");
        Check(pf.currentPath.Count == 1 && ReferenceEquals(pf.currentPath[0], origin), "Native SetGoal2 did not consume stationary result");
        Check(!result.Disembark && result.PathLength == 1, "Stationary result retained cross-ship or path-length metadata");
        Check(ReferenceEquals(pf.coDest, target), "Native route adoption lost delivery target");
        // The same shortcut serves synchronous and asynchronous route generation.
        Check(Harmony.GetPatchInfo(method).Postfixes.Count == 1, "Shortcut hook was not installed exactly once");
        Console.WriteLine(checks + " native wall-work diagnosis/correction checks passed.");
        return 0;
    }
}
