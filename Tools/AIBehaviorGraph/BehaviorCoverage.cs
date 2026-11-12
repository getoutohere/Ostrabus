using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Inventory comes from the shipped assembly, not the set of scenarios an author
// happened to write. Bindings are references for review, never proof that a
// method's branches or interactions have been transition-modeled.
static class BehaviorCoverage {
    sealed class Binding {
        internal string Model = "";
        internal string Source = "";
        internal HashSet<string> Methods = new(StringComparer.Ordinal);
    }

    internal static void Run(string root, string[] args) {
        int at = Array.IndexOf(args, "--assembly");
        if (at < 0 || at + 1 == args.Length) throw new Exception("coverage requires --assembly <Assembly-CSharp.dll>.");
        string file = args[at + 1];
        using var assembly = AssemblyDefinition.ReadAssembly(file, new ReaderParameters { ReadSymbols = false, InMemory = true });
        var types = assembly.MainModule.Types.ToDictionary(t => t.FullName, StringComparer.Ordinal);
        bool DerivesFrom(TypeDefinition type, string ancestor) {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (type.BaseType != null && visited.Add(type.FullName)) {
                if (type.BaseType.FullName == ancestor) return true;
                if (!types.TryGetValue(type.BaseType.FullName, out type!)) return false;
            }
            return false;
        }

        var bindings = new Dictionary<string, List<Binding>>(StringComparer.Ordinal);
        string graph = Path.Combine(root, "Navigation", "BehaviorGraph");
        foreach (string manifest in Directory.GetFiles(graph, "assembly-dependencies.json", SearchOption.AllDirectories).Order()) {
            using var document = JsonDocument.Parse(File.ReadAllText(manifest));
            string model = Path.GetRelativePath(graph, Path.GetDirectoryName(manifest)!).Replace('\\', '/');
            foreach (var source in document.RootElement.GetProperty("bindings").EnumerateObject()) {
                foreach (var selector in source.Value.EnumerateArray()) {
                    string type = selector.GetProperty("type").GetString()!;
                    if (!bindings.TryGetValue(type, out var references)) bindings[type] = references = new();
                    references.Add(new Binding {
                        Model = model,
                        Source = source.Name,
                        Methods = selector.GetProperty("methods").EnumerateArray().Select(m => m.GetString()!).ToHashSet(StringComparer.Ordinal)
                    });
                }
            }
        }

        string[] References(string type, string method) => bindings.GetValueOrDefault(type, new())
            .Where(b => b.Methods.Contains(method)).Select(b => b.Model + ":" + b.Source).Distinct().Order().ToArray();
        object[] Methods(TypeDefinition type, IEnumerable<MethodDefinition> methods) => methods.OrderBy(m => m.FullName)
            .Select(m => (object)new {
                symbol = m.FullName,
                sourceBindings = References(type.FullName, m.Name)
            }).ToArray();

        var roles = types.Values.Where(t => !t.IsAbstract && DerivesFrom(t, "Ostranauts.Ships.AIPilots.BaseCharacter"))
            .OrderBy(t => t.FullName).ToArray();
        var commands = types.Values.Where(t => !t.IsAbstract && DerivesFrom(t, "Ostranauts.Ships.Commands.BaseCommand"))
            .OrderBy(t => t.FullName).ToArray();
        var commandTypes = commands.Select(t => t.FullName).ToHashSet(StringComparer.Ordinal);
        bool RoleEntry(MethodDefinition method) => (method.IsConstructor && !method.IsStatic) || method.Name is "FFWD" or "GetTarget" or "GetTargetHome";
        var roleRows = roles.Select(t => new {
            type = t.FullName,
            entrypoints = Methods(t, t.Methods.Where(RoleEntry)),
            constructedCommands = t.Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions)
                .Where(i => i.OpCode == OpCodes.Newobj && i.Operand is MethodReference r && commandTypes.Contains(r.DeclaringType.FullName))
                .Select(i => ((MethodReference)i.Operand).DeclaringType.FullName).Distinct().Order().ToArray(),
            unboundEntrypoints = t.Methods.Where(m => RoleEntry(m) && References(t.FullName, m.Name).Length == 0)
                .Select(m => m.FullName).Order().ToArray()
        }).ToArray();
        var commandRows = commands.Select(t => new {
            type = t.FullName,
            entrypoints = Methods(t, t.Methods.Where(m => m.Name is "RunCommand" or "CanExecute" or "ResolveInstantly")),
            unboundEntrypoints = t.Methods.Where(m => m.Name is "RunCommand" or "CanExecute" or "ResolveInstantly")
                .Where(m => References(t.FullName, m.Name).Length == 0).Select(m => m.FullName).Order().ToArray()
        }).ToArray();
        var result = new {
            assemblySha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant(),
            completeness = "not_established",
            interpretation = "Assembly-derived inventory of concrete native roles, directly constructed commands and declared source bindings. Bindings do not establish branch, event-order or cross-actor coverage. No percentage of semantic completeness is inferred.",
            roleCount = roles.Length,
            commandCount = commands.Length,
            rolesWithUnboundEntrypoints = roleRows.Count(r => r.unboundEntrypoints.Length != 0),
            commandsWithUnboundEntrypoints = commandRows.Count(r => r.unboundEntrypoints.Length != 0),
            roles = roleRows,
            commands = commandRows
        };
        Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        if (args.Contains("--require-bound") && (result.rolesWithUnboundEntrypoints != 0 || result.commandsWithUnboundEntrypoints != 0)) Environment.ExitCode = 1;
    }
}
