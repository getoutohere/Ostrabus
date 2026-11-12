using System;
using System.Collections.Generic;
using Ostrabus.NavConsole.Outlines;
using SolarSystem;

namespace Ostrabus.ShipNavigation.Collisions;

// A numeric radius snapshot for one explicit native safe-position search. The
// dictionary's storage is reused; all actor keys are released in the finalizer.
// No global radius override, per-frame registry or geometry capture is involved.
internal static class HullPlacement {
    internal struct State {
        internal bool Active;
        internal ShipSitu Moving;
        internal DockGroup Group;
        internal double X,Y;
    }
    [ThreadStatic] static Dictionary<ShipSitu,float> radii;
    [ThreadStatic] static State current;
    internal static void Begin(ShipSitu moving, out State state) {
        state=current;
        // Native safe searches do not recurse. Preserve an enclosing scope if
        // another mod does so, rather than clearing its snapshot underneath it.
        if (current.Active || !PhysicalCollisions.Enabled || moving == null || moving.bIsBO) return;
        var registry=CrewSim.system?.dictShips;
        if (registry == null) return;
        Ship owner=null;
        foreach (var ship in registry.Values) {
            if (ship == null || ship.bDestroyed || ship.objSS == null) continue;
            if (ReferenceEquals(ship.objSS,moving) || ReferenceEquals(ship.DockGroup?.Anchor,moving)) {owner=ship;break;}
        }
        if (owner != null && !HullDocking.Ordinary(owner)) return;
        if (radii == null) radii=new Dictionary<ShipSitu,float>();
        current=new State {Active=true,Moving=moving,Group=owner?.DockGroup,X=moving.vPosx,Y=moving.vPosy};
        if (!ReferenceEquals(current.Group?.Anchor,moving)) current.Group=null;
        foreach (var ship in registry.Values) {
            if (!HullDocking.Ordinary(ship)) continue;
            radii[ship.objSS]=Math.Max(ship.objSS.GetRadiusAU(),HullDisplay.Radius(ship)*(float)HullZoom.MetresToAU);
        }
        if (owner != null) radii[moving]=Math.Max(moving.GetRadiusAU(),(float)(HullDocking.Bound(owner,moving)*HullZoom.MetresToAU));
    }
    internal static void End(State state) {
        if (state.Active) return;
        try {
            if (current.Active && (current.X!=current.Moving.vPosx || current.Y!=current.Moving.vPosy)) PhysicalCollisions.DisplacedSitu(current.Moving);
        } finally {radii?.Clear();current=state;}
    }
    internal static float RadiusSum(ShipSitu situ, float nativeOther, float nativeMoving) {
        float native=nativeOther+nativeMoving;
        if (!current.Active) return native;
        // An anchor is moved before its members. Their old world coordinates
        // must not make its own attached hulls into spawn/undock obstacles.
        if (!ReferenceEquals(situ,current.Moving) && current.Group != null && ReferenceEquals(situ._dockGroup,current.Group)) return float.NegativeInfinity;
        // Mixed pairs with stations, asteroids, projectiles and other excluded
        // classes retain both native operands, not only the obstacle's size.
        if (!radii.TryGetValue(situ,out float other)) return native;
        float moving=radii.TryGetValue(current.Moving,out float value)?value:nativeMoving;
        return Math.Max(native,other+moving);
    }
}
