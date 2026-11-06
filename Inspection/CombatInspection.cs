using System;
using System.Collections.Generic;
using System.Diagnostics;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;

namespace Ostrabus.Inspection;

internal static class CombatInspection {
    static readonly AccessTools.FieldRef<CondOwner, List<string>> COFactions = AccessTools.FieldRefAccess<CondOwner, List<string>>("aFactions");
    static readonly AccessTools.FieldRef<Blackboard, Dictionary<string, object>> BoardData = AccessTools.FieldRefAccess<Blackboard, Dictionary<string, object>>("_data");
    static readonly AccessTools.FieldRef<AIShip, ICommand> ActiveCommand = AccessTools.FieldRefAccess<AIShip, ICommand>("_activeCommand");
    static readonly AccessTools.FieldRef<AIShip, ICommand> ActiveReaction = AccessTools.FieldRefAccess<AIShip, ICommand>("_activeReaction");
    static readonly AccessTools.FieldRef<ComeHere, ICommand> ComeHereCommand = AccessTools.FieldRefAccess<ComeHere, ICommand>("_activeCommand");
    static readonly string[] Keys = {
        "InCombat", "Enemies", "Combatants", "ClosestEnemy", "SurrenderedTo", "SurrenderedToUs",
        "EnagementProfile", "EnagementRange", "DodgeTarget", "TripFuelPrediction",
        "NeedsRepairs", "RejectedPaths", "ShipboardAction.ReturnHome", "ShipboardAction.ReturnRouteQueued",
        "CeresNavigation.ReturnRouteQueued"
    };
    static Dictionary<string, object> Map(params object[] pairs) { return SnapshotJson.Map(pairs); }
    static bool OverBudget(long start) { return Stopwatch.GetTimestamp() - start >= Stopwatch.Frequency / 1000; }

    internal static object PersonFactions(CondOwner co, long start) {
        if (co == null) { return null; }
        var source = COFactions(co);
        var items = new List<object>();
        if (source != null) {
            for (int i = 0; i < source.Count && i < 32 && !OverBudget(start); i++) { items.Add(source[i]); }
        }
        return Map("items", items, "count", source?.Count, "truncated", source != null && items.Count < source.Count);
    }

    internal static object Factions(Ship ship, long start) {
        var factions = ship.GetShipFactions(); // Getter returns the existing list; no captain/trigger evaluation.
        var items = new List<object>();
        for (int i = 0; i < factions.Count && i < 32 && !OverBudget(start); i++) {
            var faction = factions[i];
            items.Add(faction == null ? null : Map("id", faction.strName, "name", faction.strNameFriendly));
        }
        return Map("ownerId", CrewSim.system.GetShipOwner(ship.strRegID), "shipFactions", items,
            "shipFactionCount", factions.Count, "shipFactionsTruncated", items.Count < factions.Count,
            "shipCOFactions", PersonFactions(ship.ShipCO, start),
            "note", "Ship, ship CO, and AI captain affiliations are distinct. No faction score or reputation calculation is executed.");
    }

    internal static object AI(Ship ship, AIShip ai, long start) {
        var character = ai.AICharacter;
        var captain = character?.CaptainUs;
        var board = ai.Blackboard ?? character?.Blackboard;
        var command = ActiveCommand(ai);
        var reaction = ActiveReaction(ai);
        var target = ship.shipCombatTarget;
        return Map("aiType", character == null ? null : (object)character.AIType.ToString(),
            "captain", captain == null ? null : Map("id", captain.strID, "name", captain.strName,
                "alive", captain.bAlive, "destroyed", captain.bDestroyed, "shipId", captain.ship?.strRegID,
                "factions", PersonFactions(captain, start)),
            "affiliations", Factions(ship, start), "combatTarget", ShipReference(target),
            "combatTargetAffiliations", target == null ? null : Factions(target, start),
            "activeCommandType", command?.GetType().Name, "activeReactionType", reaction?.GetType().Name,
            "comeHereStepType", command is ComeHere comeHere ? ComeHereCommand(comeHere)?.GetType().Name : null,
            "blackboardSource", ai.Blackboard != null ? "AIShip" : (board != null ? "AICharacter" : null),
            "blackboard", BlackboardSnapshot(board, start),
            "note", "Stored AI state, not proof of current firing. No RunCommand, CanExecute, GetCaptain, or trigger checks are called.");
    }

    internal static object BlackboardSnapshot(Blackboard board, long start) {
        if (board == null) { return null; }
        var source = BoardData(board);
        var fields = new Dictionary<string, object>();
        foreach (string key in Keys) {
            object value = null;
            bool present = source != null && source.TryGetValue(key, out value);
            fields.Add(key, Map("present", present, "value", present ? Value(value, start) : null));
        }
        return Map("fields", fields, "totalKeyCount", source?.Count,
            "note", "Only allowlisted keys are read. EnagementProfile/EnagementRange preserve the game's spelling. Missing differs from false/empty; state may be stale.");
    }

    static object ShipReference(Ship ship) {
        return ship == null ? null : Map("id", ship.strRegID, "name", ship.publicName, "destroyed", ship.bDestroyed);
    }

    static object Value(object value, long start) {
        if (value == null || value is string || value is bool || value is int || value is long || value is float || value is double) { return value; }
        if (value is Ship ship) { return ShipReference(ship); }
        if (value is List<Ship> ships) {
            var items = new List<object>();
            for (int i = 0; i < ships.Count && i < 32 && !OverBudget(start); i++) { items.Add(ShipReference(ships[i])); }
            return Map("items", items, "count", ships.Count, "truncated", items.Count < ships.Count);
        }
        // Never serialize arbitrary objects, enumerate arbitrary sequences, or call ToString.
        return Map("unsupportedType", value.GetType().FullName);
    }
}
