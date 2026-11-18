using System;
using HarmonyLib;
using Ostrabus.MiscBugFixes;
using Ostrabus.ShipNavigation;
using Ostranauts.Core;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using Ostranauts.ShipGUIs.Utilities;
using Ostranauts.ShipGUIs.NavStation;

static partial class LifecycleChecks {
    static void AutopilotTargetChecks(StarSystem world, Harmony boundaries) {
        boundaries.CreateClassProcessor(typeof(ShipDestroyedTargetsPatch)).Patch();
        boundaries.CreateClassProcessor(typeof(ShipRestoredTargetsPatch)).Patch();
        Patch(boundaries, typeof(GUIOrbitDraw), "TriggerShipRedraw", nameof(Quiet));
        double before = StarSystem.fEpoch;
        StarSystem.fEpoch = 1500;
        try {
            PlottedTargetIdentityChecks(world);
            foreach (bool patched in new[] { false, true }) {
                var fix = new Harmony("offline.player-course-target." + patched);
                try {
                    if (patched) fix.CreateClassProcessor(typeof(PlayerCourseTargetPatch)).Patch();
                    foreach (string scenario in new[] { "far", "close", "hidden", "restored", "coordinate", "offset", "npc", "actual-destination", "pause" }) {
                        string suffix = patched + "-" + scenario;
                        var goal = LifetimeShip(world, "course-goal-" + suffix);
                        goal.objSS.vPosx = 50;
                        var ship = LifetimeShip(world, "course-owner-" + suffix);
                        new TargetData(goal).ApplyToShip(ship);
                        if (scenario == "offset") ship.shipSituTarget = new ShipSitu(goal.objSS, false) { vBOOffsetx = 1e-7 };
                        var originalTarget = ship.shipSituTarget;
                        var route = new NavData(ship);
                        route.AddNavDataPoint(new NavDataPoint(1000, new ShipSitu(false), 100, 0, 200, rcsCount: 0F));
                        route.AddNavDataPoint(new NavDataPoint(2000, new ShipSitu(false) { vPosx = 50 }, 100, 0, 200, rcsCount: 0F));
                        ship.objSS.NavData = route;
                        var contact = LifetimeShip(world, "course-inspected-" + suffix);
                        contact.objSS.vPosx = scenario == "close" ? ship.objSS.vPosx : 20;
                        ship.shipScanTarget = contact;
                        if (scenario == "coordinate") {
                            ship.targetRegID = null;
                            ship.TargetData = null;
                            ship.shipSituTarget = new ShipSitu(false) { vPosx = 50 };
                        }
                        if (scenario == "hidden") { contact.HideFromSystem = true; ship.shipScanTarget = null; ship.shipSituTarget = contact.objSS; }
                        if (scenario == "restored") {
                            ship.json = new JsonShip { strTargetRegID = goal.strRegID, strScanTargetID = contact.strRegID,
                                objSituScanTarget = contact.objSS.GetJSON() };
                            ship.CheckTargets();
                            var restoredRoute = new NavData(route.GetJSON());
                            ship.objSS.NavData = route = restoredRoute;
                        }
                        if (scenario == "npc") ship.IsAIShip = true;
                        if (scenario == "pause") ship.fAIPauseTimer = StarSystem.fEpoch + 60;
                        var pilot = Empty<AutoPilot>();
                        Field(pilot, "<ShipUs>k__BackingField", ship);
                        var command = new FlyToAutoPilot(pilot);
                        if (patched && scenario != "npc") {
                            Check(ReferenceEquals(pilot.FFWD(command, new Blackboard()), command) && ReferenceEquals(ship.objSS.NavData, route),
                                "Native time-skip dispatch resolved an active course instantly");
                        }
                        var status = command.RunCommand(new Blackboard());
                        if (scenario == "pause") {
                            Check(status.Status == CommandCode.Failure && ship.objSS.NavData == null, "Native pause abort was bypassed");
                            continue;
                        }
                        bool protects = patched && scenario != "npc";
                        if (protects) {
                            Check(status.Status == CommandCode.Running && ReferenceEquals(ship.objSS.NavData, route), "Selected contact interrupted course: " + scenario);
                            Check(ship.shipScanTarget == (scenario == "hidden" ? null : contact), "Course correction changed sensor selection");
                            if (scenario != "coordinate" && scenario != "offset") Check(ReferenceEquals(ship.shipSituTarget, goal.objSS), "Destination situation was not rebound: " + scenario);
                            if (scenario == "offset") Check(ReferenceEquals(ship.shipSituTarget, originalTarget) && ship.shipSituTarget.vBOOffsetx == 1e-7, "Independent goal offset lost");
                        } else if (scenario == "far" || scenario == "npc") {
                            Check(ReferenceEquals(ship.shipSituTarget, contact.objSS), "Native scan-retarget control did not reproduce");
                        } else if (scenario == "close") {
                            Check(ship.objSS.NavData == null, "Native premature completion control did not reproduce");
                        }
                        if (scenario == "actual-destination") {
                            goal.Destroy();
                            Check(ship.objSS.NavData == null && ship.targetRegID == null, "Removed actual destination retained course");
                        } else {
                            contact.Destroy();
                            if (protects) {
                                Check(ReferenceEquals(ship.objSS.NavData, route), "Contact removal cancelled unrelated course: " + scenario);
                                Check(world.GetShipByRegID(goal.strRegID) == goal, "Actual goal was removed");
                                Check(ship.objSS.vPosx == 1 && ship.objSS.vVelX == 3e-9 && ship.fShallowRCSRemass == 100 && ship.fShallowFusionRemain == 200, "Correction changed motion/fuel");
                                Check(command.RunCommand(new Blackboard()).Status == CommandCode.Running, "Course did not continue after contact removal");
                            } else if (scenario == "far") Check(ReferenceEquals(ship.objSS.NavData, route), "Lifetime repair did not recover legacy retargeted state");
                        }
                    }
                } finally { fix.UnpatchSelf(); }
            }
        } finally { StarSystem.fEpoch = before; }
    }

