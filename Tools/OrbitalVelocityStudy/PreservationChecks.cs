using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using OrbitalVelocityStudy;

internal static partial class Program
{
    static void PreservationChecks()
    {
        double changedPosition = PhaseComparison.MaximumPositionChange();
        Gate(changedPosition == 0, "Position-preserving variant changed a native coordinate: " + changedPosition);
        ReplayedVcrsCheck();

        // Compare against the ACTUAL existing production helper. No helper
        // adaptation or tighter phase solver is installed in this variant.
        var production = Assembly.LoadFrom(Path.GetFullPath("bin/Debug/netstandard2.1/Ostrabus.dll"));
        var evaluate = AccessTools.Method(production.GetType("Ostrabus.Orbital.NativeEphemeris", true), "State");
        double maxConventionError = 0, maxAttachedPositionChange = 0;
        int stateCases = 0;
        foreach (double eccentricity in new[] { 0d, .0067, .455, .9 })
            foreach (int direction in new[] { -1, 1 })
            {
                var root = Body("Sol", 0, 0, 31556926);
                var planet = Body("Venus", .723, eccentricity, 19407509, root, direction);
                var station = Body("VORB", .00008, .455, 14470, planet, -direction);
                var auxiliary = Body("AUX", 0, 0, 14470, station);
                foreach (var body in new[] { root, planet, station, auxiliary })
                    foreach (double time in new[] { -101.25, 123.125, 432.4321386630625, 65634692000.25, 65634692000.25 + 21600 })
                    {
                        body.UpdateTime(time);
                        object[] arguments = { body, time, null, null };
                        Check((bool)evaluate.Invoke(null, arguments), "Existing detached ephemeris rejected valid reference");
                        object expectedVelocity = arguments[3];
                        double vx = (double)AccessTools.Field(expectedVelocity.GetType(), "X").GetValue(expectedVelocity);
                        double vy = (double)AccessTools.Field(expectedVelocity.GetType(), "Y").GetValue(expectedVelocity);
                        double error = Mag(body.dVelX - vx, body.dVelY - vy) * Meters;
                        maxConventionError = Math.Max(maxConventionError, error);
                        Gate(error < .00001, "Normal-time velocity differs from unchanged SFF convention: " + error);

                        var attached = new ShipSitu(false)
                        {
                            bBOLocked = true,
                            strBOPORShip = body.strName,
                            boReference = body,
                            vBOOffsetx = 10 / Meters,
                            vBOOffsety = -7 / Meters,
                            vPosx = body.dXReal + 10 / Meters,
                            vPosy = body.dYReal - 7 / Meters,
                            vVelX = body.dVelX,
                            vVelY = body.dVelY
                        };
                        double oldX = attached.vPosx, oldY = attached.vPosy;
                        attached.TimeAdvance(0);
                        double moved = Mag(attached.vPosx - oldX, attached.vPosy - oldY) * Meters;
                        maxAttachedPositionChange = Math.Max(maxAttachedPositionChange, moved);
                        Gate(moved == 0 && attached.bBOLocked && attached.strBOPORShip == body.strName,
                            "Analytical cache refresh changed an existing attachment's position or reference");
                        stateCases++;
                    }
            }

        int accepted = 0, declined = 0;
        double maximumAcceptedError = 0, maximumDeclinedMovement = 0;
        double oldEpoch = StarSystem.fEpoch;
        var oldWorld = CrewSim.system;
        try
        {
            var sample = Fixture["handoffs"][0];
            var parent = Model(sample["before"]["referenceBody"], (Newtonsoft.Json.Linq.JObject)sample["bodies"]);
            var world = Empty<StarSystem>();
            world.aBOs = new System.Collections.Generic.Dictionary<string, BodyOrbit>();
            for (var p = parent; p != null; p = p.boParent)
                world.aBOs[p.strName] = p;
            CrewSim.system = world;
            StarSystem.fEpoch = D(sample, "epoch");
            var parentState = HandoffPrototype.Detached(parent, StarSystem.fEpoch);
            const double nativeAu = 149597863936d;
            double mu = 6.674080038626684E-11 * parent.fMass;
            foreach (double eccentricity in new[] { 0d, .0067, .455, .9, .99 })
                foreach (int direction in new[] { -1, 1 })
                    foreach (double e in new[] { .00004, .00014, .01, .4, 1d, 2d, 3d, 4d, 5d, 6d })
                    {
                        double a = 10000000 / (1 - eccentricity), b = a * Math.Sqrt(1 - eccentricity * eccentricity);
                        double rate = Math.Sqrt(mu / (a * a * a)) / (1 - eccentricity * Math.Cos(e));
                        double x = a * (Math.Cos(e) - eccentricity), y = b * direction * Math.Sin(e);
                        double vx = -a * Math.Sin(e) * rate, vy = b * direction * Math.Cos(e) * rate;
                        var ship = FixtureShip(parentState.X + x / nativeAu, parentState.Y + y / nativeAu,
                            parentState.VX + vx / nativeAu, parentState.VY + vy / nativeAu);
                        double originalX = ship.objSS.vPosx, originalY = ship.objSS.vPosy;
                        double originalVX = ship.objSS.vVelX, originalVY = ship.objSS.vVelY;
                        var fitted = BodyOrbit.CreateBOFromShip(ship, parent);
                        if (fitted == null)
                        {
                            declined++;
                            double movement = Mag(ship.objSS.vPosx - originalX, ship.objSS.vPosy - originalY) * Meters;
                            maximumDeclinedMovement = Math.Max(maximumDeclinedMovement, movement);
                            Gate(movement == 0 && ship.objSS.vVelX == originalVX && ship.objSS.vVelY == originalVY && !ship.objSS.bOrbitLocked,
                                "Declined analytical optimization changed free flight");
                            continue;
                        }
                        accepted++;
                        world.aBOs[ship.strRegID] = fitted;
                        ship.objSS.LockToOrbit(fitted);
                        ship.objSS.TimeAdvance(0);
                        double error = Mag(ship.objSS.vVelX - originalVX, ship.objSS.vVelY - originalVY) * Meters;
                        maximumAcceptedError = Math.Max(maximumAcceptedError, error);
                        Gate(error <= .0051 && ship.objSS.vPosx == originalX && ship.objSS.vPosy == originalY,
                            "Accepted analytical optimization changed position or exceeded velocity tolerance: " + error);
                    }
        }
        finally { StarSystem.fEpoch = oldEpoch; CrewSim.system = oldWorld; }
        Log(new
        {
            kind = "existing-world-preservation",
            nativePositionChangeMeters = changedPosition,
            sameConventionStateCases = stateCases,
            maximumExistingSffVelocityDifferenceMps = maxConventionError,
            maximumAttachedPositionChangeM = maxAttachedPositionChange,
            fitCasesAccepted = accepted,
            fitCasesDeclinedToNativeFreeFlight = declined,
            maximumAcceptedHandoffErrorMps = maximumAcceptedError,
            maximumDeclinedFitPositionChangeM = maximumDeclinedMovement,
            note = "Retains native position solver and its known approximation error. No phase precision, authored elements, saved fields, reference hierarchy, or detached helper change."
        });
    }

