using System;
using System.Collections.Generic;
using System.Diagnostics;
using HarmonyLib;

namespace Ostrabus.Inspection;

internal static class NpcInspection {
    static readonly AccessTools.FieldRef<Interaction, string> InteractionUsId = AccessTools.FieldRefAccess<Interaction, string>("strUsID");
    static readonly AccessTools.FieldRef<Interaction, string> InteractionThemId = AccessTools.FieldRefAccess<Interaction, string>("strThemID");
    static readonly AccessTools.FieldRef<Interaction, string> InteractionThirdId = AccessTools.FieldRefAccess<Interaction, string>("str3rdID");
    static readonly AccessTools.FieldRef<Pledge2, JsonPledge> PledgeJson = AccessTools.FieldRefAccess<Pledge2, JsonPledge>("jp");
    static readonly AccessTools.FieldRef<Pledge2, string> PledgeUsId = AccessTools.FieldRefAccess<Pledge2, string>("strUsID");
    static readonly AccessTools.FieldRef<Pledge2, string> PledgeThemId = AccessTools.FieldRefAccess<Pledge2, string>("strThemID");
    static Dictionary<string, object> Map(params object[] pairs) { return SnapshotJson.Map(pairs); }
    static bool OverBudget(long start) { return Stopwatch.GetTimestamp() - start >= Stopwatch.Frequency / 1000; }

    internal static object Conditions(CondOwner co, int offset, int limit, long start) {
        var items = new List<object>();
        int index = 0;
        bool more = false;
        if (co.mapConds != null) {
            foreach (var pair in co.mapConds) {
                if (index < offset) { index++; continue; }
                if (items.Count >= limit) { more = true; break; }
                if (items.Count > 0 && OverBudget(start)) { more = true; break; }
                var condition = pair.Value;
                items.Add(Map("id", pair.Key, "name", condition?.strNameFriendly, "amount", condition?.fCount,
                    "alert", condition?.bAlert, "negative", condition?.bNegative, "fatal", condition?.bFatal, "knockout", condition?.bKO));
                index++;
            }
        }
        return Map("coId", co.strID, "conditionCount", co.mapConds?.Count, "items", items,
            "nextOffset", more ? (object)index : null, "offset", offset,
            "note", "Raw active conditions and counts, paged in live dictionary order. No condition triggers or derived health calculations are run.");
    }

    internal static object Activity(CondOwner co, int offset, int limit, long start) {
        var queue = new List<object>();
        bool queueMore = false;
        int queueCount = co.aQueue?.Count ?? 0;
        if (co.aQueue != null) {
            for (int i = offset; i < co.aQueue.Count; i++) {
                if (queue.Count >= limit) { queueMore = true; break; }
                queue.Add(InteractionState(co.aQueue[i]));
            }
        }
        var pledges = new List<object>();
        int pledgeCount = 0, pledgeScanned = 0;
        bool pledgeMore = false;
        if (co.dictPledges != null) {
            foreach (var priority in co.dictPledges) { pledgeCount += priority.Value?.Count ?? 0; }
            foreach (var priority in co.dictPledges) {
                if (priority.Value == null) { continue; }
                foreach (var pledge in priority.Value) {
                    if (pledgeScanned++ < offset) { continue; }
                    if (pledges.Count >= limit) { pledgeMore = true; break; }
                    var definition = pledge == null ? null : PledgeJson(pledge);
                    pledges.Add(Map("name", definition?.strName, "friendlyName", definition?.strNameFriendly,
                        "type", definition?.strType, "priority", definition?.nPriority,
                        "usId", pledge == null ? null : PledgeUsId(pledge), "themId", pledge == null ? null : PledgeThemId(pledge)));
                }
                if (pledgeMore) { break; }
            }
        }
        return Map("coId", co.strID, "currentInteraction", co.aQueue != null && co.aQueue.Count > 0 ? InteractionState(co.aQueue[0]) : null,
            "interactionQueue", Map("count", queueCount, "items", queue, "nextOffset", queueMore ? (object)(offset + queue.Count) : null),
            "pledges", Map("count", pledgeCount, "items", pledges, "nextOffset", pledgeMore ? (object)(offset + pledges.Count) : null),
            "offset", offset, "note", "Interaction and pledge references are read from stored queues. No pledge completion checks, trigger evaluation, or lazy CO resolution is performed.");
    }

    static object InteractionState(Interaction interaction) {
        if (interaction == null) { return null; }
        return Map("id", interaction.id.ToString(), "name", interaction.strName, "title", interaction.strTitle,
            "description", interaction.strDesc, "actionGroup", interaction.strActionGroup,
            "usId", InteractionUsId(interaction), "themId", InteractionThemId(interaction), "thirdId", InteractionThirdId(interaction),
            "epochAdded", interaction.fEpochAdded, "duration", interaction.fDuration,
            "social", interaction.bSocial, "paused", interaction.bPause);
    }
}
