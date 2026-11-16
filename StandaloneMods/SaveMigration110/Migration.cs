using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Ostrabus.SaveMigration110;

internal sealed class BodyDecision
{
    internal BodyOrbit Body;
    public string Name { get; internal set; }
    public int CurrentMap { get; internal set; }
    public int ExpectedMap { get; internal set; }
    public string Action { get; internal set; }
    public string Reason { get; internal set; }
}

internal sealed class MigrationPlan
{
    internal StarSystem World;
    public string SaveVersion { get; internal set; }
    public bool LegacyOrigin { get; internal set; }
    public bool PreviouslyApplied { get; internal set; }
    public List<BodyDecision> Bodies { get; } = new List<BodyDecision>();
    public bool CanApply => Bodies.Count == 3 && Bodies.All(body => body.Action != "blocked");
    public int Changes => Bodies.Count(body => body.Action == "assign-map");
}

// Native 1.1 already seeds the new regional maps when it loads a legacy world,
// and CrewSim's Earth upgrade supplies map 800 and its content. Ganymede/Venus
// had their own original populations: assigning maps must NEVER respawn them.
internal static class Migration
{
    internal const string Marker = "Ostrabus.SaveMigration110.v1";
    internal const string EarthUpgrade = "1.0.1.13_Earth_Upgrade";
    static readonly KeyValuePair<string, int>[] Expected =
    {
        new KeyValuePair<string, int>("Earth", 800),
        new KeyValuePair<string, int>("1036 Ganymed", 801),
        new KeyValuePair<string, int>("Venus", 802)
    };

    internal static bool Legacy(string saveVersion, IEnumerable<string> markers)
    {
        if (markers != null && markers.Contains(EarthUpgrade)) return true;
        var match = Regex.Match(saveVersion ?? "", @"\d+\.\d+\.\d+(?:\.\d+)?");
        return match.Success && Version.TryParse(match.Value, out var version)
            && version < new Version(1, 1, 0, 0);
    }

    internal static MigrationPlan Plan(StarSystem world, JsonStarSystemSave defaults,
        IDictionary<string, JsonEnvironmentMap> maps, string saveVersion, IEnumerable<string> markers)
    {
        var markerList = markers?.ToArray() ?? Array.Empty<string>();
        var result = new MigrationPlan
        {
            World = world,
            SaveVersion = saveVersion,
            LegacyOrigin = Legacy(saveVersion, markerList),
            PreviouslyApplied = markerList.Contains(Marker)
        };
        foreach (var expected in Expected)
        {
            BodyOrbit body = null;
            world?.aBOs?.TryGetValue(expected.Key, out body);
            var row = new BodyDecision
            {
                Body = body,
                Name = expected.Key,
                CurrentMap = body?.EnvironmentMapID ?? -1,
                ExpectedMap = expected.Value,
                Action = "blocked"
            };
            result.Bodies.Add(row);
            var definitions = defaults?.aSpawnBodies?.Where(value => value != null
                && value.strName == expected.Key).ToArray() ?? Array.Empty<JsonSpawnBodyOrbit>();
            if (definitions.Length != 1 || definitions[0].nEnvironmentMapID != expected.Value
                || maps == null || !maps.TryGetValue(expected.Value.ToString(), out var map)
                || map == null || map.MapID != expected.Value)
            {
                row.Reason = "Native body/map definitions differ from the reviewed 1.1 content.";
            }
            else if (body == null)
            {
                row.Reason = "Saved body is missing; this migrator does not recreate world bodies.";
            }
            else if (body.EnvironmentMapID == expected.Value)
            {
                row.Action = "preserve";
                row.Reason = "Already uses the native 1.1 map; preserve its population and depletion.";
            }
            else if (body.EnvironmentMapID != 0)
            {
                row.Reason = "Existing nonzero/custom map is preserved; no replacement is authorized.";
            }
            else if (expected.Value == 800)
            {
                row.Reason = "Native Earth upgrade has not completed; inspect that load before applying.";
            }
            else if (!result.LegacyOrigin)
            {
                row.Reason = "No legacy-save provenance; cannot infer an omitted original population.";
            }
            else if (result.PreviouslyApplied)
            {
                row.Reason = "A previously migrated map was cleared; do not silently reapply over later changes.";
            }
            else
            {
                row.Action = "assign-map";
                row.Reason = "Restore omitted native map metadata; retain the original legacy wreck field, including depleted sites.";
            }
        }
        return result;
    }

    internal static int Apply(MigrationPlan plan, StarSystem current, IList<string> markers)
    {
        if (plan == null || !plan.CanApply || markers == null || !ReferenceEquals(plan.World, current))
            throw new InvalidOperationException("Migration preflight is blocked or belongs to another world.");
        foreach (var row in plan.Bodies)
        {
            if (!current.aBOs.TryGetValue(row.Name, out var body) || !ReferenceEquals(body, row.Body)
                || body.EnvironmentMapID != row.CurrentMap)
                throw new InvalidOperationException("World changed after planning; generate a fresh plan.");
        }
        if (markers.Contains(Marker) != plan.PreviouslyApplied)
            throw new InvalidOperationException("Migration history changed after planning.");
        if (plan.Changes == 0) return 0;
        bool hadMarker = markers.Contains(Marker);
        try
        {
            foreach (var row in plan.Bodies)
                if (row.Action == "assign-map") row.Body.EnvironmentMapID = row.ExpectedMap;
            if (!hadMarker) markers.Add(Marker);
            return plan.Changes;
        }
        catch
        {
            foreach (var row in plan.Bodies)
                if (row.Action == "assign-map") row.Body.EnvironmentMapID = row.CurrentMap;
            if (!hadMarker && markers.Contains(Marker)) markers.Remove(Marker);
            throw;
        }
    }
}
