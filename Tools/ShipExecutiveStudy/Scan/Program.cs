using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

string root = Path.GetFullPath(".");
string output = Path.Combine(root, "obj/ShipExecutiveStudy");
Directory.CreateDirectory(output);
string game = Ostrabus.Development.InstallationPaths.Game;
var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(Path.Combine(game, "Ostranauts_Data/Managed"));
resolver.AddSearchDirectory(Path.Combine(game, "BepInEx/core"));
using var assembly = AssemblyDefinition.ReadAssembly(Path.Combine(game, "Ostranauts_Data/Managed/Assembly-CSharp.dll"), new ReaderParameters { AssemblyResolver = resolver });
IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> types) { foreach (var type in types) { yield return type; foreach (var inner in Types(type.NestedTypes)) yield return inner; } }
var types = Types(assembly.MainModule.Types).ToArray();
string designPath = Path.Combine(root, "Navigation/Architecture/execution-boundaries.json");
if (File.Exists(designPath))
{
    using var design = JsonDocument.Parse(File.ReadAllText(designPath));
    var symbols = design.RootElement.GetProperty("boundaries").EnumerateArray()
        .SelectMany(b => b.GetProperty("nativeSymbols").EnumerateArray())
        .Select(s => s.GetString()!).Distinct().ToArray();
    var missing = symbols.Where(symbol =>
    {
        bool constructor = symbol.EndsWith("..ctor", StringComparison.Ordinal);
        int split = constructor ? symbol.Length - 6 : symbol.LastIndexOf('.');
        string typeName = symbol[..split];
        string methodName = constructor ? ".ctor" : symbol[(split + 1)..];
        return !types.Any(t => t.FullName == typeName && t.Methods.Any(m => m.Name == methodName));
    }).ToArray();
    if (missing.Length > 0) throw new Exception("Unbound design references: " + string.Join(", ", missing));
    Console.Error.WriteLine("Validated " + symbols.Length + " distinct native method references in the design.");
}
bool Derived(TypeDefinition type, string name)
{
    for (TypeDefinition? current = type; current != null;)
    {
        if (current.FullName == name) return true;
        try { current = current.BaseType?.Resolve(); } catch { return false; }
    }
    return false;
}
string Declares(TypeDefinition type, string method)
{
    for (TypeDefinition? current = type; current != null;)
    {
        if (current.Methods.Any(m => m.Name == method)) return current.FullName;
        try { current = current.BaseType?.Resolve(); } catch { return "unresolved"; }
    }
    return "absent";
}
var commands = types.Where(t => !t.IsAbstract && Derived(t, "Ostranauts.Ships.Commands.BaseCommand")).OrderBy(t => t.FullName).ToArray();
var contractNames = new[] { "RunCommand", "OnExit", "ResolveInstantly", "CanExecute", "get_SaveData", "GetSaveData", "LoadSaveData" };
var capabilities = commands.Select(t => new
{
    type = t.FullName,
    node = t.Interfaces.Any(i => i.InterfaceType.FullName == "Ostranauts.Ships.Commands.ICommandNode"),
    inheritedContracts = contractNames.ToDictionary(n => n, n => Declares(t, n)),
    fields = t.Fields.Where(f => !f.IsStatic).Select(f => new { name = f.Name, type = f.FieldType.FullName, readOnly = f.IsInitOnly }).ToArray(),
    constructedCommands = t.Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).Where(i => i.OpCode == OpCodes.Newobj && i.Operand is MethodReference r && r.DeclaringType.Namespace == "Ostranauts.Ships.Commands").Select(i => ((MethodReference)i.Operand).DeclaringType.FullName).Distinct().Order().ToArray()
}).ToArray();
var calls = types.SelectMany(t => t.Methods).Where(m => m.HasBody).SelectMany(m => m.Body.Instructions.Where(i => i.Operand is MethodReference && i.OpCode.FlowControl == FlowControl.Call).Select(i => new { caller = m.FullName, callerType = m.DeclaringType.FullName, offset = i.Offset, target = (MethodReference)i.Operand })).ToArray();
bool Control(MethodReference m) =>
    m.DeclaringType.FullName == "Ship" && new[] { "Maneuver", "SetThrust", "ClearShipTarget", "set_NavData", "Dock", "Undock", "AIRefuel" }.Contains(m.Name) ||
    m.DeclaringType.FullName == "ShipSitu" && new[] { "ResetNavData", "set_NavData", "TimeAdvance", "PlaceOrbitPosition", "SetThrust" }.Contains(m.Name) ||
    m.DeclaringType.FullName == "Ostranauts.Ships.AIPilots.AIShip" && new[] { "RunAI", "FFWD", "AddCommand", "AddCommandLoot", "AddUrgentCommand", "GetBehaviorTreeSaveData", "LoadBehaviorTreeSave" }.Contains(m.Name) ||
    m.DeclaringType.FullName == "AIShipManager" && new[] { "AddAIToShip", "UnregisterShip", "RegisterShip", "FFWD", "RegionCleanup", "AIShipCleanup" }.Contains(m.Name) ||
    m.DeclaringType.FullName == "StarSystem" && m.Name == "Update" ||
    m.DeclaringType.Namespace == "Ostranauts.Ships.Commands" && new[] { "RunCommand", "ResolveInstantly", "OnExit" }.Contains(m.Name);
