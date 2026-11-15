using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json;
using Ostrabus.NavConsole.Rotation;
using Ostranauts.Core;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using UnityEngine;

static partial class Program {
    static void NativeControls(Harmony fixture, Harmony production) {
        production.CreateClassProcessor(typeof(RotationCommandFactoryPatch)).Patch();
        production.CreateClassProcessor(typeof(RotationFastForwardPatch)).Patch();
        var ship = Hull("native-input");
        var command = Order(ship, Math.PI);
        double fuel = ship.fShallowRCSRemass;
        ship.Maneuver(0.1f, 0.2f, 0, 0, 0.02f);
        Check(ship.fShallowRCSRemass == fuel, "Input adapter debited fuel before physics");
        StarSystem.fEpoch += .02;
        ship.objSS.TimeAdvance(.02);
        Check(ship.objSS.vAccRCS.x != 0 && ship.objSS.vAccRCS.y != 0, "PID discarded manual translation");
        Check(ship.fShallowRCSRemass < fuel && ship.objSS.fW != 0, "Combined native command failed");
        ship.Maneuver(0, 0, 0, 0, .02f);
        StarSystem.fEpoch += .02;
        ship.objSS.TimeAdvance(.02);
        Check(ship.objSS.vAccRCS == Vector2.zero, "Released translation kept firing");
        float rot = ship.objSS.fRot, rate = ship.objSS.fW;
        fuel = ship.fShallowRCSRemass;
        ship.objSS.TimeAdvance(0);
        Check(ship.fShallowRCSRemass == fuel && ship.objSS.fRot == rot && ship.objSS.fW == rate, "Paused rotation consumed fuel or moved");

        var saved = JsonConvert.DeserializeObject<string[]>(JsonConvert.SerializeObject(command.SaveData));
        var pilot = new Pilot(ship);
        var created = AIShip.CreateCommand(OstrabusRotationPilot.CommandName, pilot);
        Check(created is OstrabusRotationPilot, "Native command factory failed to construct rotation controller");
        created.SaveData = saved;
        Check(created.SaveData.SequenceEqual(saved), "Native primitive payload failed JSON roundtrip");
        pilot.PriorityQueue.EnqueueCommand(created);
        var tree = JsonConvert.DeserializeObject<JsonCommandPayload[]>(JsonConvert.SerializeObject(pilot.PriorityQueue.GetSaveData("/BehaviorTree")));
        var loaded = new Pilot(ship);
        loaded.PriorityQueue.LoadSaveData("/BehaviorTree", tree);
        var resaved = loaded.PriorityQueue.GetSaveData("/BehaviorTree");
        Check(resaved.Count == tree.Length && resaved.Any(entry => entry.aCommandPayload != null && entry.aCommandPayload.SequenceEqual(saved)), "Native priority queue lost command or payload");
        foreach (var bad in new[] { Array.Empty<string>(), new string[10], new[] { "wrong" } }) {
            created.SaveData = bad;
            Check(!RotationRuntime.Actuating, "Invalid payload changed actuation ownership");
        }
        // Restore the original owner after factory/load tests created detached commands.
        RotationRuntime.Attach(command);
        ship.Maneuver(0, 0, .1f, 0, .02f);
        Check(!command.Order.Active && !RotationRuntime.HasCommands, "Manual yaw retained bearing control");
        Check(ship.objSS.fA != 0, "Cancellation swallowed native manual rotation");

        RotationRuntime.Cleanup();
        var inertial = Hull("out-of-fuel");
        var noFuel = Order(inertial, Math.PI);
        inertial.fShallowRCSRemass = 0;
        inertial.objSS.fW = .2f;
        StarSystem.fEpoch += 60;
        inertial.objSS.TimeAdvance(60);
        Check(Math.Abs(inertial.objSS.fW - .2f) < 1e-6 && inertial.objSS.fA == 0 && inertial.fShallowRCSRemass == 0,
            "No-fuel controller manufactured a turn or residual force");

        RotationRuntime.Cleanup();
        var damaged = Hull("module-damage");
        var damageOrder = Order(damaged, Math.PI);
        damaged.objSS.TimeAdvance(.02);
        damaged.json.aItems[1].strName += "Dmg";
        float previous = damaged.objSS.fW;
        fuel = damaged.fShallowRCSRemass;
        damaged.objSS.TimeAdvance(.02);
        Check(damaged.fShallowRCSRemass == fuel && damaged.objSS.fW == previous && damaged.objSS.fA == 0,
            "Damaged module kept spending fuel or torque");

        RotationRuntime.Cleanup();
        var target = Hull("original-target"); target.objSS.vPosx = 0.001;
        var tracker = Hull("tracker");
        var tracking = Order(tracker, -Math.PI / 2, 0, true, target.strRegID);
        Simulate(tracker, tracking, .1, 60);
        target.objSS.vPosx = .0009; target.objSS.vPosy = .0001;
        Simulate(tracker, tracking, .1, 60);
        double expected = Math.Atan2(.0001, .0009) - Math.PI / 2;
        Check(tracking.Order.TargetId == "original-target" && Math.Abs(RotationPid.Wrap(tracker.objSS.fRot - expected)) < .02,
            "Original target bearing hold did not follow target movement");
        var replacement = Empty<AIShip>(); Set(replacement, "_shipUs", tracker); Set(replacement, "_aiCharacter", new Pilot(tracker));
        AIShipManager.dictAIs["fixture"].Remove(tracking.Owner);
        AIShipManager.dictAIs["fixture"].Add(replacement);
        Check(RotationRuntime.Find(tracker) == null, "Replaced controller retained angular ownership");
        RotationRuntime.Cleanup();
        fixture.Patch(AccessTools.Method(typeof(BaseCharacter), "AssignCaptain"), prefix: new HarmonyMethod(typeof(Program), nameof(Quiet)));
        var persistent = Hull("native-auto");
        var real = new AIShip("fixture", AIType.Auto, persistent);
        AIShipManager.AddAIToDict(real, "fixture");
        var autonomous = new OstrabusRotationPilot(real.AICharacter) { Owner = real };
        autonomous.SaveData = new[] { "ostrabus.rotation.v1", "3.14", "0", "0", "", "", persistent.strRegID + "-nav", persistent.strRegID + "-module", "0", "0" };
        real.AddCommand(autonomous);
        real.RunAI();
        Check(real.ActiveCommandName == OstrabusRotationPilot.CommandName, "Native Auto did not dispatch rotation command");
        var beforeSkip = JsonConvert.SerializeObject(real.AICharacter.PriorityQueue.GetSaveData("/BehaviorTree"));
        for (int i = 0; i < 3; i++) real.FFWD();
        Check(JsonConvert.SerializeObject(real.AICharacter.PriorityQueue.GetSaveData("/BehaviorTree")) == beforeSkip,
            "SFF duplicated or replaced the owned rotation command queue");
        var fullTree = JsonConvert.DeserializeObject<JsonCommandPayload[]>(JsonConvert.SerializeObject(real.GetBehaviorTreeSaveData()));
        AIShipManager.dictAIs["fixture"].Remove(real);
        RotationRuntime.Cleanup();
        real = new AIShip(new JsonAIShipSave { strHomeStation = "fixture", strRegId = persistent.strRegID, enumAIType = AIType.Auto }, persistent);
        AIShipManager.AddAIToDict(real, "fixture");
        real.LoadBehaviorTreeSave(fullTree);
        real.RunAI();
        autonomous = RotationRuntime.Find(persistent);
        Check(autonomous != null && autonomous.Order.Active && real.ActiveCommandName == OstrabusRotationPilot.CommandName,
            "Native AI reconstruction failed to restore a running rotation command");
        Simulate(persistent, autonomous, .1, 150);
        for (int i = 0; i < 8 && AIShipManager.GetAIShipByRegID(persistent.strRegID) != null; i++) real.RunAI();
        Check(AIShipManager.GetAIShipByRegID(persistent.strRegID) == null && !RotationRuntime.HasCommands,
            "Completed native Auto controller did not retire cleanly: active=" + autonomous.Order.Active +
            " error=" + RotationPid.Wrap(autonomous.Order.Heading - persistent.objSS.fRot) + " rate=" + persistent.objSS.fW);
        Console.WriteLine("Native Auto scheduler dispatched, completed and retired the physical rotation command without a visible console.");
        RotationRuntime.Cleanup();
        Console.WriteLine("Native controls: translation/release, pause, factory/queue/JSON, cancellation, no-fuel, damage, original-target hold and replacement checks passed.");
    }
}
