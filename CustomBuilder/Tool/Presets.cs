static class Presets {
    internal static readonly string[] Names = [
        "bugfixes", "bugfixes-qol", "bugfixes-performance", "recommended", "full", "developer", "empty"
    ];

    internal static Profile Create(Catalog catalog, string name) {
        Files.Need(Names.Contains(name, StringComparer.Ordinal), "Unknown preset: " + name);

        bool Category(string category) => name switch {
            "empty" => false,
            "bugfixes" => category == "bugfix",
            "bugfixes-qol" => category is "bugfix" or "qol",
            "bugfixes-performance" => category is "bugfix" or "performance",
            "recommended" => category is "bugfix" or "qol" or "performance",
            "full" => category != "developer",
            "developer" => true,
            _ => false
        };

        return new Profile {
            Name = name,
            Features = catalog.Features
                .Where(feature => !feature.Hidden && !feature.Experimental && Category(feature.Category))
                .Select(feature => feature.Id)
                .Order(StringComparer.Ordinal)
                .ToArray()
        };
    }
}
