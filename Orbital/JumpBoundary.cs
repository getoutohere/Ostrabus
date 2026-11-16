using System;

namespace Ostrabus.Orbital;

// A synchronous world discontinuity, independent of the GUI and simulation
// toggles. Collision-only builds use the same boundary without running SFF AI.
internal sealed class JumpBoundary : IDisposable {
    internal static JumpBoundary Current { get; private set; }
    internal static bool Active => Current != null;
    internal static long Generation { get; private set; }
    internal readonly string Source;
    readonly JumpBoundary previous;
    bool disposed;

    internal JumpBoundary(string source) {
        Source = source; previous = Current; Current = this;
        unchecked { Generation++; }
    }
    public void Dispose() {
        if (disposed) return;
        disposed = true;
        Current = previous;
        // Lazy invalidation also covers hulls not observed during the jump.
        unchecked { Generation++; }
    }
}
