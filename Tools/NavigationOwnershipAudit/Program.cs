using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

string game = Ostrabus.Development.InstallationPaths.Game;
string binary = Path.Combine(game, "Ostranauts_Data/Managed/Assembly-CSharp.dll");
using var assembly = AssemblyDefinition.ReadAssembly(binary);
IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> types) {
    foreach (var type in types) {
        yield return type;
        foreach (var nested in Types(type.NestedTypes)) yield return nested;
    }
}
if (args.Length == 3 && args[0] == "method") {
    var methods = Types(assembly.MainModule.Types).Where(type => type.FullName == args[1])
        .SelectMany(type => type.Methods).Where(method => method.Name == args[2] && method.HasBody).ToArray();
    if (methods.Length == 0) throw new ArgumentException("No matching method");
    foreach (var method in methods) {
        Console.WriteLine(method.FullName);
        foreach (var instruction in method.Body.Instructions) Console.WriteLine(instruction);
    }
    return;
}
if (args.Length == 1 && (args[0] == "destruction" || args[0] == "factory")) {
    foreach (var method in Types(assembly.MainModule.Types).SelectMany(type => type.Methods).Where(method => method.HasBody)) {
        var code = method.Body.Instructions;
        for (int i = 0; i < code.Count; i++) {
            if (code[i].Operand is not MethodReference target) continue;
            if (args[0] == "factory") {
                if (target.DeclaringType.FullName != "DataHandler" || target.Name != "GetCondOwner" ||
                    target.Parameters.Count != 2 && target.Parameters.Count != 8) continue;
            } else if (target.DeclaringType.FullName != "Ship" || target.Name != "Destroy") continue;
            Console.WriteLine(method.FullName);
            int preceding=args[0] == "factory" ? 14 : 3;
            foreach (var instruction in code.Skip(Math.Max(0, i - preceding)).Take(preceding+2)) Console.WriteLine("  " + instruction);
        }
    }
    return;
}
if (args.Length == 1 && args[0] == "personnel") {
    string personnelOutput = Path.GetFullPath("obj/ObituaryStudy-20261119");
    Directory.CreateDirectory(personnelOutput);
    var rows = new List<object>();
    var totals = new SortedDictionary<string, int>();
    void Inspect(AssemblyDefinition image, string source) {
        foreach (var method in Types(image.MainModule.Types).SelectMany(type => type.Methods).Where(method => method.HasBody)) {
            var code = method.Body.Instructions;
            for (int i = 0; i < code.Count; i++) {
                string? kind = null, symbol = null;
                if (code[i].Operand is FieldReference field && field.Name == "bAlive" &&
                    (field.DeclaringType.FullName == "CondOwner" || field.DeclaringType.FullName == "PersonSpec") &&
                    (code[i].OpCode == OpCodes.Stfld || code[i].OpCode == OpCodes.Ldflda)) {
                    kind = code[i].OpCode == OpCodes.Stfld ? "alive-field-store" : "alive-field-address";
                    symbol = field.FullName;
                } else if (code[i].Operand is MethodReference target && code[i].OpCode.FlowControl == FlowControl.Call &&
                    ((target.DeclaringType.FullName == "CondOwner" && new[] { "set_Kill", "Destroy", "RemoveFromCurrentHome" }.Contains(target.Name)) ||
                     (target.DeclaringType.FullName == "Ship" && target.Name == "Destroy"))) {
                    kind = "call"; symbol = target.FullName;
                }
                if (kind == null) continue;
                string group = source + ": " + symbol;
                totals.TryGetValue(group, out int count); totals[group] = count + 1;
                rows.Add(new { source, caller = method.FullName, offset = code[i].Offset, kind, symbol,
                    context = code.Skip(Math.Max(0, i - 4)).Take(Math.Min(7, code.Count - Math.Max(0, i - 4))).Select(instruction => instruction.ToString()).ToArray() });
            }
        }
    }
    Inspect(assembly, "native");
    string plugin = Path.GetFullPath("bin/Debug/netstandard2.1/Ostrabus.dll");
    if (File.Exists(plugin)) { using var own = AssemblyDefinition.ReadAssembly(plugin); Inspect(own, "ostrabus"); }
    var personnelReport = new { assemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(binary))).ToLowerInvariant(),
        assemblyMvid = assembly.MainModule.Mvid, pluginSha256 = File.Exists(plugin) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(plugin))).ToLowerInvariant() : null,
        totals, caveat = "Direct IL stores/calls only. Restoration writes are not deaths; Destroy is also unload/replacement. Reflection, aliases, transient state and transitive reachability need separate review.", rows };
    File.WriteAllText(Path.Combine(personnelOutput, "personnel-lifecycle-sites.json"), JsonSerializer.Serialize(personnelReport, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    Console.WriteLine(JsonSerializer.Serialize(new { personnelReport.assemblyMvid, personnelReport.assemblySha256, totals }, new JsonSerializerOptions { WriteIndented = true }));
    return;
}
bool LifetimeCall(MethodReference method) => method.DeclaringType.FullName switch {
    "AIShipManager" => new[] { "UnregisterShip", "AddAIToDict", "AddAIToShip", "RegisterShip", "RunAIQueue", "RegionCleanup", "FFWD" }.Contains(method.Name),
    "Ostranauts.Ships.AIPilots.AIShip" => new[] { "SetupAI", "ResetShipState", "RunAI", "FFWD" }.Contains(method.Name),
    "Ostranauts.Ships.AIShipQueue" => new[] { "Dequeue", "UnregisterShip", "Clear", "Fill" }.Contains(method.Name),
    "Ship" => new[] { "Maneuver", "StopManeuver", "SetThrust", "ClearShipTarget", "set_HideFromSystem" }.Contains(method.Name),
    "ShipSitu" => new[] { "ResetNavData", "ResetAccelerations", "CopyFrom", "TimeAdvance" }.Contains(method.Name),
    "Ostranauts.ShipGUIs.Utilities.NavData" => new[] { "TimeAdvance", ".ctor" }.Contains(method.Name),
    "CrewSim" => new[] { "DockAndDespawn", "MoveCO" }.Contains(method.Name),
    _ => false
};
bool ControlField(FieldReference field) => field.DeclaringType.FullName == "ShipSitu" &&
    new[] { "NavData", "vAccIn", "vAccRCS", "fA" }.Contains(field.Name);
var records = new List<object>();
var resetters = new SortedSet<string>();
var writers = new SortedSet<string>();
int calls = 0, stores = 0, addresses = 0;
int unregistrationSites = 0;
var unregistrationCallers = new SortedSet<string>();
var withdrawalSites = new SortedDictionary<string, int>();
foreach (var method in Types(assembly.MainModule.Types).SelectMany(type => type.Methods).Where(method => method.HasBody)) {
    foreach (var instruction in method.Body.Instructions) {
        string? kind = null, symbol = null;
        if (instruction.OpCode.FlowControl == FlowControl.Call && instruction.Operand is MethodReference target && LifetimeCall(target)) {
            kind = "call"; symbol = target.FullName; calls++;
            if (target.DeclaringType.FullName == "AIShipManager" && target.Name == "UnregisterShip") {
                unregistrationSites++; unregistrationCallers.Add(method.FullName);
                string key = method.DeclaringType.FullName + "::" + method.Name;
                withdrawalSites.TryGetValue(key, out int count); withdrawalSites[key] = count + 1;
            }
            if (target.DeclaringType.FullName == "ShipSitu" && target.Name == "ResetNavData") resetters.Add(method.FullName);
        } else if (instruction.Operand is FieldReference field && ControlField(field)) {
            if (instruction.OpCode == OpCodes.Stfld) { kind = "direct-store"; stores++; writers.Add(method.FullName); }
            else if (instruction.OpCode == OpCodes.Ldflda) { kind = "address-access"; addresses++; }
            symbol = field.FullName;
        }
        if (kind != null) records.Add(new { caller = method.FullName, offset = instruction.Offset, kind, symbol });
    }
}
bool checkReview = args.Length == 1 && args[0] == "--check-review";
string output = Path.GetFullPath(checkReview ? "obj/NavigationOwnershipAudit" : args.FirstOrDefault() ?? "obj/NavigationOwnershipAudit");
Directory.CreateDirectory(output);
var options = new JsonSerializerOptions { WriteIndented = true };
using var catalog = JsonDocument.Parse(File.ReadAllText("Tools/NavigationOwnershipAudit/withdrawal-review.json"));
string binaryHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(binary))).ToLowerInvariant();
var report = new {
    schemaVersion = 1,
    // A version label is justified only for the exact reviewed binary.
    gameVersion = catalog.RootElement.GetProperty("assemblySha256").GetString() == binaryHash
        ? catalog.RootElement.GetProperty("gameVersion").GetString() : null,
    assemblySha256 = binaryHash,
    counts = new { calls, directStores = stores, addressAccesses = addresses, unregistrationSites, unregistrationCallerMethods = unregistrationCallers.Count },
    caveat = "Direct IL inventory. Address accesses include reads; aliases, reflection and transitive calls need review. Counts are not defects, hooks or completeness proof.",
    records
};
File.WriteAllText(Path.Combine(output, "native-control-sites.json"), JsonSerializer.Serialize(report, options) + "\n");
File.WriteAllText(Path.Combine(output, "route-reset-callers.txt"), string.Join("\n", resetters) + "\n");
File.WriteAllText(Path.Combine(output, "control-field-writers.txt"), string.Join("\n", writers) + "\n");
File.WriteAllText(Path.Combine(output, "unregistration-callers.txt"), string.Join("\n", unregistrationCallers) + "\n");
var reviewed = catalog.RootElement.GetProperty("callers").EnumerateArray().ToDictionary(
    item => item.GetProperty("caller").GetString()!, item => item.GetProperty("sites").GetInt32());
