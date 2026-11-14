using System;
using System.Collections.Generic;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using Ostranauts.Ships.Sensors;
using Ostrabus.ShipNavigation;

static partial class Program {
    static int attemptedShots, attemptedSteering, continuedFlights;

    sealed class FlightBoundary : FlyTo {
        readonly bool observe;
        internal FlightBoundary(Pilot pilot, bool observe = false) : base(pilot) { this.observe = observe; }
        public override CommandStatus RunCommand(Blackboard board) {
            if (observe) continuedFlights++;
            return Running;
        }
    }

    static bool FixtureRange(ref double __result) { __result = 1000000; return false; }
    static bool FixtureAcceleration(ref double __result) { __result = 1e-10; return false; }
    static bool NoLowFuel(ref bool __result) { __result = false; return false; }
    static bool NoSteeringRotation(ref float __result) { __result = 0; return false; }
    static bool ObserveShot(Shoot __instance, ref CommandStatus __result) {
        attemptedShots++;
        __result = new CommandStatus(CommandCode.Finished, __instance);
        return false;
    }
    static bool ObserveManeuver(float fDeltaTime) {
        if (fDeltaTime <= .01f) return true; // Execute the actual native withdrawal stop.
        attemptedSteering++;
        return false;
    }

    // Specific native terminal return sites, not a global behavior-status rewrite.
    static IEnumerable<CodeInstruction> RetreatTerminalCandidate(IEnumerable<CodeInstruction> instructions) {
        var code = new List<CodeInstruction>(instructions);
        var withdrawal = AccessTools.Method(typeof(AIShipManager), nameof(AIShipManager.UnregisterShip), new[] { typeof(Ship) });
        var failure = AccessTools.PropertyGetter(typeof(BaseCommand), "Failure");
        int matches = 0;
        for (int i = 0; i < code.Count; i++) {
            if (!code[i].Calls(withdrawal)) continue;
            if (i + 3 >= code.Count || !code[i + 2].Calls(failure))
                throw new InvalidOperationException("Unknown retreat terminal return");
            code[i + 2].operand = AccessTools.PropertyGetter(typeof(BaseCommand), "Success");
            matches++;
        }
        if (matches != 1) throw new InvalidOperationException("Expected one retreat withdrawal return");
        return code;
    }
    static IEnumerable<CodeInstruction> TargetTerminalCandidate(IEnumerable<CodeInstruction> instructions) {
        int matches = 0;
        foreach (var instruction in instructions) {
            if (instruction.Calls(AccessTools.PropertyGetter(typeof(BaseCommand), "Failure"))) {
                instruction.operand = AccessTools.PropertyGetter(typeof(BaseCommand), "Success");
                matches++;
            }
            yield return instruction;
        }
        if (matches != 2) throw new InvalidOperationException("Expected exactly two terminal target-search failure returns");
    }

    static void RetreatConsumer(bool longRange, bool terminal, bool repaired) {
        ClearControllers(); StarSystem.fEpoch = 1000;
        var ship = Hull("retreat-continuation-" + longRange + "-" + terminal + "-" + repaired);
        var ai = Register(ship); var pilot = (Pilot)ai.AICharacter;
        var threat = Hull("retreat-opponent"); threat.objSS.vPosx += .000002;
        ship.shipCombatTarget = threat;
        Set(ship, "<ElectronicSystems>k__BackingField", Empty<ElectronicSystems>());
        AccessTools.Field(typeof(AIShipManager), "_shipATCLast").SetValue(null, Hull("reference-station"));
        BaseCommand consumer = longRange ? (BaseCommand)new KeepAtDistance(pilot) : new KeepWithinFiringDistance(pilot);
        var retreat = (LowFuelRetreat)AccessTools.Field(consumer.GetType(), "_lowFuelRetreat").GetValue(consumer);
        Set(retreat, "_retreating", terminal);
        Set(retreat, "_targetSelected", true);
        Set(retreat, "_flyTo", new FlightBoundary(pilot));
        if (longRange) Set(consumer, "_activeCommand", new FlightBoundary(pilot, observe: true));
        ship.fShallowRCSRemass = terminal ? 0 : 1000;
        attemptedShots = attemptedSteering = continuedFlights = 0;
        var result = consumer.RunCommand(pilot.Blackboard);
        bool stopped = terminal && repaired;
        Check(RegisteredIdentity(ai) == !terminal, "Retreat fixture did not reach native fuel withdrawal");
        Check(attemptedShots == (stopped ? 0 : 1), "Unexpected combat continuation after retreat removal");
        Check((attemptedSteering + continuedFlights > 0) == !stopped, "Unexpected steering continuation after retreat removal");
        Check(result.Status == (stopped ? CommandCode.Finished : CommandCode.Running), "Unexpected consumer terminal status");
    }

