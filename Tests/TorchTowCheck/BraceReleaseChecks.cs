using System;
using System.Collections.Generic;
using HarmonyLib;
using Ostrabus.ShipNavigation.Towing;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using Ostranauts.Utils.Models;
using SolarSystem;
using UnityEngine;

static partial class Program {
    static void BraceReleaseChecks(Harmony engine) {
        foreach (bool tailReleased in new[] { false, true }) {
            var tug = Make("CHAIN-TUG", 25000); var middle = Make("CHAIN-MIDDLE", 75000); var tail = Make("CHAIN-TAIL", 50000);
            var group = Join(tug, middle);
            tail.objSS._dockGroup = group;
            tail.objSS._dockOffsetPos = new Point(4e-8, 1e-8);
            tail.objSS._dockOffsetRot = .9f;
            group.DockedShips.Add(new DockGroupMember(tail, false));
            ((Dictionary<string, Ship>)AccessTools.Field(typeof(Ship), "aDocked").GetValue(middle))["c"] = tail;
            ((Dictionary<string, Ship>)AccessTools.Field(typeof(Ship), "aDocked").GetValue(tail))["d"] = middle;
            Set(middle, "aSecuredTowBraces", new Dictionary<string, string> { ["tail-brace"] = "c" });
            CrewSim.system = Empty<StarSystem>(); CrewSim.system.dictShips = new Dictionary<string, Ship> {
                [tug.strRegID] = tug, [middle.strRegID] = middle, [tail.strRegID] = tail
            };
            Course(tug);
            Check(DockedCoursePlayback.BracesSecured(group), "Three-ship chain failed native brace checks.");
            Near(DockedCoursePlayback.InertialMass(tug), 150000, "Chain torch mass omitted an indirect tow");
            StarSystem.fEpoch = 1050;
            Step(tug, middle, group, 50); Rigid(group);
            Check(tug.objSS.NavData != null, "Secured multi-ship tow was rejected.");
            double fuel = tug.fShallowFusionRemain;
            double velocity = tug.objSS.vVelX;
            Set(tailReleased ? middle : tug, "aSecuredTowBraces", null);
            if (tailReleased) Check(tug.TowBracesSecured(), "Tail-release fixture accidentally unclamped the tug's own link.");
            Check(!DockedCoursePlayback.BracesSecured(group), "Unsecured chain link was overlooked.");
            StarSystem.fEpoch = 1060;
            Step(tug, middle, group, 10);
            Check(tug.objSS.NavData == null, "Releasing a required brace did not abort the torch course.");
            Near(tug.fShallowFusionRemain, fuel, "Aborted course still reconciled fuel");
            Near(tug.objSS.vVelX, velocity, "Brace release erased existing momentum");
            Near(group.Anchor.vAccIn.magnitude, 0, "Aborted course left torch thrust active");
            Near(group.Anchor.vAccRCS.magnitude, 0, "Aborted course left RCS thrust active");
            Rigid(group);
        }
        var environment = new Harmony("offline.torch-tow.native-controller");
        var oldControllers = AIShipManager.dictAIs;
        var lookup = AccessTools.Method(typeof(AIShipManager), nameof(AIShipManager.GetAIShipByRegID));
        engine.Unpatch(lookup, HarmonyPatchType.Prefix, engine.Id);
        try {
            environment.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.Maneuver)), prefix: new HarmonyMethod(typeof(Program), nameof(Skip)));
            var tug = Make("CONTROLLER-TUG", 25000); var tow = Make("CONTROLLER-TOW", 75000);
            var group = Join(tug, tow);
            Course(tug);
            var controller = Empty<AIShip>();
            Set(controller, "_shipUs", tug);
            Set(controller, "_aiCharacter", Empty<AutoPilot>());
            Set(controller, "_activeCommand", Empty<FlyToAutoPilot>());
            var registered = new List<AIShip> { controller };
            AIShipManager.dictAIs = new Dictionary<string, List<AIShip>> { ["INTERREGIONAL"] = registered };
            Set(tug, "aSecuredTowBraces", null);
            StarSystem.fEpoch = 1050;
            Step(tug, tow, group, 50);
            Check(registered.Count == 0 && AIShipManager.GetAIShipByRegID(tug.strRegID) == null, "Native autopilot controller survived brace abort.");
            Check(!tug.bDestroyed && !tow.bDestroyed && tug.IsDockedWith(tow), "Brace abort destroyed or undocked the connected ships.");
        } finally {
            AIShipManager.dictAIs = oldControllers;
            environment.UnpatchSelf();
            engine.Patch(lookup, prefix: new HarmonyMethod(typeof(Program), nameof(NoController)));
        }
        Console.WriteLine("Three-ship chain: indirect mass, native either-side braces and abort on either released link passed.");
    }
}
