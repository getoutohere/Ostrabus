using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ShipExecutivePrototype;

// Shipped definitions, not a reconstructed replacement policy. This inventory
// records direct command aliases and their interaction references, including
// generated stakes. It does not claim recursive trigger/reputation reachability.
static class NavyAssetAudit
{
    internal static void Run(Action<string, Action> scenario, Action<bool, string> check)
    {
        scenario("Navy shipped command aliases include core and generated surrender interactions", () =>
        {
            string root = Ostrabus.Development.InstallationPaths.Data;
            var loot = JArray.Parse(File.ReadAllText(Path.Combine(root, "loot/loot.json")));
            var aliases = loot.OfType<JObject>().Where(l => ((string)l["strName"])?.StartsWith("CMD", StringComparison.Ordinal) == true).ToArray();
            var references = new Dictionary<string, List<object>>();
            var sources = new List<object>();
            foreach (string directory in new[] { "interactions", "tsv/output/stakes/interactions" })
            {
                foreach (string path in Directory.GetFiles(Path.Combine(root, directory), "*.json", SearchOption.AllDirectories).OrderBy(p => p))
                {
                    var records = JArray.Parse(File.ReadAllText(path));
                    bool relevant = false;
                    foreach (var interaction in records.OfType<JObject>())
                    {
                        foreach (string side in new[] { "LootCTsUs", "LootCTsThem" })
                        {
                            string alias = (string)interaction[side];
                            if (alias == null || !aliases.Any(a => (string)a["strName"] == alias))
                                continue;
                            relevant = true;
                            if (!references.TryGetValue(alias, out var entries))
                                references[alias] = entries = new List<object>();
                            entries.Add(new
                            {
                                source = path.Substring(root.Length).Replace('\\', '/'),
                                interaction = (string)interaction["strName"],
                                side,
                                shipTestUs = (string)interaction["ShipTestUs"],
                                shipTestThem = (string)interaction["ShipTestThem"],
                                ctUs = (string)interaction["CTTestUs"],
                                ctThem = (string)interaction["CTTestThem"]
                            });
                        }
                    }
                    if (relevant)
                        sources.Add(new
                        {
                            path = path.Substring(root.Length).Replace('\\', '/'),
                            sha256 = Hash(path)
                        });
                }
            }
            var report = new List<object>();
            foreach (var alias in aliases)
            {
                string name = (string)alias["strName"];
                var parsed = Loot.CloneFromJSON(alias.ToObject<JsonLoot>()).GetLootNames().ToArray();
                string command = parsed.First();
                string disposition;
                switch (command)
                {
                    case "ComeHere":
                        disposition = "explicit-phases-fixture-qualified";
                        break;
                    case "TakeFuel":
                        disposition = "native-receipt-and-transfer-provider-qualified; materialized-gas-storage-not-qualified";
                        break;
                    case "SurrenderConsiderOffer":
                        disposition = "native-body-fixture-qualified";
                        break;
                    case "SurrenderConsiderPlayerDemand":
                        disposition = "shipped-reply-order-and-native-evaluators-qualified-subset; crew-effects-fixtured";
                        break;
                    case "Shoot":
                        disposition = "native-body-empty-weapon-fixture";
                        break;
                    case "TargetPlayer":
                        disposition = "native-body-fixture-qualified";
                        break;
                    case "SurrenderArrestAndDespawn":
                    case "DeployHauler":
                    case "DeployLeo":
                    case "Scuttle":
                    case "RaceComms":
                        disposition = "exclusive-native-takeover; subsystem-outcome-not-qualified";
                        break;
                    default:
                        throw new InvalidOperationException("New authored command requires review: " + command);
                }
                check(typeof(Ship).Assembly.GetType("Ostranauts.Ships.Commands." + command) != null, "Missing native command type: " + command);
                report.Add(new
                {
                    alias = name,
                    command,
                    arguments = parsed.Skip(1).ToArray(),
                    disposition,
                    references = references.TryGetValue(name, out var entries) ? entries.ToArray() : Array.Empty<object>()
                });
            }
            check(aliases.Length == 13 && references.ContainsKey("CMDConsiderPlayerDemandSurrender"), "Command inventory changed or generated player-demand branch was missed");
            check(references["CMDComeHere"].Count > 1, "Generated boarding demand was omitted from ComeHere admission");
            File.WriteAllText("obj/ShipExecutiveModel/authored-inputs.json", JsonConvert.SerializeObject(new
            {
                schemaVersion = 1,
                scope = "Direct CMD aliases in loot/loot.json plus core/generated-stakes interaction references; conditional reachability and recursive loot closure are not certified.",
                lootSource = "loot/loot.json",
                lootSha256 = Hash(Path.Combine(root, "loot/loot.json")),
                dialogueSources = NativeDialogueFixtures.RegistryFiles.Select(path => new { path, sha256 = Hash(Path.Combine(root, path)) }).ToArray(),
                sources,
                aliases = report,
                nativeInputs = new[] { "BeatManager: FlyToPatrol", "BeatManager: LockPirate", "WeaponsSystem.DodgeHandling: Dodge then late DodgeTarget publication", "AIShip.FFWD: OneShotWrapperCommand (disabled for executive-owned actors)" },
                productionReady = false
            }, Formatting.Indented));
        });
    }

    static string Hash(string path)
    {
        using (var sha = System.Security.Cryptography.SHA256.Create())
        using (var file = File.OpenRead(path))
            return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
    }
}
