using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using HarmonyLib;
using Newtonsoft.Json;
using Ostranauts.Core;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using Ostranauts.Ships.Comms;
using Ostranauts.Ships.Sensors;
using Ostrabus.ShipNavigation;

// Native tree, sticky sequence, detection, initiation and neutralization execute
// unchanged. Unity/world boundaries and movement output are controlled fixtures.
internal static class Program {
    static int checks, cleanupCalls, homeCalls, selectionCalls, flightCalls;
    static CondOwner captain;
    static Ship flightTarget, home;
    static double range = 1e-8;
    static Dictionary<Ship, ShipSignature> visible = new();

    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Set(object target, string field, object value) => AccessTools.Field(target.GetType(), field).SetValue(target, value);
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = ReferenceEquals(__0, __1); return false; }
    static bool Unequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = !ReferenceEquals(__0, __1); return false; }
    static bool Quiet() => false;
    static bool Owner(ref string __result) { __result = "fixture"; return false; }
    static bool Captain(out CondOwner __0, ref bool __result) { __0 = captain; __result = true; return false; }
    static bool Visible(ref Dictionary<Ship, ShipSignature> __result) { __result = visible; return false; }
    static bool Hostile(ref float __result) { __result = -100; return false; }
    static bool Factions(ref List<string> __result) { __result = new List<string> { "hostile" }; return false; }
    static bool Range(ref double __result) { __result = range; return false; }
    static bool NotMoored(ref bool __result) { __result = false; return false; }
    static bool NoController(ref AIShip __result) { __result = null; return false; }
    static bool CanFly(ref bool __result) { __result = true; return false; }
    static bool Fly(FlyTo __instance, ref CommandStatus __result) {
        flightCalls++;
        flightTarget = __instance.ShipUs.shipScanTarget;
        __result = new CommandStatus(CommandCode.Running, __instance);
        return false;
    }
    static void ObserveCleanup() { cleanupCalls++; }
    static void ObserveSelection() { selectionCalls++; }
    static void ObserveHome() { homeCalls++; }
    static bool Nearest(ref Ship __result) { __result = home; return false; }
    static bool NoRegionalAlternatives(ref List<string> __result) { __result = new List<string>(); return false; }
    static bool Leaf(BaseCommand __instance, ref CommandStatus __result) {
        var type = __instance.GetType();
        CommandCode result = type == typeof(TurnOnAllSensors) || type == typeof(TurnOffAllActiveSensors) ||
            type == typeof(AmIArmed) || type == typeof(SetEngagementProfile) || type == typeof(DoIPreferShortRange)
            ? CommandCode.Finished : CommandCode.Failure;
        if (type == typeof(KeepWithinFiringDistance) || type == typeof(KeepAtDistance)) result = CommandCode.Running;
        __result = new CommandStatus(result, __instance);
        return false;
    }
    static bool HasCond(ref bool __result) { __result = false; return false; }
    static Ship Hull(string id) {
        var ship = Empty<Ship>(); ship.strRegID = id;
        Set(ship, "<objSS>k__BackingField", new ShipSitu(false));
        Set(ship, "<Comms>k__BackingField", Empty<Comms>());
        return ship;
    }
    static StickySequenceCommand MainBranch(NavyPilot pilot) => (StickySequenceCommand)
        ((List<ICommand>)AccessTools.Field(typeof(SelectorCommand), "_childCommands").GetValue(pilot.BehaviorTree)).Last();
    static AIShip Controller(NavyPilot pilot) {
        var ai = Empty<AIShip>(); Set(ai, "_shipUs", pilot.ShipUs); Set(ai, "_aiCharacter", pilot);
        Set(ai, "_behaviorTree", pilot.BehaviorTree); Set(ai, "<Blackboard>k__BackingField", pilot.Blackboard);
        return ai;
    }
    static NavyPilot Pilot(bool admitted) {
        var ship = Hull("navy"); ship.shipSituTarget = new ShipSitu(false) { vPosx = .001 };
        var pilot = new NavyPilot(null, ship); MainBranch(pilot).SaveData = new[] { "2" };
        if (admitted) Controller(pilot).AddCommand(new FlyToPatrol(pilot));
        Check(!pilot.PriorityQueue.CanExecute(pilot.Blackboard), "Fixture has an overriding queued command");
        return pilot;
    }

    static void ApplyRepair(NavyPilot pilot) {
        string originalSave = JsonConvert.SerializeObject(pilot.BehaviorTree.GetSaveData(""));
        CombatCleanupAdmission.Configure(pilot);
        var branches = (List<ICommand>)AccessTools.Field(typeof(SelectorCommand), "_childCommands").GetValue(pilot.BehaviorTree);
        var combat = (SequenceCommand)branches[1];
        var gates = (List<ICommand>)AccessTools.Field(typeof(SequenceCommand), "_childCommands").GetValue(combat);
        Check(gates.Count == 4 && gates[0].GetType() == typeof(ConditionCommand) && gates[1].GetType() == typeof(ConditionCommand) &&
            gates[3].FindCommand(typeof(IsTargetNeutralized)) != null, "Production adapters are missing");
        Check(JsonConvert.SerializeObject(pilot.BehaviorTree.GetSaveData("")) == originalSave,
            "Local gate wrappers changed native saved command paths or payloads");
        Check(!CombatCleanupAdmission.Configure(pilot), "Production gate configuration is not idempotent");
    }

    static void Scenario(bool admitted, bool savedStale, bool fixedGate) {
        var pilot = Pilot(admitted); var ship = pilot.ShipUs; var target = Hull("disabled-pirate");
        if (fixedGate) ApplyRepair(pilot);
        target.IsAIShip = true; Set(target, "nLoadState", Ship.Loaded.Shallow);
        visible = new Dictionary<Ship, ShipSignature>();
        pilot.Blackboard.Set("Enemies", new List<Ship> { target }); pilot.Blackboard.Set("ClosestEnemy", target);
        Check(new InitiateAgainstBiggestThreat(pilot).RunCommand(pilot.Blackboard).Status == CommandCode.Finished,
            "Native combat initiation failed");
        Check(ship.shipScanTarget == target && ship.shipSituTarget == target.objSS && ship.shipCombatTarget == target,
            "Native initiation did not overwrite the shared mission destination");
        if (savedStale) {
            pilot.Blackboard.Set("InCombat", false);
            ship.shipSituTarget = new ShipSitu(false) { vPosx = .004, vPosy = .002 };
        }
        int beforeCleanup = cleanupCalls, beforeHome = homeCalls, beforeFlight = flightCalls, beforeSelection = selectionCalls;
        for (int i = 0; i < 3; i++) pilot.BehaviorTree.RunCommand(pilot.Blackboard);
        if (!fixedGate) {
            Check(cleanupCalls == beforeCleanup && homeCalls == beforeHome, "Lost-contact combat unexpectedly reached native cleanup");
            Check(flightCalls == beforeFlight + 3 && flightTarget == target, "Patrol did not chase abandoned combat destination");
            Check(!pilot.Blackboard.Get<bool>("InCombat") && pilot.Blackboard.Get<List<Ship>>("Combatants").Contains(target),
                "Native lost-contact path did not leave contradictory combat bookkeeping");
            Check(selectionCalls == beforeSelection && MainBranch(pilot).SaveData[0] == "2", "Sticky patrol unexpectedly reset target selection");
        } else {
            Check(cleanupCalls == beforeCleanup + 1 && homeCalls == beforeHome + 1, "Production repair did not run one native postcombat handoff");
            Check(!pilot.Blackboard.HasKey("Combatants") && ship.shipCombatTarget == null, "Native cleanup did not clear abandoned engagement");
            Check(ship.shipScanTarget == home && flightTarget == home, "Patrol did not inherit native chosen return destination");
            Check(ship.targetRegID == home.strRegID && ship.TargetData?.Ship == home && ship.shipSituTarget == home.objSS,
                "Native postcombat TargetData did not bind live station identity consistently");
        }
        Console.WriteLine($"{(admitted ? "admitted" : "ordinary")} / {(savedStale ? "saved false flag" : "live lost contact")} / {(fixedGate ? "production gate" : "native gate")}: passed");
    }

    sealed class UrgentMarker : BaseCommand {
        internal int Calls;
        public override CommandStatus RunCommand(Blackboard board) { Calls++; return Running; }
    }
    static void Controls() {
        var pilot = Pilot(true); ApplyRepair(pilot);
        var ship = pilot.ShipUs;
        ship.shipScanTarget = home; ship.shipSituTarget = home.objSS;
        var historical = Hull("old-reference-only"); ship.shipCombatTarget = historical;
        visible.Clear(); int beforeCleanup = cleanupCalls, beforeHome = homeCalls;
        pilot.BehaviorTree.RunCommand(pilot.Blackboard);
        Check(cleanupCalls == beforeCleanup && homeCalls == beforeHome && flightTarget == home && ship.shipCombatTarget == historical,
            "Dormant combat reference changed valid noncombat station navigation");

        // Full/non-AI actors are intentionally retained by the acquisition fix.
        // Use one as an active opponent so the native combat branch must fight.
        var active = Hull("active-enemy"); active.IsAIShip = true; Set(active, "nLoadState", Ship.Loaded.Full);
        visible[active] = Empty<ShipSignature>();
        pilot.Blackboard.Set("Enemies", new List<Ship> { active }); pilot.Blackboard.Set("ClosestEnemy", active);
        ship.shipCombatTarget = null;
        new InitiateAgainstBiggestThreat(pilot).RunCommand(pilot.Blackboard);
        int beforeFlight = flightCalls;
        var result = pilot.BehaviorTree.RunCommand(pilot.Blackboard);
        Check(result.Status == CommandCode.Running && ship.shipCombatTarget == active && homeCalls == beforeHome && flightCalls == beforeFlight,
            "Local gate correction diverted an ongoing fight into patrol/return");
        Check(pilot.Blackboard.Get<List<Ship>>("Combatants").SequenceEqual(new[] { active }) && pilot.Blackboard.Get<bool>("InCombat"),
            "Ongoing fight bookkeeping changed");

        var disabled = Hull("disabled-enemy"); disabled.IsAIShip = true; Set(disabled, "nLoadState", Ship.Loaded.Shallow);
        visible[disabled] = Empty<ShipSignature>();
        pilot.Blackboard.Set("Combatants", new List<Ship> { disabled, active }); ship.shipCombatTarget = disabled;
        result = pilot.BehaviorTree.RunCommand(pilot.Blackboard);
        Check(result.Status == CommandCode.Running && ship.shipCombatTarget == active && homeCalls == beforeHome,
            "Mixed encounter dropped a surviving opponent or forced a station return");
        Check(pilot.Blackboard.Get<List<Ship>>("Combatants").SequenceEqual(new[] { active }), "Mixed native neutralization changed");

        var urgent = new UrgentMarker(); pilot.PriorityQueue.EnqueueUrgentCommand(urgent);
        visible.Clear(); beforeCleanup = cleanupCalls;
        result = pilot.BehaviorTree.RunCommand(pilot.Blackboard);
        Check(result.Status == CommandCode.Running && urgent.Calls == 1 && cleanupCalls == beforeCleanup && homeCalls == beforeHome,
            "Local gates preempted a genuine urgent command");
        Console.WriteLine("Production controls: peaceful return, dormant reference, active fight, mixed fight, urgent priority and unchanged save payloads passed.");
    }

    static void LayoutGuard() {
        var pilot = Pilot(false);
        var branches = (List<ICommand>)AccessTools.Field(typeof(SelectorCommand), "_childCommands").GetValue(pilot.BehaviorTree);
        var gates = (List<ICommand>)AccessTools.Field(typeof(SequenceCommand), "_childCommands").GetValue(branches[1]);
        var admission = gates[0]; var scan = gates[1];
        gates[2] = new TurnOffAllActiveSensors(pilot);
        bool refused = false;
        try { CombatCleanupAdmission.Configure(pilot); } catch (InvalidOperationException) { refused = true; }
        Check(refused && ReferenceEquals(gates[0], admission) && ReferenceEquals(gates[1], scan),
            "Incompatible native tree was partially changed rather than rejected");
        Check(!CombatCleanupAdmission.Configure(null), "Null controller was not ignored");
    }

    static int Main(string[] args) {
        var fixture = new Harmony("offline.patrol-target-handoff.fixture");
        var production = new Harmony("offline.patrol-target-handoff.production");
        void Patch(Type type, string method, string prefix = null, string postfix = null, Type[] parameters = null) => fixture.Patch(
            AccessTools.Method(type, method, parameters), prefix == null ? null : new HarmonyMethod(typeof(Program), prefix),
            postfix == null ? null : new HarmonyMethod(typeof(Program), postfix));
        try {
            CrewSim.system = Empty<StarSystem>(); captain = Empty<CondOwner>(); captain.bAlive = true; home = Hull("native-nearest-station");
            Patch(typeof(UnityEngine.Object), "op_Equality", nameof(Equal)); Patch(typeof(UnityEngine.Object), "op_Inequality", nameof(Unequal));
            Patch(typeof(StarSystem), nameof(StarSystem.GetShipOwner), nameof(Owner));
            Patch(typeof(Comms), nameof(Comms.GetCaptain), nameof(Captain));
            Patch(typeof(CondOwner), nameof(CondOwner.HasCond), nameof(HasCond), parameters: new[] { typeof(string) });
            Patch(typeof(DataHandler), nameof(DataHandler.GetCondTrigger), nameof(Quiet), parameters: new[] { typeof(string) });
            Patch(typeof(BaseCommand), "GetVisibleShipSignatures", nameof(Visible));
            Patch(typeof(CondOwner), nameof(CondOwner.GetFactionScore), nameof(Hostile), parameters: new[] { typeof(List<string>) });
            Patch(typeof(CondOwner), nameof(CondOwner.GetAllFactions), nameof(Factions));
            Patch(typeof(Ship), nameof(Ship.GetRangeTo), nameof(Range), parameters: new[] { typeof(Ship) });
            Patch(typeof(Ship), nameof(Ship.IsMoored), nameof(NotMoored));
            Patch(typeof(AIShipManager), nameof(AIShipManager.GetAIShipByRegID), nameof(NoController));
            Patch(typeof(FlyTo), nameof(FlyTo.RunCommand), nameof(Fly)); Patch(typeof(FlyTo), nameof(FlyTo.CanExecute), nameof(CanFly));
            Patch(typeof(IsTargetNeutralized), nameof(IsTargetNeutralized.RunCommand), nameof(ObserveCleanup));
            Patch(typeof(TargetNewIfNone), nameof(TargetNewIfNone.RunCommand), nameof(ObserveSelection));
            Patch(typeof(TargetNearestStation), nameof(TargetNearestStation.RunCommand), nameof(ObserveHome));
            Patch(typeof(StarSystem), nameof(StarSystem.GetNearestStationRegional), nameof(Nearest));
            foreach (var method in typeof(StarSystem).GetMethods().Where(method => method.Name == nameof(StarSystem.GetNearestStation)))
                fixture.Patch(method, prefix: new HarmonyMethod(typeof(Program), nameof(Nearest)));
            Patch(typeof(AIShipManager), nameof(AIShipManager.GetAllStationsInATCRegion), nameof(NoRegionalAlternatives));
            foreach (Type type in new[] { typeof(TurnOnAllSensors), typeof(TurnOffAllActiveSensors), typeof(IsFriendlyUnderAttack),
                typeof(AmIDamaged), typeof(DoIHaveDecoys), typeof(SurrenderDemand), typeof(AmIArmed), typeof(AmIBeingTargeted),
                typeof(SetEngagementProfile), typeof(DoIPreferShortRange), typeof(KeepWithinFiringDistance), typeof(KeepAtDistance) })
                Patch(type, "RunCommand", nameof(Leaf));

            Console.WriteLine("Untouched native tree:");
            foreach (bool saved in new[] { false, true }) Scenario(false, saved, false);
            production.CreateClassProcessor(typeof(PatrolAdmissionPatch)).Patch();
            Console.WriteLine("Production patrol admission:");
            foreach (bool saved in new[] { false, true }) Scenario(true, saved, false);
            production.CreateClassProcessor(typeof(NeutralizedThreatPatch)).Patch();
            Console.WriteLine("Production acquisition filter also installed:");
            foreach (bool admitted in new[] { false, true }) foreach (bool saved in new[] { false, true }) Scenario(admitted, saved, false);
            LayoutGuard();
            Console.WriteLine("Production gate configuration:");
            foreach (bool admitted in new[] { false, true }) foreach (bool saved in new[] { false, true }) Scenario(admitted, saved, true);
            Controls();
            production.CreateClassProcessor(typeof(CombatCleanupAdmissionPatch)).Patch();
            var constructed = Pilot(false);
            Check(constructed.BehaviorTree.FindCommand(typeof(AmIInCombat)) == null &&
                !CombatCleanupAdmission.Configure(constructed), "Constructor postfix did not install the production adapters");
            Console.WriteLine("Actual production constructor postfix:");
            foreach (bool admitted in new[] { false, true }) foreach (bool saved in new[] { false, true }) Scenario(admitted, saved, true);
            Controls();
            Console.WriteLine(checks + " native baseline and production combat handoff checks passed.");
            return 0;
        } finally { production.UnpatchSelf(); fixture.UnpatchSelf(); }
    }
}
