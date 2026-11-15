using Microsoft.CodeAnalysis.CSharp.Syntax;

static class SelfTest {
    internal static void Run(string root, Catalog catalog, Declaration[] declarations) {
        int checks = 0;

        void Check(bool value, string name) {
            checks++;
            Files.Need(value, "Self-test failed: " + name);
        }

        void Reject(Action action, string name) {
            bool rejected = false;
            try { action(); }
            catch { rejected = true; }
            Check(rejected, name);
        }

        Plan Make(params string[] ids) => Inventory.Plan(catalog, new Profile { Features = ids }, declarations);
        string Read(string file) => File.ReadAllText(Path.Combine(root, file));
        var pdaHack = Make("quick-pda-hack");
        var rotation = Make("rotation-module");
        Check(rotation.Included.Contains("ai-flight-controls") && rotation.NativeMods.Length == 1 && rotation.NativeMods[0].Name == "OstrabusRotation",
            "Rotation includes its native item and controller-lifetime safeguards");
        Check(rotation.TickCalls == 0 && rotation.Patches.Any(patch => patch.Type == "Ostrabus.NavConsole.Rotation.RotationPhysicsPatch"),
            "Rotation uses physical simulation steps without a plugin frame poll");
        Check(!Make("quick-pda-hack").Included.Contains("rotation-module"), "PDA hacking does not pull in rotation");
        Check(pdaHack.Patches.Length == 0 && pdaHack.TickCalls == 0 && !pdaHack.PdaAssets,
            "PDA hacking needs no game hooks, tick or Settings app");
        Check(pdaHack.NativeMods.Length == 1 && pdaHack.NativeMods[0].Name == "OstrabusPdaHack",
            "PDA hacking selects its native asset payload");
        Check(Make().NativeMods.Length == 0 && Make("pda-settings").NativeMods.Length == 0,
            "PDA hacking assets are independently selected");
        foreach (var mod in catalog.Features.SelectMany(feature => feature.NativeMods))
            Check(File.Exists(Path.Combine(root, mod.Source, "mod_info.json")), "Native asset metadata exists: " + mod.Name);
        Check(Make("barter-index").Included.Contains("barter-writeback"), "optimized barter includes writeback safety");
        Check(Make("scav-assistance").Included.Contains("ai-hold-clock"), "scav recovery includes correct wait clocks");
        Check(!Make("scav-assistance").Included.Contains("scav-return") && !Make("scav-assistance").Included.Contains("orbital-waiting"),
            "scav bugfixes do not implicitly select simulation policies");
        Check(Make("scav-return").Included.Contains("scav-assistance") && !Make("scav-return").Included.Contains("orbital-waiting"),
            "return fallback includes recovery and remains independent of orbital waiting");
        Check(new[] { "orbital", "scav-assistance", "coast", "sff" }.All(Make("orbital-waiting").Included.Contains),
            "physical waiting includes orbital and recovery prerequisites");
        Check(Make("torch-fuel-cache").Patches.Length == 1, "fuel cache remains independently selectable");
        Check(Make("sff-navigation-destination").Included.Length == 1 && Make("sff-navigation-destination").Patches.Length == 1,
            "SFF destination integrity is independent of orbital and torch modifications");
        var flightControls = Make("ai-flight-controls");
        Check(flightControls.Patches.Length == 8 && flightControls.Patches.Any(patch => patch.Type == "Ostrabus.ShipNavigation.FlightPilotAvailabilityPatch") && flightControls.TickCalls == 0,
            "flight control handoffs include native pilot-refusal repair without a polling callback");
        var crewIdentities = Make("ai-crew-identities");
        var consoleBuffer = Make("console-buffer");
        Check(consoleBuffer.Included.Length == 1 && consoleBuffer.Patches.Length == 1 && consoleBuffer.TickCalls == 0 &&
            consoleBuffer.Patches[0].Type == "Ostrabus.MiscBugFixes.ConsoleBufferPatch",
            "console retention is independently selectable without a polling callback");
        var completion = Make("route-completion");
        Check(completion.Included.Length == 1 && completion.Patches.Length == 1 && completion.TickCalls == 0 &&
            completion.Patches[0].Type == "Ostrabus.ShipNavigation.RouteCompletionPatch",
            "route completion repairs both native handoffs without an orbital dependency or frame callback");
        Check(crewIdentities.Included.Length == 1 && crewIdentities.Patches.Length == 1 &&
            crewIdentities.Patches[0].Type == "Ostrabus.MiscBugFixes.AiCrewIdentityPatch" && crewIdentities.TickCalls == 0,
            "fresh AI crew identity protection is independently selectable without polling");
        var population = Make("population-gender");
        Check(population.Included.Length == 1 && population.Patches.Length == 1 && population.TickCalls == 0,
            "population distribution is independently selectable without polling");
        Check(catalog.Features.Single(feature => feature.Id == "population-gender").Category == "simulation" &&
            !Presets.Create(catalog, "bugfixes").Features.Contains("population-gender") &&
            Presets.Create(catalog, "recommended").Features.Contains("population-gender"),
            "population distribution is simulation policy, excluded from bugfix-only builds");
        Check(Make("inspection").Patches.Any(patch => patch.Type == "Ostrabus.Inspection.ShipVelocityRecoverySffPatch") &&
            !Make("sff-navigation-destination").Patches.Any(patch => patch.Type == "Ostrabus.Inspection.ShipVelocityRecoverySffPatch"),
            "recovery Hold SFF protection belongs to the inspection feature only");
        var torchFuel = Make("torch-fuel");
        Check(torchFuel.Included.Contains("torch-fuel-cache"), "course fuel includes accurate native fuel caches");
        Check(torchFuel.Patches.All(patch => patch.Type != "Ostrabus.ShipNavigation.TorchCourses.TorchTransferKernelPatch"),
            "fuel correction does not enable momentum-aware trajectories implicitly");
        var momentum = Make("torch-momentum");
        Check(momentum.Included.Contains("torch-fuel") && momentum.Patches.Any(patch => patch.Type == "Ostrabus.ShipNavigation.TorchCourses.TorchTransferKernelPatch"),
            "momentum trajectories include their UI scope and fuel/control integration");
        Check(!Make("torch-towing").Included.Contains("torch-fuel"), "braced towing stays independent of fuel/controller changes");
        Check(catalog.Features.Single(feature => feature.Id == "barter-zones").Category == "qol", "barter presentation is QoL");
        Check(catalog.Features.Single(feature => feature.Id == "async-ship-loading").Category == "bugfix" &&
            catalog.Features.Single(feature => feature.Id == "barter-writeback").Category == "bugfix", "barter lifecycle and persistence are bugfixes");

        Reject(() => Make("not-a-feature"), "unknown feature");
        Reject(() => Inventory.Resolve(new Catalog {
            Features = [
                new Feature { Id = "a", Requires = ["b"] },
                new Feature { Id = "b", Requires = ["a"] }
            ]
        }, new Profile { Features = ["a"] }), "cycles");
        Reject(() => Inventory.Validate(new Catalog {
            Features = [
                new Feature { Id = "a", Category = "bugfix" },
                new Feature { Id = "a", Category = "bugfix" }
            ]
        }, []), "duplicate IDs");
        Reject(() => Inventory.Validate(new Catalog(), [new Declaration("NewPatch", "new.cs", [])]), "unowned patch");

        foreach (var feature in catalog.Features) {
            var plan = Make(feature.Id);
            Check(plan.Included.Contains(feature.Id), feature.Id + " included");
            foreach (var id in plan.Included)
                foreach (var dependency in catalog.Features.Single(candidate => candidate.Id == id).Requires)
                    Check(plan.Included.Contains(dependency), "closure " + id + " -> " + dependency);
        }
        foreach (string preset in Presets.Names) {
            var profile = Presets.Create(catalog, preset);
            Check(profile.Features.All(id => !catalog.Features.Single(feature => feature.Id == id).Experimental),
                "preset excludes experiments");
            if (preset != "developer")
                Check(profile.Features.All(id => catalog.Features.Single(feature => feature.Id == id).Category != "developer" ||
                    (id == "legacy-commands" && preset is "recommended" or "full")),
                    "preset excludes diagnostics");
            _ = Inventory.Plan(catalog, profile, declarations);
        }

        var recommended = Presets.Create(catalog, "recommended");
        var recommendedPlan = Inventory.Plan(catalog, recommended, declarations);
        foreach (var feature in catalog.Features.Where(feature => feature.Category == "simulation" && !feature.Hidden && !feature.Experimental))
            Check(recommended.Features.Contains(feature.Id) == (feature.Id != "ceres-navigation"),
                "recommended simulation selection: " + feature.Id);
        Check(!recommendedPlan.Included.Contains("ceres-navigation"), "recommended dependencies do not restore Ceres guardrails");
        Check(recommendedPlan.Included.Contains("legacy-commands"), "recommended includes massfix maintenance commands");
        Check(recommendedPlan.Included.Contains("navigation-targets"), "recommended includes navfix recovery");
        Check(Make("navigation-targets").Patches.Any(patch => patch.Type == "Ostrabus.MiscBugFixes.ShipStellarConversionTargetsPatch"),
            "target lifetimes include atomic asteroid conversion");
        var navigation = Make("navigation-targets");
        Check(navigation.Patches.Any(patch => patch.Type == "Ostrabus.ShipNavigation.NavyCandidateFuelPatch") &&
            navigation.Patches.Any(patch => patch.Type == "Ostrabus.ShipNavigation.LowFuelRetreatProgressPatch") &&
            !navigation.Included.Contains("ceres-navigation"), "general fuel/retreat repairs ship without Ceres policy");
        Check(Make("ceres-navigation").Included.Contains("ai-return"), "Ceres policy installs native-priority return integration");
        var civilianCoordinates = Make("civilian-sff-coordinates");
        Check(civilianCoordinates.Patches.Length == 1 && civilianCoordinates.TickCalls == 0,
            "civilian coordinate repair has one independent transpiler and no frame callback");
        var civilianVisits = Make("civilian-visits");
        Check(civilianVisits.Included.Contains("ai-hold-clock") && !civilianVisits.Included.Contains("civilian-asteroid-visits"),
            "civilian wait repair includes clocks without the mining-duration policy");
        var asteroidVisits = Make("civilian-asteroid-visits");
        Check(asteroidVisits.Included.Contains("civilian-visits") && asteroidVisits.TickCalls == 0 &&
            asteroidVisits.Patches.Any(patch => patch.Type == "Ostrabus.ShipNavigation.CivilianAsteroidVisitPatch"),
            "asteroid duration policy includes its ordinary-visit mechanism without a frame callback");
        Check(civilianVisits.TickCalls == 0 && civilianVisits.Included.Contains("navigation-targets") && civilianVisits.Included.Contains("ai-crew-integrity"),
            "civilian visit lifetime includes saved-target and connected-hull protection without a frame callback");
        var visitPatches = declarations.Where(declaration => declaration.File == "MiscBugFixes/CivilianVisitsPatch.cs").Select(declaration => declaration.Type).ToArray();
        Check(visitPatches.Length == 6 && visitPatches.All(type => civilianVisits.Patches.Any(patch => patch.Type == type)),
            "civilian visits cover selection, Main, retirement, registration, SFF and attached retirement");
        var full = Inventory.Plan(catalog, Presets.Create(catalog, "full"), declarations);
        Check(recommendedPlan.Included.All(id => full.Included.Contains(id)), "full remains a superset of recommended");

        string plugin = Read("OstrabusPlugin.cs");
        foreach (string[] ids in new[] {
            Array.Empty<string>(), new[] { "door-autoclose" }, new[] { "crew-hotkeys" },
            new[] { "altwork" }, new[] { "inspection" }
        }) {
            var profile = new Profile { Features = ids };
            var plan = Make(ids);
            var generated = Inventory.Parse(Composition.Bootstrap(plugin, Inventory.Resolve(catalog, profile), plan));
            Check(!generated.ContainsDiagnostics, "bootstrap parses");
            Check(generated.DescendantNodes().OfType<MethodDeclarationSyntax>()
                .Any(method => method.Identifier.ValueText == "Update") == (plan.TickCalls > 0), "no unused Update");
            Check(!generated.ToString().Contains("PatchAll("), "no blanket patching");
            Check(generated.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Count(invocation => invocation.Expression.ToString().EndsWith(".CreateClassProcessor")) == plan.Patches.Length,
                "explicit registration count");
        }

        string registry = Read("PdaSettings/SettingsRegistry.cs");
        foreach (string[] ids in new[] {
            new[] { "pda-settings", "crew-hotkeys" }, new[] { "pda-settings" }, new[] { "crew-hotkeys" },
            new[] { "pda-settings", "torchtrace" }
        }) {
            var plan = Make(ids);
            string generated = Composition.Settings(registry, plan);
            var rows = Inventory.Parse(generated).DescendantNodes().OfType<ObjectCreationExpressionSyntax>()
                .Where(row => row.Type.ToString() == "Setting")
                .Select(row => row.ArgumentList!.Arguments[0].Expression.ToString().Trim('"')).ToArray();
            Check(rows.Order().SequenceEqual((plan.PdaAssets ? plan.Settings : []).Order()),
                "PDA rows reflect selection");
        }

        string Prune(string file, params string[] ids) => SourcePruner.Apply(file, Read(file), Make(ids).Included);

        string proximity = "NavConsole/ProximityCollisionPatch.cs";
        Check(!Prune(proximity, "proximity-range").Contains("IgnoreAsObstacle"),
            "range alone preserves collisions");
        string station = Prune(proximity, "station-docked");
        Check(station.Contains("IgnoreAsObstacle") && !station.Contains("static void Prefix("),
            "station alone omits range callback");
        Check(!Prune("Navigation/AIShipAvoidancePatch.cs", "ai-avoidance")
            .Contains("nameof(StationDockedCollisions.IgnoreAsObstacle)"), "avoidance alone preserves parked hulls");
        Check(!Prune("Navigation/Collisions/PhysicalCollisions.cs", "hull-collisions")
            .Contains("StationDockedCollisions.ShipCollision(a, b)"), "physical collision policy independent");
        Check(!Prune("NavConsole/Outlines/ContourBuilder.cs", "hull-outlines")
            .Contains("new CollisionShape.Builder"), "outlines omit collision covers");

        string refill = Prune("Navigation/RcsReplenishment.cs", "rcs-refill");
        Check(refill.Contains("Rebase(ship, capture.Plan, added);") && !refill.Contains("RecordFuelAdded"),
            "retain refill without Venus observer");

        string orbit = Prune("Orbital/OrbitalPatches.cs", "orbital");
        Check(!orbit.Contains("SffEconomy.Begin(") && !orbit.Contains("SffSimulation.Begin("),
            "orbital alone omits SFF launch");
        Check(!Prune("Orbital/OrbitalPatches.cs", "sff").Contains("SffEconomy.Begin("),
            "SFF alone omits economy launch");
        Check(Prune("Orbital/OrbitalPatches.cs", "economy-sff").Contains("SffEconomy.Begin("),
            "economy launch included");

        foreach (string file in Inventory.Sources(root, catalog))
            Check(!Inventory.Parse(Prune(file)).ContainsDiagnostics, "empty composition syntax " + file);
        Check(Make("station-docked", "proximity-range").Patches
            .Count(hook => hook.Type == "Ostrabus.NavConsole.ProximityCollisionPatch") == 1,
            "shared patch deduplicated");
        Check(Make("social-filter").Patches.Single().Methods!
            .SequenceEqual(new[] { "GUIPDA_GetKnownSocialContacts_Postfix" }), "mixed file method selection");
        Check(Make("door-autoclose", "door-autoclose").Fingerprint == Make("door-autoclose").Fingerprint,
            "duplicate requests normalized");

        Console.WriteLine($"Custom builder self-test: {checks} checks passed.");
    }
}
