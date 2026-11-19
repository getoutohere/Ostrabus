using System.IO.Compression;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

if (args.Length > 0 && args[0] == "summarize") return SaveStudy.Summarize(args);
if (args.Length > 0 && args[0] == "root") return SaveStudy.Root(args);
if (args.Length > 0 && args[0] == "report") return SaveStudy.Report(args);
if (args.Length > 0 && args[0] == "geometry") return RouteGeometry.Run(args);
if (args.Length > 0 && args[0] == "compare") return SaveStudy.Compare(args);
if (args.Length > 0 && args[0] == "fixture") return SaveStudy.Fixture(args);

if (args.Length < 4 || args[0] != "index")
{
    Console.Error.WriteLine("Usage: FreightLifecycleStudy index OUTPUT.json SAVE_ROOT SHIP_ID [...]");
    return 2;
}

string output = Path.GetFullPath(args[1]);
string root = Path.GetFullPath(args[2]);
string[] ids = args.Skip(3).ToArray();
var reports = new JArray();
foreach (string path in Directory.EnumerateFiles(root, "*.zip", SearchOption.AllDirectories)
    .OrderBy(File.GetLastWriteTimeUtc))
{
    try
    {
        using var zip = ZipFile.OpenRead(path);
        var ships = new JObject();
        foreach (string id in ids)
        {
            var entry = zip.Entries.FirstOrDefault(entry =>
                string.Equals(entry.FullName.Replace('\\', '/'), "ships/" + id + ".json", StringComparison.Ordinal));
            if (entry == null)
            {
                ships[id] = new JObject { ["present"] = false };
                continue;
            }
            var row = new JObject { ["present"] = true, ["entryBytes"] = entry.Length };
            if (entry.Length <= 4 * 1024 * 1024)
            {
                using var stream = new StreamReader(entry.Open());
                using var reader = new JsonTextReader(stream);
                row["ship"] = JToken.ReadFrom(reader);
            }
            else row["omitted"] = "Entry exceeds the bounded index-read size; inspect separately.";
            ships[id] = row;
        }
        reports.Add(new JObject
        {
            ["save"] = Path.GetFileNameWithoutExtension(path),
            ["path"] = path,
            ["writtenUtc"] = File.GetLastWriteTimeUtc(path).ToString("O"),
            ["ships"] = ships
        });
    }
    catch (Exception error)
    {
        reports.Add(new JObject { ["path"] = path, ["error"] = error.ToString() });
    }
}
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output, reports.ToString(Formatting.Indented));
foreach (string id in ids)
{
    var present = reports.OfType<JObject>().Where(report => (bool?)report["ships"]?[id]?["present"] == true).ToArray();
    Console.WriteLine(id + ": present in " + present.Length + "/" + reports.Count + " saves; " +
        "last file timestamp=" + (string)present.LastOrDefault()?["writtenUtc"] + "; save=" +
        (string)present.LastOrDefault()?["save"]);
}
return reports.Any(report => report["error"] != null) ? 1 : 0;
