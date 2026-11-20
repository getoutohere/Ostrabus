using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HarmonyLib;
using Ostrabus.BottleRefiller;

internal static partial class Program
{
    private static void CurveCases()
    {
        double fill = 0, previousStep = double.PositiveInfinity;
        for (int second = 1; second <= 30; second++)
        {
            double next = FillCurve.NextFraction(fill, 1);
            Check(next > fill && next <= 1, "Fill curve failed monotonicity/cap");
            if (second == 5) Near(next, .9, "Curve does not reach 90% at five seconds");
            if (second > 5)
            {
                Check(next - fill < previousStep, "Final fill does not taper");
                previousStep = next - fill;
            }
            if (second < 30) Check(next < 1, "Bottle reached full early");
            fill = next;
        }
        Near(fill, 1, "Curve does not finish at thirty seconds");
        Near(FillCurve.NextFraction(.5, 0), .5, "Zero elapsed time filled gas");
        Near(FillCurve.NextFraction(.9, 1), FillCurve.NextFraction(.9, 21600), "Time skip backfilled curve");

        var clock = new ServiceClock();
        Check(!clock.Due(0, out _), "Service runs before one second elapsed");
        Check(!clock.Due(.5, out _), "Service runs per frame");
        Check(clock.Due(1, out double elapsed) && elapsed == 1, "One-second service missing");
        Check(!clock.Due(1, out _), "Duplicate same-epoch service");
        Check(clock.Due(21601, out elapsed) && elapsed == 1, "Time skip performs unbounded catch-up");
        Check(!clock.Due(21601, out _), "Time skip service repeats");
        Check(!clock.Due(100, out _), "Clock rewind causes phantom service");
        Check(clock.Due(101, out _), "Service does not recover after clock rewind");

        // Native pressure stepping, not a second pressure model: test the user
        // timeline for one/four bottles and both hot and cold incoming gas.
        double ticks = 0;
        long runtimeTicks = 0;
        foreach (int count in new[] {1, 4})
        foreach (double temperature in new[] {100d, 293d, 1000d})
        {
            var ship = MakeShip();
            var rack = Rack(ship);
            var source = Owner(ship);
            AddGas(source, temperature, 10, Mix(1000));
            var bottles = Enumerable.Range(0, count).Select(_ => Bottle(ship, rack)).ToArray();
            double initial = source.GasContainer.mapGasMols1["StatGasMolTotal"];
            for (int second = 1; second <= 30; second++)
            {
                long start = Stopwatch.GetTimestamp();
                Check(NativeTransfer.Fill(rack, source, bottles), "Native progressive fill stopped early");
                runtimeTicks += Stopwatch.GetTimestamp() - start;
                ticks++;
                foreach (var bottle in bottles)
                {
                    double fraction = bottle.GetCondAmount("StatGasPressure") / bottle.GetCondAmount("StatGasPressureMax");
                    if (second == 5) Near(fraction, .9, "Native five-second fill", 1e-8);
                    if (second == 30) Near(fraction, 1, "Native thirty-second fill", 1e-8);
                    if (second < 30) Check(fraction < .99999999, "Native bottle filled instantly/early");
                    Check(fraction <= 1, "Native curve overfilled bottle");
                }
                Near(initial, source.GasContainer.mapGasMols1["StatGasMolTotal"] + bottles.Sum(b => b.GasContainer.mapGasMols1["StatGasMolTotal"]), "Progressive fill lost/created gas");
            }
            Check(!NativeTransfer.Fill(rack, source, bottles), "Completed curve continues transferring");
        }
        Console.WriteLine("Native gas service (offline, no physics/audio): " +
            (runtimeTicks * 1000d / Stopwatch.Frequency / ticks).ToString("F4") + " ms/check; at most one check per game second.");
    }
}
