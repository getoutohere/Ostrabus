using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using HarmonyLib;
using LitJson;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ostrabus.SaveMigration110;
using Ostranauts.Core.Models;
using Ostranauts.Ships;
using SolarSystem;

static class Program
{
    static int checks;
    static JsonStarSystemSave defaults;
    static Dictionary<string, JsonEnvironmentMap> maps;
    static readonly string[] Names = { "Earth", "1036 Ganymed", "Venus" };
    static readonly int[] MapIds = { 800, 801, 802 };
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Check(bool value, string message)
    {
        checks++;
        if (!value) throw new Exception(message);
    }

    static StarSystem World(params int[] ids)
    {
        var world = Empty<StarSystem>();
        world.aBOs = new Dictionary<string, BodyOrbit>();
        world.dictShips = new Dictionary<string, Ship>();
        world.dictStellarObjects = new DictionaryArray<string, IStellarObject>();
        for (int i = 0; i < Names.Length; i++)
        {
            var body = Empty<BodyOrbit>();
            body.strName = Names[i];
            body.EnvironmentMapID = ids[i];
            body.fPeriod = 31556926;
            body.fAxis1 = 1;
            body.fAxis2 = 1;
            world.aBOs.Add(Names[i], body);
        }
        world.dictShips.Add("original-ship", Empty<Ship>());
        return world;
    }

    static MigrationPlan Plan(StarSystem world, IList<string> markers = null, string version = "Release Build: 1.0.1.10") =>
        Migration.Plan(world, defaults, maps, version, markers ?? new List<string>());

    static void Throws(Action action, string message)
    {
        bool threw = false;
        try { action(); }
        catch (InvalidOperationException) { threw = true; }
        Check(threw, message);
    }

    sealed class FailedMarkerWrite : Collection<string>
    {
        protected override void InsertItem(int index, string item)
        {
            base.InsertItem(index, item);
            if (item == Migration.Marker) throw new InvalidOperationException("Fixture failure after the marker write.");
        }
    }

