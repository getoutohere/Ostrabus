using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ostranauts.Trading;
using Ostrabus.StationMigration;
using UnityEngine;

internal static class Program
{
    static int checks, partCalls;
    static string restored;
    static void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
    static bool CaptureWorld(ref IEnumerator __result) { __result = Array.Empty<object>().GetEnumerator(); return false; }
    static bool CapturePart(ref GameObject __result) { partCalls++; __result = null; return false; }
    static bool CaptureFactory(string strCO, JsonCondOwnerSave jcos, ref CondOwner __result)
    {
        if (jcos == null) return true;
        restored = strCO;
        __result = null;
        return false;
    }
    static bool SuppressWarning() => false;
    static int Main(string[] args)
    {
        var harmony = new Harmony("ostrabus.offline.station-migration-native");
        try
        {
            string oldData = args[0], data = args[1];
            Check(Plugin.RespawnSitesFor(Plugin.NativeHash1101) == 1, "1.1.0.1 native migration contract changed");
            Check(Plugin.RespawnSitesFor(Plugin.NativeHash1102) == 2, "1.1.0.2 native migration contract changed");
            Check(Plugin.RespawnSitesFor("unreviewed") == 0, "Unknown native assembly was admitted");
            Dictionary<string, T> Load<T>(string directory) => AssetFiles.Load(data, directory)
                .ToDictionary(pair => pair.Key, pair => pair.Value.ToObject<T>());
            DataHandler.dictCOs = Load<JsonCondOwner>("condowners");
            DataHandler.dictCOOverlays = Load<JsonCOOverlay>("cooverlays");
            DataHandler.dictItemDefs = Load<JsonItemDef>("items");
            DataHandler.dictLoot = Load<Loot>("loot");
            DataHandler.dictInstallables = Load<JsonInstallable>("installables");
            DataHandler.dictInteractions = Load<JsonInteraction>("interactions");
            DataHandler.dictDataCOs = new Dictionary<string, DataCO>();
            DataHandler.dictStarSystems = Load<JsonStarSystemSave>("star_systems");
            DataHandler.dictCOSaves = new Dictionary<string, JsonCondOwnerSave>();
            DataHandler.mapCOs = new Dictionary<string, CondOwner>();
            foreach (int width in new[] { 1, 3, 5, 7 })
            {
                var samples = Enumerable.Range(0, width * 3).Select(index => index.ToString()).ToArray();
                Check(Footprint.Rotate(samples, width).SequenceEqual(TileUtils.RotateTilesCW(samples.ToList(), width)),
                    "Socket rotation differs from native TileUtils");
            }
            var init = AccessTools.Method(typeof(StarSystem), nameof(StarSystem.Init), new[] { typeof(JsonStarSystemSave), typeof(JsonShip[]) });
            harmony.Patch(init, prefix: new HarmonyMethod(typeof(Program), nameof(CaptureWorld)));

            var readInstructions = typeof(PatchProcessor).GetMethods().Single(method => method.Name == "GetOriginalInstructions" &&
                method.GetParameters().Length == 2 && !method.GetParameters()[1].ParameterType.IsByRef);
            var originalInstructions = ((IEnumerable<CodeInstruction>)readInstructions.Invoke(null,
                new object[] { LoaderPatch.TargetMethod(), null })).ToArray();
            var respawn = AccessTools.Method(typeof(DebugRespawnShip), nameof(DebugRespawnShip.RespawnShip));
            int oldRespawns = originalInstructions.Count(instruction => instruction.Calls(respawn));
            var transformed = LoaderPatch.Transpiler(originalInstructions.Select(instruction => new CodeInstruction(instruction))).ToArray();
            Check(transformed.Count(instruction => instruction.Calls(respawn)) == oldRespawns - Plugin.NativeRespawnSites, "Other native respawn migrations changed");
            Check(transformed.Count(instruction => instruction.Calls(AccessTools.Method(typeof(LoaderPatch), nameof(LoaderPatch.Init)))) == 1,
                "Native system-init call not intercepted once");
            harmony.CreateClassProcessor(typeof(LoaderPatch)).Patch();
            Check(Harmony.GetPatchInfo(LoaderPatch.TargetMethod()).Transpilers.Any(patch => patch.owner == harmony.Id), "Actual Harmony binding failed");

            using var manifestStream = typeof(Plugin).Assembly.GetManifestResourceStream("StationMigration.Manifest");
            using var manifestReader = new StreamReader(manifestStream);
            var manifest = (JArray)JObject.Parse(manifestReader.ReadToEnd())["templates"];
            var nativeSpawns = DataHandler.dictStarSystems["NewGame"].aSpawnStations.ToDictionary(value => value.strName);
            foreach (var group in manifest.OfType<JObject>().Where(row => row["kiosk"]?.Type == JTokenType.Object)
                .GroupBy(row => (string)row["station"]))
                Check(nativeSpawns.TryGetValue(group.Key, out var spawnDefinition) &&
                    group.Any(row => (string)row["template"] == spawnDefinition.strShipType),
                    "Authored kiosk manifest is attached to the wrong physical station: " + group.Key);
            var ships = new List<JsonShip>();
            var stationIds = new[] { "BCER", "BCRS", "EJDR", "HQCH", "JATL", "JATL_SUB", "JFTS", "MLAB", "MSUZ", "MSUZ_RB", "SVIR" };
            foreach (string id in stationIds)
            {
                string template = nativeSpawns[id].strShipType;
                var fixture = (JObject)JArray.Parse(File.ReadAllText(Path.Combine(oldData, "ships", template + ".json"))).Single();
                // Template GUIDs are not world identities. Materialize distinct
                // saved fixtures instead of pretending copied blueprint IDs
                // and template-only CO payloads form an existing saved world.
                fixture["aCOs"] = null;
                var ids = ((JArray)fixture["aItems"]).OfType<JObject>().Select(part => (string)part["strID"])
                    .Distinct().ToDictionary(value => value, value => Guid.NewGuid().ToString());
                foreach (var token in fixture.Descendants().OfType<JValue>().Where(token => token.Type == JTokenType.String).ToArray())
                    if (ids.TryGetValue((string)token, out string replacement)) token.Value = replacement;
                var station = fixture.ToObject<JsonShip>();
                station.strRegID = id;
                station.shipCO.strID = "CO-" + id;
                station.shipCO.strCondID = id;
                station.shipCO.strRegIDLast = id;
                ships.Add(station);
                foreach (var part in station.aItems)
                    DataHandler.dictCOSaves[part.strID] = new JsonCondOwnerSave
                    {
                        strID = part.strID, strCODef = part.strName, strRegIDLast = id,
                        aConds = new[] { "DEFAULT" }, aCondRules = new[] { "DEFAULT" }, bAlive = true
                    };
            }
            // Non-target ship keeps every reference, including docking/motion.
            var control = new JsonShip { strRegID = "PLAYER", aItems = Array.Empty<JsonItem>(), shipCO = new JsonCondOwnerSave() };
            ships.Add(control);
            var array = ships.ToArray();
            var untouched = ships.Select(ship => new object[] { ship.aDocked, ship.aSecuredTowBraces, ship.objSS, ship.aCrew, ship.aRooms, ship.aZones }).ToArray();
            var parentItems = ships.Where(ship => ship.strRegID == "JATL" || ship.strRegID == "MSUZ")
                .ToDictionary(ship => ship.strRegID, ship => JsonConvert.SerializeObject(ship.aItems));
            CrewSim.aPatchesApplied = new List<string>();
            CrewSim.aSaveVersion = new[] { 1, 0, 1, 10 };
            var world = (StarSystem)FormatterServices.GetUninitializedObject(typeof(StarSystem));
            const double epoch = 60000000000;
            LoaderPatch.Init(world, new JsonStarSystemSave { dfEpoch = epoch }, array);
            Check(CrewSim.aPatchesApplied.SequenceEqual(new[] { Migration.Marker }), "Native global marker not set exactly once");
            Check(ReferenceEquals(array.Last(), control), "Non-station ship was replaced");
            foreach (var pair in parentItems)
                Check(JsonConvert.SerializeObject(ships.Single(ship => ship.strRegID == pair.Key).aItems) == pair.Value,
                    "Unauthored parent interior was changed: " + pair.Key);
            foreach (string id in Migration.CorrectedTargets)
            {
                var station = ships.Single(ship => ship.strRegID == id);
                string wanted = id == "JATL_SUB" ? "ItmKioskJATLScrap01" : "ItmKioskMSUZScrap01";
                Check(station.aItems.Count(item => item.strName == wanted) == 1, "Missing authored substation kiosk: " + id);
            }
            Check(ships.Select((ship, index) => new object[] { ship.aDocked, ship.aSecuredTowBraces, ship.objSS, ship.aCrew, ship.aRooms, ship.aZones }
                .Zip(untouched[index], ReferenceEquals).All(equal => equal)).All(equal => equal),
                "Migration touched docking, kinematics, crew, rooms or zones");
            foreach (var station in ships.Where(ship => stationIds.Contains(ship.strRegID)))
            {
                Check(station.shipCO.aConds.Contains(Migration.Fixed + "=1.0x1"), "Station marker missing: " + station.strRegID);
                Check(ReferenceEquals(DataHandler.dictCOSaves[station.shipCO.strID], station.shipCO), "Station CO flag not synchronized");
            }

            var factory = AccessTools.Method(typeof(DataHandler), nameof(DataHandler.GetCondOwner), new[]
            {
                typeof(string), typeof(string), typeof(string), typeof(bool), typeof(string), typeof(JsonCondOwnerSave), typeof(string), typeof(Transform)
            });
            harmony.Patch(factory, prefix: new HarmonyMethod(typeof(Program), nameof(CaptureFactory)));
            harmony.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.CreatePart)), prefix: new HarmonyMethod(typeof(Program), nameof(CapturePart)));
            harmony.Patch(AccessTools.Method(typeof(Debug), nameof(Debug.LogWarning), new[] { typeof(object) }),
                prefix: new HarmonyMethod(typeof(Program), nameof(SuppressWarning)));
            var spawn = AccessTools.Method(typeof(Ship), "SpawnItems");
            var bcrs = ships.First(ship => ship.strRegID == "BCRS");
            var kiosk = bcrs.aItems.Single(item => item.strName == "ItmKioskBCRSScrap01");
            Check(DataHandler.dictCOSaves[kiosk.strID].fLastICOUpdate == epoch && DataHandler.dictCOSaves[kiosk.strID].fEpoch == epoch,
                "New kiosk would catch up from epoch zero");
            using (var clock = new Condowner.Utils.Clock())
            {
                clock.SetData(DataHandler.dictCOSaves[kiosk.strID]);
                Check(clock.Epoch == epoch && clock.Rate == 1, "Native clock rejected the new kiosk epoch");
            }
            restored = null;
            DataHandler.GetCondOwner(kiosk.strName, kiosk.strID, null, false);
            Check(restored == kiosk.strName, "Native factory restored wrong kiosk definition");
            var nativeShip = (Ship)FormatterServices.GetUninitializedObject(typeof(Ship));
            nativeShip.strRegID = "BCRS";
            partCalls = 0;
            spawn.Invoke(nativeShip, new object[] { new List<JsonItem> { kiosk }, false, Ship.Loaded.Edit,
                new Dictionary<string, CondOwner>(), new List<CondOwner>() });
            Check(partCalls == 1, "Native saved-item admission rejected migrated kiosk");

            // An old native/0.1.0 completion marker must not suppress these two
            // corrected physical targets. Previously processed stations remain
            // untouched, including a kiosk deliberately removed afterward.
            foreach (string id in Migration.CorrectedTargets)
            {
                var station = ships.Single(ship => ship.strRegID == id);
                string wanted = id == "JATL_SUB" ? "ItmKioskJATLScrap01" : "ItmKioskMSUZScrap01";
                station.aItems = station.aItems.Where(item => item.strName != wanted).ToArray();
                station.shipCO.aConds = station.shipCO.aConds.Where(value => value.Split('=')[0] != Migration.Fixed).ToArray();
            }
            bcrs.aItems = bcrs.aItems.Where(item => item.strName != "ItmKioskBCRSScrap01").ToArray();
            if (Plugin.NativeRespawnSites == 2) CrewSim.aSaveVersion = new[] { 1, 1, 0, 1 };
            LoaderPatch.Init(world, new JsonStarSystemSave { dfEpoch = epoch }, array);
            foreach (string id in Migration.CorrectedTargets)
            {
                var station = ships.Single(ship => ship.strRegID == id);
                string wanted = id == "JATL_SUB" ? "ItmKioskJATLScrap01" : "ItmKioskMSUZScrap01";
                Check(station.aItems.Count(item => item.strName == wanted) == 1 && Migration.Completed(station.shipCO.aConds),
                    "Previous global marker prevented correction for " + id);
            }
            Check(!bcrs.aItems.Any(item => item.strName == "ItmKioskBCRSScrap01"), "Correction restored a deliberately removed completed kiosk");
            Check(CrewSim.aPatchesApplied.Count(value => value == Migration.Marker) == 1, "Correction duplicated global native marker");
            string after = JsonConvert.SerializeObject(array.Select(NativeAdapter.View));
            LoaderPatch.Init(world, new JsonStarSystemSave(), array);
            Check(after == JsonConvert.SerializeObject(array.Select(NativeAdapter.View)), "Native marker did not suppress repeated migration");
            harmony.UnpatchSelf();
            Check(CrewSim.aPatchesApplied.Contains(Migration.Marker) &&
                !( !CrewSim.aPatchesApplied.Contains(Migration.Marker) && CrewSim.VersionIsOlderThan(CrewSim.aSaveVersion, new[] { 1, 1, 0, 1 }) ),
                "Native regeneration becomes eligible after plugin removal");
            Console.WriteLine("PASS: " + checks + " native station migration checks; game " + typeof(CrewSim).Assembly.ManifestModule.ModuleVersionId +
                ". Unity reconstruction/UI remain fixture boundaries.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { harmony.UnpatchSelf(); }
    }
}
