using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ostranauts.Core;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using Ostranauts.Ships.Comms;
using SolarSystem;
using UnityEngine;

static partial class Program
{
    static AIShip lifecycleController;
    static Ship lifecycleVisitor, lifecycleWreck;
    static bool lifecycleGui;
    static int lifecycleWorldSteps;
    static bool LifecycleGui(ref bool __result) { __result = lifecycleGui; return false; }
    static bool NoCaptain(out CondOwner captain) { captain = null; return false; }
    static bool NoHelp(ref bool __result) { __result = false; return false; }
    static bool HelpTrigger(ref CondTrigger __result) { __result = Empty<CondTrigger>(); return false; }
    static bool NoThreat(BaseCommand __instance, ref CommandStatus __result)
    {
        __result = new CommandStatus(CommandCode.Failure, __instance);
        return false;
    }
    static bool NoCollision(out Ostranauts.Utils.Models.Point escapePoint, ref bool __result)
    {
        escapePoint = default;
        __result = false;
        return false;
    }
    static bool FixedDelta(ref float __result) { __result = .125f; return false; }
    static bool NoAngularSteering(ref float __result) { __result = 0; return false; }

    // Calendar/scene/economy are fixture boundaries. Native orbital/group
    // movement, time-skip orchestration and AI role/queue commands execute.
    static bool LifecycleWorldStep(StarSystem __instance, double fTimeDelta)
    {
        lifecycleWorldSteps++;
        StarSystem.fEpoch += fTimeDelta;
        foreach (var body in __instance.aBOs.Values)
            body.UpdateTime(StarSystem.fEpoch);
        foreach (var ship in __instance.dictShips.Values)
            if (ship.objSS.bBOLocked || ship.objSS.bIsBO)
                ship.objSS.TimeAdvance(fTimeDelta);
        Field(__instance, "_dockingGroups", __instance.dictShips.Values.Where(s => s.HasDockGroup).ToList());
        AccessTools.Method(typeof(StarSystem), "UpdateDockingGroups").Invoke(__instance, new object[] { fTimeDelta });
        AIShipManager.FFWD(fTimeDelta);
        lifecycleController?.RunAI();
        return false;
    }

    static void SeedControl(Ship ship)
    {
        ship.IsAIShip = true;
        ship.RCSCount = 8;
        ship.fShallowRCSRemass = 1000;
        Field(ship, "nRCSCountRaw", 8);
        Field(ship, "fRCSExhaustV", 8f);
        Field(ship, "nRCSDistroCount", 1);
        Field(ship, "fFuelEfficiencyMod", 1d);
        Field(ship, "<Comms>k__BackingField", Empty<Comms>());
        Field(ship, "<ShipCO>k__BackingField", Empty<CondOwner>());
    }

    static StickySequenceCommand MainSequence(BaseCharacter pilot)
    {
        return ((List<ICommand>)AccessTools.Field(typeof(SelectorCommand), "_childCommands").GetValue(pilot.BehaviorTree))
            .OfType<StickySequenceCommand>().Last();
    }

    static AIShip Controller(BaseCharacter pilot, ICommand active)
    {
        var ai = Empty<AIShip>();
        Field(ai, "_shipUs", pilot.ShipUs);
        Field(ai, "_aiCharacter", pilot);
        Field(ai, "_behaviorTree", pilot.BehaviorTree);
        Field(ai, "_activeCommand", active);
        Field(ai, "<Blackboard>k__BackingField", pilot.Blackboard);
        AIShipManager.strATCLast = "attachment-fixture";
        AIShipManager.dictAIs = new Dictionary<string, List<AIShip>> { { "attachment-fixture", new List<AIShip> { ai } } };
        return ai;
    }

