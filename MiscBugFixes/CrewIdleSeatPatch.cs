using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace Ostrabus.MiscBugFixes;

// Reconsider restorative work only when an autonomous, unsecured chair loop is
// about to renew. Never interrupt manual sitting, secured seats or nav-console use.
[HarmonyPatch(typeof(Interaction), nameof(Interaction.GetReply))]
internal static class CrewIdleSeatPatch {
    internal delegate void Rank(CondOwner crew, CondOwner target, string interaction, bool repair, double duty, List<AutoTask> tasks);
    static readonly Rank Insert = AccessTools.MethodDelegate<Rank>(AccessTools.Method(typeof(CondOwner), "InsertUndamage"));
    static readonly AccessTools.FieldRef<CondOwner, bool> CanUndamage = AccessTools.FieldRefAccess<CondOwner, bool>("bCanUndamage");
    static readonly AccessTools.FieldRef<CondOwner, bool> CanPatch = AccessTools.FieldRefAccess<CondOwner, bool>("bCanPatch");
    static readonly AccessTools.FieldRef<CondOwner, bool> CanRepair = AccessTools.FieldRefAccess<CondOwner, bool>("bCanRepair");
    static readonly AccessTools.FieldRef<Interaction, CondOwner> StoredUs = AccessTools.FieldRefAccess<Interaction, CondOwner>("objUsTemp");
    static readonly AccessTools.FieldRef<Interaction, CondOwner> StoredThem = AccessTools.FieldRefAccess<Interaction, CondOwner>("objThemTemp");
    sealed class Probe {
        internal double Next;
        internal readonly List<AutoTask> Tasks = new List<AutoTask>();
        internal readonly List<AutoTask> Patch = new List<AutoTask>();
        internal readonly List<AutoTask> Repair = new List<AutoTask>();
        internal readonly List<AutoTask> Restore = new List<AutoTask>();
        internal void Clear() { Tasks.Clear(); Patch.Clear(); Repair.Clear(); Restore.Clear(); }
    }
    static readonly ConditionalWeakTable<CondOwner, Probe> Probes = new ConditionalWeakTable<CondOwner, Probe>();
    static readonly string[] EndRoles = { "ACTChairSitEnd", "[us]", "[them]" };
    static bool Seating(string name) => name != null && (name.StartsWith("ACTChairSit", StringComparison.Ordinal) ||
        name == "GUINavStationSit" || name.StartsWith("GUINAVSit", StringComparison.Ordinal));

