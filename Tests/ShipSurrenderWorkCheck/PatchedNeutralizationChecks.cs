using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using Ostranauts.Ships.Sensors;
using Ostrabus.ShipNavigation;

internal static partial class Program {
    static void CheckPatchedNeutralization(NavyPilot pilot, Ship navy, Ship target, AIShip peer,
        AreThreatsNearby threats, InitiateAgainstBiggestThreat initiate, IsTargetNeutralized neutralized) {
        Blackboard Fresh() {
            navy.shipCombatTarget = null;
            return pilot.Blackboard = new Blackboard();
        }
        void Register(bool registered) {
            controllers.Clear();
            if (registered) controllers[target.strRegID] = peer;
        }
        // Same native matrix as the reproduction, now at acquisition itself.
        foreach (Ship.Loaded load in new[] { Ship.Loaded.Shallow, Ship.Loaded.Edit, Ship.Loaded.Full }) {
            foreach (bool isAI in new[] { false, true }) foreach (bool registered in new[] { false, true }) {
                target.IsAIShip = isAI;
                AccessTools.Field(typeof(Ship), "nLoadState").SetValue(target, load);
                Register(registered); var board = Fresh();
                bool excluded = isAI && load < Ship.Loaded.Full && !registered;
                int before = controllerLookups;
                Check((threats.RunCommand(board).Status == CommandCode.Failure) == excluded, "Acquisition scope mismatch");
                Check(controllerLookups == before + 1 && visibleForNeutralization.Count == 1,
                    "Filter rescans controllers or mutates the sensor contact collection");
                Check(board.Get<List<Ship>>("Enemies").Contains(target) != excluded, "Filtered enemy list mismatch");
                Check((initiate.RunCommand(board).Status == CommandCode.Failure) == excluded, "Disabled hull reacquired through native initiation");
                Check((navy.shipCombatTarget == null) == excluded, "Wrong combat lock after acquisition");
            }
        }

        target.IsAIShip = true;
        AccessTools.Field(typeof(Ship), "nLoadState").SetValue(target, Ship.Loaded.Shallow);
        // Both genuine current combat and stale Combatants with InCombat=false
        // must reach the original neutralization body instead of stranding a lock.
        foreach (bool inCombat in new[] { false, true }) {
            Register(false); var board = Fresh();
            board.Set("Combatants", new List<Ship> { target });
            board.Set("InCombat", inCombat); navy.shipCombatTarget = target;
            Check(threats.RunCommand(board).Status == CommandCode.Finished, "Existing combatant cannot reach native cleanup");
            if (!inCombat) Check(initiate.RunCommand(board).Status == CommandCode.Finished, "Stale combatant cannot finish native engagement cleanup");
            Check(neutralized.RunCommand(board).Status == CommandCode.Finished && navy.shipCombatTarget == null &&
                !board.HasKey("Combatants") && !board.HasKey("InCombat"), "Native cleanup was bypassed");
            for (int tick = 0; tick < 6; tick++) {
                Check(threats.RunCommand(board).Status == CommandCode.Failure, "Disabled target reappeared after cleanup");
                Check(initiate.RunCommand(board).Status == CommandCode.Failure && navy.shipCombatTarget == null,
                    "Acquire/drop loop persisted");
            }
        }

        // Controller restoration is observed without a remembered exclusion.
        var reactivation = Fresh(); Register(false);
        Check(threats.RunCommand(reactivation).Status == CommandCode.Failure, "Unregistered target not excluded");
        Register(true);
        Check(threats.RunCommand(reactivation).Status == CommandCode.Finished && initiate.RunCommand(reactivation).Status == CommandCode.Finished,
            "Restored pilot still protected by stale exclusion");
        Register(false);
        Check(threats.RunCommand(reactivation).Status == CommandCode.Finished && neutralized.RunCommand(reactivation).Status == CommandCode.Finished,
            "New disablement failed native cleanup");
        Check(threats.RunCommand(reactivation).Status == CommandCode.Failure, "Newly disabled target reacquired");

        // A disabled hull must not hide another opponent or end a mixed fight.
        var active = TestShip("active-enemy"); active.IsAIShip = true;
        AccessTools.Field(typeof(Ship), "nLoadState").SetValue(active, Ship.Loaded.Shallow);
        controllers[active.strRegID] = peer;
        visibleForNeutralization[active] = Empty<ShipSignature>();
        var mixed = Fresh(); mixed.Set("Combatants", new List<Ship> { target, active }); mixed.Set("InCombat", true);
        navy.shipCombatTarget = target;
        Check(threats.RunCommand(mixed).Status == CommandCode.Finished, "Mixed encounter lost active opponent");
        Check(neutralized.RunCommand(mixed).Status == CommandCode.Failure && navy.shipCombatTarget == active,
            "Native cleanup failed to retain/retarget active opponent");
        Check(mixed.Get<List<Ship>>("Combatants").SequenceEqual(new[] { active }), "Mixed combatants corrupted");
        Check(threats.RunCommand(mixed).Status == CommandCode.Finished &&
            mixed.Get<List<Ship>>("Enemies").SequenceEqual(new[] { active }), "Disabled hull remained in mixed threat scan");

        visibleForNeutralization.Remove(active);
        foreach (Ship.TypeClassification kind in new[] { Ship.TypeClassification.SignalBeacon, Ship.TypeClassification.Asteroid }) {
            target.IsAIShip = false; target.Classification = kind;
            Check(threats.RunCommand(Fresh()).Status == CommandCode.Finished, "Non-AI static target made immune");
        }
        target.Classification = Ship.TypeClassification.Default; target.IsAIShip = true;
        AccessTools.Field(typeof(Ship), "nLoadState").SetValue(target, Ship.Loaded.Full);
        Check(threats.RunCommand(Fresh()).Status == CommandCode.Finished, "Fully materialized/manual target made immune");

        // Preserve native surrender exclusion for an otherwise eligible pilot.
        AccessTools.Field(typeof(Ship), "nLoadState").SetValue(target, Ship.Loaded.Shallow); Register(true);
        peer.Blackboard.Set("SurrenderedTo", new List<Ship> { navy });
        Check(threats.RunCommand(Fresh()).Status == CommandCode.Failure, "Native surrender exclusion lost");
        peer.Blackboard.Remove("SurrenderedTo");
        Check(threats.RunCommand(Fresh()).Status == CommandCode.Finished, "Surrender cleanup changed ordinary eligibility");

        // Refuse changed native layouts instead of silently binding no patch
        // or inserting this return guard at multiple unrelated lookup sites.
        var lookup = AccessTools.Method(typeof(AIShipManager), nameof(AIShipManager.GetAIShipByRegID));
        foreach (int sites in new[] { 0, 2 }) {
            var generator = new DynamicMethod("fixture", typeof(void), Type.EmptyTypes).GetILGenerator();
            bool rejected = false;
            try {
                NeutralizedThreatPatch.Transpiler(Enumerable.Range(0, sites)
                    .Select(_ => new CodeInstruction(OpCodes.Call, lookup)), generator).ToArray();
            } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Changed native lookup layout was silently accepted");
        }
    }
}
