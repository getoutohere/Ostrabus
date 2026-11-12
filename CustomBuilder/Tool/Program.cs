using System.Text.Json;

try {
    string command = args.FirstOrDefault() ?? "list";

    string Option(string name, string fallback = "") {
        int index = Array.IndexOf(args, name);
        return index >= 0
            ? args.ElementAtOrDefault(index + 1) ?? throw new Exception("Missing " + name)
            : fallback;
    }

    string root = Path.GetFullPath(Option("--root", Environment.CurrentDirectory));
    while (!File.Exists(Path.Combine(root, "Ostrabus.csproj")))
        root = Directory.GetParent(root)?.FullName ?? throw new Exception("Repository root not found.");

    string catalogFile = Path.Combine(root, "CustomBuilder", "features.json");
    var catalog = File.Exists(catalogFile) ? Files.Read<Catalog>(catalogFile) : new Catalog();
    var declarations = Inventory.Scan(root, catalog);

    if (command == "inventory") {
        Console.WriteLine(JsonSerializer.Serialize(declarations, Files.Json));
        return 0;
    }

    Inventory.Validate(catalog, declarations);

    if (command == "format-catalog") {
        var document = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(catalogFile))!;
        foreach (var node in document["features"]!.AsArray()) {
            var feature = node!.AsObject();
            foreach (var property in feature.ToArray())
                if (property.Value is System.Text.Json.Nodes.JsonArray array && array.Count == 0 ||
                    property.Value?.ToJsonString() == "false" ||
                    property.Key == "description" && property.Value?.ToString() == feature["title"]?.ToString())
                    feature.Remove(property.Key);
        }
        var formatting = new JsonSerializerOptions(Files.Json) {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        File.WriteAllText(catalogFile, document.ToJsonString(formatting) + "\n");
        return 0;
    }

    if (command == "self-test") {
        SelfTest.Run(root, catalog, declarations);
        return 0;
    }
    if (command == "preset") {
        var preset = Presets.Create(catalog, Option("--name", "recommended"));
        string output = Option("--output");
        if (output.Length > 0) Files.Write(output, preset);
        else Console.WriteLine(JsonSerializer.Serialize(preset, Files.Json));
        return 0;
    }
    if (command == "list") {
        Console.WriteLine(JsonSerializer.Serialize(catalog, Files.Json));
        return 0;
    }
    if (command == "validate") {
        Console.WriteLine($"Catalog valid: {catalog.Features.Length} features cover {declarations.Length} Harmony patch types.");
        return 0;
    }

    string profilePath = Option("--profile");
    var profile = profilePath.Length > 0 ? Files.Read<Profile>(profilePath) : new Profile { Features = [] };
    var plan = Inventory.Plan(catalog, profile, declarations);

    if (command == "plan") {
        string output = Option("--output");
        if (output.Length > 0) Files.Write(output, plan);
        else Console.WriteLine(JsonSerializer.Serialize(plan, Files.Json));
        return 0;
    }
    if (command == "generate") {
        Composition.Generate(root, Option("--output"), catalog, profile, declarations);
        Console.WriteLine($"Generated {plan.Included.Length} features, {plan.Patches.Length} patch classes; profile {plan.Fingerprint[..12]}.");
        return 0;
    }
    if (command == "finalize") {
        AssemblyProfile.Finalize(Option("--dll"), plan);
        return 0;
    }
    if (command == "verify") {
        AssemblyProfile.Verify(Option("--dll"), plan);
        return 0;
    }
    if (command == "compare-order") {
        AssemblyProfile.CompareOrder(Option("--dll"), Option("--baseline"), plan);
        return 0;
    }
    throw new Exception("Unknown command: " + command);
} catch (Exception error) {
    Console.Error.WriteLine(error.ToString());
    return 1;
}
