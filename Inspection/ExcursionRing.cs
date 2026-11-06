using System;

namespace Ostrabus.Inspection;

// Main-thread only. Preallocated value slots; no game-object references.
internal sealed class ExcursionRing<T> where T : struct {
    readonly T[] values;
    internal long Latest { get; private set; }
    internal long Oldest => Math.Max(1, Latest - values.Length + 1);
    internal int Capacity => values.Length;
    internal ExcursionRing(int capacity) { if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity)); values = new T[capacity]; }
    internal long Add(T value) { long sequence = ++Latest; values[(sequence - 1) % values.Length] = value; return sequence; }
    internal bool TryRead(long sequence, out T value) {
        if (sequence < Oldest || sequence > Latest) { value = default; return false; }
        value = values[(sequence - 1) % values.Length]; return true;
    }
    internal void Clear() { Array.Clear(values, 0, values.Length); Latest = 0; }
}
