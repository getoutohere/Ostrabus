using System;
using System.Collections.Generic;

namespace Ostrabus.MiscBugFixes;

internal sealed class MassRepairPolicy {
    internal enum Kind { None, Container, Anatomy, Person }
    static readonly HashSet<string> Limbs = new HashSet<string>(StringComparer.Ordinal) {
        "BodyarmUpperLA", "BodyarmUpperRA", "BodyarmLowerLA", "BodyarmLowerRA", "BodyhandLA", "BodyhandRA",
        "BodylegUpperLA", "BodylegUpperRA", "BodylegLowerLA", "BodylegLowerRA", "BodyfootLA", "BodyfootRA"
    };
    readonly Dictionary<string, bool> safeLoot = new Dictionary<string, bool>(StringComparer.Ordinal);
    readonly HashSet<string> inspecting = new HashSet<string>(StringComparer.Ordinal);
    internal static Kind Classify(CondOwner co) {
        if (co == null) return Kind.None;
        if (co.strCODef == "Crew01" || co.HasCond("IsHuman")) return Kind.Person;
        if (co.strCODef != null && co.strCODef.StartsWith("Body", StringComparison.Ordinal)) return Kind.Anatomy;
        return co.objContainer != null && co.HasCond("IsContainer") ? Kind.Container : Kind.None;
    }
    bool SafeLoot(string name, int depth = 0) {
        if (safeLoot.TryGetValue(name, out bool safe)) return safe;
        if (depth > 32 || !inspecting.Add(name)) return false;
        try {
            if (DataHandler.dictLoot == null || !DataHandler.dictLoot.TryGetValue(name, out Loot loot)) return false;
            safe = true;
            if (loot.strType == "condition") {
                if (loot.aCOs != null) foreach (string equation in loot.aCOs) {
                    if (equation == null) { safe = false; continue; }
                    foreach (string option in equation.Split('|')) {
                        string text = option.TrimStart('-');
                        if (text == "StatMass" || text.StartsWith("StatMass=", StringComparison.Ordinal)) safe = false;
                    }
                }
                if (loot.aLoots != null) foreach (string equation in loot.aLoots) foreach (string option in equation.Split('|')) {
                    int end = option.IndexOf('=');
                    if (end < 1 || !SafeLoot(option.Substring(0, end).TrimStart('-'), depth + 1)) safe = false;
                }
            }
            safeLoot[name] = safe; return safe;
        } finally { inspecting.Remove(name); }
    }
    bool Effects(CondOwner co, Kind kind, out string error) {
        error = null;
        if (co.mapConds == null) { error = "missing condition state"; return false; }
        foreach (Condition condition in co.mapConds.Values) {
            if (condition == null || condition.fCount <= 0 || condition.aPer == null) continue;
            foreach (string name in condition.aPer) {
                if (kind == Kind.Person && ((condition.strName == "IsStrong" && name == "CONDStrongPer") || (condition.strName == "IsFeeble" && name == "CONDFeeblePer"))) continue;
                if (string.IsNullOrEmpty(name) || !SafeLoot(name)) { error = "unreviewed mass-affecting condition: " + condition.strName; return false; }
            }
        }
        return true;
    }
    static bool Modifier(string name, out double amount) {
        amount = 0;
        if (DataHandler.dictLoot == null || !DataHandler.dictLoot.TryGetValue(name, out Loot loot) || loot.strType != "condition" || loot.aLoots?.Length > 0) return false;
        string equation = null;
        if (loot.aCOs != null) foreach (string entry in loot.aCOs) {
            if (entry == null || entry.IndexOf('|') >= 0) return false;
            string text = entry.TrimStart('-');
            if (!text.StartsWith("StatMass", StringComparison.Ordinal)) continue;
            if (equation != null) return false;
            equation = entry;
        }
        if (equation == null || !MassRepairRules.TryFixedMass(new[] { equation.TrimStart('-') }, out amount, out _)) return false;
        if (equation[0] == '-') amount = -amount;
        return true;
    }
    internal bool Tare(CondOwner co, Kind kind, out double mass, out string error) {
        mass = 0; error = null;
        var definition = DataHandler.GetCondOwnerDef(co.strCODef);
        if (definition == null) { error = "missing definition"; return false; }
        if (kind == Kind.Container) {
            if (definition.strName != co.strCODef) { error = "unaudited container overlay"; return false; }
            if (!MassRepairRules.TryTare(definition.strType, definition.strLoot, definition.aStartingConds, co.mapConds?.Keys, out mass, out error)) return false;
            return Effects(co, kind, out error);
        }
        if (kind == Kind.Anatomy) {
            if (!Limbs.Contains(definition.strName)) { error = "unreviewed anatomical definition"; return false; }
            if (co.strCODef != definition.strName && (DataHandler.dictCOOverlays == null ||
                !DataHandler.dictCOOverlays.TryGetValue(co.strCODef, out var overlay) || overlay.strCOBase != definition.strName ||
                (!string.IsNullOrEmpty(overlay.strCondLoot) && overlay.strCondLoot != "Blank"))) { error = "unaudited anatomical overlay"; return false; }
            string region = definition.strName.Substring(4, definition.strName.Length - 5);
            string wound = "WOUND" + char.ToUpperInvariant(region[0]) + region.Substring(1);
            if (!string.IsNullOrEmpty(definition.strLoot) && definition.strLoot != wound) { error = "unreviewed anatomical starting loot"; return false; }
            if (!string.IsNullOrEmpty(definition.strLoot) && DataHandler.dictLoot != null && DataHandler.dictLoot.ContainsKey(definition.strLoot) && !SafeLoot(definition.strLoot)) {
                error = "mass-affecting anatomical starting loot"; return false;
            }
            if (!MassRepairRules.TryTare(definition.strType, null, definition.aStartingConds, co.mapConds?.Keys, out mass, out error)) return false;
        } else if (kind == Kind.Person) {
            if (co.strCODef != "Crew01" || definition.strName != "Crew01" || definition.strType != "Crew" || !string.IsNullOrEmpty(definition.strLoot) ||
                co.GetCondAmount("IsHuman") <= 0 || co.GetCondAmount("IsRobot") > 0) { error = "unreviewed human definition"; return false; }
            if (!MassRepairRules.TryFixedMass(definition.aStartingConds, out mass, out error)) return false;
            double strong = co.GetCondAmount("IsStrong"), feeble = co.GetCondAmount("IsFeeble");
            if ((strong != 0 && strong != 1) || (feeble != 0 && feeble != 1) || strong + feeble > 1) { error = "noncanonical Strong/Feeble state"; return false; }
            if (strong > 0) { if (!Modifier("CONDStrongPer", out double delta)) { error = "unreviewed Strong mass effect"; return false; } mass += delta; }
            if (feeble > 0) { if (!Modifier("CONDFeeblePer", out double delta)) { error = "unreviewed Feeble mass effect"; return false; } mass += delta; }
            if (!MassRepairRules.Finite(mass) || mass < 0) { error = "invalid character core mass"; return false; }
        } else { error = "not a supported mass repair target"; return false; }
        return Effects(co, kind, out error);
    }
}