    static void Controls()
    {
        foreach (string version in new[] { "Release Build: 1.0.1.7", "1.0.1.10", "Early Access Build: 0.15.0.0" })
            Check(Migration.Legacy(version, Array.Empty<string>()), "Legacy version was not recognized.");
        foreach (string version in new[] { "Release Build: 1.1.0.0", "1.1.0.1", "unrecognized", "" })
            Check(!Migration.Legacy(version, Array.Empty<string>()), "Unproven legacy provenance was accepted.");
        Check(Migration.Legacy("1.1.0.0", new[] { Migration.EarthUpgrade }), "Native upgrade history was ignored.");

        // Native new-game state must be a no-op, including its migration history.
        var world = World(800, 801, 802);
        var markers = new List<string> { "unrelated-native-upgrade" };
        var plan = Plan(world, markers, "1.1.0.0");
        Check(plan.CanApply && plan.Changes == 0, "Fresh world needs a migration.");
        Check(Migration.Apply(plan, world, markers) == 0 && markers.Count == 1, "Fresh no-op changed history.");

        // Every combination of already-filled and omitted legacy map fields.
        for (int bits = 0; bits < 4; bits++)
        {
            world = World(800, (bits & 1) != 0 ? 801 : 0, (bits & 2) != 0 ? 802 : 0);
            markers = new List<string> { Migration.EarthUpgrade, "keep-this-marker" };
            var ships = world.dictShips;
            var originalShip = ships["original-ship"];
            var stellar = world.dictStellarObjects;
            var bodyRefs = world.aBOs.Values.ToArray();
            string[] before = bodyRefs.Select(body => JsonConvert.SerializeObject(body.GetJSONSave())).ToArray();
            plan = Plan(world, markers);
            int expected = ((bits & 1) == 0 ? 1 : 0) + ((bits & 2) == 0 ? 1 : 0);
            Check(plan.CanApply && plan.Changes == expected, "Legacy plan selected the wrong fields.");
            Check(world.aBOs["1036 Ganymed"].EnvironmentMapID == ((bits & 1) != 0 ? 801 : 0), "Planning wrote a body.");
            Check(Migration.Apply(plan, world, markers) == expected, "Wrong application count.");
            Check(ReferenceEquals(world.dictShips, ships) && ReferenceEquals(ships["original-ship"], originalShip)
                && ReferenceEquals(world.dictStellarObjects, stellar) && stellar.Count == 0,
                "Migration replaced a registry or refilled a depleted population.");
            Check(markers.Contains("keep-this-marker") && markers.Count(value => value == Migration.Marker) == (expected > 0 ? 1 : 0),
                "History was replaced or duplicate migration markers were added.");
            for (int i = 0; i < bodyRefs.Length; i++)
            {
                var old = JObject.Parse(before[i]);
                var after = JObject.FromObject(bodyRefs[i].GetJSONSave());
                Check((int)after["nEnvironmentMapID"] == MapIds[i], "Native GetJSONSave omitted the migrated map.");
                old.Remove("nEnvironmentMapID");
                after.Remove("nEnvironmentMapID");
                Check(JToken.DeepEquals(old, after), "Orbital, atmospheric or presentation fields changed.");
                // Actual native serializer and body loader retain the new field.
                var dto = JsonMapper.ToObject<JsonBodyOrbitSave>(JsonMapper.ToJson(bodyRefs[i].GetJSONSave()));
                var reloaded = new BodyOrbit(dto);
                Check(reloaded.EnvironmentMapID == MapIds[i], "Native body save/load lost the map.");
            }
            var again = Plan(world, markers);
            Check(again.CanApply && Migration.Apply(again, world, markers) == 0, "Repeated application was not a no-op.");
        }

        foreach (int conflicting in new[] { -1, 37, 900 })
        {
            world = World(800, 0, conflicting);
            markers = new List<string>();
            plan = Plan(world, markers);
            Check(!plan.CanApply, "Conflicting map passed preflight.");
            Throws(() => Migration.Apply(plan, world, markers), "Blocked plan wrote to a world.");
            Check(world.aBOs["1036 Ganymed"].EnvironmentMapID == 0 && world.aBOs["Venus"].EnvironmentMapID == conflicting,
                "A blocked plan partially applied.");
        }
        world = World(0, 0, 0);
        Check(!Plan(world).CanApply, "Missing native Earth upgrade was silently replaced.");
        Check(!Plan(World(800, 0, 0), version: "1.1.0.0").CanApply, "Fresh missing fields were mistaken for a legacy population.");
        Check(!Plan(World(800, 0, 0), new List<string> { Migration.Marker }).CanApply, "Later-cleared fields were silently restored.");
        world = World(800, 0, 0);
        world.aBOs.Remove("Venus");
        Check(!Plan(world).CanApply, "Missing world body passed preflight.");

        world = World(800, 0, 0);
        markers = new List<string>();
        plan = Plan(world, markers);
        world.aBOs["Venus"].EnvironmentMapID = 99;
        Throws(() => Migration.Apply(plan, world, markers), "Stale body plan applied.");
        Check(world.aBOs["1036 Ganymed"].EnvironmentMapID == 0, "Stale plan changed another body.");
        plan = Plan(World(800, 0, 0), markers);
        Throws(() => Migration.Apply(plan, World(800, 0, 0), markers), "Plan applied to a different world.");

        world = World(800, 0, 0);
        var failed = new FailedMarkerWrite { "keep-me" };
        plan = Plan(world, failed);
        Throws(() => Migration.Apply(plan, world, failed), "Marker write failure did not propagate.");
        Check(world.aBOs["1036 Ganymed"].EnvironmentMapID == 0 && world.aBOs["Venus"].EnvironmentMapID == 0
            && failed.SequenceEqual(new[] { "keep-me" }), "Failed commit did not restore both maps and history.");

        int original = defaults.aSpawnBodies.Single(body => body.strName == "Venus").nEnvironmentMapID;
        defaults.aSpawnBodies.Single(body => body.strName == "Venus").nEnvironmentMapID = 999;
        Check(!Plan(World(800, 0, 0)).CanApply, "Changed native body definition passed preflight.");
        defaults.aSpawnBodies.Single(body => body.strName == "Venus").nEnvironmentMapID = original;
        var savedMap = maps["801"];
        maps.Remove("801");
        Check(!Plan(World(800, 0, 0)).CanApply, "Missing native map definition passed preflight.");
        maps["801"] = savedMap;
    }

