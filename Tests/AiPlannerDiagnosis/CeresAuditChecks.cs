using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;
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
    static Ship home;
    static BodyOrbit homeBody;
    static string owner = "CCRE";
    static List<Tuple<double, ShipSitu>> spots;
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Field(object target, string name, object value) => AccessTools.Field(target.GetType(), name).SetValue(target, value);
    static void Check(bool ok, string reason) { checks++; if (!ok) throw new Exception(reason); }
    static bool Quiet() => false;
    static bool No(ref bool __result) { __result = false; return false; }
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
        CrewSim.system.dictShips[id] = ship;
        return ship;
    }
    sealed class NativeProbe : BaseCommand {
        internal NativeProbe(Program.Pilot pilot) : base(pilot) { }
        public override CommandStatus RunCommand(Blackboard board) { nativeCalls++; return Running; }
    }
    static AIShip Controller(Ship ship, Blackboard board = null, AIType role = AIType.Navy) {
        var pilot = new Program.Pilot { ShipUs = ship, Speed = 1e-9, Role = role, Blackboard = board ?? new Blackboard() };
        pilot.PriorityQueue = new QueuedCommandsNode("PriorityQueue", pilot);
        var ai = Empty<AIShip>(); Field(ai, "_shipUs", ship); Field(ai, "_aiCharacter", pilot);
        Field(ai, "<Blackboard>k__BackingField", pilot.Blackboard); Field(ai, "<HomeStation>k__BackingField", home.strRegID);
        Field(ai, "_behaviorTree", new NativeProbe(pilot));
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
    internal static void Run() {
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
            foreach (string method in new[] { "IsDockedWithAStation", "IsDockedWithPlayer", "IsDocked", "IsStation", "IsStationHidden" }) Patch(h, typeof(Ship), method, nameof(No));
            h.Patch(AccessTools.PropertyGetter(typeof(Ship), "NavPlayerManned"), prefix: new HarmonyMethod(typeof(CeresAuditChecks), nameof(PlayerManned)));
            h.Patch(AccessTools.PropertyGetter(typeof(Ship), "HasDockGroup"), prefix: new HarmonyMethod(typeof(CeresAuditChecks), nameof(Connected)));
            h.Patch(AccessTools.PropertyGetter(typeof(Ship), "DeltaVRemainingRCS"), prefix: new HarmonyMethod(typeof(CeresAuditChecks), nameof(DeltaV)));
            h.Patch(AccessTools.Method(typeof(Ship), "CalculateRCSFuelConsumption", new[] { typeof(double) }), prefix: new HarmonyMethod(typeof(CeresAuditChecks), nameof(Cost)));
            Patch(h, typeof(Ship), "StopManeuver", nameof(Quiet));
            Patch(h, typeof(ShipSitu), "UpdateTime", nameof(Quiet));
            Patch(h, typeof(FlyTo), "RunCommand", nameof(Fly));
            Patch(h, typeof(FlyTo), "CanExecute", nameof(Yes));
            Patch(h, typeof(Dock), "RunCommand", nameof(Docked));
            foreach (Type patch in new[] { typeof(InvalidRcsBudgetPatch), typeof(ClearTargetFuelPatch), typeof(RetargetFuelPatch),
                typeof(StationReturnPatch), typeof(StationReturnFastForwardPatch), typeof(NavyCandidateFuelPatch),
                typeof(LegacyReturnLoadPatch),
                typeof(CeresNavigationGuardrails.ReturnGuardPatch), typeof(CeresNavigationGuardrails.PatrolHomePatch),
                typeof(CeresNavigationGuardrails.PlannedReservePatch) }) h.CreateClassProcessor(patch).Patch();

            var ship = ShipAt("audit-patrol"); var ai = Controller(ship);
            ai.Blackboard.Set("TripFuelPrediction", "old-patrol&100");
            StationReturn.Request(ai, home);
            Check(!ai.Blackboard.HasKey("TripFuelPrediction"), "Retarget retains previous fuel prediction");
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
            AssertFlight(ai);
            Check(ai.AICharacter.PriorityQueue.CanExecute(ai.Blackboard), "Return discarded unrelated queued command");
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
                Check(faction == "Independent" ? navy.GetTarget() == null : navy.GetTarget()?.Ship == home, "Single unaffordable target escaped filter");
                spots.Add(Tuple.Create(2d, new ShipSitu(false) { vPosx = .00003 }));
                Check(faction == "Independent" ? navy.GetTarget() == null : navy.GetTarget()?.Ship == home, "All-rejected fallback selected unaffordable target");
            }
            owner = "CCRE";
            home.HideFromSystem = true;
            Check(CeresNavigationGuardrails.FindHome(ship) == null, "Unavailable station accepted as home");
            home.HideFromSystem = false;
            foreach (double fuelBudget in new[] { 0d, 2.4e-9, 4.8e-9, 4.800001e-9, 1e-8, double.NaN, double.PositiveInfinity }) {
                budget = fuelBudget; spots = new List<Tuple<double, ShipSitu>> { Tuple.Create(1d, target) };
                var result = navy.GetTarget();
                Check(fuelBudget > 4.8e-9 && NavigationFuel.Finite(fuelBudget) ? result?.Situ == target : result?.Ship == home,
                    "Round-trip finite/boundary rejection incorrect");
            }
            budget = 1e-8;
            var unsafeTarget = new ShipSitu(false) { vPosx = .00004, vVelX = 1e-6 };
            spots = new List<Tuple<double, ShipSitu>> { Tuple.Create(.1, unsafeTarget), Tuple.Create(1d, target) };
            Check(navy.GetTarget()?.Situ == target, "Affordable alternative lost to unsafe nearest candidate");
            navy.Blackboard.Set(CeresNavigationGuardrails.RejectedPlanKey, true);
            Check(navy.GetTarget()?.Ship == home && !navy.Blackboard.HasKey(CeresNavigationGuardrails.RejectedPlanKey),
                "Rejected real route retried patrol instead of returning home");
            owner = "Independent"; budget = 3e-9;
            spots = new List<Tuple<double, ShipSitu>> { Tuple.Create(1d, target) };
            Check(navy.GetTarget()?.Situ == target, "Ceres round-trip policy leaked to unrelated faction");
            owner = "CCRE";
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
        } finally { h.UnpatchSelf(); CeresNavigationGuardrails.ResetRuntimeState(); }
    }
    static bool Speed(ref double __result) { __result = 1e-9; return false; }
    static bool ZeroAccel(ref double __result) { __result = 0; return false; }
    static bool Accel(ref double __result) { __result = 1e-10; return false; }
    static bool Cost(double dVdiff, ref double __result) { __result = dVdiff * 1e10; return false; }
}
