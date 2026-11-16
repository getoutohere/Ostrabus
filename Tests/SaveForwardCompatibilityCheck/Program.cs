using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ostrabus.SaveCompatibility;
using Ostranauts.Trading;

static partial class Program
{
    static int checks;
    static int randomReads;
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Check(bool value, string message)
    {
        checks++;
        if (!value)
            throw new Exception(message);
    }

    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result)
    {
        __result = ReferenceEquals(__0, __1);
        return false;
    }

    static bool Parent(ref bool __result)
    {
        __result = true;
        return false;
    }

    static bool Random(ref float __result)
    {
        randomReads++;
        __result = .5f;
        return false;
    }

    static bool Quiet() => false;
    static string data;
    static CondTrigger gate;
    static readonly Dictionary<string, Condition> conditions = new Dictionary<string, Condition>();
    static Condition Condition(string name)
    {
        if (!conditions.TryGetValue(name, out var result))
            conditions[name] = result = new Condition(DataHandler.dictConds[name]);
        return result;
    }

    static void Load()
    {
        data = Ostrabus.Development.InstallationPaths.Data;
        DataHandler.dictCOs = new Dictionary<string, JsonCondOwner>();
        foreach (string path in Directory.GetFiles(Path.Combine(data, "condowners"), "*.json", SearchOption.AllDirectories))
            foreach (var row in JArray.Parse(File.ReadAllText(path)))
            {
                var definition = row.ToObject<JsonCondOwner>();
                DataHandler.dictCOs[definition.strName] = definition;
            }

        DataHandler.dictConds = new Dictionary<string, JsonCond>();
        DataHandler.dictCOOverlays = new Dictionary<string, JsonCOOverlay>();
        foreach (string path in Directory.GetFiles(Path.Combine(data, "cooverlays"), "*.json")
            .Concat(new[] { Path.Combine("NavConsole", "Rotation", "Assets", "data", "cooverlays", "rotation.json") }))
            foreach (var row in JArray.Parse(File.ReadAllText(path)))
            {
                var overlay = new JsonCOOverlay
                {
                    strName = (string)row["strName"],
                    strCOBase = (string)row["strCOBase"]
                };
                DataHandler.dictCOOverlays[overlay.strName] = overlay;
            }

        foreach (string path in Directory.GetFiles(Path.Combine(data, "conditions"), "*.json", SearchOption.AllDirectories))
            foreach (var row in JArray.Parse(File.ReadAllText(path)))
            {
                var definition = row.ToObject<JsonCond>();
                DataHandler.dictConds[definition.strName] = definition;
            }

        foreach (var table in JArray.Parse(File.ReadAllText(Path.Combine(data, "conditions_simple", "conditions_simple.json"))))
        {
            var values = table["aValues"];
            for (int i = 0; i < values.Count(); i += 7)
            {
                string name = (string)values[i];
                DataHandler.dictConds[name] = new JsonCond
                {
                    strName = name,
                    strNameFriendly = (string)values[i + 1],
                    bInvert = bool.Parse((string)values[i + 6])
                };
            }
        }

        var raw = JArray.Parse(File.ReadAllText(Path.Combine(data, "condtrigs", "condtrigs.json"))).Single(row => (string)row["strName"] == "TIsRCSClusterAudioEmitter").ToObject<JsonCondTrigger>();
        gate = CondTrigger.CloneFromJSON(raw);
        DataHandler.dictLoot = Directory.GetFiles(Path.Combine(data, "loot"), "*.json").SelectMany(path => JsonConvert.DeserializeObject<Loot[]>(File.ReadAllText(path))).GroupBy(loot => loot.strName).ToDictionary(group => group.Key, group => group.Last());
        DataHandler.dictInteractions = Directory.GetFiles(Path.Combine(data, "interactions"), "*.json").SelectMany(path => JsonConvert.DeserializeObject<JsonInteraction[]>(File.ReadAllText(path))).GroupBy(row => row.strName).ToDictionary(group => group.Key, group => group.Last());
        DataHandler.dictItemDefs = new Dictionary<string, JsonItemDef>();
        DataHandler.dictDataCOs = DataHandler.dictCOs.ToDictionary(pair => pair.Key, pair => new DataCO(pair.Value));
        foreach (var row in DataHandler.dictDataCOs.Values.ToArray())
            if (row.JCO.aStartingConds?.Any(value => value.StartsWith("IsRCSCluster=", StringComparison.Ordinal)) == true)
                row.GetMaxHealth();
        randomReads = 0; // Native asset preparation precedes compatibility loading.
    }

    static JsonCondOwner Definition(string name) => DataHandler.GetCondOwnerDef(name);
    static void Eligibility()
    {
        Check(LegacyRcsCaches.Supported(gate), "Native asset no longer fits the supported projection");
        var names = new[]
        {
            "IsShipSpecialItem",
            "IsRCSCluster",
            "IsInstalled",
            "IsOff"
        };
        // Compare the actual native gate and outer native HasCond guard against
        // the data adapter, including present zero/negative counts.
        for (int mask = 0; mask < 256; mask++)
        {
            var values = new Dictionary<string, double>();
            var co = Empty<CondOwner>();
            co.mapConds = new Dictionary<string, Condition>();
            int cursor = mask;
            foreach (string name in names)
            {
                int state = cursor % 4;
                cursor /= 4;
                if (state == 0)
                    continue;
                double amount = state == 1 ? 0 : state == 2 ? -1 : 2;
                values[name] = amount;
                var condition = new Condition(DataHandler.dictConds[name]);
                condition.fCount = amount;
                co.mapConds[name] = condition;
            }

            bool native = co.HasCond("IsShipSpecialItem") && gate.Triggered(co, logOutcome: false);
            Check(LegacyRcsCaches.Eligible(gate, values) == native, "Eligibility differs from native at mask " + mask);
        }
    }

    static JsonShip ReadShip(JToken raw)
    {
        return new JsonShip
        {
            strRegID = (string)raw["strRegID"],
            nRCSCount = (float? )raw["nRCSCount"] ?? 0,
            fLastVisit = (double? )raw["fLastVisit"] ?? 0,
            nRCSCountRaw = (int? )raw["nRCSCountRaw"] ?? 0,
            nRCSExhaustV = (float? )raw["nRCSExhaustV"] ?? 0,
            aItems = raw["aItems"].Select(item => new JsonItem { strName = (string)item["strName"], strID = (string)item["strID"], bForceLoad = (bool? )item["bForceLoad"], aCondOverrides = item["aCondOverrides"]?.ToObject<DataCond[]>() }).ToArray(),
            aCOs = raw["aCOs"].Select(co => new JsonCondOwnerSave { strID = (string)co["strID"], strCODef = (string)co["strCODef"], aConds = co["aConds"]?.ToObject<string[]>() }).ToArray()
        };
    }

    static void RealSave(string folder)
    {
        foreach (string id in new[]
        {
            "O-E3SE",
            "O-Y2L"
        }

        )
        {
            var raw = JArray.Parse(File.ReadAllText(Path.Combine(folder, "pre-sff-save-" + id + ".json")))[0];
            var saved = ReadShip(raw);
            var map = saved.aCOs.ToDictionary(co => co.strID);
            Check(LegacyRcsCaches.Missing(saved), "Actual legacy cache fixture is not missing");
            bool ok = LegacyRcsCaches.TryDerive(saved, gate, key => map.TryGetValue(key, out var co) ? co : null, Definition, Condition, out var result, out string reason);
            Check(ok, id + " failed: " + reason);
            Check(result.Count > 0 && result.Exhaust > 0, "Reconstruction has no working propulsion");
            NativeMovement(saved, result);
            Console.WriteLine(id + " derived raw=" + result.Count + " exhaust=" + result.Exhaust + " weighted=" + result.Strength);
        }
    }

    static void Archive(string path, string globalPath = null)
    {
        var global = globalPath == null ? new Dictionary<string, JsonCondOwnerSave>() : JObject.Parse(File.ReadAllText(globalPath))["selectedCOs"].Select(co => new JsonCondOwnerSave { strID = (string)co["strID"], strCODef = (string)co["strCODef"], aConds = co["aConds"]?.ToObject<string[]>() }).ToDictionary(co => co.strID);
        DataHandler.dictCOSaves = global;
        var prefix = AccessTools.Method(typeof(LegacyRcsLoadingPatch), "Prefix");
        using (var reader = File.OpenText(path))
        using (var json = new JsonTextReader(reader))
        {
            json.Read();
            int restored = 0, unchanged = 0, failed = 0;
            double derivationMs = 0;
            while (json.Read() && json.TokenType != JsonToken.EndArray)
            {
                JsonShip saved = ReadShip(JObject.Load(json));
                if (!LegacyRcsCaches.Missing(saved))
                {
                    unchanged++;
                    continue;
                }

                var map = saved.aCOs.ToDictionary(co => co.strID);
                var hull = Empty<Ship>();
                hull.strRegID = saved.strRegID;
                hull.json = saved;
                var timer = System.Diagnostics.Stopwatch.StartNew();
                bool ok = LegacyRcsCaches.TryDerive(saved, gate, id => map.TryGetValue(id, out var co) ? co : global.TryGetValue(id, out co) ? co : null, Definition, Condition, out var result, out string reason, saved.fLastVisit == 0, item => LegacyRcsLoadingPatch.ItemDestroyed(hull, item));
                derivationMs += timer.Elapsed.TotalMilliseconds;
                if (!ok)
                {
                    failed++;
                    Console.WriteLine("Archive " + saved.strRegID + ": " + reason + "; saved=" + saved.nRCSCount + " derived=" + result.Strength + " raw=" + result.Count);
                    continue;
                }

                float originalThrust = saved.nRCSCount;
                // Match StarSystem.Init's actual saved-hull arguments.
                prefix.Invoke(null, new object[] { hull, false, Ship.Loaded.Shallow });
                Check(saved.nRCSCountRaw == result.Count && saved.nRCSExhaustV == result.Exhaust
                    && saved.nRCSCount == originalThrust, saved.strRegID + ": production load prefix disagrees with derivation");
                restored++;
            }

            Console.WriteLine("Archive: reconstructed " + restored + " legacy hulls; left " + unchanged + " other hulls alone; derivation " + derivationMs.ToString("F1") + " ms (excludes fixture parsing).");
            Check(failed == 0, "Archive has " + failed + " unreconstructed inputs; investigate rather than guessing");
        }
    }

    static void Cases()
    {
        var first = new JsonCondOwner
        {
            strName = "fixture-strong",
            aStartingConds = new[]
            {
                "IsShipSpecialItem=1.0x1",
                "IsRCSCluster=1.0x1",
                "IsInstalled=1.0x1",
                "StatThrustStrength=1.0x2",
                "StatThrustExhaustV=1.0x0.9"
            }
        };
        var second = new JsonCondOwner
        {
            strName = "fixture-efficient",
            aStartingConds = new[]
            {
                "IsShipSpecialItem=1.0x1",
                "IsRCSCluster=1.0x1",
                "IsInstalled=1.0x1",
                "StatThrustStrength=1.0x1",
                "StatThrustExhaustV=1.0x2"
            }
        };
        DataHandler.dictCOs[first.strName] = first;
        DataHandler.dictCOs[second.strName] = second;
        var saved = new JsonShip
        {
            strRegID = "fixture",
            nRCSCount = 3,
            aItems = new[]
            {
                new JsonItem
                {
                    strID = "one",
                    strName = first.strName
                },
                new JsonItem
                {
                    strID = "two",
                    strName = second.strName
                },
                new JsonItem
                {
                    strID = "off",
                    strName = first.strName
                },
                new JsonItem
                {
                    strID = "loose",
                    strName = first.strName
                },
                new JsonItem
                {
                    strID = "one",
                    strName = first.strName
                }
            }
        };
        var records = new Dictionary<string, JsonCondOwnerSave>
        {
            ["off"] = new JsonCondOwnerSave
            {
                strID = "off",
                strCODef = first.strName,
                aConds = new[]
                {
                    "IsOff=1.0x1",
                    "DEFAULT"
                }
            },
            ["loose"] = new JsonCondOwnerSave
            {
                strID = "loose",
                strCODef = first.strName,
                aConds = Array.Empty<string>()
            }
        };
        bool Derive(out LegacyRcsCaches.Result result, out string reason) => LegacyRcsCaches.TryDerive(saved, gate, id => records.TryGetValue(id, out var co) ? co : null, Definition, Condition, out result, out reason);
        Check(Derive(out var output, out string why), "Mixed package failed: " + why);
        Check(output.Count == 2 && output.Strength == 3 && Math.Abs(output.Exhaust - 2.9) < .000001, "Weighted thrust was mistaken for raw count/exhaust");
        Check(saved.nRCSCountRaw == 0 && saved.nRCSExhaustV == 0, "Derivation mutated its input");
        saved.nRCSCount = 4;
        saved.aItems[0].aCondOverrides = new[]
        {
            new DataCond
            {
                CondName = "StatThrustStrength",
                Amount = 3,
                Chance = 1
            }
        };
        Check(Derive(out output, out why) && output.Count == 2 && output.Strength == 4, "Native item override order was lost");
        saved.aItems[0].aCondOverrides = null;
        saved.nRCSCount = 3;
        first.aStartingConds[3] = "StatThrustStrength=0.5x2";
        Check(!Derive(out _, out _), "Ambiguous probability was rerolled/guessed");
        first.aStartingConds[3] = "StatThrustStrength=1.0x1-2";
        Check(!Derive(out _, out _), "Random range was rerolled/guessed");
        first.aStartingConds[3] = "StatThrustStrength=1.0x2";
        saved.nRCSCount = 3;
        saved.aItems[0].bForceLoad = true;
        Check(!Derive(out output, out _) && output.NativeRebuild, "Force-loaded stats would have been counted twice");
        saved.aItems[0].bForceLoad = null;
        var legacy = new JsonCondOwner
        {
            strName = "fixture-legacy",
            aStartingConds = new[]
            {
                "IsShipSpecialItem=1.0x1",
                "IsRCSCluster=1.0x1",
                "IsInstalled=1.0x1",
                "StatThrustStrength=1.0x1",
                "StatThrustExhaustV=1.0x1"
            }
        };
        DataHandler.dictCOs[legacy.strName] = legacy;
        var combat = new JsonShip
        {
            nRCSCount = 2,
            aItems = new[]
            {
                new JsonItem
                {
                    strID = "A",
                    strName = legacy.strName,
                    aCondOverrides = new[]
                    {
                        new DataCond
                        {
                            CondName = "StatDamage",
                            Amount = 1,
                            Chance = 1
                        }
                    }
                },
                new JsonItem
                {
                    strID = "B",
                    strName = legacy.strName
                },
                new JsonItem
                {
                    strID = "C",
                    strName = legacy.strName
                }
            }
        };
        Check(LegacyRcsCaches.TryDerive(combat, gate, id => null, Definition, Condition, out output, out why) && output.Count == 2 && output.Exhaust == 2 && output.PreservedShallowDamage, "Homogeneous shallow damage was healed or guessed incorrectly");
        combat.aItems[1].strName = second.strName;
        Check(!LegacyRcsCaches.TryDerive(combat, gate, id => null, Definition, Condition, out _, out _), "Mixed exhausts had an ambiguous damaged subset invented");
        combat.aItems[1].strName = legacy.strName;
        combat.aItems[0].aCondOverrides = null;
        Check(!LegacyRcsCaches.TryDerive(combat, gate, id => null, Definition, Condition, out _, out _), "Unexplained cache inconsistency was accepted");
        Check(!LegacyRcsCaches.TryDerive(combat, gate, id => null, Definition, Condition, out _, out _, false), "Missing saved parts were resurrected as a template");
        var nativeHull = Empty<Ship>();
        nativeHull.strRegID = "damaged-parts";
        var destroyed = new JsonItem
        {
            strID = "destroyed",
            strName = "ItmRCSCluster01",
            aCondOverrides = new[]
            {
                new DataCond
                {
                    CondName = "StatDamage",
                    Amount = 999,
                    Chance = 1
                }
            }
        };
        Check(LegacyRcsLoadingPatch.ItemDestroyed(nativeHull, destroyed), "Native destroyed-part control was not exercised");
        var damageFixture = new JsonShip
        {
            nRCSCount = 1,
            aItems = new[]
            {
                destroyed,
                new JsonItem
                {
                    strID = "healthy",
                    strName = "ItmRCSCluster01"
                }
            }
        };
        Check(LegacyRcsCaches.TryDerive(damageFixture, gate, id => null, Definition, Condition, out output, out why, true, item => LegacyRcsLoadingPatch.ItemDestroyed(nativeHull, item)) && output.Count == 1, "Native fully-destroyed RCS was counted as recoverable propulsion");
        var cachedData = DataHandler.dictDataCOs["ItmRCSCluster01"];
        DataHandler.dictDataCOs.Remove("ItmRCSCluster01");
        bool coldRejected = false;
        try
        {
            LegacyRcsLoadingPatch.ItemDestroyed(nativeHull, destroyed);
        }
        catch (InvalidOperationException)
        {
            coldRejected = true;
        }
        finally
        {
            DataHandler.dictDataCOs["ItmRCSCluster01"] = cachedData;
        }

        Check(coldRejected, "Cold native loot expansion was allowed during compatibility loading");
        saved.nRCSCount = 99;
        Check(!Derive(out _, out _), "Conflicting old cache was silently rewritten");
        saved.nRCSCount = 3;
        var previous = gate.bAND;
        gate.bAND = false;
        Check(!Derive(out _, out _), "Unsupported native rule shape was overridden");
        gate.bAND = previous;
        Check(!LegacyRcsCaches.Missing(new JsonShip { nRCSCount = 3, nRCSCountRaw = 2, nRCSExhaustV = 2.9f }), "Valid 1.1 cache treated as legacy");
        Check(!LegacyRcsCaches.Missing(new JsonShip { nRCSCount = 3, nRCSCountRaw = 2 }), "Partial native cache treated as absent");
        Check(!LegacyRcsCaches.Missing(new JsonShip { nRCSCount = 0 }), "Unpowered hull treated as legacy thrust");
        DataHandler.dictCOSaves = new Dictionary<string, JsonCondOwnerSave>(records);
        DataHandler.dictCTs = new Dictionary<string, CondTrigger>
        {
            {
                "TIsRCSClusterAudioEmitter",
                gate
            }
        };
        saved.aCOs = records.Values.ToArray();
        var ship = Empty<Ship>();
        ship.json = saved;
        var prefix = AccessTools.Method(typeof(LegacyRcsLoadingPatch), "Prefix");
        prefix.Invoke(null, new object[] { ship, false, Ship.Loaded.Full });
        Check(saved.nRCSCountRaw == 0, "Full native materialization was intercepted");
        saved.fLastVisit = 123;
        prefix.Invoke(null, new object[] { ship, false, Ship.Loaded.Shallow });
        Check(saved.nRCSCountRaw == 0, "Missing parts on a previously materialized hull were recreated");
        saved.fLastVisit = 0;
        // StarSystem.Init restores saved hulls through _SpawnShip(false), even
        // when their persisted layout has never been fully materialized.
        prefix.Invoke(null, new object[] { ship, false, Ship.Loaded.Shallow });
        Check(saved.nRCSCountRaw == 2 && Math.Abs(saved.nRCSExhaustV - 2.9) < .000001 && saved.nRCSCount == 3, "Load prefix did not restore only missing fields");
        float preserved = saved.nRCSExhaustV;
        first.aStartingConds[3] = "StatThrustStrength=1.0x99";
        prefix.Invoke(null, new object[] { ship, false, Ship.Loaded.Shallow });
        Check(saved.nRCSExhaustV == preserved, "Reload recomputed valid persisted caches");
        first.aStartingConds[3] = "StatThrustStrength=1.0x2";
    }

    static int Main(string[] args)
    {
        var boundary = new Harmony("offline.save-forward.boundaries");
        try
        {
            boundary.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Equality"), prefix: new HarmonyMethod(typeof(Program), nameof(Equal)));
            boundary.Patch(AccessTools.Method(typeof(CondOwner), nameof(CondOwner.ValidateParent)), prefix: new HarmonyMethod(typeof(Program), nameof(Parent)));
            boundary.Patch(AccessTools.PropertyGetter(typeof(UnityEngine.Random), nameof(UnityEngine.Random.value)), prefix: new HarmonyMethod(typeof(Program), nameof(Random)));
            boundary.Patch(AccessTools.Method(typeof(UnityEngine.Debug), nameof(UnityEngine.Debug.Log), new[] { typeof(object) }), prefix: new HarmonyMethod(typeof(Program), nameof(Quiet)));
            boundary.Patch(AccessTools.Method(typeof(UnityEngine.Debug), nameof(UnityEngine.Debug.LogWarning), new[] { typeof(object) }), prefix: new HarmonyMethod(typeof(Program), nameof(Quiet)));
            Load();
            Eligibility();
            Cases();
            if (args.Length > 0)
                RealSave(args[0]);
            if (args.Length > 1)
                Archive(args[1], args.Length > 2 ? args[2] : null);
            Check(randomReads == 0, "Compatibility loading consumed gameplay randomness");
            Console.WriteLine(checks + " save forward-compatibility checks passed.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
        finally
        {
            boundary.UnpatchSelf();
        }
    }
}

namespace Ostrabus
{
    static class OstrabusPlugin
    {
        internal static BepInEx.Logging.ManualLogSource Logger = null;
    }
}
