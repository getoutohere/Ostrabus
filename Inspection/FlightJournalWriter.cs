using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Ostrabus.Inspection;

internal sealed class FlightJournalWriter {
    internal const int QueueCapacity = 16, RecordLimit = 8192;
    internal const long ByteLimit = 128L * 1024 * 1024;
    readonly BlockingCollection<FlightJournalData.Record> queue = new BlockingCollection<FlightJournalData.Record>(QueueCapacity);
    readonly object gate = new object();
    readonly object[] recent = new object[64];
    readonly Task worker;
    readonly Action<FlightJournalData.Record> release;
    readonly long byteLimit;
    long sequence, accepted, dropped, written, bytes;
    int accepting = 1;
    string error;
    internal readonly string Path;
    sealed class LimitedStream : Stream {
        readonly Stream inner;
        readonly FlightJournalWriter owner;
        internal long Limit;
        internal LimitedStream(Stream stream, FlightJournalWriter journal) { inner = stream; owner = journal; Limit = journal.byteLimit; }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => inner.Length;
        public override long Position { get => Interlocked.Read(ref owner.bytes); set => throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) {
            if (Position + count > Limit) throw new IOException("Flight journal size limit reached.");
            inner.Write(buffer, offset, count); Interlocked.Add(ref owner.bytes, count);
        }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
    internal bool Accepting => Volatile.Read(ref accepting) != 0;
    internal string Error => Volatile.Read(ref error);
    internal bool Finished => worker.IsCompleted;
    internal Task Completion => worker;
    internal FlightJournalWriter(string path, long limit = ByteLimit, Action<FlightJournalData.Record> releaseRecord = null) {
        Path = path; byteLimit = limit; release = releaseRecord ?? FlightJournalData.Release;
        worker = Task.Factory.StartNew(Write, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }
    internal bool Enqueue(FlightJournalData.Record record) {
        bool queued = false;
        if (!Monitor.TryEnter(gate)) { Interlocked.Increment(ref dropped); release(record); return false; }
        try {
            if (!Accepting) return false;
            if (accepted >= RecordLimit) { Volatile.Write(ref error, "Flight journal record limit reached."); Complete(); return false; }
            record.Sequence = ++sequence;
            var metadata = SnapshotJson.Map("sequence", record.Sequence, "kind", record.Kind, "epoch", record.Epoch,
                "region", record.Region, "ships", record.Count, "captureMs", record.CaptureMs,
                "truncated", record.ShipsTruncated || record.BodiesTruncated || record.AisTruncated || record.PlayerCourseTruncated,
                "newSffReport", record.NewSffReport);
            try { queued = queue.TryAdd(record); } catch (InvalidOperationException) { }
            if (!queued) return false;
            Interlocked.Increment(ref accepted);
            recent[(int)((accepted - 1) % recent.Length)] = metadata;
            return true;
        } finally {
            Monitor.Exit(gate);
            if (!queued) { Interlocked.Increment(ref dropped); release(record); }
        }
    }
    internal void Complete() {
        if (Interlocked.Exchange(ref accepting, 0) != 0) queue.CompleteAdding();
    }
    void Write() {
        try {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
            using (var file = new FileStream(Path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 65536, FileOptions.SequentialScan))
            using (var bounded = new LimitedStream(file, this))
            using (var writer = new StreamWriter(bounded, new UTF8Encoding(false), 65536) { NewLine = "\n" }) {
                var settings = new JsonSerializerSettings { FloatFormatHandling = FloatFormatHandling.String };
                var serializer = JsonSerializer.Create(settings);
                const string header = "{\"format\":\"ostrabus-flight-journal\",\"version\":1,\"units\":\"AU, seconds, kg; fuel fields explicitly cached/raw\"}";
                writer.WriteLine(header); writer.Flush(); bounded.Limit = byteLimit - 1024;
                foreach (var record in queue.GetConsumingEnumerable()) {
                    try {
                        // Stream each record instead of building a world-sized JSON string.
                        using (var json = new JsonTextWriter(writer) { CloseOutput = false, AutoCompleteOnClose = false }) {
                            serializer.Serialize(json, record); json.Flush();
                        }
                        writer.WriteLine(); writer.Flush(); Interlocked.Increment(ref written);
                    } catch { Interlocked.Increment(ref dropped); throw; }
                    finally { release(record); }
                }
                bounded.Limit = byteLimit;
                string footer = JsonConvert.SerializeObject(new { kind = "journal-end", accepted = Interlocked.Read(ref accepted),
                    written = Interlocked.Read(ref written), dropped = Interlocked.Read(ref dropped) });
                writer.WriteLine(footer); writer.Flush();
            }
        } catch (Exception ex) { var cause = ex.GetBaseException(); Volatile.Write(ref error, cause.GetType().Name + ": " + cause.Message); }
        finally {
            Complete();
            while (queue.TryTake(out var leftover)) { Interlocked.Increment(ref dropped); release(leftover); }
            queue.Dispose();
        }
    }
    internal object Summary(bool active, long captureErrors) {
        object[] history;
        lock (gate) {
            int count = (int)Math.Min(accepted, recent.Length); history = new object[count];
            for (int i = 0; i < count; i++) history[i] = recent[(int)((accepted - count + i) % recent.Length)];
        }
        return SnapshotJson.Map("active", active, "file", Path, "writerFinished", Finished, "accepting", Accepting,
            "accepted", Interlocked.Read(ref accepted), "written", Interlocked.Read(ref written), "dropped", Interlocked.Read(ref dropped),
            "bytesWritten", Interlocked.Read(ref bytes), "captureErrors", captureErrors, "writerError", Error,
            "queueCapacity", QueueCapacity, "recordLimit", RecordLimit, "byteLimit", byteLimit, "recent", history);
    }
}
