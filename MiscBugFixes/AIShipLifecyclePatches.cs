using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using Ostranauts.Core;

namespace Ostrabus.MiscBugFixes;

// Instant docking must observe the same already-attached precondition as normal docking.
// Keep the normal cleanup/failure transition so the behavior tree can hold, then undock.
[HarmonyPatch(typeof(Dock),nameof(Dock.ResolveInstantly))]
internal static class InstantDockStatePatch {
    static readonly Action<Dock> Cleanup = AccessTools.MethodDelegate<Action<Dock>>(AccessTools.Method(typeof(Dock),"Cleanup"));
    static readonly Func<BaseCommand,CommandStatus> Failure = AccessTools.MethodDelegate<Func<BaseCommand,CommandStatus>>(AccessTools.PropertyGetter(typeof(BaseCommand),"Failure"));
    static bool Prefix(Dock __instance, ref CommandStatus __result) {
        if (__instance.ShipUs?.IsDocked() != true) return true;
        Cleanup(__instance);
        __result = Failure(__instance);
        return false;
    }
}

// Retirement is not complete while docking links remain. Filter only the native
// hidden-hull predicate, retaining native faction, preference and random selection.
[HarmonyPatch(typeof(AIShip),nameof(AIShip.FindShipForFaction))]
internal static class DetachedReserveHullPatch {
    static bool DetachedHidden(Ship ship,bool hidden) => hidden && !ship.IsDocked();
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var getter = AccessTools.PropertyGetter(typeof(Ship),nameof(Ship.HideFromSystem));
        var filter = AccessTools.Method(typeof(DetachedReserveHullPatch),nameof(DetachedHidden));
        int matches = 0;
        foreach (var instruction in instructions) {
            if (instruction.Calls(getter)) {
                matches++;
                var duplicate = new CodeInstruction(OpCodes.Dup);
                instruction.MoveLabelsTo(duplicate); instruction.MoveBlocksTo(duplicate);
                yield return duplicate;
                yield return instruction;
                yield return new CodeInstruction(OpCodes.Call,filter);
            } else yield return instruction;
        }
        if (matches != 1) throw new InvalidOperationException("Expected one hidden-hull predicate in AIShip.FindShipForFaction; refusing to patch an unknown version.");
    }
}

internal static class CrewedAIControl {
    static readonly AccessTools.FieldRef<Ship,List<PersonSpec>> People = AccessTools.FieldRefAccess<Ship,List<PersonSpec>>("aPeople");
    const AIType NeedsCrew = AIType.Civilian | AIType.Scav | AIType.Police | AIType.Pirate | AIType.Navy |
        AIType.Blockade | AIType.HaulerCargo | AIType.HaulerDeployer | AIType.HaulerRetriever;
    internal static bool Allow(AIShip ai) {
        if (ai == null || (ai.AIType & NeedsCrew) == 0) return true;
        Ship ship = ai.Ship;
        if (ship == null || ship.bDestroyed || ship.objSS == null || ship.LoadState < Ship.Loaded.Shallow || ship.objSS.bIsBO) return true;
        if (HasCrew(ship)) return true;
        // Native unregistration stops controls but also marks the hull stale. Do not
        // introduce a disposal marker merely because a missing pilot was detected.
        bool wasStale = ship.ShipCO != null && ship.ShipCO.HasCond("IsStale",isThreshold:false);
        if (AIShipManager.dictAIs != null) AIShipManager.UnregisterShip(ship);
        ship.objSS.ResetNavData();
        ship.objSS.vAccRCS = default;
        ship.objSS.vAccIn = default;
        ship.objSS.bIgnoreGrav = false;
        if (!wasStale && ship.ShipCO != null) ship.ShipCO.ZeroCondAmount("IsStale");
        return false;
    }
    static bool HasCrew(Ship ship) {
        var people = People(ship);
        if (people == null) return false;
        var company = CrewSim.coPlayer != null ? CrewSim.coPlayer.Company : null;
        // Match native ValidateCrew's readiness policy, without allocating GetPeople's
        // result list on each scheduled AI invocation. No work runs on a background thread.
        for (int i=0;i<people.Count;i++) {
            var person = people[i]?.GetCO();
            if (person != null && !person.bDestroyed && person.bAlive && ReferenceEquals(person.ship,ship) &&
                !person.HasCond("Unconscious",isThreshold:false) &&
                (company?.mapRoster == null || !company.mapRoster.ContainsKey(person.strID))) return true;
        }
        return false;
    }
    internal static void Validate(Ship ship) {
        if (ship == null || AIShipManager.dictAIs == null) return;
        var ai = AIShipManager.GetAIShipByRegID(ship.strRegID);
        if (ai != null) Allow(ai);
    }
}

// Registration is after native pilot creation or saved crew restoration. Auto,
// Station, Missile and Arena controllers retain their native control policy.
[HarmonyPatch(typeof(AIShipManager),nameof(AIShipManager.AddAIToDict))]
internal static class CrewedAIRegistrationPatch {
    static void Postfix(AIShip __0) { CrewedAIControl.Allow(__0); }
}

[HarmonyPatch]
internal static class CrewedAIExecutionPatch {
    static IEnumerable<MethodBase> TargetMethods() {
        yield return AccessTools.Method(typeof(AIShip),nameof(AIShip.RunAI));
        yield return AccessTools.Method(typeof(AIShip),nameof(AIShip.FFWD));
    }
    static bool Prefix(AIShip __instance) => CrewedAIControl.Allow(__instance);
}

// The shallow undock path bypasses CrewSim's materialized crew validation.
[HarmonyPatch(typeof(Ship),nameof(Ship.Undock))]
internal static class ShallowUndockCrewPatch {
    static void Postfix(Ship __instance,Ship __0) {
        if (__instance.LoadState > Ship.Loaded.Shallow && (__0 == null || __0.LoadState > Ship.Loaded.Shallow)) return;
        CrewedAIControl.Validate(__instance);
        CrewedAIControl.Validate(__0);
    }
}
