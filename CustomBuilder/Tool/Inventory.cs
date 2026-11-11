using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text.Json;

sealed record Declaration(string Type, string File, string[] Methods);

static class Inventory {
    internal static string[] Sources(string root, Catalog catalog) {
        var excluded = new HashSet<string>(catalog.ExcludedSources, StringComparer.Ordinal);

        // Discover new module directories too: a new hook must fail validation
        // until it has an owner, rather than quietly disappearing from customs.
        var ignored = new HashSet<string>(new[] {
            "Tests", "Tools", "Build", "CustomBuilder", "bin", "obj", "dist", "OldAnalysisData", "vendor"
        }, StringComparer.OrdinalIgnoreCase);

        IEnumerable<string> Walk(string directory) {
            foreach (string path in Directory.EnumerateFiles(directory, "*.cs")) yield return path;
            foreach (string child in Directory.EnumerateDirectories(directory)) {
                string name = Path.GetFileName(child);
                if (name.StartsWith('.') || ignored.Contains(name)) continue;
                foreach (string path in Walk(child)) yield return path;
            }
        }

        return Walk(root)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Where(path => !excluded.Contains(path))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static CompilationUnitSyntax Parse(string text, IEnumerable<string>? defines = null) =>
        CSharpSyntaxTree.ParseText(text,
            new CSharpParseOptions(LanguageVersion.Latest, preprocessorSymbols: defines ?? []))
            .GetCompilationUnitRoot();

    internal static bool Harmony(AttributeSyntax attribute, string suffix) {
        string name = attribute.Name.ToString().Split('.').Last();
        return name == suffix || name == suffix + "Attribute";
    }

    internal static string Name(TypeDeclarationSyntax type) {
        var ns = string.Join(".", type.Ancestors().OfType<BaseNamespaceDeclarationSyntax>()
            .Reverse().Select(parent => parent.Name.ToString()));
        var names = type.Ancestors().OfType<TypeDeclarationSyntax>()
            .Reverse().Select(parent => parent.Identifier.ValueText)
            .Append(type.Identifier.ValueText);
        return (ns.Length == 0 ? "" : ns + ".") + string.Join("+", names);
    }

    internal static Declaration[] Scan(string root, Catalog catalog) =>
        Sources(root, catalog).SelectMany(file => {
            var tree = Parse(File.ReadAllText(Path.Combine(root, file)));
            return tree.DescendantNodes().OfType<TypeDeclarationSyntax>()
                .Where(type => type.AttributeLists.SelectMany(list => list.Attributes)
                    .Any(attribute => Harmony(attribute, "HarmonyPatch")))
                .Select(type => new Declaration(
                    Name(type),
                    file,
                    type.Members.OfType<MethodDeclarationSyntax>()
                        .Where(method => method.AttributeLists.SelectMany(list => list.Attributes)
                            .Any(attribute => Harmony(attribute, "HarmonyPatch")))
                        .Select(method => method.Identifier.ValueText)
                        .ToArray()));
        }).OrderBy(declaration => declaration.Type, StringComparer.Ordinal).ToArray();

    internal static Hook[] Hooks(Feature feature, Declaration[] declarations) =>
        feature.Patches.Concat(declarations
            .Where(declaration => feature.PatchFiles.Contains(declaration.File, StringComparer.Ordinal))
            .Select(declaration => new Hook { Type = declaration.Type }))
            .ToArray();

    internal static void Validate(Catalog catalog, Declaration[] declarations) {
        Files.Need(catalog.SchemaVersion == 1, "Unsupported catalog schema.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var types = declarations.ToDictionary(declaration => declaration.Type, StringComparer.Ordinal);

        foreach (var feature in catalog.Features) {
            Files.Need(System.Text.RegularExpressions.Regex.IsMatch(feature.Id, "^[a-z][a-z0-9-]*$") &&
                ids.Add(feature.Id), "Invalid/duplicate feature ID: " + feature.Id);
            Files.Need(feature.Category is "bugfix" or "qol" or "performance" or "simulation" or "developer" or "support",
                "Unknown category: " + feature.Id);

            foreach (string path in feature.PatchFiles)
                Files.Need(declarations.Any(declaration => declaration.File == path),
                    "Patch source has no declarations: " + path);

            foreach (var hook in feature.Patches) {
                Files.Need(types.TryGetValue(hook.Type, out var declaration), "Unknown patch type: " + hook.Type);
                if (hook.Methods != null)
                    foreach (string method in hook.Methods)
                        Files.Need(declaration!.Methods.Contains(method, StringComparer.Ordinal),
                            "Unknown method-scoped patch: " + hook.Type + "." + method);
            }
        }

        var features = catalog.Features.ToDictionary(feature => feature.Id, StringComparer.Ordinal);
        foreach (var feature in catalog.Features)
            foreach (string dependency in feature.Requires)
                Files.Need(features.ContainsKey(dependency), "Unknown dependency " + dependency + " of " + feature.Id);
        foreach (var feature in catalog.Features) Resolve(catalog, new Profile { Features = [feature.Id] });

        var owned = catalog.Features
            .SelectMany(feature => Hooks(feature, declarations).Select(hook => (feature.Id, Hook: hook)))
            .GroupBy(owner => owner.Hook.Type)
            .ToDictionary(group => group.Key, group => group.ToArray());

        foreach (var declaration in declarations) {
            Files.Need(owned.TryGetValue(declaration.Type, out var owners),
                "Uncatalogued Harmony patch: " + declaration.Type + " in " + declaration.File);
            if (owners!.Length > 1 && !catalog.SharedPatches.Contains(declaration.Type, StringComparer.Ordinal)) {
                Files.Need(owners.All(owner => owner.Hook.Methods != null),
                    "Multiple whole-type owners for " + declaration.Type);
                var methods = owners.SelectMany(owner => owner.Hook.Methods!).ToArray();
                Files.Need(methods.Distinct(StringComparer.Ordinal).Count() == methods.Length,
                    "Duplicate method ownership for " + declaration.Type);
            }
            if (owners.All(owner => owner.Hook.Methods != null))
                foreach (string method in declaration.Methods)
                    Files.Need(owners.Any(owner => owner.Hook.Methods!.Contains(method, StringComparer.Ordinal)),
                        "Uncatalogued method patch: " + declaration.Type + "." + method);
        }
    }

    internal static Feature[] Resolve(Catalog catalog, Profile profile) {
        Files.Need(profile.SchemaVersion == 1, "Unsupported profile schema.");
        var map = catalog.Features.ToDictionary(feature => feature.Id, StringComparer.Ordinal);
        var seen = new HashSet<string>();
        var visiting = new HashSet<string>();
        var result = new List<Feature>();

        void Visit(string id) {
            Files.Need(map.TryGetValue(id, out var feature), "Unknown feature: " + id);
            if (seen.Contains(id)) return;
            Files.Need(visiting.Add(id), "Dependency cycle at " + id);
            foreach (string dependency in feature!.Requires.Order(StringComparer.Ordinal)) Visit(dependency);
            visiting.Remove(id);
            seen.Add(id);
            result.Add(feature);
        }

        foreach (string id in profile.Features.Order(StringComparer.Ordinal)) Visit(id);
        return result.ToArray();
    }

    internal static Plan Plan(Catalog catalog, Profile profile, Declaration[] declarations) {
        var selected = Resolve(catalog, profile);
        var hooks = selected.SelectMany(feature => Hooks(feature, declarations))
            .GroupBy(hook => hook.Type)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new Hook {
                Type = group.Key,
                Methods = group.Any(hook => hook.Methods == null)
                    ? null
                    : group.SelectMany(hook => hook.Methods!)
                        .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()
            }).ToArray();

        var plan = new Plan {
            Name = profile.Name,
            Requested = profile.Features.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            Included = selected.Select(feature => feature.Id).ToArray(),
            Patches = hooks,
            Settings = selected.SelectMany(feature => feature.Settings)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            TickCalls = selected.Sum(feature => feature.Tick.Length),
            PdaAssets = selected.Any(feature => feature.Id == "pda-settings")
        };

        foreach (var feature in selected.Where(feature => !plan.Requested.Contains(feature.Id, StringComparer.Ordinal)))
            plan.RequiredBy[feature.Id] = selected
                .Where(parent => parent.Requires.Contains(feature.Id, StringComparer.Ordinal))
                .Select(parent => parent.Id).ToArray();

        plan.Fingerprint = Files.Hash(JsonSerializer.Serialize(new { profile = plan.Requested, catalog }, Files.Json));
        return plan;
    }
}
