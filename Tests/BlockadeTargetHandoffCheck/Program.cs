using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ostranauts.Core;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using Ostranauts.Ships.Comms;
using Ostranauts.Ships.Sensors;
using Ostrabus.ShipNavigation;

// Actual native Blockade construction and command execution. Unity/world output
// boundaries are fixtures. The production constructor patch is enabled only
// after the native baseline has demonstrated the defective handoff.
internal static partial class Program {
    static int checks, cleanupCalls, homeCalls, flightCalls, warnings, scans;
    static CondOwner captain;
    static Ship home, flightTarget;
    static double range = 1e-8;
    static string nativeSave;
    static bool armed = true;
    static readonly Dictionary<string, string> owners = new();
    static Dictionary<Ship, ShipSignature> visible = new();

    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Set(object target, string field, object value) => AccessTools.Field(target.GetType(), field).SetValue(target, value);
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = ReferenceEquals(__0, __1); return false; }
    static bool Unequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = !ReferenceEquals(__0, __1); return false; }
    static bool Quiet() => false;
    static bool Owner(string __0, ref string __result) { __result = owners.TryGetValue(__0, out string owner) ? owner : "blockading-faction"; return false; }
    static bool Captain(out CondOwner __0, ref bool __result) { __0 = captain; __result = true; return false; }
    static bool Visible(ref Dictionary<Ship, ShipSignature> __result) { scans++; __result = visible; return false; }
    static bool Range(ref double __result) { __result = range; return false; }
    static bool NotMoored(ref bool __result) { __result = false; return false; }
    static int controllerLookups;
    static readonly Dictionary<string, AIShip> controllers = new();
    static bool NoController(string __0, ref AIShip __result) {
        controllerLookups++; controllers.TryGetValue(__0, out __result); return false;
    }
    static bool HasCond(ref bool __result) { __result = false; return false; }
    static bool CanFly(ref bool __result) { __result = true; return false; }
    static bool Fly(FlyTo __instance, ref CommandStatus __result) {
        flightCalls++; flightTarget = __instance.ShipUs.shipScanTarget;
        __result = new CommandStatus(CommandCode.Running, __instance); return false;
    }
    static void ObserveCleanup() { cleanupCalls++; }
    static void ObserveHome() { homeCalls++; }
    static bool Nearest(ref Ship __result) { __result = home; return false; }
    static bool NoRegionalAlternatives(ref List<string> __result) { __result = new List<string>(); return false; }
    static bool Send(string __0, ref bool __result) {
        Check(__0 == "SHIPBlockadeComing", "Detector issued an unexpected interaction");
        warnings++; __result = true; return false;
    }
    static bool Leaf(BaseCommand __instance, ref CommandStatus __result) {
        Type type = __instance.GetType();
        CommandCode result = type == typeof(TurnOnAllSensors) || type == typeof(TurnOffAllActiveSensors) ||
            type == typeof(AmIArmed) || type == typeof(SetEngagementProfile) || type == typeof(DoIPreferShortRange)
            ? CommandCode.Finished : CommandCode.Failure;
        if (type == typeof(KeepWithinFiringDistance) || type == typeof(KeepAtDistance)) result = CommandCode.Running;
        if (type == typeof(AmIArmed) && !armed) result = CommandCode.Failure;
        __result = new CommandStatus(result, __instance); return false;
    }
    static Ship Hull(string id, bool foreign = true, bool disabled = false) {
        var ship = Empty<Ship>(); ship.strRegID = id;
        Set(ship, "<objSS>k__BackingField", new ShipSitu(false)); Set(ship, "<Comms>k__BackingField", Empty<Comms>());
        ship.IsAIShip = true; Set(ship, "nLoadState", disabled ? Ship.Loaded.Shallow : Ship.Loaded.Full);
        owners[id] = foreign ? "foreign-faction" : "blockading-faction";
        return ship;
    }
    static StickySequenceCommand MainBranch(BlockadePilot pilot) => (StickySequenceCommand)
        ((List<ICommand>)AccessTools.Field(typeof(SelectorCommand), "_childCommands").GetValue(pilot.BehaviorTree)).Last();
    static BlockadePilot Pilot(bool patched) {
        var ship = Hull("blockader", false); ship.shipSituTarget = new ShipSitu(false) { vPosx = .001 };
        var pilot = new BlockadePilot(null, ship); MainBranch(pilot).SaveData = new[] { "2" };
        Check(!pilot.PriorityQueue.CanExecute(pilot.Blackboard), "Unexpected overriding queued command");
        string save = JsonConvert.SerializeObject(pilot.BehaviorTree.GetSaveData(""));
        if (nativeSave == null) nativeSave = save;
        Check(save == nativeSave, "Production Blockade constructor changed native saved paths/payloads");
        var combat = (SequenceCommand)((List<ICommand>)AccessTools.Field(typeof(SelectorCommand), "_childCommands").GetValue(pilot.BehaviorTree))[1];
        var gates = (List<ICommand>)AccessTools.Field(typeof(SequenceCommand), "_childCommands").GetValue(combat);
        Check(patched ? gates[0] is ConditionCommand && gates[1] is ConditionCommand :
            gates[0].GetType() == typeof(AmIInCombat) && gates[1].GetType() == typeof(AreThreatsNearbyBlockade),
            "Production constructor patch did not bind the intended Blockade combat gates");
        return pilot;
    }
    static void Initiate(BlockadePilot pilot, Ship target) {
        pilot.Blackboard.Set("Enemies", new List<Ship> { target }); pilot.Blackboard.Set("ClosestEnemy", target);
        Check(new InitiateAgainstBiggestThreat(pilot).RunCommand(pilot.Blackboard).Status == CommandCode.Finished,
            "Native initiation failed");
    }
    static void LostContact(bool patched, bool savedFalse, bool rangedOut) {
        var pilot = Pilot(patched); var ship = pilot.ShipUs; var target = Hull("disabled-foreign", disabled: true);
        visible = new Dictionary<Ship, ShipSignature>(); range = rangedOut ? 3e-5 : 1e-8;
        if (rangedOut) visible[target] = Empty<ShipSignature>();
        Initiate(pilot, target);
        if (savedFalse) {
            pilot.Blackboard.Set("InCombat", false);
            ship.shipSituTarget = new ShipSitu(false) { vPosx = .003 };
        }
        int oldCleanup = cleanupCalls, oldHome = homeCalls, oldFlight = flightCalls, oldScans = scans;
        pilot.BehaviorTree.RunCommand(pilot.Blackboard);
        if (patched) {
            Check(cleanupCalls == oldCleanup + 1 && homeCalls == oldHome + 1 && scans == oldScans + 1,
                "Production gates did not admit one native scan/cleanup/retarget pass");
            Check(!pilot.Blackboard.HasKey("Combatants") && ship.shipCombatTarget == null, "Native cleanup did not end the abandoned engagement");
            Check(ship.shipScanTarget == home && ship.targetRegID == home.strRegID && ship.TargetData?.Ship == home,
                "Native TargetNearestStation did not consistently bind live station identity");
            // Do not leave the formerly visible opponent in the fixture on later
            // ticks: Blockade has its own unrelated fresh disabled-acquisition
            // behavior, which is measured explicitly below.
            visible.Clear();
        }
        for (int tick = 0; tick < 3; tick++) pilot.BehaviorTree.RunCommand(pilot.Blackboard);
        if (!patched) {
            Check(cleanupCalls == oldCleanup && homeCalls == oldHome && flightCalls == oldFlight + 4 && flightTarget == target,
                "Native lost-contact failure did not strand ordinary patrol on the enemy");
            Check(!pilot.Blackboard.Get<bool>("InCombat") && pilot.Blackboard.Get<List<Ship>>("Combatants").Contains(target),
                "Native contradictory pending combat bookkeeping did not persist");
        } else Check(flightTarget == home && cleanupCalls == oldCleanup + 1 && homeCalls == oldHome + 1,
            "Production cleanup repeated or ordinary patrol retained the enemy destination");
        Check(MainBranch(pilot).SaveData[0] == "2", "Repair reset the ordinary movement phase");
        Console.WriteLine($"{(patched ? "production" : "native")} / {(savedFalse ? "saved false flag" : "live loss")} / {(rangedOut ? "outside range" : "no signatures")}: passed");
        range = 1e-8;
    }
    sealed class UrgentMarker : BaseCommand {
        internal int Calls;
        public override CommandStatus RunCommand(Blackboard board) { Calls++; return Running; }
    }
    static void Controls(bool patched) {
        var pilot = Pilot(patched); var ship = pilot.ShipUs;
        ship.shipScanTarget = home; ship.shipSituTarget = home.objSS; ship.shipCombatTarget = Hull("reference-only");
        visible.Clear(); int oldCleanup = cleanupCalls, oldHome = homeCalls;
        pilot.BehaviorTree.RunCommand(pilot.Blackboard);
        Check(cleanupCalls == oldCleanup && homeCalls == oldHome && flightTarget == home && ship.shipCombatTarget != null,
            "Dormant reference redirected valid station travel");
        var active = Hull("active-foreign"); visible[active] = Empty<ShipSignature>(); ship.shipCombatTarget = null;
        Initiate(pilot, active); int oldFlight = flightCalls;
        var result = pilot.BehaviorTree.RunCommand(pilot.Blackboard);
        Check(result.Status == CommandCode.Running && ship.shipCombatTarget == active && homeCalls == oldHome && flightCalls == oldFlight,
            "Ongoing blockade fight diverted into station return");
        Check(pilot.Blackboard.Get<bool>("InCombat") && pilot.Blackboard.Get<List<Ship>>("Combatants").SequenceEqual(new[] { active }),
            "Active combat bookkeeping changed");
        var disabled = Hull("disabled-mixed", disabled: true); visible[disabled] = Empty<ShipSignature>();
        pilot.Blackboard.Set("Combatants", new List<Ship> { disabled, active }); ship.shipCombatTarget = disabled;
        result = pilot.BehaviorTree.RunCommand(pilot.Blackboard);
        Check(result.Status == CommandCode.Running && ship.shipCombatTarget == active && homeCalls == oldHome &&
            pilot.Blackboard.Get<List<Ship>>("Combatants").SequenceEqual(new[] { active }), "Mixed native fight/neutralization changed");
        var urgent = new UrgentMarker(); pilot.PriorityQueue.EnqueueUrgentCommand(urgent); visible.Clear(); oldCleanup = cleanupCalls;
        result = pilot.BehaviorTree.RunCommand(pilot.Blackboard);
        Check(result.Status == CommandCode.Running && urgent.Calls == 1 && cleanupCalls == oldCleanup && homeCalls == oldHome,
            "Gate repair preempted an urgent command");
    }
    static void BlockadePolicy(bool patched) {
        var pilot = Pilot(patched); var detector = new AreThreatsNearbyBlockade(pilot); var board = pilot.Blackboard;
        var own = Hull("same-faction", false); var foreign = Hull("civilian-foreign");
        visible.Clear(); visible[own] = Empty<ShipSignature>(); int oldWarnings = warnings;
        Check(detector.RunCommand(board).Status == CommandCode.Failure && warnings == oldWarnings,
            "Own-faction ship became a fresh blockade violation");
        visible[foreign] = Empty<ShipSignature>();
        Check(detector.RunCommand(board).Status == CommandCode.Finished && board.Get<List<Ship>>("Enemies").SequenceEqual(new[] { foreign }) &&
            warnings == oldWarnings + 1, "Foreign civilian did not receive native restricted-area warning");
        Check(detector.RunCommand(board).Status == CommandCode.Finished && warnings == oldWarnings + 1,
            "Unchanged closest contact repeated the native warning");
        board.Set("SurrenderedTo", new List<Ship> { foreign });
        Check(detector.RunCommand(board).Status == CommandCode.Failure, "Blockade ignored native surrendered-to exemption");
        board.Remove("SurrenderedTo"); foreign.Classification = Ship.TypeClassification.Projectile;
        Check(detector.RunCommand(board).Status == CommandCode.Failure, "Blockade classified a projectile as a target");
        foreign.Classification = Ship.TypeClassification.Default; Set(foreign, "bTorchDriveThrusting", true);
        foreign.objSS.vVelX = 2e-8;
        Check(detector.RunCommand(board).Status == CommandCode.Failure, "Fast torch escape stopped excluding the contact");
        Set(foreign, "bTorchDriveThrusting", false); foreign.objSS.vVelX = 0; range = 3e-5;
        Check(detector.RunCommand(board).Status == CommandCode.Failure, "Beyond-2500km contact became eligible");
        range = 1e-8;
        Check(detector.RunCommand(board).Status == CommandCode.Finished && warnings == oldWarnings + 2,
            "Reentry did not retain native renewed warning behavior");
    }
    static void ExistingDisabledAcquisition(bool patched) {
        var pilot = Pilot(patched); var target = Hull("fresh-disabled", disabled: true);
        var detector = new AreThreatsNearbyBlockade(pilot);
        visible.Clear(); visible[target] = Empty<ShipSignature>();
        Check(detector.RunCommand(pilot.Blackboard).Status == CommandCode.Finished,
            "Existing Blockade disabled-contact acquisition changed unexpectedly");
        Initiate(pilot, target);
        Check(new IsTargetNeutralized(pilot).RunCommand(pilot.Blackboard).Status == CommandCode.Finished,
            "Native disabled-contact neutralization changed");
        Check(detector.RunCommand(pilot.Blackboard).Status == CommandCode.Finished,
            "Blockade's separate acquire/drop contradiction no longer reproduces");
    }
    static void FreshViolation(bool patched) {
        var pilot = Pilot(patched); var foreign = Hull("fresh-civilian-violation");
        visible.Clear(); visible[foreign] = Empty<ShipSignature>(); int oldWarnings = warnings, oldHome = homeCalls;
        var result = pilot.BehaviorTree.RunCommand(pilot.Blackboard);
        Check(result.Status == CommandCode.Finished && warnings == oldWarnings + 1 && homeCalls == oldHome &&
            pilot.ShipUs.shipCombatTarget == foreign && pilot.Blackboard.Get<bool>("InCombat"),
            "Native restricted-area warning/engagement chain changed");
        result = pilot.BehaviorTree.RunCommand(pilot.Blackboard);
        Check(result.Status == CommandCode.Running && warnings == oldWarnings + 1 && pilot.ShipUs.shipCombatTarget == foreign,
            "Native blockade did not retain engagement after its warning");
    }
    static void UnarmedRetreat(bool patched) {
        var pilot = Pilot(patched); var target = Hull("active-threat-to-unarmed");
        visible.Clear(); visible[target] = Empty<ShipSignature>(); Initiate(pilot, target);
        int oldHome = homeCalls;
        armed = false;
        try {
            for (int tick = 0; tick < 4; tick++) pilot.BehaviorTree.RunCommand(pilot.Blackboard);
            Check(homeCalls == oldHome + 1 && pilot.ShipUs.shipScanTarget == home && flightTarget == home,
                "Native sticky Flee did not retain its station destination and movement");
            Check(pilot.Blackboard.Get<List<Ship>>("Combatants").SequenceEqual(new[] { target }),
                "Retreat control silently erased a still-visible combatant");
        } finally { armed = true; }
    }
    static void AuthoredAssets() {
        string data = Path.Combine(Ostrabus.Development.InstallationPaths.Game, "Ostranauts_Data", "StreamingAssets", "data");
        JObject Definition(string path, string name) => JArray.Parse(File.ReadAllText(Path.Combine(data, path)))
            .OfType<JObject>().Single(item => (string)item["strName"] == name);
        var warning = Definition(Path.Combine("interactions", "interactions_ship_to_ship.json"), "SHIPBlockadeComing");
        Check((string)warning["ShipTestUs"] == "IsBlockader" && ((JArray)warning["aInverse"]).Count == 0,
            "Authored blockade warning predicate or reply policy changed");
        Check(warning.Properties().All(property => !property.Name.StartsWith("Loot", StringComparison.Ordinal)),
            "Warning now has effects requiring separate policy review");
        var trigger = Definition(Path.Combine("condtrigs", "condtrigs.json"), "TIsBlockader");
        Check(((JArray)trigger["aReqs"]).Values<string>().SequenceEqual(new[] { "IsBlockadeShip" }) && (bool)trigger["bAND"],
            "Authored blockader identity condition changed");
        var loot = JArray.Parse(File.ReadAllText(Path.Combine(data, "loot", "loot.json")));
        Check(loot.OfType<JObject>().Any(item => (string)item["strType"] == "text" && item["aCOs"] is JArray entries &&
            entries.Values<string>().Contains("SHIPBlockadeComing=1x1")), "Warning is absent from native ship interaction loot");
    }
    static int Main() {
        var fixture = new Harmony("offline.blockade-target-handoff.fixture");
        var production = new Harmony("offline.blockade-target-handoff.production");
        void Patch(Type type, string method, string prefix = null, Type[] parameters = null) => fixture.Patch(
            AccessTools.Method(type, method, parameters), prefix: new HarmonyMethod(typeof(Program), prefix));
        try {
            AuthoredAssets();
            CrewSim.system = Empty<StarSystem>(); captain = Empty<CondOwner>(); captain.bAlive = true; home = Hull("fixture-station", false);
            Patch(typeof(UnityEngine.Object), "op_Equality", nameof(Equal)); Patch(typeof(UnityEngine.Object), "op_Inequality", nameof(Unequal));
            Patch(typeof(StarSystem), nameof(StarSystem.GetShipOwner), nameof(Owner)); Patch(typeof(Comms), nameof(Comms.GetCaptain), nameof(Captain));
            Patch(typeof(Comms), nameof(Comms.SendMessage), nameof(Send), new[] { typeof(string), typeof(string), typeof(string) });
            Patch(typeof(CondOwner), nameof(CondOwner.HasCond), nameof(HasCond), new[] { typeof(string) });
            Patch(typeof(DataHandler), nameof(DataHandler.GetCondTrigger), nameof(Quiet), new[] { typeof(string) });
            Patch(typeof(BaseCommand), "GetVisibleShipSignatures", nameof(Visible));
            Patch(typeof(Ship), nameof(Ship.GetRangeTo), nameof(Range), new[] { typeof(Ship) }); Patch(typeof(Ship), nameof(Ship.IsMoored), nameof(NotMoored));
            Patch(typeof(AIShipManager), nameof(AIShipManager.GetAIShipByRegID), nameof(NoController));
            Patch(typeof(FlyTo), nameof(FlyTo.RunCommand), nameof(Fly)); Patch(typeof(FlyTo), nameof(FlyTo.CanExecute), nameof(CanFly));
            Patch(typeof(IsTargetNeutralized), nameof(IsTargetNeutralized.RunCommand), nameof(ObserveCleanup));
            Patch(typeof(TargetNearestStation), nameof(TargetNearestStation.RunCommand), nameof(ObserveHome));
            Patch(typeof(StarSystem), nameof(StarSystem.GetNearestStationRegional), nameof(Nearest));
            foreach (var method in typeof(StarSystem).GetMethods().Where(method => method.Name == nameof(StarSystem.GetNearestStation)))
                fixture.Patch(method, prefix: new HarmonyMethod(typeof(Program), nameof(Nearest)));
            Patch(typeof(AIShipManager), nameof(AIShipManager.GetAllStationsInATCRegion), nameof(NoRegionalAlternatives));
            foreach (Type type in new[] { typeof(TurnOnAllSensors), typeof(TurnOffAllActiveSensors), typeof(IsFriendlyUnderAttack),
                typeof(AmIDamaged), typeof(DoIHaveDecoys), typeof(AmIArmed), typeof(AmIBeingTargeted), typeof(SetEngagementProfile),
                typeof(DoIPreferShortRange), typeof(KeepWithinFiringDistance), typeof(KeepAtDistance) }) Patch(type, "RunCommand", nameof(Leaf));
            foreach (bool patched in new[] { false, true }) {
                if (patched) production.CreateClassProcessor(typeof(CombatCleanupAdmissionPatch)).Patch();
                foreach (bool saved in new[] { false, true }) foreach (bool outside in new[] { false, true }) LostContact(patched, saved, outside);
                Controls(patched); BlockadePolicy(patched); FreshViolation(patched); UnarmedRetreat(patched); ExistingDisabledAcquisition(patched);
            }
            Console.WriteLine(checks + " native baseline/production handoff checks passed before acquisition repair.");
            production.CreateClassProcessor(typeof(BlockadeNeutralizedThreatPatch)).Patch();
            foreach (bool saved in new[] { false, true }) foreach (bool outside in new[] { false, true }) LostContact(true, saved, outside);
            Controls(true); BlockadePolicy(true); FreshViolation(true); UnarmedRetreat(true);
            AcquisitionChecks();
            Console.WriteLine(checks + " native baseline/production Blockade handoff, acquisition and policy checks passed.");
            return 0;
        } finally { production.UnpatchSelf(); fixture.UnpatchSelf(); }
    }
}
