#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Ostrabus.MiscBugFixes;

// Shared by the live command and read-only save audit. Never evaluates loot or consumes RNG.
internal static class MassRepairRules {
    internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    internal static bool TryTare(string type, string loot, string[] starting, IEnumerable<string> activeConditions, out double mass, out string error) {
        mass = 0; error = null;
        if (type != "Item") { error = "not an inventory item"; return false; }
        if (!string.IsNullOrEmpty(loot)) { error = "definition loot has not been audited for mass changes"; return false; }
        if (activeConditions != null) foreach (string condition in activeConditions) {
            if (condition == "IsHuman" || condition == "IsRobot" || condition == "IsGasContainer" ||
                condition.StartsWith("StatGas", StringComparison.Ordinal) || condition.StartsWith("StatFireChem", StringComparison.Ordinal) ||
                condition.StartsWith("StatFood", StringComparison.Ordinal) || condition.StartsWith("StatDrink", StringComparison.Ordinal)) {
                error = "potentially variable body/contents mass"; return false;
            }
        }
        bool found = false;
        if (starting != null) foreach (string equation in starting) {
            if (equation == null) continue;
            if (equation == "DEFAULT" || equation.StartsWith("-StatMass", StringComparison.Ordinal) || equation.StartsWith("StatMass|", StringComparison.Ordinal)) {
                error = "ambiguous base mass"; return false;
            }
            if (!equation.StartsWith("StatMass=", StringComparison.Ordinal)) continue;
            int separator = equation.IndexOf('x');
            if (found || separator < 10 ||
                !double.TryParse(equation.Substring(9, separator - 9), NumberStyles.Float, CultureInfo.InvariantCulture, out double chance) || chance != 1 ||
                !double.TryParse(equation.Substring(separator + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out mass) || mass < 0 || !Finite(mass)) {
                error = "ambiguous base mass"; return false;
            }
            found = true;
        }
        if (!found) { error = "missing base mass"; return false; }
        return true;
    }
}
