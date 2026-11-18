using System;
using System.Collections.Generic;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OrbitalVelocityStudy;

internal static partial class Program
{
    static Ship FixtureShip(double x, double y, double vx, double vy)
    {
        var ship = Empty<Ship>();
        ship.strRegID = "STUDY-LIFECYCLE";
        AccessTools.Field(typeof(Ship), "nLoadState").SetValue(ship, Ship.Loaded.Full);
        AccessTools.Field(typeof(Ship), "_mass").SetValue(ship, 32000d);
        var situation = new ShipSitu(false) { vPosx = x, vPosy = y, vVelX = vx, vVelY = vy };
        situation.SetSize(12);
        AccessTools.Property(typeof(Ship), nameof(Ship.objSS)).SetValue(ship, situation);
        return ship;
    }
    static void LifecycleChecks(string stage)
    {
        var previousWorld = CrewSim.system;
        double previousEpoch = StarSystem.fEpoch;
        double maxHandoff = 0, maxSavedError = 0, maxPosition = 0;
        try
        {
            var f = Fixture["handoffs"][0];
            var m = f["before"];
            double start = D(f, "epoch");
            var parent = Model(m["referenceBody"], (JObject)f["bodies"]);
            var world = Empty<StarSystem>();
            world.aBOs = new Dictionary<string, BodyOrbit>();
            world.dictBOHierarchy = new Dictionary<BodyOrbit, BodyOrbit>();
            for (var p = parent; p != null; p = p.boParent)
                world.aBOs[p.strName] = p;
            CrewSim.system = world;
            StarSystem.fEpoch = start;
            var ship = FixtureShip(D(m, "xAU"), D(m, "yAU"), D(m, "vxAUPerSecond"), D(m, "vyAUPerSecond"));
            for (int cycle = 0; cycle < 64; cycle++)
            {
                parent.UpdateTime(StarSystem.fEpoch);
                double x = ship.objSS.vPosx, y = ship.objSS.vPosy, vx = ship.objSS.vVelX, vy = ship.objSS.vVelY;
                var body = BodyOrbit.CreateBOFromShip(ship, parent);
                Check(body != null, "Repeated handoff lost a bound orbit");
                CheckShipOrbitMetadata(body, ship, parent);
                world.AddBO(body, parent);
                Check(world.dictBOHierarchy[body] == parent, "Native registration lost the orbital parent");
                Check(parent.boChildren == null || !parent.boChildren.Contains(body), "Ship orbit became a permanent child reference");
                ship.objSS.LockToOrbit(body);
                ship.objSS.TimeAdvance(0);
                maxHandoff = Math.Max(maxHandoff, Mag(ship.objSS.vVelX - vx, ship.objSS.vVelY - vy) * Meters);
                maxPosition = Math.Max(maxPosition, Mag(ship.objSS.vPosx - x, ship.objSS.vPosy - y) * Meters);
                // Use native JSON round trips, not a new serialized velocity/history marker.
                var saved = JsonConvert.DeserializeObject<JsonBodyOrbitSave>(JsonConvert.SerializeObject(body.GetJSONSave()));
                var reloaded = new BodyOrbit(saved) { boParent = parent };
                CheckShipOrbitMetadata(reloaded, ship, parent);
                double later = StarSystem.fEpoch + .5 + (cycle % 4) * .125;
                body.UpdateTime(later);
                reloaded.UpdateTime(later);
                double error = Mag(body.dVelX - reloaded.dVelX, body.dVelY - reloaded.dVelY) * Meters;
                maxSavedError = Math.Max(maxSavedError, error);
                Gate(error < .00001, "Saved orbit changes prototype velocity: " + error);
                StarSystem.fEpoch = later;
                ship.objSS.TimeAdvance(later - start);
                ship.UnlockFromOrbit();
                Check(!ship.objSS.bOrbitLocked, "Native unlock left the ship orbit locked");
                Check(!world.aBOs.ContainsKey(body.strName), "Native unlock leaked the generated ship orbit");
                Check(!world.dictBOHierarchy.ContainsKey(body), "Native unlock leaked the generated parent relationship");
                Check(world.aBOs.ContainsKey(parent.strName), "Removing a generated ship orbit removed the parent");
            }
            Gate(maxHandoff < .01, "Repeated handoffs exceed velocity tolerance: " + maxHandoff);
            Gate(maxPosition < .001, "Repeated handoffs moved the hull: " + maxPosition);
            // An actual escaped trajectory must remain free rather than be force-fitted.
            parent.UpdateTime(StarSystem.fEpoch);
            var reference = HandoffPrototype.Detached(parent, StarSystem.fEpoch);
            var escape = FixtureShip(reference.X + .0001, reference.Y, reference.VX, reference.VY + 100000 / Meters);
            Gate(BodyOrbit.CreateBOFromShip(escape, parent) == null, "Escape trajectory was converted into a bound orbit");
            Log(new
            {
                kind = "native-lifecycle",
                stage,
                cycles = 64,
                nativeRemovalCycles = 64,
                maxHandoffErrorMps = maxHandoff,
                maxPositionChangeM = maxPosition,
                maxJsonRoundTripVelocityErrorMps = maxSavedError
            });
        }
        finally { CrewSim.system = previousWorld; StarSystem.fEpoch = previousEpoch; }
    }

    static void CheckShipOrbitMetadata(BodyOrbit body, Ship ship, BodyOrbit parent)
    {
        Check(body.strName == ship.strRegID && body.IsShipOrbit(), "Generated orbit lost its native ship identity");
        Check(body.nDrawFlagsBody == 1 && body.nDrawFlagsTrack == 2, "Generated orbit lost native draw flags");
        Check(body.fMass == ship.Mass, "Generated orbit changed the ship mass");
        double radius = ((float)ship.objSS.Size / 1000f) / 149597872.0;
        Check(body.fRadius == radius, "Generated orbit changed the native radius conversion");
        Check(body.fRotationPeriod == 87658.125, "Generated orbit changed the native rotation period");
        Check(body.boParent == parent, "Generated orbit changed the native parent identity");
    }
}
