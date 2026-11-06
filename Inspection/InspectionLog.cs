using System;
using System.Collections.Generic;
using System.Threading;

namespace Ostrabus.Inspection;

// Producers never wait for the reader or invoke game code/ToString on arbitrary objects.
internal static class InspectionLog {
    const int Capacity = 512;
    static readonly object Gate = new object();
    sealed class Entry {
        internal long Sequence;
        internal DateTime Utc;
        internal string Source, Level, Message, Stack;
        internal bool MessageTruncated, StackTruncated;
    }
    static readonly Entry[] Ring = new Entry[Capacity];
    static long latest, dropped;

    static string Clip(string text, int max) { return text == null || text.Length <= max ? text : text.Substring(0, max); }
    internal static void Add(string source, string level, string message, string stack = null) {
        if (!Monitor.TryEnter(Gate)) { Interlocked.Increment(ref dropped); return; }
        try {
            long sequence = ++latest;
            Ring[(int)((sequence - 1) % Capacity)] = new Entry {
                Sequence = sequence, Utc = DateTime.UtcNow, Source = Clip(source, 128), Level = Clip(level, 32),
                Message = Clip(message, 1024), Stack = Clip(stack, 1024),
                MessageTruncated = message != null && message.Length > 1024, StackTruncated = stack != null && stack.Length > 1024
            };
        } finally { Monitor.Exit(Gate); }
    }

    internal static object Read(long since, int limit) {
        var entries = new List<Entry>(limit);
        long oldest, newest, next = since;
        lock (Gate) {
            oldest = Math.Max(1, latest - Capacity + 1);
            newest = latest;
            long first = since == 0 ? Math.Max(oldest, latest - limit + 1) : (since >= latest ? latest + 1 : Math.Max(oldest, since + 1));
            for (long seq = first; seq <= latest && entries.Count < limit; seq++) {
                entries.Add(Ring[(int)((seq - 1) % Capacity)]);
                next = seq;
            }
        }
        // Formatting/JSON allocations happen on the HTTP thread, outside the producer lock.
        var items = new List<object>(entries.Count);
        foreach (var entry in entries) {
            items.Add(SnapshotJson.Map("sequence", entry.Sequence, "utc", entry.Utc.ToString("O"),
                "source", entry.Source, "level", entry.Level, "message", entry.Message, "stack", entry.Stack,
                "messageTruncated", entry.MessageTruncated, "stackTruncated", entry.StackTruncated));
        }
        return SnapshotJson.Map("items", items, "nextSince", next, "latestSequence", newest,
            "oldestAvailableSequence", newest == 0 ? 0L : oldest, "overwritten", Math.Max(0, newest - Capacity),
            "cursorOverrun", since > 0 && since < oldest - 1, "droppedOnContention", Interlocked.Read(ref dropped),
            "note", "since=0 returns recent entries; subsequent polls use nextSince. History begins at plugin startup and resets on restart.");
    }
}
