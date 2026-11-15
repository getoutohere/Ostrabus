using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json;
using Ostrabus.MiscBugFixes;
using Ostrabus.ShipNavigation;
using Ostranauts.Core;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using Ostranauts.Ships.Comms;

static partial class TrafficChecks {
    static bool waitingHelp, waitingHostile, waitingNoHome, waitingPrivate, waitingLow;
    static int waitingDraws, waitingStops;
    static CondOwner waitingCaptain;
    static double waitingDraw = .5;
    static bool WaitCaptain(ref CondOwner captain, ref bool __result) { captain = waitingCaptain; __result = true; return false; }
    static bool WaitTrigger(ref bool __result) { __result = waitingHelp; return false; }
    static bool WaitCond(ref CondTrigger __result) { __result = Empty<CondTrigger>(); return false; }
    static bool WaitRotor(ref float __result) { __result = 0; return false; }
    static bool WaitStop() { waitingStops++; return false; }
    static bool WaitScore(ref float __result) { __result = waitingHostile ? -100 : 100; return false; }
    static bool WaitOwner(ref string __result) { __result = waitingPrivate ? "waiting-captain" : "owner-faction"; return false; }
    static bool WaitNearest(List<string> __3, ref Ship __result) { homeCalls++; homeFilter = __3?.ToArray(); __result = waitingNoHome || waitingPrivate && __3 != null ? null : home; return false; }
    static bool WaitLow(ref bool __result) { __result = waitingLow; return false; }
    static bool WaitTarget(ref TargetData __result) { __result = new TargetData(destination); return false; }
    static bool WaitPlan(ref Ostranauts.ShipGUIs.Utilities.NavData __result) { __result = null; return false; }
    static bool WaitCost(ref double __result) { __result = 5; return false; }
    static bool WaitUndock(Undock __instance, ref CommandStatus __result) {
        Ship ship = __instance.ShipUs;
        var links = (Dictionary<string, Ship>)AccessTools.Field(typeof(Ship), "aDocked").GetValue(ship);
        foreach (Ship peer in links.Values) ((Dictionary<string, Ship>)AccessTools.Field(typeof(Ship), "aDocked").GetValue(peer)).Clear();
        links.Clear(); ship.CachedAllDockedShips = null; ship.ClearShipTarget(); ship.fAIDockingExpire = double.NegativeInfinity;
        __result = new CommandStatus(CommandCode.Finished, __instance); return false;
    }
    static bool WaitInstant(BaseCommand __instance, ref CommandStatus __result) {
        instantCalls++;
        if (instantResult == CommandCode.Finished) LinkVisit(__instance.ShipUs, destination);
        __result = new CommandStatus(instantResult, __instance); return false;
    }
    static bool WaitDraw(int __0, int __1, ref int __result) { waitingDraws++; __result = __0 + (int)((__1 - __0) * waitingDraw); return false; }
    static CivilianPilot WaitPilot(bool asteroid, double tank = 0) {
        var ship = Hull("waiting-" + checks); ship.IsAIShip = true; ship.fShallowRCSRemass = tank; ship.fShallowRCSRemassMax = 1000;
        ship.fAIDockingExpire = double.NegativeInfinity;
        Set(ship, "<Comms>k__BackingField", Empty<Comms>());
        waitingCaptain = Empty<CondOwner>(); Set(waitingCaptain, "_strID", "waiting-captain"); waitingCaptain.bAlive = true;
        var pilot = new CivilianPilot(home.strRegID, ship);
        Set(pilot, "<CaptainUs>k__BackingField", waitingCaptain);
        var peer = Hull("contact-" + checks); peer.Classification = asteroid ? Ship.TypeClassification.Asteroid : Ship.TypeClassification.Default;
        LinkVisit(ship, peer);
        CivilianVisits.Phase(CivilianVisits.Sequence(pilot)) = 4;
        return pilot;
    }
    static Hold WaitLeaf(CivilianPilot pilot) => (Hold)VisitChildren(CivilianVisits.Sequence(pilot))[4];
    internal static void Waiting() {
        var boundary = new Harmony("offline.civilian-waiting.fixtures");
        var repair = new Harmony("offline.civilian-waiting.production");
        var policy = new Harmony("offline.civilian-waiting.policy");
        try {
            void Patch(Type t, string name, string replacement, Type[] signature = null) => boundary.Patch(
                AccessTools.Method(t, name, signature), prefix: new HarmonyMethod(typeof(TrafficChecks), replacement));
            Patch(typeof(BaseCharacter), "AssignCaptain", nameof(Quiet));
            Patch(typeof(Ship), "Maneuver", nameof(WaitStop));
            Patch(typeof(Comms), "GetCaptain", nameof(WaitCaptain));
            Patch(typeof(DataHandler), "GetCondTrigger", nameof(WaitCond), new[] { typeof(string) });
            Patch(typeof(CondTrigger), "Triggered", nameof(WaitTrigger));
            Patch(typeof(CondOwner), "GetFactionScore", nameof(WaitScore), new[] { typeof(string) });
            Patch(typeof(StarSystem), "GetShipOwner", nameof(WaitOwner));
            Patch(typeof(StarSystem), "GetNearestStation", nameof(WaitNearest));
            Patch(typeof(CivilianPilot), "MaxSpeed", nameof(Speed));
            Patch(typeof(UnityEngine.Debug), "LogWarning", nameof(Quiet), new[] { typeof(object) });
            Patch(typeof(UnityEngine.Debug), "Log", nameof(Quiet), new[] { typeof(object) });
            Patch(typeof(MathUtils), "Rand", nameof(WaitDraw), new[] { typeof(int), typeof(int), typeof(MathUtils.RandType), typeof(string) });
            boundary.Patch(AccessTools.PropertyGetter(typeof(Ship), "LiftRotorsThrustStrength"), prefix: new HarmonyMethod(typeof(TrafficChecks), nameof(WaitRotor)));
            boundary.Patch(AccessTools.PropertyGetter(typeof(Ship), "HasDockingPorts"), prefix: new HarmonyMethod(typeof(TrafficChecks), nameof(Yes)));
            CrewSim.system = Empty<StarSystem>(); Collections(CrewSim.system); AIShipManager.dictAIs = new Dictionary<string, List<AIShip>>();
            CrewSim.coPlayer = null; StarSystem.fEpoch = 100000;
            home = Hull("waiting-home"); home.objSS.bIsBO = true; home.Classification = Ship.TypeClassification.GroundStation;
            foreach (var type in new[] { typeof(CivilianVisitSequencePatch), typeof(CivilianVisitSffPatch), typeof(AiHoldRunClockPatch),
                typeof(AiHoldEligibilityClockPatch), typeof(CivilianVisitHoldPatch), typeof(CivilianHomeDispatchPatch) }) repair.CreateClassProcessor(type).Patch();

            var pilot = WaitPilot(true); var hold = WaitLeaf(pilot); var ship = pilot.ShipUs; var main = CivilianVisits.Sequence(pilot);
            Check(hold.RunCommand(pilot.Blackboard).Status == CommandCode.Running && ship.fAIDockingExpire == 375,
                "Repair-only asteroid wait changed native duration.");
            StarSystem.fEpoch += 376;
            var ended = main.RunCommand(pilot.Blackboard);
            Check(ended.Status == CommandCode.Finished && CivilianVisits.Phase(main) == 0,
                "Empty civilian did not finish visit through actual native Main.");
            Check(ship.fShallowRCSRemass == 0, "Visit repair invented fuel.");
            Check(((Ostranauts.Ships.AIPilots.Interfaces.IAICharacter)pilot).GetTargetHome().Ship == home &&
                homeFilter.SequenceEqual(new[] { "owner-faction" }), "Interface home selection lost native faction filter.");

            policy.CreateClassProcessor(typeof(CivilianAsteroidVisitPatch)).Patch();
            foreach (double draw in new[] { 0d, .5, 1d }) {
                waitingDraw = draw; pilot = WaitPilot(true); hold = WaitLeaf(pilot); ship = pilot.ShipUs;
                int draws = waitingDraws; hold.RunCommand(pilot.Blackboard);
                double duration = 300 + (int)(6900 * draw);
                Check(ship.fAIDockingExpire == duration && waitingDraws == draws + 1, "Asteroid duration bounds/draw count changed.");
                for (int i = 0; i < 20; i++) { StarSystem.fEpoch += 1; hold.RunCommand(pilot.Blackboard); }
                Check(waitingDraws == draws + 1 && ship.fAIDockingExpire == duration - 20, "Visit rerolled during ordinary ticks.");
            }
            waitingDraw = .5;
            foreach (var classification in new[] { Ship.TypeClassification.Default, Ship.TypeClassification.GroundStation }) {
                pilot = WaitPilot(false); ship = pilot.ShipUs; hold = WaitLeaf(pilot);
                ((Dictionary<string, Ship>)AccessTools.Field(typeof(Ship), "aDocked").GetValue(ship)).Values.First().Classification = classification;
                hold.RunCommand(pilot.Blackboard);
                Check(ship.fAIDockingExpire == 375, "Non-asteroid visit acquired hour-scale timing.");
            }
            pilot = WaitPilot(true); hold = WaitLeaf(pilot); ship = pilot.ShipUs; main = CivilianVisits.Sequence(pilot);
            hold.RunCommand(pilot.Blackboard); StarSystem.fEpoch += 123; hold.RunCommand(pilot.Blackboard);
            double remaining = ship.fAIDockingExpire; int previousDraws = waitingDraws;
            var saved = JsonConvert.DeserializeObject<JsonCommandPayload[]>(JsonConvert.SerializeObject(pilot.BehaviorTree.GetSaveData("")));
            var restored = WaitPilot(true); restored.ShipUs.fAIDockingExpire = remaining;
            restored.BehaviorTree.LoadSaveData("", saved);
            Check(restored.PriorityQueue.CanExecute(restored.Blackboard) == false && CivilianVisits.Phase(CivilianVisits.Sequence(restored)) == 4,
                "Save restore changed native visit parent/queue.");
            StarSystem.fEpoch += 3600;
            Check(restored.FFWD(WaitLeaf(restored), restored.Blackboard) == null, "SFF reset/requeued a restored visit.");
            WaitLeaf(restored).RunCommand(restored.Blackboard);
            Check(restored.ShipUs.fAIDockingExpire == remaining - 3600 && waitingDraws == previousDraws,
                "Save/load/SFF lost elapsed visit time or rerolled duration.");
            StarSystem.fEpoch += 60;
            Check(CivilianVisits.Sequence(restored).RunCommand(restored.Blackboard).Status == CommandCode.Finished,
                "SFF-completed visit did not return to native Main.");

            // A finished Hold may be referenced by an old one-shot wrapper and Main.
            pilot = WaitPilot(true); hold = WaitLeaf(pilot); main = CivilianVisits.Sequence(pilot); hold.RunCommand(pilot.Blackboard);
            StarSystem.fEpoch += 8000; var wrapper = new OneShotWrapperCommand(hold); wrapper.RunCommand(pilot.Blackboard);
            int prior = waitingDraws, priorStops = waitingStops;
            Check(main.RunCommand(pilot.Blackboard).Status == CommandCode.Finished && CivilianVisits.Phase(main) == 0 && waitingDraws == prior,
                "Shared SFF/Main Hold restarted an already completed visit.");
            Check(waitingStops == priorStops + 1, "Cached visit completion skipped native stop action.");
            // The same native Hold is reused after a long flight. Travel time
            // must not immediately consume the next prospecting duration.
            StarSystem.fEpoch += 50000; CivilianVisits.Phase(main) = 4; hold.RunCommand(pilot.Blackboard);
            Check(pilot.ShipUs.fAIDockingExpire == 3750, "Previous visit/flight clock consumed a fresh mining visit.");

            pilot = WaitPilot(true); hold = WaitLeaf(pilot); ship = pilot.ShipUs;
            ship.fAIDockingExpire = 0; ship.fAIPauseTimer = StarSystem.fEpoch + 60;
            prior = waitingDraws;
            Check(hold.RunCommand(pilot.Blackboard).Status == CommandCode.Failure && waitingDraws == prior,
                "Visit duration overrode the native failed-route pause.");

            foreach (string reason in new[] { "help", "full", "combat", "hostile-home", "missing-home", "private-owner", "scripted" }) {
                pilot = WaitPilot(true); ship = pilot.ShipUs; hold = reason == "scripted" ? new Hold(pilot) : WaitLeaf(pilot);
                waitingHelp = reason == "help"; waitingHostile = reason == "hostile-home"; waitingNoHome = reason == "missing-home";
                waitingPrivate = reason == "private-owner";
                if (reason == "full") { Set(ship, "nLoadState", Ship.Loaded.Full); boundary.Patch(AccessTools.Method(typeof(Ship), "GetRCSRemain"), prefix: new HarmonyMethod(typeof(TrafficChecks), nameof(Fuel))); fuel = 0; }
                if (reason == "combat") ship.shipCombatTarget = home;
                ship.fAIDockingExpire = 1;
                hold.RunCommand(pilot.Blackboard); StarSystem.fEpoch += 8000;
                Check(hold.RunCommand(pilot.Blackboard).Status == CommandCode.Running, "Wait exclusion bypassed: " + reason);
                if (reason == "full") boundary.Unpatch(AccessTools.Method(typeof(Ship), "GetRCSRemain"), HarmonyPatchType.Prefix, boundary.Id);
                waitingHelp = waitingHostile = waitingNoHome = waitingPrivate = false;
            }

            // SFF arrival runs the native civilian callback and only accepts its
            // already successful physical-docking result into native Main.
            Patch(typeof(Dock), "ResolveInstantly", nameof(WaitInstant));
            foreach (bool asteroid in new[] { false, true }) {
                pilot = WaitPilot(asteroid); ship = pilot.ShipUs; main = CivilianVisits.Sequence(pilot);
                destination = ((Dictionary<string, Ship>)AccessTools.Field(typeof(Ship), "aDocked").GetValue(ship)).Values.First();
                ((Dictionary<string, Ship>)AccessTools.Field(typeof(Ship), "aDocked").GetValue(ship)).Clear(); ship.CachedAllDockedShips = null;
                ship.shipScanTarget = destination; ship.shipSituTarget = destination.objSS;
                CivilianVisits.Phase(main) = 2; var fly = VisitChildren(main)[2]; instantResult = CommandCode.Finished;
                previousDraws = waitingDraws;
                Check(pilot.FFWD(fly, pilot.Blackboard) == null && CivilianVisits.Phase(main) == 4,
                    "Successful native SFF visit arrival did not enter Main/Hold.");
                Check(ship.fAIDockingExpire == (asteroid ? 3750 : 375) && waitingDraws == previousDraws + 1,
                    "SFF arrival used wrong visit category/draw count.");
                double oldRemaining = ship.fAIDockingExpire; StarSystem.fEpoch += 3600;
                Check(pilot.FFWD(fly, pilot.Blackboard) == null, "Repeated SFF before normal dispatch replayed approach.");
                WaitLeaf(pilot).RunCommand(pilot.Blackboard);
                Check(asteroid ? ship.fAIDockingExpire == oldRemaining - 3600 : double.IsNegativeInfinity(ship.fAIDockingExpire),
                    "SFF arrival lost its wait clock.");
            }
            pilot = WaitPilot(true); ship = pilot.ShipUs; main = CivilianVisits.Sequence(pilot);
            ((Dictionary<string, Ship>)AccessTools.Field(typeof(Ship), "aDocked").GetValue(ship)).Clear(); ship.CachedAllDockedShips = null;
            ship.shipScanTarget = destination; ship.shipSituTarget = destination.objSS; CivilianVisits.Phase(main) = 2;
            instantResult = CommandCode.Failure; var approach = VisitChildren(main)[2];
            Check(ReferenceEquals(pilot.FFWD(approach, pilot.Blackboard), approach) && CivilianVisits.Phase(main) == 2,
                "Failed native docking was promoted to successful visit.");

            // A completed visit returns to the real native Undock/selection
            // order: the physical unlink is a boundary, selection itself runs.
            Patch(typeof(Undock), "RunCommand", nameof(WaitUndock));
            Patch(typeof(AIShipManager), "LowFuel", nameof(WaitLow));
            Patch(typeof(CivilianPilot), "GetTarget", nameof(WaitTarget));
            Patch(typeof(TargetNewIfNone), "GenerateNavData", nameof(WaitPlan));
            Patch(typeof(TargetNewIfNone), "GetNeededFuel", nameof(WaitCost));
            foreach (bool low in new[] { false, true }) {
                waitingLow = low; pilot = WaitPilot(true, low ? 0 : 1000); ship = pilot.ShipUs; main = CivilianVisits.Sequence(pilot);
                destination = Hull("second-asteroid-" + checks); destination.Classification = Ship.TypeClassification.Asteroid;
                hold = WaitLeaf(pilot); hold.RunCommand(pilot.Blackboard); StarSystem.fEpoch += 8000;
                Check(main.RunCommand(pilot.Blackboard).Status == CommandCode.Finished && CivilianVisits.Phase(main) == 0, "Visit did not finish before reselection.");
                main.RunCommand(pilot.Blackboard); // Undock.
                main.RunCommand(pilot.Blackboard); // Native TargetNewIfNone.
                Check(ship.shipScanTarget == (low ? home : destination) && CivilianVisits.Phase(main) == 2,
                    "Native next-job/low-fuel home choice was replaced.");
            }

            // Completing a detached failed visit must release its stale goal.
            pilot = WaitPilot(true); ship = pilot.ShipUs; hold = WaitLeaf(pilot); main = CivilianVisits.Sequence(pilot);
            foreach (var peer in ((Dictionary<string, Ship>)AccessTools.Field(typeof(Ship), "aDocked").GetValue(ship)).Values)
                ((Dictionary<string, Ship>)AccessTools.Field(typeof(Ship), "aDocked").GetValue(peer)).Clear();
            ((Dictionary<string, Ship>)AccessTools.Field(typeof(Ship), "aDocked").GetValue(ship)).Clear(); ship.CachedAllDockedShips = null;
            ship.shipScanTarget = home; ship.shipSituTarget = home.objSS;
            hold.RunCommand(pilot.Blackboard); StarSystem.fEpoch += 8000; main.RunCommand(pilot.Blackboard);
            Check(ship.shipScanTarget == null && ship.shipSituTarget == null, "Detached completed visit retained an obsolete goal.");
            Check(!waitingHelp, "Fixture help leaked.");
            // Legacy saves keep an existing remainder; corrupt/foreign payloads
            // cannot force completion or require a custom type to deserialize.
            foreach (string[] payload in new[] { new[] { "foreign-data" }, new[] { "ostrabus.civilian-visit.v1", "bad", "0", "NaN" } }) {
                pilot = WaitPilot(true); ship = pilot.ShipUs; hold = WaitLeaf(pilot); ship.fAIDockingExpire = 900;
                var legacy = new[] { new JsonCommandPayload { strPath = "/BehaviorTree/Main", aCommandPayload = new[] { "4" } },
                    new JsonCommandPayload { strPath = "/BehaviorTree/Main/Hold", aCommandPayload = payload } };
                pilot.BehaviorTree.LoadSaveData("", legacy); int drawCount = waitingDraws;
                StarSystem.fEpoch += 60; hold.RunCommand(pilot.Blackboard);
                Check(ship.fAIDockingExpire == 840 && waitingDraws == drawCount, "Legacy remainder was restarted.");
                if (payload[0] == "foreign-data") {
                    pilot.BehaviorTree.GetSaveData(""); Check(hold.SaveData.SequenceEqual(payload), "Foreign payload was overwritten.");
                }
            }
            // Removing the plugin still leaves a wholly native loadable tree.
            pilot = WaitPilot(true); hold = WaitLeaf(pilot); hold.RunCommand(pilot.Blackboard);
            var portable = JsonConvert.DeserializeObject<JsonCommandPayload[]>(JsonConvert.SerializeObject(pilot.BehaviorTree.GetSaveData("")));
            repair.Unpatch(AccessTools.Constructor(typeof(CivilianPilot), new[] { typeof(string), typeof(Ship) }), HarmonyPatchType.Transpiler, repair.Id);
            var vanilla = new CivilianPilot(home.strRegID, Hull("vanilla-load")); vanilla.BehaviorTree.LoadSaveData("", portable);
            var nativeMain = (StickySequenceCommand)((List<ICommand>)AccessTools.Field(typeof(SelectorCommand), "_childCommands").GetValue(vanilla.BehaviorTree)).Last();
            Check(nativeMain.GetType() == typeof(StickySequenceCommand) && nativeMain.SaveData[0] == "4" &&
                VisitChildren(nativeMain)[4].GetType() == typeof(Hold), "Unpatched game cannot restore the saved visit.");
            repair.CreateClassProcessor(typeof(CivilianVisitSequencePatch)).Patch();
            pilot = WaitPilot(true); hold = WaitLeaf(pilot); hold.RunCommand(pilot.Blackboard);
            var untouched = new CommandStatus(CommandCode.Running, hold);
            for (int i = 0; i < 100; i++) CivilianVisitWaiting.BeforeRun(hold, ref untouched);
            long allocationStart = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10000; i++) CivilianVisitWaiting.BeforeRun(hold, ref untouched);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocationStart;
            Check(allocated == 0, "Steady visit bookkeeping allocated " + allocated + " bytes.");
            Console.WriteLine(checks + " civilian visit completion/timing/save/SFF/permission assertions passed.");
        } finally { policy.UnpatchSelf(); repair.UnpatchSelf(); boundary.UnpatchSelf(); }
    }
}
