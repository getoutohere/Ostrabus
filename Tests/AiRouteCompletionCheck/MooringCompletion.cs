using System;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;

static partial class Program {
    static bool realLockedMotion;
    static bool RefreshBoundary() => realLockedMotion;
    static bool AdvanceBoundary(ShipSitu __instance) => realLockedMotion;
    static bool ValidLock(ref bool __result) { __result = true; return false; }
    static bool MoorBoundary(Dock __instance, ref CommandStatus __result) {
        __result = new CommandStatus(CommandCode.Finished, __instance); return false;
    }
    static void MooringCompletion(Harmony boundary) {
        boundary.Patch(AccessTools.Method(typeof(Dock), "HasValidTargetLock"), prefix: new HarmonyMethod(typeof(Program), nameof(ValidLock)));
        boundary.Patch(AccessTools.Method(typeof(Dock), "AttemptToMoor"), prefix: new HarmonyMethod(typeof(Program), nameof(MoorBoundary)));
        var oldBody = mercury;
        try {
            realLockedMotion = true;
            mercury = new BodyOrbit(new JsonBodyOrbitSave { strName = "Mercury", fAxis1 = 5.5, fAxis2 = 5.5, fPerh = 2.75,
                fAph = 2.75, fPeriod = 145161856, nOrbitDirection = 1 });
            foreach (double skip in new[] { 60d, 7200d, 21600d }) {
                StarSystem.fEpoch = 1000000;
                mercury.UpdateTime(StarSystem.fEpoch);
                var target = new ShipSitu(false) { bBOLocked = true, strBOPORShip = "Mercury", vBOOffsetx = .00001, Size = 1000 };
                target.UpdateTime(StarSystem.fEpoch);
                var ship = Vessel("ai"); ship.objSS.Size = 500;
                ship.shipSituTarget = target; ship.targetRegID = "asteroid";
                var pilot = Empty<CivilianPilot>(); Back(pilot, "ShipUs", ship);
                var dock = new Dock(pilot);
                var endpoint = new ShipSitu(target, false);
                double arrival = StarSystem.fEpoch + 1;
                StarSystem.fEpoch += skip;
                mercury.UpdateTime(StarSystem.fEpoch);
                double x = mercury.dXReal + target.vBOOffsetx, y = mercury.dYReal + target.vBOOffsety;
                dock.ResolveInstantly(new Blackboard());
                double staleError = DistanceKm(ship.objSS.vPosx - x, ship.objSS.vPosy - y);
                Check(staleError > skip * 10, "Native stale-target mooring did not reproduce skip-sized separation");
                // Native completion, with production target preservation, must refresh
                // the destination before the actual native instant-moor placement.
                var route = Route(ship, endpoint, arrival);
                route.TimeAdvance(ship.objSS);
                Check(ReferenceEquals(ship.shipSituTarget, target), "Mooring changed destination identity");
                dock.ResolveInstantly(new Blackboard());
                double error = DistanceKm(ship.objSS.vPosx - x, ship.objSS.vPosy - y);
                double placementRadius = (ship.objSS.GetRadiusAU() + target.GetRadiusAU() + 6.684587e-11f) * 149597870.7;
                Check(error <= placementRadius + .001, "SFF instant mooring retained a stale target offset: " + error + " km");
                Check(target.bBOLocked && target.strBOPORShip == "Mercury", "Mooring target lost its authored body lock");
            }
        } finally { realLockedMotion = false; mercury = oldBody; }
    }
    static double DistanceKm(double x, double y) => Math.Sqrt(x * x + y * y) * 149597870.7;
}
