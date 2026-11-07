using System;
using System.Collections.Generic;

namespace Ostrabus.Optimizations;

// One idle buffer per thread. Active/nested operations never share buffers.
internal static class BarterWritebackScratch {
    internal const int RetainedLimit = 32768;
    [ThreadStatic] static Buffer cached;
    internal sealed class Buffer {
        internal readonly HashSet<string> Ids = new HashSet<string>(StringComparer.Ordinal);
        internal readonly List<JsonItem> Added = new List<JsonItem>();
        internal JsonItem[] Source;
        internal int Peak;
        internal void Index(JsonItem[] source) {
            Ids.Clear();
            foreach (var item in source) Ids.Add(item.strID);
            Source = source; Peak = Math.Max(Peak, Ids.Count);
        }
    }
    internal static Buffer Rent() { var result = cached ?? new Buffer(); cached = null; return result; }
    internal static void Release(Buffer scratch) {
        bool keep = scratch.Peak <= RetainedLimit && scratch.Added.Capacity <= RetainedLimit;
        scratch.Ids.Clear(); scratch.Added.Clear(); scratch.Source = null; scratch.Peak = 0;
        if (keep && cached == null) cached = scratch;
    }
}
