using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Ostranauts.ShipGUIs.Utilities;

internal static class DepartureStudy
{
    internal static void Run(string[] args)
    {
        if (args.Length != 4) throw new ArgumentException("study-departure <world-audit.json> <route-audit.json> <six-hour-ships.json> <report.json>");
        var worldJson = JObject.Parse(File.ReadAllText(args[0]));
        var routeJson = JObject.Parse(File.ReadAllText(args[1]));
        if ((string)worldJson["archiveSHA256"] != (string)routeJson["archiveSHA256"])
            throw new InvalidDataException("World and route are from different archives.");
        var saved = worldJson["fields"]["objSystem"].ToObject<JsonStarSystemSave>();
        var route = routeJson["fields"]["objSS"]["jnd"].ToObject<JsonNavData>();
        var world = new StarSystem();
        CrewSim.system = world;
        foreach (var body in saved.aBOs) world.aBOs.Add(body.strName, new BodyOrbit(body));
        var sun = world.aBOs["Sol"];
        foreach (var body in world.aBOs.Values) if (body != sun) body.boParent = sun;
        foreach (string text in saved.dictBOHierarchy)
        {
            var pair = text.Split('=');
            if (pair.Length == 2 && world.aBOs.TryGetValue(pair[0], out var child) && world.aBOs.TryGetValue(pair[1], out var parent))
                child.boParent = parent;
        }
        var nav = new NavData(route);
        var reference = world.aBOs["EJDR"];
        const double auKm = 149597872d;
        const double regionRadius = 0.06684587150812149;
        double Range(double epoch)
        {
            StarSystem.fEpoch = epoch;
            reference.UpdateTime(epoch);
            var ship = nav.GetShipSituAtTime(epoch, false) ?? throw new InvalidDataException("No native route sample.");
            return (ship.vPos - reference.vPos).magnitude;
        }

        // Validate both moving objects against an independent live capture
        // before inferring a crossing. A plausible-looking reconstruction is
        // insufficient if it does not reproduce those observed positions.
        var capture = JArray.Parse(File.ReadAllText(args[2]));
        var stationCapture = capture.Single(x => (string)x["data"]?["id"] == "EJDR");
        var shipCapture = capture.Single(x => (string)x["data"]?["id"] == route.strRegID);
        double checkEpoch = (double)stationCapture["epoch"];
        if ((double)shipCapture["epoch"] != checkEpoch) throw new InvalidDataException("Non-atomic validation snapshot.");
        Range(checkEpoch);
        var predicted = nav.GetShipSituAtTime(checkEpoch, false);
        double Error(JToken motion, double x, double y) =>
            Math.Sqrt(Math.Pow((double)motion["x"] - x, 2) + Math.Pow((double)motion["y"] - y, 2)) * auKm;
        double stationError = Error(stationCapture["data"]["navigation"]["currentMotion"]["positionAU"], reference.dXReal, reference.dYReal);
        double shipError = Error(shipCapture["data"]["navigation"]["currentMotion"]["positionAU"], predicted.vPosx, predicted.vPosy);
        if (stationError > .001 || shipError > .001)
            throw new InvalidDataException("Reconstruction differs from live snapshot: station " + stationError + " km, ship " + shipError + " km.");
        double origin = route.aPoints.First().ArrivalTime;
        double lo = checkEpoch, hi = lo;
        if (Range(lo) >= regionRadius) throw new InvalidDataException("Six-hour snapshot was not inside EJDR.");
        while (Range(hi) < regionRadius && hi < route.aPoints.Last().ArrivalTime) hi += 60;
        if (hi >= route.aPoints.Last().ArrivalTime) throw new InvalidDataException("No early departure crossing found.");
        for (int i = 0; i < 50; i++)
        {
            double middle = (lo + hi) / 2;
            if (Range(middle) < regionRadius) lo = middle; else hi = middle;
        }
        double now = saved.dfEpoch;
        var report = new JObject
        {
            ["archiveSHA256"] = worldJson["archiveSHA256"], ["routeOriginEpoch"] = origin,
            ["nativeGameMvid"] = typeof(CrewSim).Module.ModuleVersionId.ToString("D"),
            ["routeEndEpoch"] = route.aPoints.Last().ArrivalTime,
            ["plottedHours"] = (route.aPoints.Last().ArrivalTime - origin) / 3600,
            ["sixHourCaptureEpoch"] = checkEpoch, ["sixHourRangeKm"] = Range(checkEpoch) * auKm,
            ["stationReplayErrorKm"] = stationError, ["shipReplayErrorKm"] = shipError,
            ["exitEpoch"] = hi, ["hoursAfterEngage"] = (hi - origin) / 3600,
            ["absenceAtCoastHours"] = (now - hi) / 3600,
            ["basis"] = "Native saved-route playback and saved body hierarchy, validated against the six-hour live snapshot; geometric crossing, not a recorded RegionCleanup callback.",
            ["maximumSffStepSeconds"] = 21600,
            ["latestDetectionEpochUnderSixHourSteps"] = hi + 21600,
            ["regionRadiusKm"] = regionRadius * auKm
        };
        File.WriteAllText(args[3], report.ToString());
        Console.WriteLine(report.ToString());
    }
}
