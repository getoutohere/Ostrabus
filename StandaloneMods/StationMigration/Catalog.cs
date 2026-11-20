using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Ostrabus.StationMigration;

// Asset definitions are supplied by the game's loaded dictionaries in game,
// and by the same vanilla JSON in the offline runner. No alternate AI policy.
internal sealed class Catalog
{
    readonly Func<string, JObject> co, overlay, item, loot;
    readonly JObject[] installables;
    readonly Func<JObject, bool> destroyed;
    readonly Dictionary<string, HashSet<string>> socketCache = new Dictionary<string, HashSet<string>>();
    readonly Dictionary<string, JObject> definitionCache = new Dictionary<string, JObject>();
    readonly Dictionary<string, JObject> itemCache = new Dictionary<string, JObject>();
    readonly Dictionary<(string, float), Footprint> shapeCache = new Dictionary<(string, float), Footprint>();

    internal Catalog(Func<string, JObject> co, Func<string, JObject> overlay,
        Func<string, JObject> item, Func<string, JObject> loot, IEnumerable<JObject> installables,
        Func<JObject, bool> destroyed = null)
    {
        this.co = co;
        this.overlay = overlay;
        this.item = item;
        this.loot = loot;
        this.installables = installables.ToArray();
        this.destroyed = destroyed;
    }

    internal static string[] Strings(JToken token) => token is JArray a ? a.Values<string>().ToArray() : Array.Empty<string>();
    internal JObject Definition(string name)
    {
        if (!definitionCache.TryGetValue(name, out var value))
        {
            value = co(name) ?? co((string)(overlay(name) ?? throw new InvalidOperationException("Unknown item: " + name))["strCOBase"])
                ?? throw new InvalidOperationException("Unknown base definition: " + name);
            definitionCache[name] = value;
        }
        return value;
    }
    internal JObject Item(string name)
    {
        if (!itemCache.TryGetValue(name, out var value))
        {
            value = item((string)Definition(name)["strItemDef"]) ?? throw new InvalidOperationException("Unknown item geometry: " + name);
            itemCache[name] = value;
        }
        return value;
    }
    internal Footprint Shape(string name, float angle)
    {
        if (!shapeCache.TryGetValue((name, angle), out var value)) shapeCache[(name, angle)] = value = new Footprint(Item(name), angle);
        return value;
    }
    internal bool Destroyed(JObject part) => destroyed?.Invoke(part) ?? false;

    internal string[] Conditions(string name, JObject state)
    {
        var values = Strings(state?["aConds"] ?? Definition(name)["aStartingConds"]).ToList();
        if (values.Remove("DEFAULT"))
            values.AddRange(Strings(Definition(name)["aStartingConds"]));
        return values.ToArray();
    }

    internal bool Installed(string name, JObject state) => Conditions(name, state)
        .Where(value => value.Split('=')[0] == "IsInstalled")
        .Select(value => value.Contains("=") ? Amount(value) : 1d).LastOrDefault() > 0;

