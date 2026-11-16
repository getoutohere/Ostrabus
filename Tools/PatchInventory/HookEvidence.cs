#nullable enable
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Security.Cryptography;
using Mono.Cecil;

// Test associations are navigation aids, never assertion-level proof. Runtime
// binding receipts and semantic/live validation remain distinct evidence layers.
static class HookEvidence
{
    static string Normalize(string path) => path.Replace('\\', '/');
    static IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> types)
    {
        foreach (var type in types)
        {
            yield return type;
            foreach (var nested in Types(type.NestedTypes)) yield return nested;
        }
    }
    static bool Generated(string path) => Normalize(path).Split('/').Any(part => part is "bin" or "obj");
    static bool Matches(string pattern, string source, string directory)
    {
        string absolute = Normalize(Path.GetFullPath(Path.Combine(directory, pattern)));
        string regex = Regex.Escape(absolute).Replace("\\*\\*", ".*").Replace("\\*", "[^/]*").Replace("\\?", "[^/]");
        return Regex.IsMatch(source, "^" + regex + "$", RegexOptions.IgnoreCase);
    }
    internal static void Run(string reportPath, string repository, string output)
    {
        string root = Path.GetFullPath(repository);
        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "CustomBuilder/features.json")));
        string plugin = report.RootElement.GetProperty("pluginAssembly").GetString()!;
        if (report.RootElement.TryGetProperty("pluginSha256", out var expectedHash) &&
            !Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(plugin))).Equals(expectedHash.GetString(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Binding receipt no longer matches its plugin image; rerun the binding harness.");
        using var module = ModuleDefinition.ReadModule(plugin, new ReaderParameters { ReadSymbols = true });
        var types = Types(module.Types).ToDictionary(type => type.FullName);
        var projects = new[] { "Tests", "Tools" }.SelectMany(folder =>
                Directory.EnumerateFiles(Path.Combine(root, folder), "*.csproj", SearchOption.AllDirectories))
            .Where(path => !Generated(path)).Select(path =>
            {
                var xml = XDocument.Load(path);
                var includes = xml.Descendants("Compile").Attributes("Include").Select(attribute => attribute.Value).ToArray();
                var excludes = xml.Descendants("Compile").Attributes("Exclude").SelectMany(attribute => attribute.Value.Split(';')).ToArray();
                var text = string.Join("\n", Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.cs", SearchOption.AllDirectories)
                    .Where(file => !Generated(file)).Select(File.ReadAllText));
                return new { path, includes, excludes, text };
            }).ToArray();
        var rows = new List<object>();
        int unmapped = 0, unowned = 0, unassociated = 0;
        foreach (var patch in report.RootElement.GetProperty("patches").EnumerateArray())
        {
            string name = patch.GetProperty("patch").GetString()!;
            var type = types[name.Replace('+', '/')];
            string? document = Types(new[] { type }).SelectMany(candidate => candidate.Methods)
                .SelectMany(method => method.DebugInformation.SequencePoints).Select(point => point.Document.Url).FirstOrDefault();
            string? source = document == null ? null : Normalize(document);
            const string mappedRoot = "/_/Ostrabus/";
            if (source?.StartsWith(mappedRoot, StringComparison.Ordinal) == true) source = source[mappedRoot.Length..];
            else if (source != null) source = Normalize(Path.GetRelativePath(root, source));
            if (source == null || !File.Exists(Path.Combine(root, source))) unmapped++;
            var owners = catalog.RootElement.GetProperty("features").EnumerateArray().Where(feature =>
                feature.TryGetProperty("patchFiles", out var files) && files.EnumerateArray().Any(file => Normalize(file.GetString()!) == source) ||
                feature.TryGetProperty("patches", out var declarations) && declarations.EnumerateArray().Any(declaration =>
                    declaration.GetProperty("type").GetString()!.Replace('+', '/') == name.Replace('+', '/')))
                .Select(feature => feature.GetProperty("id").GetString()).ToArray();
            if (owners.Length == 0) unowned++;
            string shortName = type.Name;
            string absoluteSource = source == null ? "" : Normalize(Path.GetFullPath(Path.Combine(root, source)));
            var associations = projects.Where(project =>
            {
                string directory = Path.GetDirectoryName(project.path)!;
                bool linked = source != null && project.includes.Any(include => Matches(include, absoluteSource, directory)) &&
                    !project.excludes.Any(exclude => Matches(exclude, absoluteSource, directory));
                string reference = shortName.Length >= 8 ? shortName : name.Replace('+', '.');
                return linked || Regex.IsMatch(project.text, @"\b" + Regex.Escape(reference) + @"\b");
            }).Select(project => Normalize(Path.GetRelativePath(root, project.path))).ToArray();
            if (associations.Length == 0) unassociated++;
            rows.Add(new
            {
                patch = name,
                source,
                features = owners,
                actualTargets = patch.GetProperty("targets"),
                nativeSuiteAssociations = associations,
                associationMeaning = "Linked production source or a named type reference; inspect the suite and its execution receipt before claiming effect coverage."
            });
        }
        File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            scope = "Actual Harmony targets plus source/feature/test associations. Binding and association are not semantic or live proof.",
            gameAssembly = Path.GetFileName(report.RootElement.GetProperty("gameAssembly").GetString()),
            pluginAssembly = Path.GetFileName(plugin),
            pluginMvid = module.Mvid,
            classes = rows.Count,
            unmappedSources = unmapped,
            unownedFeatures = unowned,
            withoutSuiteAssociation = unassociated,
            patches = rows
        }, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine($"Mapped {rows.Count} patch classes; {unmapped} missing source mappings, {unowned} missing feature owners, {unassociated} without a suite association.");
        if (unmapped != 0 || unowned != 0) Environment.ExitCode = 1;
    }
}
