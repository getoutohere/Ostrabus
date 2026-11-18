using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

internal static class AssetFiles
{
    internal static Dictionary<string, JObject> Load(string data, string folder) =>
        Directory.GetFiles(Path.Combine(data, folder), "*.json").OrderBy(path => path, StringComparer.Ordinal)
            .SelectMany(path => JArray.Parse(File.ReadAllText(path)).OfType<JObject>())
            .GroupBy(row => (string)row["strName"]).ToDictionary(group => group.Key, group => group.Last());
}