    static void ArchiveSnapshot(string path, bool fresh)
    {
        // Read-only slices contain native saved-system DTOs, not the huge CO table.
        var source = JObject.Parse(File.ReadAllText(path));
        var system = source["objSystem"].ToObject<JsonStarSystemSave>();
        var world = World(0, 0, 0);
        foreach (string name in Names)
            world.aBOs[name] = new BodyOrbit(system.aBOs.Single(body => body.strName == name));
        world.dictShips.Clear();
        foreach (string id in system.dictShips ?? Array.Empty<string>())
            world.dictShips.Add(id, Empty<Ship>());
        foreach (var saved in system.aStellarShips ?? Array.Empty<JsonStellarShipSave>())
        {
            var ship = new StellarShip(saved);
            world.dictStellarObjects[ship.strID] = ship;
        }
        foreach (var saved in system.aStellarDerelicts ?? Array.Empty<JsonStellarDerelictSave>())
        {
            var wreck = new StellarDerelict(saved);
            world.dictStellarObjects[wreck.strID] = wreck;
        }
        var hulls = world.dictShips.ToArray();
        var environment = world.dictStellarObjects.ToArray();
        string population = JsonConvert.SerializeObject(environment.Select(item => item is StellarShip ship
            ? (object)new JsonStellarShipSave(ship) : new JsonStellarDerelictSave((StellarDerelict)item)));
        var markers = fresh ? new List<string>() : new List<string> { Migration.EarthUpgrade };
        string version = (string)source["strVersion"];
        string immutableSystem = source["objSystem"].ToString(Formatting.None);
        var plan = Plan(world, markers, version);
        Check(plan.CanApply && plan.Changes == (fresh ? 0 : 2), "Real-save preflight differs from reviewed metadata.");
        int result = Migration.Apply(plan, world, markers);
        Check(result == (fresh ? 0 : 2), "Real-save application count differs.");
        Check(source["objSystem"].ToString(Formatting.None) == immutableSystem, "Read-only source DTO slice changed.");
        Check(world.dictShips.Count == hulls.Length && world.dictStellarObjects.Count == environment.Length,
            "Real-save case created or removed world content.");
        foreach (var hull in hulls)
            Check(ReferenceEquals(world.dictShips[hull.Key], hull.Value), "Real-save physical hull was replaced.");
        foreach (var item in environment)
            Check(ReferenceEquals(world.dictStellarObjects[item.strID], item), "Real-save deferred object was replaced.");
        string afterPopulation = JsonConvert.SerializeObject(environment.Select(item => item is StellarShip ship
            ? (object)new JsonStellarShipSave(ship) : new JsonStellarDerelictSave((StellarDerelict)item)));
        Check(population == afterPopulation, "Native serialized deferred population changed.");
        Check(Plan(world, markers, version).Changes == 0, "Real-save repeated plan still changes fields.");
        Console.WriteLine((fresh ? "Fresh" : "Legacy") + " save: " + result + " map assignments; population source and original slice unchanged.");
    }

    static bool UnityEqual(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result)
    {
        __result = ReferenceEquals(__0, __1);
        return false;
    }