    static (Ship visitor, Ship wreck) PhysicalPair()
    {
        World();
        var world = CrewSim.system;
        world.aBOs.Clear();
        var saved = JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "PhobosBodies.json")));
        foreach (string name in new[] { "Sol", "Mars", "Phobos" })
            world.aBOs[name] = new BodyOrbit(saved[name].ToObject<JsonBodyOrbitSave>());
        world.boStar = world.aBOs["Sol"];
        var moon = world.aBOs["Phobos"];
        var mars = world.aBOs["Mars"];
        mars.boParent = world.boStar;
        moon.boParent = mars;
        world.boStar.boChildren = new List<BodyOrbit> { mars };
        mars.boChildren = new List<BodyOrbit> { moon };
        Field(world, "fGravAccelConstant", 2e-44f);
        StarSystem.fEpoch = 65634302058.282005;
        foreach (var body in world.aBOs.Values)
            body.UpdateTime(StarSystem.fEpoch);
        var visitor = Hull("VISITOR");
        var wreck = Hull("WRECK");
        foreach (var ship in new[] { visitor, wreck })
        {
            ship.objSS.vPosx = moon.dXReal + 70 / 149597872d;
            ship.objSS.vPosy = moon.dYReal;
            ship.objSS.vVelX = moon.dVelX;
            ship.objSS.vVelY = moon.dVelY;
        }
        wreck.objSS.LockToBO(moon);
        wreck.DMGStatus = Ship.Damage.Derelict;
        visitor.Dock(wreck, false, "WRECK-port", "VISITOR-port");
        return (visitor, wreck);
    }

    static void LifecycleChecks(Harmony engine)
    {
        var fixture = new Harmony("offline.docking-orbit.lifecycle.environment");
        var production = new Harmony("offline.docking-orbit.lifecycle.production");
        var plugin = typeof(Ostrabus.OstrabusPlugin).Assembly;
        var orbital = plugin.GetType("Ostrabus.Orbital.OrbitalSimulation", true);
        var enabled = AccessTools.Property(orbital, "Enabled");
        bool oldEnabled = (bool)enabled.GetValue(null);
        var oldAis = AIShipManager.dictAIs;
        string oldRegion = AIShipManager.strATCLast;
        var bodyUpdate = AccessTools.Method(typeof(BodyOrbit), nameof(BodyOrbit.UpdateTime));
        var guiGetter = AccessTools.PropertyGetter(typeof(GUIFFWD), nameof(GUIFFWD.Active));
        engine.Unpatch(bodyUpdate, HarmonyPatchType.Prefix, engine.Id);
        engine.Unpatch(guiGetter, HarmonyPatchType.Prefix, engine.Id);
        try
        {
            void Prefix(Type type, string method, string callback, int priority = Priority.Normal)
            {
                fixture.Patch(AccessTools.Method(type, method), prefix: new HarmonyMethod(typeof(Program), callback) { priority = priority });
            }
            Prefix(typeof(GUIFFWD), "get_Active", nameof(LifecycleGui), Priority.First);
            Prefix(typeof(Comms), nameof(Comms.GetCaptain), nameof(NoCaptain));
            fixture.Patch(AccessTools.Method(typeof(DataHandler), nameof(DataHandler.GetCondTrigger), new[] { typeof(string) }),
                prefix: new HarmonyMethod(typeof(Program), nameof(HelpTrigger)));
            Prefix(typeof(CondTrigger), nameof(CondTrigger.Triggered), nameof(NoHelp));
            Prefix(typeof(CondOwner), nameof(CondOwner.AddCondAmount), nameof(Skip));
            Prefix(typeof(AmIInCombat), nameof(AmIInCombat.RunCommand), nameof(NoThreat));
            Prefix(typeof(AreThreatsNearby), nameof(AreThreatsNearby.RunCommand), nameof(NoThreat));
            Prefix(typeof(AIShipManager), nameof(AIShipManager.IsOnCollisionCourse), nameof(NoCollision));
            fixture.Patch(AccessTools.Method(typeof(AIShipManager), nameof(AIShipManager.PrioritizeShip), new[] { typeof(Ship) }),
                prefix: new HarmonyMethod(typeof(Program), nameof(Skip)));
            Prefix(typeof(CrewSim), nameof(CrewSim.TimeElapsedScaled), nameof(FixedDelta));
            Prefix(typeof(AIShipManager), nameof(AIShipManager.CalculateAISteeringInput), nameof(NoAngularSteering));
            Prefix(typeof(StarSystem), nameof(StarSystem.Update), nameof(LifecycleWorldStep), Priority.Last);
            enabled.SetValue(null, true);
            foreach (string type in new[] { "Ostrabus.ShipNavigation.PassiveDockAttachmentPatch",
                "Ostrabus.ShipNavigation.PassiveUndockAttachmentPatch",
                "Ostrabus.ShipNavigation.PassiveAttachmentInputPatch", "Ostrabus.ShipNavigation.HaulAttachmentReleasePatch",
                "Ostrabus.MiscBugFixes.AiHoldRunClockPatch",
                "Ostrabus.MiscBugFixes.AiHoldEligibilityClockPatch", "Ostrabus.Orbital.OrbitalStepObservationPatch",
                "Ostrabus.Orbital.SffMotionPatch", "Ostrabus.Orbital.SffBeforeCatchupPatch", "Ostrabus.Orbital.SffCatchupPatch" })
                production.CreateClassProcessor(plugin.GetType(type, true)).Patch();

            // Real retrieving-pilot Main enters the actual native HaulShip leaf.
            foreach (bool paused in new[] { false, true })
            {
                var pair = PhysicalPair();
                lifecycleVisitor = pair.visitor;
                lifecycleWreck = pair.wreck;
                Field(CrewSim.system, "_updatedGroups", new HashSet<string>());
                Field(CrewSim.system, "dictShipOwners", new Dictionary<string, string>());
                SeedControl(lifecycleVisitor);
                Field(lifecycleWreck, "<ShipCO>k__BackingField", Empty<CondOwner>());
                var pilot = new HaulerRetrievingPilot(null, lifecycleVisitor);
                var main = MainSequence(pilot);
                main.SaveData = new[] { "4" };
                lifecycleVisitor.shipSituTarget = new ShipSitu(false) {
                    vPosx = lifecycleVisitor.objSS.vPosx + 1000 / 149597872d,
                    vPosy = lifecycleVisitor.objSS.vPosy,
                    vVelX = lifecycleVisitor.objSS.vVelX, vVelY = lifecycleVisitor.objSS.vVelY };
                lifecycleVisitor.fAIPauseTimer = paused ? StarSystem.fEpoch + 60 : 0;
                var ai = Controller(pilot, main.FindCommand(typeof(HaulShip)));
                ai.RunAI();
                Check(ai.ActiveCommandName == "HaulShip", "Retrieving pilot did not retain the native haul command.");
                if (paused)
                {
                    Check(lifecycleVisitor.DockGroup.Anchor.bBOLocked, "Paused haul released the group before movement.");
                    Check(lifecycleWreck.objSS.bBOLocked, "Paused haul discarded the member attachment needed for save restoration.");
                    var restored = new ShipSitu(lifecycleWreck.objSS.GetJSON());
                    Check(restored.bBOLocked && restored.strBOPORShip == "Phobos", "Paused-haul saved state lost its original attachment.");
                    Field(lifecycleWreck, "<objSS>k__BackingField", restored);
                    var rebuilt = new DockGroup();
                    rebuilt.AddShip(lifecycleVisitor);
                    rebuilt.AddShip(lifecycleWreck);
                    Check(rebuilt.Anchor.bBOLocked, "Paused-haul group restoration lost its body attachment.");
                    StarSystem.fEpoch += 61;
                    ai.RunAI();
                }
                Check(!lifecycleVisitor.DockGroup.Anchor.bBOLocked && !lifecycleWreck.objSS.bBOLocked,
                    "Native haul steering remained trapped by the retained attachment.");
                Check(lifecycleVisitor.objSS.vAccRCS.sqrMagnitude > 0 && lifecycleVisitor.DockGroup.Anchor.vAccRCS.sqrMagnitude > 0,
                    "Native haul departure lost the initiating RCS command.");
                Check(lifecycleVisitor.TowBracesSecured(), "Native hauler did not secure the tow before departure.");
            }

            foreach (bool pass in new[] { false, true })
            {
                var pair = PhysicalPair();
                lifecycleVisitor = pair.visitor;
                lifecycleWreck = pair.wreck;
                SeedControl(lifecycleVisitor);
                Field(CrewSim.system, "dictShipOwners", new Dictionary<string, string>());
                Field(CrewSim.system, "_updatedGroups", new HashSet<string>());
                var pilot = new CivilianPilot(null, lifecycleVisitor);
                var main = MainSequence(pilot);
                main.SaveData = new[] { "4" };
                var hold = main.FindCommand(typeof(Hold));
                lifecycleVisitor.fAIDockingExpire = 120;
                lifecycleController = Controller(pilot, hold);
                lifecycleController.RunAI();
                Check(lifecycleController.ActiveCommandName == "Hold", "Civilian did not enter native visit Hold.");
                lifecycleGui = !pass;
                lifecycleWorldSteps = 0;
                double start = StarSystem.fEpoch;
                var advance = plugin.GetType("Ostrabus.Orbital.SffAdvance", true);
                AccessTools.Method(advance, pass ? "Pass" : "Update").Invoke(null, new object[] { CrewSim.system, 7200d });
                Check(StarSystem.fEpoch == start + 7200 && lifecycleWorldSteps > 0, "Actual skip adapter did not advance the requested duration.");
                var report = (Dictionary<string, object>)AccessTools.Method(plugin.GetType("Ostrabus.Orbital.SffSimulation", true), "Snapshot").Invoke(null, null);
                Check((int)report["nativeLocked"] == 1 && (int)report["failures"] == 0,
                    "Skip did not use the intended native-locked path, or silently fell back.");
                Check(lifecycleWreck.objSS.bBOLocked && lifecycleVisitor.DockGroup.Anchor.bBOLocked,
                    "Time-skip visit lost its authored body attachment.");
                lifecycleGui = false;
                int resumed = 0;
                // Native FFWD resets this visit's remaining wait to 1-30 s.
                // Resume ordinary native AI turns rather than forcing undock.
                while (lifecycleVisitor.IsDocked() && resumed++ < 40)
                    CrewSim.system.Update(1);
                Check(!lifecycleVisitor.IsDocked() && !lifecycleVisitor.objSS.bBOLocked,
                    "Civilian visit could not naturally finish and undock after the skip.");
                Check(lifecycleWreck.objSS.bBOLocked, "Native post-skip departure left the wreck drifting.");
                Console.WriteLine("Native civilian lifecycle: " + (pass ? "PASS" : "GUI SFF") +
                    "; skip=7200 s; ordinary resume to undock=" + resumed + " s.");
            }
        }
        finally
        {
            lifecycleController = null;
            fixture.UnpatchSelf();
            production.UnpatchSelf();
            enabled.SetValue(null, oldEnabled);
            AIShipManager.dictAIs = oldAis;
            AIShipManager.strATCLast = oldRegion;
            engine.Patch(bodyUpdate, prefix: new HarmonyMethod(typeof(Program), nameof(Skip)));
            engine.Patch(guiGetter, prefix: new HarmonyMethod(typeof(Program), nameof(NoSff)));
        }
    }
}
