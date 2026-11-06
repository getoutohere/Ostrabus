using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Condowner.Utils;

namespace Ostrabus.Inspection;

// On-demand only, called by the API pump on Unity's main thread. No scheduler calls or mutations.
internal static class WorkSchedulerInspection {
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static object Field(object value, string name) => value?.GetType().GetField(name, Fields)?.GetValue(value);
    static object SchedulerField(string name) => typeof(Scheduler).GetField(name, Fields)?.GetValue(null);

    internal static object Snapshot() {
        var dispatcher = (EndTurnDispatcher)SchedulerField("_endTurnDispatcher");
        var active = Field(dispatcher, "_active") as Request;
        var pending = new List<object>();
        if (Field(dispatcher, "_pending") is IEnumerable requests) foreach (Request request in requests) {
            if (pending.Count == 64) break;
            pending.Add(RequestState(request));
        }
        var debts = new List<object>();
        if (SchedulerField("_priorityClockDebt") is Dictionary<CondOwner, double> credit) foreach (var entry in credit) {
            if (debts.Count == 64) break;
            debts.Add(SnapshotJson.Map("seconds", entry.Value, "owner", Actor(entry.Key)));
        }
        var crew = new List<object>();
        if (CrewSim.coPlayer?.Company?.mapRoster != null) foreach (string id in CrewSim.coPlayer.Company.mapRoster.Keys) {
            if (crew.Count == 32) break;
            if (DataHandler.mapCOs.TryGetValue(id, out CondOwner co) && co != null) crew.Add(Actor(co));
        }
        return SnapshotJson.Map("worldEpoch", StarSystem.fEpoch, "processedTime", Scheduler.ProcessedTime,
            "active", RequestState(active), "pending", pending, "priorityDebt", debts, "crew", crew,
            "note", "Bounded read-only snapshot. Iterator state numbers identify compiler-generated suspension points; no enumerators are advanced.");
    }

    static object RequestState(Request request) => request == null ? null : SnapshotJson.Map(
        "owner", Actor(request.CO), "cancelRequested", request.CancelRequested,
        "completed", request.Handle.IsCompleted, "cancelled", request.Handle.IsCancelled,
        "requiresContinuation", typeof(EndTurnHandle).GetProperty("RequiresContinuation", Fields)?.GetValue(request.Handle),
        "operation", Operation(request.Steps, 0, new HashSet<object>()));

    static object Actor(CondOwner co) {
        if (co == null) return null;
        Interaction ia = co.aQueue != null && co.aQueue.Count > 0 ? co.aQueue[0] : null;
        var tickers = new List<object>();
        if (Field(co, "aTickers") is List<JsonTicker> scheduled) for (int i = 0; i < Math.Min(scheduled.Count, 16); i++) tickers.Add(Ticker(scheduled[i]));
        return SnapshotJson.Map("id", co.strID, "ship", co.ship?.strRegID,
            "epoch", co.Clock.Epoch, "rate", co.Clock.Rate, "nextTickerLocalSecs", co.NextTickerLocalSecs,
            "priority", co.TickerPriority, "hasPriorityTickers", co.HasTickersWithPriority,
            "continuation", co.HasEndTurnContinuation, "continuationReady", co.EndTurnContinuationReady,
            "endTurnTicker", Ticker(Field(co, "_endTurnTicker") as JsonTicker), "tickers", tickers,
            "interaction", ia == null ? null : SnapshotJson.Map("name", ia.strName, "id", ia.id.ToString(),
                "epochAdded", ia.fEpochAdded, "duration", ia.fDuration, "cancel", ia.bCancel,
                "manual", ia.bManual, "urgent", ia.bUrgentScheduling, "raisedUI", ia.bRaisedUI));
    }

    static object Ticker(JsonTicker ticker) => ticker == null ? null : SnapshotJson.Map(
        "name", ticker.strName, "start", ticker.fEpochStart, "period", ticker.fPeriod,
        "queue", ticker.bQueue, "repeat", ticker.bRepeat, "priority", ticker.nPriority);

    static object Operation(object value, int depth, HashSet<object> seen) {
        if (value == null || depth > 10 || !seen.Add(value)) return null;
        Type type = value.GetType();
        var fields = new Dictionary<string, object>();
        foreach (FieldInfo field in type.GetFields(Fields)) {
            if (field.IsStatic) continue;
            object child = field.GetValue(value);
            if (field.Name.Contains("__state") || field.Name == "done" || field.Name == "started") fields[field.Name] = child;
            else if (child is IEnumerator) fields[field.Name] = Operation(child, depth + 1, seen);
            else if (child != null && child.GetType().Name.StartsWith("<>c__DisplayClass")) fields[field.Name] = Operation(child, depth + 1, seen);
            else if (child is PathResult path) fields[field.Name] = SnapshotJson.Map("hasPath", path.HasPath);
            else if (field.FieldType == typeof(PathResult)) fields[field.Name] = null;
            else if (child is Interaction ia) fields[field.Name] = ia.strName;
            else if (child is CondOwner co) fields[field.Name] = co.strID;
        }
        return SnapshotJson.Map("type", type.FullName, "fields", fields);
    }
}
