using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;

static class HookComparison
{
    static string Canon(TypeReference type)
    {
        if (type is ByReferenceType reference) return Canon(reference.ElementType) + "&";
        if (type is ArrayType array) return Canon(array.ElementType) + "[" + new string(',', array.Rank - 1) + "]";
        if (type is GenericParameter parameter) return "!" + parameter.Position;
        if (type is GenericInstanceType generic) return generic.ElementType.FullName + "<" + string.Join(",", generic.GenericArguments.Select(Canon)) + ">";
        return type.FullName;
    }
    static IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> input)
    {
        foreach (var type in input) { yield return type; foreach (var nested in Types(type.NestedTypes)) yield return nested; }
    }
    static string Key(JsonElement method) => method.GetProperty("assembly").GetString() + "::" + method.GetProperty("type").GetString() + "::" +
        method.GetProperty("name").GetString() + "(" + string.Join(",", method.GetProperty("parameters").EnumerateArray().Select(p => p.GetString())) + ")";
    static string Body(MethodDefinition method)
    {
        if (!method.HasBody) return method.Attributes + ":extern:" + method.ImplAttributes;
        var instructions = method.Body.Instructions;
        string Operand(object value) => value switch
        {
            Mono.Cecil.Cil.Instruction branch => "@" + instructions.IndexOf(branch),
            Mono.Cecil.Cil.Instruction[] branches => string.Join(",", branches.Select(b => "@" + instructions.IndexOf(b))),
            Mono.Cecil.Cil.VariableDefinition local => "local" + local.Index,
            ParameterDefinition parameter => "arg" + parameter.Index,
            MemberReference member => member.FullName,
            IFormattable scalar => scalar.ToString(null, CultureInfo.InvariantCulture),
            null => "",
            _ => value.ToString()
        };
        string text = method.Attributes + ":" + method.ImplAttributes + ":" + Canon(method.ReturnType) + ":" + method.Body.InitLocals + ":" +
            string.Join(",", method.Body.Variables.Select(v => Canon(v.VariableType))) + "\n" +
            string.Join("\n", instructions.Select(i => i.OpCode.Code + ":" + Operand(i.Operand))) + "\n" +
            string.Join("\n", method.Body.ExceptionHandlers.Select(h => h.HandlerType + ":" + h.CatchType?.FullName + ":" +
                instructions.IndexOf(h.TryStart) + ":" + instructions.IndexOf(h.TryEnd) + ":" + instructions.IndexOf(h.HandlerStart) + ":" +
                instructions.IndexOf(h.HandlerEnd) + ":" + instructions.IndexOf(h.FilterStart)));
        return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
    }
    internal static void Run(string oldReport, string newReport, string oldManaged, string newManaged, string output)
    {
        using var previous = JsonDocument.Parse(File.ReadAllText(oldReport));
        using var current = JsonDocument.Parse(File.ReadAllText(newReport));
        Validate(previous.RootElement, oldManaged);
        Validate(current.RootElement, newManaged);
        var cache = new Dictionary<string, ModuleDefinition>();
        var rows = new List<object>();
        try
        {
            MethodDefinition Find(string directory, JsonElement report, JsonElement target)
            {
                string assemblyName = target.GetProperty("assembly").GetString();
                string path = assemblyName == "Ostrabus" ? report.GetProperty("pluginAssembly").GetString() : Path.Combine(directory, assemblyName + ".dll");
                if (!File.Exists(path)) return null;
                if (!cache.TryGetValue(path, out var module)) cache[path] = module = ModuleDefinition.ReadModule(path);
                var type = Types(module.Types).SingleOrDefault(t => t.FullName == target.GetProperty("type").GetString());
                var parameters = target.GetProperty("parameters").EnumerateArray().Select(p => p.GetString()).ToArray();
                return type?.Methods.SingleOrDefault(m => m.Name == target.GetProperty("name").GetString() && m.Parameters.Select(p => Canon(p.ParameterType)).SequenceEqual(parameters));
            }
            var oldPatches = previous.RootElement.GetProperty("patches").EnumerateArray().ToDictionary(p => p.GetProperty("patch").GetString());
            var currentNames = current.RootElement.GetProperty("patches").EnumerateArray()
                .Select(p => p.GetProperty("patch").GetString()).ToHashSet(StringComparer.Ordinal);
            foreach (var patch in current.RootElement.GetProperty("patches").EnumerateArray())
            {
                string name = patch.GetProperty("patch").GetString();
                var targets = patch.GetProperty("targets").EnumerateArray().ToArray();
                var before = oldPatches.TryGetValue(name, out var oldPatch) ? oldPatch.GetProperty("targets").EnumerateArray().ToArray() : Array.Empty<JsonElement>();
                var oldKeys = before.Select(Key).ToHashSet();
                var newKeys = targets.Select(Key).ToHashSet();
                foreach (var target in targets)
                {
                    var a = Find(oldManaged, previous.RootElement, target); var b = Find(newManaged, current.RootElement, target);
                    string ah = a == null ? null : Body(a), bh = b == null ? null : Body(b);
                    rows.Add(new
                    {
                        patch = name,
                        target = Key(target),
                        previousTarget = oldKeys.Contains(Key(target)),
                        status = b == null ? "unresolved_current" : a == null ? "new_signature_or_target" : ah == bh ? "body_unchanged" : "body_changed",
                        oldHash = ah,
                        newHash = bh
                    });
                }
                foreach (var missing in before.Where(t => !newKeys.Contains(Key(t))))
                    rows.Add(new { patch = name, target = Key(missing), status = "previous_target_missing", previousTarget = true });
                if (targets.Length == 0) rows.Add(new { patch = name, status = "no_reported_original_targets" });
            }
            foreach (var missing in oldPatches.Where(pair => !currentNames.Contains(pair.Key)))
            {
                var targets = missing.Value.GetProperty("targets").EnumerateArray().ToArray();
                foreach (var target in targets)
                    rows.Add(new { patch = missing.Key, target = Key(target), status = "patch_removed", previousTarget = true });
                if (targets.Length == 0) rows.Add(new { patch = missing.Key, status = "patch_removed", previousTarget = true });
            }
            File.WriteAllText(output, JsonSerializer.Serialize(new { scope = "Native direct target body/contract comparison only; not transitive or semantic approval.", rows }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("Compared " + rows.Count + " hook-target rows; detailed receipts: " + output);
        }
        finally { foreach (var module in cache.Values) module.Dispose(); }
    }

    static void Validate(JsonElement report, string managed)
    {
        foreach (var entry in new[] {
            (Path: Path.Combine(managed, "Assembly-CSharp.dll"), Hash: "gameSha256"),
            (Path: report.GetProperty("pluginAssembly").GetString()!, Hash: "pluginSha256") })
        {
            using var stream = File.OpenRead(entry.Path);
            string actual = Convert.ToHexString(SHA256.HashData(stream));
            if (!string.Equals(actual, report.GetProperty(entry.Hash).GetString(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Binding receipt no longer matches assembly bytes: " + entry.Path);
        }
    }
}
