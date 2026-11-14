using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json;
using Ostranauts.Core;
using Ostranauts.ShipGUIs.Utilities;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;

static partial class Program {
    static BodyOrbit waitingBody;
    static bool WaitingBody(ref BodyOrbit __result) { __result = waitingBody; return false; }
    static bool EmptyCandidates(ref object __result) {
        __result = Activator.CreateInstance(AccessTools.Inner(typeof(TargetNewIfNone), "TargetDataDTO"));
        return false;
    }
    static void InstallWaiting(Harmony patches, bool simulation) {
        foreach (string name in new[] { "ScavVisitRegistrationPatch", "ScavVisitFuelPatch", "ScavHomeFeasibilityPatch",
            "ScavNullHomePlanPatch", "ScavHelpLifecyclePatch", "AiHoldRunClockPatch", "AiHoldEligibilityClockPatch" })
            patches.CreateClassProcessor(typeof(Ostrabus.OstrabusPlugin).Assembly.GetType("Ostrabus.MiscBugFixes." + name, true)).Patch();
        if (simulation) foreach (string name in new[] { "ScavengerReturnFallbackPatch", "OrbitalHoldWaitingPatch", "OrbitalSelectionWaitingPatch" })
            patches.CreateClassProcessor(typeof(Ostrabus.OstrabusPlugin).Assembly.GetType("Ostrabus.ShipNavigation." + name, true)).Patch();
    }
    static (ScavPilot pilot, StickySequenceCommand main, List<ICommand> leaves) WaitingMain(Ship ship) {
        var pilot = new ScavPilot(null, ship);
        var branches = (List<ICommand>)AccessTools.Field(typeof(SelectorCommand), "_childCommands").GetValue(pilot.BehaviorTree);
        var main = (StickySequenceCommand)branches.Single(c => (string)AccessTools.Field(typeof(BaseCommand), "_nodeName").GetValue(c) == "Main");
        var leaves = (List<ICommand>)AccessTools.Field(typeof(StickySequenceCommand), "_childCommands").GetValue(main);
        Set(leaves[1], "_ai", new HelpPilot(ship)); // Search/speed boundary; Main and its native children execute.
        return (pilot, main, leaves);
    }
    static void CheckVorbHold(Harmony boundary) {
        var production = new Harmony("offline.scav-waiting.production");
        try {
            StarSystem.fEpoch = 65633393023; help = false; lowFixture = false;
            CrewSim.coPlayer.ship.objSS.vPosx = 1E-4;
            var ship = HelpShip(); ship.fShallowRCSRemass = ship.fShallowRCSRemassMax = 1873;
            ship.fAIDockingExpire = double.NegativeInfinity;
            ship.objSS.vVelX = 1000 / 149597872000.0;
            double velocity = ship.objSS.vVelX;
            var pilot = new HelpPilot(ship);
            int retired = unregisters;
            Check(new TargetNewIfNone(pilot).RunCommand(pilot.Blackboard).Status == CommandCode.Failure && unregisters > retired,
                "Native failed-selection contrast no longer unregisters.");
            ship.fAIPauseTimer = StarSystem.fEpoch - 100; ship.shipSituTarget = homeFixture.objSS;
            homeFixture.objSS.vVelX = homeFixture.objSS.vVelY = 0;
            var nativeHold = new Hold(pilot);
            Check(nativeHold.CanExecute(pilot.Blackboard), "Native historical deadline did not bypass speed eligibility.");
            nativeHold.RunCommand(pilot.Blackboard); StarSystem.fEpoch += 1;
            double deadline = ship.fAIPauseTimer;
            nativeHold.RunCommand(pilot.Blackboard);
            Check(ship.fAIPauseTimer == deadline - 1 && ship.objSS.bIgnoreGrav, "Native clock/gravity contrast changed.");

            InstallWaiting(production, false);
            ship.ClearShipTarget(); ship.fAIPauseTimer = 0; ship.fAIDockingExpire = double.NegativeInfinity;
            ship.targetRegID = "unresolved-saved-id"; // An ID alone is not a usable native goal.
            var state = WaitingMain(ship); state.main.SaveData = new[] { "1" };
            retired = unregisters; int sent = sends; double before = StarSystem.fEpoch;
            Check(state.main.RunCommand(state.pilot.Blackboard).Status == CommandCode.Running && state.main.SaveData[0] == "1",
                "Failed selection advanced Main instead of waiting at selection.");
            Check(unregisters == retired && sends == sent + 1 && lastSend == "SHIPSosStranded", "SOS did not preserve controller.");
            Check(ship.fAIPauseTimer == before + 64 && ship.shipSituTarget == null, "Retry deadline or absent goal changed.");
            for (int i = 0; i < 32; i++) {
                StarSystem.fEpoch += 1;
                Check(state.main.RunCommand(state.pilot.Blackboard).Status == CommandCode.Running && state.main.SaveData[0] == "1", "Cooldown advanced Main.");
            }
            Check(sends == sent + 1, "Cooldown repeated distress messages.");
            help = true; StarSystem.fEpoch = before + 65;
            Check(state.main.RunCommand(state.pilot.Blackboard).Status == CommandCode.Running && sends == sent + 1, "Active help did not suppress a repeat request.");
            var saved = state.pilot.BehaviorTree.GetSaveData("").ToArray();
            var timers = JsonConvert.DeserializeObject<JsonShip>(JsonConvert.SerializeObject(new {
                fAIPauseTimer = ship.fAIPauseTimer, fAIDockingExpire = ship.fAIDockingExpire }));
            var restoredShip = HelpShip(); restoredShip.fShallowRCSRemass = 1873;
            restoredShip.fAIPauseTimer = timers.fAIPauseTimer; restoredShip.fAIDockingExpire = timers.fAIDockingExpire;
            var restored = WaitingMain(restoredShip); restored.pilot.BehaviorTree.LoadSaveData("", saved);
            Check(restored.main.SaveData[0] == "1" && restored.main.RunCommand(restored.pilot.Blackboard).Status == CommandCode.Running, "Native payload/timer roundtrip lost selection wait.");
            var leaf = state.leaves[1];
            Check(ReferenceEquals(state.pilot.FFWD(leaf, state.pilot.Blackboard), leaf), "Scav SFF replaced selection.");
            Check(new OneShotWrapperCommand(leaf).RunCommand(state.pilot.Blackboard).Status == CommandCode.Finished && state.main.SaveData[0] == "1", "SFF wrapper advanced Main.");
            StarSystem.fEpoch += 6 * 3600; help = false;
            Check(state.main.RunCommand(state.pilot.Blackboard).Status == CommandCode.Running && sends == sent + 2, "Post-SFF selection did not retry.");
            Check(unregisters == retired && ship.GetRCSRemain() == 1873 && ship.objSS.vVelX == velocity, "Wait changed actor/fuel/velocity.");
            state.main.SaveData = new[] { "4" }; ship.fAIDockingExpire = 200; help = true;
            Check(state.main.RunCommand(state.pilot.Blackboard).Status == CommandCode.Finished && state.main.SaveData[0] == "0", "Legacy detached Hold did not release Main.");
            var links = (Dictionary<string, Ship>)AccessTools.Field(typeof(Ship), "aDocked").GetValue(ship);
            links["port"] = homeFixture; ship.ResetDockedCache(); state.main.SaveData = new[] { "4" }; ship.fAIDockingExpire = 200;
            Check(state.main.RunCommand(state.pilot.Blackboard).Status == CommandCode.Running && state.main.SaveData[0] == "4", "Docked visit was prematurely released.");
            links.Clear(); ship.ResetDockedCache(); help = false; ship.shipSituTarget = homeFixture.objSS;
            ship.fAIPauseTimer = StarSystem.fEpoch - 100; ship.fAIDockingExpire = double.NegativeInfinity;
            var hold = new Hold(pilot);
            Check(!hold.CanExecute(pilot.Blackboard), "Expired deadline bypassed the 25 m/s eligibility guard.");
            ship.fAIPauseTimer = StarSystem.fEpoch + 100;
            Check(hold.CanExecute(pilot.Blackboard), "Future deadline did not permit timed Hold.");
            ship.fAIDockingExpire = 10; deadline = ship.fAIPauseTimer;
            hold.RunCommand(pilot.Blackboard); StarSystem.fEpoch += 2; hold.RunCommand(pilot.Blackboard);
            Check(ship.fAIPauseTimer == deadline && ship.fAIDockingExpire == 8, "Pause and visit used the wrong clocks.");
            CheckOrbitalWaitAndReturn(boundary, production);
            CheckHelpExpiry(boundary);
            Console.WriteLine(checks + " cumulative scav wait/return/cooldown assertions passed. Native Main, targetless flight, Hold, conditions and command payload loading execute; search, plan, delivery and Unity boundaries are explicit.");
        } finally { production.UnpatchSelf(); }
    }