    static void ReadyStateChecks()
    {
        var oldCrew = CrewSim.objInstance;
        var oldWorld = CrewSim.system;
        var oldDefaults = DataHandler.dictStarSystems;
        var oldMaps = DataHandler.dictEnvironmentMaps;
        var oldMarkers = CrewSim.aPatchesApplied;
        string oldVersion = CrewSim.strSaveVersion;
        bool oldEdit = CrewSim.bShipEdit;
        var boundary = new Harmony("offline.migration110.unity-identity");
        boundary.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Equality"),
            prefix: new HarmonyMethod(typeof(Program), nameof(UnityEqual)));
        try
        {
            CrewSim.objInstance = Empty<CrewSim>();
            CrewSim.system = World(800, 0, 0);
            CrewSim.bShipEdit = false;
            CrewSim.strSaveVersion = "Release Build: 1.0.1.10";
            CrewSim.aPatchesApplied = new List<string> { Migration.EarthUpgrade };
            DataHandler.dictStarSystems = new Dictionary<string, JsonStarSystemSave> { ["NewGame"] = defaults };
            DataHandler.dictEnvironmentMaps = maps;
            Plugin.Ready = true;
            // Native DoLoadGame invokes the finished event before flipping this
            // flag. The observation path may plan there; write commands may not.
            AccessTools.Field(typeof(CrewSim), "_finishedLoading").SetValue(CrewSim.objInstance, false);
            Throws(() => Commands.Plan(), "Command path allowed a mutation plan during loading.");
            Check(Commands.Plan(allowLoadingSummary: true).Changes == 2, "Native load-event observation was incorrectly gated.");
            Check(CrewSim.system.aBOs["Venus"].EnvironmentMapID == 0 && CrewSim.aPatchesApplied.Count == 1,
                "Load-event planning mutated world state or history.");
            AccessTools.Field(typeof(CrewSim), "_finishedLoading").SetValue(CrewSim.objInstance, true);
            Check(Commands.Plan().Changes == 2, "Loaded command path could not plan.");
            CrewSim.bShipEdit = true;
            Throws(() => Commands.Plan(allowLoadingSummary: true), "Editor mode was accepted as a saved world.");
            CrewSim.bShipEdit = false;
            Plugin.Ready = false;
            Throws(() => Commands.Plan(allowLoadingSummary: true), "Disabled integration was accepted.");
        }
        finally
        {
            Plugin.Ready = false;
            CrewSim.objInstance = oldCrew;
            CrewSim.system = oldWorld;
            CrewSim.bShipEdit = oldEdit;
            CrewSim.strSaveVersion = oldVersion;
            CrewSim.aPatchesApplied = oldMarkers;
            DataHandler.dictStarSystems = oldDefaults;
            DataHandler.dictEnvironmentMaps = oldMaps;
            boundary.UnpatchSelf();
        }
    }

    static int Main(string[] args)
    {
        string data = Ostrabus.Development.InstallationPaths.Data;
        defaults = JArray.Parse(File.ReadAllText(Path.Combine(data, "star_systems", "star_system.json")))
            .Single(value => (string)value["strName"] == "NewGame").ToObject<JsonStarSystemSave>();
        maps = JArray.Parse(File.ReadAllText(Path.Combine(data, "blueprints", "environments", "environments.json")))
            .Select(value => value.ToObject<JsonEnvironmentMap>()).ToDictionary(value => value.strName);
        Controls();
        ReadyStateChecks();
        if (args.Length == 2)
        {
            ArchiveSnapshot(args[0], false);
            ArchiveSnapshot(args[1], true);
        }
        else if (args.Length != 0) throw new ArgumentException("Provide both legacy and fresh saved-system slice paths.");
        var harmony = new Harmony("offline.save-migration110.binding");
        try
        {
            harmony.CreateClassProcessor(typeof(MigrationCommandPatch)).Patch();
            var targets = Harmony.GetAllPatchedMethods().Where(method => Harmony.GetPatchInfo(method)?.Owners.Contains(harmony.Id) == true).ToList();
            Check(targets.Count == 1 && targets[0].DeclaringType == typeof(ConsoleResolver), "Console integration did not bind its single target.");
            Check(!Commands.Handle("unrelated-command"), "Migration intercepted an unrelated console command.");
        }
        finally { harmony.UnpatchSelf(); }
        Console.WriteLine(checks + " native migration planning, no-op, preservation, refusal, rollback and body save/load checks passed.");
        return 0;
    }
}
