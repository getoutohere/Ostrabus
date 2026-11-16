using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ostrabus.Orbital;
using Ostranauts.Core;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using Ostranauts.Ships.Comms;
using Ostranauts.ShipGUIs.Utilities;

// Native Navy tree, selector/sticky phases, Undock and NavData playback execute.
// World-step bookkeeping/presentation, pilot eligibility, external combat
// observations and random target generation are explicit fixture boundaries.
static partial class SffDecisionChecks {
    static StarSystem world;
    static Ship ship;
    static AIShip controller;
    static BodyOrbit planet;
    static bool pilotAvailable, guiActive, throwOnStep;
    static int worldSteps, impacts, selections, shortPhysicsSteps;
    static double epoch;
    static DVector destinationOffset;
    static MotionState expectedEnd;
    static JObject fixture;
    static Action<double> stepAction;
    static bool omitQueue;
    static UnityEngine.Vector2? gravitySample;
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Set(object instance, string name, object value) => AccessTools.Field(instance.GetType(), name).SetValue(instance, value);
    static bool Quiet() => false;
    static bool Gui(ref bool __result) { __result = guiActive; return false; }
    static bool Available(ref bool __result) { __result = pilotAvailable; return false; }
    static bool NoPlayer(ref bool __result) { __result = false; return false; }
    static bool NoThreat(BaseCommand __instance, ref CommandStatus __result) { __result = new CommandStatus(CommandCode.Failure, __instance); return false; }
    static bool ContinueFlight(BaseCommand __instance, ref CommandStatus __result) { __result = new CommandStatus(CommandCode.Running, __instance); return false; }
    static bool OtherActor(AIShip __instance) => ReferenceEquals(__instance, controller);

