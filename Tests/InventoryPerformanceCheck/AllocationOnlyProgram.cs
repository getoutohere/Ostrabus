using System;

static partial class Program {
    static int checks;
    static bool allocationCounter;
    static void Check(bool ok, string reason) { checks++; if (!ok) throw new Exception(reason); }
    static void Main() {
        long before = GC.GetAllocatedBytesForCurrentThread(); GC.KeepAlive(new byte[1024]);
        allocationCounter = GC.GetAllocatedBytesForCurrentThread() > before;
        AllocationChecks();
    }
}
