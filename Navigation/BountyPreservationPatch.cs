using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Ostranauts.Systems;

namespace Ostrabus.ShipNavigation;

// Intentional simulation policy: active contracts reserve their actual target
// and assigned hull across administrative retirement. No global Destroy/Kill
// veto, additional AI controller, persistent index or per-frame polling.
internal static class BountyPreservation {
    internal static JsonBountySave Contract(CondOwner person) {
        if (person == null || person.bDestroyed || BountyManager.Bounties == null) return null;
        foreach (var bounty in BountyManager.Bounties)
            if (bounty != null && bounty.strTargetID == person.strID) return bounty;
        return null;
    }

    internal static bool Target(CondOwner person) => Contract(person) != null;

    internal static bool Reserved(Ship ship) {
        if (ship == null || ship.bDestroyed || BountyManager.Bounties == null) return false;
        foreach (var bounty in BountyManager.Bounties) {
            if (bounty == null) continue;
            if (!string.IsNullOrEmpty(bounty.strTargetRegId) && bounty.strTargetRegId == ship.strRegID) return true;
            if (!string.IsNullOrEmpty(bounty.strTargetID) && DataHandler.mapCOs != null && DataHandler.mapCOs.TryGetValue(bounty.strTargetID, out var person) &&
                person != null && !person.bDestroyed && ReferenceEquals(person.ship, ship)) return true;
        }
        return false;
    }

    internal static void Unclaim(CondOwner person, string id) {
        // Retain an existing claim, never create ownership or undo a capture.
        if (Contract(person)?.strTargetRegId != id) person.UnclaimShip(id);
    }

    internal static void RegionalDisposal(Ship ship, bool isDespawning) {
        if (!Reserved(ship)) { ship.Destroy(isDespawning); return; }
        if (!isDespawning) return; // Derelict sweep: preserve the actual hull in place.
        // AIShipCleanup has already run native CrewCleanup and hidden visuals.
        // Park the same instance; BountyManager can later reboard it by reg ID.
        ship.HideFromSystem = true;
        foreach (var bounty in BountyManager.Bounties) {
            if (bounty?.strTargetRegId != ship.strRegID || DataHandler.mapCOs == null ||
                !DataHandler.mapCOs.TryGetValue(bounty.strTargetID, out var person)) continue;
            var station = person?.ship;
            if (station != null && !ReferenceEquals(station, ship) && station.IsStation())
                ship.strAIDespawnedAt = station.strRegID;
        }
        AIShipManager.UnregisterShip(ship);
    }

    internal static List<Ship> StaleCandidates(IEnumerable<Ship> ships) {
        // Called on the main thread, before native Task.Run captures its list.
        // Never enumerate the mutable bounty registry on the cleanup worker.
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (BountyManager.Bounties != null) foreach (var bounty in BountyManager.Bounties) {
            if (bounty == null) continue;
            if (!string.IsNullOrEmpty(bounty.strTargetRegId)) ids.Add(bounty.strTargetRegId);
            if (!string.IsNullOrEmpty(bounty.strTargetID) && DataHandler.mapCOs != null &&
                DataHandler.mapCOs.TryGetValue(bounty.strTargetID, out var person) && person != null && !person.bDestroyed && person.ship != null)
                ids.Add(person.ship.strRegID);
        }
        var result = new List<Ship>();
        foreach (var ship in ships) if (ship == null || !ids.Contains(ship.strRegID)) result.Add(ship);
        return result;
    }
}