    static void FailedTargetParent(bool repaired) {
        ClearControllers(); StarSystem.fEpoch = 1000;
        var ship = Hull("target-parent-" + repaired);
        var ai = Register(ship); var pilot = (Pilot)ai.AICharacter;
        ship.ClearShipTarget(); ship.fLastVisit = 1;
        CrewSim.coPlayer = ConsoleOwner(Hull("target-parent-player"));
        var parent = new StickySequenceCommand("Main", new ICommand[] {
            new TargetNewIfNone(pilot), new FlightBoundary(pilot, observe: true)
        });
        continuedFlights = 0;
        var result = parent.RunCommand(pilot.Blackboard);
        Check(!RegisteredIdentity(ai), "Failed target fixture did not retire its native controller");
        Check(continuedFlights == (repaired ? 0 : 1), "Unexpected same-invocation target/flight continuation");
        Check(result.Status == (repaired ? CommandCode.Finished : CommandCode.Running), "Unexpected terminal target-parent status");
    }

    static void RealFailedTargetContinuation(bool repaired) {
        ClearControllers(); StarSystem.fEpoch = 1000;
        var ship = Hull("real-target-parent-" + repaired);
        var ai = Register(ship); var pilot = (Pilot)ai.AICharacter;
        ship.ClearShipTarget(); ship.fLastVisit = 1;
        CrewSim.coPlayer = ConsoleOwner(Hull("real-target-player"));
        var parent = new StickySequenceCommand("Main", new ICommand[] {
            new TargetNewIfNone(pilot), new FlyTo(pilot), new Dock(pilot)
        });
        attemptedSteering = 0;
        parent.RunCommand(pilot.Blackboard);
        Check(!RegisteredIdentity(ai), "Real target parent did not retire its controller");
        Check(attemptedSteering == 0 && ship.objSS.NavData == null && ship.objSS.vAccIn.x == 0 && ship.objSS.vAccRCS.x == 0,
            "Real no-target FlyTo/Dock continuation unexpectedly resumed flight");
        // The counting leaf above proves traversal, not a current force leak.
        // Native FlyTo and Dock both reject the missing target. Therefore no
        // TargetNewIfNone status change belongs in the proposed force repair.
    }

    static void SetupContinuationFixtures(Harmony fixture) {
        void Patch(Type type, string name, string handler, Type[] parameters = null) => fixture.Patch(
            AccessTools.Method(type, name, parameters), prefix: new HarmonyMethod(typeof(Program), handler));
        Patch(typeof(UnityEngine.Debug), nameof(UnityEngine.Debug.Log), nameof(Quiet), new[] { typeof(object) });
        fixture.Patch(AccessTools.Constructor(typeof(ShipSignature), new[] { typeof(Ship), typeof(Ship) }),
            prefix: new HarmonyMethod(typeof(Program), nameof(Quiet)));
        Patch(typeof(ElectronicSystems), nameof(ElectronicSystems.GetSensorRangeKm), nameof(FixtureRange));
        Patch(typeof(Ship), "get_RCSAccelMax", nameof(FixtureAcceleration));
        Patch(typeof(Ship), nameof(Ship.Maneuver), nameof(ObserveManeuver));
        Patch(typeof(CrewSim), nameof(CrewSim.TimeElapsedScaled), nameof(SmallElapsed));
        Patch(typeof(AIShipManager), nameof(AIShipManager.CalculateAISteeringInput), nameof(NoSteeringRotation));
        Patch(typeof(AIShipManager), nameof(AIShipManager.LowFuel), nameof(NoLowFuel));
        Patch(typeof(Ship), "get_IsLocalAuthority", nameof(NoLowFuel));
        Patch(typeof(Shoot), nameof(Shoot.RunCommand), nameof(ObserveShot));
        Patch(typeof(StarSystem), nameof(StarSystem.GetNearestStation), nameof(NoStation));
    }

    static void ContinuationAudit(Harmony fixture) {
        SetupContinuationFixtures(fixture);
        var cancellation = new Harmony("offline.continuation-audit.withdrawal-candidate");
        var terminal = new Harmony("offline.continuation-audit.terminal-return-candidate");
        var progress = new Harmony("offline.continuation-audit.existing-retreat-progress");
        try {
            cancellation.Patch(AccessTools.Method(typeof(AIShipManager), "UnregisterShip", new[] { typeof(AIShip) }),
                prefix: new HarmonyMethod(typeof(Program), nameof(CancelRegisteredOnly)));
            foreach (bool existingProgress in new[] { false, true }) {
                if (existingProgress) progress.CreateClassProcessor(typeof(LowFuelRetreatProgressPatch)).Patch();
                foreach (bool repaired in new[] { false, true }) {
                    if (repaired) {
                        terminal.Patch(AccessTools.Method(typeof(LowFuelRetreat), nameof(LowFuelRetreat.RunCommand)),
                            transpiler: new HarmonyMethod(typeof(Program), nameof(RetreatTerminalCandidate)));
                        terminal.Patch(AccessTools.Method(typeof(TargetNewIfNone), nameof(TargetNewIfNone.RunCommand)),
                            transpiler: new HarmonyMethod(typeof(Program), nameof(TargetTerminalCandidate)));
                    }
                    foreach (bool longRange in new[] { false, true }) foreach (bool retires in new[] { false, true })
                        RetreatConsumer(longRange, retires, repaired);
                    FailedTargetParent(repaired);
                    RealFailedTargetContinuation(repaired);
                }
                terminal.UnpatchSelf();
            }
        } finally {
            progress.UnpatchSelf(); terminal.UnpatchSelf(); cancellation.UnpatchSelf(); CrewSim.coPlayer = null;
        }
    }
}
