using System;
using System.Collections.Generic;
using HarmonyLib;
using Ostrabus.ShipNavigation;
using Ostranauts.Core;
using Ostranauts.ShipGUIs.Utilities;
using Ostranauts.Ships;
using Ostranauts.Ships.Commands;

static partial class Program {
    static void PlanningChecks() {
        World();
        var ship = Vessel();
        var route = Plan(ship);
        Check(route != null && route.Destination.ArrivalTime - route.Origin.ArrivalTime < 600, "Corrected approach did not converge within ten minutes");
        Check(route.Destination.FuelLevel > 500, "Corrected approach wasted the captured RCS reserve");
        Check(AiRendezvous.Arrives(route, Target()), "Corrected approach failed independent arrival check");
        Check(Math.Abs(ship.fShallowRCSRemass - (double)fixture["rcsFuelKg"]) < 1e-8, "Planning consumed actual fuel");
        var actual = new FlyToPath(Controller(ship));
        actual.PlanTrip4(new NavDataPoint(StarSystem.fEpoch, new ShipSitu(ship.objSS, false)), Target(), .2f, 1);
        foreach (float limiter in new[] { 0f, .01f, .2f, .8f, 1f }) {
            double result = (double)Acceleration.Invoke(actual, new object[] { false, limiter });
            Check(Math.Abs(result - ship.RCSAccelMax) < 1e-20, "NPC RCS still depends on reactor throttle");
        }
        ship.LiftRotorsThrustStrength = 100;
        double lift = (double)Acceleration.Invoke(actual, new object[] { true, .2f });
        Check(Math.Abs(lift - 100d / 149597870f * .2f) < 1e-15, "Repair changed atmospheric rotor limiting");
        ship.LiftRotorsThrustStrength = 0;
        foreach (AIType role in new[] { AIType.HaulerCargo, AIType.Civilian, AIType.Navy, AIType.Pirate, AIType.Scav, AIType.Police }) {
            var planner = new FlyToPath(Controller(ship, role));
            Check(Plan(ship, .2f, planner) != null, "Shared approach repair failed for " + role);
        }
        // Empty shallow tanks remain a permitted flight abstraction; do not add fuel.
        ship.fShallowRCSRemass = 0;
        Check(Plan(ship) != null && ship.fShallowRCSRemass == 0, "Repair changed shallow empty-tank navigation policy");
        ship.fShallowRCSRemass = 1552;
        ship.IsAIShip = false;
        var playerRoute = Plan(ship, .2f);
        Check(playerRoute != null && playerRoute.Destination.ArrivalTime - playerRoute.Origin.ArrivalTime > 86400,
            "NPC repair altered native player planning");
        ship.IsAIShip = true; playerManned = true;
        Check(Plan(ship, .2f) != null, "Player-manned AI hull was subjected to NPC rejection");
        playerManned = false;
        CrewSim.coPlayer = Empty<CondOwner>(); Set(CrewSim.coPlayer, "_objShip", ship);
        Check(!AiRendezvous.Applies(new FlyToPath(Controller(ship))), "Player's occupied ship was treated as NPC");
        CrewSim.coPlayer = null;

        World(); ship = Vessel(); detour = true;
        route = Plan(ship);
        Check(route != null, "Recursive native intermediate leg was rejected against the final station");
        Check(route.Destination.ObjSS.GetDistance(station.objSS) > 15000d / 149597872000d,
            "Intermediate-leg fixture accidentally reached the final station");

        World(); ship = Vessel(); ship.bFusionReactorRunning = true;
        ship.objSS.vPosx = -1.00865980697427; ship.objSS.vPosy = -1.22446751357313;
        ship.objSS.vVelX = 1.09912948887742e-7; ship.objSS.vVelY = -1.08974996358881e-7;
        ship.fShallowFusionRemain = 1000000;
        route = Plan(ship);
        Check(route != null && route.IsUsingTorchDrive(), "Valid interplanetary NPC torch leg was rejected");
        Check(AiRendezvous.Arrives(route, Target()), "Interplanetary torch leg failed arrival validation");

        int accepted = 0, rejected = 0;
        foreach (double phase in new[] { 0d, 3600d, 7200d })
        foreach (double mass in new[] { .1d, 1d, 10d })
        foreach (double distance in new[] { 40000d, 300000d, 2500000d }) {
            World(); StarSystem.fEpoch += phase; station.objSS.UpdateTime(StarSystem.fEpoch);
            ship = Vessel(mass);
            ship.objSS.vPosx = station.objSS.vPosx + distance / 149597872000d;
            ship.objSS.vPosy = station.objSS.vPosy;
            ship.objSS.vVelX = station.objSS.vVelX; ship.objSS.vVelY = station.objSS.vVelY;
            double fuel = ship.fShallowRCSRemass;
            route = Plan(ship);
            Check(ship.fShallowRCSRemass == fuel && ship.objSS.NavData == null, "Planning mutated fuel or installed a trial route");
            if (route == null) { rejected++; continue; }
            accepted++;
            Check(AiRendezvous.Arrives(route, Target()), "Native matrix admitted an unconverged rendezvous");
            foreach (var point in route.GetNavDataPoints()) {
                double acceleration = point.ObjSS.vAccRCS.magnitude;
                Check(acceleration <= 1.9016982118751132e-10 * 1.00001, "Native NPC RCS safety cap was bypassed");
            }
        }
        Check(accepted > 0 && rejected > 0, "Phase/mass/distance matrix did not exercise acceptance and rejection");
    }
    static void ValidationChecks() {
        World(); var ship = Vessel();
        foreach (double offset in new[] { 0d, 4920d, 5800d })
            Check(AiRendezvous.Arrives(Synthetic(ship, offset), Target()), "Valid native RCS stand-off was rejected");
        Check(!AiRendezvous.Arrives(Synthetic(ship, 6500), Target()), "RCS miss beyond tolerance was accepted");
        Check(AiRendezvous.Arrives(Synthetic(ship, 300000, true), Target()), "Native torch stand-off was rejected");
        Check(!AiRendezvous.Arrives(Synthetic(ship, 302000, true), Target()), "Torch miss beyond tolerance was accepted");
        foreach (Action<NavData> corrupt in new Action<NavData>[] {
            n => n.Destination.ObjSS.vPosx = double.NaN,
            n => Set(n.Destination.ObjSS, "_vVelY", double.PositiveInfinity),
            n => n.Destination.ArrivalTime = double.PositiveInfinity,
            n => n.Destination.ArrivalTime = n.Origin.ArrivalTime - 1,
            n => n.Destination.FuelLevel = double.NaN,
            n => n.Destination.TorchCycle = double.NaN,
            n => n.Destination.ArrivalTime = n.Origin.ArrivalTime,
            n => n.Destination.ObjSS.vVelX += 101d / 149597872000d
        }) {
            var bad = Synthetic(ship); corrupt(bad);
            Check(!AiRendezvous.Arrives(bad, Target()), "Malformed or non-rendezvous route accepted");
        }
        var before = new Dictionary<string, double>();
        foreach (var pair in CrewSim.system.aBOs) before[pair.Key] = pair.Value.dTimeCalcLast;
        Check(AiRendezvous.Arrives(Synthetic(ship), Target()), "Valid route rejected during clock control");
        foreach (var pair in before) Check(CrewSim.system.aBOs[pair.Key].dTimeCalcLast == pair.Value, "Validation changed shared ephemeris clock");
        var exceptional = Synthetic(ship); throwFuture = true;
        bool threw = false;
        try { AiRendezvous.Arrives(exceptional, Target()); } catch (InvalidOperationException) { threw = true; }
        throwFuture = false;
        Check(threw, "Exception fixture did not run");
        foreach (var pair in before) Check(CrewSim.system.aBOs[pair.Key].dTimeCalcLast == pair.Value, "Exception leaked a future body clock");

        // Arrival sampling must restore a historical planning clock, not global now.
        double epoch = StarSystem.fEpoch;
        var destination = Target(); var historical = Synthetic(ship);
        station.objSS.UpdateTime(epoch - 7200);
        Check(AiRendezvous.Arrives(historical, destination), "Historical-scope arrival was rejected");
        Check(CrewSim.system.aBOs["VORB"].dTimeCalcLast == epoch - 7200, "Historical clock was restored to global now");
        station.objSS.UpdateTime(epoch);

        World(); ship = Vessel();
        station.objSS.bIsBO = station.objSS.bBOLocked = station.objSS.bOrbitLocked = false;
        station.objSS.vVelX = station.objSS.vVelY = 0;
        ship.objSS.vVelX = ship.objSS.vVelY = 0;
        ship.IsAIShip = false;
        var nativeFree = Plan(ship, 1);
        ship.IsAIShip = true;
        Check(nativeFree != null && AiRendezvous.Arrives(nativeFree, Target()), "Validator rejected native free-hull target semantics");
        Check(Plan(ship, 1) != null, "Production repair blocked a free-hull visit route");
        nativeFree.Destination.ObjSS.vPosx += 10000d / 149597872000d;
        Check(!AiRendezvous.Arrives(nativeFree, Target()), "Validator accepted a miss against the native free-hull target");

        World(); ship = Vessel();
        station.objSS.bIsBO = station.objSS.bBOLocked = station.objSS.bOrbitLocked = false;
        ship.IsAIShip = false; nativeFree = Plan(ship, 1); ship.IsAIShip = true;
        Check(nativeFree != null && !AiRendezvous.Arrives(nativeFree, Target()), "Divergent native free-hull route was accepted");
        Check(Plan(ship, 1) == null, "Production planner published a divergent free-hull visit");

        World(); ship = Vessel();

        // Native position and velocity clocks need not agree, nor need parents
        // share their child's clock. Sampling must preserve every cache exactly.
        var sampled = Synthetic(ship); destination = Target();
        var states = new Dictionary<BodyOrbit, double[]>();
        int clockIndex = 0;
        foreach (var body in CrewSim.system.aBOs.Values) {
            body.UpdateTime(epoch - 10 - clockIndex++ * 3);
            body.dVelCalcLast -= .5;
            states.Add(body, new[] { body.dTimeCalcLast, body.dVelCalcLast, body.dXReal, body.dYReal, body.dVelX, body.dVelY });
        }
        // Updating a child can change its parent; capture only after all updates.
        foreach (var body in CrewSim.system.aBOs.Values)
            states[body] = new[] { body.dTimeCalcLast, body.dVelCalcLast, body.dXReal, body.dYReal, body.dVelX, body.dVelY };
        Check(AiRendezvous.Arrives(sampled, destination), "Independent body-clock fixture rejected a valid route");
        foreach (var pair in states) {
            var body = pair.Key;
            var after = new[] { body.dTimeCalcLast, body.dVelCalcLast, body.dXReal, body.dYReal, body.dVelX, body.dVelY };
            for (int i = 0; i < after.Length; i++) Check(after[i] == pair.Value[i], "Validation changed a shared position/velocity cache");
        }
    }
    static void FailureChecks() {
        World(); var ship = Vessel(10); var pilot = Controller(ship);
        pilot.Blackboard.Set("TripFuelPrediction", "old-course&123");
        var planner = new FlyToPath(pilot);
        Check(Plan(ship, 1) == null, "Weak-RCS divergence control was accepted");
        for (int i = 0; i < 6; i++) {
            var result = planner.RunCommand(pilot.Blackboard);
            Check(ReferenceEquals(ship.shipScanTarget, station) && ReferenceEquals(ship.shipSituTarget, station.objSS),
                "Rejected freight plot discarded the delivery destination");
            Check(ship.objSS.NavData == null, "Rejected plot was installed as a course");
            if (i == 4) Check(result.Status == CommandCode.Failure, "Native five-failure retry transition changed");
        }
        Check(pilot.Blackboard.Get<int>("RejectedPaths") == 1, "Native rejection counter did not advance");
        Check(!pilot.Blackboard.HasKey("TripFuelPrediction"), "Rejected plot retained an abandoned fuel prediction");
        Check(new DockCargoAndDespawn(pilot).RunCommand(pilot.Blackboard).Status == CommandCode.Failure,
            "Failed plot allowed distant cargo delivery");
        AiRendezvous.ClearRejectedTarget(ship, planner);
        Check(ship.shipScanTarget == null, "Per-invocation rejection context leaked after RunCommand");

        World(); ship = Vessel(10); pilot = Controller(ship, AIType.Scav); planner = new FlyToPath(pilot);
        planner.RunCommand(pilot.Blackboard);
        Check(ship.shipScanTarget == null, "Non-freight target failure policy changed");

        World(); ship = Vessel(); ship.RCSCount = 0; pilot = Controller(ship); planner = new FlyToPath(pilot);
        planner.RunCommand(pilot.Blackboard);
        Check(ship.shipScanTarget == null, "Native non-geometric failure was mistaken for a rejected rendezvous");
    }
}
