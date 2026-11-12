using System;
using HarmonyLib;
using Ostrabus.NavConsole.Outlines;

namespace Ostrabus.ShipNavigation.Collisions;

// Docking's own symbolic bounding circles must enclose the same hulls as its
// physical collision model. Shared combat/planet/avoidance radius helpers stay
// native. No actor cache, geometry scan, snapshot or worker wait is added here.
internal static class HullDocking {
    static readonly AccessTools.FieldRef<Ostranauts.ShipGUIs.NavStation.NavModBase,CondOwner> MooringOwner =
        AccessTools.FieldRefAccess<Ostranauts.ShipGUIs.NavStation.NavModBase,CondOwner>("COSelf");
    internal static bool Ordinary(Ship ship) {
        if (ship == null || ship.bDestroyed || ship.objSS == null || ship.objSS.bIsBO ||
            ship.Classification != Ship.TypeClassification.Default || StationDockedCollisions.ActualStation(ship)) return false;
        // Keep station-docked chains native regardless of the independent
        // station-collision exemption setting. Native group lists are reused.
        var members = ship.DockGroup?.DockedShips;
        if (members != null) for (int i = 0; i < members.Count; i++)
            if (StationDockedCollisions.ActualStation(members[i]?.Ship)) return false;
        if (members == null && StationDockedCollisions.HasStationConnection(ship)) return false;
        return true;
    }
    internal static bool Applies(Ship a, Ship b) => PhysicalCollisions.Enabled && Ordinary(a) && Ordinary(b);
    internal static float RadiusSum(Ship a, Ship b, float native) {
        if (!Applies(a,b)) return native;
        // Radius reads the immutable published envelope even during native
        // shallow/full reconstruction. Before any publication it uses the
        // existing two-dimensional header bound, without blocking a frame.
        bool shared=a.DockGroup != null && ReferenceEquals(a.DockGroup,b.DockGroup);
        double radius = shared ? (double)HullDisplay.Radius(a)+HullDisplay.Radius(b) : Around(a)+Around(b);
        double au = radius*HullZoom.MetresToAU;
        return radius > 0 && !double.IsInfinity(au) && !double.IsNaN(au) && au <= float.MaxValue ? (float)au : native;
    }
    internal static float DistanceAU(Ship a, Ship b) => RadiusSum(a,b,CollisionManager.GetCollisionDistanceAU(a,b));
    internal static float PlacementRadius(float nativeA, float nativeB, Ship ship, Ship target) => RadiusSum(ship,target,nativeA+nativeB);
    internal static float InspectionRange(float native, Ostranauts.Ships.Commands.ShakeDown command, Ship target) {
        if (!Applies(command.ShipUs,target)) return native;
        // FlyTo hands off at three bounding radii. Native inspection's 5km
        // minimum normally hides its smaller two-radius acceptance threshold;
        // a large hull/group exposes that mismatch. Accept the same handoff,
        // rather than making the pilot repeatedly fly to an already-reached
        // waypoint until its inspection timer expires.
        return Math.Max(native,DistanceAU(command.ShipUs,target)*3);
    }
    internal static float OrbitPlacementRadius(float nativeA, float nativeB, ShipSitu moving, ShipSitu target) {
        float native = nativeA+nativeB;
        if (!PhysicalCollisions.Enabled || moving == null || target == null || moving.bIsBO || target.bIsBO) return native;
        var registry = CrewSim.system?.dictShips;
        if (registry == null) return native;
        Ship first = null, second = null;
        // Anonymous ShipSitu points have no owner backlink. Placement is a rare
        // explicit operation, not a tick query: resolve exact live identities in
        // one allocation-free pass, without a persistent inverse actor registry.
        foreach (var ship in registry.Values) {
            if (ship == null || ship.bDestroyed || ship.objSS == null) continue;
            if (ReferenceEquals(ship.objSS,moving) || ReferenceEquals(ship.DockGroup?.Anchor,moving)) first=ship;
            if (ReferenceEquals(ship.objSS,target) || ReferenceEquals(ship.DockGroup?.Anchor,target)) second=ship;
            if (first != null && second != null) break;
        }
        if (!Applies(first,second)) return native;
        double radius = Bound(first,moving)+Bound(second,target);
        double au = radius*HullZoom.MetresToAU;
        // Native SetSituToSafeCoords still checks its original circles after
        // pushback. Satisfy both models rather than invoking its random retry.
        return radius > 0 && !double.IsInfinity(au) && !double.IsNaN(au) && au <= float.MaxValue ? Math.Max(native,(float)au) : native;
    }
    internal static double Bound(Ship owner, ShipSitu situ) {
        var group=owner.DockGroup;
        if (group == null) return HullDisplay.Radius(owner);
        if (!ReferenceEquals(group.Anchor,situ)) return Around(owner);
        double radius=0;
        foreach (var member in group.DockedShips) {
            var ship=member?.Ship;
            if (ship == null || ship.bDestroyed || ship.objSS == null || !ReferenceEquals(ship.DockGroup,group)) continue;
            // Offsets remain valid while native pushback has moved the anchor
            // but has not propagated its new position to the members yet.
            double distance=ship.objSS._dockOffsetPos.magnitude/HullZoom.MetresToAU;
            radius=Math.Max(radius,distance+HullDisplay.Radius(ship));
        }
        return radius;
    }
    static double Around(Ship ship) {
        double radius=HullDisplay.Radius(ship);
        var group=ship.DockGroup;
        if (group == null) return radius;
        foreach (var member in group.DockedShips) {
            var peer=member?.Ship;
            if (peer == null || peer.bDestroyed || peer.objSS == null || !ReferenceEquals(peer.DockGroup,group)) continue;
            double offset=(peer.objSS._dockOffsetPos-ship.objSS._dockOffsetPos).magnitude/HullZoom.MetresToAU;
            radius=Math.Max(radius,offset+HullDisplay.Radius(peer));
        }
        return radius;
    }
    internal static float MooringDistance(ShipSitu a, ShipSitu b, Ostranauts.ShipGUIs.NavStation.NavModMooringControl panel) {
        var us=MooringOwner(panel)?.ship;
        var them=GUIOrbitDraw.CrossHairTarget?.Ship;
        float native=CollisionManager.GetCollisionDistanceAU(a,b);
        return us != null && them != null && ReferenceEquals(a,them.objSS) && ReferenceEquals(b,us.objSS) ? RadiusSum(us,them,native) : native;
    }
    internal static float RingOuterLimit(float native, GUIDockSys panel, Ship target) {
        var ship = panel.COSelf?.ship;
        if (!Applies(ship,target)) return native;
        // Native ring interpolation divides by (10 km - contact radius).
        // A large hull must still have a positive approach window.
        return Math.Max(native,DistanceAU(ship,target)*1.5f);
    }
}