    static void PlottedTargetIdentityChecks(StarSystem world) {
        var ship = LifetimeShip(world, "plot-identity-owner");
        var destination = LifetimeShip(world, "plot-identity-destination");
        var console = Empty<CondOwner>(); console.ship = ship;
        var module = Empty<NavModCoursePlot>(); Field(module, "COSelf", console);
        var route = LifetimeRoute(ship);
        var completion = AccessTools.Method(typeof(PlayerCourseCoordinateTargetPatch), "Postfix");
        var previous = GUIOrbitDraw.CrossHairTarget;
        try {
            GUIOrbitDraw.CrossHairTarget = new NavPOI(40, 50);
            ship.targetRegID = destination.strRegID;
            completion.Invoke(null, new object[] { module, route });
            Check(ship.targetRegID == null, "Coordinate plot retained previous destination ID");
            ship.targetRegID = destination.strRegID;
            completion.Invoke(null, new object[] { module, null });
            Check(ship.targetRegID == destination.strRegID, "Failed plot changed target identity");
            var body = Empty<BodyOrbit>(); body.strName = "natural-or-manufactured-reference";
            GUIOrbitDraw.CrossHairTarget.bodyOrbit = body;
            completion.Invoke(null, new object[] { module, route });
            Check(ship.targetRegID == body.strName, "Body reference was confused with an unlabelled coordinate");
            GUIOrbitDraw.CrossHairTarget.bodyOrbit = null;
            var stellar = new StellarFixture { strID = "stellar-plot-target" };
            GUIOrbitDraw.CrossHairTarget.stellarObj = stellar;
            completion.Invoke(null, new object[] { module, route });
            Check(ship.targetRegID == stellar.strID, "Stellar destination identity lost");
            Field(GUIOrbitDraw.CrossHairTarget, "_ship", destination);
            completion.Invoke(null, new object[] { module, route });
            Check(ship.targetRegID == destination.strRegID, "Hull destination identity lost");
        } finally { GUIOrbitDraw.CrossHairTarget = previous; }
    }
}