var missing = withdrawalSites.Keys.Except(reviewed.Keys).ToArray();
var removed = reviewed.Keys.Except(withdrawalSites.Keys).ToArray();
var changedCounts = withdrawalSites.Where(pair => reviewed.TryGetValue(pair.Key, out int count) && count != pair.Value)
    .Select(pair => pair.Key).ToArray();
bool sameAssembly = catalog.RootElement.GetProperty("assemblySha256").GetString() == report.assemblySha256;
bool covered = sameAssembly && missing.Length == 0 && removed.Length == 0 && changedCounts.Length == 0;
File.WriteAllText(Path.Combine(output, "withdrawal-review-coverage.json"), JsonSerializer.Serialize(new {
    reviewedAssemblyMatches = sameAssembly, reviewedCallerCount = reviewed.Count, nativeCallerCount = withdrawalSites.Count,
    missing, removed, changedCounts, covered,
    meaning = "Coverage means all direct withdrawal sites have a disposition for this exact assembly; it does not prove runtime compatibility. Any assembly change requires revalidation."
}, options) + "\n");
if (checkReview && !covered) Environment.ExitCode = 1;
Console.WriteLine(JsonSerializer.Serialize(new { report.assemblySha256, report.counts, routeResetMethods = resetters.Count, directWriterMethods = writers.Count }, options));
Console.WriteLine("Withdrawal review coverage: " + (covered ? "current" : "REVALIDATION REQUIRED"));
