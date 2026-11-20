using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ostrabus.NavConsole.Outlines;
using Ostrabus.ShipNavigation.Collisions;

static partial class Program {
    // Relative motion is atomic; independently polled absolute poses are not.
    // This diagnostic isolates the last unsteered coast, not the full AI loop.
    static void PursuitReplay(string directory, string save) {
        var bundle = JObject.Parse(File.ReadAllText(Path.Combine(directory, "048.json")));
        var comms = JObject.Parse(File.ReadAllText(Path.Combine(directory, "approach-evasion-comms.json")));
        double warningEpoch = (double)comms["data"]["shipLog"]["items"].First(x => (string)x["interaction"] == "SHIPCorrectingCourse")["availableTime"];
        var target = bundle["target"]; var player = bundle["player"];
        double epoch = (double)target["epoch"], elapsed = warningEpoch - epoch;
        var relative = target["data"]["relativeToSelectedCrew"];
        var p = new HullContact.Vec((double)relative["dxKm"] * 1000, (double)relative["dyKm"] * 1000);
        var v = new HullContact.Vec((double)relative["relativeVxKmPerSecond"] * 1000, (double)relative["relativeVyKmPerSecond"] * 1000);
        Check(elapsed >= 0 && elapsed < 5, "Pursuit fixture is not immediately before the warning");
        var oldWorld = CrewSim.system; var oldFlag = PhysicalCollisions.Flag; byte[] hash = Hash(save);
        Ship Load(string id, JToken capture, out Envelope geometry) {
            var saved = SavedHullPreview.ReadRecord(save, id);
            foreach (string name in saved.aItems.Select(item => item.strName).Distinct()) {
                DataHandler.dictCOOverlays.TryGetValue(name, out var overlay); DataHandler.dictCOs.TryGetValue(name, out var basis);
                AccessTools.Method(typeof(DataHandler), "BuildDataCO").Invoke(null, new object[] { name, overlay, basis });
            }
            var ship = Hull(Ship.Loaded.Shallow, saved.aItems); ship.strRegID = id; ship.json = saved; ship.nGridRotation = saved.nGridRotation;
            Field(ship, "aDocked", new Dictionary<string, Ship>());
            var recorded = capture["data"]["navigation"]["currentMotion"];
            var situ = new ShipSitu(false) { Size = saved.objSS.size, fRot = (float)recorded["rotationRadians"], fW = (float)recorded["rotationalVelocity"] };
            Field(ship, "<objSS>k__BackingField", situ);
            Check((double)recorded["rcsAccelerationRaw"]["x"] == 0 && (double)recorded["rcsAccelerationRaw"]["y"] == 0 &&
                (double)recorded["inputAccelerationRaw"]["x"] == 0 && (double)recorded["inputAccelerationRaw"]["y"] == 0,
                "Pre-warning coasting premise no longer matches the fixture");
            geometry = Take(ship, out _); WarmCollision(ship, geometry, true); return ship;
        }
        try {
            HullDisplay.Reset(); PhysicalCollisions.Flag = Empty<BepInEx.Configuration.ConfigEntry<bool>>();
            Field(PhysicalCollisions.Flag, "_typedValue", true);
            CrewSim.system = Empty<StarSystem>(); CrewSim.system.aBOs = new Dictionary<string, BodyOrbit>();
            var a = Load("V-HZ0GD", target, out var ga); var b = Load("O-GXD", player, out var gb);
            CrewSim.system.dictShips = new Dictionary<string, Ship> { { a.strRegID, a }, { b.strRegID, b } };
            var solver = new HullContact();
            foreach (double advance in new[] { 0d, elapsed }) {
                var start = p + v * advance;
                double yawA = a.objSS.fRot - a.nGridRotation * Math.PI / 180 + a.objSS.fW * advance;
                double yawB = b.objSS.fRot - b.nGridRotation * Math.PI / 180 + b.objSS.fW * advance;
                var path = new HullContact.Motion(start, start + v * 30, yawA, yawA + a.objSS.fW * 30,
                    yawB, yawB + b.objSS.fW * 30, ga.MapRadius, gb.MapRadius, true);
                Check(solver.TrySolve(ga.Collision, gb.Collision, path, out var contact), "Captured exterior sweep failed");
                a.objSS.vPosx = start.X * HullZoom.MetresToAU; a.objSS.vPosy = start.Y * HullZoom.MetresToAU;
                a.objSS.vVelX = v.X * HullZoom.MetresToAU; a.objSS.vVelY = v.Y * HullZoom.MetresToAU;
                var padded = new ShipSitu(a.objSS, false); padded.Size *= 2;
                Console.WriteLine("Pursuit coasting control: epoch=" + (epoch + advance) + "; range m=" + start.Length +
                    "; closing m/s=" + (-start.Dot(v) / start.Length) + "; physical sweep hit=" + contact.Hit +
                    "; exterior avoidance=" + HullAvoidance.PredictFuture(padded, b.objSS, 30, a, b));
            }
            Check(hash.SequenceEqual(Hash(save)), "Read-only pursuit diagnostic changed the save");
            Console.WriteLine("Source archive unchanged. The second row extrapolates the last unsteered coast to the first announcement; it is not the already-evasive captured motion.");
        } finally { HullDisplay.Reset(); CrewSim.system = oldWorld; PhysicalCollisions.Flag = oldFlag; }
    }
}
