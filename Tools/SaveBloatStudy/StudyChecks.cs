using System.IO.Compression;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static class StudyChecks
{
    public static void Run()
    {
        string folder = Path.Combine(Path.GetTempPath(), "Ostrabus-SaveBloatStudy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        int checks = 0;
        try
        {
            foreach (string newline in new[] { "\n", "\r\n" })
            {
                var root = new JObject
                {
                    ["strPlayerCO"] = "Player",
                    ["strVersion"] = "fixture",
                    ["aCOs"] = new JArray(Co("root", "ItmBox", "S", "child"), Co("child", "ItmItem", "S"),
                        Co("loose", "ItmItem", "S"), Co("cycle-a", "ItmItem", "S", "cycle-b"),
                        Co("cycle-b", "ItmItem", "S", "cycle-a"), Co("spawner", "SysLootSpawner", "S")),
                    ["objSystem"] = new JObject
                    {
                        ["dfEpoch"] = 50,
                        ["aStellarDerelicts"] = new JArray(new JObject { ["strID"] = "deferred" })
                    },
                    ["aPlots"] = new JArray(new JObject { ["token"] = "loose" }),
                    ["objMarketSave"] = new JObject
                    {
                        ["mapMarket"] = new JObject
                        {
                            ["S"] = Market("S"), ["gone"] = Market("gone"), ["deferred"] = Market("deferred")
                        }
                    }
                };
                var ship = new JObject
                {
                    ["strRegID"] = "S", ["publicName"] = "Unicode \u03a9 \ud83d\ude80",
                    ["aItems"] = new JArray(new JObject { ["strID"] = "root", ["strName"] = "ItmBox" },
                        new JObject { ["strID"] = "bad-parent", ["strParentID"] = "absent" },
                        new JObject { ["strID"] = "layout-cycle-a", ["strParentID"] = "layout-cycle-b" },
                        new JObject { ["strID"] = "layout-cycle-b", ["strSlotParentID"] = "layout-cycle-a" }),
                    ["aShallowPSpecs"] = new JArray(new JObject { ["strID"] = "spawner", ["strName"] = "SysLootSpawner" }),
                    ["shipCO"] = Co("ship-co", "Ship", "S"),
                    ["aCOs"] = new JArray(Co("dead", "Human", "S"))
                };
                ship["aCOs"]![0]!["bAlive"] = false;
                ship["aCOs"]![0]!["strBodyType"] = "Human";
                string path = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".zip");
                string rootText = new JArray(root).ToString(Formatting.Indented).Replace("\r\n", "\n").Replace("\n", newline);
                using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
                {
                    Write(zip, "Player.json", rootText);
                    Write(zip, "ships/S.json", new JArray(ship).ToString(Formatting.Indented).Replace("\r\n", "\n").Replace("\n", newline));
                }
                string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
                var report = new Study().Read(path);
                Check((string?)report["archiveSHA256"] == hash, "source identity");
                Check(hash == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), "source untouched");
                Check((int?)report["objects"]?["records"] == 8, "all CO records scanned");
                Check((int?)report["inventoryReachability"]?["unanchoredRecords"] == 4, "nested inventory reachable; isolated cycle not rooted");
                Check((int?)report["inventoryReachability"]?["unanchoredWithOtherExactStringReferences"] == 3, "explicit plot reference and cycle links retained as evidence");
                Check((int?)report["objects"]?["deadPersons"] == 1, "dead person counted but not discarded");
                Check((bool?)report["markets"]?[1]?["physicalHullPresent"] == false, "absent market identified");
                Check((bool?)report["markets"]?[2]?["deferredWorldObjectPresent"] == true, "deferred object distinguished from absent hull");
                Check((int?)report["layoutDependencies"]?[0]?["unreachableFromParentlessItem"] == 3, "missing and cyclic layout parents identified");
                Check((int?)report["layoutDependencies"]?[0]?["directMissingParents"] == 1, "missing parent distinguished from cycle");
                Check((int?)report["inventoryReachability"]?["layoutIdsWithoutExplicitState"] == 3, "absence of saved state identified separately");
                var field = report["fields"]!.Single(x => (string?)x["entry"] == "Player.json" && (string?)x["field"] == "aCOs");
                int fieldStart = rootText.IndexOf("\"aCOs\"", StringComparison.Ordinal) + "\"aCOs\":".Length;
                int fieldEnd = rootText.IndexOf("," + newline + "    \"objSystem\"", fieldStart, StringComparison.Ordinal);
                Check((long?)field["rawCharacters"] == fieldEnd - fieldStart, $"raw field span, including separators: actual={field["rawCharacters"]}, expected={fieldEnd - fieldStart}");
                long coCharacters = root["aCOs"]!.Sum(co => co.ToString(Formatting.Indented).Replace("\r\n", "\n").Length);
                Check((long?)report["objects"]?["rawRecordCharacters"] > coCharacters, "native record spans measured");
            }
            string trainingFolder = Path.Combine(folder, "ai_training");
            Directory.CreateDirectory(trainingFolder);
            File.WriteAllText(Path.Combine(trainingFolder, "test.json"), new JArray(new JObject
            {
                ["strName"] = "fixture", ["mapIAHist2"] = new JObject
                {
                    ["Need"] = new JObject
                    {
                        ["mapInteractions"] = new JObject
                        {
                            ["Action"] = new JObject { ["nIterations"] = 4, ["fTotalValue"] = -3.25 }
                        }
                    }
                }
            }).ToString(Formatting.Indented));
            var baseline = new TrainingBaseline(folder);
            Check(baseline.Matches("Need", ["Action", "4", "-3.25"], new JObject()), "exact training baseline matched");
            Check(!baseline.Matches("OtherNeed", ["Action", "4", "-3.25"], new JObject()), "different condition rejected");
            Check(!baseline.Matches("Need", ["OtherAction", "4", "-3.25"], new JObject()), "different interaction rejected");
            Check(!baseline.Matches("Need", ["Action", "5", "-3.25"], new JObject()), "learned iteration count preserved");
            Check(!baseline.Matches("Need", ["Action", "4", "-3.5"], new JObject()), "learned score preserved");
            Check(!baseline.Matches("Need", ["Action", "4", "-3.25"], new JObject
            {
                ["mapScores"] = new JArray("Target=1")
            }), "learned target-specific map preserved");
            Console.WriteLine($"Save bloat study: {checks} checks passed.");
        }
        finally
        {
            // Only this uniquely-created fixture directory is removed.
            if (!Path.GetFullPath(folder).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(folder).StartsWith("Ostrabus-SaveBloatStudy-", StringComparison.Ordinal))
                throw new InvalidOperationException("Fixture cleanup target is outside its expected temporary directory.");
            Directory.Delete(folder, true);
        }

        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidDataException("Fixture failed: " + message);
            checks++;
        }
    }

    private static JObject Co(string id, string type, string ship, params string[] children) => new()
    {
        ["strID"] = id, ["strCODef"] = type, ["strRegIDLast"] = ship, ["bAlive"] = true,
        ["aStack"] = new JArray(children)
    };

    private static JObject Market(string id) => new()
    {
        ["strShipRegId"] = id, ["configMap"] = new JObject()
    };

    private static void Write(ZipArchive zip, string name, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open());
        writer.Write(content);
    }
}
