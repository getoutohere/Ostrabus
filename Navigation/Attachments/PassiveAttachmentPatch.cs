using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Ostranauts.Ships.Commands;
using SolarSystem;

namespace Ostrabus.ShipNavigation;

// Preserve an existing authored body attachment for a passive visit. This does
// not attach free ships, override AI waiting policy or choose a new reference.
internal static class PassiveAttachment
{
    internal static void DockUnlock(ShipSitu situation, Ship owner, Ship target)
    {
        // Native fixed-target precedence remains authoritative when both sides
        // have an attachment. Stations also retain their native handling.
        if (situation.bBOLocked && !situation.bIsBO && !target.IsBOLockedInAnyWay(owner.strRegID))
            return;
        situation.UnlockFromBO();
    }

    internal static bool CanRelease(DockGroup group)
    {
        var anchor = group?.Anchor;
        if (anchor == null || !anchor.bBOLocked || anchor.bIsBO || anchor.bOrbitLocked)
            return false;
        foreach (var member in group.DockedShips)
        {
            var ship = member?.Ship;
            if (ship == null || ship.bDestroyed || ship.objSS == null || ship.objSS.bIsBO ||
                ship.objSS.bGrounded || ship.objSS.bOrbitLocked || member.IsMooringTarget ||
                ship.IsStation(bIgnoreDocks: true) || ship.IsSubStation() || ship.IsGroundStation())
                return false;
        }
        return true;
    }

    internal static bool Release(DockGroup group)
    {
        if (!CanRelease(group))
            return false;
        var anchor = group.Anchor;

        // Follow the old attachment to this epoch before releasing it. The
        // anchor carries the group's current orbital velocity and offsets.
        anchor.UpdateTime(StarSystem.fEpoch, false, false);
        group.TimeAdvance(0);
        foreach (var member in group.DockedShips)
        {
            var situation = member.Ship.objSS;
            // HaulShip may already have cleared bBOLocked before issuing input.
            // Clear its stale orbital rate too, without touching AI ignore flags.
            situation.UnlockFromBO();
        }
        anchor.UnlockFromBO();
        return true;
    }

    internal static void HaulUnlock(ShipSitu situation, bool locked)
    {
        var anchor = situation._dockGroup?.Anchor;
        // HaulShip prepares a tow before checking its pause deadline. Keep the
        // member's saved attachment coherent with a retained soft group lock;
        // applied steering releases both through SyncShipInput instead.
        if (!locked && anchor?.bBOLocked == true && !anchor.bIsBO && !anchor.bOrbitLocked)
            return;
        situation.bBOLocked = locked;
    }

    internal static void Input(DockGroup group, ShipSitu current)
    {
        if (!ReferenceEquals(current?._dockGroup, group) ||
            current.vAccRCS.x == 0 && current.vAccRCS.y == 0 &&
            current.vAccIn.x == 0 && current.vAccIn.y == 0 && current.fA == 0)
            return;

        var rcs = current.vAccRCS;
        var torch = current.vAccIn;
        float angular = current.fA, spin = current.fW;
        if (!Release(group))
            return;
        // Zero-time native group alignment copies the previous anchor's inputs
        // onto members. Retain the applied input that initiated this release.
        current.vAccRCS = rcs;
        current.vAccIn = torch;
        current.fA = angular;
        current.fW = spin;
    }
}

[HarmonyPatch(typeof(Ship), nameof(Ship.Dock), new[] { typeof(Ship), typeof(bool), typeof(string), typeof(string) })]
internal static class PassiveDockAttachmentPatch
{
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = new List<CodeInstruction>(instructions);
        var unlock = AccessTools.Method(typeof(ShipSitu), nameof(ShipSitu.UnlockFromBO));
        int count = 0;
        foreach (var instruction in code)
            if (instruction.Calls(unlock))
                count++;
        if (count != 1)
            throw new InvalidOperationException("Expected one native docking body unlock; passive attachment requires review.");

        foreach (var instruction in code)
        {
            if (instruction.Calls(unlock))
            {
                var owner = new CodeInstruction(OpCodes.Ldarg_0);
                instruction.MoveLabelsTo(owner);
                instruction.MoveBlocksTo(owner);
                yield return owner;
                yield return new CodeInstruction(OpCodes.Ldarg_1);
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(PassiveAttachment), nameof(PassiveAttachment.DockUnlock));
            }
            yield return instruction;
        }
    }
}

[HarmonyPatch(typeof(DockGroup), nameof(DockGroup.SyncShipInput))]
internal static class PassiveAttachmentInputPatch
{
    static void Prefix(DockGroup __instance, ShipSitu currentSitu) => PassiveAttachment.Input(__instance, currentSitu);
}

[HarmonyPatch(typeof(HaulShip), nameof(HaulShip.RunCommand))]
internal static class HaulAttachmentReleasePatch
{
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = new List<CodeInstruction>(instructions);
        var locked = AccessTools.Field(typeof(ShipSitu), nameof(ShipSitu.bBOLocked));
        int count = 0;
        for (int i = 0; i < code.Count; i++)
        {
            if (!code[i].StoresField(locked))
                continue;
            if (i == 0 || code[i - 1].opcode != OpCodes.Ldc_I4_0)
                throw new InvalidOperationException("Native haul attachment release changed; refusing an unknown write.");
            code[i].opcode = OpCodes.Call;
            code[i].operand = AccessTools.Method(typeof(PassiveAttachment), nameof(PassiveAttachment.HaulUnlock));
            count++;
        }
        if (count != 1)
            throw new InvalidOperationException("Expected one haul body unlock; passive attachment requires review.");
        return code;
    }
}
