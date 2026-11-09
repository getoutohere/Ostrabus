using System;
using System.Collections.Generic;
using HarmonyLib;
using Ostranauts.Utils.Models;

static partial class Program {
    static bool NoSelectedCrew(ref CondOwner __result) { __result = null; return false; }
    static bool TextKey(string __0, ref string __result) { __result = __0; return false; }
    static void CheckPhysicalInstantDock() {
        var boundary = new Harmony("offline.physical-instant-dock.boundaries");
        try {
            // Exclude rendering/UI, accounting and wear. Native docking links,
            // kinematics and docking-group formation are the subject of the test.
            boundary.Patch(AccessTools.PropertyGetter(typeof(Ship), nameof(Ship.Mass)), prefix:new HarmonyMethod(typeof(Program), nameof(FixtureMass)));
            boundary.Patch(AccessTools.Method(typeof(CrewSim), nameof(CrewSim.GetSelectedCrew)), prefix:new HarmonyMethod(typeof(Program), nameof(NoSelectedCrew)));
            boundary.Patch(AccessTools.Method(typeof(Ship), "AddDockingFees"), prefix:new HarmonyMethod(typeof(Program), nameof(Quiet)));
            boundary.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.LogAdd)), prefix:new HarmonyMethod(typeof(Program), nameof(Quiet)));
            boundary.Patch(AccessTools.Method(typeof(Ship), "CheckAccruedWear"), prefix:new HarmonyMethod(typeof(Program), nameof(Quiet)));
            boundary.Patch(AccessTools.Method(typeof(DataHandler), nameof(DataHandler.GetString), new[] { typeof(string), typeof(bool) }), prefix:new HarmonyMethod(typeof(Program), nameof(TextKey)));
            var ship = MakeShip("PHYSICAL-AI"); var station = MakeShip("PHYSICAL-STATION");
            var person = Person(ship, "Same Pilot"); var resident = Person(station, "Same Resident");
            Set(ship, "bAIShip", true); station.objSS.bIsBO = true; station.objSS.vPosx = .1; station.objSS.vVelY = 1E-7;
            Set(ship, "aDockingPorts", new List<string> { "us-port" }); Set(station, "aDockingPorts", new List<string> { "station-port" });
            ship.objSS._dockOffsetPos = new Point(0, 0); station.objSS._dockOffsetPos = new Point(0, 0);
            CrewSim.coPlayer = Empty<CondOwner>(); CrewSim.coPlayer.ship = MakeShip("PLAYER");
            CrewSim.system = Empty<StarSystem>(); CrewSim.system.dictShips = new Dictionary<string,Ship> { [ship.strRegID] = ship, [station.strRegID] = station };
            cleanupCalls = 0;
            ship.objSS.CopyFrom(station.objSS, true);
            ship.objSS.PlaceOrbitPosition(station.objSS);
            ship.Dock(station, false, "station-port", "us-port");
            Check(ship.IsDockedWith(station) && station.IsDockedWith(ship), "Native physical docking establishes reciprocal ship links");
            Check(ship.HasDockGroup && ReferenceEquals(ship.DockGroup, station.DockGroup), "Native physical docking builds a shared docking group");
            Check(ship.objSS.vVelX == station.objSS.vVelX && ship.objSS.vVelY == station.objSS.vVelY, "Native physical docking matches station velocity");
            Check(person.bAlive && person.ship == ship && resident.bAlive && resident.ship == station && ship.GetPeople(false).Count == 1,
                "Physical docking leaves existing crew alive aboard their original hulls");
            Check(!ship.bDestroyed && !ship.HideFromSystem && CrewSim.system.dictShips[ship.strRegID] == ship && cleanupCalls == 0,
                "Physical docking does not call retirement cleanup or remove/hide the hull");
            Console.WriteLine(checks + " native physical instant-docking checks passed; scene/UI/accounting/wear boundaries substituted.");
        } finally { boundary.UnpatchSelf(); }
    }
}
