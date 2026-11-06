using System;
using System.Collections;
using System.Diagnostics;

namespace Ostrabus.Optimizations;

// Drive the original iterator in order. Only cheap restore-phase null yields may be coalesced.
internal sealed class SystemInitBatching : IEnumerator, IDisposable {
    [ThreadStatic] static SystemInitBatching executing;
    readonly IEnumerator inner;
    readonly long budgetTicks;
    readonly int maxSteps;
    bool cheapPhase = true, completed, disposed;
    int progressRevision;

    internal SystemInitBatching(IEnumerator inner, double budgetMs, int maxSteps = 128) {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        if (double.IsNaN(budgetMs) || double.IsInfinity(budgetMs)) budgetMs = 4;
        budgetTicks = Math.Max(1, (long)(Math.Max(0.5, Math.Min(8, budgetMs)) * Stopwatch.Frequency / 1000d));
        this.maxSteps = Math.Max(1, maxSteps);
        LoadProfiling.Count("systemInitBatchingEnabled", 1);
        LoadProfiling.Count("systemInitBatchBudgetMicroseconds", (long)(budgetTicks * 1000000d / Stopwatch.Frequency));
    }
    public object Current => inner.Current;

    // Invoked only during synchronous execution of this iterator, never from a global stage cache.
    internal static void Progress(string label) {
        SystemInitBatching current = executing;
        if (current == null) return;
        current.progressRevision++;
        current.cheapPhase = label == "Parse System Bodies" || label == "Parse Asteroid Fields" || label == "Parse System Body Hierarchy";
    }

    public bool MoveNext() {
        if (completed || disposed) return false;
        long start = Stopwatch.GetTimestamp();
        int steps = 0, suppressed = 0, returnedKind = -1;
        var sample = LoadProfiling.Enter("StarSystem.Init.batch");
        Exception error = null;
        try {
            while (true) {
                int revision = progressRevision;
                bool result;
                SystemInitBatching previous = executing;
                executing = this;
                try { steps++; result = inner.MoveNext(); }
                finally { executing = previous; }
                if (!result) { completed = true; return false; }
                object yielded = inner.Current;
                returnedKind = yielded == null ? 0 : 1;
                // Preserve progress redraws, all non-null waits/child iterators, and every non-restore phase yield.
                if (yielded != null || !cheapPhase || progressRevision != revision || steps >= maxSteps ||
                    Stopwatch.GetTimestamp() - start >= budgetTicks) return true;
                suppressed++;
            }
        }
        catch (Exception ex) { error = ex; throw; }
        finally {
            LoadProfiling.Count("systemInitBatchCalls", 1);
            LoadProfiling.Count("systemInitInnerSteps", steps);
            LoadProfiling.Count("systemInitSuppressedNullYields", suppressed);
            if (!completed && error == null) LoadProfiling.Count(returnedKind == 0 ? "systemInitReturnedNullYields" : "systemInitReturnedOtherYields", 1);
            LoadProfiling.Exit(sample, error);
        }
    }
    public void Reset() { inner.Reset(); }
    public void Dispose() {
        if (disposed) return;
        disposed = true;
        (inner as IDisposable)?.Dispose();
    }
}
