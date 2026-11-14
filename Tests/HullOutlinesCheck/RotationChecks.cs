using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Ostranauts.Core.Models;
using Ostrabus.NavConsole.Outlines;
using Ostranauts.InputControl;
using Ostranauts.ShipGUIs.Utilities;
using UnityEngine;

static partial class Program {
    static Ship rotatingShip;
    static bool CurrentRotatingShip(ref Ship __result) { __result = rotatingShip; return false; }
    static bool NotTyping(ref bool __result) { __result = false; return false; }
    static List<Vector2> RotationFloor() => new List<Vector2> {
        new Vector2(0,0),new Vector2(1,0),new Vector2(1,1),new Vector2(0,1),
        new Vector2(1,0),new Vector2(2,0),new Vector2(2,1),new Vector2(1,1),
        new Vector2(0,1),new Vector2(1,1),new Vector2(1,2),new Vector2(0,2)
    };
    static void PublishFloor(HullWorker worker, HullWorker.Entry entry, List<Vector2> points) {
        Check(worker.TryReserve(entry, out var ticket), "Rotated hull could not reserve a fresh snapshot");
        var buffer = worker.Rent(points.Count);
        for (int i = 0; i < points.Count; i++) buffer[i] = new HullPoint(points[i].x, points[i].y);
        worker.Submit(ticket, buffer, points.Count); WaitReady(worker, entry);
    }
    static HashSet<string> RenderEdges(IEnumerable<Vector2> source) {
        var points = source.ToArray(); var edges = new HashSet<string>();
        string Point(Vector2 p) {
            double x = Math.Round(p.x, 4), y = Math.Round(p.y, 4);
            return (x == 0 ? 0 : x) + "," + (y == 0 ? 0 : y);
        }
        for (int i = 0; i < points.Length; i += 2) {
            string a = Point(points[i]), b = Point(points[i + 1]);
            edges.Add(string.CompareOrdinal(a, b) < 0 ? a + "->" + b : b + "->" + a);
        }
        return edges;
    }
    static void RotationCases(Harmony harmony) {
        // Execute the native input commands and RotateCW body. The empty CO/
        // room fixture isolates its in-place layout/grid changes; tile scene
        // transforms, work-manager bookkeeping, camera and audio are boundaries.
        var fixture = new Harmony("offline.hull.rotation.scene");
        var oldSim = CrewSim.objInstance; var oldPlayer = CrewSim.coPlayer;
        var oldCanvas = CrewSim.CanvasManager; var oldInstance = CanvasManager.instance; var oldAudio = AudioManager.am;
        try {
            HullDisplay.Reset();
            fixture.Patch(AccessTools.PropertyGetter(typeof(CrewSim), nameof(CrewSim.Typing)), prefix: new HarmonyMethod(typeof(Program), nameof(NotTyping)));
            fixture.Patch(AccessTools.PropertyGetter(typeof(CrewSim), nameof(CrewSim.shipCurrentLoaded)), prefix: new HarmonyMethod(typeof(Program), nameof(CurrentRotatingShip)));
            fixture.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.RotateCW)), transpiler: new HarmonyMethod(typeof(Program), nameof(EngineBoundary)));
            fixture.Patch(AccessTools.Method(typeof(WorkManager), nameof(WorkManager.RefreshTileIDs)), prefix: new HarmonyMethod(typeof(Program), nameof(Skip)));
            fixture.Patch(AccessTools.Method(typeof(AudioManager), nameof(AudioManager.PlayAudioEmitter)), prefix: new HarmonyMethod(typeof(Program), nameof(Skip)));
            CrewSim.objInstance = Empty<CrewSim>(); CrewSim.objInstance.workManager = Empty<WorkManager>();
            CrewSim.CanvasManager = CanvasManager.instance = Empty<CanvasManager>(); Field(CanvasManager.instance, "nState", CanvasManager.GUIState.NORMAL);
            AudioManager.am = Empty<AudioManager>(); CrewSim.coPlayer = null;
            var ship = Hull(Ship.Loaded.Full); rotatingShip = ship;
            ship.gameObject = Empty<GameObject>(); var root = Empty<Transform>(); transforms[ship.gameObject] = root; positions[root] = default;
            ship.aTiles = new List<Tile> { null }; ship.aRooms = new List<Room>(); ship.mapZones = new Dictionary<string, JsonZone>();
            ship.nCols = ship.nRows = 1; ship.FloorPlan = RotationFloor(); ship.SilhouettePoints = new List<Vector2>(ship.FloorPlan);
            Field(ship, "mapICOs", new DictionaryArray<string, CondOwner>());
            var situ = new ShipSitu(false); situ.Size = 250; Field(ship, "<objSS>k__BackingField", situ);
            var worker = (HullWorker)AccessTools.Field(typeof(HullDisplay), "worker").GetValue(null);
            var source = AccessTools.Method(typeof(HullDisplay), "Create").Invoke(null, new object[] { ship });
            var entry = (HullWorker.Entry)AccessTools.Field(source.GetType(), "Entry").GetValue(source);
            var sources = AccessTools.Field(typeof(HullDisplay), "sources").GetValue(null);
            sources.GetType().GetMethod("Add").Invoke(sources, new[] { (object)ship, source });
            var line = NativeLine(5);
            var draw = new ShipDraw(ship) { lineBodySilhouette = line, silhouetteDrawPoints = line.points2,
                SignalVisibility = Ostranauts.Ships.Sensors.SignalVisibility.Visible };
            PublishFloor(worker, entry, ship.FloorPlan); HullDisplay.Display(draw, true);
            var originalPoints = draw.silhouetteDrawPoints.ToArray(); var floorIdentity = ship.FloorPlan;
            var floorBefore = ship.FloorPlan.ToArray();
            var clockwise = Empty<CommandRotateCW>(); var counterclockwise = Empty<CommandRotateCCW>();

            // Control: old Observe sees the same load, grid=0 and list reference,
            // despite the native command actually rotating the list's contents.
            clockwise.Execute(default);
            Check(ship.nGridRotation == 0 && ReferenceEquals(floorIdentity, ship.FloorPlan) && !floorBefore.SequenceEqual(ship.FloorPlan), "Native Q/E control failed to reproduce unchanged cache keys");
            HullDisplay.Display(draw, true);
            worker.Read(entry, out _, out _, out bool clean, out _);
            Check(clean && originalPoints.SequenceEqual(draw.silhouetteDrawPoints), "Old rotation-cache failure did not reproduce");
            counterclockwise.Execute(default);
            Check(floorBefore.SequenceEqual(ship.FloorPlan), "Native Q/E opposite turn did not restore the layout");

            harmony.CreateClassProcessor(typeof(HullRotationPatch)).Patch();
            var expected = originalPoints;
            for (int quarter = 0; quarter < 4; quarter++) {
                clockwise.Execute(default);
                worker.Read(entry, out var retained, out _, out clean, out _);
                Check(!clean && retained != null && ship.nGridRotation == 0 && ReferenceEquals(floorIdentity, ship.FloorPlan), "Q/E with a reset grid did not invalidate existing geometry");
                Check((double)AccessTools.Field(source.GetType(), "NextCapture").GetValue(source) == 0, "Rotation retained capture backoff");
                PublishFloor(worker, entry, ship.FloorPlan); HullDisplay.Display(draw, true); SyncLine(line);
                expected = expected.Select(p => new Vector2(p.y, -p.x)).ToArray();
                Check(RenderEdges(expected).SetEquals(RenderEdges(draw.silhouetteDrawPoints)), "Asymmetric outline did not follow native clockwise turn " + quarter);
            }
            Check(RenderEdges(originalPoints).SetEquals(RenderEdges(draw.silhouetteDrawPoints)), "Four native Q/E turns changed the original hull outline");
            counterclockwise.Execute(default); worker.Read(entry, out _, out _, out clean, out _);
            Check(!clean, "Native counterclockwise command did not invalidate geometry");
            PublishFloor(worker, entry, ship.FloorPlan); HullDisplay.Display(draw, true);
            Check(RenderEdges(originalPoints.Select(p => new Vector2(-p.y, p.x))).SetEquals(RenderEdges(draw.silhouetteDrawPoints)), "Native counterclockwise outline orientation is wrong");

            // Docking calls the same native RotateCW without resetting the grid.
            // Its net rotation convention remains the existing native convention.
            ship.RotateCW(); worker.Read(entry, out _, out _, out clean, out _);
            Check(!clean && ship.nGridRotation == 90, "Docking-style grid rotation was not invalidated");
            AccessTools.Method(typeof(HullDisplay), "Observe").Invoke(null, new[] { (object)ship, source });
            PublishFloor(worker, entry, ship.FloorPlan); HullDisplay.Display(draw, true);
            worker.Read(entry, out var geometry, out _, out clean, out _);
            Check(clean && geometry != null, "Rotated docking geometry did not publish");
            worker.Read(entry, out _, out long beforeHeading, out _, out _); assignments = 0;
            for (int i = 0; i < 8; i++) { situ.fRot += .3f; HullDisplay.Display(draw, true); }
            worker.Read(entry, out _, out long afterHeading, out clean, out _);
            Check(clean && beforeHeading == afterHeading && assignments == 0, "Ordinary heading changes rebuilt or reuploaded hull geometry");

            worker.Dirty(entry); Check(worker.TryReserve(entry, out var ticket), "Partial rotation snapshot not reserved");
            var capture = new HullSnapshot(ship, worker, ticket, 256);
            AccessTools.Field(typeof(HullDisplay), "capture").SetValue(null, capture);
            ship.RotateCW();
            Check(AccessTools.Field(typeof(HullDisplay), "capture").GetValue(null) == null && !capture.Owner.TryGetTarget(out _) && !worker.Valid(ticket), "Rotation retained an old-orientation capture or reservation");
            var untouched = Hull(Ship.Loaded.Full);
            HullDisplay.Rotated(untouched);
            Check((bool)sources.GetType().GetMethod("TryGetValue").Invoke(sources, new object[] { untouched, null }) == false, "Rotation allocated a cache for an unobserved hull");
        } finally {
            fixture.UnpatchSelf(); HullDisplay.Reset(); CrewSim.objInstance = oldSim; rotatingShip = null;
            CrewSim.coPlayer = oldPlayer; CrewSim.CanvasManager = oldCanvas; CanvasManager.instance = oldInstance; AudioManager.am = oldAudio;
        }
    }
}
