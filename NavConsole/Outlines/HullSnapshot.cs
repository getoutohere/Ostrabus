using System;
using System.Collections.Generic;
using System.Diagnostics;
using HarmonyLib;
using Ostranauts.Core.Models;
using Ostranauts.Trading;
using UnityEngine;

namespace Ostrabus.NavConsole.Outlines;

// Entirely main-thread-owned. Native references are released on completion,
// cancellation, nav close, ship retirement and scene/save replacement.
internal sealed class HullSnapshot : IDisposable {
    static readonly AccessTools.FieldRef<Ship, DictionaryArray<string, CondOwner>> Items = AccessTools.FieldRefAccess<Ship, DictionaryArray<string, CondOwner>>("mapICOs");
    internal readonly WeakReference<Ship> Owner;
    internal readonly HullWorker.Ticket Ticket;
    readonly HullWorker worker;
    WeakReference<CondOwner[]> live;
    JsonItem[] saved;
    List<Loot> liveSockets;
    string[] savedSockets;
    HullPoint[] points;
    int record, socket, columns, socketCount;
    readonly int records;
    double x, y, cos, sin;
    internal int Count { get; private set; }
    internal int MissingDefinitions { get; private set; }
    internal int Progress => record;
    internal int Records => records;
    internal HullSnapshot(Ship ship, HullWorker worker, HullWorker.Ticket ticket, int expected) {
        Owner = new WeakReference<Ship>(ship); this.worker = worker; Ticket = ticket;
        if (ship.LoadState >= Ship.Loaded.Edit) {
            var items = Items(ship);
            CondOwner[] array;
            if (items != null && items.Count > 0) { _ = items[0]; array = items._array; }
            else array = Array.Empty<CondOwner>();
            live = new WeakReference<CondOwner[]>(array); records = array.Length;
        } else { saved = ship.json?.aItems ?? Array.Empty<JsonItem>(); records = saved.Length; }
        points = worker.Rent(Math.Min(HullWorker.MaxPoints, Math.Max(256, expected)));
    }
    internal static bool Relevant(CondOwner co) => co != null && !co.bDestroyed && !co.HasCond("IsPlaceholder") &&
        (co.HasCond("IsCategoryHull") || co.HasCond("IsFloorGrate") || co.HasCond("IsWall") ||
         co.HasCond("IsDockSys") || co.HasCond("IsHeavyLiftRotor") || Railgun(co.strCODef));
    internal static bool Relevant(DataCO data) => data != null && !data.HasCond("IsPlaceholder") &&
        (data.HasCond("IsCategoryHull") || data.HasCond("IsFloorGrate") || data.HasCond("IsWall") ||
         data.HasCond("IsDockSys") || data.HasCond("IsHeavyLiftRotor") || Railgun(data.Name));
    static bool Railgun(string name) => name != null && name.IndexOf("MassThrower", StringComparison.Ordinal) >= 0;
    bool BeginRecord(Ship ship, CondOwner[] liveRecords) {
        if (live != null) {
            var co = liveRecords[record++];
            if (!Relevant(co) || co.GetCondAmount("IsInstalled") <= 0 || co.objCOParent != null || co.coStackHead != null || co.tf == null) return false;
            var item = co.Item;
            if (item == null || item.aSocketAdds == null || item.nWidthInTiles <= 0) return false;
            // This is the native, already-rotated socket grid used by UpdateTiles.
            liveSockets = item.aSocketAdds; savedSockets = null; columns = item.nWidthInTiles;
            socketCount = liveSockets.Count;
            var position = co.tf.position - ship.gameObject.transform.position;
            x = position.x; y = position.y; cos = 1; sin = 0;
        } else {
            var item = saved[record++];
            if (item == null || !string.IsNullOrEmpty(item.strParentID) || !string.IsNullOrEmpty(item.strSlotParentID)) return false;
            var data = DataHandler.GetDataCO(item.strName);
            if (data == null) { MissingDefinitions++; return false; }
            if (!Relevant(data) || (item.GetCondAmountOverride("IsInstalled") ?? data.GetCondAmount("IsInstalled")) <= 0) return false;
            var definition = data.JItmDef;
            if (definition == null || definition.nCols <= 0 || definition.aSocketAdds == null) return false;
            savedSockets = definition.aSocketAdds; liveSockets = null; columns = definition.nCols;
            socketCount = savedSockets.Length;
            x = item.fX; y = item.fY;
            Rotation(item.fRotation, out cos, out sin);
        }
        if (socketCount % columns != 0) throw new InvalidOperationException("Hull socket grid is not rectangular.");
        socket = 0;
        return socketCount > 0;
    }
    internal static void Rotation(double degrees, out double cos, out double sin) {
        double turn = degrees % 360;
        if (turn < 0) turn += 360;
        if (Math.Abs(turn - Math.Round(turn / 90) * 90) < .00001) {
            int quarter = (int)Math.Round(turn / 90) % 4;
            cos = quarter == 0 ? 1 : quarter == 2 ? -1 : 0;
            sin = quarter == 1 ? 1 : quarter == 3 ? -1 : 0;
        } else { double radians = turn * Math.PI / 180; cos = Math.Cos(radians); sin = Math.Sin(radians); }
    }
    void Add(double px, double py) {
        if (Count == HullWorker.MaxPoints) throw new InvalidOperationException("Hull outline exceeds the bounded snapshot capacity.");
        if (Count == points.Length) {
            var expanded = worker.Rent(Math.Min(HullWorker.MaxPoints, points.Length * 2));
            Array.Copy(points, expanded, Count); worker.Return(points); points = expanded;
        }
        points[Count++] = new HullPoint(x + px * cos - py * sin, y + px * sin + py * cos);
    }
    // At most one call per Unity frame. Budget covers records AND large socket grids.
    internal bool Step(Ship ship, double budgetMs = .75) {
        CondOwner[] liveRecords = null;
        if (live != null && !live.TryGetTarget(out liveRecords)) throw new SnapshotExpiredException();
        long start = Stopwatch.GetTimestamp(); int operations = 0;
        while (true) {
            if ((operations++ & 31) == 0 && (operations > 2048 || (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency >= budgetMs)) return false;
            if (socket >= socketCount) {
                liveSockets = null; savedSockets = null; socketCount = 0;
                if (record >= Records) return true;
                if (!BeginRecord(ship, liveRecords)) continue;
            }
            int index = socket++;
            bool occupied = liveSockets != null ? liveSockets[index] != null && liveSockets[index].strName != Loot.NAME_BLANK :
                savedSockets[index] != null && savedSockets[index] != Loot.NAME_BLANK;
            if (!occupied) continue;
            double px = index % columns - (columns - 1) * .5, py = (socketCount / columns - 1) * .5 - index / columns;
            // Keep socket quads together for the worker's exact exterior trace.
            Add(px - .5, py - .5); Add(px + .5, py - .5); Add(px + .5, py + .5); Add(px - .5, py + .5);
        }
    }
    internal void Submit() {
        var buffer = points; points = null;
        worker.Submit(Ticket, buffer, Count);
        ReleaseReferences();
    }
    void ReleaseReferences() { live = null; saved = null; liveSockets = null; savedSockets = null; Owner.SetTarget(null); }
    public void Dispose() {
        if (points != null) { worker.Cancel(Ticket); worker.Return(points); points = null; }
        ReleaseReferences();
    }
}

// A replaced native dictionary cache was collected while the UI was inactive.
// Recollect next frame; this is normal cancellation, not a logged failure.
internal sealed class SnapshotExpiredException : Exception { }
