using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Ionic.Zip;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ostrabus.BoneyardTurnover;
using SolarSystem;

internal static partial class Program
{
    private static void AssignPhobos(string[] args)
    {
        if (args.Length != 4) throw new ArgumentException("assign-phobos <current.zip> <world-audit.json> <content-audit.json> <new-copy.zip>");
        string source = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[3]);
        if (File.Exists(output) || source == output) throw new IOException("Output must be a new copy.");
        string sourceHash = Hash(source);
        var header = JObject.Parse(File.ReadAllText(args[1]));
        var content = JObject.Parse(File.ReadAllText(args[2]));
        if ((string)header["archiveSHA256"] != sourceHash || (string)content["archiveSHA256"] != sourceHash)
            throw new InvalidDataException("Audits do not match the current archive.");
        var game = header["fields"].ToObject<JsonGameSave>();
        if (game.strPlayerCO == null || game.objAIShipManager.strATCLast == "MTRS") throw new InvalidDataException("Incomplete header or player is at Phobos.");
        using var zip = ZipFile.Read(source);
        var state = ReadObject(zip[Entry]);
        string seed = (string)ReadArray(zip["saveInfo.json"])[0]["seedId"];
        Validate(state, game, seed);
        var fields = (JArray)state["Fields"];
        if (fields.Any(x => (string)x["Region"] == "MTRS")) throw new InvalidDataException("Phobos record already exists; refusing overwrite.");
        var preserved = fields.DeepClone();
        string data = Ostrabus.Development.InstallationPaths.Data;
        DataHandler.dictCOs = JsonConvert.DeserializeObject<JsonCondOwner[]>(File.ReadAllText(Path.Combine(data, "condowners", "condowners.json"))).ToDictionary(x => x.strName);
        DataHandler.dictCOOverlays = JsonConvert.DeserializeObject<JsonCOOverlay[]>(File.ReadAllText(Path.Combine(data, "cooverlays", "cooverlays.json"))).ToDictionary(x => x.strName);
        var station = content["physical"].Single(x => (string)x["id"] == "MTRS");
        int map = (int)station["mapID"];
        if (map != 1002) throw new InvalidDataException("Reviewed Phobos map changed.");
        var definition = JsonConvert.DeserializeObject<JsonEnvironmentMap[]>(File.ReadAllText(Path.Combine(data, "blueprints", "environments", "environments.json"))).Single(x => x.strName == "1002");
        var ring = definition.aSpawnDerelictRings.Single();
        if (ring.strSpawnAroundBody != "Phobos" || ring.strOwner != "Xinhua" || ring.strLootShipType != "RandomDerelictMTRS_Phobos" || !ring.bIsBodyLocked || ring.bOrbitBO)
            throw new InvalidDataException("Phobos definition differs from reviewed scope.");
        var world = new StarSystem();
        CrewSim.system = world;
        StarSystem.fEpoch = game.objSystem.dfEpoch;
        foreach (var body in game.objSystem.aBOs) world.aBOs.Add(body.strName, new BodyOrbit(body));
        foreach (var body in world.aBOs.Values) if (body.strName != "Sol") body.boParent = world.aBOs["Sol"];
        foreach (string text in game.objSystem.dictBOHierarchy)
        {
            var pair = text.Split('=');
            if (pair.Length == 2 && world.aBOs.TryGetValue(pair[0], out var child) && world.aBOs.TryGetValue(pair[1], out var parent)) child.boParent = parent;
        }
        var anchor = world.aBOs["Phobos"];
        anchor.UpdateTime(StarSystem.fEpoch);
        var fieldType = typeof(Plugin).Assembly.GetType("Ostrabus.BoneyardTurnover.Field");
        var field = Activator.CreateInstance(fieldType, true);
        void Set(string name, object value) => fieldType.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(field, value);
        Set("Anchor", anchor); Set("Center", anchor.vPos);
        Set("InnerKm", anchor.fRadiusKM + ring.fSpawnRadiusMinKM); Set("OuterKm", anchor.fRadiusKM + ring.fSpawnRadiusMaxKM);
        bool Contains(ShipSitu situ) => (bool)fieldType.GetMethod("Contains", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(field, new object[] { situ });
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var excluded = new JArray();
        foreach (var hull in content["physical"].Where(x => (int?)x["mapID"] == map && (int?)x["classification"] == 0 && (string)x["owner"] == ring.strOwner))
        {
            string id = (string)hull["id"];
            var raw = ReadHull(zip["ships/" + id + ".json"]);
            var situ = new ShipSitu(raw["objSS"].ToObject<JsonShipSitu>());
            bool safe = (bool?)raw["bAIShip"] != true && !(raw["aCrew"] as JArray ?? new JArray()).Any() &&
                raw["aDocked"]?.HasValues != true && Condition(raw["shipCO"], "IsEnvironmentEntity") > 0 &&
                (raw["aShallowPSpecs"] as JArray ?? new JArray()).All(x => DataHandler.GetCondOwnerDef((string)x["strName"])?.strName == "SysLootSpawner");
            if (safe && Contains(situ)) ids.Add(id);
            else excluded.Add(new JObject { ["id"] = id, ["reason"] = safe ? "Outside authored bounds" : "Uncertain saved occupancy or attachment" });
        }
        var owners = Owners(game.objSystem.dictShipOwners);
        foreach (var wreck in game.objSystem.aStellarDerelicts.Where(x => x.nMapID == map && x.strOwner == ring.strOwner))
        {
            if (owners.TryGetValue(wreck.strID, out string owner) && owner != ring.strOwner) continue;
            if (Contains(new StellarDerelict(wreck).objSS)) ids.Add(wreck.strID);
            else excluded.Add(new JObject { ["id"] = wreck.strID, ["reason"] = "Outside authored bounds" });
        }
        if (ids.Count != 10 || excluded.Count != 1 || (string)excluded[0]["id"] != "O-8MCY")
            throw new InvalidDataException("The audited Phobos cohort changed; inspect instead of guessing.");
        var ages = new JObject();
        foreach (string id in ids.OrderBy(x => x, StringComparer.Ordinal)) ages[id] = 0d;
        string hash = (string)fieldType.GetMethod("Hash", StaticInternal).Invoke(null, new object[] { "1002:Phobos:" + JsonConvert.SerializeObject(ring) });
        fields.Add(new JObject
        {
            ["Key"] = "MTRS|MTRS|RandomDerelictMTRS_Phobos|Phobos", ["Region"] = "MTRS",
            ["Definition"] = hash, ["Away"] = true, ["Departure"] = StarSystem.fEpoch - 8 * 86400d,
            ["HalfLife"] = 7d, ["Shape"] = 1d, ["Seed"] = Guid.NewGuid().GetHashCode(), ["Ages"] = ages
        });
        Validate(state, game, seed);
        if (!JToken.DeepEquals(new JArray(fields.Take(fields.Count - 1)), preserved)) throw new InvalidDataException("Existing field state changed.");
        zip.UpdateEntry(Entry, System.Text.Encoding.UTF8.GetBytes(state.ToString(Formatting.None)));
        using (var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write)) zip.Save(stream);
        using var result = ZipFile.Read(output);
        Validate(ReadObject(result[Entry]), game, seed);
        if (Hash(source) != sourceHash) throw new IOException("Original changed while preparing.");
        var report = new JObject
        {
            ["currentSourceSHA256"] = sourceHash, ["repairedSHA256"] = Hash(output),
            ["region"] = "MTRS", ["creditedHours"] = 192, ["assignedDays"] = 8,
            ["departureBasis"] = "User-requested eight-day starting absence; not reconstructed history.",
            ["candidateIds"] = new JArray(ids), ["excluded"] = excluded, ["fieldDefinition"] = hash,
            ["preservedExistingFields"] = fields.Count - 1, ["productionReaderAccepted"] = true,
            ["originalsUnchanged"] = true, ["nativePayloadsHashVerified"] = 0,
            ["changedEntry"] = Entry, ["installed"] = false,
            ["mercury"] = "Not changed: saved orbiting field does not fit the fixed-ring model."
        };
        File.WriteAllText(output + ".report.json", report.ToString());
        Console.WriteLine(report.ToString());
        Console.WriteLine("Run SaveContentAudit --verify-native-copy before applying; native payload verification is pending.");
    }
}
