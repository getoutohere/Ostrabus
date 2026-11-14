using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

static partial class Program {
    static JObject ReadShip(ZipArchive zip, string shipId) {
        var entry = zip.GetEntry("ships/" + shipId + ".json");
        if (entry == null) return null;
        using (var stream = new StreamReader(entry.Open()))
        using (var reader = new JsonTextReader(stream)) return (JObject)JArray.Load(reader)[0];
    }
    static void History(string root, string[] names) {
        foreach (string name in names) {
            string path = Path.Combine(root, name, name + ".zip");
            if (!File.Exists(path)) continue;
            using (var zip = ZipFile.OpenRead(path)) {
                var ship = ReadShip(zip, "O-GXD");
                if (ship == null) continue;
                Console.WriteLine("SAVE " + name);
                foreach (var pod in ship["aItems"].Where(x => ((string)x["strName"] ?? "").Contains("CargoPod")))
                    Console.WriteLine("  " + pod["strID"] + " " + pod["strName"] + " parent=" + pod["strParentID"]);
                Console.WriteLine("  Ship mappings: " + ship["aMarketConfigs"]?.ToString(Formatting.None));
            }
        }
    }
    static void MarketSave(string path) {
        using (var zip = ZipFile.OpenRead(path)) {
            var entry = zip.Entries.First(e => !e.FullName.Contains("/") && e.FullName.EndsWith(".json") && e.FullName != "saveInfo.json");
            using (var stream = new StreamReader(entry.Open()))
            using (var reader = new JsonTextReader(stream)) {
                while (reader.Read()) {
                    if (reader.TokenType != JsonToken.PropertyName) continue;
                    if ((string)reader.Value == "objMarketSave") {
                        reader.Read();
                        var market = JToken.ReadFrom(reader);
                        foreach (string id in new[] { "O-GXD", "M-TZ3T" }) {
                            Console.WriteLine("MARKET " + id + ": " + market["mapMarket"]?[id]?.ToString(Formatting.None));
                        }
                        return;
                    }
                    reader.Skip();
                }
            }
        }
        throw new Exception("Market save not found.");
    }
    static void Main(string[] args) {
        if (args.Length == 0) throw new ArgumentException("Expected native, patched, repair, history <saves-root> <names...>, or market <save.zip>.");
        if (args[0] == "history") History(args[1], args.Skip(2).ToArray());
        else if (args[0] == "market") MarketSave(args[1]);
        else if (args[0] == "native") NativeChecks();
        else if (args[0] == "patched") NativeChecks(true);
        else if (args[0] == "repair") NativeChecks(true, repair: true);
        else throw new ArgumentException("Expected native, patched, repair, history <saves-root> <names...>, or market <save.zip>.");
    }
}
