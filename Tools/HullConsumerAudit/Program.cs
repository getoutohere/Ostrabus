using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;

// Metadata only. Audit the shipped assembly, including generated methods, rather
// than assuming a text search found every consumer of the radius contract.
if (args.Length != 1) throw new ArgumentException("Pass Assembly-CSharp.dll.");
string path = Path.GetFullPath(args[0]);
using var module = ModuleDefinition.ReadModule(path);
IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> types) {
    foreach (var type in types) {
        yield return type;
        foreach (var nested in Types(type.NestedTypes)) yield return nested;
    }
}
bool RadiusReference(MemberReference member) => member switch {
    FieldReference field => field.DeclaringType.FullName == "ShipSitu" && field.Name == "Size",
    MethodReference method => method.DeclaringType.FullName switch {
        "ShipSitu" => method.Name is "GetRadiusAU" or "get_Size" or "set_Size" or "SetSize" or "PlaceOrbitPosition" or "PushbackFrom",
        "CollisionManager" => method.Name is "GetCollisionDistanceAU" or "GetRangeToCollisionAU" or "GetRangeToCollisionKM" or "WillCollide",
        "Ship" => method.Name == "PlaceAtAirlocks",
        "Ostranauts.ShipGUIs.Utilities.ShipDraw" => method.Name is "get_fRadiusAU" or "get_fRadiusM",
        _ => false
    },
    _ => false
};
var rows = Types(module.Types).SelectMany(type => type.Methods).Where(method => method.HasBody)
    .Select(method => new {
        caller = method.FullName,
        references = method.Body.Instructions.Where(instruction => instruction.Operand is MemberReference member && RadiusReference(member))
            .Select(instruction => new { offset = instruction.Offset, operation = instruction.OpCode.Name, target = ((MemberReference)instruction.Operand).FullName }).ToArray()
    }).Where(row => row.references.Length > 0).OrderBy(row => row.caller).ToArray();
Console.WriteLine(JsonSerializer.Serialize(new {
    assemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(),
    scope = "Direct radius/size/placement references, including ShipDraw wrappers and compiler-generated methods. Callers of these callers still require source review; this is not a claim of complete behavioral proof.",
    methods = rows.Length, references = rows.Sum(row => row.references.Length), consumers = rows
}, new JsonSerializerOptions { WriteIndented = true }));
