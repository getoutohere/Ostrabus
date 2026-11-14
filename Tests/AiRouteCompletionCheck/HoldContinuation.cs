using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using LitJson;
using Newtonsoft.Json;
using Ostranauts.Core;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using Ostranauts.Ships;

static partial class Program {
    static Task cleanupTask;
    static readonly Dictionary<CondOwner, double> stale = new();
    static int searches;
    static bool nextTripAvailable;
    static bool NoRoute(ref object __result) {
        searches++; __result = null;
        if (nextTripAvailable) {
            var type = AccessTools.Inner(typeof(TargetNewIfNone), "TargetDataDTO");
            __result = Activator.CreateInstance(type);
            Set(__result, "TargetData", new TargetData(CrewSim.system.dictShips["HQCH"]));
            Set(__result, "FuelCostRCS", 10d);
        }
        return false;
    }
    static bool LowFuel(ref bool __result) { __result = false; return false; }
    static bool Speed(ref double __result) { __result = 1e-9; return false; }
    static bool ConditionAmount(CondOwner __instance, string __0, ref double __result) { __result = __0 == "IsStale" && stale.TryGetValue(__instance, out var value) ? value : 0; return false; }
    static bool SetCondition(CondOwner __instance, string __0, double __1) { if (__0 == "IsStale") stale[__instance] = __1; return false; }
    static bool ZeroCondition(CondOwner __instance, string __0) { if (__0 == "IsStale") stale.Remove(__instance); return false; }
    static bool NoCondition(ref bool __result) { __result = false; return false; }
    static CondOwner bountyCaptain;
    static bool HasCondition(CondOwner __instance, string __0, ref bool __result) { __result = ReferenceEquals(__instance, bountyCaptain) && __0 == "IsBountyTarget"; return false; }
    static bool StillManeuver(Ship __instance) { __instance.objSS.ResetAccelerations(); return false; }
    static void CaptureTask(Task __result) => cleanupTask = __result;
    static void CleanupNow() {
        cleanupTask = null;
        AccessTools.Method(typeof(StarSystem), "CleanupStaleShips").Invoke(null, null);
        Check(cleanupTask != null, "Native stale cleanup did not start");
        cleanupTask.GetAwaiter().GetResult();
    }
    static (AIShip ai, NavyPilot pilot, StickySequenceCommand main, Ship ship, CondOwner person) HoldFixture() {
        var ship = Vessel("wayward");
        Back(ship, "ShipCO", Empty<CondOwner>()); ship.ShipCO.ship = ship;
        Set(ship, "aPeople", new List<PersonSpec>());
        var person = Empty<CondOwner>(); person.ship = ship; person.bAlive = true;
        bountyCaptain = person;
        var spec = Empty<PersonSpec>(); spec.strCO = "bounty captain"; Set(spec, "co", person); person.pspec = spec;
        ((List<PersonSpec>)AccessTools.Field(typeof(Ship), "aPeople").GetValue(ship)).Add(spec);
        var pilot = Empty<NavyPilot>(); Back(pilot, "ShipUs", ship); Back(pilot, "CaptainUs", person);
        pilot.Blackboard = new Blackboard(); pilot.PriorityQueue = new QueuedCommandsNode("PriorityQueue", pilot);
        var main = new StickySequenceCommand("MainBehavior", new ICommand[] { new TargetNewIfNone(pilot) });
        pilot.BehaviorTree = new SelectorCommand("BehaviorTree", pilot.PriorityQueue, main);
        var ai = Empty<AIShip>(); Set(ai, "_shipUs", ship); Set(ai, "_aiCharacter", pilot);
        Set(ai, "_behaviorTree", pilot.BehaviorTree); Back(ai, "Blackboard", pilot.Blackboard);
        var reference = Vessel("HQCH", false); reference.objSS.vVelX = 2e-7; reference.objSS.vVelY = 1e-7;
        Back(reference, "ShipCO", Empty<CondOwner>());
        CrewSim.system.dictShips = new Dictionary<string, Ship> { ["wayward"] = ship, ["HQCH"] = reference };
        CrewSim.coPlayer.ship = Vessel("PLAYER", false);
        AIShipManager.dictAIs = new Dictionary<string, List<AIShip>> { ["HQCH"] = new List<AIShip> { ai } };
        ship.fLastVisit = 100; ship.fShallowRCSRemass = 1300;
        return (ai, pilot, main, ship, person);
    }
    static void HoldContinuation() {
        var boundaries = new Harmony("offline.ai-velocity.continuation-boundaries");
        void Patch(MethodBase method, string name) => boundaries.Patch(method, prefix: new HarmonyMethod(typeof(Program), name));
        Patch(AccessTools.Method(typeof(TargetNewIfNone), "FindTarget"), nameof(NoRoute));
        Patch(AccessTools.Method(typeof(AIShipManager), nameof(AIShipManager.LowFuel)), nameof(LowFuel));
        Patch(AccessTools.Method(typeof(NavyPilot), nameof(NavyPilot.MaxSpeed)), nameof(Speed));
        Patch(AccessTools.Method(typeof(Ship), nameof(Ship.Maneuver)), nameof(StillManeuver));
        Patch(AccessTools.Method(typeof(Ship), nameof(Ship.SetThrust)), nameof(Skip));
        Patch(AccessTools.Method(typeof(Ship), nameof(Ship.IsStationHidden)), nameof(NoCondition));
        Patch(AccessTools.Method(typeof(CondOwner), nameof(CondOwner.GetCondAmount), new[] { typeof(string), typeof(bool) }), nameof(ConditionAmount));
        Patch(AccessTools.Method(typeof(CondOwner), nameof(CondOwner.SetCondAmount)), nameof(SetCondition));
        Patch(AccessTools.Method(typeof(CondOwner), nameof(CondOwner.ZeroCondAmount)), nameof(ZeroCondition));
        Patch(AccessTools.Method(typeof(CondOwner), nameof(CondOwner.HasCond), new[] { typeof(string), typeof(bool) }), nameof(HasCondition));
        Patch(AccessTools.Method(typeof(PlotManager), nameof(PlotManager.IsActivePlotCO)), nameof(NoCondition));
        Patch(AccessTools.Method(typeof(UnityEngine.Debug), nameof(UnityEngine.Debug.Log), new[] { typeof(object) }), nameof(Skip));
        Patch(AccessTools.Method(typeof(UnityEngine.Debug), nameof(UnityEngine.Debug.LogWarning), new[] { typeof(object) }), nameof(Skip));
        boundaries.Patch(AccessTools.Method(typeof(Task), nameof(Task.Run), new[] { typeof(Action) }), postfix: new HarmonyMethod(typeof(Program), nameof(CaptureTask)));
        try {
            StarSystem.fEpoch = 100000; searches = 0; stale.Clear(); nextTripAvailable = false;
            var baseline = HoldFixture();
            Check(baseline.person.HasCond("IsBountyTarget"), "Baseline captain is not a bounty target");
            // Real caller, real Navy RequestHelp refusal and real UnregisterShip.
            // Only route-search availability and engine/condition/UI boundaries are fixtures.
            new TargetNewIfNone(baseline.pilot).RunCommand(baseline.ai.Blackboard);
            Check(searches == 1 && AIShipManager.dictAIs["HQCH"].Count == 0 && stale.ContainsKey(baseline.ship.ShipCO), "Did not reproduce native unregister-on-no-route");
            StarSystem.fEpoch += 4 * 3600;
            CleanupNow();
            Check(baseline.ship.bRemove, "Native stale cleanup did not schedule failed recovery hull for deletion");

            var production = new Harmony("offline.ai-velocity.sff-protection");
            production.CreateClassProcessor(typeof(Ostrabus.OstrabusPlugin).Assembly.GetType("Ostrabus.Inspection.ShipVelocityRecoverySffPatch", true)).Patch();

            StarSystem.fEpoch = 200000; searches = 0; stale.Clear();
            var held = HoldFixture();
            stale[held.ship.ShipCO] = 1; // A recovery must also cancel an obsolete disposal timestamp.
            var recovered = Recover("wayward", "HQCH", true);
            Check(recovered.Status == 200 && (bool)recovered.Body["held"], "Recovery did not establish Hold");
            Check(!stale.ContainsKey(held.ship.ShipCO), "Recovery retained stale disposal timestamp");
            for (int i = 0; i < 100; i++) {
                StarSystem.fEpoch += 3600;
                held.ai.RunAI();
                Check(AIShipManager.dictAIs["HQCH"].Count == 1 && searches == 0 && double.IsPositiveInfinity(held.ship.fAIDockingExpire), "Held AI entered target search/retirement");
            }
            CleanupNow();
            Check(!held.ship.bRemove && held.person.bAlive && ReferenceEquals(held.person.ship, held.ship), "Held hull/crew failed cleanup boundary");
            Check(held.person.HasCond("IsBountyTarget"), "Recovery removed bounty status");
            Check(held.ship.fShallowRCSRemass == 1300 && !held.ship.HideFromSystem, "Hold changed fuel or hid hull");

            // Native SFF must neither replay FlyTo nor append growing one-shot queues.
            for (int i = 0; i < 20; i++) { StarSystem.fEpoch += 21600; held.ai.FFWD(); held.ai.RunAI(); }
            Check(searches == 0 && AIShipManager.dictAIs["HQCH"].Count == 1, "SFF released recovery Hold");
            var payload = held.pilot.PriorityQueue.GetSaveData("").ToArray();
            Check(payload.Count(x => x.strPath.EndsWith("/Hold", StringComparison.Ordinal)) == 1, "Hold failed persistence or SFF duplicated commands");
            string commandWire = JsonMapper.ToJson(payload);
            var restoredPayload = JsonMapper.ToObject<JsonCommandPayload[]>(commandWire);
            Check(restoredPayload.Length == payload.Length && restoredPayload.Any(x => x.strPath.EndsWith("/Hold", StringComparison.Ordinal) && x.aCommandPayload?.Length == 1), "Native command payload JSON lost Hold");
            var saved = new JsonShip { fAIDockingExpire = held.ship.fAIDockingExpire, fAIPauseTimer = held.ship.fAIPauseTimer };
            string wire = JsonMapper.ToJson(saved);
            var lit = JsonMapper.ToObject<JsonShip>(wire);
            var compatible = typeof(Ostrabus.OstrabusPlugin).Assembly.GetType("Ostrabus.Optimizations.CompatibleJsonDeserializer", true);
            var json = (JsonShip)AccessTools.Method(compatible, "DeserializeNewtonsoft").MakeGenericMethod(typeof(JsonShip)).Invoke(null, new object[] { wire });
            var compatiblePayload = (JsonCommandPayload[])AccessTools.Method(compatible, "DeserializeNewtonsoft").MakeGenericMethod(typeof(JsonCommandPayload[])).Invoke(null, new object[] { commandWire });
            Check(compatiblePayload.Length == restoredPayload.Length && compatiblePayload.Any(x => x.aCommandPayload?.Length == 1 && x.aCommandPayload[0] == "Ostrabus.VelocityRecovery.Hold"), "Compatible command reader lost Hold marker");
            Check(double.IsPositiveInfinity(lit.fAIDockingExpire) && double.IsPositiveInfinity(json.fAIDockingExpire), "Native Hold timer did not survive JSON codecs: LitJson=" + lit.fAIDockingExpire + "; Newtonsoft=" + json.fAIDockingExpire + "; wire=" + wire.Substring(Math.Max(0, wire.IndexOf("fAIDockingExpire", StringComparison.Ordinal) - 1), 65));
            held.pilot.PriorityQueue = new QueuedCommandsNode("PriorityQueue", held.pilot);
            // Native tree references must refer to the restored native queue.
            held.pilot.BehaviorTree = new SelectorCommand("BehaviorTree", held.pilot.PriorityQueue, held.main);
            Set(held.ai, "_behaviorTree", held.pilot.BehaviorTree);
            held.pilot.PriorityQueue.LoadSaveData("", restoredPayload);
            held.ship.fAIDockingExpire = lit.fAIDockingExpire; held.ship.fAIPauseTimer = lit.fAIPauseTimer;
            for (int i = 0; i < 10; i++) { StarSystem.fEpoch += 21600; held.ai.RunAI(); }
            CleanupNow();
            Check(searches == 0 && !held.ship.bRemove && held.person.bAlive && AIShipManager.dictAIs["HQCH"].Count == 1, "Restored Hold did not preserve hull/controller/crew");
            // Exercise every supported role callback through the real AIShip
            // dispatcher; the guard must protect the recovered native queue.
            foreach (Type type in new[] { typeof(PolicePilot), typeof(PiratePilot), typeof(ScavPilot), typeof(CivilianPilot), typeof(HaulerCargoPilot), typeof(HaulerDeployingPilot), typeof(HaulerRetrievingPilot) }) {
                var role = (BaseCharacter)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(type);
                Back(role, "ShipUs", held.ship); role.Blackboard = held.ai.Blackboard;
                role.PriorityQueue = held.pilot.PriorityQueue; role.BehaviorTree = held.pilot.BehaviorTree;
                Set(held.ai, "_aiCharacter", role);
                double x = held.ship.objSS.vPosx, y = held.ship.objSS.vPosy;
                held.ai.FFWD(); held.ai.RunAI();
                Check(held.ship.objSS.vPosx == x && held.ship.objSS.vPosy == y && !held.ship.HideFromSystem && !held.ship.bRemove && held.person.bAlive && AIShipManager.dictAIs["HQCH"].Count == 1, "SFF role callback moved/retired recovery Hold: " + type.Name);
            }
            Set(held.ai, "_aiCharacter", held.pilot);
            var preview = Recover("wayward", null, false, release: true);
            Check(preview.Status == 200 && searches == 0 && (bool)preview.Body["held"], "Release preview planned or changed the Hold");
            var rejected = Recover("wayward", null, true, release: true);
            Check(rejected.Status == 409 && searches == 1 && (bool)rejected.Body["held"] && double.IsPositiveInfinity(held.ship.fAIDockingExpire), "Failed release discarded Hold");
            held.ai.RunAI(); CleanupNow();
            Check(searches == 1 && !held.ship.bRemove && held.person.bAlive && AIShipManager.dictAIs["HQCH"].Count == 1, "Failed release entered destructive AI continuation");
            nextTripAvailable = true;
            var released = Recover("wayward", null, true, release: true);
            Check(released.Status == 200 && !(bool)released.Body["held"] && !held.pilot.PriorityQueue.CanExecute(held.ai.Blackboard), "Feasible release did not remove recovery Hold");
            Check(ReferenceEquals(held.ship.shipScanTarget, CrewSim.system.dictShips["HQCH"]) && held.ai.Blackboard.Get<string>("TripFuelPrediction").EndsWith("&10", StringComparison.Ordinal), "Release lost accepted native target/fuel prediction");
            held.ai.RunAI();
            Check(searches == 2 && AIShipManager.dictAIs["HQCH"].Count == 1 && held.person.bAlive, "Released native AI did not retain accepted trip");
            production.UnpatchSelf();
        } finally { boundaries.UnpatchSelf(); }
    }
}
