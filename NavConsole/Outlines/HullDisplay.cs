using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Ostranauts.ShipGUIs.NavStation;
using Ostranauts.ShipGUIs.Utilities;
using UnityEngine;
using UnityEngine.SceneManagement;
using Vectrosity;

namespace Ostrabus.NavConsole.Outlines;

internal static class HullDisplay {
    sealed class Source {
        internal readonly HullWorker.Entry Entry = worker.Create();
        internal Ship.Loaded Loaded;
        internal int Rotation, Expected = 256, InputPoints, Missing, Records;
        internal List<Vector2> Floor;
        internal bool Initialized;
        internal double NextCapture;
        internal string ReportedError;
    }
    sealed class Applied {
        internal HullWorker.Entry Entry;
        internal long Version = -1;
        internal int Rotation;
        internal float Radius;
    }
    static readonly HullWorker worker = new HullWorker();
    static ConditionalWeakTable<Ship, Source> sources = new ConditionalWeakTable<Ship, Source>();
    static ConditionalWeakTable<ShipDraw, Applied> applied = new ConditionalWeakTable<ShipDraw, Applied>();
    static readonly AccessTools.FieldRef<GUIOrbitDraw, List<ShipDraw>> Draws = AccessTools.FieldRefAccess<GUIOrbitDraw, List<ShipDraw>>("aShipDraws");
    static HullSnapshot capture;
    static int lastStepFrame = -1;
    static bool stopped;
    static Source Create(Ship _) => new Source();
    static Applied CreateApplied(ShipDraw _) => new Applied();
    static void Observe(Ship ship, Source source) {
        if (source.Initialized && (source.Loaded != ship.LoadState || source.Rotation != ship.nGridRotation || !ReferenceEquals(source.Floor, ship.FloorPlan))) {
            worker.Dirty(source.Entry); source.NextCapture = 0;
        }
        source.Initialized = true; source.Loaded = ship.LoadState; source.Rotation = ship.nGridRotation; source.Floor = ship.FloorPlan;
    }
    // Mutation hook only marks existing caches. Strict interior removals cannot
    // change the filled exterior and need neither a revision nor a new snapshot.
    internal static void Changed(Ship ship, CondOwner co, bool removing) {
        if (stopped || ship == null || !sources.TryGetValue(ship, out var source) || !HullSnapshot.Relevant(co) || (!removing && co.GetCondAmount("IsInstalled") <= 0)) return;
        if (source.Loaded >= Ship.Loaded.Edit && source.Loaded == ship.LoadState && source.Rotation == ship.nGridRotation && worker.Read(source.Entry, out var envelope, out _, out bool clean, out _) && clean && co.tf != null) {
            var item = co.Item;
            if (item != null && item.nWidthInTiles > 0 && item.nHeightInTiles > 0) {
                var center = co.tf.position - ship.gameObject.transform.position;
                // UpdateTiles can snap an added item by half a tile. Enlarge
                // its test box conservatively; removals use settled coordinates.
                // Also cover the worker's small float-to-grid recovery tolerance.
                double margin = removing ? .01 : .51;
                double x = item.nWidthInTiles * .5 + margin, y = item.nHeightInTiles * .5 + margin;
                if (envelope.ContainsBox(center.x-x,center.y-y,center.x+x,center.y+y,removing)) return;
            }
        }
        worker.Dirty(source.Entry);
    }
    internal static void Retire(Ship ship) {
        if (ship == null || !sources.TryGetValue(ship, out var source)) return;
        worker.Retire(source.Entry); sources.Remove(ship);
        if (capture != null && ReferenceEquals(capture.Ticket.Entry, source.Entry)) CloseCapture();
    }
    static void CloseCapture() { capture?.Dispose(); capture = null; }
    internal static void NavClosed() => CloseCapture();
    internal static void Pump() => AdvanceCapture();
    static void AdvanceCapture() {
        if (capture == null || lastStepFrame == Time.frameCount) return;
        lastStepFrame = Time.frameCount;
        if (!capture.Owner.TryGetTarget(out var ship) || ship == null || ship.bDestroyed || !sources.TryGetValue(ship, out var source)) { CloseCapture(); return; }
        Observe(ship, source);
        if (!worker.Valid(capture.Ticket)) { CloseCapture(); return; }
        try {
            if (!capture.Step(ship)) return;
            source.InputPoints = capture.Count; source.Expected = Math.Max(256, capture.Count); source.Missing = capture.MissingDefinitions; source.Records = capture.Records;
            if (source.Missing != 0) throw new InvalidOperationException("Unresolved part definitions in the hull snapshot: " + source.Missing + ".");
            capture.Submit(); capture.Dispose(); capture = null;
        } catch (SnapshotExpiredException) {
            source.NextCapture = 0; CloseCapture();
        } catch (Exception error) {
            source.NextCapture = Time.unscaledTime + 2;
            Report(source, error.Message); CloseCapture();
        }
    }
    static void Report(Source source, string error) {
        if (error == null) { source.ReportedError = null; return; }
        if (source.ReportedError == error) return;
        source.ReportedError = error;
        OstrabusPlugin.Logger?.LogWarning("[HullOutlines] Retaining the previous outline: " + error);
    }
    internal static void Display(ShipDraw draw, bool show) {
        if (stopped || !show || draw?.ship == null || draw.ship.bDestroyed || !draw.IsVisible || draw.lineBodySilhouette == null) return;
        var ship = draw.ship; var source = sources.GetValue(ship, Create);
        Request(ship,source,out var result,out long version,out bool clean,out string problem);
        if (result!=null && clean) {
            Report(source, null);
            var stamp = applied.GetValue(draw, CreateApplied);
            float radius = ship.objSS.Size;
            if (ReferenceEquals(stamp.Entry, source.Entry) && stamp.Version == version && stamp.Radius == radius && stamp.Rotation == ship.nGridRotation) return;
            Assign(draw, result, radius, ship.nGridRotation);
            stamp.Entry = source.Entry; stamp.Version = version; stamp.Radius = radius; stamp.Rotation = ship.nGridRotation;
        } else if (problem != null) Report(source, problem);
    }
    internal static bool Geometry(Ship ship,out Envelope result) {
        result=null;
        if(stopped||ship==null||ship.bDestroyed)return false;
        var source=sources.GetValue(ship,Create);
        Request(ship,source,out result,out _,out bool clean,out _);
        return clean&&result?.Collision!=null;
    }
    static void Request(Ship ship,Source source,out Envelope result,out long version,out bool clean,out string problem) {
        Observe(ship, source); AdvanceCapture();
        worker.Read(source.Entry, out result, out version, out clean, out problem);
        if (!clean && capture == null && lastStepFrame != Time.frameCount && Time.unscaledTime >= source.NextCapture && worker.TryReserve(source.Entry, out var ticket)) {
            source.NextCapture = Time.unscaledTime + .5;
            try { capture = new HullSnapshot(ship, worker, ticket, source.Expected); AdvanceCapture(); }
            catch (Exception error) { worker.Cancel(ticket, error.Message); Report(source, error.Message); CloseCapture(); }
        }
    }
    internal static void Assign(ShipDraw draw, Envelope result, float radius, int gridRotation) {
        var line=draw.lineBodySilhouette;
        if(line.lineType!=LineType.Discrete) {
            // Resize must synchronize Vectrosity's cached point count before
            // ResetLine reallocates its mesh arrays. Replacing points2 with an
            // empty list alone leaves the old count and makes ZeroVertices
            // index the newly empty arrays. Do this before editing the shared
            // point list, and before a large perimeter enters continuous mode.
            line.Resize(0);line.lineType=LineType.Discrete;
        }
        var points=draw.silhouetteDrawPoints;
        if(points==null)points=draw.silhouetteDrawPoints=new List<Vector2>(result.Count*2);
        Copy(result,radius,gridRotation,points);
        line.points2=points;
        HullPresentation.Mark(line,result.Count>=3);
    }
    internal static void Copy(Envelope result, float radius, int gridRotation, List<Vector2> points) {
        points.Clear();
        if (result.Count < 3) { points.AddRange(NavIcon.Ship(radius)); return; }
        if(points.Capacity<result.Count*2)points.Capacity=result.Count*2;
        HullSnapshot.Rotation(-gridRotation, out double cos, out double sin); // Same clockwise compensation as NavIcon.
        for(int loop=0;loop<result.Loops;loop++)for(int i=result.Start(loop);i<result.End(loop);i++) {
            var p=result.Unit(i);var next=result.Unit(i+1==result.End(loop)?result.Start(loop):i+1);
            points.Add(new Vector2((float)((p.X*cos-p.Y*sin)*radius),(float)((p.X*sin+p.Y*cos)*radius)));
            points.Add(new Vector2((float)((next.X*cos-next.Y*sin)*radius),(float)((next.X*sin+next.Y*cos)*radius)));
        }
    }
    internal static List<Vector2> Initial(Ship ship, float radius) {
        // The native constructor creates a continuous line. Keep its temporary
        // symbolic marker; Display adopts the cached exterior using discrete
        // edges so disconnected components never acquire phantom connectors.
        return NavIcon.Ship(radius);
    }
    internal static void Loaded() => Reset();
    internal static void SceneUnloaded(Scene _) => Reset();
    internal static void Reset(bool stop = false) {
        CloseCapture(); worker.Reset(stop); stopped |= stop;
        HullPresentation.Reset();
        global::Ostrabus.ShipNavigation.Collisions.PhysicalCollisions.Reset();
        sources = new ConditionalWeakTable<Ship, Source>(); applied = new ConditionalWeakTable<ShipDraw, Applied>(); lastStepFrame = -1;
    }
    internal static object Inspect() {
        var items = new List<object>(); var nav = GUIOrbitDraw.Instance;
        if (nav != null && Draws(nav) != null) foreach (var draw in Draws(nav)) {
            if (draw?.ship == null || !sources.TryGetValue(draw.ship, out var source)) continue;
            worker.Read(source.Entry, out var result, out long version, out bool clean, out string error);
            items.Add(new Dictionary<string, object> {
                ["shipId"] = draw.ship.strRegID, ["materialized"] = source.Loaded >= Ship.Loaded.Edit,
                ["clean"] = clean, ["version"] = version, ["vertices"] = result?.Count ?? 0,
                ["components"] = result?.Loops ?? 0,
                ["collisionPieces"] = result?.Collision?.Pieces.Length,
                ["collisionGeometryError"] = result?.CollisionError,
                ["renderedPoints"] = draw.lineBodySilhouette?.points2?.Count,
                ["lineMode"] = draw.lineBodySilhouette?.lineType.ToString(),
                ["outlineSelected"] = ReferenceEquals(draw.lineBody,draw.lineBodySilhouette),
                ["detailedScale"] = HullPresentation.Detailed(draw.lineBodySilhouette),
                ["appliedVersion"] = applied.TryGetValue(draw,out var stamp) ? (object)stamp.Version : null,
                ["records"] = source.Records, ["inputPoints"] = source.InputPoints, ["missingDefinitions"] = source.Missing, ["error"] = error ?? source.ReportedError
            });
            if (items.Count == 16) break;
        }
        return new Dictionary<string, object> {
            ["worker"] = worker.Inspect(), ["captureRecordsDone"] = capture?.Progress, ["captureRecordsTotal"] = capture?.Records, ["ships"] = items,
            ["outlineEnterMapPixels"] = HullZoom.EnterPixels, ["outlineLeaveMapPixels"] = HullZoom.LeavePixels,
            ["physicalCollisions"] = global::Ostrabus.ShipNavigation.Collisions.PhysicalCollisions.Inspect(),
            ["note"] = "Unpadded exterior perimeters shared with optional hull collisions. Native radii, weapon hit tests and sensor visibility are unchanged."
        };
    }
}
