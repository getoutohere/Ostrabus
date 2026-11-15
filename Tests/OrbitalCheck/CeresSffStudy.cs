using System;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Ostrabus.Orbital;

// Optional reproduction against an external extracted save; no player data is tracked.
static class CeresSffStudy {
    internal static void Run(string folder) {
        var system = JObject.Parse(File.ReadAllText(Path.Combine(folder, "system.json")));
        var bodies = new Dictionary<string, BodyOrbit>();
        foreach (string key in new[] { "aBOs", "aAsteroidFields" })
            foreach (var node in system[key]) {
                var body = new BodyOrbit(node.ToObject<JsonBodyOrbitSave>());
                bodies.Add(body.strName, body);
            }
        foreach (string pair in system["dictBOHierarchy"].Values<string>()) {
            var parts = pair.Split('=');
            if (parts.Length == 2 && bodies.TryGetValue(parts[0], out var child) && bodies.TryGetValue(parts[1], out var parent)) child.boParent = parent;
        }
        double start = (double)system["dfEpoch"], end = 65634053088.797806;
        foreach (var id in new[] { "BCER", "BCRS", "Ceres", "CeresAsteroidFieldCentral", "C108a", "C670p" }) {
            OrbitalMotion.BodyAt(bodies[id], start, out var first);
            OrbitalMotion.BodyAt(bodies[id], end, out var last);
            Console.WriteLine(id + " start=" + first.Position.X / OrbitalMotion.KmPerAU + "," + first.Position.Y / OrbitalMotion.KmPerAU +
                " end=" + last.Position.X / OrbitalMotion.KmPerAU + "," + last.Position.Y / OrbitalMotion.KmPerAU);
        }
        foreach (var id in new[] { "O-7X75", "O-326", "O-BDJ", "O-KGEE" }) {
            var root = JToken.Parse(File.ReadAllText(Path.Combine(folder, id + ".json")));
            var ship = root is JArray ? root[0] : root;
            var target = ship["objSituScanTarget"];
            Console.WriteLine(id + " target=" + ship["strTargetRegID"] + " targetBody=" + target?["boPORShip"]);
            if (target?["boPORShip"] != null && bodies.TryGetValue((string)target["boPORShip"], out var body)) {
                OrbitalMotion.BodyAt(body, end, out var actual);
                Console.WriteLine(" expected target end=" + (actual.Position.X / OrbitalMotion.KmPerAU + (double)target["vBOOffsetx"]) + "," +
                    (actual.Position.Y / OrbitalMotion.KmPerAU + (double)target["vBOOffsety"]));
            }
        }
    }
}
