using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.AIPilots.Interfaces;
using Ostranauts.Ships.Comms;
using Ostranauts.Ships.Commands;
using Ostranauts.ShipGUIs.Utilities;
using Ostrabus.CeresNavigation;
using Ostrabus.ShipNavigation;

// Native queues, Blackboard serialization, AI dispatch, candidate selection and
// fuel getter run under Mono. World assets and native leaf outcomes are controlled.
static class CeresAuditChecks {
    static int checks, flyCalls, dockCalls, nativeCalls;
    static double budget = 1e-8;
    static bool flyRunning, dockRunning, flyFailure, dockFailure, playerManned, connected;
    static LowFuelRetreat combatRetreat;
    static Ship home;
    static BodyOrbit homeBody;
    static string owner = "CCRE";
    static List<Tuple<double, ShipSitu>> spots;
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Field(object target, string name, object value) => AccessTools.Field(target.GetType(), name).SetValue(target, value);
    static void Check(bool ok, string reason) { checks++; if (!ok) throw new Exception(reason); }
    static bool Quiet() => false;
    static bool No(ref bool __result) { __result = false; return false; }
    static bool IsStation(Ship __instance, ref bool __result) { __result = ReferenceEquals(__instance, home); return false; }
    static bool Yes(ref bool __result) { __result = true; return false; }
    static bool PlayerManned(ref bool __result) { __result = playerManned; return false; }
    static bool Connected(ref bool __result) { __result = connected; return false; }
    static bool Owner(ref string __result) { __result = owner; return false; }
    static bool Station(ref Ship __result) { __result = home; return false; }
    static bool Body(ref BodyOrbit __result) { __result = homeBody; return false; }
    static bool DeltaV(ref double __result) { __result = budget; return false; }
    static bool Pois(ref List<Ship> __result) { __result = new List<Ship>(); return false; }
    static bool Spots(ref List<Tuple<double, ShipSitu>> __result) { __result = spots; return false; }
    static bool SafeCoords(ShipSitu objSS, double dX, double dY, ref bool __result) {
        objSS.vPosx = dX; objSS.vPosy = dY; __result = true; return false;
    }
    static bool Fly(ref CommandStatus __result, FlyTo __instance) {
        flyCalls++; __result = new CommandStatus(flyFailure ? CommandCode.Failure : flyRunning ? CommandCode.Running : CommandCode.Finished, __instance); return false;
    }
    static bool Docked(ref CommandStatus __result, Dock __instance) {
        dockCalls++; __result = new CommandStatus(dockFailure ? CommandCode.Failure : dockRunning ? CommandCode.Running : CommandCode.Finished, __instance); return false;
    }
    static void Patch(Harmony h, Type type, string name, string prefix) => h.Patch(AccessTools.Method(type, name), prefix: new HarmonyMethod(typeof(CeresAuditChecks), prefix));
    static Ship ShipAt(string id, double x = 0) {
        var ship = Empty<Ship>(); ship.strRegID = id; ship.IsAIShip = true;
        Field(ship, "<objSS>k__BackingField", new ShipSitu(false) { vPosx = x });
        Field(ship, "nLoadState", Ship.Loaded.Shallow); ship.fShallowRCSRemass = 300;
        Field(ship, "fFuelEfficiencyMod", 1d);
        CrewSim.system.dictShips[id] = ship;
        return ship;
    }
    sealed class NativeProbe : BaseCommand {
        internal NativeProbe(Program.Pilot pilot) : base(pilot) { }
        public override CommandStatus RunCommand(Blackboard board) { nativeCalls++; return combatRetreat == null ? Running : combatRetreat.RunCommand(board); }
    }
    sealed class FlagGate : BaseCommand {
        readonly string key;
        internal FlagGate(Program.Pilot pilot, string flag) : base(pilot) { key = flag; }
        public override CommandStatus RunCommand(Blackboard board) => board.Get<bool>(key) ? Success : Failure;
    }
    static AIShip Controller(Ship ship, Blackboard board = null, AIType role = AIType.Navy) {
        var pilot = new Program.Pilot { ShipUs = ship, Speed = 1e-9, Role = role, Blackboard = board ?? new Blackboard() };
        pilot.PriorityQueue = new QueuedCommandsNode("PriorityQueue", pilot);
        var ai = Empty<AIShip>(); Field(ai, "_shipUs", ship); Field(ai, "_aiCharacter", pilot);
        Field(ai, "<Blackboard>k__BackingField", pilot.Blackboard); Field(ai, "<HomeStation>k__BackingField", home.strRegID);
        var main = new StickySequenceCommand("Main", new ICommand[] { new NativeProbe(pilot), new FlyTo(pilot), new Dock(pilot) });
        var tree = new SelectorCommand("BehaviorTree", pilot.PriorityQueue,
            new SequenceCommand("CombatResponse", new ICommand[] { new AmIInCombat(pilot), new NativeProbe(pilot) }),
            new SequenceCommand("ThreatResponse", new ICommand[] { new FlagGate(pilot, "fixture.threat"), new NativeProbe(pilot) }),
            new SequenceCommand("SupportResponse", new ICommand[] { new FlagGate(pilot, "fixture.support"), new NativeProbe(pilot) }), main);
        pilot.BehaviorTree = tree;
        Field(ai, "_behaviorTree", tree);
        AIShipManager.dictAIs = new Dictionary<string, List<AIShip>> { ["ceres"] = new List<AIShip> { ai } };
        return ai;
    }
    static Blackboard Reload(Blackboard source) {
        var board = new Blackboard(); board.LoadSavedData(source.GetJson()); return board;
    }
    static void AssertFlight(AIShip ai) {
        int before = flyCalls;
        ai.RunAI();
        Check(flyCalls == before + 1 && StationReturn.Pending(ai), "Return flight did not run exclusively");
    }
    internal static void Run(bool auditPriority = false, bool reassess = false) {
        var h = new Harmony("offline.ceres.repair");
        try {
            CrewSim.system = Empty<StarSystem>(); CrewSim.system.dictShips = new Dictionary<string, Ship>();
            home = ShipAt("BCRS", .00001); home.objSS.bIsBO = true; home.objSS.strBOPORShip = "BCRS";
            homeBody = Empty<BodyOrbit>(); homeBody.strName = "BCRS"; homeBody.fPerh = .00001; homeBody.fPeriod = 1;
            homeBody.dXReal = 123; homeBody.dVelX = 456; homeBody.dTimeCalcLast = 789;
            Patch(h, typeof(StarSystem), "GetBO", nameof(Body));
            Patch(h, typeof(StarSystem), "GetShipOwner", nameof(Owner));
            Patch(h, typeof(StarSystem), "GetNearestStation", nameof(Station));
            Patch(h, typeof(StarSystem), "GetNearestStationRegional", nameof(Station));
            h.Patch(AccessTools.Method(typeof(Ship), "IsDockedWith", new[] { typeof(Ship) }), prefix: new HarmonyMethod(typeof(CeresAuditChecks), nameof(No)));
            foreach (string method in new[] { "IsDockedWithAStation", "IsDockedWithPlayer", "IsDocked", "IsStationHidden" }) Patch(h, typeof(Ship), method, nameof(No));
            Patch(h, typeof(Ship), "IsStation", nameof(IsStation));
            h.Patch(AccessTools.PropertyGetter(typeof(Ship), "NavPlayerManned"), prefix: new HarmonyMethod(typeof(CeresAuditChecks), nameof(PlayerManned)));
            h.Patch(AccessTools.PropertyGetter(typeof(Ship), "HasDockGroup"), prefix: new HarmonyMethod(typeof(CeresAuditChecks), nameof(Connected)));
            h.Patch(AccessTools.PropertyGetter(typeof(Ship), "DeltaVRemainingRCS"), prefix: new HarmonyMethod(typeof(CeresAuditChecks), nameof(DeltaV)));
            h.Patch(AccessTools.Method(typeof(Ship), "CalculateRCSFuelConsumption", new[] { typeof(double) }), prefix: new HarmonyMethod(typeof(CeresAuditChecks), nameof(Cost)));
            Patch(h, typeof(Ship), "StopManeuver", nameof(Quiet));
            Patch(h, typeof(Ship), "SetThrust", nameof(Quiet));
            Patch(h, typeof(ShipSitu), "UpdateTime", nameof(Quiet));
            Patch(h, typeof(FlyTo), "RunCommand", nameof(Fly));
            Patch(h, typeof(FlyTo), "CanExecute", nameof(Yes));
            Patch(h, typeof(Dock), "RunCommand", nameof(Docked));
            foreach (Type patch in new[] { typeof(InvalidRcsBudgetPatch), typeof(ClearTargetFuelPatch), typeof(RetargetFuelPatch),
                typeof(StationReturnPatch), typeof(StationReturnMissionPatch), typeof(StationReturnFastForwardPatch), typeof(NavyCandidateFuelPatch),
                typeof(LegacyReturnLoadPatch),
                typeof(CeresNavigationGuardrails.ReturnGuardPatch), typeof(CeresNavigationGuardrails.PatrolHomePatch),
                typeof(CeresNavigationGuardrails.PlannedReservePatch), typeof(CeresNavigationGuardrails.ManualReservePatch),
                typeof(CeresCombatRetreat), typeof(LowFuelRetreatProgressPatch) }) h.CreateClassProcessor(patch).Patch();

            var ship = ShipAt("audit-patrol"); var ai = Controller(ship);
            // Real pilot constructors, not assumed Main names/shapes. In
            // particular Navy uses FlyToPatrol, whose FindCommand is exact-type.
            h.Patch(AccessTools.Method(typeof(Comms), nameof(Comms.GetCaptain)), prefix: new HarmonyMethod(typeof(CeresAuditChecks), nameof(Quiet)));
            var triggerLookup = AccessTools.Method(typeof(DataHandler), nameof(DataHandler.GetCondTrigger), new[] { typeof(string) });
            h.Patch(triggerLookup, prefix: new HarmonyMethod(typeof(CeresAuditChecks), nameof(Quiet)));
            foreach (AIType role in new[] { AIType.Navy, AIType.Pirate, AIType.Police }) {
                var realShip = ShipAt("real-tree-" + role);
                Field(realShip, "<Comms>k__BackingField", Empty<Comms>());
                BaseCharacter realPilot = role == AIType.Navy ? (BaseCharacter)new NavyPilot(null, realShip) :
                    role == AIType.Pirate ? new PiratePilot(null, realShip) : new PolicePilot(null, realShip);
                var realAI = Empty<AIShip>(); Field(realAI, "_shipUs", realShip); Field(realAI, "_aiCharacter", realPilot);
                Field(realAI, "<Blackboard>k__BackingField", realPilot.Blackboard);
                StationReturn.Request(realAI, home); StationReturn.Register(realAI);
                var root = (SelectorCommand)realPilot.BehaviorTree;
                var branches = (List<ICommand>)AccessTools.Field(typeof(SelectorCommand), "_childCommands").GetValue(root);
                int beforeReal = flyCalls; flyRunning = true;
                StationReturn.RunChild(branches.Last(), realPilot.Blackboard, root);
                Check(flyCalls == beforeReal + 1, "Real " + role + " mission did not accept return dispatch");
                StationReturn.Cancel(realAI);
            }
            h.Unpatch(triggerLookup, HarmonyPatchType.Prefix, h.Id);
            ai.Blackboard.Set("TripFuelPrediction", "old-patrol&100");
            StationReturn.Request(ai, home);
            Check(ai.Blackboard.HasKey("TripFuelPrediction") && ship.shipScanTarget == null, "Request changed active target/prediction before gaining control");
            Check(ai.AICharacter.PriorityQueue.CanExecute(ai.Blackboard) == false, "Return created a queued custom sequence");
            flyRunning = true; AssertFlight(ai); AssertFlight(ai);
            Check(dockCalls == 0 && nativeCalls == 0, "Return ran docking/tree while flight is running");
            flyRunning = false; AssertFlight(ai);
            Check(ai.Blackboard.Get<bool>(StationReturn.DockingKey), "Flight success did not retain Dock stage");
            dockRunning = true; ai.RunAI();
            Check(dockCalls == 1 && StationReturn.Pending(ai), "Dock running discarded intent");
            dockRunning = false; ai.RunAI();
            Check(dockCalls == 2 && !StationReturn.Pending(ai), "Dock success did not complete return");
            ai.RunAI(); Check(nativeCalls == 1, "Native behavior did not resume after return");
            Console.WriteLine("Native AI dispatch: FlyTo Running -> Finished -> Dock Running -> Finished, no lost docking step.");

            foreach (bool duringDocking in new[] { false, true }) {
                ai = Controller(ship); StationReturn.Request(ai, home);
                ai.Blackboard.Set(StationReturn.DockingKey, duringDocking);
                var saved = Reload(ai.Blackboard); ai = Controller(ship, saved);
                int flies = flyCalls, docks = dockCalls;
                flyRunning = true; dockRunning = true; ai.RunAI();
                Check(duringDocking ? dockCalls == docks + 1 && flyCalls == flies : flyCalls == flies + 1 && dockCalls == docks,
                    "Reload lost return destination/stage");
                Check(StationReturn.Pending(ai), "Reload consumed pending return");
                int queued = ((Queue<ICommand>)AccessTools.Field(typeof(QueuedCommandsNode), "commandQueue").GetValue(ai.AICharacter.PriorityQueue)).Count;
                ai.FFWD();
                Check(((Queue<ICommand>)AccessTools.Field(typeof(QueuedCommandsNode), "commandQueue").GetValue(ai.AICharacter.PriorityQueue)).Count == queued,
                    "SFF queued a duplicate return leaf");
            }
            flyRunning = true; dockRunning = false;
            ai = Controller(ship); ai.Blackboard.Set("CeresNavigation.ReturnRouteQueued", true);
            ai = Controller(ship, Reload(ai.Blackboard));
            ai.RunAI();
            Check(StationReturn.Pending(ai) && !ai.Blackboard.HasKey("CeresNavigation.ReturnRouteQueued"), "Legacy return latch not migrated");
            Console.WriteLine("Return intent/stage survive native Blackboard serialization; legacy latch migrates; SFF leaves no duplicate leaf.");
            foreach (string nodeName in new[] { "CeresNavigationReturn", "ShipboardActionReturn" }) {
                foreach (bool active in new[] { false, true }) {
                var oldPilot = (Program.Pilot)ai.AICharacter;
                var oldQueue = new QueuedCommandsNode("PriorityQueue", oldPilot);
                oldQueue.EnqueueCommand(new StickySequenceCommand(nodeName, new ICommand[] { new FlyTo(oldPilot), new Dock(oldPilot) }));
                if (active) oldQueue.RunCommand(ai.Blackboard);
                oldQueue.EnqueueCommand(new FlyTo(oldPilot));
                var payload = oldQueue.GetSaveData("root").ToArray();
                var loadedQueue = new QueuedCommandsNode("PriorityQueue", oldPilot);
                loadedQueue.LoadSaveData("root", payload);
                var savedQueue = (Queue<ICommand>)AccessTools.Field(typeof(QueuedCommandsNode), "commandQueue").GetValue(loadedQueue);
                Check(savedQueue.Count == 1 && savedQueue.Peek() is FlyTo, "Old return cleanup removed unrelated command or retained a stray leaf");
                }
            }

            // Player/manual control, attached groups and active reactions retain authority.
            int before = flyCalls;
            playerManned = true; ai.RunAI(); playerManned = false;
            Check(flyCalls == before, "Return overrode manual helm");
            playerManned = true;
            Check(StationReturnFastForwardPatch.Prefix(ai), "Pending return blocked manually controlled SFF");
            playerManned = false;
            connected = true; ai.RunAI(); connected = false;
            Check(flyCalls == before, "Return steered a connected group");
            Field(ai, "_activeReaction", new NativeProbe((Program.Pilot)ai.AICharacter)); ai.RunAI();
            Check(StationReturnFastForwardPatch.Prefix(ai), "Pending return blocked reaction SFF");
            Check(flyCalls == before, "Return overrode active reaction"); Field(ai, "_activeReaction", null);
            // Existing logical home target and route survive idempotent requests.
            var plan = new NavData(ship); plan.AddNavDataPoint(new NavDataPoint(0, new ShipSitu(false), 300, 0, 0));
            plan.AddNavDataPoint(new NavDataPoint(100, new ShipSitu(false), 280, 0, 0)); ship.objSS.NavData = plan;
            StationReturn.Request(ai, home);
            Check(ReferenceEquals(plan, ship.objSS.NavData), "Duplicate return request discarded healthy route");
            ai.AICharacter.PriorityQueue.EnqueueCommand(new NativeProbe((Program.Pilot)ai.AICharacter));
            int urgentBefore = nativeCalls, returnBefore = flyCalls;
            ai.RunAI();
            Check(nativeCalls == urgentBefore + 1 && flyCalls == returnBefore, "Return starved native queued command");
            Check(StationReturnFastForwardPatch.Prefix(ai), "Return suppressed queued command SFF");
            var priorityQueue = ai.AICharacter.PriorityQueue;
            Field(priorityQueue, "currentCommand", null);
            ((Queue<ICommand>)AccessTools.Field(typeof(QueuedCommandsNode), "commandQueue").GetValue(priorityQueue)).Clear();
            if (auditPriority) {
                int treeBefore = nativeCalls, flightBefore = flyCalls;
                ai.Blackboard.Set("InCombat", true);
                Check(new AmIInCombat(ai.AICharacter).RunCommand(ai.Blackboard).Status == CommandCode.Finished,
                    "Audit setup did not activate native combat predicate");
                ai.RunAI();
                Check(nativeCalls == treeBefore + 1 && flyCalls == flightBefore,
                    "Pending return bypassed native combat branch");
                Check(StationReturnFastForwardPatch.Prefix(ai), "Return suppressed active combat SFF");
                ai.Blackboard.Set("InCombat", false);
                AssertFlight(ai);
                foreach (string flag in new[] { "fixture.threat", "fixture.support" }) {
                    ai.Blackboard.Set(flag, true); treeBefore = nativeCalls; flightBefore = flyCalls;
                    ai.RunAI();
                    Check(nativeCalls == treeBefore + 1 && flyCalls == flightBefore, "Return bypassed preceding " + flag + " selector branch");
                    ai.Blackboard.Set(flag, false); AssertFlight(ai);
                }
                ai.Blackboard.Set("InCombat", true);
                ai = Controller(ship, Reload(ai.Blackboard)); treeBefore = nativeCalls; flightBefore = flyCalls;
                ai.RunAI();
                Check(nativeCalls == treeBefore + 1 && flyCalls == flightBefore && StationReturn.Pending(ai), "Reloaded combat lost priority or return intent");
                ai.Blackboard.Set("InCombat", false); AssertFlight(ai);
                // A new request during a reaction must not steal its target or
                // invalidate its planned maneuver before the mission gets control.
                var reactionDestination = ShipAt("reaction-destination", .005);
                new TargetData(reactionDestination).ApplyToShip(ship);
                Field(ai, "_activeReaction", new NativeProbe((Program.Pilot)ai.AICharacter));
                StationReturn.Request(ai, reactionDestination); StationReturn.Request(ai, home);
                Check(ReferenceEquals(ship.shipScanTarget, reactionDestination), "Request overwrote reaction destination");
                Field(ai, "_activeReaction", null);
                Console.WriteLine("Priority regression fixed: native queued/combat decisions run before return; quiet return resumes.");
            }
            StationReturn.Cancel(ai);

            ai = Controller(ship); StationReturn.Request(ai, home);
            flyFailure = true; int docksBeforeFailure = dockCalls; ai.RunAI();
            Check(StationReturn.Pending(ai) && !ai.Blackboard.Get<bool>(StationReturn.DockingKey) && dockCalls == docksBeforeFailure && ship.objSS.NavData == null,
                "Failed approach lost return intent, retained route, or attempted docking");
            flyFailure = false; flyRunning = false; ai.RunAI();
            dockFailure = true; ai.RunAI();
            Check(StationReturn.Pending(ai) && !ai.Blackboard.Get<bool>(StationReturn.DockingKey), "Failed docking did not reset to approach");
            dockFailure = false; ai.RunAI(); ai.RunAI();
            Check(!StationReturn.Pending(ai), "Return retry could not complete");

            // Clear/retarget fixes are general; equivalent target reapplication preserves fuel accounting.
            new TargetData(home).ApplyToShip(ship);
            ai.Blackboard.Set("TripFuelPrediction", "same-leg&10");
            new TargetData(home).ApplyToShip(ship);
            Check(ai.Blackboard.HasKey("TripFuelPrediction"), "Equivalent target lost valid fuel prediction");
            var other = ShipAt("other-station", .00003);
            new TargetData(other).ApplyToShip(ship);
            Check(!ai.Blackboard.HasKey("TripFuelPrediction"), "Changed target retains old fuel prediction");
            ai.Blackboard.Set("TripFuelPrediction", "old-leg&10"); ship.ClearShipTarget();
            Check(!ai.Blackboard.HasKey("TripFuelPrediction"), "ClearShipTarget retains old fuel prediction");

            // Actual native Navy selection, with bounded world candidates and native RNG choice.
            Patch(h, typeof(AIShipManager), "GetPOIs", nameof(Pois));
            Patch(h, typeof(NavyPilot), "GetRandomSpotsWithinInfluenceRange", nameof(Spots));
            Patch(h, typeof(StarSystem), "SetSituToRandomSafeCoords", nameof(SafeCoords));
            h.Patch(AccessTools.Method(typeof(ShipSitu), "LockToBO", new[] { typeof(double), typeof(bool) }), prefix: new HarmonyMethod(typeof(CeresAuditChecks), nameof(Quiet)));
            var navy = Empty<NavyPilot>(); Field(navy, "<ShipUs>k__BackingField", ship);
            var captain = Empty<CondOwner>(); Field(captain, "_strID", "captain"); Field(navy, "<CaptainUs>k__BackingField", captain);
            navy.Blackboard = new Blackboard();
            h.Patch(AccessTools.Method(typeof(NavyPilot), "MaxSpeed"), prefix: new HarmonyMethod(typeof(CeresAuditChecks), nameof(Speed)));
            var target = new ShipSitu(false) { vPosx = .00002 };
            foreach (string faction in new[] { "CCRE", "GalileanConfederacy", "BeltPirates", "Independent" }) {
                owner = faction; budget = 0;
                spots = new List<Tuple<double, ShipSitu>> { Tuple.Create(1d, target) };
                Check(navy.GetTarget() == null, "Single unaffordable target escaped filter");
                spots.Add(Tuple.Create(2d, new ShipSitu(false) { vPosx = .00003 }));
                Check(navy.GetTarget() == null, "All-rejected fallback selected unaffordable target");
            }
            owner = "CCRE";
            home.HideFromSystem = true;
            Check(CeresNavigationGuardrails.FindHome(ship) == null, "Unavailable station accepted as home");
            home.HideFromSystem = false;
            foreach (double fuelBudget in new[] { 0d, 2.4e-9, 4.8e-9, 4.800001e-9, 1e-8, double.NaN, double.PositiveInfinity }) {
                budget = fuelBudget; spots = new List<Tuple<double, ShipSitu>> { Tuple.Create(1d, target) };
                var result = navy.GetTarget();
                Check(fuelBudget > 2.4e-9 && NavigationFuel.Finite(fuelBudget) ? result?.Situ == target : result == null,
                    "General native affordability finite/boundary rejection incorrect");
            }
            budget = 1e-8;
            var unsafeTarget = new ShipSitu(false) { vPosx = .00004, vVelX = 1e-6 };
            spots = new List<Tuple<double, ShipSitu>> { Tuple.Create(.1, unsafeTarget), Tuple.Create(1d, target) };
            Check(navy.GetTarget()?.Situ == target, "Affordable alternative lost to unsafe nearest candidate");
            navy.Blackboard.Set(CeresNavigationGuardrails.RejectedPlanKey, true);
            Check(navy.GetTarget()?.Situ == target, "Obsolete rejection latch still suppresses affordable alternatives");
            navy.Blackboard.Remove(CeresNavigationGuardrails.RejectedPlanKey);
            owner = "Independent"; budget = 3e-9;
            spots = new List<Tuple<double, ShipSitu>> { Tuple.Create(1d, target) };
            Check(navy.GetTarget()?.Situ == target, "Ceres round-trip policy leaked to unrelated faction");
            owner = "CCRE";
            if (reassess) Reassess(h, ship, navy);
            Console.WriteLine("Native Navy single/all-rejected/alternative selection passes with correct faction and reserve boundaries.");

            // Both patrol and combat roles now check at AI dispatch, outside FlyTo.
            foreach (AIType role in new[] { AIType.Navy, AIType.Pirate, AIType.Police, AIType.HaulerCargo }) {
                CeresNavigationGuardrails.ResetRuntimeState(); ship.ClearShipTarget(); ship.objSS.ResetNavData();
                ai = Controller(ship, role: role); budget = 1e-10; StarSystem.fEpoch += 100;
                ai.RunAI();
                Check(role == AIType.HaulerCargo ? !StationReturn.Pending(ai) : StationReturn.Pending(ai), "Role/short-range return coverage incorrect");
            }
            // Fuel prediction uses only unspent route fuel, plus native return estimate and 20% margin.
            ai = Controller(ship); budget = 1e-8; StarSystem.fEpoch = 0; ship.fShallowRCSRemass = 300;
            plan = new NavData(ship); plan.AddNavDataPoint(new NavDataPoint(0, new ShipSitu(false), 300, 0, 0));
            plan.AddNavDataPoint(new NavDataPoint(100, new ShipSitu(false), 100, 0, 0));
            Check(CeresNavigationGuardrails.PlanHasReserve(ship, home, plan, 1e-9), "Affordable planned route rejected");
            Check(homeBody.dXReal == 123 && homeBody.dVelX == 456 && homeBody.dTimeCalcLast == 789, "Reserve check changed shared orbital caches");
            ship.fShallowRCSRemass = 200;
            Check(!CeresNavigationGuardrails.PlanHasReserve(ship, home, plan, 1e-9), "Unsafe planned fuel estimate accepted");
            StarSystem.fEpoch = 50; ship.fShallowRCSRemass = 200;
            Check(CeresNavigationGuardrails.PlanHasReserve(ship, home, plan, 1e-9), "Already spent route fuel counted twice");
            ship.fShallowRCSRemass = 400;
            Check(CeresNavigationGuardrails.PlanHasReserve(ship, home, plan, 1e-9), "Refill incorrectly increased unspent route cost");
            ship.fShallowRCSRemass = 144;
            Check(!CeresNavigationGuardrails.PlanHasReserve(ship, home, plan, 1e-9), "Exact 20% planned reserve boundary accepted");
            ship.fShallowRCSRemass = 144.001;
            Check(CeresNavigationGuardrails.PlanHasReserve(ship, home, plan, 1e-9), "Fuel above planned reserve boundary rejected");
            plan.Destination.FuelLevel = double.NaN;
            Check(!CeresNavigationGuardrails.PlanHasReserve(ship, home, plan, 1e-9), "Invalid planned fuel accepted");
            plan.Destination.FuelLevel = 100;
            plan.Destination.ArrivalTime = double.PositiveInfinity;
            Check(!CeresNavigationGuardrails.PlanHasReserve(ship, home, plan, 1e-9), "Invalid arrival projected into native ephemeris");
            plan.Destination.ArrivalTime = 100;
            plan.Origin.TorchFuelLevel = 1000; plan.Destination.TorchFuelLevel = 900;
            Check(CeresNavigationGuardrails.PlanHasReserve(ship, home, plan, 1e-9), "Torch plan subjected to RCS-only reserve");
            ship.fFusionThrustMax = 1; ship.fShallowFusionRemain = 900;
            ai = Controller(ship); ship.objSS.NavData = plan; ship.ClearShipTarget();
            budget = 1e-12; CeresNavigationGuardrails.ResetRuntimeState(); ai.RunAI();
            Check(!StationReturn.Pending(ai), "Usable torch journey redirected by RCS guard");
            ship.objSS.ResetNavData(); ship.fFusionThrustMax = 0; ship.fShallowFusionRemain = 0;
            // Native short-range combat enters through AIShip.RunAI too. Check
            // cadence, normal time advance and a save/epoch rewind explicitly.
            ai = Controller(ship); ship.ClearShipTarget(); budget = 1e-8;
            CeresNavigationGuardrails.ResetRuntimeState(); StarSystem.fEpoch = 1000; ai.RunAI();
            budget = 1e-12; StarSystem.fEpoch = 1029; ai.RunAI();
            Check(!StationReturn.Pending(ai), "30-second reserve throttle failed");
            ship.shipCombatTarget = other; ai.Blackboard.Set("InCombat", true);
            StarSystem.fEpoch = 1030; ai.RunAI();
            Check(StationReturn.Pending(ai), "Short-range combat reserve breach did not retreat");
            StationReturn.Cancel(ai); ship.ClearShipTarget(); ship.shipCombatTarget = null;
            budget = 1e-8; StarSystem.fEpoch = 1060; ai.RunAI();
            budget = 1e-12; StarSystem.fEpoch = 500; ai.RunAI();
            Check(StationReturn.Pending(ai), "Epoch rewind blocked return check");
            // Use the real native mass/efficiency conversion as an independent
            // control for the planned reserve gate after its arithmetic fixtures.
            h.Unpatch(AccessTools.Method(typeof(Ship), "CalculateRCSFuelConsumption", new[] { typeof(double) }), HarmonyPatchType.Prefix, h.Id);
            h.Patch(AccessTools.PropertyGetter(typeof(Ship), "RCSAccelMax"), prefix: new HarmonyMethod(typeof(CeresAuditChecks), nameof(Accel)));
            Field(ship, "fRCSCount", 12f); Field(ship, "fFuelEfficiencyMod", .75f);
            StarSystem.fEpoch = 50; plan.Origin.TorchFuelLevel = plan.Destination.TorchFuelLevel = 0;
            ship.fShallowRCSRemass = 300;
            Check(CeresNavigationGuardrails.PlanHasReserve(ship, home, plan, 1e-9), "Native fuel conversion rejected funded return");
            ship.fShallowRCSRemass = 250;
            Check(!CeresNavigationGuardrails.PlanHasReserve(ship, home, plan, 1e-9), "Native fuel conversion accepted insufficient return reserve");
            h.Unpatch(AccessTools.PropertyGetter(typeof(Ship), "RCSAccelMax"), HarmonyPatchType.Prefix, h.Id);
            // Native getter really emits NaN without RCS; general postfix turns it into usable zero.
            h.Unpatch(AccessTools.PropertyGetter(typeof(Ship), "DeltaVRemainingRCS"), HarmonyPatchType.Prefix, h.Id);
            if (reassess) {
                h.Patch(AccessTools.PropertyGetter(typeof(Ship), "RCSAccelMax"), prefix: new HarmonyMethod(typeof(CeresAuditChecks), nameof(Accel)));
                Field(ship, "fRCSCount", 12f); ship.fShallowRCSRemass = 300;
                foreach (double efficiency in new[] { 1d, .75d }) {
                    Field(ship, "fFuelEfficiencyMod", efficiency);
                    h.Unpatch(AccessTools.PropertyGetter(typeof(Ship), "DeltaVRemainingRCS"), HarmonyPatchType.Postfix, h.Id);
                    double nativeBudget = ship.DeltaVRemainingRCS;
                    Check(Math.Abs(ship.CalculateRCSFuelConsumption(nativeBudget) - 300 * efficiency) < 1e-5,
                        "Native remaining delta-v baseline did not reproduce the efficiency mismatch");
                    h.CreateClassProcessor(typeof(InvalidRcsBudgetPatch)).Patch();
                    double advertised = ship.DeltaVRemainingRCS;
                    double billed = ship.CalculateRCSFuelConsumption(advertised);
                    Check(Math.Abs(billed - ship.GetRCSRemain()) < 1e-5, "Corrected delta-v does not invert native fuel consumption");
                    Console.WriteLine("Corrected efficiency " + efficiency + ": advertised budget costs " + billed + " of 300 kg.");
                }
                foreach (double invalid in new[] { 0d, -1d, double.NaN, double.PositiveInfinity }) {
                    Field(ship, "fFuelEfficiencyMod", invalid);
                    Check(ship.DeltaVRemainingRCS == 0, "Invalid efficiency created usable fuel capability");
                }
                // Full/Edit canister enumeration is a fuel-source fixture;
                // the getter, efficiency conversion and native cost run unchanged.
                Patch(h, typeof(Ship), nameof(Ship.GetRCSRemain), nameof(InventoryFuel));
                foreach (Ship.Loaded state in new[] { Ship.Loaded.Shallow, Ship.Loaded.Edit, Ship.Loaded.Full })
                    foreach (bool aiFlag in new[] { false, true })
                        foreach (double fuel in new[] { 0d, 1d, 300d })
                            foreach (double efficiency in new[] { 1d, .75d }) {
                                Field(ship, "nLoadState", state); ship.IsAIShip = aiFlag;
                                ship.fShallowRCSRemass = fuel; Field(ship, "fFuelEfficiencyMod", efficiency);
                                Check(Math.Abs(ship.CalculateRCSFuelConsumption(ship.DeltaVRemainingRCS) - fuel) < 1e-5 && ship.fShallowRCSRemass == fuel,
                                    "Budget inverse failed or changed fuel across materialization/player/skill boundaries");
                            }
                h.Unpatch(AccessTools.Method(typeof(Ship), nameof(Ship.GetRCSRemain)), HarmonyPatchType.Prefix, h.Id);
                Field(ship, "nLoadState", Ship.Loaded.Shallow); ship.IsAIShip = true; ship.fShallowRCSRemass = 300;
                Field(ship, "fFuelEfficiencyMod", 1d);
                plan = RouteFor(new TargetNewIfNone(ai.AICharacter), affordableVisit);
                StarSystem.fEpoch = plan.Origin.ArrivalTime;
                var near = new ShipSitu(false);
                spots = new List<Tuple<double, ShipSitu>> { Tuple.Create(1d, near) };
                navy.Blackboard = new Blackboard();
                Check(CeresNavigationGuardrails.LacksRoundTripReserve(ship, near, home, 1e-9) && navy.GetTarget()?.Situ == near &&
                    CeresNavigationGuardrails.PlanHasReserve(ship, home, plan, 1e-9),
                    "A funded short-hop candidate was still vetoed by the extra pre-plan reserve heuristic");
                Console.WriteLine("Funded short-hop candidate reaches native planning despite failing the removed extra cruise-speed prefilter.");
                h.Unpatch(AccessTools.PropertyGetter(typeof(Ship), "RCSAccelMax"), HarmonyPatchType.Prefix, h.Id);
            }
            h.Patch(AccessTools.PropertyGetter(typeof(Ship), "RCSAccelMax"), prefix: new HarmonyMethod(typeof(CeresAuditChecks), nameof(ZeroAccel)));
            Field(ship, "fRCSCount", 0f); ship.fShallowRCSRemass = 100;
            Check(ship.DeltaVRemainingRCS == 0 && AIShipManager.BingoFuelCheck(ship, home, 1e-9), "Zero-thruster NaN still bypasses fuel checks");
            Check(CeresNavigationGuardrails.CannotReturnWithRCS(ship, home, 1e-9), "Stranded capability check disagrees");
            ship.fFusionThrustMax = 1; ship.fShallowFusionRemain = 1;
            Check(!CeresNavigationGuardrails.CannotReturnWithRCS(ship, home, 1e-9), "Pruner lost usable torch exemption");
            StationReturn.Cancel(ai); new TargetData(home).ApplyToShip(ship);
            foreach (string faction in new[] { "Independent", "CCRE" }) {
                owner = faction;
                for (int i = 0; i < 10; i++) ai.RunAI();
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 1000; i++) ai.RunAI();
                allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
                Check(allocated == 0, "Idle " + faction + " navigation hooks allocated " + allocated + " bytes");
            }
            Console.WriteLine(checks + " Ceres/navigation repair regression checks passed.");
        } finally { combatRetreat = null; h.UnpatchSelf(); CeresNavigationGuardrails.ResetRuntimeState(); }
    }
    static bool Speed(ref double __result) { __result = 1e-9; return false; }
    static bool ZeroAccel(ref double __result) { __result = 0; return false; }
    static bool Accel(ref double __result) { __result = 1e-10; return false; }
    static bool InventoryFuel(Ship __instance, ref double __result) { __result = __instance.fShallowRCSRemass; return false; }
    static bool Cost(double dVdiff, ref double __result) { __result = dVdiff * 1e10; return false; }

    static Ship expensiveVisit, affordableVisit;
    static int candidateDraws, expensivePlans, affordablePlans, homePlans;
    static bool rejectAll, failHome;
    static bool Draw(ref TargetData __result) {
        __result = new TargetData(++candidateDraws == 1 || rejectAll ? expensiveVisit : affordableVisit);
        return false;
    }
    static bool Route(TargetData target, TargetNewIfNone __instance, ref NavData __result) {
        if (target.Ship == expensiveVisit) expensivePlans++;
        else if (target.Ship == affordableVisit) affordablePlans++;
        else if (target.Ship == home) homePlans++;
        if (target.Ship == home && failHome) { __result = null; return false; }
        __result = new NavData(__instance.ShipUs);
        __result.AddNavDataPoint(new NavDataPoint(StarSystem.fEpoch, new ShipSitu(false), 300, 0, 0));
        __result.AddNavDataPoint(new NavDataPoint(StarSystem.fEpoch + 100, new ShipSitu(false), target.Ship == expensiveVisit ? 50 : 280, 0, 0));
        return false;
    }
    static bool Capacity(ref double __result) { __result = 400; return false; }
    static bool ManualCost(ref double __result) { __result = 5; return false; }
    static void Reassess(Harmony h, Ship ship, NavyPilot navy) {
        // Native bounded retries, reserve hooks, tactical retreat, dispatch and
        // fuel predicate execute. Candidate order, generated trajectories, tree
        // combat gate and physical FlyTo/Dock outcomes are fixture boundaries.
        expensiveVisit = ShipAt("expensive-visit"); expensiveVisit.objSS.bIsBO = true;
        affordableVisit = ShipAt("affordable-visit"); affordableVisit.objSS.bIsBO = true;
        var ai = Controller(ship);
        Field(ai, "_aiCharacter", navy); navy.Blackboard = ai.Blackboard;
        ship.fShallowRCSRemass = 300; budget = 1e-8;
        Patch(h, typeof(NavyPilot), nameof(NavyPilot.GetTarget), nameof(Draw));
        Patch(h, typeof(TargetNewIfNone), "GenerateNavData", nameof(Route));
        var selector = new TargetNewIfNone(navy);
        var dto = AccessTools.Method(typeof(TargetNewIfNone), "FindTarget").Invoke(selector, new object[] { });
        var selected = (TargetData)AccessTools.Field(dto.GetType(), "TargetData").GetValue(dto);
        Check(expensivePlans == 1 && candidateDraws == 2 && selected.Ship == affordableVisit && homePlans == 0,
            "First rejected plan still forces home instead of trying an affordable alternative");
        Check(affordablePlans == 1, "Affordable alternative was not planned exactly once");
        Check(CeresNavigationGuardrails.PlanHasReserve(ship, home, RouteFor(selector, affordableVisit), 1e-9),
            "Alternative fixture does not satisfy the production reserve gate");
        rejectAll = true; candidateDraws = expensivePlans = affordablePlans = homePlans = 0;
        dto = AccessTools.Method(typeof(TargetNewIfNone), "FindTarget").Invoke(selector, new object[] { });
        Check(candidateDraws == 9 && expensivePlans == 9 && homePlans == 1 && (TargetData)AccessTools.Field(dto.GetType(), "TargetData").GetValue(dto) is TargetData fallback && fallback.Ship == home,
            "Exhausted native attempts did not fall back home once within bounded work");
        failHome = true; candidateDraws = expensivePlans = affordablePlans = homePlans = 0;
        dto = AccessTools.Method(typeof(TargetNewIfNone), "FindTarget").Invoke(selector, new object[] { });
        Check(candidateDraws == 9 && homePlans == 1 && AccessTools.Field(dto.GetType(), "TargetData").GetValue(dto) == null,
            "Unreachable home was fabricated into a usable route");
        rejectAll = failHome = false; owner = "Independent"; candidateDraws = 0;
        dto = AccessTools.Method(typeof(TargetNewIfNone), "FindTarget").Invoke(selector, new object[] { });
        Check(((TargetData)AccessTools.Field(dto.GetType(), "TargetData").GetValue(dto)).Ship == expensiveVisit && candidateDraws == 1,
            "Ceres planned reserve policy leaked to another faction");
        owner = "CCRE";
        Patch(h, typeof(TargetNewIfNone), "GetNeededFuel", nameof(ManualCost));
        var manual = AccessTools.Method(typeof(TargetNewIfNone), "GetNeededFuel");
        var freeGoal = new TargetData(new ShipSitu(false));
        budget = 4e-9;
        Check((double)manual.Invoke(selector, new object[] { freeGoal }) < 0, "Unlocked patrol bypassed its homeward reserve");
        budget = 1e-8;
        Check((double)manual.Invoke(selector, new object[] { freeGoal }) == 5, "Affordable unlocked patrol was rejected");
        owner = "Independent"; budget = 4e-9;
        Check((double)manual.Invoke(selector, new object[] { freeGoal }) == 5, "Unlocked reserve policy leaked to another faction");
        owner = "CCRE"; budget = 1e-8;
        h.Unpatch(manual, HarmonyPatchType.Prefix, h.Id);
        h.Unpatch(AccessTools.Method(typeof(NavyPilot), nameof(NavyPilot.GetTarget)), HarmonyPatchType.Prefix, h.Id);
        h.Unpatch(AccessTools.Method(typeof(TargetNewIfNone), "GenerateNavData"), HarmonyPatchType.Prefix, h.Id);
        navy.Blackboard.Remove(CeresNavigationGuardrails.RejectedPlanKey);
        Console.WriteLine("Native selection tries a funded alternative; nine rejected candidates fall back home once; unreachable home stays rejected.");

        ai = Controller(ship); ship.ClearShipTarget(); ship.objSS.ResetNavData();
        home.objSS.vVelX = 1e-8; budget = 8e-9;
        Patch(h, typeof(Ship), nameof(Ship.GetRCSMax), nameof(Capacity));
        Check(!AIShipManager.LowFuel(ship, 1e-9) && AIShipManager.BingoFuelCheck(ship, home, 1e-9),
            "Chase fixture must be above native low-fuel thresholds but below homeward reserve");
        h.Unpatch(AccessTools.Method(typeof(LowFuelRetreat), nameof(LowFuelRetreat.RunCommand)), HarmonyPatchType.Transpiler, h.Id);
        Check(new LowFuelRetreat(ai.AICharacter).RunCommand(ai.Blackboard).Status == CommandCode.Failure, "Native baseline no longer misses homeward reserve");
        h.CreateClassProcessor(typeof(CeresCombatRetreat)).Patch();
        combatRetreat = new LowFuelRetreat(ai.AICharacter); flyRunning = true;
        ai.Blackboard.Set("InCombat", true); CeresNavigationGuardrails.ResetRuntimeState();
        int flightBefore = flyCalls, combatBefore = nativeCalls;
        for (int i = 0; i < 100; i++) { StarSystem.fEpoch += 30; ai.RunAI(); }
        Check(StationReturn.Pending(ai) && flyCalls == flightBefore + 100 && nativeCalls == combatBefore + 100 && ship.shipScanTarget == home,
            "Native combat branch did not execute tactical homeward retreat");
        var intruder = ShipAt("interruption-target"); new TargetData(intruder).ApplyToShip(ship);
        combatRetreat.RunCommand(ai.Blackboard);
        Check(ship.shipScanTarget == home, "Resumed native retreat followed an intervening command's target");
        var runRetreat = AccessTools.Method(typeof(LowFuelRetreat), nameof(LowFuelRetreat.RunCommand));
        h.Unpatch(runRetreat, HarmonyPatchType.Transpiler, h.Id);
        h.CreateClassProcessor(typeof(CeresCombatRetreat)).Patch();
        var baseline = new LowFuelRetreat(ai.AICharacter);
        Field(baseline, "_retreating", true); Field(baseline, "_targetSelected", true);
        flyFailure = true; int beforeBadFlight = dockCalls;
        baseline.RunCommand(ai.Blackboard);
        Check(dockCalls == beforeBadFlight + 1 && (bool)AccessTools.Field(typeof(LowFuelRetreat), "_docking").GetValue(baseline),
            "Native baseline no longer advances a failed approach into docking");
        h.CreateClassProcessor(typeof(LowFuelRetreatProgressPatch)).Patch();
        foreach (string faction in new[] { "CCRE", "Independent" }) {
            owner = faction;
            var retry = new LowFuelRetreat(ai.AICharacter);
            Field(retry, "_retreating", true); Field(retry, "_targetSelected", true);
            beforeBadFlight = dockCalls; retry.RunCommand(ai.Blackboard);
            Check(dockCalls == beforeBadFlight && !(bool)AccessTools.Field(typeof(LowFuelRetreat), "_docking").GetValue(retry),
                "Failed native approach still attempted docking for " + faction);
            flyFailure = false; flyRunning = true; int beforeRetry = flyCalls;
            retry.RunCommand(ai.Blackboard);
            Check(flyCalls == beforeRetry + 1, "Failed approach did not resume native flight for " + faction);
            flyRunning = false; dockFailure = true;
            retry.RunCommand(ai.Blackboard);
            Check(!(bool)AccessTools.Field(typeof(LowFuelRetreat), "_docking").GetValue(retry), "Failed docking did not reset native approach for " + faction);
            dockFailure = false; flyRunning = true; beforeRetry = flyCalls;
            retry.RunCommand(ai.Blackboard);
            Check(flyCalls == beforeRetry + 1, "Failed docking retried docking without a new approach for " + faction);
            flyFailure = true;
        }
        owner = "CCRE"; flyFailure = false;
        flyRunning = false; combatRetreat.RunCommand(ai.Blackboard);
        Check((bool)AccessTools.Field(typeof(LowFuelRetreat), "_docking").GetValue(combatRetreat), "Native retreat never reached docking stage");
        new TargetData(intruder).ApplyToShip(ship); flyRunning = true;
        combatRetreat.RunCommand(ai.Blackboard);
        Check(ship.shipScanTarget == home && !(bool)AccessTools.Field(typeof(LowFuelRetreat), "_docking").GetValue(combatRetreat),
            "Interrupted docking stage was not reset before a new approach");
        home.bDestroyed = true; int beforeLostHome = flyCalls;
        combatRetreat.RunCommand(ai.Blackboard);
        Check(ship.shipScanTarget == null && flyCalls == beforeLostHome && !(bool)AccessTools.Field(typeof(LowFuelRetreat), "_targetSelected").GetValue(combatRetreat),
            "Missing friendly home retained a destroyed goal or selected a different-faction fallback");
        home.bDestroyed = false; combatRetreat.RunCommand(ai.Blackboard);
        Check(ship.shipScanTarget == home && flyCalls == beforeLostHome + 1, "Native retreat failed to retry a restored home");
        var reloaded = Controller(ship, Reload(ai.Blackboard)); combatRetreat = new LowFuelRetreat(reloaded.AICharacter);
        CeresNavigationGuardrails.ResetRuntimeState();
        combatRetreat.RunCommand(reloaded.Blackboard);
        Check(ship.shipScanTarget == home && StationReturn.Pending(reloaded), "Reload lost tactical retreat or ordinary return intent");
        combatRetreat = null; ai = reloaded;
        ai.Blackboard.Set("InCombat", false); int beforeQuiet = flyCalls; ai.RunAI();
        Check(flyCalls == beforeQuiet + 1, "Ordinary return failed to resume after combat ceased");
        owner = "Independent";
        Check(new LowFuelRetreat(ai.AICharacter).RunCommand(ai.Blackboard).Status == CommandCode.Failure,
            "Home-reserve retreat policy leaked to another faction");
        owner = "CCRE"; playerManned = true;
        Check(new LowFuelRetreat(ai.AICharacter).RunCommand(ai.Blackboard).Status == CommandCode.Failure,
            "Home-reserve retreat policy took over a player helm");
        playerManned = false; connected = true;
        Check(new LowFuelRetreat(ai.AICharacter).RunCommand(ai.Blackboard).Status == CommandCode.Failure,
            "Home-reserve retreat policy took over a connected group");
        connected = false;
        var cargo = Controller(ship, role: AIType.HaulerCargo);
        cargo.Blackboard.Set("CeresNavigation.ReturnRouteQueued", true); cargo.RunAI();
        Check(!StationReturn.Pending(cargo) && !cargo.Blackboard.HasKey("CeresNavigation.ReturnRouteQueued"),
            "Obsolete Ceres migration imposed a patrol return on legitimate cargo traffic");
        Check(new LowFuelRetreat(cargo.AICharacter).RunCommand(cargo.Blackboard).Status == CommandCode.Failure,
            "Home-reserve retreat policy leaked to cargo traffic");
        foreach (AIType role in new[] { AIType.Navy, AIType.Pirate, AIType.Police }) {
            ai = Controller(ship, role: role); ship.ClearShipTarget();
            CeresNavigationGuardrails.ResetRuntimeState();
            new LowFuelRetreat(ai.AICharacter).RunCommand(ai.Blackboard);
            Check(ship.shipScanTarget == home, "Native tactical reserve integration missed " + role);
        }
        home.objSS.vVelX = 0; budget = 1e-8; StationReturn.Cancel(ai);
        h.Unpatch(AccessTools.Method(typeof(Ship), nameof(Ship.GetRCSMax)), HarmonyPatchType.Prefix, h.Id);
        Console.WriteLine("Native combat retreat now handles breached home reserve, interrupted approach/docking, missing/restored home and reload.");
    }
    static NavData RouteFor(TargetNewIfNone selector, Ship target) {
        NavData result = null; Route(new TargetData(target), selector, ref result); return result;
    }
}
