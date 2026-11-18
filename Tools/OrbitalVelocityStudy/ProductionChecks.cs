using System;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ostrabus.Orbital;

internal static partial class Program
{
    static void InstallProduction(Harmony owner)
    {
        owner.CreateClassProcessor(typeof(OrbitalVelocityPatch)).Patch();
        owner.CreateClassProcessor(typeof(CoastOrbitFitPatch)).Patch();
    }

    static void ProductionChecks()
    {
        var sample = Fixture["handoffs"][0];
        var body = Model(sample["before"]["referenceBody"], (JObject)sample["bodies"]);
        double epoch = D(sample, "epoch");
        body.UpdateTime(epoch);
        double x = body.dXReal, y = body.dYReal, vx = body.dVelX, vy = body.dVelY;
        double parentVelocityTime = body.boParent.dVelCalcLast;

        Check(OrbitVelocity.TryState(body, epoch + 12345, out _), "Detached production query rejected a supported hierarchy");
        Check(body.dXReal == x && body.dYReal == y && body.dVelX == vx && body.dVelY == vy &&
            body.dTimeCalcLast == epoch && body.dVelCalcLast == epoch && body.boParent.dVelCalcLast == parentVelocityTime,
            "Detached production query changed live caches");

        body.UpdateTime(epoch + 21600, bCalcV: false);
        Check(body.dTimeCalcLast == epoch + 21600 && body.dVelCalcLast == epoch &&
            body.dVelX == vx && body.dVelY == vy && body.boParent.dVelCalcLast == parentVelocityTime,
            "Future position query consumed or rewrote the velocity clock");
        body.UpdateTime(epoch);
        Check(body.dXReal == x && body.dYReal == y && body.dVelX == vx && body.dVelY == vy,
            "Restoring the current epoch introduced a cache-dependent jump");

        body.UpdateTime(epoch + .25);
        Check(body.dVelCalcLast == epoch + .25 && body.boParent.dVelCalcLast == epoch + .25,
            "Subsecond updates retained native 1.5-second velocity staleness");
        Check(OrbitVelocity.TryState(body, epoch + .25, out var state) &&
            body.dVelX == state.VX && body.dVelY == state.VY,
            "Cached and detached production tangent velocities disagree");

        BodyOrbit sentinel = body;
        Check(!CoastOrbitFitPatch.Prefix(null, body, ref sentinel, __runOriginal: false) && ReferenceEquals(sentinel, body),
            "A prior Harmony prefix's factory result was overwritten");
        Check(!OrbitalVelocityPatch.Prefix(body, epoch, true, true, __runOriginal: false) && body.dTimeCalcLast == epoch + .25,
            "A prior Harmony prefix's update cancellation was ignored");

        var unsupported = new BodyOrbit(new JsonBodyOrbitSave
        {
            strName = "negative-period-fixture", fAxis1 = .001, fAxis2 = .001,
            fPerh = .0005, fPeriod = -1000, nOrbitDirection = 1
        });
        Check(!OrbitVelocity.Supports(unsupported), "Unsupported retrograde representation was claimed");
        sentinel = body;
        Check(CoastOrbitFitPatch.Prefix(null, unsupported, ref sentinel, true) && ReferenceEquals(sentinel, body),
            "Unsupported factory input did not retain native handling");
        Check(!OrbitVelocity.TryState(unsupported, epoch, out _), "Unsupported detached state was accepted");
        Check(!OrbitVelocity.TryState(body, double.NaN, out _), "Nonfinite epoch was accepted");
        Check(CoastOrbitFit.Fit(null, body) == null, "Missing ship produced a coasting orbit");
        Log(new { kind = "production-cache-and-prefix-contracts", passed = true });
    }
}
