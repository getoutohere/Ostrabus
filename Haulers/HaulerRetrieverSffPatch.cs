using System;
using System.Collections.Generic;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;

namespace Ostrabus.Haulers;

// Native SFF completes pickup out of band, then queues HaulShip as a one-shot.
// Continue the native Main hauling stage instead of resuming outbound FlyTo
// against the newly selected home while the wreck is still attached.
[HarmonyPatch(typeof(HaulerRetrievingPilot), nameof(HaulerRetrievingPilot.FFWD))]
internal static class HaulerRetrieverSffPatch {
    static readonly AccessTools.FieldRef<SelectorCommand, List<ICommand>> Choices = AccessTools.FieldRefAccess<SelectorCommand, List<ICommand>>("_childCommands");
    static readonly AccessTools.FieldRef<StickySequenceCommand, List<ICommand>> Steps = AccessTools.FieldRefAccess<StickySequenceCommand, List<ICommand>>("_childCommands");
    static readonly AccessTools.FieldRef<StickySequenceCommand, int> Current = AccessTools.FieldRefAccess<StickySequenceCommand, int>("_currentChild");
    static readonly AccessTools.FieldRef<BaseCommand, string> Name = AccessTools.FieldRefAccess<BaseCommand, string>("_nodeName");
    static void Postfix(HaulerRetrievingPilot __instance, ref ICommand __result) {
        if (!(__result is HaulShip haul)) return;
        try {
            var ship = __instance.ShipUs;
            if (ship?.objSS == null || ship.bDestroyed || ship.LoadState != Ship.Loaded.Shallow || !ship.IsAIShip || ship.HideFromSystem ||
                ship.objSS.NavData != null || !ship.IsDocked() || ReferenceEquals(ship, CrewSim.coPlayer?.ship) ||
                !(__instance.BehaviorTree is SelectorCommand root)) return;
            var system = CrewSim.system;
            string player = CrewSim.coPlayer?.strID;
            if (system == null || string.IsNullOrEmpty(player)) return;
            if (system.GetShipOwner(ship.strRegID) == player) return;
            var attached = ship.GetAllDockedShips();
            if (attached == null || attached.Count == 0) return;
            foreach (var tow in attached) if (tow == null || tow.bDestroyed || !tow.IsDerelict() || tow.IsStation() ||
                ReferenceEquals(tow, CrewSim.coPlayer.ship) || system.GetShipOwner(tow.strRegID) == player) return;
            foreach (var branch in Choices(root)) {
                if (!(branch is StickySequenceCommand main) || Name(main) != "Main") continue;
                var steps = Steps(main);
                // Fail closed against a changed behavior tree; do not reinterpret
                // arbitrary commands or alter threat-response/queued work.
                if (steps.Count != 6 || !(steps[0] is Undock) || !(steps[1] is TargetNewIfNone) || !(steps[2] is FlyTo) ||
                    !(steps[3] is Dock) || !ReferenceEquals(steps[4], haul) || !(steps[5] is DockCargoAndDespawn)) return;
                int current = Current(main);
                if (current != 2 && current != 3) return;
                Current(main) = 4;
                __result = null; // AIShip.FFWD must not create the discarded one-shot.
                return;
            }
        } catch (Exception error) {
            OstrabusPlugin.Logger?.LogWarning("[Hauler Retrieval] SFF continuation retained native behavior: " + error);
        }
    }
}