    [HarmonyPrefix]
    static bool Prefix(Interaction __instance, ref Interaction __result) {
        if (__instance.strName != "ACTChairSitAllow" || __instance.bManual) return true;
        CondOwner crew = StoredUs(__instance);
        if (!CrewRestPolicy.Autonomous(crew) || crew.ship == null || crew.jsShiftLast?.nID != 2 || crew.Company == null ||
            !crew.Company.mapRoster.TryGetValue(crew.strID, out var rules) || !rules.bRestorePermission ||
            crew.HasCond("IsInCombat") || crew.HasCond("InSocialCombat") || crew.HasCond("IsEmergencyOverride") ||
            !crew.OwnsShip(crew.ship.strRegID)) return true;
        Probe probe = Probes.GetValue(crew, _ => new Probe());
        double now = StarSystem.fEpoch;
        if (now < probe.Next && probe.Next - now <= 30) return true;
        probe.Next = now + 30;
        bool available;
        try { available = HasAutomaticWork(crew, probe); }
        catch (Exception error) {
            UnityEngine.Debug.LogWarning("[CrewRest] Automatic work probe failed; retaining native sitting behavior: " + error.Message);
            return true;
        }
        finally { probe.Clear(); }
        if (!available) return true;
        var end = DataHandler.GetInteraction("ACTChairSitEnd");
        if (end == null) return true;
        __instance.AssignReplyRoles(end, EndRoles, false);
        if (!end.Triggered(crew, StoredThem(__instance), bStats: false, bFetchItems: false)) return true;
        end.strChainOwner = __instance.strChainOwner;
        end.strChainStart = __instance.strChainStart;
        end.strPlot = __instance.strPlot;
        __result = end;
        return false;
    }
    static void RankCategory(CondOwner crew, CondOwner target, string category, bool repair, double weight, List<AutoTask> tasks) {
        foreach (string name in target.aInteractions) if (name != null && name.Contains(category) && (category != "Patch" || !name.Contains("Scrap"))) {
            Insert(crew, target, name, repair, weight, tasks);
            break;
        }
    }
    static void Merge(Probe probe, List<AutoTask> first, List<AutoTask> second, List<AutoTask> third) {
        probe.Tasks.AddRange(first); probe.Tasks.AddRange(second); probe.Tasks.AddRange(third);
    }
    static bool HasAutomaticWork(CondOwner crew, Probe probe) {
        probe.Clear();
        double patch = Math.Pow(10, crew.Company.GetDutyLevel(crew, "Patch"));
        double repair = Math.Pow(10, crew.Company.GetDutyLevel(crew, "Repair")) + 1;
        double restore = Math.Pow(10, crew.Company.GetDutyLevel(crew, "Restore")) + 2;
        foreach (CondOwner target in crew.ship.GetCOs(DataHandler.GetCondTrigger("TIsAIRestoreItem"), bSubObjects: true, bAllowDocked: false, bAllowLocked: true)) {
            if (target?.aInteractions == null) continue;
            if (CanPatch(target)) RankCategory(crew, target, "Patch", true, patch, probe.Patch);
            if (CanRepair(target)) RankCategory(crew, target, "Repair", true, repair, probe.Repair);
            if (CanUndamage(target)) RankCategory(crew, target, "Undamage", false, restore, probe.Restore);
        }
        // Preserve ProcessAutoTasks' category order, including its tie behavior.
        // InsertUndamage already applies native distance/damage ranking within each.
        if (patch > repair) {
            if (repair > restore) Merge(probe, probe.Restore, probe.Repair, probe.Patch);
            else if (restore < patch) Merge(probe, probe.Repair, probe.Restore, probe.Patch);
            else Merge(probe, probe.Repair, probe.Patch, probe.Restore);
        } else if (repair < restore) Merge(probe, probe.Patch, probe.Repair, probe.Restore);
        else if (restore < patch) Merge(probe, probe.Restore, probe.Patch, probe.Repair);
        else Merge(probe, probe.Patch, probe.Restore, probe.Repair);
        List<AutoTask> ranked = probe.Tasks;
        Pathfinder path = crew.Pathfinder;
        Tile destination = path?.tilDest;
        CondOwner targetBefore = path?.coDest;
        float range = path?.fRangeGoal ?? 0;
        try {
            // Match the native ProcessAutoTasks probe limit and its cooldown/ranking.
            for (int i = 0; i < Math.Min(2, ranked.Count); i++) {
                var interaction = DataHandler.GetInteraction(ranked[i].strIA);
                if (interaction == null) continue;
                interaction.bHumanOnly = false;
                if (interaction.Triggered(crew, ranked[i].co, bStats: false, bIgnoreItems: false, bCheckPath: true, bFetchItems: false)) return true;
            }
            return false;
        } finally {
            // Native CheckGoal can leave these changed on a cache hit. A probe
            // must not replace the active sitting destination or target.
            if (path != null) { path.tilDest = destination; path.coDest = targetBefore; path.fRangeGoal = range; }
        }
    }
    [HarmonyPostfix]
    static void Postfix(Interaction __instance, Interaction __result) {
        // Vanilla reply-role assignment does not copy bManual. Keep a manual
        // seating chain marked through renewals and normal save serialization.
        if (__result != null && __instance.bManual && Seating(__instance.strName) && Seating(__result.strName) &&
            ReferenceEquals(StoredUs(__instance), StoredUs(__result))) __result.bManual = true;
    }
}
