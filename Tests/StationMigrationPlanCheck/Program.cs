using System.IO.Compression;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ostrabus.StationMigration;

try
{
if (args.Length < 3) throw new ArgumentException("OLD-DATA NEW-DATA MANIFEST [SAVE.zip]");
var definitions = AssetFiles.Load(args[1], "condowners");
var overlays = AssetFiles.Load(args[1], "cooverlays");
var itemDefs = AssetFiles.Load(args[1], "items");
var loot = AssetFiles.Load(args[1], "loot");
var installables = AssetFiles.Load(args[1], "installables");
var assets = new Catalog(name => definitions.GetValueOrDefault(name), name => overlays.GetValueOrDefault(name),
    name => itemDefs.GetValueOrDefault(name), name => loot.GetValueOrDefault(name), installables.Values);
var manifests = (JArray)JObject.Parse(File.ReadAllText(args[2]))["templates"];
int checks = 0;
void Check(bool ok, string message) { checks++; if (!ok) throw new InvalidOperationException(message); }
var worldSpawns = ((JArray)AssetFiles.Load(args[1], "star_systems")["NewGame"]["aSpawnStations"])
    .OfType<JObject>().ToDictionary(row => (string)row["strName"], row => (string)row["strShipType"]);
foreach (var group in manifests.OfType<JObject>().Where(row => row["kiosk"]?.Type == JTokenType.Object).GroupBy(row => (string)row["station"]))
    Check(worldSpawns.TryGetValue(group.Key, out string physicalTemplate) && group.Any(row => (string)row["template"] == physicalTemplate),
        "World spawn assets disagree with the authored kiosk target: " + group.Key);
JObject Read(string data, string name)
{
    var fixture = (JObject)JArray.Parse(File.ReadAllText(Path.Combine(data, "ships", name + ".json"))).Single();
    // Synthetic saved fixtures use the explicit Records() table below, not
    // template-only embedded actor/loot payloads from blueprint generation.
    fixture["aCOs"] = null;
    return fixture;
}
Dictionary<string, JObject> Records(JObject station) => ((JArray)station["aItems"]).OfType<JObject>()
    .ToDictionary(Migration.Id, part => new JObject { ["strID"] = Migration.Id(part), ["strCODef"] = Migration.Name(part),
        ["strRegIDLast"] = station["strRegID"], ["aConds"] = new JArray("DEFAULT") });

StationPlan Run(JObject station, JObject manifest, Dictionary<string, JObject> records)
{
    string original = station.ToString(Formatting.None), stateBefore = JsonConvert.SerializeObject(records);
    var plan = Migration.Plan(station, manifest, assets, id => records.GetValueOrDefault(id));
    Check(original == station.ToString(Formatting.None) && stateBefore == JsonConvert.SerializeObject(records), "Planning modified inputs");
    var items = ((JArray)plan.Updated["aItems"]).OfType<JObject>().ToArray();
    bool authored = manifest["kiosk"]?.Type == JTokenType.Object;
    string wanted = authored ? (string)manifest["kiosk"]["strName"] : "ItmKiosk" + station["strRegID"] + "Scrap01";
    Check(items.Count(part => Migration.Root(part) && Migration.Name(part) == wanted) == (authored ? 1 : 0), "Kiosk count: " + station["strRegID"]);
    if (!authored)
        Check(JToken.DeepEquals(station["aItems"], plan.Updated["aItems"]) && plan.Added == 0 && plan.Uninstalled == 0,
            "Protected parent received unauthored layout changes");
    Check(Catalog.Strings(plan.Updated["shipCO"]["aConds"]).Contains(Migration.Fixed + "=1.0x1"), "Missing per-station completion flag");
    var originalIds = ((JArray)station["aItems"]).OfType<JObject>().Select(Migration.Id).Except(plan.Retired);
    Check(!originalIds.Except(items.Select(Migration.Id)).Any(), "Lost non-retired item");
    foreach (var pair in plan.Records) records[pair.Key] = pair.Value;
    foreach (string id in plan.Retired) records.Remove(id);
    var repeat = Migration.Plan(plan.Updated, manifest, assets, id => records.GetValueOrDefault(id));
    Check(JToken.DeepEquals(plan.Updated, repeat.Updated) && repeat.Added == 0 && repeat.Moved == 0, "Repeated migration is not idempotent");
    // Persist exactly the data consumed by a vanilla loader, then repeat with
    // no plugin-only identifiers or serialized runtime structures.
    var roundtrip = JObject.Parse(plan.Updated.ToString(Formatting.None));
    var statesRoundtrip = JsonConvert.DeserializeObject<Dictionary<string, JObject>>(JsonConvert.SerializeObject(records));
    var afterSave = Migration.Plan(roundtrip, manifest, assets, id => statesRoundtrip.GetValueOrDefault(id));
    Check(JToken.DeepEquals(roundtrip, afterSave.Updated), "JSON roundtrip changed migration result");
    Console.WriteLine("PASS " + plan);
    return plan;
}

foreach (var manifest in manifests.OfType<JObject>())
{
    var station = Read(args[0], (string)manifest["template"]);
    station["strRegID"] = manifest["station"];
    Run(station, manifest, Records(station));
    if (!authoredManifest(manifest)) continue;
    // Translate and rotate the saved interior independently of its blueprint.
    var transformed = Migration.Clone(station);
    transformed["nGridRotation"] = ((int?)station["nGridRotation"] ?? 0) + 90;
    foreach (var part in ((JArray)transformed["aItems"]).OfType<JObject>().Where(Migration.Root).ToArray())
    {
        float x = (float)part["fX"], y = (float)part["fY"];
        part["fX"] = y + 37;
        part["fY"] = -x - 91;
        if ((bool?)assets.Item(Migration.Name(part))["bHasSpriteSheet"] != true)
            part["fRotation"] = (((float?)part["fRotation"] ?? 0) + 270) % 360;
    }
    Run(transformed, manifest, Records(transformed));
}

bool authoredManifest(JObject manifest) => manifest["kiosk"]?.Type == JTokenType.Object;

// Blocking player construction is uninstalled through the authored native
// installed/loose pair, retaining its ID, damage, GUI overrides and children.
var bcrsManifest = manifests.OfType<JObject>().First(row => (string)row["station"] == "BCRS");
var blocked = Read(args[0], (string)bcrsManifest["template"]);
blocked["strRegID"] = "BCRS";
var blocker = Migration.Clone((JObject)bcrsManifest["kiosk"]);
blocker["strName"] = "ItmWall1x1";
blocker["strID"] = "PLAYER-BLOCKER";
((JArray)blocked["aItems"]).Add(blocker);
var blockedStates = Records(blocked);
blockedStates["PLAYER-BLOCKER"]["aConds"] = new JArray("DEFAULT", "StatDamage=1.0x7");
var blockedPlan = Run(blocked, bcrsManifest, blockedStates);
var preserved = ((JArray)blockedPlan.Updated["aItems"]).OfType<JObject>().Single(part => Migration.Id(part) == "PLAYER-BLOCKER");
Check(Migration.Name(preserved) == "ItmWall1x1Loose" && !assets.Installed(Migration.Name(preserved), blockedStates["PLAYER-BLOCKER"]), "Blocking wall not made loose");
Check(Catalog.Strings(blockedStates["PLAYER-BLOCKER"]["aConds"]).Contains("StatDamage=1.0x7"), "Conversion lost damage");

// Named output tables introduced for weapons in 1.1.0.4 must remain usable
// when migration preserves a player's obstructing installed gun as a loose item.
foreach (var recipe in installables.Values.Where(row => (string)row["strJobType"] == "uninstall" &&
    ((string)row["strActionCO"]).StartsWith("ItmShipWeapon", StringComparison.Ordinal) &&
    row["strLootOut"]?.Type == JTokenType.String))
{
    string installed = (string)recipe["strActionCO"];
    Check(assets.LooseDefinition(installed) == assets.RecipeOutputs(recipe).Single(),
        "Named native uninstall output was not resolved: " + installed);
}
Check(assets.RecipeOutputs(new JObject { ["strLootOut"] = "ItmTransponder01LooseChance" }).Length == 0,
    "Migration guessed a deterministic replacement from a random transponder recipe");
var armed = Read(args[0], (string)bcrsManifest["template"]);
armed["strRegID"] = "BCRS";
var gun = Migration.Clone(blocker);
gun["strName"] = "ItmShipWeaponPDC01Off";
gun["strID"] = "PLAYER-PDC";
((JArray)armed["aItems"]).Add(gun);
var gunStates = Records(armed);
gunStates["PLAYER-PDC"]["aConds"] = new JArray("DEFAULT", "StatDamage=1.0x7");
gunStates["PLAYER-PDC"]["aLot"] = new JArray("RETAINED-AMMO");
var armedPlan = Run(armed, bcrsManifest, gunStates);
Check(armedPlan.Uninstalled == 1 &&
    ((JArray)armedPlan.Updated["aItems"]).OfType<JObject>().Single(part => Migration.Id(part) == "PLAYER-PDC")["strName"].Value<string>() == "ItmShipWeaponPDC01Loose",
    "Blocking PDC did not preserve the authored loose form");
Check(Catalog.Strings(gunStates["PLAYER-PDC"]["aLot"]).SequenceEqual(new[] { "RETAINED-AMMO" }) &&
    Catalog.Strings(gunStates["PLAYER-PDC"]["aConds"]).Contains("StatDamage=1.0x7"),
    "Named-output conversion changed ammunition identity or damage");

// Use a real native container recipe to test nested inventory and saved GUI
// connection/settings preservation through an administrative uninstallation.
string containerName = installables.Values.Where(row => (string)row["strJobType"] == "uninstall" &&
        assets.RecipeOutputs(row).Length == 1)
    .Select(row => (string)row["strActionCO"])
    .First(name => definitions.TryGetValue(name, out var definition) &&
        (int?)definition["nContainerWidth"] > 0 && assets.Installed(name, null));
var withContents = Read(args[0], (string)bcrsManifest["template"]);
withContents["strRegID"] = "BCRS";
var container = Migration.Clone(blocker);
container["strName"] = containerName;
container["strID"] = "PLAYER-CONTAINER";
container["aGPMSettings"] = new JArray(new JObject { ["strName"] = "PreservedSettings", ["dictGUIPropMap"] = new JArray("connection", "original") });
((JArray)withContents["aItems"]).Add(container);
var child = new JObject { ["strID"] = "PLAYER-CONTENTS", ["strName"] = "ItmWall1x1Loose", ["fX"] = 0, ["fY"] = 0,
    ["fRotation"] = 0, ["strParentID"] = "PLAYER-CONTAINER" };
((JArray)withContents["aItems"]).Add(child);
var containerStates = Records(withContents);
containerStates["PLAYER-CONTAINER"]["aLot"] = new JArray("PLAYER-CONTENTS");
var contentsPlan = Run(withContents, bcrsManifest, containerStates);
Check(contentsPlan.Uninstalled == 1, "Container fixture did not exercise uninstallation");
Check(JToken.DeepEquals(((JArray)contentsPlan.Updated["aItems"]).Single(part => (string)part["strID"] == "PLAYER-CONTENTS"), child),
    "Uninstallation changed nested child layout/identity");
var retainedContainer = ((JArray)contentsPlan.Updated["aItems"]).Single(part => (string)part["strID"] == "PLAYER-CONTAINER");
Check(JToken.DeepEquals(retainedContainer["aGPMSettings"], container["aGPMSettings"]) &&
    Catalog.Strings(containerStates["PLAYER-CONTAINER"]["aLot"]).SequenceEqual(new[] { "PLAYER-CONTENTS" }), "Container settings or ownership links were lost");

// Native map parts may omit CO records; they must still contribute their asset
// geometry. Local CO data must take precedence over the same global ID.
var mapFixture = Read(args[0], (string)bcrsManifest["template"]);
mapFixture["strRegID"] = "BCRS";
var mapStates = Records(mapFixture);
var mapFloor = ((JArray)mapFixture["aItems"]).OfType<JObject>().First(part => Migration.Root(part) && Migration.Name(part).Contains("Floor") &&
    assets.Installed(Migration.Name(part), null));
string oldId = Migration.Id(mapFloor);
mapFloor["strID"] = "MP|STATION-FLOOR";
mapStates.Remove(oldId);
var mapPlan = Run(mapFixture, bcrsManifest, mapStates);
Check(!mapPlan.Records.ContainsKey("MP|STATION-FLOOR"), "Unchanged map part acquired unnecessary saved state");
var localKioskFixture = Read(args[0], "Station_MLAB");
localKioskFixture["strRegID"] = "MLAB";
var localKioskStates = Records(localKioskFixture);
var mlabManifest = manifests.OfType<JObject>().Single(row => (string)row["station"] == "MLAB");
var mlabSite = (JObject)mlabManifest["replace"][0];
var oldKioskItem = ((JArray)localKioskFixture["aItems"]).OfType<JObject>().Single(part => Migration.Name(part) == Migration.Name(mlabSite) &&
    Math.Abs((float)part["fX"] - (float)mlabSite["fX"]) < .01 && Math.Abs((float)part["fY"] - (float)mlabSite["fY"]) < .01);
var localKioskState = Migration.Clone(localKioskStates[Migration.Id(oldKioskItem)]);
localKioskState["aConds"] = new JArray("DEFAULT", "StatDamage=1.0x3");
localKioskFixture["aCOs"] = new JArray(localKioskState);
var localPlan = Run(localKioskFixture, manifests.OfType<JObject>().Single(row => (string)row["station"] == "MLAB"), localKioskStates);
Check((string)localPlan.Updated["aCOs"][0]["strCODef"] == "ItmKioskMLABScrap01" &&
    Catalog.Strings(localPlan.Updated["aCOs"][0]["aConds"]).Contains("StatDamage=1.0x3"), "Local record was not preserved and upgraded");

// Reserve every clear floor cell with a loose 1x1 item. A new conflict must
// still complete by overlapping loose items instead of deferring the upgrade.
var crowded = Read(args[0], (string)bcrsManifest["template"]);
crowded["strRegID"] = "BCRS";
var crowdedStates = Records(crowded);
var structural = new Grid(assets);
foreach (var part in ((JArray)crowded["aItems"]).OfType<JObject>().Where(Migration.Root))
    if (assets.Installed(Migration.Name(part), crowdedStates[Migration.Id(part)])) structural.Add(part);
string looseName = "ItmWall1x1Loose";
Check(!assets.Installed(looseName, null) && new Footprint(assets.Item(looseName), 0) is { Width: 1, Height: 1 },
    "Crowded-floor fixture must use a real loose 1x1 native item");
int serial = 0;
foreach (var cell in structural.Floors)
{
    string id = "CROWD-" + serial++;
    ((JArray)crowded["aItems"]).Add(new JObject { ["strID"] = id, ["strName"] = looseName, ["fX"] = cell.X, ["fY"] = cell.Y, ["fRotation"] = 0 });
    crowdedStates[id] = new JObject { ["strID"] = id, ["strCODef"] = looseName, ["aConds"] = new JArray("DEFAULT") };
}
var crowdedPlan = Run(crowded, bcrsManifest, crowdedStates);
Check(crowdedPlan.Stacked > 0, "Crowded-floor case did not use overlap fallback");

if (args.Length > 3)
{
    using var archive = ZipFile.OpenRead(args[3]);
    var physicalIds = manifests.OfType<JObject>().Select(row => (string)row["station"]).Distinct().ToArray();
    var stations = archive.Entries.Where(entry => physicalIds.Any(id => entry.FullName == "ships/" + id + ".json"))
        .Select(entry => { using var reader = new StreamReader(entry.Open()); return (JObject)JArray.Parse(reader.ReadToEnd()).Single(); }).ToArray();
    var wanted = stations.SelectMany(station => ((JArray)station["aItems"]).OfType<JObject>()).Select(Migration.Id).ToHashSet();
    var saved = new Dictionary<string, JObject>();
    var root = archive.Entries.Where(entry => !entry.FullName.StartsWith("ships/") && entry.FullName.EndsWith(".json"))
        .OrderByDescending(entry => entry.Length).First();
    using var rootText = new StreamReader(root.Open());
    using var json = new JsonTextReader(rootText) { DateParseHandling = DateParseHandling.None };
    while (json.Read())
    {
        if (json.TokenType != JsonToken.PropertyName || json.Depth != 2) continue;
        string key = (string)json.Value;
        json.Read();
        if (key != "aCOs") { json.Skip(); continue; }
        while (json.Read() && json.TokenType != JsonToken.EndArray)
        {
            int depth = json.Depth;
            var record = new JObject();
            string id = null;
            while (json.Read() && !(json.TokenType == JsonToken.EndObject && json.Depth == depth))
            {
                if (json.TokenType != JsonToken.PropertyName) continue;
                string field = (string)json.Value;
                json.Read();
                if (field == "strID") { id = (string)json.Value; record[field] = id; }
                else if (id == null || wanted.Contains(id)) record[field] = JToken.ReadFrom(json);
                else json.Skip();
            }
            if (id != null && wanted.Contains(id)) saved[id] = record;
        }
    }
    var defaults = AssetFiles.Load(args[1], "star_systems")["NewGame"];
    var lookup = ((JArray)defaults["aSpawnStations"]).OfType<JObject>().ToDictionary(row => (string)row["strName"], row => (string)row["strShipType"]);
    foreach (var station in stations)
    {
        string resolved = lookup.GetValueOrDefault((string)station["strName"], (string)station["strName"]);
        var variant = manifests.OfType<JObject>().Where(row => (string)row["station"] == (string)station["strRegID"]).ToArray();
        var manifest = variant.FirstOrDefault(row => (string)row["template"] == resolved) ?? variant.First();
        var plan = Run(station, manifest, saved);
        var originalIds = ((JArray)station["aItems"]).OfType<JObject>().Select(Migration.Id).ToHashSet();
        var added = ((JArray)plan.Updated["aItems"]).OfType<JObject>().Where(part => !originalIds.Contains(Migration.Id(part))).ToArray();
        if (added.Length > 1)
        {
            var kiosk = added.Single(part => Migration.Name(part).EndsWith("Scrap01"));
            var nearby = ((JArray)station["aItems"]).OfType<JObject>().Where(Migration.Root)
                .Where(part => Math.Abs((float)part["fX"] - (float)kiosk["fX"]) < 5 && Math.Abs((float)part["fY"] - (float)kiosk["fY"]) < 5).ToArray();
            Directory.CreateDirectory("obj/Compatibility-1.1.0.1");
            File.WriteAllText("obj/Compatibility-1.1.0.1/station-support-" + station["strRegID"] + ".json",
                new JObject { ["added"] = new JArray(added), ["existingNearSite"] = new JArray(nearby),
                    ["alignment"] = JObject.FromObject(Migration.Alignment(station, manifest)) }.ToString());
        }
    }
}
Console.WriteLine("PASS: " + checks + " station migration assertions.");
return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error);
    return 1;
}
