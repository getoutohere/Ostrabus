using System;
using HarmonyLib;
using SolarSystem;

namespace Ostrabus.MiscBugFixes;

// StarSystem updates individual ships before advancing their groups. Anchored
// groups skip gravity propagation; copy an anchored member's native result here.
[HarmonyPatch(typeof(DockGroup), nameof(DockGroup.TimeAdvance))]
internal static class DockedGravityPatch {
    static readonly AccessTools.FieldRef<DockGroupMember, Ship> StoredShip = AccessTools.FieldRefAccess<DockGroupMember, Ship>("_ship");
    static bool Anchored(ShipSitu situation) => situation != null && (situation.bIsBO || situation.bBOLocked || situation.bOrbitLocked);
    static void Postfix(DockGroup __instance, double timeDelta) {
        if (!(timeDelta > 0) || !Anchored(__instance.Anchor)) return;
        foreach (var entry in __instance.DockedShips) {
            Ship source = entry == null ? null : StoredShip(entry);
            if (source == null || source.bDestroyed || source.LoadState < Ship.Loaded.Shallow || source.bNeedsGravSet
                || !Anchored(source.objSS) || !ReferenceEquals(source.DockGroup, __instance)) continue;
            Synchronize(source);
            return;
        }
    }
    internal static void Synchronize(Ship source) {
        if (source == null || source.bDestroyed || source.LoadState < Ship.Loaded.Shallow || !Anchored(source.objSS)) return;
        var group = source.DockGroup;
        if (group == null || !Anchored(group.Anchor)) return;
        double gravity = source.Gravity;
        if (gravity < 0 || double.IsNaN(gravity) || double.IsInfinity(gravity)) return;
        foreach (var entry in group.DockedShips) {
            // Avoid DockGroupMember.Ship's lazy reacquisition of removed ships.
            Ship member = entry == null ? null : StoredShip(entry);
            if (member == null || member.bDestroyed || ReferenceEquals(member, source) || member.LoadState < Ship.Loaded.Edit
                || member.objSS == null || !ReferenceEquals(member.DockGroup, group) || Anchored(member.objSS)
                || entry.IsMooringTarget || member.Gravity == gravity) continue;
            bool initializing = member.bNeedsGravSet;
            member.bNeedsGravSet = true;
            try {
                // Native setter refreshes crew encumbrance/StatGrav. This repairs
                // stale bookkeeping; it must not cause maneuver wear or knockdown.
                member.Gravity = gravity;
            } finally { member.bNeedsGravSet = initializing; }
        }
    }
}
