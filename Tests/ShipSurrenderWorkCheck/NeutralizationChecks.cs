using System;
using System.Collections.Generic;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using Ostranauts.Ships.Comms;
using Ostranauts.Ships.Sensors;
using Ostrabus.ShipNavigation;

internal static partial class Program {
    static CondOwner neutralizationCaptain;
    static Dictionary<Ship, ShipSignature> visibleForNeutralization;
    static bool NeutralizationCaptain(out CondOwner captain, ref bool __result) {
        captain = neutralizationCaptain; __result = true; return false;
    }
    static bool VisibleForNeutralization(ref Dictionary<Ship, ShipSignature> __result) {
        __result = visibleForNeutralization; return false;
    }
    static bool HostileScore(ref float __result) { __result = -100; return false; }
    static bool FactionsForNeutralization(ref List<string> __result) { __result = new List<string> { "enemy" }; return false; }
    static bool NearbyRange(ref double __result) { __result = 1e-8; return false; }
    static bool NotMoored(ref bool __result) { __result = false; return false; }

    // Always establish the unpatched native contradiction before the contrast.
    static int CheckNeutralization(bool patched = false) {
        var fixture = new Harmony("offline.neutralization.fixture");
        var production = new Harmony("offline.neutralization.production");
        try {
            fixture.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Equality"), prefix: new HarmonyMethod(typeof(Program), nameof(Equal)));
            fixture.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Inequality"), prefix: new HarmonyMethod(typeof(Program), nameof(Unequal)));
            fixture.Patch(AccessTools.Method(typeof(Comms), nameof(Comms.GetCaptain)), prefix: new HarmonyMethod(typeof(Program), nameof(NeutralizationCaptain)));
            fixture.Patch(AccessTools.Method(typeof(BaseCommand), "GetVisibleShipSignatures"), prefix: new HarmonyMethod(typeof(Program), nameof(VisibleForNeutralization)));
            fixture.Patch(AccessTools.Method(typeof(CondOwner), nameof(CondOwner.GetFactionScore), new[] { typeof(List<string>) }), prefix: new HarmonyMethod(typeof(Program), nameof(HostileScore)));
            fixture.Patch(AccessTools.Method(typeof(CondOwner), nameof(CondOwner.GetAllFactions)), prefix: new HarmonyMethod(typeof(Program), nameof(FactionsForNeutralization)));
            fixture.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.GetRangeTo), new[] { typeof(Ship) }), prefix: new HarmonyMethod(typeof(Program), nameof(NearbyRange)));
            fixture.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.IsMoored)), prefix: new HarmonyMethod(typeof(Program), nameof(NotMoored)));
            fixture.Patch(AccessTools.Method(typeof(AIShipManager), nameof(AIShipManager.GetAIShipByRegID)), prefix: new HarmonyMethod(typeof(Program), nameof(NoController)));

            neutralizationCaptain = Empty<CondOwner>(); neutralizationCaptain.bAlive = true;
            var navy = TestShip("navy"); var target = TestShip("disabled-pirate");
            var pilot = Empty<NavyPilot>(); pilot.Blackboard = new Blackboard();
            AccessTools.Field(typeof(BaseCharacter), "<CaptainUs>k__BackingField").SetValue(pilot, neutralizationCaptain);
            AccessTools.Field(typeof(BaseCharacter), "<ShipUs>k__BackingField").SetValue(pilot, navy);
            var threats = Empty<AreThreatsNearby>();
            AccessTools.Field(typeof(AreThreatsNearby), "_ai").SetValue(threats, pilot);
            AccessTools.Field(typeof(AreThreatsNearby), "_ignoreWeapons").SetValue(threats, true);
            var neutralized = Empty<IsTargetNeutralized>(); var initiate = Empty<InitiateAgainstBiggestThreat>();
            foreach (BaseCommand command in new BaseCommand[] { threats, neutralized, initiate })
                AccessTools.Field(typeof(BaseCommand), "_shipUs").SetValue(command, navy);
            visibleForNeutralization = new Dictionary<Ship, ShipSignature> { [target] = Empty<ShipSignature>() };
            var peer = Empty<AIShip>();
            AccessTools.Field(typeof(AIShip), "<Blackboard>k__BackingField").SetValue(peer, new Blackboard());

            // Pin native scope: an AI-marked hull below Full is neutralized when
            // its controller is absent. Full and non-AI targets remain eligible.
            foreach (Ship.Loaded load in new[] { Ship.Loaded.Shallow, Ship.Loaded.Edit, Ship.Loaded.Full }) {
                foreach (bool isAI in new[] { false, true }) foreach (bool registered in new[] { false, true }) {
                    target.IsAIShip = isAI;
                    AccessTools.Field(typeof(Ship), "nLoadState").SetValue(target, load);
                    controllers.Clear(); if (registered) controllers[target.strRegID] = peer;
                    var board = pilot.Blackboard = new Blackboard();
                    Check(threats.RunCommand(board).Status == CommandCode.Finished && board.Get<List<Ship>>("Enemies").Contains(target),
                        "Native detection did not acquire the hostile control case");
                    Check(initiate.RunCommand(board).Status == CommandCode.Finished && navy.shipCombatTarget == target,
                        "Native initiation did not acquire detected target");
                    bool excluded = isAI && load < Ship.Loaded.Full && !registered;
                    Check((neutralized.RunCommand(board).Status == CommandCode.Finished) == excluded,
                        "Native neutralization scope changed");
                    Check((navy.shipCombatTarget == null) == excluded && board.HasKey("Combatants") != excluded,
                        "Native combat cleanup differs from neutralization result");
                }
            }

            target.IsAIShip = true;
            AccessTools.Field(typeof(Ship), "nLoadState").SetValue(target, Ship.Loaded.Shallow);
            controllers.Clear(); var loop = pilot.Blackboard = new Blackboard();
            for (int tick = 0; tick < 6; tick++) {
                Check(threats.RunCommand(loop).Status == CommandCode.Finished, "Disabled target was not reacquired");
                Check(initiate.RunCommand(loop).Status == CommandCode.Finished && navy.shipCombatTarget == target,
                    "Reacquisition did not lock the target");
                Check(neutralized.RunCommand(loop).Status == CommandCode.Finished && navy.shipCombatTarget == null && !loop.HasKey("InCombat"),
                    "Neutralization did not reproduce the observed drop");
            }
            if (patched) {
                Console.WriteLine(checks + " unpatched native checks passed before enabling the repair.");
                production.CreateClassProcessor(typeof(NeutralizedThreatPatch)).Patch();
                CheckPatchedNeutralization(pilot, navy, target, peer, threats, initiate, neutralized);
            }
        } finally {
            controllers.Clear(); neutralizationCaptain = null; visibleForNeutralization = null;
            production.UnpatchSelf(); fixture.UnpatchSelf();
        }
        Console.WriteLine(checks + (patched ? " native baseline and production neutralization checks passed." :
            " native neutralization/threat contradiction checks passed; no Ostrabus gameplay patches installed."));
        return 0;
    }
}
