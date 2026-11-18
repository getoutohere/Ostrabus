using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ostrabus.StationMigration;

if (args.Length != 3) throw new ArgumentException("OLD-DATA NEW-DATA MANIFEST.json");
var definitions = AssetFiles.Load(args[1], "condowners");
var overlays = AssetFiles.Load(args[1], "cooverlays");
var items = AssetFiles.Load(args[1], "items");
var loot = AssetFiles.Load(args[1], "loot");
var installables = AssetFiles.Load(args[1], "installables");
var assets = new Catalog(name => definitions.GetValueOrDefault(name), name => overlays.GetValueOrDefault(name),
    name => items.GetValueOrDefault(name), name => loot.GetValueOrDefault(name), installables.Values);
var output = new JArray();
var templates = new Dictionary<string, string[]>
{
    ["BCER"] = new[] { "Port Mojave", "Port Mojave Xin" },
    ["BCRS"] = new[] { "Station_BCRS", "Station_BCRS_Titan" },
    ["EJDR"] = new[] { "Station_EJDR" }, ["HQCH"] = new[] { "Station_HQCH" },
    ["JATL_SUB"] = new[] { "Station_JATL" }, ["JFTS"] = new[] { "Station_JFTS" },
    ["MLAB"] = new[] { "Station_MLAB" }, ["MSUZ_RB"] = new[] { "Station_MSUZ_RB" },
    ["SVIR"] = new[] { "Station_SVIR" }
};
// A regional kiosk name is not a physical station ID. Verify this binding
// against native world spawn assets, rather than copying the migration list.
var defaults = JArray.Parse(File.ReadAllText(Path.Combine(args[1], "star_systems", "star_system.json")))
    .OfType<JObject>().Single(row => (string)row["strName"] == "NewGame");
var stationSpawns = ((JArray)defaults["aSpawnStations"]).OfType<JObject>()
    .ToDictionary(row => (string)row["strName"], row => (string)row["strShipType"]);
var kioskNames = new Dictionary<string, string>
{
    ["JATL_SUB"] = "ItmKioskJATLScrap01", ["MSUZ_RB"] = "ItmKioskMSUZScrap01"
};
foreach (var station in templates)
    if (!stationSpawns.TryGetValue(station.Key, out string nativeTemplate) || nativeTemplate != station.Value[0])
        throw new InvalidDataException("Physical station/template binding disagrees with NewGame: " + station.Key);
foreach (var station in templates)
    foreach (string template in station.Value)
    {
        JObject Read(string data) => (JObject)JArray.Parse(File.ReadAllText(Path.Combine(data, "ships", template + ".json"))).Single();
        var before = Read(args[0]);
        var after = Read(args[1]);
        var oldRoots = ((JArray)before["aItems"]).OfType<JObject>().Where(Migration.Root).ToArray();
        var newRoots = ((JArray)after["aItems"]).OfType<JObject>().Where(Migration.Root).ToArray();
        bool Installed(JObject part) => assets.Installed(Migration.Name(part), null);
        var anchors = oldRoots.Where(Installed).GroupBy(Migration.Name).Where(group => group.Count() == 1)
            .Select(group => Compact(group.Single())).ToArray();
        string kioskName = kioskNames.GetValueOrDefault(station.Key, "ItmKiosk" + station.Key + "Scrap01");
        var kiosk = newRoots.Single(part => Migration.Name(part) == kioskName);
        var grid = new Grid(assets);
        var shape = grid.Shape(kiosk);
        var socketCells = Enumerable.Range(0, shape.Requires.Length).Select(index => shape.SocketCell(kiosk, index)).ToHashSet();
        string Site(JObject part) => string.Join("|", Migration.Name(part), (double)part["fX"], (double)part["fY"], (double?)part["fRotation"] ?? 0);
        var addedSites = newRoots.Select(Site).ToHashSet();
        var removed = oldRoots.Where(part => !addedSites.Contains(Site(part))).ToArray();
        var replace = removed.Where(part => Migration.Name(part).Contains("Kiosk")).ToArray();
        var retire = removed.Except(replace).ToArray();
        if (retire.Any(part => !Migration.Name(part).StartsWith("ItmWallRock", StringComparison.Ordinal)))
            throw new InvalidDataException("Review new non-rock retirement in " + template);
        var support = newRoots.Where(part => part != kiosk && Installed(part) &&
                grid.Shape(part).Cells(part).Any(socketCells.Contains) &&
                grid.Shape(part).Adds.Any(socket => assets.Socket(socket).Overlaps(new[] { "IsFloor", "IsWall" })))
            .Select(part => Compact(part)).ToArray();
        output.Add(new JObject
        {
            ["station"] = station.Key, ["template"] = template, ["gridRotation"] = before["nGridRotation"] ?? 0,
            ["anchors"] = new JArray(anchors), ["kiosk"] = Compact(kiosk, true),
            ["replace"] = new JArray(replace.Select(part => Compact(part))),
            ["remove"] = new JArray(retire.Select(part => Compact(part))), ["support"] = new JArray(support)
        });
    }
foreach (var parent in new[] { (Id: "JATL", Template: "Station_JATL_LZ"), (Id: "MSUZ", Template: "Station_MSUZ") })
{
    if (stationSpawns[parent.Id] != parent.Template) throw new InvalidDataException("Parent section binding changed: " + parent.Id);
    var ship = (JObject)JArray.Parse(File.ReadAllText(Path.Combine(args[1], "ships", parent.Template + ".json"))).Single();
    if (((JArray)ship["aItems"]).Any(part => (string)part["strName"] == "ItmKiosk" + parent.Id + "Scrap01"))
        throw new InvalidDataException("Previously unchanged parent now has an authored kiosk: " + parent.Template);
    output.Add(new JObject { ["station"] = parent.Id, ["template"] = parent.Template, ["kiosk"] = null,
        ["reason"] = "Native regeneration targets this parent, but the kiosk is authored in its separate substation. Preserve the parent and mark regeneration handled." });
}
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[2]))!);
File.WriteAllText(args[2], new JObject
{
    ["sourceVersions"] = "1.1.0.0 -> 1.1.0.1", ["templates"] = output
}.ToString(Formatting.Indented) + "\n");
Console.WriteLine("Generated " + output.Count + " reviewed template deltas: " + args[2]);

static JObject Compact(JObject part, bool maps = false)
{
    var result = new JObject();
    foreach (string field in new[] { "strName", "fX", "fY", "fRotation" }) result[field] = part[field]?.DeepClone() ?? 0;
    if (maps && part["aGPMSettings"] != null) result["aGPMSettings"] = part["aGPMSettings"].DeepClone();
    return result;
}
