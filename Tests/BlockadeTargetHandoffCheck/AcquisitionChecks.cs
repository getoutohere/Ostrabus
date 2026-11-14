using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using Ostranauts.Ships.Sensors;
using Ostrabus.ShipNavigation;

internal static partial class Program {
    static void AcquisitionChecks() {
        foreach (Ship.Loaded load in new[] { Ship.Loaded.Shallow, Ship.Loaded.Edit, Ship.Loaded.Full })
        foreach (bool isAI in new[] { false, true }) foreach (bool registered in new[] { false, true }) {
            var pilot = Pilot(true); var target = Hull("matrix"); target.IsAIShip = isAI;
            Set(target, "nLoadState", load);
            controllers.Clear(); if (registered) controllers[target.strRegID] = Empty<AIShip>();
            visible.Clear(); visible[target] = Empty<ShipSignature>();
            int oldWarnings = warnings, oldLookups = controllerLookups;
            var result = new AreThreatsNearbyBlockade(pilot).RunCommand(pilot.Blackboard);
            bool eligible = registered || !isAI || load >= Ship.Loaded.Full;
            Check((result.Status == CommandCode.Finished) == eligible, "Acquisition eligibility differs from native neutralization scope");
            Check(pilot.Blackboard.Get<List<Ship>>("Enemies").Contains(target) == eligible, "Filtered enemy publication differs");
            Check(warnings == oldWarnings + (eligible ? 1 : 0), "Warning was suppressed for an eligible target or sent to a filtered hull");
            Check(controllerLookups == oldLookups + (isAI && load < Ship.Loaded.Full ? 1 : 0), "Unneeded/duplicate controller query");
            Check(visible.ContainsKey(target), "Acquisition mutated the shared signature dictionary");
        }
        controllers.Clear();

        foreach (bool savedFalse in new[] { false, true }) {
            var pilot = Pilot(true); var target = Hull("pending-disabled", disabled: true);
            visible.Clear(); visible[target] = Empty<ShipSignature>();
            Initiate(pilot, target);
            if (savedFalse) pilot.Blackboard.Set("InCombat", false);
            int oldLookups = controllerLookups, oldHome = homeCalls;
            Check(new AreThreatsNearbyBlockade(pilot).RunCommand(pilot.Blackboard).Status == CommandCode.Finished &&
                controllerLookups == oldLookups, "Pending combatant was filtered or queried before native cleanup");
            pilot.BehaviorTree.RunCommand(pilot.Blackboard);
            Check(homeCalls == oldHome + 1 && pilot.ShipUs.shipCombatTarget == null &&
                !pilot.Blackboard.HasKey("Combatants"), "Existing combatant did not finish native cleanup and station handoff");
            int oldWarnings = warnings;
            for (int tick = 0; tick < 6; tick++) pilot.BehaviorTree.RunCommand(pilot.Blackboard);
            Check(pilot.ShipUs.shipScanTarget == home && flightTarget == home && warnings == oldWarnings && homeCalls == oldHome + 1,
                "Disabled hull was reacquired or warned after cleanup");
        }

        var reactivation = Pilot(true); var inactive = Hull("reactivating", disabled: true);
        visible.Clear(); visible[inactive] = Empty<ShipSignature>();
        var detector = new AreThreatsNearbyBlockade(reactivation);
        int warningCount = warnings;
        Check(detector.RunCommand(reactivation.Blackboard).Status == CommandCode.Failure && warnings == warningCount,
            "Fresh inactive hull was acquired");
        controllers[inactive.strRegID] = Empty<AIShip>();
        Check(detector.RunCommand(reactivation.Blackboard).Status == CommandCode.Finished && warnings == warningCount + 1,
            "Restored controller did not immediately restore warning/acquisition eligibility");
        controllers.Clear();
        Check(detector.RunCommand(reactivation.Blackboard).Status == CommandCode.Failure, "Controller loss did not remove fresh target eligibility");
        controllers[inactive.strRegID] = Empty<AIShip>();
        Check(detector.RunCommand(reactivation.Blackboard).Status == CommandCode.Finished && warnings == warningCount + 2,
            "Native renewed warning after reactivation changed");
        controllers.Clear();

        var mixed = Pilot(true); var disabled = Hull("mixed-fresh-disabled", disabled: true); var active = Hull("mixed-active");
        visible.Clear(); visible[disabled] = Empty<ShipSignature>(); visible[active] = Empty<ShipSignature>();
        var outcome = mixed.BehaviorTree.RunCommand(mixed.Blackboard);
        Check(outcome.Status == CommandCode.Finished && mixed.ShipUs.shipCombatTarget == active &&
            mixed.Blackboard.Get<List<Ship>>("Enemies").SequenceEqual(new[] { active }), "Mixed fresh selection failed to engage the active foreign ship");
        Check(mixed.BehaviorTree.RunCommand(mixed.Blackboard).Status == CommandCode.Running && mixed.ShipUs.shipCombatTarget == active,
            "Acquisition repair interrupted legitimate combat");

        var reader = typeof(PatchProcessor).GetMethods().Single(method => method.Name == "GetOriginalInstructions" &&
            method.GetParameters().Length == 2 && !method.GetParameters()[1].ParameterType.IsByRef);
        var original = ((IEnumerable<CodeInstruction>)reader.Invoke(null, new object[] {
            AccessTools.Method(typeof(AreThreatsNearbyBlockade), nameof(AreThreatsNearbyBlockade.RunCommand)), null })).ToList();
        var corrected = BlockadeNeutralizedThreatPatch.Transpiler(original).ToList();
        Check(corrected.Count == original.Count + 4 && corrected.Count(instruction => instruction.Calls(
            AccessTools.Method(typeof(BlockadeNeutralizedThreatPatch), nameof(BlockadeNeutralizedThreatPatch.Exclude)))) == 1,
            "Transpiler inserted more than one candidate guard");
        bool rejected = false;
        try { BlockadeNeutralizedThreatPatch.Transpiler(new[] { new CodeInstruction(OpCodes.Ret) }).ToList(); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Missing native candidate layout was silently accepted");
        var damaged = original.Select(instruction => new CodeInstruction(instruction)).ToList();
        int destroyed = damaged.FindIndex(instruction => instruction.LoadsField(AccessTools.Field(typeof(Ship), nameof(Ship.bDestroyed))));
        damaged[destroyed - 2].opcode = OpCodes.Brtrue;
        rejected = false;
        try { BlockadeNeutralizedThreatPatch.Transpiler(damaged).ToList(); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Changed null/continue guard was silently accepted");
        Console.WriteLine("Blockade acquisition: native scope matrix, pending cleanup, repeat-scan stability, controller reactivation, mixed fights, lookup budget and IL drift passed.");
    }
}
