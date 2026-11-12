using System;
using System.Collections.Generic;
using HarmonyLib;
using Ostrabus.MiscBugFixes;
using Ostranauts.Core;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;

static partial class Program {
    static bool NoCompany(ref JsonCompany __result) { __result = null; return false; }
    static Ship SavedActive(string id, double age = 12000) {
        var ship = Hull(id);
        ship.IsAIShip = true;
        Person(ship, id + " pilot");
        CrewSim.system.dictShips[id] = ship;
        Write(ship.ShipCO, "IsStale", StarSystem.fEpoch - age);
        return ship;
    }
    static void RepairChecks() {
        AIShipManager.dictAIs = new Dictionary<string,List<AIShip>>();
        SetManagerQueue();
        CrewSim.system.dictShips.Clear();
        StarSystem.fEpoch = 300000;
        var h = new Harmony("offline.stale-ship.production");
        try {
            Patch(h,typeof(CondOwner),"get_Company",nameof(NoCompany));
            h.CreateClassProcessor(typeof(CrewedAIRegistrationPatch)).Patch();
            h.CreateClassProcessor(typeof(StaleShipMarkPatch)).Patch();
            var ship = SavedActive("RESTORED");
            AIShipManager.AddAIToDict(Controller(ship), "REGION");
            Check(Amount(ship.ShipCO,"IsStale") == 0, "Actual registration hook cancels an expired saved retirement marker");
            StarSystem.fEpoch += 7200;
            Sweep();
            Check(!ship.bRemove, "Restored active ship survives two-hour SFF crossing the old deadline");

            AIShipManager.UnregisterShip(ship);
            Check(Amount(ship.ShipCO,"IsStale") == StarSystem.fEpoch, "Patched ship still gets a fresh native retirement timestamp");
            ship.HideFromSystem = true;
            StarSystem.fEpoch += 10800;
            Sweep();
            Check(ship.bRemove, "Genuinely retired hidden hull still reaches native stale removal");

            var reserve = SavedActive("REUSED");
            reserve.HideFromSystem = true;
            AIShip.ResetShipState(reserve);
            var reserveAI = Controller(reserve);
            AIShipManager.AddAIToDict(reserveAI, "REGION");
            Check(Amount(reserve.ShipCO,"IsStale") == 0, "Native reserve reset plus actual registration cancels old disposal");
            // A new deliberate command-failure mark after registration is not old
            // retirement: preserve native cleanup rather than protecting all AIs.
            Write(reserve.ShipCO,"IsStale",StarSystem.fEpoch - 10800);
            AIShipManager.AddAIToDict(reserveAI,"REGION");
            Check(Amount(reserve.ShipCO,"IsStale") > 0, "Re-enqueuing the same active controller preserves a later failure marker");
            Sweep();
            Check(reserve.bRemove, "New deliberate failure cleanup remains effective for a registered ship");

            var hidden = SavedActive("HIDDEN");
            hidden.HideFromSystem = true;
            AIShipManager.AddAIToDict(Controller(hidden), "REGION");
            Check(Amount(hidden.ShipCO,"IsStale") > 0, "Hidden reserve registration does not cancel retirement");
            var nonAI = SavedActive("NOT-AI");
            nonAI.IsAIShip = false;
            AIShipManager.AddAIToDict(Controller(nonAI),"REGION");
            Check(Amount(nonAI.ShipCO,"IsStale") > 0, "Non-AI hull registration does not clear disposal state");
            var unregistered = SavedActive("UNREGISTERED");
            ActiveShipRetirement.Reactivated(Controller(unregistered));
            Check(Amount(unregistered.ShipCO,"IsStale") > 0, "An unregistered controller cannot cancel a retirement timer");
            var condemned = SavedActive("ALREADY-QUEUED");
            condemned.bRemove = true;
            AIShipManager.AddAIToDict(Controller(condemned), "REGION");
            Check(condemned.bRemove && Amount(condemned.ShipCO,"IsStale") > 0, "Pre-existing removal flags and timers are never revived");

            var pending = SavedActive("WORKER-READ-OLD-MARKER");
            onRead = (co, name) => {
                if (!ReferenceEquals(co,pending.ShipCO) || name != "IsStale") return;
                onRead = null;
                AIShipManager.AddAIToDict(Controller(pending), "REGION");
            };
            int before = messages.Count;
            Sweep();
            Check(Amount(pending.ShipCO,"IsStale") == 0 && !pending.bRemove,
                "Worker that read the old deadline cannot mark a subsequently reactivated ship");
            Check(!messages.GetRange(before,messages.Count-before).Exists(m => m.Contains(pending.strRegID)),
                "Cancelled worker decision does not emit a false stale-removal log");

            var refreshed = SavedActive("REFRESHED-TIMER");
            onRead = (co, name) => {
                if (!ReferenceEquals(co,refreshed.ShipCO) || name != "IsStale") return;
                onRead = null;
                Write(co,"IsStale",StarSystem.fEpoch);
            };
            Sweep();
            Check(!refreshed.bRemove, "A refreshed retirement timestamp is rechecked at the worker write boundary");

            var attached = SavedActive("ATTACHED");
            AIShipManager.AddAIToDict(Controller(attached),"REGION");
            ((Dictionary<string,Ship>)AccessTools.Field(typeof(Ship),"aDocked").GetValue(attached))["port"] = pending;
            Sweep();
            ((Dictionary<string,Ship>)AccessTools.Field(typeof(Ship),"aDocked").GetValue(attached)).Clear();
            StarSystem.fEpoch += 14400;
            Sweep();
            Check(!attached.bRemove && Amount(attached.ShipCO,"IsStale") == 0,
                "Docking and later separating do not resurrect a cancelled timer");

            foreach (var type in new[] { typeof(PolicePilot), typeof(HaulerRetrievingPilot), typeof(HaulerDeployingPilot), typeof(HaulerCargoPilot), typeof(CivilianPilot), typeof(NavyPilot), typeof(PiratePilot) }) {
                var roleShip = SavedActive(type.Name);
                var pilot = (BaseCharacter)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(type);
                Back(pilot,"ShipUs",roleShip);
                pilot.Blackboard = new Blackboard();
                var ai = Empty<AIShip>();
                Set(ai,"_shipUs",roleShip); Set(ai,"_aiCharacter",pilot); Back(ai,"Blackboard",pilot.Blackboard);
                AIShipManager.AddAIToDict(ai,"REGION");
                Check(Amount(roleShip.ShipCO,"IsStale") == 0, "Registration cancels retirement for " + type.Name);
            }

            var empty = Hull("CREWLESS");
            empty.IsAIShip = true;
            CrewSim.system.dictShips[empty.strRegID] = empty;
            Write(empty.ShipCO,"IsStale",StarSystem.fEpoch-12000);
            AIShipManager.AddAIToDict(Controller(empty),"REGION");
            Check(AIShipManager.GetAIShipByRegID(empty.strRegID) == null && Amount(empty.ShipCO,"IsStale") > 0,
                "Crewless registration keeps the existing crew guard and is not treated as reactivation");
        } finally {
            onRead = null;
            h.UnpatchSelf();
        }
    }
}
