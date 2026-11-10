using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using LitJson;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Newtonsoft.Json.Linq;
using Ostrabus.Development;
using Ostranauts.ShipGUIs.NavStation;
using Ostranauts.ShipGUIs.Utilities;
using UnityEngine;

static class Program {
    static int checks;
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Check(bool ok, string reason) { checks++; if (!ok) throw new Exception(reason); }
    static List<Vector2> Rectangle(int x, int y, int width, int height) {
        var points = new List<Vector2>();
        for (int ix = 0; ix < width; ix++) for (int iy = 0; iy < height; iy++) points.Add(new Vector2(x + ix, y + iy));
        return points;
    }
    static string Bounds(List<Vector2> points) => points == null || points.Count == 0 ? "empty" :
        $"[{points.Min(p => p.x):F2},{points.Max(p => p.x):F2}] x [{points.Min(p => p.y):F2},{points.Max(p => p.y):F2}]";
    static float SpanX(List<Vector2> points) => points.Max(p => p.x) - points.Min(p => p.x);
    static float SpanY(List<Vector2> points) => points.Max(p => p.y) - points.Min(p => p.y);
    static void Reproductions() {
        var hull = Rectangle(0, 0, 20, 10);
        var items = new List<JsonItem> { new JsonItem { strName = "ItmDockSys02Closed", fX = -10, fY = 0 } };
        items.AddRange(hull.Select(p => new JsonItem { strName = "ItmFloor01", fX = p.x, fY = p.y }));
        var plan = SilhouetteUtility.GetFloorVectors(items.ToArray());
        var first = SilhouetteUtility.GenerateVectorPoints(plan);
        var reordered = SilhouetteUtility.GenerateVectorPoints(plan.OrderByDescending(p => p.x).ThenBy(p => p.y).ToList());
        Console.WriteLine($"Detached port: native seed={first.Count} points {Bounds(first)}; same cells hull-first={reordered.Count} points {Bounds(reordered)}.");
        Check(SpanX(first) < SpanX(reordered) / 2, "Docking-port seed no longer hides most of the hull; review native update");

        var ring = Rectangle(0, 0, 20, 10).Where(p => p.x != 10 || p.y != 5).ToList();
        var outer = SilhouetteUtility.GenerateVectorPoints(ring);
        var inner = SilhouetteUtility.GenerateVectorPoints(ring.OrderBy(p => (p - new Vector2(9, 5)).sqrMagnitude).ToList());
        Console.WriteLine($"One-tile hole, connected hull: outer seed={outer.Count} points {Bounds(outer)}; hole seed={inner.Count} points {Bounds(inner)}.");
        Check(SpanX(inner) < SpanX(outer) / 2, "Hole seed no longer hides outer hull; review native update");

        foreach (var shape in new[] { hull, Rectangle(0, 0, 10, 20), Rectangle(0, 0, 2, 20) }) {
            var situ = Empty<ShipSitu>(); situ.SetSize(SilhouetteUtility.GetSilhouetteLength(shape));
            var ship = Empty<Ship>(); ship.FloorPlan = shape;
            var icon = NavIcon.GetSilhouette(ship, situ.Size);
            Console.WriteLine($"Shape {SpanX(shape) + 1}x{SpanY(shape) + 1}: native radius={situ.Size}m; icon {Bounds(icon)}.");
        }
        var a = Empty<ShipSitu>(); var b = Empty<ShipSitu>();
        a.SetSize(SilhouetteUtility.GetSilhouetteLength(hull)); b.SetSize(SilhouetteUtility.GetSilhouetteLength(Rectangle(0, 0, 10, 20)));
        Check(a.Size != b.Size, "Native size now accounts for both axes; review native update");

        var cachedShip = Empty<Ship>(); cachedShip.FloorPlan = new List<Vector2>(hull);
        var before = NavIcon.GetSilhouette(cachedShip, 380); var cache = cachedShip.SilhouettePoints;
        var drawn = new ShipDraw(cachedShip) { silhouetteDrawPoints = before };
        cachedShip.FloorPlan = Rectangle(0, 0, 30, 10);
        var after = NavIcon.GetSilhouette(cachedShip, 380);
        Check(ReferenceEquals(cache, cachedShip.SilhouettePoints) && before.SequenceEqual(after), "Native outline now notices floor-plan replacement; review native update");
        cachedShip.SilhouettePoints = null;
        var regenerated = NavIcon.GetSilhouette(cachedShip, 380);
        Check(!regenerated.SequenceEqual(before), "Invalidating silhouette did not pick up explicitly replaced geometry");
        Check(ReferenceEquals(drawn.silhouetteDrawPoints, before) && !drawn.silhouetteDrawPoints.SequenceEqual(regenerated), "Existing ShipDraw unexpectedly refreshed its independent outline copy");
        Console.WriteLine("Cached silhouette survives floor-plan replacement; clearing it explicitly regenerates from the supplied snapshot.");

        var loose = new JsonItem { strName = "ItmStorageBinFloor1x101", fX = 99, fY = 99 };
        loose.SetCondAmount("IsInstalled", 0);
        var loosePlan = SilhouetteUtility.GetFloorVectors(new[] { loose });
        loose.SetCondAmount("IsInstalled", 1);
        Check(loosePlan.SequenceEqual(SilhouetteUtility.GetFloorVectors(new[] { loose })), "Native geometry now filters installation state; review native update");
        Console.WriteLine("Native floor-vector extraction includes a floor item with IsInstalled=0, identically to IsInstalled=1.");

        var random = new System.Random(6173);
        foreach (var shape in new[] { hull, plan, ring, Rectangle(-40, -20, 3, 8).Concat(Rectangle(40, 20, 5, 3)).ToList() }) {
            var edges = ReferenceEdges.Build(shape);
            for (int i = 0; i < 12; i++) Check(edges.SetEquals(ReferenceEdges.Build(shape.OrderBy(_ => random.Next()).ToList())), "Reference outline depends on input order");
            Check(edges.SetEquals(ReferenceEdges.Build(shape.Concat(shape).ToList())), "Duplicate occupancy changes reference boundaries");
        }
        Check(ReferenceEdges.Build(hull).Count == 120, "Rectangle edge perimeter incorrect");
        Check(ReferenceEdges.Build(ring).Count == 128, "Hole edges missing from reference");
        Check(ReferenceEdges.Build(plan).Count > ReferenceEdges.Build(hull).Count, "Detached port missing from reference");
        Check(ReferenceEdges.Build(Rectangle(0, 0, 1, 1)).Count == 8, "Single floor tile missing from reference");
    }
    static void Metadata() {
        using (var assembly = AssemblyDefinition.ReadAssembly(Path.Combine(InstallationPaths.Managed, "Assembly-CSharp.dll"))) {
            var ship = assembly.MainModule.Types.Single(t => t.Name == "Ship");
            var floor = ship.Fields.Single(f => f.Name == "FloorPlan");
            var silhouette = ship.Fields.Single(f => f.Name == "SilhouettePoints");
            var accesses = ship.Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions.Where(i => i.Operand is FieldReference f && f.FullName == floor.FullName).Select(i => m.Name + ":" + i.OpCode.Code)).Distinct().ToArray();
            Console.WriteLine("Native FloorPlan field access: " + string.Join(", ", accesses));
            foreach (var method in ship.Methods.Where(m => m.HasBody && (m.Name == "AddCO" || m.Name == "RemoveCO" || m.Name == "UpdateTiles"))) {
                Check(!method.Body.Instructions.Any(i => i.Operand is FieldReference f && f.FullName == floor.FullName), method.Name + " now maintains FloorPlan; review native update");
            }
            foreach (string name in new[] { "AddCO", "RemoveCO" }) Check(ship.Methods.Any(m => m.Name == name && m.HasBody && m.Body.Instructions.Any(i => i.OpCode.Code == Code.Stfld && i.Operand is FieldReference f && f.FullName == silhouette.FullName)), name + " no longer invalidates silhouette");
            var gui = assembly.MainModule.Types.Single(t => t.Name == "GUIOrbitDraw");
            var callers = gui.Methods.Where(m => m.HasBody && m.Body.Instructions.Any(i => i.Operand is MethodReference mr && mr.DeclaringType.Name == "NavIcon" && mr.Name == "GetSilhouette")).Select(m => m.Name).ToArray();
            Console.WriteLine("NavIcon.GetSilhouette GUI call sites: " + string.Join(", ", callers));
            Check(callers.Length == 1 && callers[0] == "GetLineBodyForShip", "Native GUI now refreshes silhouettes differently");
            var setup = gui.Methods.Single(m => m.Name == "SetupShipDraw");
            Check(setup.Body.Instructions.Any(i => i.Operand is MethodReference mr && mr.DeclaringType.Name == "VectorLine" && mr.Name == ".ctor"), "Native line allocation lifecycle changed");
        }
    }
    static void Assets() {
        long probeStart = GC.GetAllocatedBytesForCurrentThread();
        var probe = new byte[65536];
        bool allocationCounter = GC.GetAllocatedBytesForCurrentThread() - probeStart >= probe.Length;
        GC.KeepAlive(probe);
        Console.WriteLine("Per-thread allocation counter: " + (allocationCounter ? "available" : "unavailable in this Mono build; allocation totals are not reported"));
        var definitions = JArray.Parse(File.ReadAllText(Path.Combine(InstallationPaths.Data, "items", "items.json")));
        foreach (string name in new[] { "ItmDockSys02Closed", "ItmDockSys02Open" }) {
            var definition = definitions.OfType<JObject>().Single(d => (string)d["strName"] == name);
            int columns = (int)definition["nCols"];
            var sockets = (JArray)definition["aSocketAdds"]; int rows = sockets.Count / columns;
            foreach (int rotation in new[] { 0, 90, 180, 270 }) {
                var expected = new HashSet<Vector2>();
                for (int index = 0; index < sockets.Count; index++) {
                    string socket = (string)sockets[index];
                    if (socket == null || socket == "Blank") continue;
                    var point = new Vector2(index % columns - (columns - 1) * .5f, (rows - 1) * .5f - index / columns);
                    for (int step = 0; step < rotation / 90; step++) point = new Vector2(-point.y, point.x);
                    expected.Add(point);
                }
                var actual = SilhouetteUtility.GetFloorVectors(new[] { new JsonItem { strName = name, fRotation = rotation } });
                Check(expected.SetEquals(actual), "Authored docking-port sockets disagree with native floor extraction: " + name + "/" + rotation);
            }
        }
        Console.WriteLine("Closed/open docking-port asset socket footprints agree with native extraction at all four cardinal rotations.");
        string[] names = { "Light Tug", "Heavy Tug 01", "Argute Rapid Courier", "Babak", "ResAero01", "ResAero02" };
        foreach (string name in names) {
            string path = Path.Combine(InstallationPaths.Data, "ships", name + ".json");
            var json = JArray.Parse(File.ReadAllText(path));
            var items = JsonMapper.ToObject<JsonItem[]>(json[0]["aItems"].ToString());
            var plan = SilhouetteUtility.GetFloorVectors(items);
            var native = SilhouetteUtility.GenerateVectorPoints(plan);
            var reference = ReferenceEdges.Build(plan);
            Check(native != null && reference.Count > 0, "Asset geometry missing: " + name);
            var times = new List<double>(); var nativeTimes = new List<double>(); long allocated = 0;
            for (int i = 0; i < 5; i++) {
                long bytes = GC.GetAllocatedBytesForCurrentThread(); var timer = Stopwatch.StartNew();
                var result = ReferenceEdges.Build(plan); timer.Stop();
                allocated += GC.GetAllocatedBytesForCurrentThread() - bytes; times.Add(timer.Elapsed.TotalMilliseconds);
                Check(reference.SetEquals(result), "Reference changed across calls: " + name);
                timer.Restart(); var nativeResult = SilhouetteUtility.GenerateVectorPoints(plan); timer.Stop();
                nativeTimes.Add(timer.Elapsed.TotalMilliseconds);
                Check(nativeResult.SequenceEqual(native), "Native geometry unexpectedly changed across identical calls: " + name);
            }
            string allocation = allocationCounter ? $", ref allocation={allocated / 5 / 1024d:F1}KiB/build" : "";
            Console.WriteLine($"Asset {name}: {items.Length} items, {plan.Count} floor vectors; native={native.Count} points, ref={reference.Count} edges; median native={nativeTimes.OrderBy(t => t).ElementAt(2):F3}ms, ref={times.OrderBy(t => t).ElementAt(2):F3}ms{allocation} (offline).");
        }
    }
    static int Main() {
        Reproductions(); Metadata(); Assets();
        Console.WriteLine(checks + " native defect reproductions/reference-boundary checks passed. No game patches or saves changed.");
        return 0;
    }
}
