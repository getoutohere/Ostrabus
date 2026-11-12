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
            "recommended" => category is "bugfix" or "qol" or "performance" or "simulation",
            "full" => category != "developer",
            "developer" => true,
            _ => false
        };

        bool Include(Feature feature) {
            if (name == "recommended" && feature.Id == "ceres-navigation") return false;
            if (feature.Id == "legacy-commands" && name is "recommended" or "full") return true;
            return Category(feature.Category);
        }

        return new Profile {
            Name = name,
            Features = catalog.Features
                .Where(feature => !feature.Hidden && !feature.Experimental && Include(feature))
                .Select(feature => feature.Id)
                .Order(StringComparer.Ordinal)
                .ToArray()
        };
    }
}
