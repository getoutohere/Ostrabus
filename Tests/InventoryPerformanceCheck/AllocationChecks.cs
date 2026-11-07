using System;
using System.Collections.Generic;
using System.Linq;
using Ostrabus.Optimizations;

static partial class Program {
    static void AllocationChecks() {
        Check(allocationCounter, "Allocation counter does not work in this runtime");
        var saved = Enumerable.Range(0, 8000).Select(i => new JsonItem { strID = "key" + i }).ToArray();
        Action write = () => { var lease = BarterWritebackScratch.Rent(); lease.Index(saved); BarterWritebackScratch.Release(lease); };
        for (int i = 0; i < 10; i++) write();
        long start = GC.GetAllocatedBytesForCurrentThread(); for (int i = 0; i < 1000; i++) write();
        long bytes = GC.GetAllocatedBytesForCurrentThread() - start;
        Check(bytes == 0, "Warmed barter scratch allocates " + bytes);
        Console.WriteLine(".NET allocation check: 1,000 barter ID-index cycles (8,000 IDs each): " + bytes + " bytes.");
        var ids = saved.Select(i => i.strID).ToArray(); var pending = new List<string>(ids);
        var groups = new Dictionary<string, List<string>> { ["hull"] = pending };
        Action trim = () => {
            pending.Clear(); pending.AddRange(ids); var lease = SaveRemainingIdsPatch.Begin();
            foreach (var id in ids) SaveRemainingIdsPatch.Remove(pending, id);
            SaveRemainingIdsPatch.Values(groups); SaveRemainingIdsPatch.End(lease);
        };
        for (int i = 0; i < 10; i++) trim();
        start = GC.GetAllocatedBytesForCurrentThread(); for (int i = 0; i < 1000; i++) trim();
        bytes = GC.GetAllocatedBytesForCurrentThread() - start;
        Check(bytes == 0, "Warmed save scratch allocates " + bytes);
        Console.WriteLine(".NET allocation check: 1,000 save trim cycles (8,000 IDs each): " + bytes + " bytes.");
        var list = new List<JsonItem>(1); var item = new JsonItem(); list.Add(item); ContainedItemRemovalPatch.Remove(list, item, 0);
        start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) { list.Add(item); ContainedItemRemovalPatch.Remove(list, item, 0); }
        Check(GC.GetAllocatedBytesForCurrentThread() == start, "Known-index removals allocate");
        Console.WriteLine(checks + " allocation checks passed. No Unity engine calls or Harmony hooks used in this mode.");
    }
}