var selected = calls.Where(c => Control(c.target)).Select(c => new { c.caller, c.callerType, c.offset, callee = c.target.FullName }).ToArray();
var writes = types.SelectMany(t => t.Methods).Where(m => m.HasBody).SelectMany(m => m.Body.Instructions.Where(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference).Select(i => new { caller = m.FullName, callerType = m.DeclaringType.FullName, offset = i.Offset, field = (FieldReference)i.Operand })).Where(c =>
    c.field.DeclaringType.FullName == "Ship" && new[] { "shipScanTarget", "shipCombatTarget", "shipSituTarget", "fAIPauseTimer", "fAIDockingExpire" }.Contains(c.field.Name) ||
    c.field.DeclaringType.FullName == "ShipSitu" && new[] { "vAccIn", "vAccRCS", "vVelX", "vVelY", "vPosx", "vPosy", "fA", "fW" }.Contains(c.field.Name) ||
    c.field.DeclaringType.FullName == "Ostranauts.Ships.AIPilots.AIShip" && new[] { "_activeReaction", "_activeCommand", "_behaviorTree", "_aiCharacter" }.Contains(c.field.Name)
).Select(c => new { c.caller, c.callerType, c.offset, target = c.field.FullName }).ToArray();
var json = new JsonSerializerOptions { WriteIndented = true };
void Save(string file, object data) => File.WriteAllText(Path.Combine(output, file), JsonSerializer.Serialize(data, json) + "\n");
Save("command-capabilities.json", capabilities);
Save("native-control-calls.json", selected);
Save("native-control-writes.json", writes);
Save("entrypoints.json", selected.Where(c => !c.callerType.StartsWith("Ostranauts.Ships.Commands.", StringComparison.Ordinal) && !c.callerType.StartsWith("Ostranauts.Ships.AIPilots.", StringComparison.Ordinal)).ToArray());
string Csv(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
File.WriteAllLines(Path.Combine(output, "native-control-calls.csv"), new[] { "caller,offset,callee" }.Concat(selected.Select(c => $"{Csv(c.caller)},{c.offset},{Csv(c.callee)}")));
using var plugin = AssemblyDefinition.ReadAssembly(Path.Combine(root, "bin/Debug/netstandard2.1/Ostrabus.dll"), new ReaderParameters { AssemblyResolver = resolver });
var patchTypes = Types(plugin.MainModule.Types).Where(t => t.CustomAttributes.Any(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch")).ToArray();
var relevant = patchTypes.Where(t => t.Namespace.StartsWith("Ostrabus.ShipNavigation", StringComparison.Ordinal) || t.Namespace.StartsWith("Ostrabus.Haulers", StringComparison.Ordinal) || t.Namespace.StartsWith("Ostrabus.CeresNavigation", StringComparison.Ordinal) || t.Namespace.StartsWith("Ostrabus.Orbital", StringComparison.Ordinal) || t.Name.Contains("AI") || t.Name.Contains("Sff") || t.Name.Contains("Scav") || t.Name.Contains("Shipboard") || t.Name.Contains("Vorb")).Select(t => new
{
    type = t.FullName,
    declarations = t.CustomAttributes.Where(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch").Select(a => a.ConstructorArguments.Select(v => v.Value?.ToString()).ToArray()).ToArray(),
    callbacks = t.Methods.Where(m => m.CustomAttributes.Any(a => a.AttributeType.Namespace == "HarmonyLib")).Select(m => new { name = m.Name, kind = m.CustomAttributes.Select(a => a.AttributeType.Name).ToArray() }).ToArray()
}).ToArray();
Save("relevant-patch-declarations.json", relevant);
var commandNames = commands.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
string dataRoot = Path.Combine(game, "Ostranauts_Data/StreamingAssets/data");
using var lootDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dataRoot, "loot/loot.json")), new JsonDocumentOptions { MaxDepth = 256 });
var commandLoot = lootDoc.RootElement.EnumerateArray().Where(r => r.TryGetProperty("aCOs", out var list) && list.ValueKind == JsonValueKind.Array && list.EnumerateArray().Any(v => v.ValueKind == JsonValueKind.String && commandNames.Contains(v.GetString()!.Split('=')[0]))).Select(r => r.Clone()).ToArray();
Save("command-loot.json", new { path = "loot/loot.json", caveat = "Raw shipped records; command strings remain the native asset interface. This is not a resolved mod/load-order graph.", records = commandLoot });
var aliases = commandLoot.Select(r => r.GetProperty("strName").GetString()!).ToHashSet(StringComparer.Ordinal);
using var interactions = JsonDocument.Parse(File.ReadAllText(Path.Combine(dataRoot, "interactions/interactions_ship_to_ship.json")), new JsonDocumentOptions { MaxDepth = 256 });
var directiveInteractions = interactions.RootElement.EnumerateArray().Where(r => r.EnumerateObject().Any(p => p.Value.ValueKind == JsonValueKind.String && aliases.Contains(p.Value.GetString()!))).Select(r => r.Clone()).ToArray();
Save("command-interactions.json", new { path = "interactions/interactions_ship_to_ship.json", caveat = "Core ship-to-ship records directly referencing command loot. Generated stakes and indirect trigger chains need separate review; no completeness claim.", records = directiveInteractions });
Console.Error.WriteLine($"Asset boundary: {commandLoot.Length} command-bearing loot records; {directiveInteractions.Length} directly referencing core ship-to-ship interactions.");
var summary = new
{
    gameAssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(game, "Ostranauts_Data/Managed/Assembly-CSharp.dll")))).ToLowerInvariant(),
    pluginSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, "bin/Debug/netstandard2.1/Ostrabus.dll")))).ToLowerInvariant(),
    pilotTypes = types.Where(t => !t.IsAbstract && Derived(t, "Ostranauts.Ships.AIPilots.BaseCharacter")).Select(t => t.FullName).Order().ToArray(),
    commandCount = commands.Length,
    leafOnExitImplementations = commands.Where(t => !t.Interfaces.Any(i => i.InterfaceType.Name == "ICommandNode") && Declares(t, "OnExit") != "Ostranauts.Ships.Commands.BaseCommand").Select(t => t.FullName).ToArray(),
    selectedCallSites = selected.Length,
    controlWriteSites = writes.Length,
    commandRunCallers = selected.Where(c => c.callee.Contains("::RunCommand(")).Select(c => c.caller).Distinct().Count(),
    livePatchClasses = patchTypes.Length,
    relevantPatchDeclarations = relevant.Length,
    timeAdvanceEntrypoints = selected.Where(c => c.callee.Contains("StarSystem::Update(")).ToArray(),
    queueAdmissionCallers = selected.Where(c => c.callee.Contains("AIShip::AddCommand") || c.callee.Contains("AIShip::AddUrgentCommand")).ToArray(),
    caveat = "Static direct call/write sites and declared overrides, not transitive ownership proof, runtime hook counts, reachability proof or semantic completeness. No patch factory/game initializer was executed."
};
Save("scan-summary.json", summary);
Console.WriteLine(JsonSerializer.Serialize(summary, json));
