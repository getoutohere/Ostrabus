using System;
using System.Collections.Generic;
using System.Globalization;

namespace Ostrabus.SaveCompatibility;
// A load-time projection of the native installed-thruster data, not an AI policy.
// Do not roll loot, instantiate parts, or assume weighted thrust is a raw count.
internal static class LegacyRcsCaches
{
    internal struct Result
    {
        internal int Count;
        internal float Exhaust, Strength;
        internal bool PreservedShallowDamage;
        internal bool NativeRebuild;
    }

    static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    static bool Empty(string[] values) => values == null || values.Length == 0;
    internal static bool Missing(JsonShip ship) => ship != null && ship.nRCSCountRaw == 0 && ship.nRCSExhaustV == 0 && Finite(ship.nRCSCount) && ship.nRCSCount > 0;
    // CondTrigger.Triggered requires a scene CondOwner. Accept only its current
    // deterministic, positive-presence AND subset here, using the loaded asset's
    // actual requirements/forbids. Reject extensions rather than invent an answer.
    internal static bool Supported(CondTrigger gate) => gate != null && gate.bAND && !gate.bNOT && gate.fChance == 1 && string.IsNullOrEmpty(gate.strCondName) && string.IsNullOrEmpty(gate.strHigherCond) && Empty(gate.aLowerConds) && Empty(gate.aTriggers) && Empty(gate.aTriggersForbid) && gate.aReqs != null && gate.aReqs.Length > 0 && gate.aForbids != null;
    internal static bool Eligible(CondTrigger gate, Dictionary<string, double> values)
    {
        if (!Supported(gate) || !values.ContainsKey("IsShipSpecialItem"))
            return false;
        foreach (string name in gate.aReqs)
            if (!Positive(values, name))
                return false;
        foreach (string name in gate.aForbids)
            if (Positive(values, name))
                return false;
        return true;
    }

