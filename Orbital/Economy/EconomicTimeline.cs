using System;
using System.Collections.Generic;

namespace Ostrabus.Orbital.Economy;

// One node per recurring activity, reused after every execution. No event-per-cycle allocation.
internal sealed class EconomicTimeline {
    internal sealed class Activity {
        internal double At;
        internal int Priority, Order;
        internal Func<double, double> Run;
    }
    sealed class Earlier : IComparer<Activity> {
        public int Compare(Activity a, Activity b) {
            int n = a.At.CompareTo(b.At);
            if (n == 0) n = a.Priority.CompareTo(b.Priority);
            return n != 0 ? n : a.Order.CompareTo(b.Order);
        }
    }
    static readonly Earlier earlier = new Earlier();
    readonly List<Activity> pending = new List<Activity>();
    readonly double start, end;
    int serial;
    internal int Executed { get; private set; }
    internal EconomicTimeline(double start, double end) { this.start = start; this.end = end; }
    internal void Add(double at, int priority, Func<double, double> run) {
        if (double.IsNaN(at) || double.IsInfinity(at) || at > end) return;
        Push(new Activity { At = Math.Max(start, at), Priority = priority, Order = serial++, Run = run });
    }
    void Push(Activity job) {
        int index = pending.Count;
        pending.Add(job);
        while (index > 0) {
            int parent = (index - 1) / 2;
            if (earlier.Compare(pending[parent], job) <= 0) break;
            pending[index] = pending[parent]; index = parent;
        }
        pending[index] = job;
    }
    Activity Pop() {
        var first = pending[0];
        int count = pending.Count - 1;
        var tail = pending[count]; pending.RemoveAt(count);
        if (count == 0) return first;
        int index = 0;
        while (index * 2 + 1 < count) {
            int child = index * 2 + 1;
            if (child + 1 < count && earlier.Compare(pending[child + 1], pending[child]) < 0) child++;
            if (earlier.Compare(tail, pending[child]) <= 0) break;
            pending[index] = pending[child]; index = child;
        }
        pending[index] = tail;
        return first;
    }
    internal void Run() {
        while (pending.Count != 0) {
            var job = Pop();
            double next = job.Run(job.At);
            Executed++;
            if (double.IsNaN(next) || double.IsInfinity(next) || next > end) continue;
            if (next <= job.At) throw new InvalidOperationException("Economic activity did not advance its clock.");
            job.At = next;
            Push(job);
        }
    }
    internal void Clear() { pending.Clear(); }
}
