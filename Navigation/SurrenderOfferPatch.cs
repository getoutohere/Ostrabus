using System;
using System.Collections.Generic;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;

namespace Ostrabus.ShipNavigation;

// Retain the native leaf's candidates and message eligibility. Commit its
// surrender side effects only for opponents to whom an offer actually exists.
[HarmonyPatch(typeof(SurrenderOffer), nameof(SurrenderOffer.RunCommand))]
internal static class SurrenderOfferPatch {
    static readonly Func<BaseCommand, CommandStatus> Failure = AccessTools.MethodDelegate<Func<BaseCommand, CommandStatus>>(
        AccessTools.PropertyGetter(typeof(BaseCommand), "Failure"));
    static readonly Func<BaseCommand, CommandStatus> Success = AccessTools.MethodDelegate<Func<BaseCommand, CommandStatus>>(
        AccessTools.PropertyGetter(typeof(BaseCommand), "Success"));

    internal static CommandStatus Run(SurrenderOffer command, Blackboard board) {
        Ship ship = command.ShipUs;
        List<Ship> source = board.Get<List<Ship>>("Combatants");
        bool threatsOnly = source == null || source.Count == 0;
        if (threatsOnly) source = board.Get<List<Ship>>("Enemies");
        if (source == null || source.Count == 0) return Failure(command);

        // Native fallback filters Enemies in place. Stage candidates instead:
        // a failed offer must not erase the perception/engagement it came from.
        var candidates = new List<Ship>(source.Count);
        foreach (Ship opponent in source) {
            if (opponent == null || opponent.bDestroyed || threatsOnly && opponent.shipCombatTarget != ship || candidates.Contains(opponent)) continue;
            candidates.Add(opponent);
        }
        if (candidates.Count == 0) return Failure(command);
        List<Ship> remaining = null;
        List<Ship> offered = board.Get<List<Ship>>("SurrenderedTo");
        int completed = 0;
        bool copied = false;
        try {
            foreach (Ship opponent in candidates) {
                bool exists = offered != null && offered.Contains(opponent);
                if (!exists && !ship.Comms.SendMessage("STKShipSurrender_AIOffer_Intro", opponent.strRegID)) continue;
                if (!exists) {
                    // Copy only once, after the first successful send. Preserve
                    // missing/empty/existing board state when every send fails.
                    if (!copied) {
                        offered = offered == null ? new List<Ship>() : new List<Ship>(offered);
                        copied = true;
                    }
                    offered.Add(opponent);
                }
                remaining ??= new List<Ship>(candidates);
                remaining.Remove(opponent);
                completed++;
                if (ship.shipCombatTarget == opponent) ship.shipCombatTarget = null;
                AIShip other = AIShipManager.GetAIShipByRegID(opponent.strRegID);
                other?.Blackboard.Get<List<Ship>>("Combatants")?.Remove(ship);
            }
        } finally {
            // An exception after one message was sent cannot undo that message.
            // Retain its committed outcome and every unattempted/failed foe.
            if (completed > 0) {
                board.Set("SurrenderedTo", offered);
                board.Set("InCombat", remaining.Count > 0);
                if (remaining.Count == 0) board.Remove("Combatants");
                else board.Set("Combatants", remaining);
            }
        }
        // A partial offer is not permission to run the all-clear/return branch.
        return completed > 0 && remaining.Count == 0 ? Success(command) : Failure(command);
    }

    static bool Prefix(SurrenderOffer __instance, Blackboard blackboard, ref CommandStatus __result) {
        __result = Run(__instance, blackboard);
        return false;
    }
}
