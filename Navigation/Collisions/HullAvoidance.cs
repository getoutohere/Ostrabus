using System;
using Ostrabus.NavConsole.Outlines;

namespace Ostrabus.ShipNavigation.Collisions;

internal static class HullAvoidance {
    internal struct State {
        internal ShipSitu Us, Them;
        internal float OurRadius, TheirRadius;
    }
    [ThreadStatic] static State current;
    internal static void Begin(out State state) {state=current;current=default;}
    internal static void End(State state) => current=state;
    internal static float EscapeRadius(ShipSitu situ) {
        if (ReferenceEquals(situ,current.Us)) return current.OurRadius;
        if (ReferenceEquals(situ,current.Them)) return current.TheirRadius;
        return situ.GetRadiusAU();
    }
    // Two numeric scratch situations, never actor copies. Native WillCollide
    // remains the predictor; weapons and other callers never see these sizes.
    [ThreadStatic] static ShipSitu first, second;
    internal static bool PredictNative(ShipSitu us, ShipSitu them, double seconds, Ship ship, Ship obstacle) => Predict(us,them,seconds,ship,obstacle,false);
    internal static bool PredictFuture(ShipSitu us, ShipSitu them, double seconds, Ship ship, Ship obstacle) => Predict(us,them,seconds,ship,obstacle,true);
    static bool Predict(ShipSitu us, ShipSitu them, double seconds, Ship ship, Ship obstacle, bool futureOnly) {
        current=default;
        State candidate=default;
        if (HullDocking.Applies(ship,obstacle)) {
            // Native doubles the moving ship/group's prediction circle. Keep
            // that existing clearance policy, using a bound of the real hulls.
            if (first == null) {first=new ShipSitu(false);second=new ShipSitu(false);}
            var anchor=ship.DockGroup?.Anchor??ship.objSS;
            Copy(us,first,2*HullDocking.Bound(ship,anchor));
            Copy(them,second,HullDocking.Bound(obstacle,obstacle.DockGroup?.Anchor??obstacle.objSS));
            candidate=new State {Us=anchor,Them=them,OurRadius=first.GetRadiusAU()*.5f,TheirRadius=second.GetRadiusAU()};
            us=first;them=second;
        }
        if (futureOnly) {
            var p=us.vPos-them.vPos;var v=us.vVel-them.vVel;
            float radius=CollisionManager.GetCollisionDistanceAU(us,them);
            if (p.Dot(p)>(double)(radius*radius) && p.Dot(v)>=0) return false;
        }
        bool hit=CollisionManager.WillCollide(us,them,seconds);
        current=hit?candidate:default;
        return hit;
    }
    static void Copy(ShipSitu source, ShipSitu target, double radius) {
        target.vPosx=source.vPosx;target.vPosy=source.vPosy;
        target.vVelX=source.vVelX;target.vVelY=source.vVelY;
        target.Size=(int)Math.Min(int.MaxValue,Math.Max(source.Size,Math.Ceiling(radius)));
    }
}
