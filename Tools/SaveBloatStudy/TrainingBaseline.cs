using System.Globalization;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;

// An exact numeric match is evidence of repeated baseline
// data, not permission to discard it. Native loading expects a full history.
internal sealed class TrainingBaseline
{
    private readonly record struct Entry(string Condition, string Interaction, int Iterations, float Value);
    private readonly HashSet<Entry> entries = new();
    public List<object> Sources { get; } = new();

    public TrainingBaseline(string dataDirectory)
    {
        string folder = Path.Combine(dataDirectory, "ai_training");
        foreach (string path in Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            byte[] bytes = File.ReadAllBytes(path);
            Sources.Add(new { file = Path.GetFileName(path), sha256 = Convert.ToHexString(SHA256.HashData(bytes)) });
            foreach (var personality in JArray.Parse(System.Text.Encoding.UTF8.GetString(bytes)))
                foreach (var condition in ((JObject?)personality["mapIAHist2"] ?? new JObject()).Properties())
                    foreach (var action in ((JObject?)condition.Value["mapInteractions"] ?? new JObject()).Properties())
                    {
                        // The current native training assets have no per-target
                        // score maps. Do not make a partial comparison if that changes.
                        if (action.Value["mapScores"] is JObject scores && scores.Count != 0) continue;
                        entries.Add(new Entry(condition.Name, action.Name,
                            (int?)action.Value["nIterations"] ?? 0, (float?)action.Value["fTotalValue"] ?? 0));
                    }
        }
    }

    public bool Matches(string condition, string[] parts, JToken saved)
    {
        if (parts.Length != 3 || (saved["mapScores"] as JArray)?.Count > 0 ||
            !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) ||
            !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float value)) return false;
        return entries.Contains(new Entry(condition, parts[0], count, value));
    }
}
