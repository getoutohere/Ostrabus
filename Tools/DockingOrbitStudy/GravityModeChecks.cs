using System;
using System.Reflection;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;

static partial class Program
{
    static bool SuppliedGravityStudyFuel(ref float __result)
    {
        __result = 1;
        return false;
    }

    // Investigation only. No policy hook is installed into the game/plugin.
    static void GravityModeChecks(Harmony engine)
    {
        const double metersPerAu = 149597863936d;
        int before = checks;
        var study = new Harmony("offline.gravity-mode-comparison");
        try
        {
            World();
            var body = CrewSim.system.aBOs["Phobos"];
            var ship = Hull("LOCK-DELTA");
            ship.objSS.vVelX = body.dVelX + 10 / metersPerAu;
            ship.objSS.vVelY = body.dVelY;
            double vx = ship.objSS.vVelX;
            ship.objSS.LockToBO(body);
            double removed = Math.Abs(vx - ship.objSS.vVelX) * metersPerAu;
            Check(Math.Abs(removed - 10) < 1e-8, "Body lock did not reproduce momentum replacement.");
            Console.WriteLine("Body lock erased relative velocity: " + removed + " m/s; no propulsion command was issued.");

            ship.objSS.LockToBOOrbit(body);
            double injected = Math.Sqrt(Math.Pow(ship.objSS.vVelX - body.dVelX, 2) +
                Math.Pow(ship.objSS.vVelY - body.dVelY, 2)) * metersPerAu;
            Check(injected > 0, "Circular body lock did not impose orbital velocity.");
            Console.WriteLine("Circular lock injected tangential velocity from rest: " + injected + " m/s.");

            var pilot = Empty<AutoPilot>();
            Field(pilot, "<ShipUs>k__BackingField", ship);
            Check(new FlyTo(pilot).RunCommand(new Blackboard()).Status == CommandCode.Failure,
                "Native FlyTo unexpectedly accepted a body-locked ship.");

            ship.objSS.bIgnoreGrav = true;
            var dodgeBoard = new Blackboard();
            dodgeBoard.Set("DodgeTarget", "absent-dodge-target");
            Check(new Dodge(pilot).RunCommand(dodgeBoard).Status == CommandCode.Finished && ship.objSS.bIgnoreGrav,
                "Native lost-target dodge no longer leaves its gravity exemption set.");
            Console.WriteLine("Native Dodge early target-loss success retains bIgnoreGrav; normal timed completion clears it.");
            DodgeCleanupChecks(study, ship, pilot);

            study.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.RemoveGasMass)),
                prefix: new HarmonyMethod(typeof(Program), nameof(SuppliedGravityStudyFuel)));
            World();
            body = CrewSim.system.aBOs["Phobos"];
            ship = Hull("ORBIT-RCS");
            ship.IsAIShip = true;
            ship.objSS.bOrbitLocked = true;
            ship.objSS.strBOPORShip = body.strName;
            ship.objSS.vVelX = body.dVelX;
            ship.objSS.vVelY = body.dVelY;
            Field(ship, "fRCSCount", 8f);
            Field(ship, "nRCSDistroCount", 1);
            ship.Maneuver(1, 0, 0, 0, 1);
            Check(ship.objSS.vAccRCS.x != 0, "Native maneuver did not apply supplied RCS input.");
            Check(ship.objSS.bOrbitLocked, "Native AI RCS input unexpectedly released the orbit.");
            ship.objSS.TimeAdvance(1);
            Check(ship.objSS.vVelX == body.dVelX && ship.objSS.vVelY == body.dVelY,
                "Orbit-locked playback unexpectedly integrated applied RCS acceleration.");
            ship.objSS.UnlockFromBO();
            Check(ship.objSS.bOrbitLocked, "Body-unlock unexpectedly cleared analytical orbit state.");
            ship.UnlockFromOrbit();
            Check(!ship.objSS.bOrbitLocked, "Native analytical orbit release failed.");
            ship.objSS.TimeAdvance(1);
            Check(ship.objSS.vVelX != body.dVelX, "Released ship failed to respond to the same RCS input.");
            Console.WriteLine("Native RCS applies acceleration but leaves analytical orbit locked; explicit orbit release restores response.");

            // Execute the native distance calculation used by Ship.LockToOrbit's
            // atmosphere gate, comparing translated copies of the same geometry.
            double translatedWrong = MathUtils.GetDistance(1, 1, 1 + 1e-7, 2);
            double translatedRight = MathUtils.GetDistance(1, 2, 1 + 1e-7, 2);
            double origin = MathUtils.GetDistance(0, 0, 1e-7, 0);
            Check(origin < 1e-5 && translatedRight < 1e-5 && translatedWrong > 1e-5,
                "Native orbit-entry X/X atmosphere-gate counterexample did not reproduce.");
            Console.WriteLine("Orbit-entry atmosphere gate: X/X yields " + translatedWrong +
                " AU; actual X/Y separation " + translatedRight + " AU for the same nearby point.");

            var plugin = typeof(Ostrabus.OstrabusPlugin).Assembly;
            var update = AccessTools.Method(typeof(BodyOrbit), nameof(BodyOrbit.UpdateTime));
            engine.Unpatch(update, HarmonyPatchType.Prefix, engine.Id);
            study.CreateClassProcessor(plugin.GetType("Ostrabus.Orbital.OrbitalVelocityPatch", true)).Patch();
            World();
            body = CrewSim.system.aBOs["Phobos"];
            body.dXReal = body.dYReal = body.dVelX = body.dVelY = 0;
            ship = Hull("FIT-OWNER");
            const double radius = 1e-4;
            ship.objSS.vPosx = radius;
            ship.objSS.vPosy = ship.objSS.vVelX = 0;
            double circularSpeed = Math.Sqrt(6.674080038626684E-11 * body.fMass / (radius * metersPerAu));
            ship.objSS.vVelY = circularSpeed / metersPerAu;
            var fit = AccessTools.Method(plugin.GetType("Ostrabus.Orbital.CoastOrbitFit", true), "Fit");
            var orbit = (BodyOrbit)fit.Invoke(null, new object[] { ship, body });
            Check(orbit != null, "Production conic fit rejected the circular bound input.");
            Check(Math.Abs(orbit.dVelY - ship.objSS.vVelY) * metersPerAu < .005,
                "Production conic fit changed circular velocity beyond its tolerance.");
            ship.objSS.vVelY = 0;
            Check(fit.Invoke(null, new object[] { ship, body }) == null,
                "Stationary radial fall was silently converted into an invented safe orbit.");
            ship.objSS.vVelY = 2 * circularSpeed / metersPerAu;
            Check(fit.Invoke(null, new object[] { ship, body }) == null,
                "Escaping trajectory was silently converted into a bound orbit.");
            Console.WriteLine("Production conic fit preserves a bound orbit and refuses radial-rest and escaping inputs.");
            study.CreateClassProcessor(plugin.GetType("Ostrabus.Orbital.OrbitalGravityPatch", true)).Patch();
            ExistingWaitAdmission(study);
            WaitTrajectoryChecks(study);
            Console.WriteLine("Gravity mode study: " + (checks - before) + " native/production checks passed.");
        }
        finally
        {
            study.UnpatchSelf();
            engine.Patch(AccessTools.Method(typeof(BodyOrbit), nameof(BodyOrbit.UpdateTime)),
                prefix: new HarmonyMethod(typeof(Program), nameof(Skip)));
        }
    }

    static void ExistingWaitAdmission(Harmony study)
    {
        // A constructed airless point-mass world, not a recorded live incident.
        // This isolates the existing policy's admission from atmosphere and groups.
        const double kmPerAu = 149597872d;
        World();
        var body = CrewSim.system.aBOs["Phobos"];
        body.dXReal = body.dYReal = body.dVelX = body.dVelY = 0;
        body.fMass = 3.3e23;
        body.fRadius = 2439.7 / kmPerAu;
        CrewSim.system.boStar = body;
        Field(CrewSim.system, "fGravAccelConstant", 2e-44f);
        nearestOverride = body;
        var ship = Hull("FREE-WAIT");
        ship.IsAIShip = true;
        ship.objSS.vPosx = body.fRadius + 300 / kmPerAu;
        ship.objSS.vPosy = ship.objSS.vVelX = ship.objSS.vVelY = 0;
        ship.shipSituTarget = new ShipSitu(false);
        ship.fAIDockingExpire = double.NegativeInfinity;
        var pilot = Empty<AutoPilot>();
        Field(pilot, "<ShipUs>k__BackingField", ship);
        var hold = new Hold(pilot);
        var board = new Blackboard();
        Check(hold.CanExecute(board), "Native Hold rejected the free zero-relative-speed control.");
        ship.fAIDockingExpire = 600;
        var plugin = typeof(Ostrabus.OstrabusPlugin).Assembly;
        var enabled = AccessTools.Property(plugin.GetType("Ostrabus.Orbital.OrbitalSimulation", true), "Enabled");
        bool previous = (bool)enabled.GetValue(null);
        try
        {
            enabled.SetValue(null, true);
            // Contrast: the former policy admitted every atmosphere-free state.
            Check(hold.RunCommand(board).Status == CommandCode.Running && ship.objSS.bIgnoreGrav,
                "Native free Hold did not retain its gravity exemption.");
            double start = ship.objSS.vPosx;
            ship.objSS.bIgnoreGrav = false;
            var gravity = AccessTools.Method(typeof(StarSystem), "GetGreatestGravBOFast");
            double elapsed = 0;
            while (elapsed < 600 && ship.objSS.vPosx > body.fRadius)
            {
                var parameters = new object[] { ship.objSS, UnityEngine.Vector2.zero, null };
                gravity.Invoke(CrewSim.system, parameters);
                ship.objSS.vAccEx = (UnityEngine.Vector2)parameters[1];
                ship.objSS.TimeAdvance(.5);
                elapsed += .5;
                StarSystem.fEpoch += .5;
            }
            Check(ship.objSS.vPosx <= body.fRadius, "Former stationary admission did not fall into the controlled body.");
            Console.WriteLine("Former orbital-waiting admission: free AI at 300 km with zero relative velocity falls through the surface after " +
                elapsed + " s using native gravity and TimeAdvance at 0.5 s steps.");
            ship.objSS.vPosx = start;
            ship.objSS.vVelX = ship.objSS.vVelY = 0;
            ship.objSS.vAccEx = UnityEngine.Vector2.zero;
            ship.fAIDockingExpire = 600;
            study.CreateClassProcessor(plugin.GetType("Ostrabus.ShipNavigation.OrbitalHoldWaitingPatch", true)).Patch();
            Check(new Hold(pilot).RunCommand(board).Status == CommandCode.Running && ship.objSS.bIgnoreGrav,
                "Patched policy restored gravity to radial rest.");
            ship.objSS.TimeAdvance(600);
            Check(ship.objSS.vPosx == start && ship.objSS.vVelX == 0,
                "Refused radial rest changed position or velocity during the native wait.");
        }
        finally { enabled.SetValue(null, previous); nearestOverride = null; }
    }

    static void DodgeCleanupChecks(Harmony study, Ship ship, AutoPilot pilot)
    {
        var type = typeof(Ostrabus.OstrabusPlugin).Assembly.GetType("Ostrabus.ShipNavigation.DodgeCompletionPatch", true);
        study.CreateClassProcessor(type).Patch();
        var incoming = Hull("INCOMING");
        var board = new Blackboard();
        board.Set("DodgeTarget", incoming.strRegID);
        board.Set("DodgeDir", 1f);
        board.Set("Unrelated", "keep");
        ship.fAIPauseTimer = 0;
        ship.fAIDockingExpire = 10;
        ship.objSS.bIgnoreGrav = false;
        var dodge = new Dodge(pilot);
        Check(dodge.RunCommand(board).Status == CommandCode.Running && ship.objSS.bIgnoreGrav,
            "Cleanup changed an active timed dodge.");
        foreach (bool destroyed in new[] { false, true })
        {
            incoming.bDestroyed = destroyed;
            if (destroyed) CrewSim.system.dictShips[incoming.strRegID] = incoming;
            else CrewSim.system.dictShips.Remove(incoming.strRegID);
            board.Set("DodgeTarget", incoming.strRegID);
            board.Set("DodgeDir", 1f);
            ship.fAIDockingExpire = 7;
            ship.objSS.bIgnoreGrav = true;
            Check(dodge.RunCommand(board).Status == CommandCode.Finished && !ship.objSS.bIgnoreGrav,
                "Missing/destroyed incoming retained gravity immunity.");
            Check(double.IsNegativeInfinity(ship.fAIDockingExpire) && !board.HasKey("DodgeTarget") && !board.HasKey("DodgeDir"),
                "Lost incoming retained completed dodge timer or keys.");
            Check(board.Get<string>("Unrelated") == "keep", "Cleanup modified unrelated blackboard data.");
        }
        incoming.bDestroyed = false;
        CrewSim.system.dictShips[incoming.strRegID] = incoming;
        board.Set("DodgeTarget", incoming.strRegID);
        board.Set("DodgeDir", 1f);
        ship.fAIDockingExpire = 0;
        ship.objSS.bIgnoreGrav = true;
        Check(dodge.RunCommand(board).Status == CommandCode.Finished && !ship.objSS.bIgnoreGrav &&
            double.IsNegativeInfinity(ship.fAIDockingExpire), "Normal timed completion changed.");
        board.Set("DodgeTarget", incoming.strRegID);
        ship.fAIPauseTimer = 1;
        ship.fAIDockingExpire = 0;
        Check(dodge.RunCommand(board).Status == CommandCode.Failure && ship.fAIDockingExpire == 0,
            "Cleanup changed native dodge failure semantics.");
        Console.WriteLine("Production dodge cleanup: active, missing/destroyed target, normal completion, failure and unrelated state pass.");
    }

    static void WaitTrajectoryChecks(Harmony study)
    {
        const double kmPerAu = 149597870.7;
        World();
        var body = CrewSim.system.aBOs["Phobos"];
        body.dXReal = body.dYReal = body.dVelX = body.dVelY = 0;
        body.fMass = 3.3e23;
        body.fRadius = 2439.7 / kmPerAu;
        CrewSim.system.boStar = body;
        Field(CrewSim.system, "fGravAccelConstant", 2e-44f);
        var ship = Hull("SAFE-WAIT");
        ship.IsAIShip = true;
        double radius = body.fRadius + 300 / kmPerAu;
        double circular = Math.Sqrt((double)(float)2e-44 * body.fMass / radius);
        var pilot = Empty<AutoPilot>();
        Field(pilot, "<ShipUs>k__BackingField", ship);
        var enabled = AccessTools.Property(typeof(Ostrabus.OstrabusPlugin).Assembly.GetType("Ostrabus.Orbital.OrbitalSimulation", true), "Enabled");
        bool previous = (bool)enabled.GetValue(null);
        try
        {
            enabled.SetValue(null, true);
            foreach (var scenario in new[] { "circular", "retrograde", "eccentric-clear", "radial", "stationary", "impact", "escape", "atmospheric-pericenter", "hull-clearance", "invalid", "unknown" })
            {
                body.aAtmospheres = scenario == "atmospheric-pericenter" ? new[] { new JsonAtmosphere { fMaxAltitude = 2700 } } : null;
                body.fRadius = 2439.7 / kmPerAu;
                if (scenario == "hull-clearance") body.fRadius = radius - .5 / kmPerAu;
                ship.objSS.vPosx = radius;
                ship.objSS.vPosy = ship.objSS.vVelX = 0;
                ship.objSS.vVelY = scenario == "retrograde" ? -circular : circular;
                if (scenario == "eccentric-clear" || scenario == "atmospheric-pericenter") ship.objSS.vVelY = circular * .99;
                if (scenario == "impact") ship.objSS.vVelY = circular * .5;
                if (scenario == "escape") ship.objSS.vVelY = circular * 2;
                if (scenario == "stationary" || scenario == "radial") ship.objSS.vVelY = 0;
                if (scenario == "radial") ship.objSS.vVelX = circular * .5;
                if (scenario == "invalid") Field(ship.objSS, "_vVelY", double.NaN); // Corrupt-save input; bypass Unity log presentation.
                if (scenario == "unknown") CrewSim.system.boStar = null;
                ship.fAIPauseTimer = 0;
                ship.fAIDockingExpire = 600;
                ship.objSS.bIgnoreGrav = false;
                var before = (ship.objSS.vPosx, ship.objSS.vPosy, ship.objSS.vVelX, ship.objSS.vVelY);
                Check(new Hold(pilot).RunCommand(new Blackboard()).Status == CommandCode.Running,
                    "Trajectory control did not remain in Hold: " + scenario);
                bool admitted = scenario == "circular" || scenario == "retrograde" || scenario == "eccentric-clear";
                Check(ship.objSS.bIgnoreGrav != admitted, "Trajectory admission changed: " + scenario);
                Check(before.Equals((ship.objSS.vPosx, ship.objSS.vPosy, ship.objSS.vVelX, ship.objSS.vVelY)) &&
                    ship.objSS.NavData == null && !ship.objSS.bBOLocked && !ship.objSS.bOrbitLocked,
                    "Admission altered motion or created a lock: " + scenario);
                CrewSim.system.boStar = body;
            }
            body.aAtmospheres = null;
            body.fRadius = 2439.7 / kmPerAu;
            ship.objSS.vPosx = radius;
            ship.objSS.vPosy = ship.objSS.vVelX = 0;
            ship.objSS.vVelY = circular;
            ship.fAIDockingExpire = 600;
            var wait = new Hold(pilot);
            var board = new Blackboard();
            wait.RunCommand(board);
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) wait.RunCommand(board);
            Check(GC.GetAllocatedBytesForCurrentThread() == allocatedBefore,
                "Unchanged actual Hold admissions allocated managed memory.");
            var gravity = AccessTools.Method(typeof(StarSystem), "GetGreatestGravBOFast");
            var args = new object[] { ship.objSS, UnityEngine.Vector2.zero, null };
            for (int i = 0; i < 1200; i++)
            {
                gravity.Invoke(CrewSim.system, args);
                ship.objSS.vAccEx = (UnityEngine.Vector2)args[1];
                ship.objSS.TimeAdvance(.5);
                StarSystem.fEpoch += .5;
            }
            Check(Math.Abs(Math.Sqrt(ship.objSS.vPosx * ship.objSS.vPosx + ship.objSS.vPosy * ship.objSS.vPosy) - radius) * kmPerAu < .2,
                "Admitted circular wait failed native numerical coasting with the production gravity patch.");
            // A moving gravity source must be evaluated in its own frame, not
            // mistaken for a stationary body because the fixture was at origin.
            var star = Empty<BodyOrbit>();
            star.strName = "Sol";
            star.fMass = 1.99e30;
            star.boChildren = new System.Collections.Generic.List<BodyOrbit> { body };
            body.boParent = star;
            body.fAxis1 = body.fAxis2 = 2;
            body.fPerh = body.fAph = 1;
            body.fPeriod = 31556926;
            body.nOrbitDirection = 1;
            body.dTimeCalcLast = body.dVelCalcLast = double.NegativeInfinity;
            CrewSim.system.boStar = star;
            body.UpdateTime(StarSystem.fEpoch);
            ship.objSS.vPosx = body.dXReal + radius;
            ship.objSS.vPosy = body.dYReal;
            ship.objSS.vVelX = body.dVelX;
            ship.objSS.vVelY = body.dVelY + circular;
            ship.objSS.vAccEx = UnityEngine.Vector2.zero;
            ship.fAIDockingExpire = 600;
            Check(new Hold(pilot).RunCommand(board).Status == CommandCode.Running && !ship.objSS.bIgnoreGrav,
                "Translated, moving-body bound wait was not admitted.");
            ship.objSS.vVelY = body.dVelY;
            Check(new Hold(pilot).RunCommand(board).Status == CommandCode.Running && ship.objSS.bIgnoreGrav,
                "Moving-body radial rest was admitted by using heliocentric speed.");
            Console.WriteLine("Production free-wait checks: clear bound orbits admitted; radial/impact/escape/atmosphere/hull/unknown states retain native waits.");
        }
        finally { enabled.SetValue(null, previous); }
    }
}
