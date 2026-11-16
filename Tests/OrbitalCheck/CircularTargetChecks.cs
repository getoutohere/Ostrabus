using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using HarmonyLib;
using Ostrabus.Orbital;
using Ostrabus.Orbital.Economy;
using Ostranauts.ShipGUIs.Utilities;

static class CircularTargetChecks {
    internal static void Run(Action<bool, string> check, BodyOrbit body, double start) {
        var prior = CrewSim.system;
        var world = (StarSystem)FormatterServices.GetUninitializedObject(typeof(StarSystem));
        world.aBOs = new Dictionary<string, BodyOrbit> { [body.strName] = body };
        CrewSim.system = world;
        try {
            using var context = (SffSimulation)AccessTools.Constructor(typeof(SffSimulation), new[] { typeof(double), typeof(double) })
                .Invoke(new object[] { start, 7200d });
            foreach (double rate in new[] { 0d, .0002, -.0002 }) {
                var target = new ShipSitu(false) { bBOLocked = true, strBOPORShip = body.strName,
                    vBOOffsetx = 9000 / OrbitalMotion.KmPerAU, vBOOffsety = 3000 / OrbitalMotion.KmPerAU,
                    fOrbitW = rate, fOrbitTimeLast = start - 321 };
                var captured = (ISffTarget)AccessTools.Method(typeof(SffSimulation), "CaptureTarget")
                    .Invoke(context, new object[] { world, target });
                var route = new NavData((Ship)null);
                route.AddNavDataPoint(new NavDataPoint(start, new ShipSitu(target, false), 100, 0, 100, 4));
                route.AddNavDataPoint(new NavDataPoint(start + 7200, new ShipSitu(target, false), 100, 0, 100, 4));
                var plannedTarget = new ShipSitu(false) { NavData = route };
                var capturedPath = (ISffTarget)AccessTools.Method(typeof(SffSimulation), "CaptureTarget")
                    .Invoke(context, new object[] { world, plannedTarget });
                foreach (double elapsed in new[] { 0d, 60, 3600, 7200, 10000 }) {
                    double epoch = start + elapsed;
                    target.GetBOOffsetPrediction(epoch, out double x, out double y);
                    check(OrbitalMotion.BodyAt(body, epoch, out var parent), "Circular target parent ephemeris failed");
                    var offset = new DVector(x, y) * OrbitalMotion.KmPerAU;
                    var position = parent.Position + offset;
                    var velocity = parent.Velocity + new DVector(-rate * offset.Y, rate * offset.X);
                    foreach (var actual in new[] { captured.At(epoch), capturedPath.At(epoch),
                        FreightRoute.StationAt(target, epoch), FreightRoute.Sample(route, epoch, out _, out _) }) {
                        check((actual.Position - position).Length < 1e-6, "Locked target prediction discarded native circular phase");
                        check((actual.Velocity - velocity).Length < 1e-8, "Locked target prediction discarded native tangential velocity");
                    }
                    check(OrbitAcceleration.TryFrame(body, epoch, out var frame, out _), "Circular target frame failed");
                    var acceleration = new DVector(frame.X, frame.Y) * OrbitalMotion.KmPerAU - offset * (rate * rate);
                    check((captured.Acceleration(epoch) - acceleration).Length < 1e-10,
                        "Circular target prediction discarded native centripetal acceleration");
                }
                check(target.fOrbitTimeLast == start - 321 && target.vBOOffsetx == 9000 / OrbitalMotion.KmPerAU,
                    "Prediction changed the live target's orbit cache");
            }
        } finally { CrewSim.system = prior; }
    }
}