    static void CheckOrbitalWaitAndReturn(Harmony boundary, Harmony production) {
        var enabled = AccessTools.Property(typeof(Ostrabus.OstrabusPlugin).Assembly.GetType("Ostrabus.Orbital.OrbitalSimulation", true), "Enabled");
        enabled.SetValue(null, true);
        foreach (string name in new[] { "ScavengerReturnFallbackPatch", "OrbitalHoldWaitingPatch", "OrbitalSelectionWaitingPatch" })
            production.CreateClassProcessor(typeof(Ostrabus.OstrabusPlugin).Assembly.GetType("Ostrabus.ShipNavigation." + name, true)).Patch();
        waitingBody = Empty<BodyOrbit>(); waitingBody.fRadius = 1E-6;
        boundary.Patch(AccessTools.Method(typeof(StarSystem), nameof(StarSystem.GetNearestBO)), prefix: new HarmonyMethod(typeof(Program), nameof(WaitingBody)));
        var search = AccessTools.Method(typeof(TargetNewIfNone), "FindTarget");
        boundary.Unpatch(search, HarmonyPatchType.Prefix, boundary.Id);
        boundary.Patch(search, prefix: new HarmonyMethod(typeof(Program), nameof(EmptyCandidates)));
        CrewSim.system.dictShips = new Dictionary<string, Ship> { [homeFixture.strRegID] = homeFixture };
        Set(homeFixture, "aDockingPorts", new List<string> { "station-port" });
        var ship = HelpShip(); ship.IsAIShip = true; ship.fShallowRCSRemass = 1873;
        ship.objSS.vPosx = 1E-4; ship.objSS.vVelX = 1000 / 149597872000.0;
        var pilot = new HelpPilot(ship); var target = new TargetNewIfNone(pilot);
        lowFixture = false; help = false; plannedFixture = null;
        int sent = sends, planned = plans;
        Check(target.RunCommand(pilot.Blackboard).Status == CommandCode.Running && sends == sent + 1 && plans == planned + 1, "Failed native return did not retain selection/SOS.");
        Check(!ship.objSS.bIgnoreGrav && ship.objSS.NavData == null, "Free selection wait did not retain gravity.");
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) target.RunCommand(pilot.Blackboard);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(plans == planned + 1 && sends == sent + 1, "Idle cooldown repeated planning or requests.");
        Check(allocated == 0, "Unchanged selection waits allocated " + allocated + " bytes.");
        StarSystem.fEpoch += 65;
        plannedFixture = new NavData(ship);
        plannedFixture.AddNavDataPoint(new NavDataPoint(StarSystem.fEpoch, new ShipSitu(false), 1873, 0, 0));
        plannedFixture.AddNavDataPoint(new NavDataPoint(StarSystem.fEpoch + 600, homeFixture.objSS, 1700, 0, 0));
        Check(target.RunCommand(pilot.Blackboard).Status == CommandCode.Finished && ship.shipScanTarget == homeFixture && ReferenceEquals(ship.objSS.NavData, plannedFixture), "Feasible fallback did not preserve native station and plan.");
        Check(sends == sent + 1 && pilot.Blackboard.HasKey("TripFuelPrediction"), "Successful return sent false distress or lost fuel prediction.");
        Check(pilot.Blackboard.Get<string>("TripFuelPrediction").EndsWith("&173"), "Return prediction did not use the actual one-leg RCS difference.");
        var fallback = AccessTools.Method(typeof(Ostrabus.OstrabusPlugin).Assembly.GetType("Ostrabus.ShipNavigation.ScavengerReturn", true), "Fallback");
        var dtoType = AccessTools.Inner(typeof(TargetNewIfNone), "TargetDataDTO");
        foreach (string scenario in new[] { "accepted-job", "missing", "destroyed", "hidden", "replaced", "no-ports", "nonstation", "combat", "combat-target", "failed-plan" }) {
            homeFixture.bDestroyed = scenario == "destroyed"; homeFixture.HideFromSystem = scenario == "hidden";
            homeFixture.objSS.bIsBO = scenario != "nonstation";
            Set(homeFixture, "aDockingPorts", scenario == "no-ports" ? new List<string>() : new List<string> { "station-port" });
            CrewSim.system.dictShips[homeFixture.strRegID] = scenario == "replaced" ? HelpShip() : homeFixture;
            pilot.MissingHome = scenario == "missing"; pilot.Blackboard.Set("InCombat", scenario == "combat");
            ship.shipCombatTarget = scenario == "combat-target" ? homeFixture : null;
            var dto = Activator.CreateInstance(dtoType);
            var original = scenario == "accepted-job" ? new TargetData(ship) : null;
            AccessTools.Field(dtoType, "TargetData").SetValue(dto, original);
            var acceptedPlan = plannedFixture;
            if (scenario == "failed-plan") plannedFixture = null;
            int oldPlans = plans;
            fallback.Invoke(null, new object[] { target, dto });
            Check(ReferenceEquals(AccessTools.Field(dtoType, "TargetData").GetValue(dto), original), "Unsafe return fallback accepted: " + scenario);
            Check(plans == oldPlans + (scenario == "failed-plan" ? 1 : 0), "Excluded return unnecessarily planned: " + scenario);
            plannedFixture = acceptedPlan;
        }
        pilot.MissingHome = false; pilot.Blackboard.Set("InCombat", false); ship.shipCombatTarget = null;
        homeFixture.bDestroyed = false; homeFixture.HideFromSystem = false; homeFixture.objSS.bIsBO = true;
        Set(homeFixture, "aDockingPorts", new List<string> { "station-port" });
        CrewSim.system.dictShips[homeFixture.strRegID] = homeFixture;
        foreach (AIType role in new[] { AIType.Civilian, AIType.Navy, AIType.Police, AIType.HaulerCargo, AIType.HaulerDeployer }) {
            var other = HelpShip(); var otherPilot = new HelpPilot(other) { Role = role };
            int oldPlans = plans;
            new TargetNewIfNone(otherPilot).RunCommand(otherPilot.Blackboard);
            Check(other.shipScanTarget == null && plans == oldPlans, "Scav fallback changed " + role);
        }
        ship.objSS.NavData = null; ship.ClearShipTarget();
        foreach (string scenario in new[] { "vacuum", "atmosphere", "surface", "docked", "body-lock", "orbit-lock", "grounded", "input", "rcs", "lift", "drag", "player", "hidden", "unknown-body", "combat", "combat-target", "navigation", "torch", "station", "non-ai", "orbital-off" }) {
            enabled.SetValue(null, scenario != "orbital-off");
            ship.objSS.bIgnoreGrav = false; ship.fAIPauseTimer = 0; ship.fAIDockingExpire = 20;
            ship.objSS.bBOLocked = scenario == "body-lock"; ship.objSS.bOrbitLocked = scenario == "orbit-lock"; ship.objSS.bGrounded = scenario == "grounded";
            ship.objSS.vAccIn.x = scenario == "input" ? 1 : 0;
            ship.objSS.vAccLift.x = scenario == "lift" ? 1 : 0; ship.objSS.vAccDrag.x = scenario == "drag" ? 1 : 0;
            ship.objSS.vAccRCS.x = scenario == "rcs" ? 1 : 0; // Native Maneuver is a no-op fixture.
            pilot.Blackboard.Set("InCombat", scenario == "combat");
            ship.shipCombatTarget = scenario == "combat-target" ? homeFixture : null;
            ship.objSS.bIsBO = scenario == "station";
            ship.IsAIShip = scenario != "non-ai";
            ship.objSS.NavData = scenario == "navigation" ? plannedFixture : null;
            Set(ship, "nLoadState", scenario == "torch" ? Ship.Loaded.Full : Ship.Loaded.Shallow);
            Set(ship, "bTorchDriveThrusting", scenario == "torch");
            var links = (Dictionary<string, Ship>)AccessTools.Field(typeof(Ship), "aDocked").GetValue(ship);
            links.Clear(); if (scenario == "docked") links["port"] = homeFixture; ship.ResetDockedCache();
            ((Dictionary<string, string>)AccessTools.Field(typeof(StarSystem), "dictShipOwners").GetValue(CrewSim.system))[ship.strRegID] =
                scenario == "player" ? CrewSim.coPlayer.strID : "OKLGScav";
            ship.HideFromSystem = scenario == "hidden";
            if (scenario == "unknown-body") waitingBody = null;
            else {
                waitingBody = Empty<BodyOrbit>(); waitingBody.fRadius = scenario == "surface" ? .001 : 1E-6;
                if (scenario == "atmosphere") waitingBody.aAtmospheres = new[] { new JsonAtmosphere { fMaxAltitude = 20000 } };
            }
            var wait = new Hold(pilot);
            wait.RunCommand(pilot.Blackboard);
            Check(ship.objSS.bIgnoreGrav == (scenario != "vacuum"), "Orbital waiting exclusion failed: " + scenario);
        }
        plannedFixture = null;
        enabled.SetValue(null, false);
    }
}
