using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ostrabus.NavConsole.Outlines;
using Ostrabus.ShipNavigation;
using Ostrabus.ShipNavigation.Collisions;

static partial class Program {
    static void SavedAsternApproach(string capture, string save) {
        var first = JObject.Parse(File.ReadAllText(Path.Combine(capture, "_ships_V-HZ0GD.json")));
        var second = JObject.Parse(File.ReadAllText(Path.Combine(capture, "_ships_O-GXD.json")));
        Check((double)first["epoch"] == (double)second["epoch"], "Saved approach requires equal live capture epochs");
        byte[] hash = Hash(save);
        var oldWorld = CrewSim.system; var oldFlag = PhysicalCollisions.Flag;
        Ship Read(string id, JToken snapshot) {
            var saved = SavedHullPreview.ReadRecord(save, id);
            foreach (string name in saved.aItems.Select(item => item.strName).Distinct()) {
                DataHandler.dictCOOverlays.TryGetValue(name, out var overlay); DataHandler.dictCOs.TryGetValue(name, out var basis);
                AccessTools.Method(typeof(DataHandler), "BuildDataCO").Invoke(null, new object[] { name, overlay, basis });
            }
            var hull = Hull(Ship.Loaded.Shallow, saved.aItems); hull.strRegID = id; hull.json = saved; hull.nGridRotation = saved.nGridRotation;
            Field(hull, "aDocked", new Dictionary<string, Ship>());
            var motion = snapshot["data"]["navigation"]["currentMotion"];
            var situ = new ShipSitu(false) { Size = saved.objSS.size, fRot = (float)motion["rotationRadians"] };
            // Remove only a common inertial translation and velocity. Deliberate
            // zero-thrust control: the historical captured steering was evasive.
            var origin = second["data"]["navigation"]["currentMotion"];
            situ.vPosx = (double)motion["positionAU"]["x"] - (double)origin["positionAU"]["x"];
            situ.vPosy = (double)motion["positionAU"]["y"] - (double)origin["positionAU"]["y"];
            situ.vVelX = (double)motion["velocityAUPerSecond"]["x"] - (double)origin["velocityAUPerSecond"]["x"];
            situ.vVelY = (double)motion["velocityAUPerSecond"]["y"] - (double)origin["velocityAUPerSecond"]["y"];
            Field(hull, "<objSS>k__BackingField", situ);
            var geometry = Take(hull, out _); WarmCollision(hull, geometry, true);
            Console.WriteLine(id + " saved exterior radius=" + geometry.MapRadius + "m");
            return hull;
        }
        try {
            HullDisplay.Reset(); PhysicalCollisions.Flag = Empty<BepInEx.Configuration.ConfigEntry<bool>>();
            Field(PhysicalCollisions.Flag, "_typedValue", true);
            CrewSim.system = Empty<StarSystem>(); CrewSim.system.aBOs = new Dictionary<string, BodyOrbit>();
            var a = Read("V-HZ0GD", first); var b = Read("O-GXD", second);
            CrewSim.system.dictShips = new Dictionary<string, Ship> { { a.strRegID, a }, { b.strRegID, b } };
            var padded = new ShipSitu(a.objSS, false) { Size = (int)Math.Ceiling(HullDisplay.Radius(a) * 2) };
            var obstacle = new ShipSitu(b.objSS, false) { Size = (int)Math.Ceiling(HullDisplay.Radius(b)) };
            Check(AvoidancePrediction.Collision(padded, obstacle, 30), "Historical padded warning was not reproduced");
            var original = new ShipSitu(a.objSS, false); original.Size *= 2;
            Check(!HullAvoidance.PredictFuture(original, b.objSS, 30, a, b), "Production confirmation still rejects the captured-velocity clear pass");
            Check(hash.SequenceEqual(Hash(save)), "Read-only approach replay modified the save");
            Console.WriteLine("Saved astern A/B: padded warning=True; production exterior warning=False; source archive unchanged. Coasting counterfactual, not an exact first-dodge replay.");
        } finally { HullDisplay.Reset(); CrewSim.system = oldWorld; PhysicalCollisions.Flag = oldFlag; }
    }
}
