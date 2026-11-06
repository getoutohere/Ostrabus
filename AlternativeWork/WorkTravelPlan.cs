using System;
using System.Collections.Generic;
using Ostranauts.Pathing;
using UnityEngine;

namespace Ostrabus.AlternativeWork;

internal sealed class WorkTravelPlan : IRouteGraph<Tile> {
    readonly IncrementalRoutes<Tile> routes = new IncrementalRoutes<Tile>();
    readonly Dictionary<Tile, bool> walkable = new Dictionary<Tile, bool>();
    Tile[] surrounding = new Tile[8];
    readonly bool[] allowed = new bool[8];
    CondOwner worker;
    Ship ship;
    Tile origin;
    PathResult permissionProbe;
    int revision;
    bool shoreLeave, airlocks;
    internal string WorkerId { get; }
    internal string SuggestedTarget;
    internal int Nodes => routes.Count;
    internal bool Complete => routes.Complete;
    internal bool Capped => routes.Capped;
    internal WorkTravelPlan(string id) { WorkerId = id; }

    internal bool Refresh(CondOwner co) {
        if (co == null || co.ship == null || co.tf == null) { Clear(); return false; }
        Tile start = co.ship.GetTileAtWorldCoords1(co.tf.position.x, co.tf.position.y, bAllowDocked: true);
        if (start == null) { Clear(); return false; }
        bool leave = co.HasShoreLeave(), air = co.HasAirlockPermission(false);
        if (worker == co && ship == co.ship && origin == start && revision == NavigationSnapshot.Revision && shoreLeave == leave && airlocks == air) return true;
        Clear();
        worker = co; ship = co.ship; origin = start;
        revision = NavigationSnapshot.Revision; shoreLeave = leave; airlocks = air;
        permissionProbe = new PathResult(air);
        routes.Reset(start);
        return true;
    }
    internal void Step() { routes.Step(this); }
    internal void Clear() {
        routes.Clear(); walkable.Clear(); Array.Clear(surrounding, 0, surrounding.Length);
        worker = null; ship = null; origin = null; permissionProbe = null; SuggestedTarget = null;
    }
    bool Walkable(Tile tile) {
        if (tile == null || tile.coProps == null || tile.coProps.ship == null) return false;
        if (!shoreLeave && tile.coProps.ship != ship) return false;
        if (walkable.TryGetValue(tile, out bool value)) return value;
        value = tile.IsWalkable(worker, permissionProbe);
        walkable[tile] = value;
        return value;
    }
    public int Neighbors(Tile from, Tile[] nodes, float[] costs) {
        Array.Clear(surrounding, 0, surrounding.Length);
        TileUtils.GetSurroundingTiles(ref surrounding, from);
        for (int i = 0; i < 8; i++) allowed[i] = Walkable(surrounding[i]);
        int count = 0;
        for (int i = 0; i < 8; i++) {
            if (!allowed[i]) continue;
            // Same diagonal rule as vanilla JPS: at least one adjoining cardinal must be open.
            if (i == 4 && !allowed[0] && !allowed[1] || i == 5 && !allowed[1] && !allowed[2] ||
                i == 6 && !allowed[0] && !allowed[3] || i == 7 && !allowed[2] && !allowed[3]) continue;
            Tile tile = surrounding[i];
            float cost = i < 4 ? 1 : 1.41421356f;
            // Travel preferences only; the game's final route and work checks remain authoritative.
            if (tile.coProps.HasCond("IsEVATile", isThreshold: false)) cost *= 3;
            if (tile.IsPortal && tile.IsWall) cost += 4;
            nodes[count] = tile; costs[count++] = cost;
        }
        return count;
    }
    internal float Cost(CondOwner target, JsonInteraction interaction) {
        if (target == null || target.ship == null || target.tf == null || worker == null) return float.PositiveInfinity;
        if (interaction != null && interaction.strTargetPoint == Interaction.POINT_REMOTE) return 0;
        Vector2 point = interaction == null || string.IsNullOrEmpty(interaction.strTargetPoint) ? (Vector2)target.tf.position : target.GetPos(interaction.strTargetPoint);
        float range = interaction == null ? 1 : Math.Max(0, interaction.fTargetPointRange);
        if (range > 8) return Fallback(point);
        float best = float.PositiveInfinity;
        int radius = (int)Math.Ceiling(range) + 1;
        for (int x = -radius; x <= radius; x++) for (int y = -radius; y <= radius; y++) {
            Tile tile = ship.GetTileAtWorldCoords1(point.x + x, point.y + y, bAllowDocked: true);
            if (tile == null || !routes.TryCost(tile, out float cost) || cost >= best) continue;
            if (Vector2.Distance(tile.tf.position, point) > range + 0.01f) continue;
            bool aligned = target.tf.position.x == tile.tf.position.x || target.tf.position.y == tile.tf.position.y;
            if (!Visibility.IsCondOwnerLOSVisibleBlocks(target, tile.tf.position, bIgnoreEndpoints: !aligned, bIgnoreGlass: false)) continue;
            best = cost;
        }
        // Unknown routes remain candidates: vanilla can prepare a suit or find a different use point.
        return float.IsPositiveInfinity(best) ? Fallback(point) : best;
    }
    float Fallback(Vector2 point) { return 100000 + Vector2.Distance(worker.tf.position, point); }
}
