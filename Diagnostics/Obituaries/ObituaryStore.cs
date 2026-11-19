using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Ostrabus.Diagnostics.Obituaries;

// Worker owns the files; game objects never cross this boundary. Fixed filenames
// bound storage across process restarts as well as across save reloads.
internal sealed class ObituaryStore {
    internal const int QueueCapacity = 64, FileCount = 4, RecordBytes = 32768;
    internal const long FileBytes = 16L * 1024 * 1024;
    readonly BlockingCollection<Dictionary<string, object>> queue = new BlockingCollection<Dictionary<string, object>>(QueueCapacity);
    readonly Task worker;
    readonly long fileLimit;
    readonly object files = new object();
    readonly string directory;
    long accepted, written, dropped, truncated, sequence;
    string error;
    int stopping;
    internal Task Completion => worker;
    internal ObituaryStore(string directory, long fileLimit = FileBytes) {
        this.directory = directory; this.fileLimit = Math.Max(1024, fileLimit);
        worker = Task.Factory.StartNew(Write, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }
    internal bool Enqueue(Dictionary<string, object> record) {
        // Sequence is a process-local cursor, paired with record.session.
        record["sequence"] = Interlocked.Increment(ref sequence);
        try {
            if (Volatile.Read(ref stopping) == 0 && queue.TryAdd(record)) { Interlocked.Increment(ref accepted); return true; }
        } catch (InvalidOperationException) { }
        Interlocked.Increment(ref dropped); return false;
    }
    internal object Status() => ObituaryRecorder.Map(
        "accepted", Interlocked.Read(ref accepted), "written", Interlocked.Read(ref written),
        "dropped", Interlocked.Read(ref dropped), "truncated", Interlocked.Read(ref truncated),
        "error", Volatile.Read(ref error), "stopping", Volatile.Read(ref stopping) != 0,
        "directory", directory, "maxDiskBytes", FileCount * fileLimit, "maxQueuedRecords", QueueCapacity, "maxRecordBytes", RecordBytes);
    internal void Stop() {
        if (Interlocked.Exchange(ref stopping, 1) == 0) queue.CompleteAdding();
        // Do not block Unity shutdown on a stalled disk. Worker drains on exit.
    }
    string PathFor(int index) => Path.Combine(directory, "obituaries-" + index + ".jsonl");
    void Rotate() {
        if (File.Exists(PathFor(FileCount - 1))) File.Delete(PathFor(FileCount - 1));
        for (int i = FileCount - 2; i >= 0; i--) if (File.Exists(PathFor(i))) File.Move(PathFor(i), PathFor(i + 1));
    }
    void Write() {
        try {
            Directory.CreateDirectory(directory);
            // A previous process may have stopped halfway through a JSON line.
            // Separate that tail from the next event instead of corrupting both.
            lock (files) if (File.Exists(PathFor(0)) && new FileInfo(PathFor(0)).Length > 0) {
                using (var tail = new FileStream(PathFor(0), FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete)) {
                    tail.Position = tail.Length - 1;
                    if (tail.ReadByte() != '\n' && tail.Length < fileLimit) { tail.Position = tail.Length; tail.WriteByte((byte)'\n'); }
                }
            }
            foreach (var record in queue.GetConsumingEnumerable()) {
                try {
                    record["droppedSoFar"] = Interlocked.Read(ref dropped);
                    byte[] bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(record, Settings) + "\n");
                    if (bytes.Length > RecordBytes || bytes.Length > fileLimit) {
                        Interlocked.Increment(ref truncated);
                        // Keep event identity even when its evidence exceeded the cap.
                        var reduced = new Dictionary<string, object>();
                        foreach (string key in new[] { "format", "session", "branch", "sequence", "utc", "epoch", "kind", "npcId", "shipId", "mechanism", "vesselEvent", "droppedSoFar" })
                            if (record.TryGetValue(key, out var value)) reduced[key] = value;
                        reduced["evidenceTruncated"] = true;
                        bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(reduced, Settings) + "\n");
                    }
                    lock (files) {
                        string path = PathFor(0);
                        if (File.Exists(path) && new FileInfo(path).Length + bytes.Length > fileLimit) Rotate();
                        using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read)) {
                            stream.Write(bytes, 0, bytes.Length); stream.Flush();
                        }
                    }
                    Interlocked.Increment(ref written);
                } catch { Interlocked.Increment(ref dropped); throw; }
            }
        } catch (Exception ex) { Volatile.Write(ref error, ex.GetType().Name + ": " + ex.Message); }
        finally { Stop(); while (queue.TryTake(out _)) Interlocked.Increment(ref dropped); }
    }
    static readonly JsonSerializerSettings Settings = new JsonSerializerSettings { FloatFormatHandling = FloatFormatHandling.String };
    internal object Read(string npcId, string shipId, string kind, string session, long since, int limit, double? after, double? before) {
        // Bounded result and line size. Read on the API worker, never on Unity.
        // Open all rotated files under the rotation lock, then release it before
        // scanning so a large query cannot stall the writer/game event producer.
        var streams = new List<FileStream>(FileCount);
        var result = new Queue<JObject>(limit);
        long matched = 0, corrupt = 0;
        try {
            lock (files) for (int i = FileCount - 1; i >= 0; i--) if (File.Exists(PathFor(i)))
                streams.Add(new FileStream(PathFor(i), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
            foreach (var stream in streams) {
                using (var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, true)) {
                    while (!reader.EndOfStream) {
                        string line = ReadBoundedLine(reader);
                        if (line == null) { corrupt++; continue; }
                        JObject row;
                        try {
                            using (var json = new JsonTextReader(new StringReader(line)) { DateParseHandling = DateParseHandling.None }) row = JObject.Load(json);
                        } catch (JsonException) { corrupt++; continue; }
                        if (row["session"]?.Type != JTokenType.String || row["sequence"]?.Type != JTokenType.Integer || row["kind"]?.Type != JTokenType.String) {
                            corrupt++; continue;
                        }
                        if (npcId != null && (string)row["npcId"] != npcId || shipId != null && (string)row["shipId"] != shipId ||
                            kind != null && (string)row["kind"] != kind || session != null && (string)row["session"] != session ||
                            (long?)row["sequence"] <= since || after.HasValue && (double?)row["epoch"] < after.Value ||
                            before.HasValue && (double?)row["epoch"] > before.Value) continue;
                        matched++; if (result.Count == limit) result.Dequeue(); result.Enqueue(row);
                    }
                }
            }
            var events = new List<object>(result.Count);
            foreach (var row in result) events.Add(Detach(row));
            return ObituaryRecorder.Map("events", events, "matched", matched, "corruptLines", corrupt,
                "selection", "latest matching retained events in file order", "status", Status());
        } catch (Exception ex) { return ObituaryRecorder.Map("error", ex.GetType().Name + ": " + ex.Message, "status", Status()); }
        finally { foreach (var stream in streams) stream.Dispose(); }
    }
    // The HTTP transport deliberately accepts only primitive dictionaries/lists,
    // not anonymous objects or Newtonsoft DOM nodes. Convert at this boundary
    // rather than widening the global encoder to arbitrary reflected objects.
    static object Detach(JToken token) {
        switch (token.Type) {
            case JTokenType.Object:
                var map = new Dictionary<string, object>();
                foreach (var property in ((JObject)token).Properties()) map[property.Name] = Detach(property.Value);
                return map;
            case JTokenType.Array:
                var list = new List<object>();
                foreach (var item in (JArray)token) list.Add(Detach(item));
                return list;
            case JTokenType.Integer: return token.Value<long>();
            case JTokenType.Float: return token.Value<double>();
            case JTokenType.Boolean: return token.Value<bool>();
            case JTokenType.Null: return null;
            default: return token.Value<string>();
        }
    }
    static string ReadBoundedLine(StreamReader reader) {
        var text = new StringBuilder(1024); bool overflow = false;
        int ch;
        while ((ch = reader.Read()) >= 0 && ch != '\n') {
            if (text.Length < RecordBytes) text.Append((char)ch); else overflow = true;
        }
        // A crash can leave an incomplete final line. Do not parse it as an event.
        return overflow || ch < 0 ? null : text.ToString();
    }
}
