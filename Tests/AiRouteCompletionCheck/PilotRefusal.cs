using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using Ostranauts.ShipGUIs.Utilities;

static partial class Program {
    static bool pilotAvailable;
    static bool PilotAvailable(ref bool __result) { __result = pilotAvailable; return false; }
    static void PilotRefusal(Harmony boundary, Harmony production) {
        boundary.Patch(AccessTools.PropertyGetter(typeof(Ship), nameof(Ship.NavAIManned)), prefix: new HarmonyMethod(typeof(Program), nameof(PilotAvailable)));
        boundary.Patch(AccessTools.Method(typeof(FlyToPath), "TorchOff"), prefix: new HarmonyMethod(typeof(Program), nameof(Skip)));
        CrewSim.coPlayer.ship = null; manuallyPiloted = null; pilotAvailable = false;
        var ship = Vessel("stunned-pilot");
        var pilot = Empty<CivilianPilot>(); Back(pilot, "ShipUs", ship);
        var path = new FlyToPath(pilot);
        ship.objSS.vAccRCS = new UnityEngine.Vector2(-3.814619e-11f, -3.994418e-11f);
        ship.objSS.vVelX = -4.46711702973617e-7; ship.objSS.vVelY = -4.66712630607774e-7;
        var result = path.RunCommand(new Blackboard());
        Check(result.Status == CommandCode.Finished && ship.objSS.vAccRCS.x != 0, "Native no-pilot stale-acceleration/false-success defect not reproduced");
        production.CreateClassProcessor(typeof(Ostrabus.OstrabusPlugin).Assembly.GetType("Ostrabus.ShipNavigation.FlightPilotAvailabilityPatch", true)).Patch();
        result = path.RunCommand(new Blackboard());
        Check(result.Status == CommandCode.Running && ship.objSS.vAccRCS.x == 0 && ship.objSS.vAccRCS.y == 0, "Incapacitated pilot flight must stop RCS and wait");
        Check(ship.objSS.vVelX == -4.46711702973617e-7 && ship.objSS.vVelY == -4.66712630607774e-7, "Pilot refusal changed momentum");
        Check(ReferenceEquals(result, path.RunCommand(new Blackboard())), "Pilot refusal allocated a status on every poll");
        var nav = new NavData(ship); ship.objSS.NavData = nav;
        ship.objSS.vAccRCS = new UnityEngine.Vector2(1e-11f, 2e-11f);
        Check(path.RunCommand(new Blackboard()).Status == CommandCode.Running && ReferenceEquals(ship.objSS.NavData, nav) && ship.objSS.vAccRCS.x != 0,
            "Existing unattended course was interrupted");
        ship.objSS.NavData = null;
        manuallyPiloted = ship;
        Check(path.RunCommand(new Blackboard()).Status == CommandCode.Finished && ship.objSS.vAccRCS.x != 0, "Player takeover controls changed");
        manuallyPiloted = null; CrewSim.coPlayer.ship = ship;
        Check(path.RunCommand(new Blackboard()).Status == CommandCode.Finished && ship.objSS.vAccRCS.x != 0, "Current player controls changed");
        CrewSim.coPlayer.ship = null; ship.IsAIShip = false;
        Check(path.RunCommand(new Blackboard()).Status == CommandCode.Finished && ship.objSS.vAccRCS.x != 0, "Non-AI control changed");
        pilotAvailable = true;
    }
}
