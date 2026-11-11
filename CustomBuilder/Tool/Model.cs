using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

sealed class Feature {
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public bool Hidden { get; set; }
    public bool Experimental { get; set; }
    public string[] Requires { get; set; } = [];
    public string[] PatchFiles { get; set; } = [];
    public Hook[] Patches { get; set; } = [];
    public string[] Before { get; set; } = [];
    public string[] After { get; set; } = [];
    public string[] Tick { get; set; } = [];
    public string[] Stop { get; set; } = [];
    public string[] Settings { get; set; } = [];
}

sealed class Hook {
    public string Type { get; set; } = "";
    public string[]? Methods { get; set; }
}

sealed class Catalog {
    public int SchemaVersion { get; set; } = 1;
    public Feature[] Features { get; set; } = [];
    public string[] ExcludedSources { get; set; } = [];
    public string[] SharedPatches { get; set; } = [];
}

sealed class Profile {
    public int SchemaVersion { get; set; } = 1;
    public string Name { get; set; } = "custom";
    public string[] Features { get; set; } = [];
}

sealed class Plan {
    public int SchemaVersion { get; set; } = 1;
    public string Name { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public string[] Requested { get; set; } = [];
    public string[] Included { get; set; } = [];
    public Dictionary<string, string[]> RequiredBy { get; set; } = new(StringComparer.Ordinal);
    public Hook[] Patches { get; set; } = [];
    public string[] Settings { get; set; } = [];
    public int TickCalls { get; set; }
    public bool PdaAssets { get; set; }
}

static class Files {
    internal static readonly JsonSerializerOptions Json = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    internal static T Read<T>(string file) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(file), Json)
        ?? throw new Exception("Empty JSON: " + file);

    internal static void Write<T>(string file, T value) {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        File.WriteAllText(file, JsonSerializer.Serialize(value, Json) + "\n", new UTF8Encoding(false));
    }

    internal static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    internal static void Need(bool ok, string reason) {
        if (!ok) throw new Exception(reason);
    }
}
