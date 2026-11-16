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