    static ShipSitu Destination(double at) {
        var state = new ShipSitu(false) { bBOLocked = true, strBOPORShip = planet.strName,
            vBOOffsetx = destinationOffset.X / OrbitalMotion.KmPerAU, vBOOffsety = destinationOffset.Y / OrbitalMotion.KmPerAU };
        state.UpdateTime(at);
        return state;
    }
    static bool ChooseTarget(BaseCommand __instance, ref CommandStatus __result) {
        selections++;
        ship.shipSituTarget = Destination(StarSystem.fEpoch);
        var route = new NavData(ship);
        route.AddNavDataPoint(new NavDataPoint(StarSystem.fEpoch, new ShipSitu(ship.objSS, false), ship.fShallowRCSRemass, 0, 0, ship.RCSCount));
        var endPoint = Destination(StarSystem.fEpoch + 1800);
        // Native plotted maneuver endpoints are unlocked snapshots. Do not
        // manufacture a body-locked final waypoint with different overrun rules.
        endPoint.bBOLocked = false;
        // The following coast must be a viable orbit. A stationary endpoint
        // 1,000 km from Ceres legitimately falls back toward its surface.
        OrbitalMotion.BodyAt(planet, StarSystem.fEpoch + 1800, out var parent);
        var radius = OrbitalMotion.Read(endPoint).Position - parent.Position;
        double mu = OrbitalMotion.GravityConstant * planet.fMass;
        var velocity = parent.Velocity + new DVector(-radius.Y, radius.X) * (Math.Sqrt(mu / radius.Length) / radius.Length);
        endPoint.vVelX = velocity.X / OrbitalMotion.KmPerAU;
        endPoint.vVelY = velocity.Y / OrbitalMotion.KmPerAU;
        route.AddNavDataPoint(new NavDataPoint(StarSystem.fEpoch + 1800, endPoint, ship.fShallowRCSRemass, 0, 0, ship.RCSCount));
        var final = OrbitalMotion.Read(endPoint);
        var environment = new SffEnvironment();
        foreach (var body in world.aBOs.Values) environment.Bodies.Add(new SffBody(body));
        expectedEnd = SffDynamics.Propagate(environment, final, StarSystem.fEpoch + 1800,
            epoch + 7200 - StarSystem.fEpoch - 1800, default, null, ship.objSS.GetRadiusAU() * OrbitalMotion.KmPerAU, null).State;
        ship.objSS.NavData = route;
        __result = new CommandStatus(CommandCode.Finished, __instance);
        return false;
    }
    static bool WorldStep(double fTimeDelta) {
        if (throwOnStep) throw new InvalidOperationException("fixture step failed");
        worldSteps++;
        OrbitalStepObservationPatch.Prefix(world, fTimeDelta, out var step);
        try {
            if (fTimeDelta < 60 && step?.Motion != null) shortPhysicsSteps++;
            StarSystem.fEpoch += fTimeDelta;
            foreach (var body in world.aBOs.Values) body.UpdateTime(StarSystem.fEpoch);
            var external = ship.objSS.vAccEx;
            if (gravitySample.HasValue) ship.objSS.vAccEx += gravitySample.Value;
            ship.objSS.TimeAdvance(fTimeDelta);
            if (gravitySample.HasValue) ship.objSS.vAccEx = external;
            stepAction?.Invoke(fTimeDelta);
            OrbitalMotion.BodyAt(planet, StarSystem.fEpoch, out var center);
            if ((OrbitalMotion.Read(ship.objSS).Position - center.Position).Length < planet.fRadius * OrbitalMotion.KmPerAU + ship.objSS.GetRadiusAU() * OrbitalMotion.KmPerAU) {
                impacts++;
                ship.bDestroyed = true;
            } else if (!omitQueue) AccessTools.Method(typeof(AIShipManager), "RunAIQueue").Invoke(null, null);
        } finally { OrbitalStepObservationPatch.Finalizer(null, step); }
        return false;
    }
    static void Setup() {
        fixture = JObject.Parse(FixtureFiles.Read("PatrolDecisionImpact.json"));
        epoch = (double)fixture["epoch"];
        StarSystem.fEpoch = epoch;
        worldSteps = impacts = selections = shortPhysicsSteps = 0;
        stepAction = null;
        omitQueue = false;
        gravitySample = null;
        guiActive = pilotAvailable = true; throwOnStep = false;
        world = Empty<StarSystem>();
        world.aBOs = new Dictionary<string, BodyOrbit>();
        foreach (var row in fixture["bodies"]) {
            var body = new BodyOrbit(row.ToObject<JsonBodyOrbitSave>());
            world.aBOs.Add(body.strName, body);
        }
        planet = world.aBOs["Ceres"]; world.boStar = world.aBOs["Sol"]; planet.boParent = world.boStar;
        foreach (var body in world.aBOs.Values) body.UpdateTime(epoch);
        CrewSim.system = world;
        var state = fixture["motion"].ToObject<JsonShipSitu>();
        ship = Empty<Ship>(); ship.strRegID = "patrol-decision-fixture"; ship.IsAIShip = true;
        Set(ship, "<objSS>k__BackingField", new ShipSitu(false) { vPosx = state.vPosx, vPosy = state.vPosy,
            vVelX = state.vVelX, vVelY = state.vVelY, Size = (int)fixture["size"] });
        OrbitalMotion.BodyAt(planet, epoch, out var center);
        var relative = OrbitalMotion.Read(ship.objSS).Position - center.Position;
        destinationOffset = relative * (1000 / relative.Length);
        Set(ship, "nLoadState", Ship.Loaded.Edit); Set(ship, "_mass", (double)fixture["massKg"]);
        ship.RCSCount = (float)fixture["rcsCount"]; ship.fShallowRCSRemass = (double)fixture["fuelKg"];
        ship.nCols = ship.nRows = 4;
        Set(ship, "<Comms>k__BackingField", Empty<Comms>());
        world.dictShips = new Dictionary<string, Ship> { { ship.strRegID, ship } };
        Set(world, "dictShipOwners", new Dictionary<string, string> { { ship.strRegID, "GalCon" } });
        var pilot = new NavyPilot(null, ship);
        var branches = (List<ICommand>)AccessTools.Field(typeof(SelectorCommand), "_childCommands").GetValue(pilot.BehaviorTree);
        ((StickySequenceCommand)branches.Last()).SaveData = new[] { "3" }; // Flight finished; Dock is pending.
        controller = Empty<AIShip>();
        Set(controller, "_shipUs", ship); Set(controller, "_aiCharacter", pilot); Set(controller, "_behaviorTree", pilot.BehaviorTree);
        Set(controller, "<Blackboard>k__BackingField", pilot.Blackboard); Set(controller, "_activeCommand", new Dock(pilot));
        AIShipManager.strATCLast = "fixture-region";
        AIShipManager.dictAIs = new Dictionary<string, List<AIShip>> { { "fixture-region", new List<AIShip> { controller } } };
        var queue = (AIShipQueue)AccessTools.Field(typeof(AIShipManager), "_aIQueue").GetValue(null);
        queue.Clear();
        // A pending ordinary queue must not postpone the endangered pilot.
        // Unrelated actor bodies are outside this fixture; the native dequeue
        // and urgent-priority handling themselves remain unmodified.
        for (int i = 0; i < 10; i++) {
            var bystander = Empty<AIShip>(); var bystanderShip = Empty<Ship>();
            bystanderShip.strRegID = "queued-bystander-" + i;
            Set(bystander, "_shipUs", bystanderShip); Set(bystander, "_aiCharacter", Empty<CivilianPilot>());
            AIShipManager.dictAIs["fixture-region"].Add(bystander);
            queue.Enqueue(bystander);
        }
    }
    internal static void Run(Action<bool, string> check, bool passStudy = false) {
        var h = new Harmony("offline.orbital.decision-steps");
        var oldWorld = CrewSim.system; var oldPlayer = CrewSim.coPlayer;
        var oldAis = AIShipManager.dictAIs; string oldRegion = AIShipManager.strATCLast; double oldEpoch = StarSystem.fEpoch;
        try {
            CrewSim.coPlayer = null;
            void Prefix(Type type, string method, string callback) => h.Patch(AccessTools.Method(type, method), prefix: new HarmonyMethod(typeof(SffDecisionChecks), callback));
            Prefix(typeof(GUIFFWD), "get_Active", nameof(Gui));
            Prefix(typeof(Ship), "get_NavAIManned", nameof(Available));
            Prefix(typeof(Ship), "get_NavPlayerManned", nameof(NoPlayer));
            Prefix(typeof(Comms), nameof(Comms.GetCaptain), nameof(Quiet));
            h.Patch(AccessTools.Method(typeof(DataHandler), nameof(DataHandler.GetCondTrigger), new[] { typeof(string) }), prefix: new HarmonyMethod(typeof(SffDecisionChecks), nameof(Quiet)));
            foreach (var type in new[] { typeof(AmIInCombat), typeof(AreThreatsNearby), typeof(IsFriendlyUnderAttack) }) Prefix(type, "RunCommand", nameof(NoThreat));
            Prefix(typeof(TargetNewIfNone), "RunCommand", nameof(ChooseTarget));
            Prefix(typeof(FlyToPatrol), "RunCommand", nameof(ContinueFlight));
            Prefix(typeof(StarSystem), nameof(StarSystem.Update), nameof(WorldStep));
            Prefix(typeof(AIShip), nameof(AIShip.RunAI), nameof(OtherActor));
            h.CreateClassProcessor(typeof(SffMotionPatch)).Patch();
            if (passStudy) { PassCompletionStudy(check, h); return; }

            Setup();
            world.Update(7200);
            check(impacts == 1 && selections == 0 && worldSteps == 1, "Single SFF step did not reproduce the missed native decision/impact");

            Setup();
            object priorReport = SffSimulation.Snapshot();
            double fuel = ship.fShallowRCSRemass;
            using (var preview = SffSimulation.Preview(world, 7200)) {
                var urgent = new List<AIShip>();
                check(preview.DecisionStep(urgent) == 60 && urgent.Count == 1 && ReferenceEquals(urgent[0], controller), "Projected Ceres impact did not select its existing pilot");
                check(SffSimulation.Current == null && StarSystem.fEpoch == epoch && selections == 0 && ship.fShallowRCSRemass == fuel,
                    "Forecast changed the world, ran AI or debited fuel");
            }
            check(ReferenceEquals(SffSimulation.Snapshot(), priorReport), "Forecast replaced the last actual SFF report");
            var originalController = controller; var originalShip = ship;
            SffAdvance.Update(world, 7200);
            check(impacts == 0 && !ship.bDestroyed && selections == 1 && worldSteps == 4,
                "Decision-aware SFF failed to let native phases select the next patrol: impacts=" + impacts + " selections=" + selections + " steps=" + worldSteps + " command=" + controller.ActiveCommandName);
            check(ReferenceEquals(controller, originalController) && ReferenceEquals(ship, originalShip), "Safety step replaced ship or controller");
            double endpointError = (OrbitalMotion.Read(ship.objSS).Position - expectedEnd.Position).Length;
            check(StarSystem.fEpoch == epoch + 7200 && endpointError < .01,
                "Short steps lost requested time or native endpoint playback: seconds=" + (StarSystem.fEpoch - epoch) + " endpointErrorKm=" + endpointError);
            var report = (Dictionary<string, object>)SffAdvance.Snapshot();
            check((int)report["decisionSteps"] == 2 && !(bool)report["budgetFallback"] && !SffAdvance.Active, "Decision-aware report or cleanup is wrong");

            Setup();
            OrbitalMotion.BodyAt(planet, epoch, out var nearCenter);
            var inward = (nearCenter.Position - OrbitalMotion.Read(ship.objSS).Position);
            var inwardVelocity = inward * (1 / inward.Length);
            ship.objSS.vVelX += inwardVelocity.X / OrbitalMotion.KmPerAU;
            ship.objSS.vVelY += inwardVelocity.Y / OrbitalMotion.KmPerAU;
            SffAdvance.Update(world, 7200);
            check(impacts == 0 && selections == 1 && shortPhysicsSteps >= 2 && StarSystem.fEpoch == epoch + 7200,
                "Sub-minute danger did not retain corrected physics and native decision opportunities");

            // Prospective PASS boundary: reuse the existing motion engine with
            // its explicit isSff argument; never change the native GUI flag.
            Setup(); guiActive = false;
            OrbitalSimulation.ObserveUpdate(world, 1500);
            OrbitalMotion.BodyAt(planet, epoch, out var coastCenter);
            var radius = OrbitalMotion.Read(ship.objSS).Position - coastCenter.Position;
            double mu = OrbitalMotion.GravityConstant * planet.fMass;
            var tangent = new DVector(-radius.Y, radius.X) * (Math.Sqrt(mu / radius.Length) / radius.Length);
            ship.objSS.vVelX = (coastCenter.Velocity.X + tangent.X) / OrbitalMotion.KmPerAU;
            ship.objSS.vVelY = (coastCenter.Velocity.Y + tangent.Y) / OrbitalMotion.KmPerAU;
            using (var motion = SffSimulation.Begin(world, 1500, true)) {
                check(motion != null && !GUIFFWD.Active, "Motion engine requires the GUI flag despite explicit bulk context");
                StarSystem.fEpoch = epoch + 1500;
                ship.objSS.TimeAdvance(1500);
                OrbitalMotion.BodyAt(planet, StarSystem.fEpoch, out var endCenter);
                var endRadius = OrbitalMotion.Read(ship.objSS).Position - endCenter.Position;
                check(Math.Abs(endRadius.Length - radius.Length) < .001 && !GUIFFWD.Active,
                    "Reused motion scope failed to preserve a Ceres orbit during a 1,500-second non-GUI jump");
            }

            foreach (string exclusion in new[] { "no-pilot", "no-controller", "no-thrusters", "outside-region", "player", "locked", "hidden", "combat", "plotted", "safe-coast", "disabled", "non-sff" }) {
                Setup();
                if (exclusion == "no-pilot") pilotAvailable = false;
                if (exclusion == "no-controller") { AIShipManager.dictAIs["fixture-region"].Clear(); controller = null; }
                if (exclusion == "no-thrusters") ship.RCSCount = 0;
                if (exclusion == "outside-region") AIShipManager.strATCLast = "elsewhere";
                if (exclusion == "player") { CrewSim.coPlayer = Empty<CondOwner>(); CrewSim.coPlayer.ship = ship; }
                if (exclusion == "locked") ship.objSS.bIgnoreGrav = true;
                if (exclusion == "hidden") ship.HideFromSystem = true;
                if (exclusion == "combat") ship.shipCombatTarget = Empty<Ship>();
                if (exclusion == "plotted") { CommandStatus chosen = null; ChooseTarget(new TargetNewIfNone(controller.AICharacter), ref chosen); }
                if (exclusion == "safe-coast") {
                    OrbitalMotion.BodyAt(planet, epoch, out var center);
                    ship.objSS.vVelX += (ship.objSS.vPosx - center.Position.X / OrbitalMotion.KmPerAU) * .01;
                    ship.objSS.vVelY += (ship.objSS.vPosy - center.Position.Y / OrbitalMotion.KmPerAU) * .01;
                }
                if (exclusion == "disabled") SffSimulation.Enabled = false;
                if (exclusion == "non-sff") guiActive = false;
                SffAdvance.Update(world, 7200);
                check(worldSteps == (exclusion == "plotted" ? 2 : 1) && StarSystem.fEpoch == epoch + 7200, "Changed excluded case: " + exclusion);
                CrewSim.coPlayer = null; SffSimulation.Enabled = true;
            }
            Setup(); throwOnStep = true;
            bool threw = false;
            try { SffAdvance.Update(world, 7200); } catch (InvalidOperationException error) { threw = error.Message == "fixture step failed"; }
            check(threw && !SffAdvance.Active && SffSimulation.Current == null, "Failed world step swallowed error or leaked a scope");

            var target = SffDecisionStepsPatch.TargetMethod();
            check(target != null, "Native GUI SFF integration changed");
            h.CreateClassProcessor(typeof(SffDecisionStepsPatch)).Patch();
            var call = AccessTools.Method(typeof(StarSystem), "Update");
            foreach (int count in new[] { 0, 2 }) {
                bool refused = false;
                try { SffDecisionStepsPatch.Transpiler(Enumerable.Repeat(new CodeInstruction(OpCodes.Callvirt, call), count)).ToArray(); }
                catch (InvalidOperationException) { refused = true; }
                check(refused, "SFF integration accepted an unknown native call shape");
            }
            PassAdapterChecks(check);
            PassCompletionStudy(check, h);
            Console.WriteLine("Ceres decision-gap replay: one large update impacts; 2 native decision turns, course completion and conic coast survive with unchanged ship/controller and exact elapsed time.");
        } finally {
            h.UnpatchSelf(); SffSimulation.Current?.Dispose(); SffSimulation.Enabled = true;
            ((AIShipQueue)AccessTools.Field(typeof(AIShipManager), "_aIQueue").GetValue(null)).Clear();
            CrewSim.system = oldWorld; CrewSim.coPlayer = oldPlayer; AIShipManager.dictAIs = oldAis;
            AIShipManager.strATCLast = oldRegion; StarSystem.fEpoch = oldEpoch;
        }
    }
}