    internal static double Amount(string equation)
    {
        string value = equation.Substring(equation.IndexOf('=') + 1);
        var pieces = value.Split('x');
        if (pieces.Length != 2 || !double.TryParse(pieces[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double chance)
            || chance != 1 || !double.TryParse(pieces[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double amount))
            throw new InvalidOperationException("Migration requires a deterministic condition: " + equation);
        return amount;
    }

    internal HashSet<string> Socket(string name)
    {
        if (socketCache.TryGetValue(name, out var cached)) return cached;
        var result = new HashSet<string>(StringComparer.Ordinal);
        Resolve(name, result, new HashSet<string>());
        socketCache[name] = result;
        return result;
    }

    void Resolve(string name, HashSet<string> result, HashSet<string> path)
    {
        if (!path.Add(name)) throw new InvalidOperationException("Cyclic socket table: " + name);
        var table = loot(name) ?? throw new InvalidOperationException("Missing socket table: " + name);
        foreach (string value in Strings(table["aCOs"]))
        {
            if (value.Contains("=") && Amount(value) != 1) throw new InvalidOperationException("Random socket: " + value);
            result.Add(value.Split('=')[0]);
        }
        foreach (string value in Strings(table["aLoots"]))
        {
            if (value.Contains("=") && Amount(value) != 1) throw new InvalidOperationException("Random nested socket: " + value);
            Resolve(value.Split('=')[0], result, path);
        }
        path.Remove(name);
    }

    internal string LooseDefinition(string name)
    {
        string baseName = (string)Definition(name)["strName"];
        var outputs = installables.Where(row => (string)row["strActionCO"] == baseName &&
                (string)row["strJobType"] == "uninstall")
            .SelectMany(RecipeOutputs).Distinct().ToArray();
        // Some floors supply only the inverse install recipe. It still defines
        // the native loose/installed pair; do not invent a name suffix.
        if (outputs.Length == 0)
            outputs = installables.Where(row => (string)row["strJobType"] == "install" &&
                RecipeOutputs(row).SequenceEqual(new[] { baseName }))
                .Select(row => (string)row["strActionCO"]).Distinct().ToArray();
        if (outputs.Length != 1)
            throw new InvalidOperationException("No unambiguous native loose form for blocking construction: " + name);
        string target = outputs[0];
        var swaps = Strings(overlay(name)?["mapModeSwitches"]);
        for (int i = 0; i + 1 < swaps.Length; i += 2)
            if (swaps[i] == target) return swaps[i + 1];
        return target;
    }

    // Native Installables.Create prefers a named output table when present.
    // 1.1.0.4 weapon recipes use this form to suppress fresh starting ammo.
    // Accept only a deterministic item mapping; never roll loot to guess a pair.
    internal string[] RecipeOutputs(JObject recipe)
    {
        string named = (string)recipe["strLootOut"];
        if (named == null) return Strings(recipe["aLootCOs"]);
        var table = loot(named) ?? throw new InvalidOperationException("Missing conversion output: " + named);
        if ((string)table["strType"] != "item" || Strings(table["aLoots"]).Length != 0)
            return Array.Empty<string>();
        var result = new List<string>();
        foreach (string value in Strings(table["aCOs"]))
        {
            try
            {
                if (value.Contains("=") && Amount(value) != 1) return Array.Empty<string>();
            }
            catch (InvalidOperationException) { return Array.Empty<string>(); }
            result.Add(value.Split('=')[0]);
        }
        return result.ToArray();
    }

    // Change structural defaults while retaining damage, charge, inventory,
    // ownership and other explicit saved state. This is an administrative
    // conversion, so it deliberately does not apply ModeSwitch's extra wear.
    internal void Convert(JObject state, string oldName, string newName)
    {
        var oldDefaults = Strings(Definition(oldName)["aStartingConds"])
            .GroupBy(value => value.Split('=')[0]).ToDictionary(group => group.Key, group => group.Last());
        var newDefaults = Strings(Definition(newName)["aStartingConds"])
            .GroupBy(value => value.Split('=')[0]).ToDictionary(group => group.Key, group => group.Last());
        var values = Conditions(oldName, state).ToList();
        values.RemoveAll(value => oldDefaults.TryGetValue(value.Split('=')[0], out string original) && SameDefault(value, original));
        var stateNames = values.Select(value => value.Split('=')[0]).ToHashSet();
        // State wins over shared defaults; classification comes from the new
        // asset (IsInstalled/IsWall/etc.). Clear only defaults of the old type.
        values.AddRange(newDefaults.Where(pair => !stateNames.Contains(pair.Key)).Select(pair => pair.Value));
        state["aConds"] = new JArray(values);
        state["strCODef"] = newName;
        state["aCondReveals"] = null;
    }

    static bool SameDefault(string value, string original)
    {
        if (value == original) return true;
        try { return (value.Contains("=") ? Amount(value) : 1) == (original.Contains("=") ? Amount(original) : 1); }
        // A random authored default is not proof that an explicit saved value
        // is unchanged. Preserve the saved state instead of rolling it again.
        catch (InvalidOperationException) { return false; }
    }
}
