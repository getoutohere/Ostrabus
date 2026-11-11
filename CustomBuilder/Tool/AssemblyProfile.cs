using Mono.Cecil;
using Mono.Cecil.Cil;

static class AssemblyProfile {
    static IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> source) {
        foreach (var type in source) {
            yield return type;
            foreach (var nested in Types(type.NestedTypes)) yield return nested;
        }
    }

    static bool Patch(Mono.Cecil.ICustomAttributeProvider provider) =>
        provider.CustomAttributes.Any(attribute => attribute.AttributeType.FullName == "HarmonyLib.HarmonyPatch");

    static string Name(TypeDefinition type) => type.FullName.Replace('/', '+');

    static void Strip(Mono.Cecil.ICustomAttributeProvider provider) {
        foreach (var attribute in provider.CustomAttributes
            .Where(attribute => attribute.AttributeType.Namespace == "HarmonyLib").ToArray())
            provider.CustomAttributes.Remove(attribute);
    }

    internal static void Finalize(string dll, Plan plan) {
        dll = Path.GetFullPath(dll);
        bool symbols = File.Exists(Path.ChangeExtension(dll, ".pdb"));
        using var resolver = new DefaultAssemblyResolver();
        string game = Ostrabus.Development.InstallationPaths.Game;
        resolver.AddSearchDirectory(Path.Combine(game, "Ostranauts_Data", "Managed"));
        resolver.AddSearchDirectory(Path.Combine(game, "BepInEx", "core"));
        resolver.AddSearchDirectory(Path.GetDirectoryName(dll)!);
        string temporary = Path.Combine(Path.GetDirectoryName(dll)!, "Ostrabus.finalizing.dll");

        using (var assembly = AssemblyDefinition.ReadAssembly(dll, new ReaderParameters {
            ReadSymbols = symbols,
            InMemory = true,
            AssemblyResolver = resolver
        })) {
            if (assembly.CustomAttributes.Any(attribute =>
                attribute.AttributeType.FullName == "System.Reflection.AssemblyMetadataAttribute" &&
                (string)attribute.ConstructorArguments[0].Value == "Ostrabus.Composed")) {
                Verify(dll, plan);
                return;
            }

            var selected = plan.Patches.ToDictionary(patch => patch.Type, StringComparer.Ordinal);
            foreach (var type in Types(assembly.MainModule.Types)) {
                if (!Patch(type)) continue;
                if (!selected.TryGetValue(Name(type), out var hook)) {
                    Strip(type);
                    foreach (var method in type.Methods) Strip(method);
                } else if (hook.Methods != null) {
                    foreach (var method in type.Methods.Where(Patch))
                        if (!hook.Methods.Contains(method.Name, StringComparer.Ordinal)) Strip(method);
                }
            }

            // Match PatchAll's assembly metadata order. Source/name ordering is
            // different and could change the order of equal-priority patches.
            var order = Types(assembly.MainModule.Types).Where(Patch)
                .OrderBy(type => type.MetadataToken.RID).ToArray();
            var awake = Types(assembly.MainModule.Types)
                .Single(type => Name(type) == "Ostrabus.OstrabusPlugin")
                .Methods.Single(method => method.Name == "Awake");
            var tokens = awake.Body.Instructions
                .Where(instruction => instruction.OpCode == OpCodes.Ldtoken && instruction.Operand is TypeReference)
                .ToArray();
            Files.Need(tokens.Length == order.Length, "Unexpected bootstrap type-token layout.");
            for (int i = 0; i < tokens.Length; i++) tokens[i].Operand = order[i];

            var constructor = assembly.CustomAttributes
                .First(attribute => attribute.AttributeType.FullName == "System.Reflection.AssemblyMetadataAttribute")
                .Constructor;
            var composed = new CustomAttribute(constructor);
            composed.ConstructorArguments.Add(new CustomAttributeArgument(assembly.MainModule.TypeSystem.String, "Ostrabus.Composed"));
            composed.ConstructorArguments.Add(new CustomAttributeArgument(assembly.MainModule.TypeSystem.String, "1"));
            assembly.CustomAttributes.Add(composed);
            assembly.Write(temporary, new WriterParameters { WriteSymbols = symbols });
        }

        Verify(temporary, plan);
        File.Move(temporary, dll, true);
        if (symbols) File.Move(Path.ChangeExtension(temporary, ".pdb"), Path.ChangeExtension(dll, ".pdb"), true);
    }

    internal static void Verify(string dll, Plan plan) {
        bool symbols = File.Exists(Path.ChangeExtension(dll, ".pdb"));
        using var assembly = AssemblyDefinition.ReadAssembly(dll, new ReaderParameters { ReadSymbols = symbols });
        var types = Types(assembly.MainModule.Types).ToDictionary(Name, StringComparer.Ordinal);
        var metadata = assembly.CustomAttributes
            .Where(attribute => attribute.AttributeType.FullName == "System.Reflection.AssemblyMetadataAttribute")
            .ToDictionary(
                attribute => (string)attribute.ConstructorArguments[0].Value,
                attribute => (string)attribute.ConstructorArguments[1].Value);

        Files.Need(metadata.TryGetValue("Ostrabus.BuildKind", out string? kind) && kind == "Custom" &&
            metadata.TryGetValue("Ostrabus.Profile", out string? fingerprint) && fingerprint == plan.Fingerprint,
            "Assembly build profile does not match the current recipe; regenerate it.");
        Files.Need(metadata.TryGetValue("Ostrabus.Composed", out string? composed) && composed == "1",
            "Assembly has not been finalized by the custom builder.");

        if (symbols)
            foreach (var document in types.Values.SelectMany(type => type.Methods)
                .SelectMany(method => method.DebugInformation.SequencePoints)
                .Select(point => point.Document.Url).Distinct())
                Files.Need(document.StartsWith("/_/OstrabusCustom/", StringComparison.Ordinal),
                    "PDB contains an unmapped source path: " + document);

        var installed = types.Values.Where(Patch).Select(Name).Order(StringComparer.Ordinal).ToArray();
        Files.Need(installed.SequenceEqual(plan.Patches.Select(hook => hook.Type).Order(StringComparer.Ordinal)),
            "Custom assembly has missing or unselected Harmony patch declarations.");

        var plugin = types["Ostrabus.OstrabusPlugin"];
        Files.Need(plugin.Methods.Any(method => method.Name == "Update") == (plan.TickCalls > 0),
            "Unexpected/missing per-frame Update method.");

        var awake = plugin.Methods.Single(method => method.Name == "Awake");
        Files.Need(!awake.Body.Instructions.Any(instruction =>
            instruction.Operand is MethodReference method && method.Name == "PatchAll"),
            "Custom bootstrap contains blanket PatchAll.");
        Files.Need(awake.Body.Instructions.Count(instruction =>
            instruction.Operand is MethodReference method && method.Name == "CreateClassProcessor") == plan.Patches.Length,
            "Bootstrap patch count differs from selection.");

        var registered = awake.Body.Instructions
            .Where(instruction => instruction.OpCode == OpCodes.Ldtoken && instruction.Operand is TypeReference)
            .Select(instruction => ((TypeReference)instruction.Operand).FullName.Replace('/', '+'));
        Files.Need(registered.Order(StringComparer.Ordinal)
            .SequenceEqual(plan.Patches.Select(hook => hook.Type).Order(StringComparer.Ordinal)),
            "Bootstrap registers unexpected patch types.");

        foreach (var hook in plan.Patches.Where(hook => hook.Methods != null))
            Files.Need(types[hook.Type].Methods.Where(Patch).Select(method => method.Name)
                .Order(StringComparer.Ordinal).SequenceEqual(hook.Methods!.Order(StringComparer.Ordinal)),
                "Method-level patch selection leaked: " + hook.Type);

        var registry = types["Ostrabus.PdaSettings.SettingsRegistry"].Methods.Single(method => method.Name == ".cctor");
        var rows = registry.Body.Instructions.Count(instruction => instruction.OpCode == OpCodes.Newobj &&
            instruction.Operand is MethodReference method &&
            method.DeclaringType.FullName == "Ostrabus.PdaSettings.Setting");
        Files.Need(rows == (plan.PdaAssets ? plan.Settings.Length : 0), "Compiled PDA rows differ from selection.");
        Files.Need(assembly.MainModule.Resources.Any(resource => resource.Name == "Ostrabus.NavConsole.RoundKnob.png") ==
            plan.Included.Contains("proximity-range"), "Unexpected/missing knob resource.");

        void NoReference(string type, string method, string forbidden) {
            var owner = types[type];
            var methods = owner.Methods.Where(member => member.Name == method)
                .Concat(Types(owner.NestedTypes)
                    .Where(nested => nested.Name.StartsWith("<" + method + ">", StringComparison.Ordinal))
                    .SelectMany(nested => nested.Methods));

            foreach (var member in methods.Where(member => member.HasBody))
                Files.Need(!member.Body.Instructions.Any(instruction =>
                    instruction.Operand is MemberReference reference &&
                    reference.FullName.Contains(forbidden, StringComparison.Ordinal)),
                    "Unselected behavior reference: " + type + "." + method + " -> " + forbidden);
        }

        if (!plan.Included.Contains("station-docked")) {
            NoReference("Ostrabus.NavConsole.ProximityCollisionPatch", "Transpiler", "StationDockedCollisions");
            NoReference("Ostrabus.ShipNavigation.AIShipAvoidancePatch", "Transpiler", "StationDockedCollisions");
            NoReference("Ostrabus.ShipNavigation.Collisions.PhysicalCollisions", "Overlap", "StationDockedCollisions::ShipCollision");
        }
        if (!plan.Included.Contains("venus-navigation")) {
            NoReference("Ostrabus.ShipNavigation.RcsReplenishment", "After", "VorbNavPlanGuard");
            NoReference("Ostrabus.ShipNavigation.RetargetFuelPatch", "Postfix", "VorbNavPlanGuard");
        }
        if (!plan.Included.Contains("hull-collisions")) {
            NoReference("Ostrabus.NavConsole.Outlines.EnvelopeBuilder", ".ctor", "CollisionShape/Builder");
            NoReference("Ostrabus.NavConsole.Outlines.HullDisplay", "Reset", "PhysicalCollisions");
        }
        if (!plan.Included.Contains("economy-sff"))
            NoReference("Ostrabus.Orbital.OrbitalStepObservationPatch", "Prefix", "SffEconomy");
        if (!plan.Included.Contains("sff"))
            NoReference("Ostrabus.Orbital.OrbitalStepObservationPatch", "Prefix", "SffSimulation");
        if (!plan.Included.Contains("coast"))
            NoReference("Ostrabus.Orbital.OrbitalGravityPatch", "Postfix", "CoastIntegration");

        Console.WriteLine($"Verified custom assembly: {installed.Length} selected patch classes; {plan.TickCalls} frame calls; PDA assets: {plan.PdaAssets}.");
    }

    internal static void CompareOrder(string dll, string baseline, Plan plan) {
        using var custom = AssemblyDefinition.ReadAssembly(dll);
        using var full = AssemblyDefinition.ReadAssembly(baseline);
        var included = plan.Patches.Select(hook => hook.Type).ToHashSet(StringComparer.Ordinal);

        string[] Order(AssemblyDefinition assembly) => Types(assembly.MainModule.Types)
            .Where(type => included.Contains(Name(type)))
            .OrderBy(type => type.MetadataToken.RID).Select(Name).ToArray();

        var expected = Order(full);
        var actual = Types(custom.MainModule.Types)
            .Single(type => Name(type) == "Ostrabus.OstrabusPlugin")
            .Methods.Single(method => method.Name == "Awake")
            .Body.Instructions
            .Where(instruction => instruction.OpCode == OpCodes.Ldtoken && instruction.Operand is TypeReference)
            .Select(instruction => ((TypeReference)instruction.Operand).FullName.Replace('/', '+'))
            .ToArray();

        Files.Need(actual.SequenceEqual(expected),
            "Custom assembly changed relative patch order compared with normal full build: " +
            string.Join("; ", actual.Zip(expected, (actualType, expectedType) => (actualType, expectedType))
                .Where(pair => pair.actualType != pair.expectedType).Take(4)
                .Select(pair => pair.actualType + " vs " + pair.expectedType)));
        Console.WriteLine("Custom patch order matches the normal full build.");
    }
}
