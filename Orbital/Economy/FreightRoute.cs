using System;
using System.Collections.Generic;
using HarmonyLib;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using Ostranauts.ShipGUIs.Utilities;

namespace Ostrabus.Orbital.Economy;

// Native route planning uses explicit NavDataPoint epochs. Never change StarSystem.fEpoch.
internal sealed class FreightRoute {
    static readonly AccessTools.FieldRef<NavData, List<NavDataPoint>> Points = AccessTools.FieldRefAccess<NavData, List<NavDataPoint>>("_navPoints");
    static readonly AccessTools.FieldRef<NavData, double> PlaybackClock = AccessTools.FieldRefAccess<NavData, double>("fEpochLast");
    readonly AIShip pilot;
    readonly Ship station;
    readonly ShipSitu target;
    readonly BodyOrbit targetBody;
    readonly SffEnvironment environment;
    readonly double start, end;
    readonly NavData course;
    readonly List<NavDataPoint> points;
    readonly MotionState initial;
    internal readonly Ship Ship;
    internal double Arrival = double.PositiveInfinity;
    internal MotionState Final;
    internal bool Delivered;
    internal SffOutcome Approach;

    internal FreightRoute(AIShip pilot, Ship station, double start, double end, SffEnvironment environment, NavData course) {
        this.pilot = pilot; Ship = pilot.Ship; this.station = station; this.start = start; this.end = end;
        this.environment = environment; this.course = course;
        target = new ShipSitu(station.objSS, createPath: false);
        if (target.strBOPORShip != null) CrewSim.system.aBOs.TryGetValue(target.strBOPORShip, out targetBody);
        initial = OrbitalMotion.Read(Ship.objSS);
        if (course != null) points = Points(course);
    }
    internal static NavData Plan(AIShip pilot, Ship destination, double departure) {
        using var scope = new FreightPlanningScope(departure);
        var planner = new FlyToPath(pilot.Ship, enableDebugLogs: false);
        var target = new ShipSitu(destination.objSS, createPath: false);
        OrbitalMotion.Write(target, StationAt(destination.objSS, departure));
        var origin = planner.CreateNavPointStatic(pilot.Ship.objSS, departure, bLockToBO: false);
        var finish = planner.CreateNavPointStatic(target, departure);
        return planner.PlanTrip4(origin, finish, 1, 1);
    }
    MotionState TargetAt(double epoch) {
        if (targetBody == null || !OrbitalMotion.BodyAt(targetBody, epoch, out var body)) throw new InvalidOperationException("Freight station has no analytical orbit.");
        return new MotionState(body.Position + new DVector(target.vBOOffsetx, target.vBOOffsety) * OrbitalMotion.KmPerAU, body.Velocity);
    }
    internal static MotionState StationAt(ShipSitu situation, double epoch) {
        if (!situation.bIsBO && !situation.bBOLocked && !situation.bOrbitLocked) throw new InvalidOperationException("Freight station is not analytically anchored.");
        return PointAt(situation, epoch);
    }
    static MotionState PointAt(ShipSitu point, double epoch) {
        if ((point.bIsBO || point.bBOLocked || point.bOrbitLocked) && point.strBOPORShip != null &&
            CrewSim.system.aBOs.TryGetValue(point.strBOPORShip, out var body) && OrbitalMotion.BodyAt(body, epoch, out var state))
            return new MotionState(state.Position + new DVector(point.vBOOffsetx, point.vBOOffsety) * OrbitalMotion.KmPerAU, state.Velocity);
        return OrbitalMotion.Read(point);
    }
    internal static MotionState Sample(NavData course, double epoch, out double rcs, out double fusion) {
        var points = Points(course);
        var a = points[0]; var b = points[points.Count - 1];
        for (int i = 1; i < points.Count; i++) if (epoch <= points[i].ArrivalTime) { a = points[i - 1]; b = points[i]; break; }
        float span = (float)(b.ArrivalTime - a.ArrivalTime), elapsed = (float)(epoch - a.ArrivalTime);
        double blend = span > 0 ? Math.Max(0, Math.Min(1, elapsed / span)) : 0;
        var ma = PointAt(a.ObjSS, epoch); var mb = PointAt(b.ObjSS, epoch);
        rcs = a.FuelLevel + (b.FuelLevel - a.FuelLevel) * blend;
        fusion = a.TorchFuelLevel + (b.TorchFuelLevel - a.TorchFuelLevel) * blend;
        return new MotionState(ma.Position + (mb.Position - ma.Position) * blend, ma.Velocity + (mb.Velocity - ma.Velocity) * blend);
    }
    internal bool Prepare() {
        if (targetBody == null || !OrbitalMotion.BodyAt(targetBody, start, out _)) return false;
        double approachStart = start;
        MotionState position = initial;
        if (course != null) {
            if (points == null || points.Count < 2 || points.Count > 4096) return false;
            double previous = double.NegativeInfinity;
            foreach (var point in points) {
                if (point?.ObjSS == null || !OrbitAcceleration.Finite(point.ArrivalTime) || point.ArrivalTime <= previous) return false;
                previous = point.ArrivalTime;
            }
            if (previous > end) { Final = Sample(course, end, out _, out _); return Final.Finite; }
            approachStart = Math.Max(start, previous);
            position = Sample(course, approachStart, out _, out _);
        }
        var goal = TargetAt(approachStart);
        if ((position.Position - goal.Position).Length < 15 && (position.Velocity - goal.Velocity).Length < .1) {
            Arrival = approachStart; Final = goal; return true;
        }
        double speed = pilot.AICharacter.MaxSpeed(false) * OrbitalMotion.KmPerAU;
        double acceleration = .5 * Math.Min(Ship.RCSAccelMax, 1.9016982118751132e-10) * OrbitalMotion.KmPerAU;
        if (!(speed > 0) || !(acceleration > 0)) return false;
        var flight = new SffFlight { Target = new StationTarget(this), Acceleration = acceleration, MaxSpeed = speed,
            MaxSpeedAtmo = pilot.AICharacter.MaxSpeed(true) * OrbitalMotion.KmPerAU, HandoffKm = 14, ArrivalSpeed = .09 };
        Approach = SffDynamics.Propagate(environment, position, approachStart, Math.Max(0, end - approachStart), default, null,
            Ship.objSS.GetRadiusAU() * OrbitalMotion.KmPerAU, flight);
        if (Approach.BudgetFallback || Approach.Impact != null || !Approach.State.Finite) return false;
        Final = Approach.State;
        if (Approach.Arrived) Arrival = approachStart + Approach.ArrivalAfter;
        return true;
    }
    internal void Apply() {
        OrbitalMotion.Write(Ship.objSS, Final);
        Ship.objSS.ResetAccelerations();
        if (course != null) {
            Sample(course, Math.Min(end, points[points.Count - 1].ArrivalTime), out double rcs, out double fusion);
            double remaining = Math.Max(0, rcs);
            if (remaining < Ship.fShallowRCSRemass) Ship.RemoveGasMass((float)(Ship.fShallowRCSRemass - remaining));
            Ship.fShallowFusionRemain = Math.Min(Ship.fShallowFusionRemain, fusion);
            PlaybackClock(course) = end;
        }
        Ship.objSS.NavData = course != null && points[points.Count - 1].ArrivalTime > end ? course : null;
        // Native targets stay bound to the real station, never a waypoint or snapshot.
        Ship.shipScanTarget = station; Ship.shipSituTarget = station.objSS; Ship.targetRegID = station.strRegID;
        Ship.TargetData = new TargetData(station);
    }
    sealed class StationTarget : ISffTarget {
        readonly FreightRoute route;
        internal StationTarget(FreightRoute route) { this.route = route; }
        public MotionState At(double epoch) => route.TargetAt(epoch);
        public DVector Acceleration(double epoch) => (At(epoch + .5).Velocity - At(epoch - .5).Velocity);
    }
}
