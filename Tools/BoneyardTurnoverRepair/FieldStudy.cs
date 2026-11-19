using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ostrabus.BoneyardTurnover;
using SolarSystem;
using Ostranauts.Utils.Models;

internal static class FieldStudy
{
    internal static void Run(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("study-fields <world-audit.json> <content-audit.json> <report.json>");
        var root = JObject.Parse(File.ReadAllText(args[0]));
        var content = JObject.Parse(File.ReadAllText(args[1]));
        if ((string)root["archiveSHA256"] != (string)content["archiveSHA256"]) throw new InvalidDataException("Audit sources differ.");
        var saved = root["fields"]["objSystem"].ToObject<JsonStarSystemSave>();
        var world = new StarSystem();
        CrewSim.system = world;
        StarSystem.fEpoch = saved.dfEpoch;
        foreach (var body in saved.aBOs) world.aBOs.Add(body.strName, new BodyOrbit(body));
        foreach (var body in world.aBOs.Values) if (body.strName != "Sol") body.boParent = world.aBOs["Sol"];
        foreach (string text in saved.dictBOHierarchy)
        {
            var pair = text.Split('=');
            if (pair.Length == 2 && world.aBOs.TryGetValue(pair[0], out var child) && world.aBOs.TryGetValue(pair[1], out var parent)) child.boParent = parent;
        }
        foreach (var wreck in saved.aStellarDerelicts) world.dictStellarObjects.Add(wreck.strID, new StellarDerelict(wreck));
        var maps = JsonConvert.DeserializeObject<JsonEnvironmentMap[]>(File.ReadAllText(Path.Combine(
            Ostrabus.Development.InstallationPaths.Data, "blueprints", "environments", "environments.json")));
        var result = new JArray();
        foreach (string region in new[] { "HQCH", "EJDR", "MTRS", "JFTS", "SVIR", "MSUZ", "JPTN" })
        {
            var station = content["physical"].Single(x => (string)x["id"] == region);
            int map = (int)station["mapID"];
            var ring = maps.Single(x => x.strName == map.ToString()).aSpawnDerelictRings.Single();
            var stationSitu = new ShipSitu(station["situation"].ToObject<JsonShipSitu>());
            stationSitu.UpdateTime(StarSystem.fEpoch);
            bool ground = (int)station["classification"] == (int)Ship.TypeClassification.GroundStation ||
                (int)station["classification"] == (int)Ship.TypeClassification.GroundStationUnfinished;
            var anchor = string.IsNullOrEmpty(ring.strSpawnAroundBody)
                ? world.GetNearestBO(stationSitu, StarSystem.fEpoch, !ground) : world.GetBO(ring.strSpawnAroundBody);
            string previousAnchor = anchor.strName;
            var type = typeof(Plugin).Assembly.GetType("Ostrabus.BoneyardTurnover.Field");
            anchor.UpdateTime(StarSystem.fEpoch);
            Point center = anchor.vPos;
            if (ring.fAltSpawnDistanceKM > 0)
            {
                var reference = content["physical"].FirstOrDefault(x => (string)x["id"] == anchor.strName) ??
                    content["physical"].Where(x => (bool?)x["region"] == true)
                        .OrderBy(x => Math.Pow((double)x["situation"]["vPosx"] - anchor.dXReal, 2) +
                            Math.Pow((double)x["situation"]["vPosy"] - anchor.dYReal, 2)).First();
                var referenceSitu = new ShipSitu(reference["situation"].ToObject<JsonShipSitu>());
                referenceSitu.UpdateTime(StarSystem.fEpoch);
                bool referenceGround = (int)reference["classification"] == (int)Ship.TypeClassification.GroundStation ||
                    (int)reference["classification"] == (int)Ship.TypeClassification.GroundStationUnfinished;
                var parent = world.GetNearestBO(referenceSitu, StarSystem.fEpoch, !referenceGround);
                parent.UpdateTime(StarSystem.fEpoch);
                var method = typeof(EnvironmentEntities).GetMethod("CalculateEntityPosition", BindingFlags.Static | BindingFlags.NonPublic);
                center = (Point)method.Invoke(null, new object[] { parent.vPos, referenceSitu.vPos, (double)ring.fAltSpawnDistanceKM, ring.fAltSpawnAngle });
            }
            double bodyRadius = ring.fAltSpawnDistanceKM > 0 ? 0 : anchor.fRadiusKM;
            var field = Activator.CreateInstance(type, true);
            void Set(string name, object value) => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(field, value);
            Set("Anchor", anchor); Set("Center", center);
            Set("InnerKm", bodyRadius + ring.fSpawnRadiusMinKM);
            Set("OuterKm", bodyRadius + ring.fSpawnRadiusMaxKM);
            var rows = new JArray();
            var situations = new List<ShipSitu>();
            void Observe(string id, ShipSitu situ, string representation)
            {
                situations.Add(situ);
                bool contains = (bool)type.GetMethod("Contains", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(field, new object[] { situ });
                situ.UpdateTime(StarSystem.fEpoch);
                rows.Add(new JObject { ["id"] = id, ["representation"] = representation,
                    ["parent"] = situ.strBOPORShip, ["orbitRate"] = situ.fOrbitW, ["contains"] = contains,
                    ["distanceFromCenterKm"] = (situ.vPos - center).magnitude * 149597872d });
            }
            foreach (var hull in content["physical"].Where(x => (int?)x["mapID"] == map && (int?)x["classification"] == 0 && (string)x["owner"] == ring.strOwner))
                Observe((string)hull["id"], new ShipSitu(hull["situation"].ToObject<JsonShipSitu>()), "physical");
            foreach (var wreck in saved.aStellarDerelicts.Where(x => x.nMapID == map && x.strOwner == ring.strOwner))
                Observe(wreck.strID, new StellarDerelict(wreck).objSS, "deferred");
            var observation = new JObject { ["region"] = region, ["map"] = map,
                ["previousAnchor"] = previousAnchor, ["anchor"] = anchor.strName,
                ["center"] = new JObject { ["x"] = center.X, ["y"] = center.Y },
                ["innerKm"] = bodyRadius + ring.fSpawnRadiusMinKM,
                ["outerKm"] = bodyRadius + ring.fSpawnRadiusMaxKM, ["wrecks"] = rows };
            if (ring.fAltSpawnDistanceKM > 0)
            {
                var definition = maps.Single(x => x.strName == map.ToString());
                var markers = definition.aEntities.Where(x => x.bIsStation && x.fDistanceKM == ring.fAltSpawnDistanceKM &&
                    x.fAngle == ring.fAltSpawnAngle).ToArray();
                if (markers.Length == 1)
                {
                    string id = (string)typeof(EnvironmentEntities).GetMethod("GenerateRegID", BindingFlags.Static | BindingFlags.NonPublic)
                        .Invoke(null, new object[] { markers[0], region });
                    var deferred = saved.aStellarShips.FirstOrDefault(x => x.strID == id);
                    var physical = content["physical"].FirstOrDefault(x => (string)x["id"] == id);
                    ShipSitu marker = deferred != null ? new StellarShip(deferred).objSS :
                        physical != null ? new ShipSitu(physical["situation"].ToObject<JsonShipSitu>()) : null;
                    if (marker != null)
                    {
                        marker.UpdateTime(StarSystem.fEpoch);
                        Set("Center", marker.vPos);
                        int passes = situations.Count(x => (bool)type.GetMethod("Contains", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(field, new object[] { x }));
                        observation["savedCenterProbe"] = new JObject { ["marker"] = id, ["parent"] = marker.strBOPORShip,
                            ["orbitRate"] = marker.fOrbitW, ["centerDifferenceKm"] = (marker.vPos - center).magnitude * 149597872d,
                            ["spatialMatches"] = passes, ["runtimeChange"] = false };
                    }
                }
            }
            result.Add(observation);
        }
        File.WriteAllText(args[2], result.ToString());
        Console.WriteLine(result.ToString());
    }
}
