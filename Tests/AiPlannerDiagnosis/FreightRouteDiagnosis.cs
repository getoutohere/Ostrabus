using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using Newtonsoft.Json.Linq;
using Ostrabus.ShipNavigation;
using Ostranauts.ShipGUIs.Utilities;

static class FreightRouteDiagnosis {
    internal static void Run(string path, string id) {
        var report = JObject.Parse(File.ReadAllText(path));
        var world = (StarSystem)FormatterServices.GetUninitializedObject(typeof(StarSystem));
        world.aBOs = new Dictionary<string, BodyOrbit>();
        world.dictShips = new Dictionary<string, Ship>();
        CrewSim.system = world;
        StarSystem.fEpoch = (double)report["epoch"];
        foreach (var body in report["bodies"]) {
            var model = new BodyOrbit(body.ToObject<JsonBodyOrbitSave>());
            world.aBOs.Add(model.strName, model);
        }
        foreach (string link in report["hierarchy"]) {
            string[] pair = link.Split('=');
            if (world.aBOs.TryGetValue(pair[0], out var child) && world.aBOs.TryGetValue(pair[1], out var parent))
                child.boParent = parent;
        }
        world.boStar = world.aBOs["Sol"];
        var savedShip = report["ships"][id];
        string destinationId = (string)savedShip["strScanTargetID"];
        var route = new NavData(savedShip["objSS"]["jnd"].ToObject<JsonNavData>());
        var destination = new ShipSitu(report["stationMotion"][destinationId].ToObject<JsonShipSitu>());
        bool admitted = AiRendezvous.Arrives(route, new NavDataPoint(StarSystem.fEpoch, destination));
        Console.WriteLine(new JObject {
            ["save"] = report["save"], ["id"] = id, ["destination"] = destinationId,
            ["passesExistingRendezvousValidation"] = admitted,
            ["points"] = route.GetNavDataPoints().Count,
            ["gameAssemblyMvid"] = typeof(BodyOrbit).Assembly.ManifestModule.ModuleVersionId.ToString()
        }.ToString(Newtonsoft.Json.Formatting.None));
        CrewSim.system = null;
    }
}
