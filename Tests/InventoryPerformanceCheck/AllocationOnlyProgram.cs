using System;
using System.Collections.Generic;
using Ostrabus.MiscBugFixes;

static partial class Program {
    static int checks;
    static bool allocationCounter;
    static void Check(bool ok, string reason) { checks++; if (!ok) throw new Exception(reason); }
    static void Main() {
        long before = GC.GetAllocatedBytesForCurrentThread(); GC.KeepAlive(new byte[1024]);
        allocationCounter = GC.GetAllocatedBytesForCurrentThread() > before;
        AllocationChecks();
        var records = new List<JsonItem> { new JsonItem { strID = "child", strParentID = "parent" } };
        DataHandler.dictCOSaves = new Dictionary<string, JsonCondOwnerSave> { ["child"] = new JsonCondOwnerSave() };
        LoadedInventoryParentPatch.Prefix(records, false, out var scope);
        LoadedInventoryParentPatch.Finalizer(scope);
        before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) {
            LoadedInventoryParentPatch.Prefix(records, false, out scope);
            LoadedInventoryParentPatch.Finalizer(scope);
        }
        Check(GC.GetAllocatedBytesForCurrentThread() == before, "Warm inventory parent scopes allocate.");
        Console.WriteLine("10,000 warm inventory parent scopes allocated zero bytes on .NET 8.");
    }
}
