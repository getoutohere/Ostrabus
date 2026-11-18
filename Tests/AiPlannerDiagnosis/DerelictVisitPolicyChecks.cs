using System;
using HarmonyLib;
using Ostrabus.MiscBugFixes;
using Ostrabus.ShipNavigation;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using Ostranauts.ShipGUIs.Utilities;

static partial class TrafficChecks {
    static void DerelictVisitPolicy(CivilianPilot civilian, Ship visitor, Ship target) {
        int before = checks;
        var production = new Harmony("offline.civilian-derelict-policy");
        var candidate = AccessTools.Method(typeof(CivilianPilot), "CanDockWith");
        var scavCandidate = AccessTools.Method(typeof(ScavPilot), "ScavCanDockWith");
        bool Eligible() => (bool)candidate.Invoke(civilian, new object[] { visitor, target });
        var scav = new ScavPilot(home.strRegID, visitor);
        bool ScavEligible() => (bool)scavCandidate.Invoke(scav, new object[] { visitor, target });
        try {
            target.Classification = Ship.TypeClassification.Default;
            target.HideFromSystem = false;
            target.DMGStatus = Ship.Damage.Derelict;
            Check(Eligible() && civilian.GetTarget().Ship == target,
                "Native civilian selector no longer reproduces random derelict visits");
            bool scavBefore = ScavEligible();
            Check(scavBefore, "Scavenger native fixture cannot select a derelict");
            foreach (bool hiddenGuard in new[] { false, true }) {
                production.CreateClassProcessor(typeof(CivilianDerelictVisitsPatch)).Patch();
                if (hiddenGuard) production.CreateClassProcessor(typeof(CivilianVisibleVisitPatch)).Patch();
                foreach (Ship.Damage status in Enum.GetValues(typeof(Ship.Damage))) {
                    target.DMGStatus = status;
                    Check(Eligible() == (status != Ship.Damage.Derelict), "Civilian candidate damage scope changed: " + status);
                }
                target.DMGStatus = Ship.Damage.Derelict;
                Check(civilian.GetTarget().Ship.IsStation(), "No eligible ship candidate failed to use native station fallback");
                Check(ScavEligible() == scavBefore, "Civilian policy changed scavenger salvage eligibility");
                new TargetData(target).ApplyToShip(visitor);
                var route = new NavData(visitor); visitor.objSS.NavData = route;
                civilian.Blackboard.Set("TripFuelPrediction", "existing-visit&12");
                Check(new TargetNewIfNone(civilian).RunCommand(civilian.Blackboard).Status == CommandCode.Finished &&
                    visitor.shipScanTarget == target && ReferenceEquals(visitor.objSS.NavData,route) &&
                    civilian.Blackboard.Get<string>("TripFuelPrediction") == "existing-visit&12",
                    "New selection policy interrupted an already assigned visit");
                visitor.ClearShipTarget(); visitor.objSS.NavData = null;
                target.DMGStatus = Ship.Damage.Used;
                Check(civilian.GetTarget().Ship == target, "Ordinary visible ship visits were removed");
                // An asteroid's native no-port allowance remains independent
                // of its damage value. The asteroid-field branch is untouched.
                target.Classification = Ship.TypeClassification.Asteroid;
                target.DMGStatus = Ship.Damage.Derelict;
                Check(Eligible(), "Civilian asteroid prospecting was classified as salvage");
                target.Classification = Ship.TypeClassification.Default;
                var cargoShip = Hull("explicit-derelict-control");
                cargoShip.shipScanTarget = target;
                var cargo = new HaulerCargoPilot(home.strRegID, cargoShip, "unused-fixture-loot");
                Check(cargo.GetTarget().Ship == target, "Explicit freight/PASS destination was changed");
                CrewSim.system.dictShips.Remove(cargoShip.strRegID);
                if (hiddenGuard) {
                    target.DMGStatus = Ship.Damage.Used; target.HideFromSystem = true;
                    Check(!Eligible(), "Civilian policy bypassed hidden-hull rejection");
                    target.HideFromSystem = false;
                }
                production.UnpatchSelf();
            }
            Console.WriteLine((checks-before) + " civilian derelict-policy checks passed: native selection A/B, station fallback, ordinary visits, asteroid and scavenger eligibility, explicit targets and hidden-visit composition.");
        } finally { production.UnpatchSelf(); target.DMGStatus = Ship.Damage.Used; target.Classification = Ship.TypeClassification.Default; }
    }
}
