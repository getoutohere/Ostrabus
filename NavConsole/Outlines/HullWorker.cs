using System;
using System.Collections.Generic;
using System.Threading;

namespace Ostrabus.NavConsole.Outlines;

// Numeric entries and buffers only. The queue cannot retain Ship, CO, renderer
// or capture objects. All expensive work happens outside gate.
internal sealed class HullWorker {
    internal const int MaxJobs = 4, MaxPoints = 131072;
    internal sealed class Entry {
        internal readonly int Generation;
        internal long Revision, Accepted = -1, Token, DrawingVersion;
        internal bool Retired, Pending;
        internal Envelope Result;
        internal string Error;
        internal Entry(int generation) { Generation = generation; }
    }
    internal readonly struct Ticket {
        internal readonly Entry Entry;
        internal readonly long Revision, Token;
        internal Ticket(Entry entry) { Entry = entry; Revision = entry.Revision; Token = entry.Token; }
    }
    sealed class Job {
        internal Ticket Ticket;
        internal HullPoint[] Buffer;
        internal int Count;
    }
    readonly object gate = new object();
    readonly Queue<Job> jobs = new Queue<Job>(MaxJobs);
    readonly HashSet<Entry> pending = new HashSet<Entry>();
    readonly List<HullPoint[]> buffers = new List<HullPoint[]>(MaxJobs);
    readonly EnvelopeBuilder builder = new EnvelopeBuilder();
    readonly Action<WaitCallback, object> schedule;
    readonly Func<HullPoint[], int, Envelope, Envelope> build;
    readonly WaitCallback run;
    bool running, stopped;
    int generation, reserved;
    long sequence, completed, discarded, failures;
    internal HullWorker(Action<WaitCallback, object> scheduler = null, Func<HullPoint[], int, Envelope, Envelope> calculate = null) {
        schedule = scheduler ?? Queue; build = calculate ?? builder.Build; run = Run;
    }
    static void Queue(WaitCallback callback, object state) { if (!ThreadPool.QueueUserWorkItem(callback, state)) throw new InvalidOperationException("Could not queue hull worker."); }
    internal Entry Create() { lock (gate) return new Entry(generation); }
    bool Current(Entry entry) => !stopped && !entry.Retired && entry.Generation == generation;
    bool Current(Ticket ticket) => Current(ticket.Entry) && ticket.Entry.Pending && ticket.Entry.Token == ticket.Token && ticket.Entry.Revision == ticket.Revision;
    internal void Dirty(Entry entry) { lock (gate) { if (Current(entry)) entry.Revision++; } }
    internal void Retire(Entry entry) { lock (gate) { entry.Retired = true; entry.Revision++; entry.Result = null; } }
    internal bool TryReserve(Entry entry, out Ticket ticket) {
        lock (gate) {
            ticket = default;
            if (!Current(entry) || entry.Pending || entry.Accepted == entry.Revision || reserved >= MaxJobs) return false;
            entry.Pending = true; entry.Token = ++sequence; reserved++; pending.Add(entry); ticket = new Ticket(entry); return true;
        }
    }
    internal bool Valid(Ticket ticket) { lock (gate) return Current(ticket); }
    internal bool Read(Entry entry, out Envelope result, out long version, out bool clean, out string error) {
        lock (gate) {
            result = Current(entry) ? entry.Result : null; version = entry.DrawingVersion;
            clean = Current(entry) && entry.Accepted == entry.Revision; error = entry.Error;
            return result != null;
        }
    }
    internal HullPoint[] Rent(int minimum) {
        if (minimum < 0 || minimum > MaxPoints) throw new ArgumentOutOfRangeException(nameof(minimum));
        lock (gate) {
            int best = -1;
            for (int i = 0; i < buffers.Count; i++)
                if (buffers[i].Length >= minimum && (best < 0 || buffers[i].Length < buffers[best].Length)) best = i;
            if (best >= 0) { var result = buffers[best]; buffers.RemoveAt(best); return result; }
        }
        int capacity = 256;
        while (capacity < minimum) capacity *= 2;
        return new HullPoint[capacity];
    }
    internal void Return(HullPoint[] buffer) {
        if (buffer == null) return;
        lock (gate) { if (!stopped && buffer.Length <= MaxPoints && buffers.Count < MaxJobs) buffers.Add(buffer); }
    }
    void Finish(Ticket ticket) {
        if (ticket.Entry.Pending && ticket.Entry.Token == ticket.Token) { ticket.Entry.Pending = false; reserved--; pending.Remove(ticket.Entry); }
    }
    internal void Cancel(Ticket ticket, string error = null) {
        lock (gate) { bool valid = Current(ticket); Finish(ticket); if (valid) ticket.Entry.Error = error; }
    }
    // Always consumes buffer ownership, including stale/rejected submissions.
    internal void Submit(Ticket ticket, HullPoint[] buffer, int count) {
        bool start = false, rejected;
        lock (gate) {
            rejected = !Current(ticket);
            if (rejected) { Finish(ticket); discarded++; }
            else {
                jobs.Enqueue(new Job { Ticket = ticket, Buffer = buffer, Count = count });
                if (!running) { running = true; start = true; }
            }
        }
        if (rejected) Return(buffer);
        if (start) {
            try { schedule(run, null); }
            catch (Exception error) {
                lock (gate) { running = false; ClearQueued(error.Message); failures++; }
            }
        }
    }
    void Run(object _) {
        while (true) {
            Job job; Envelope previous;
            lock (gate) {
                if (jobs.Count == 0) { running = false; if (stopped) builder.Release(); return; }
                job = jobs.Dequeue(); previous = job.Ticket.Entry.Result;
            }
            try {
                if (!Valid(job.Ticket)) { lock (gate) discarded++; continue; }
                Envelope result = build(job.Buffer, job.Count, previous);
                bool changed = previous == null || !result.SameDrawing(previous);
                lock (gate) {
                    if (!Current(job.Ticket)) discarded++;
                    else {
                        var entry = job.Ticket.Entry;
                        entry.Result = result; entry.Accepted = job.Ticket.Revision; entry.Error = null;
                        if (changed) entry.DrawingVersion++;
                        completed++;
                    }
                }
            } catch (Exception error) {
                lock (gate) { if (Current(job.Ticket)) job.Ticket.Entry.Error = error.Message; failures++; }
            } finally {
                lock (gate) Finish(job.Ticket);
                Return(job.Buffer);
            }
        }
    }
    void ClearQueued(string error = null) {
        while (jobs.Count > 0) { var job = jobs.Dequeue(); Finish(job.Ticket); job.Ticket.Entry.Error = error; Return(job.Buffer); }
    }
    internal void Reset(bool stop = false) {
        lock (gate) {
            generation++; stopped |= stop;
            foreach (var entry in pending) entry.Pending = false;
            pending.Clear(); reserved = 0;
            ClearQueued(); buffers.Clear();
            if (!running) builder.Release();
        }
    }
    internal object Inspect() {
        lock (gate) return new Dictionary<string, object> {
            ["generation"] = generation, ["running"] = running, ["stopped"] = stopped,
            ["queued"] = jobs.Count, ["reserved"] = reserved, ["pooledBuffers"] = buffers.Count,
            ["completed"] = completed, ["discarded"] = discarded, ["failures"] = failures
        };
    }
}
