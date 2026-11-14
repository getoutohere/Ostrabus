using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using Ostrabus.MiscBugFixes;
using UnityEngine;

static partial class Program {
    sealed class ActionCommand : BaseCommand {
        readonly Action action;
        internal ActionCommand(Pilot pilot, Action action) : base(pilot) { this.action = action; }
        public override CommandStatus RunCommand(Blackboard blackboard) { action(); return Running; }
    }

    static bool NoConditions(ref bool __result) { __result = false; return false; }
    static bool SmallElapsed(ref float __result) { __result = .1f; return false; }
    static bool NoStation(ref Ship __result) { __result = null; return false; }

    static AIShip Scheduled(Ship ship, Action action) {
        var ai = Register(ship);
        var pilot = (Pilot)ai.AICharacter;
        Set(ai, "_behaviorTree", new ActionCommand(pilot, action));
        Set(ai, "<Blackboard>k__BackingField", pilot.Blackboard);
        var person = Empty<CondOwner>();
        person.bAlive = true; person.ship = ship;
        var spec = Empty<PersonSpec>(); spec.strCO = ship.strRegID + "-pilot";
        Set(spec, "co", person);
        Set(ship, "aPeople", new List<PersonSpec> { spec });
        return ai;
    }

    static bool RegisteredIdentity(AIShip ai) {
        if (ai == null || AIShipManager.dictAIs == null) return false;
        foreach (var list in AIShipManager.dictAIs.Values) {
            if (list == null) continue;
            foreach (var current in list)
                if (ReferenceEquals(current, ai)) return true;
        }
        return false;
    }
    static void RegisteredRun(AIShip ai) { if (RegisteredIdentity(ai)) ai.RunAI(); }
    static void RegisteredFastForward(AIShip ai) { if (RegisteredIdentity(ai)) ai.FFWD(); }
    // Offline counterfactual at the two manager dispatch calls, not a global
    // RunAI restriction or a second registry. Production is unchanged.
    static IEnumerable<CodeInstruction> DispatchCandidate(IEnumerable<CodeInstruction> instructions) {
        int matches = 0;
        foreach (var instruction in instructions) {
            if (instruction.Calls(AccessTools.Method(typeof(AIShip), nameof(AIShip.RunAI)))) {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(Program), nameof(RegisteredRun)); matches++;
            } else if (instruction.Calls(AccessTools.Method(typeof(AIShip), nameof(AIShip.FFWD)))) {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(Program), nameof(RegisteredFastForward)); matches++;
            }
            yield return instruction;
        }
        if (matches != 1) throw new Exception("Expected one native manager dispatch site");
    }

    static void DispatchAfterWithdrawal(bool candidate) {
        var queue = (AIShipQueue)AccessTools.Field(typeof(AIShipManager), "_aIQueue").GetValue(null);
        foreach (bool replace in new[] { false, true }) {
            queue.Clear(); AIShipManager.dictAIs["fixture"].Clear();
            var ship = Hull("dispatch-withdrawn-" + replace);
            int oldRuns = 0, replacementRuns = 0;
            var old = Scheduled(ship, () => { oldRuns++; ship.objSS.vAccRCS = new Vector2(1e-10f, 0); });
            AIShip replacement = null;
            var first = Scheduled(Hull("dispatch-first-" + replace), () => {
                AIShipManager.UnregisterShip(ship);
                if (replace) replacement = Scheduled(ship, () => replacementRuns++);
            });
            // Native priority dequeue materializes both actors before the first executes.
            // Count must stay nonzero so RunAIQueue does not refill the fixture batch.
            queue.Enqueue(first); queue.PrioritizeShip(old); queue.PrioritizeShip(first);
            AccessTools.Method(typeof(AIShipManager), "RunAIQueue").Invoke(null, null);
            Check(oldRuns == (candidate ? 0 : 1), "Unexpected withdrawn controller execution");
            Check((ship.objSS.vAccRCS.x != 0) == !candidate, "Unexpected overwrite of the native stop");
            Check(replacementRuns == 0, "New registration unexpectedly joined the already-dequeued batch");
            Check(ReferenceEquals(AIShipManager.GetAIShipByRegID(ship.strRegID), replacement), "Registry identity changed unexpectedly");
            Check(replace || AIShipManager.GetAIShipByRegID(ship.strRegID) == null, "Withdrawn controller returned to registry");
            if (replace) {
                replacement.RunAI();
                Check(replacementRuns == 1, "Replacement controller was not independently executable");
            }
        }
        Console.WriteLine(candidate ? "Identity admission prevents stale dispatch without excluding replacement controllers." :
            "Native detached scheduler batches execute withdrawn controllers, including after same-hull replacement.");
    }

    static void FastForwardAfterWithdrawal(bool candidate) {
        AIShipManager.dictAIs["fixture"].Clear();
        var first = Scheduled(Hull("ffwd-first"), () => { });
        var ship = Hull("ffwd-withdrawn"); int oldRuns = 0;
        var old = Scheduled(ship, () => { });
        Set(first, "_activeCommand", new ActionCommand((Pilot)first.AICharacter, () => { }));
        Set(old, "_activeCommand", new ActionCommand((Pilot)old.AICharacter, () => { }));
        ((Pilot)first.AICharacter).OnFastForward = () => AIShipManager.UnregisterShip(ship);
        ((Pilot)old.AICharacter).OnFastForward = () => { oldRuns++; ship.objSS.vAccRCS = new Vector2(1e-10f, 0); };
        AIShipManager.FFWD(86400);
        Check(oldRuns == (candidate ? 0 : 1), "Unexpected withdrawn FFWD execution from copied registry list");
        Check((ship.objSS.vAccRCS.x != 0) == !candidate, "Unexpected FFWD overwrite of withdrawal stop");
        Check(!RegisteredIdentity(old), "Old FFWD controller unexpectedly remains registered");
    }

    static void PreservationControls() {
        foreach (bool torch in new[] { false, true }) {
            AIShipManager.dictAIs["fixture"].Clear();
            var ship = Hull("preserved-" + torch); Register(ship); Course(ship, torch);
            var other = Hull("unrelated-" + torch); var otherPlan = Course(other, !torch);
            ship.objSS.vAccEx = new Vector2(2e-12f, -3e-12f);
            ship.objSS.vAccLift = new Vector2(4e-12f, 5e-12f);
            ship.objSS.vAccDrag = new Vector2(-6e-12f, 7e-12f);
            var external = ship.objSS.vAccEx; var lift = ship.objSS.vAccLift; var drag = ship.objSS.vAccDrag;
            bool ignoredGravity = ship.objSS.bIgnoreGrav;
            ship.objSS.ResetNavData(); AIShipManager.UnregisterShip(ship);
            Check(ship.objSS.vAccEx.x == external.x && ship.objSS.vAccEx.y == external.y, "Cancellation changed external gravity input");
            Check(ship.objSS.vAccLift.x == lift.x && ship.objSS.vAccLift.y == lift.y, "Cancellation changed lift");
            Check(ship.objSS.vAccDrag.x == drag.x && ship.objSS.vAccDrag.y == drag.y, "Cancellation changed drag");
            Check(ship.objSS.bIgnoreGrav == ignoredGravity, "Candidate silently changed gravity policy");
            Check(ReferenceEquals(other.objSS.NavData, otherPlan) && other.objSS.vAccIn.x != 0 && other.objSS.vAccRCS.x != 0,
                "Cancellation affected an unrelated ship's course/input");

            // ResetNavData itself is just a plan reset. No-plan callers must not
            // become an implicit stop of freshly supplied manual/other input.
            var manual = Hull("manual-" + torch); manual.IsAIShip = false;
            manual.objSS.ResetNavData();
            Check(manual.objSS.vAccIn.x != 0 && manual.objSS.vAccRCS.x != 0, "Native plan-only reset changed manual input");
        }

        // Reproduce EngageTrip's verified native call order, without claiming to
        // execute the full Unity course console: publish new plan, withdraw old AI,
        // then add Auto. The simple withdrawal candidate erases the new plan.
        var engaging = Hull("engage-order"); Register(engaging);
        var preview = Course(engaging, true);
        engaging.objSS.ResetNavData(); AIShipManager.UnregisterShip(engaging);
        Check(engaging.objSS.NavData == null, "Candidate no longer demonstrates EngageTrip assignment-order collateral");
        engaging.objSS.NavData = preview; // Candidate caller repair: publish after withdrawal.
        Check(ReferenceEquals(engaging.objSS.NavData, preview), "Publishing the new plan after withdrawal failed");
        Console.WriteLine("Preservation controls retain other force channels and unrelated/manual controls; EngageTrip ordering requires explicit caller integration.");
    }

    static void ReplacementCourseCallback() {
        AIShipManager.dictAIs["fixture"].Clear();
        var ship = Hull("callback-replacement"); Register(ship);
        var old = Course(ship, true);
        var velocity = ship.objSS.vVelX;
        Ostranauts.ShipGUIs.Utilities.NavData replacement = null;
        UnityEngine.Events.UnityAction<Ship> listener = removed => {
            if (!ReferenceEquals(removed, ship)) return;
            replacement = Course(ship, false);
        };
        AIShipManager.AIShipUnregisteredEvent.AddListener(listener);
        try {
            ship.objSS.ResetNavData(); // Candidate cancellation is before native callbacks.
            AIShipManager.UnregisterShip(ship);
        } finally { AIShipManager.AIShipUnregisteredEvent.RemoveListener(listener); }
        Check(replacement != null && !ReferenceEquals(old, replacement), "Withdrawal callback did not install a replacement plan");
        Check(ReferenceEquals(ship.objSS.NavData, replacement), "Pre-withdrawal cancellation erased callback-owned plan");
        Check(ship.objSS.vVelX == velocity, "Cancellation changed existing velocity");
        // A blanket post-unregister reset would destroy exactly this valid replacement.
        ship.objSS.ResetNavData();
        Check(ship.objSS.NavData == null, "Broad postfix collateral counterexample was not established");
        Console.WriteLine("Pre-withdrawal cancellation preserves callback replacement; broad postfix cancellation would erase it.");
    }

    static void CommandExitChecks(Harmony fixture) {
        fixture.Patch(AccessTools.Method(typeof(CrewSim), nameof(CrewSim.TimeElapsedScaled)),
            prefix: new HarmonyMethod(typeof(Program), nameof(SmallElapsed)));
        fixture.Patch(AccessTools.Method(typeof(StarSystem), nameof(StarSystem.GetNearestStationRegional)),
            prefix: new HarmonyMethod(typeof(Program), nameof(NoStation)));

        var ship = Hull("dodge-windup"); var pilot = new Pilot(ship); var board = pilot.Blackboard;
        var threat = Hull("dodge-threat"); board.Set("DodgeTarget", threat.strRegID);
        ship.fAIDockingExpire = double.NegativeInfinity; Course(ship, true);
        var torch = ship.objSS.vAccIn;
        var status = new Dodge(pilot).RunCommand(board);
        Check(status.Status == CommandCode.Running && ship.objSS.NavData == null, "Native Dodge did not enter its route-cancelling windup");
        Check(ship.objSS.vAccIn.x == torch.x && ship.objSS.vAccIn.y == torch.y, "Dodge windup no longer retains old torch force");
        Check(ship.fAIDockingExpire > 8, "Dodge fixture unexpectedly started RCS movement");

        ship = Hull("nearest-station-failure"); Course(ship, true);
        status = new TargetNearestStation(new Pilot(ship)).RunCommand(new Blackboard());
        Check(status.Status == CommandCode.Failure && ship.objSS.NavData == null, "Native nearest-station failure did not clear the plan");
        Check(ship.objSS.vAccIn.x != 0 && ship.objSS.vAccRCS.x != 0, "Nearest-station failure no longer retains controls");

        ship = Hull("emergency-target-handoff"); Course(ship, true);
        var destination = new ShipSitu(false); board = new Blackboard();
        board.Set("EmergencySignal", new Ostranauts.Core.Models.Tuple<double, ShipSitu>(1000, destination));
        status = new TargetEmergencySignal(new Pilot(ship)).RunCommand(board);
        Check(status.Status == CommandCode.Finished && ReferenceEquals(ship.shipSituTarget, destination), "Native emergency destination was not accepted");
        Check(ship.objSS.NavData == null && ship.objSS.vAccIn.x != 0, "Emergency handoff no longer discards plan while retaining torch input");
        Console.WriteLine("Native Dodge windup, station-search failure and emergency retarget discard plans while retaining controls; caller reachability still requires classification.");
    }

    static void OwnershipBoundaryChecks(Harmony fixture) {
        fixture.Patch(AccessTools.Method(typeof(CondOwner), nameof(CondOwner.HasCond), new[] { typeof(string), typeof(bool) }),
            prefix: new HarmonyMethod(typeof(Program), nameof(NoConditions)));
        var crew = new Harmony("offline.unregistered-course.existing-crew-guard");
        try {
            crew.CreateClassProcessor(typeof(CrewedAIExecutionPatch)).Patch();
            DispatchAfterWithdrawal(false);
            FastForwardAfterWithdrawal(false);
            var candidate = new Harmony("offline.unregistered-course.dispatch-candidate");
            try {
                candidate.Patch(AccessTools.Method(typeof(AIShipManager), "RunAIQueue"), transpiler: new HarmonyMethod(typeof(Program), nameof(DispatchCandidate)));
                candidate.Patch(AccessTools.Method(typeof(AIShipManager), nameof(AIShipManager.FFWD)), transpiler: new HarmonyMethod(typeof(Program), nameof(DispatchCandidate)));
                DispatchAfterWithdrawal(true);
                FastForwardAfterWithdrawal(true);
            } finally { candidate.UnpatchSelf(); }
            ReplacementCourseCallback();
            PreservationControls();
            CommandExitChecks(fixture);
        } finally { crew.UnpatchSelf(); }
    }
}
