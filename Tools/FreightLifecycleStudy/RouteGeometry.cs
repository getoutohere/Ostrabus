using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static class RouteGeometry
{
    const double KmPerAu = 149597872d; // Native game distance conversion.

    internal static int Run(string[] args)
    {
        var report = JObject.Parse(File.ReadAllText(args[1]));
        string id = args[2];
        var ship = (JObject)report["ships"][id];
        double epoch = (double)report["epoch"];
        var models = ((JArray)report["bodies"]).Select(body => body.ToObject<JsonBodyOrbitSave>())
            .ToDictionary(body => body.strName, body => new BodyOrbit(body));
        foreach (string text in (JArray)report["hierarchy"] ?? new JArray())
        {
            var pair = text.Split('=');
            if (pair.Length == 2 && models.TryGetValue(pair[0], out var child) && models.TryGetValue(pair[1], out var parent))
                child.boParent = parent;
        }
        var situ = ship["objSS"];
        var points = (JArray)situ?["jnd"]?["aPoints"] ?? new JArray();
        var future = points.Where(point => (double)point["ArrivalTime"] >= epoch).ToArray();
        if (future.Any(point => new[] { "bIsBO", "bBOLocked", "bOrbitLocked" }
            .Any(flag => (bool?)point["ObjSS"]?[flag] == true)))
            throw new NotSupportedException("An attached future route point requires native attachment propagation; raw-coordinate analysis refused.");
        var samples = new List<(double Epoch, double X, double Y)> {
            (epoch, (double)situ["vPosx"], (double)situ["vPosy"])
        };
        samples.AddRange(future
            .Select(point => ((double)point["ArrivalTime"], (double)point["ObjSS"]["vPosx"], (double)point["ObjSS"]["vPosy"])));
        var encounters = new JArray();
        // Match native solid-body collision eligibility. Placeholder station and
        // region references still participate in the ephemeris hierarchy. Small
        // natural bodies such as Ganymed must not disappear behind a mass cutoff.
        // Asteroid-field random rock encounters require a separate calculation.
        foreach (var body in models.Values.Where(body => !body.IsPlaceholder() && !body.IsAsteroidField && body.fRadius > 0))
        {
            double minimum = double.PositiveInfinity, at = 0, current = 0;
            foreach (var sample in samples)
            {
                body.UpdateTime(sample.Epoch, true, false);
                double dx = sample.X - body.dXReal, dy = sample.Y - body.dYReal;
                double altitude = (Math.Sqrt(dx * dx + dy * dy) - body.fRadius) * KmPerAu;
                if (!double.IsFinite(altitude)) throw new InvalidDataException("Nonfinite body ephemeris: " + body.strName);
                if (sample.Epoch == epoch) current = altitude;
                if (altitude < minimum) { minimum = altitude; at = sample.Epoch; }
            }
            encounters.Add(new JObject { ["body"] = body.strName, ["radiusKm"] = body.fRadius * KmPerAu,
                ["currentAltitudeKm"] = current, ["minimumSavedPointAltitudeKm"] = minimum,
                ["minimumEpoch"] = at, ["secondsFromSave"] = at - epoch });
        }
        var result = new JObject {
            ["gameAssemblyMvid"] = typeof(BodyOrbit).Assembly.ManifestModule.ModuleVersionId.ToString(),
            ["save"] = report["save"], ["id"] = id, ["epoch"] = epoch,
            ["futureSamples"] = samples.Count,
            ["closest"] = new JArray(encounters.OrderBy(row => (double)row["minimumSavedPointAltitudeKm"]).Take(5)),
            ["stationMotion"] = report["stationMotion"],
            ["note"] = "Native BodyOrbit ephemeris, reconstructed hierarchy, saved route points. Not a replay of AI decisions, collisions or interpolation between points. Ship radius is not subtracted."
        };
        string output = Path.Combine(Path.GetDirectoryName(args[1])!, Path.GetFileNameWithoutExtension(args[1]) + "-" + id + "-geometry.json");
        File.WriteAllText(output, result.ToString(Formatting.Indented));
        result.Remove("stationMotion");
        Console.WriteLine(result.ToString(Formatting.None));
        return 0;
    }
}
