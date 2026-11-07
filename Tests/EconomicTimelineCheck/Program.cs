using System;
using System.Diagnostics;
using Ostrabus.Orbital.Economy;

static class Program {
    static void Main() {
        EconomicTimeline Make() {
            var queue = new EconomicTimeline(0, 10000);
            for (int i = 0; i < 100; i++) queue.Add(1, i % 3, at => at + 1);
            return queue;
        }
        Make().Run();
        var test = Make();
        long before = GC.GetAllocatedBytesForCurrentThread();
        long began = Stopwatch.GetTimestamp();
        test.Run();
        long ticks = Stopwatch.GetTimestamp() - began;
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        if (test.Executed != 1000000 || bytes != 0) throw new Exception($"Queue count={test.Executed}, allocated={bytes}");
        Console.WriteLine($"1,000,000 economic events: {bytes} scheduler bytes; {ticks * 1000d / Stopwatch.Frequency:F1} ms (.NET 8 allocation counter).");
    }
}
