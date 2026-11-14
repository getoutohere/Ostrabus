using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;

static partial class Program {
    static (int Status, Dictionary<string, object> Body) Recover(string id, string reference, bool apply, bool release = false) {
        var method = typeof(Ostrabus.OstrabusPlugin).Assembly.GetType("Ostrabus.Inspection.ShipVelocityRecovery", true)
            .GetMethod("Run", BindingFlags.Static | BindingFlags.NonPublic);
        object result = null;
        int status = 0;
        // A cold metadata lookup can exhaust the API's 1 ms inspection budget.
        for (int i = 0; i < 4; i++) {
            result = method.Invoke(null, new object[] { "/ships/" + id + (release ? "/release-hold" : "/match-velocity"), reference, apply });
            status = (int)AccessTools.Field(result.GetType(), "Status").GetValue(result);
            if (status != 503) break;
        }
        return (status, (Dictionary<string, object>)AccessTools.Field(result.GetType(), "Body").GetValue(result));
    }
    static void Recovery() {
        CrewSim.coPlayer.ship = null;
        var ship = Vessel("wayward");
        var reference = Vessel("HQCH", false);
        reference.objSS.vVelX = -3.74e-7; reference.objSS.vVelY = 8.3e-8;
        CrewSim.system.dictShips = new Dictionary<string, Ship> { [ship.strRegID] = ship, [reference.strRegID] = reference };
        var pilot = Empty<NavyPilot>(); Back(pilot, "ShipUs", ship);
        pilot.Blackboard = new Blackboard(); pilot.PriorityQueue = new QueuedCommandsNode("PriorityQueue", pilot);
        var fly = new FlyToPatrol(pilot);
        var main = new StickySequenceCommand("MainBehavior", new ICommand[] { new Undock(pilot), new TargetNewIfNone(pilot), fly });
        main.SaveData = new[] { "2" };
        var root = new SelectorCommand("BehaviorTree", pilot.PriorityQueue, main);
        // Match native Selector.RunCommand: its saved index can remain -1
        // even while Main is executing a later flight leaf.
        Set(root, "_currentChild", -1); pilot.BehaviorTree = root;
        var ai = Empty<AIShip>(); Set(ai, "_shipUs", ship); Set(ai, "_aiCharacter", pilot);
        Set(ai, "_behaviorTree", root); Set(ai, "_activeCommand", fly); Back(ai, "Blackboard", pilot.Blackboard);
        AIShipManager.dictAIs = new Dictionary<string, List<AIShip>> { ["HQCH"] = new List<AIShip> { ai } };
        ship.objSS.vPosx = .0375; ship.objSS.vPosy = .3147;
        ship.objSS.vVelX = -3.54e-7; ship.objSS.vVelY = 1.04e-7;
        ship.objSS.vAccRCS = new UnityEngine.Vector2(1e-10f, 1e-10f);
        ship.objSS.fW = 1; ship.objSS.fA = 1;
        ship.shipSituTarget = Endpoint(); ship.targetRegID = "old";
        ship.json.strScanTargetID = "old"; ship.json.strTargetRegID = "old"; ship.json.objSituScanTarget = ship.shipSituTarget.GetJSON();
        ship.shipStationKeepingTarget = reference; ship.json.strStationKeepingTargetID = "HQCH";
        ship.fShallowRCSRemass = 1300; ship.fShallowFusionRemain = 500;
        ship.fAIPauseTimer = 999999; ship.fAIDockingExpire = 100;
        ai.Blackboard.Set("TripFuelPrediction", "old&198"); ai.Blackboard.Set("RejectedPaths", 8);
        ai.Blackboard.Set("NeedsRepairs", false);
        var person = Empty<CondOwner>(); person.bAlive = true; person.ship = ship;
        var oldTarget = ship.shipSituTarget; var nav = Route(ship, Endpoint());
        var preview = Recover("wayward", "HQCH", false);
        Check(preview.Status == 200 && (bool)preview.Body["eligible"] && !(bool)preview.Body["applied"], "Preview rejected valid free Navy vessel");
        Check(ReferenceEquals(ship.shipSituTarget, oldTarget) && ReferenceEquals(ship.objSS.NavData, nav) && main.SaveData[0] == "2" && ship.objSS.fW == 1, "Preview changed navigation");
        var reply = Recover("wayward", "HQCH", true);
        Check(reply.Status == 200 && (bool)reply.Body["applied"], "Velocity recovery failed");
        Check(ship.objSS.vVelX == reference.objSS.vVelX && ship.objSS.vVelY == reference.objSS.vVelY, "Velocity was not matched");
        Check(ship.objSS.vPosx == .0375 && ship.objSS.vPosy == .3147, "Recovery teleported ship");
        Check(ship.objSS.NavData == null && ship.shipSituTarget == null && ship.shipScanTarget == null && ship.targetRegID == null, "Old navigation survived");
        Check(ship.json.objSituScanTarget == null && ship.json.strTargetRegID == null && ship.json.strScanTargetID == null && ship.json.strStationKeepingTargetID == null, "Stale saved navigation survived");
        Check(!ai.Blackboard.HasKey("TripFuelPrediction") && !ai.Blackboard.HasKey("RejectedPaths"), "Old route accounting survived");
        Check(main.SaveData[0] == "0" && ReferenceEquals(AccessTools.Field(typeof(AIShip), "_activeCommand").GetValue(ai), pilot.PriorityQueue), "Priority Hold was not made authoritative");
        Check(ship.objSS.vAccRCS.x == 0 && ship.objSS.vAccIn.x == 0 && ship.objSS.fW == 0 && ship.objSS.fA == 0, "Cancelled maneuver survived");
        Check(ship.fAIPauseTimer == 0 && double.IsPositiveInfinity(ship.fAIDockingExpire) && !ship.objSS.bIgnoreGrav, "Durable native Hold was not established");
        Check(ship.fShallowRCSRemass == 1300 && ship.fShallowFusionRemain == 500 && person.bAlive && ReferenceEquals(person.ship, ship), "Recovery changed fuel or crew");
        Check(ReferenceEquals(AIShipManager.dictAIs["HQCH"][0], ai) && !ship.bDestroyed && !ship.HideFromSystem, "Controller/hull was retired or replaced");
        reply = Recover("wayward", "Mercury", true);
        Check(reply.Status == 200 && ship.objSS.vVelX == mercury.dVelX && ship.objSS.vVelY == mercury.dVelY && (string)reply.Body["referenceKind"] == "body", "Celestial reference failed");
        Check(!ship.objSS.bBOLocked && !ship.objSS.bOrbitLocked, "Recovery imposed an orbit lock");
        foreach (Type type in new[] { typeof(HaulerCargoPilot), typeof(HaulerDeployingPilot), typeof(HaulerRetrievingPilot) }) {
            var hauler = (BaseCharacter)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(type);
            Back(hauler, "ShipUs", ship); hauler.Blackboard = pilot.Blackboard;
            hauler.PriorityQueue = new QueuedCommandsNode("PriorityQueue", hauler);
            hauler.BehaviorTree = new SelectorCommand("BehaviorTree", hauler.PriorityQueue, main);
            Set(ai, "_aiCharacter", hauler);
            Set(ai, "_behaviorTree", hauler.BehaviorTree);
            main.SaveData = new[] { "2" };
            ship.shipScanTarget = reference; ship.shipSituTarget = reference.objSS; ship.targetRegID = "HQCH";
            ship.json.strScanTargetID = "HQCH"; ship.json.strTargetRegID = "HQCH"; ship.json.objSituScanTarget = reference.objSS.GetJSON();
            Route(ship, Endpoint());
            var retained = Recover("wayward", "Mercury", true);
            Check(retained.Status == 200 && !(bool)retained.Body["freshMission"], "Hauler recovery failed: " + type.Name);
            Check(ReferenceEquals(ship.shipScanTarget, reference) && ReferenceEquals(ship.shipSituTarget, reference.objSS) && ship.targetRegID == "HQCH", "Hauler destination lost");
            Check(main.SaveData[0] == "2" && ship.json.strTargetRegID == "HQCH" && ship.objSS.NavData == null, "Hauler stage or saved destination lost");
        }
        Set(ai, "_aiCharacter", pilot);
        Set(ai, "_behaviorTree", root);
        foreach (bool apply in new[] { false, true }) {
            Check(Recover("wayward", "missing", apply).Status == 404, "Missing reference accepted");
            Check(Recover("wayward", "wayward", apply).Status == 400, "Self reference accepted");
            Check(Recover("wayward", "bad/id", apply).Status == 400, "Invalid reference accepted");
        }
        void Refused(string why) {
            double before = ship.objSS.vVelX;
            var refused = Recover("wayward", "HQCH", true);
            Check(refused.Status == 409 && ship.objSS.vVelX.Equals(before), why);
        }
        ship.objSS.bBOLocked = true; Refused("Anchored ship accepted"); ship.objSS.bBOLocked = false;
        ship.objSS.bGrounded = true; Refused("Grounded ship accepted"); ship.objSS.bGrounded = false;
        CrewSim.coPlayer.ship = ship; Refused("Player ship accepted"); CrewSim.coPlayer.ship = null;
        ship.shipCombatTarget = reference; Refused("Combat target accepted"); ship.shipCombatTarget = null;
        ai.Blackboard.Set("InCombat", true); Refused("Combat blackboard accepted"); ai.Blackboard.Set("InCombat", false);
        Set(ai, "_activeReaction", fly); Refused("Active reaction accepted"); Set(ai, "_activeReaction", null);
        ((Dictionary<string, Ship>)AccessTools.Field(typeof(Ship), "aDocked").GetValue(ship))["dock"] = reference;
        Refused("Docked ship accepted"); ((Dictionary<string, Ship>)AccessTools.Field(typeof(Ship), "aDocked").GetValue(ship)).Clear();
        AIShipManager.dictAIs["HQCH"].Add(Empty<AIShip>());
        var duplicate = Empty<AIShip>(); Set(duplicate, "_shipUs", ship); Set(duplicate, "_aiCharacter", pilot);
        AIShipManager.dictAIs["HQCH"].Add(duplicate); Refused("Conflicting controller accepted"); AIShipManager.dictAIs["HQCH"].Remove(duplicate);
        Set(ship.objSS, "_vVelX", double.NaN); Refused("NaN velocity accepted"); Set(ship.objSS, "_vVelX", 1e-7);
        var nativeTree = pilot.BehaviorTree;
        pilot.BehaviorTree = new SelectorCommand("NoPriorityQueue", new TargetNewIfNone(pilot));
        Refused("Hold accepted without priority queue in behavior tree"); pilot.BehaviorTree = nativeTree;
        pilot.BehaviorTree = new SelectorCommand("PriorityTooLate", new TargetNewIfNone(pilot), pilot.PriorityQueue);
        Refused("Hold accepted behind mission branch"); pilot.BehaviorTree = nativeTree;
        pilot.PriorityQueue.EnqueueCommand(new Hold(pilot)); Refused("Priority order was discarded");
    }
}