[HarmonyPatch]
internal static class BountyRetirementPatch {
    static bool LoadsLocal(CodeInstruction instruction) => instruction.opcode == OpCodes.Ldloc || instruction.opcode == OpCodes.Ldloc_S ||
        instruction.opcode == OpCodes.Ldloc_0 || instruction.opcode == OpCodes.Ldloc_1 || instruction.opcode == OpCodes.Ldloc_2 || instruction.opcode == OpCodes.Ldloc_3;
    static IEnumerable<MethodBase> TargetMethods() {
        yield return AccessTools.Method(typeof(CrewSim), nameof(CrewSim.DockAndDespawn));
        yield return AccessTools.Method(typeof(AIShipManager), nameof(AIShipManager.CrewCleanup));
    }

    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod) {
        var code = new List<CodeInstruction>(instructions);
        var unclaim = AccessTools.Method(typeof(CondOwner), nameof(CondOwner.UnclaimShip));
        var replacement = AccessTools.Method(typeof(BountyPreservation), nameof(BountyPreservation.Unclaim));
        int count = 0;
        foreach (var instruction in code) if (instruction.Calls(unclaim)) {
            instruction.opcode = OpCodes.Call;
            instruction.operand = replacement;
            count++;
        }
        if (count != 1) throw new InvalidOperationException("Bounty retirement: expected one native ownership release in " + __originalMethod);
        if (__originalMethod.DeclaringType != typeof(CrewSim)) return code;

        // Both rejection causes (authored drop-off trigger and reputation) enter
        // this block. Only active targets instead enter the ORIGINAL transfer
        // branch, including its logging, company rules and WanderSoon setup.
        var destroy = AccessTools.Method(typeof(CondOwner), nameof(CondOwner.Destroy));
        int dead = code.FindIndex(i => i.Calls(destroy));
        int log = code.FindIndex(i => i.opcode == OpCodes.Ldstr && Equals(i.operand, "NAV_LOG_CREW_DESTROY"));
        int rejected = log - 3;
        if (code.Count(i => i.Calls(destroy)) != 1 ||
            code.Count(i => i.opcode == OpCodes.Ldstr && Equals(i.operand, "NAV_LOG_CREW_DESTROY")) != 1 || rejected < 1 || dead < log ||
            code[rejected].opcode != OpCodes.Ldarg_0 || !LoadsLocal(code[rejected + 1]) ||
            !code[rejected + 2].Calls(AccessTools.PropertyGetter(typeof(CondOwner), nameof(CondOwner.strID))) ||
            code[rejected - 1].opcode.FlowControl != FlowControl.Cond_Branch ||
            !(code[rejected - 1].opcode == OpCodes.Brtrue || code[rejected - 1].opcode == OpCodes.Brtrue_S) ||
            dead + 2 >= code.Count || code[dead + 1].opcode.FlowControl != FlowControl.Branch ||
            !LoadsLocal(code[dead + 2]) ||
            code[dead + 2].opcode != code[rejected + 1].opcode || !Equals(code[dead + 2].operand, code[rejected + 1].operand) ||
            !((System.Collections.IList)AccessTools.Field(typeof(CodeInstruction), "labels").GetValue(code[dead + 2])).Contains(code[rejected - 1].operand))
            throw new InvalidOperationException("Bounty retirement: unknown rejected/accepted station drop-off branches.");
        var load = new CodeInstruction(code[rejected + 1].opcode, code[rejected + 1].operand).MoveLabelsFrom(code[rejected]);
        code.InsertRange(rejected, new[] {
            load,
            new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(BountyPreservation), nameof(BountyPreservation.Target))),
            new CodeInstruction(OpCodes.Brtrue, code[rejected - 1].operand)
        });
        return code;
    }
}

[HarmonyPatch]
internal static class BountyRegionalDisposalPatch {
    static IEnumerable<MethodBase> TargetMethods() {
        yield return AccessTools.Method(typeof(AIShipManager), "AIShipCleanup");
        yield return AccessTools.Method(typeof(AIShipManager), "DerelictCleanup");
    }
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var code = new List<CodeInstruction>(instructions);
        var destroy = AccessTools.Method(typeof(Ship), nameof(Ship.Destroy));
        int count = 0;
        foreach (var instruction in code) if (instruction.Calls(destroy)) {
            instruction.opcode = OpCodes.Call;
            instruction.operand = AccessTools.Method(typeof(BountyPreservation), nameof(BountyPreservation.RegionalDisposal));
            count++;
        }
        if (count != 1) throw new InvalidOperationException("Bounty regional disposal: expected one native hull destruction call.");
        return code;
    }
}

[HarmonyPatch(typeof(StarSystem), "CleanupStaleShips")]
internal static class BountyStaleCandidatesPatch {
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var code = new List<CodeInstruction>(instructions);
        var snapshot = AccessTools.Method(typeof(Enumerable), nameof(Enumerable.ToList)).MakeGenericMethod(typeof(Ship));
        int count = 0;
        foreach (var instruction in code) if (instruction.Calls(snapshot)) {
            instruction.operand = AccessTools.Method(typeof(BountyPreservation), nameof(BountyPreservation.StaleCandidates));
            count++;
        }
        if (count != 1) throw new InvalidOperationException("Bounty stale cleanup: expected one main-thread ship snapshot.");
        return code;
    }
}
