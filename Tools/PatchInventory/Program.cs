using Mono.Cecil;
using System.Text.Json;

// Metadata only: never loads the plugin or installs patches in a game/runtime.
if (args.Length != 2) throw new ArgumentException("Pass the built Ostrabus DLL path and BepInEx/core directory.");
using var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(Path.GetFullPath(args[1]));
using var module = ModuleDefinition.ReadModule(args[0], new ReaderParameters { AssemblyResolver = resolver });
IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> types) {
    foreach (var type in types) { yield return type; foreach (var nested in Types(type.NestedTypes)) yield return nested; }
}
object Attribute(CustomAttribute a) => new {
    name = a.AttributeType.Name,
    arguments = a.ConstructorArguments.Select(v => v.Value?.ToString()).ToArray()
};
var rows = Types(module.Types).Where(t => t.CustomAttributes.Any(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch"))
    .OrderBy(t => t.FullName).Select(t => new {
        type = t.FullName,
        targets = t.CustomAttributes.Where(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch").Select(Attribute).ToArray(),
        methods = t.Methods.Where(m => m.Name is "Prefix" or "Postfix" or "Transpiler" or "Finalizer" or "TargetMethod" or "TargetMethods" ||
            m.CustomAttributes.Any(a => a.AttributeType.Namespace == "HarmonyLib"))
            .Select(m => new { name = m.Name, attributes = m.CustomAttributes.Where(a => a.AttributeType.Namespace == "HarmonyLib").Select(Attribute).ToArray() }).ToArray()
    }).ToArray();
Console.WriteLine(JsonSerializer.Serialize(new {
    scope = "Static HarmonyPatch type declarations in the supplied DLL. Not installed hooks, target-method count or a performance measurement. Dynamic diagnostic patches are outside this count.",
    patchTypes = rows.Length, patches = rows
}, new JsonSerializerOptions { WriteIndented = true }));