    sealed class ReadoutPair
    {
        internal double Epoch, Raw, Corrected;
        internal string OrbitKey;
    }

    static void ReplayedVcrsCheck()
    {
        var values = new System.Collections.Generic.List<ReadoutPair>();
        foreach (var sample in (Newtonsoft.Json.Linq.JArray)Fixture["samples"])
        {
            Newtonsoft.Json.Linq.JToken own = null, target = null;
            foreach (var ship in (Newtonsoft.Json.Linq.JArray)sample["ships"])
            {
                if ((string)ship["id"] == (string)sample["playerShipId"])
                    own = ship["motion"];
                if ((string)ship["id"] == "B-7G57")
                    target = ship["motion"];
            }
            if (own == null || target == null || !(bool)own["orbitLocked"] ||
                D(own["rcsAcceleration"], "x") != 0 || D(own["rcsAcceleration"], "y") != 0)
                continue;
            double epoch = D(sample, "epoch"), dx = D(target, "xAU") - D(own, "xAU"), dy = D(target, "yAU") - D(own, "yAU");
            double range = Mag(dx, dy);
            double raw = (dx * (D(target, "vyAUPerSecond") - D(own, "vyAUPerSecond")) -
                dy * (D(target, "vxAUPerSecond") - D(own, "vxAUPerSecond"))) / range * Meters;
            var parentBodies = (Newtonsoft.Json.Linq.JObject)sample["bodies"];
            var playerOrbit = Model(own["referenceBody"], parentBodies);
            var targetOrbit = Model(target["referenceBody"], parentBodies);
            playerOrbit.UpdateTime(epoch);
            targetOrbit.UpdateTime(epoch);
            double corrected = (dx * (targetOrbit.dVelY - playerOrbit.dVelY) -
                dy * (targetOrbit.dVelX - playerOrbit.dVelX)) / range * Meters;
            values.Add(new ReadoutPair
            {
                Epoch = epoch,
                Raw = raw,
                Corrected = corrected,
                OrbitKey = (string)own["referenceBody"]["periodSeconds"] + "/" + (string)own["referenceBody"]["phaseSeconds"]
            });
        }
        values.Sort((a, b) => a.Epoch.CompareTo(b.Epoch));
        double largestRaw = 0, correspondingCorrected = 0, atEpoch = 0;
        for (int i = 1; i < values.Count; i++)
        {
            var a = values[i - 1];
            var b = values[i];
            if (a.OrbitKey != b.OrbitKey || b.Epoch - a.Epoch > 2)
                continue;
            double jump = Math.Abs(b.Raw - a.Raw);
            if (jump <= largestRaw)
                continue;
            largestRaw = jump;
            correspondingCorrected = Math.Abs(b.Corrected - a.Corrected);
            atEpoch = b.Epoch;
        }
        Gate(largestRaw > 3 && correspondingCorrected < .1, "Captured coast VCRS jump was not reduced by the position-preserving convention");
        Log(new
        {
            kind = "captured-vcrs-recalculation",
            rawStepMps = largestRaw,
            correctedStepMps = correspondingCorrected,
            atEpoch,
            note = "Offline coordinated-snapshot recalculation; not a patched live GUI measurement. Positions and input trajectory are identical."
        });
    }
}