    static bool Positive(Dictionary<string, double> values, string name) => values.TryGetValue(name, out double amount) && amount > 0;
    internal static bool TryDerive(JsonShip ship, CondTrigger gate, Func<string, JsonCondOwnerSave> savedById, Func<string, JsonCondOwner> definitionByName, Func<string, Condition> conditionByName, out Result result, out string reason, bool allowDefinitionFallback = true, Func<JsonItem, bool> isItemDestroyed = null)
    {
        result = default;
        reason = null;
        if (ship == null || !Finite(ship.nRCSCount) || ship.nRCSCount <= 0)
        {
            reason = "invalid saved thrust counter";
            return false;
        }

        if (!Supported(gate))
        {
            reason = "native RCS eligibility asset is outside the supported data-only subset";
            return false;
        }

        if (ship?.aItems == null)
        {
            reason = "installed item layout is missing";
            return false;
        }

        var relevant = new HashSet<string>(gate.aReqs, StringComparer.Ordinal);
        relevant.UnionWith(gate.aForbids);
        relevant.UnionWith(new[] { "IsShipSpecialItem", "StatThrustStrength", "StatThrustExhaustV" });
        // Native condition application can cancel an opposite condition.
        foreach (string name in new List<string>(relevant))
        {
            var condition = conditionByName(name);
            if (!string.IsNullOrEmpty(condition?.strAnti))
                relevant.Add(condition.strAnti);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var values = new Dictionary<string, double>(StringComparer.Ordinal);
        bool uniform = true, damaged = false;
        float unitStrength = 0, unitExhaust = 0;
        foreach (var item in ship.aItems)
        {
            if (item == null)
                continue;
            if (string.IsNullOrEmpty(item.strID))
            {
                reason = "item has no stable identity";
                return false;
            }

            if (!seen.Add(item.strID))
                continue;
            var saved = savedById(item.strID);
            // Unvisited cached layouts can retain blueprint parts without saved
            // COs. Previously materialized hulls require actual saved records
            // except for the native MP-part fallback; never recreate lost parts.
            if (!allowDefinitionFallback && saved == null && item.strID.IndexOf("MP|", StringComparison.Ordinal) < 0)
                continue;
            var definition = definitionByName(saved?.strCODef ?? item.strName);
            if (definition == null)
            {
                reason = "missing part definition: " + item.strName;
                return false;
            }

            values.Clear();
            if (!Read(saved == null ? definition.aStartingConds : saved.aConds, definition, relevant, values, conditionByName, out reason))
                return false;
            if (item.aCondOverrides != null)
                foreach (var change in item.aCondOverrides)
                {
                    if (change == null || !relevant.Contains(change.CondName))
                        continue;
                    double amount = change.NegativeValue ? -change.Amount : change.Amount;
                    if (!Assign(values, change.CondName, amount, true, conditionByName, out reason))
                        return false;
                }

            if (!Eligible(gate, values))
                continue;
            damaged |= item.GetCondAmountOverride("StatDamage") > 0;
            if (isItemDestroyed != null && isItemDestroyed(item))
                continue;
            if (item.ForceLoad())
            {
                // InitShip subsequently calls AddICO for these parts. Seeding
                // cached totals here would be counted again by that native path.
                result.NativeRebuild = true;
                reason = "native force-loaded RCS parts own cache reconstruction";
                return false;
            }

            float strength = values.TryGetValue("StatThrustStrength", out double thrust) ? (float)thrust : 1;
            float exhaust = values.TryGetValue("StatThrustExhaustV", out double velocity) ? (float)velocity : 1;
            if (result.Count == 0)
            {
                unitStrength = strength;
                unitExhaust = exhaust;
            }
            else
                uniform &= strength == unitStrength && exhaust == unitExhaust;
            result.Count++;
            result.Strength += strength;
            result.Exhaust += exhaust;
        }

        // Native shallow DamageSystem can reduce the authoritative thrust
        // counter without switching every layout item to its damaged variant.
        // For a proven homogeneous package only, its missing aggregate count
        // and exhaust have a unique inverse. Preserve that combat loss instead
        // of healing the counter or pretending every layout part is active.
        if (damaged && uniform && result.Count > 0 && result.Strength != ship.nRCSCount && unitStrength > 0 && unitExhaust > 0)
        {
            float strength = 0, exhaust = 0;
            int match = 0, matches = 0;
            float matchedExhaust = 0;
            for (int count = 1; count <= result.Count; count++)
            {
                strength += unitStrength;
                exhaust += unitExhaust;
                if (strength != ship.nRCSCount)
                    continue;
                match = count;
                matchedExhaust = exhaust;
                matches++;
            }

            if (matches == 1)
            {
                result.Count = match;
                result.Exhaust = matchedExhaust;
                result.Strength = ship.nRCSCount;
                result.PreservedShallowDamage = true;
            }
        }

        if (result.Count == 0 || !Finite(result.Exhaust) || result.Exhaust <= 0 || !Finite(result.Strength) || Math.Abs(result.Strength - ship.nRCSCount) > Math.Max(1e-12, Math.Abs(ship.nRCSCount) * .000002))
        {
            reason = "derived active thrusters do not agree with the saved weighted thrust";
            return false;
        }

        return true;
    }

    static bool Read(string[] equations, JsonCondOwner definition, HashSet<string> relevant, Dictionary<string, double> values, Func<string, Condition> conditionByName, out string reason)
    {
        reason = null;
        if (equations == null)
            return true;
        bool defaults = false;
        foreach (string equation in equations)
        {
            if (equation == "DEFAULT")
            {
                defaults = true;
                continue;
            }

            if (!Equation(equation, relevant, values, conditionByName, out reason))
                return false;
        }

        // Native CondOwner.Init appends DEFAULT definitions after saved entries.
        if (defaults)
        {
            if (definition.aStartingConds == null)
            {
                reason = "missing DEFAULT conditions";
                return false;
            }

            foreach (string equation in definition.aStartingConds)
            {
                if (equation == "DEFAULT")
                {
                    reason = "recursive DEFAULT conditions";
                    return false;
                }

                if (!Equation(equation, relevant, values, conditionByName, out reason))
                    return false;
            }
        }

        return true;
    }

    static bool Equation(string equation, HashSet<string> relevant, Dictionary<string, double> values, Func<string, Condition> conditionByName, out string reason)
    {
        reason = null;
        if (string.IsNullOrEmpty(equation))
            return true;
        int equals = equation.IndexOf('=');
        string name = equals >= 0 ? equation.Substring(0, equals) : equation;
        if (name.StartsWith("-", StringComparison.Ordinal))
            name = name.Substring(1);
        if (name.IndexOf('|') >= 0)
        {
            foreach (string choice in name.Split('|'))
                if (relevant.Contains(choice))
                {
                    reason = "ambiguous relevant condition choice";
                    return false;
                }

            return true;
        }

        if (!relevant.Contains(name))
            return true;
        // Even native Loot.ParseCondEquation reads Unity RNG on its first parse.
        // Decode only the unambiguous saved form instead; never reroll an item.
        int separator = equation.IndexOf('x', equals + 1);
        if (equals < 0 || separator < 0 || !double.TryParse(equation.Substring(equals + 1, separator - equals - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out double chance) || chance != 1 || !double.TryParse(equation.Substring(separator + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out double amount))
        {
            reason = "nondeterministic relevant condition: " + name;
            return false;
        }

        if (equation.StartsWith("-", StringComparison.Ordinal))
            amount = -amount;
        return Assign(values, name, amount, equals >= 0, conditionByName, out reason);
    }

    static bool Assign(Dictionary<string, double> values, string name, double amount, bool absolute, Func<string, Condition> conditionByName, out string reason)
    {
        reason = null;
        var definition = conditionByName(name);
        if (definition == null || !Finite(amount))
        {
            reason = "missing/invalid relevant condition: " + name;
            return false;
        }

        // These migration inputs are condition counts, not effects or timers.
        // Refuse special cases that would require executing the simulation.
        if (definition.bRemoveOnLoad || definition.bRoom || definition.ctImmune != null || amount < 0 || amount > definition.fClampMax)
        {
            reason = "relevant condition requires nontrivial native application: " + name;
            return false;
        }

        if (absolute)
            values.Remove(name);
        if (amount > 0 && !string.IsNullOrEmpty(definition.strAnti) && values.TryGetValue(definition.strAnti, out double opposite))
        {
            if (opposite > amount)
            {
                values[definition.strAnti] = opposite - amount;
                return true;
            }

            values.Remove(definition.strAnti);
            amount -= opposite;
        }

        if (values.TryGetValue(name, out double before))
            amount += before;
        if (amount == 0)
            values.Remove(name);
        else
            values[name] = amount;
        return true;
    }
}
