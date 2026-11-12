using System;
using System.Collections.Generic;
using HarmonyLib;
using Ostranauts.ShipGUIs.Utilities;
using Ostranauts.Ships.AIPilots;
using SolarSystem;

namespace Ostrabus.ShipNavigation.Towing;

// The member owns its native course and fuel state. The group owns movement.
// Execute that course once at group integration, then express its pose at the
// anchor so native offset propagation can move every attached hull together.
internal static partial class DockedCoursePlayback {
    static readonly AccessTools.FieldRef<NavData, Ship> CourseOwner = AccessTools.FieldRefAccess<NavData, Ship>("_shipUs");
    static readonly AccessTools.FieldRef<Ship, Dictionary<string, Ship>> Docked = AccessTools.FieldRefAccess<Ship, Dictionary<string, Ship>>("aDocked");
    static readonly Action<DockGroup> NativeAero = AccessTools.MethodDelegate<Action<DockGroup>>(AccessTools.Method(typeof(DockGroup), "CalculateAverageLiftDrag"));
    [ThreadStatic] static ShipSitu executing;
    [ThreadStatic] static DockGroup integrated;

    internal struct StepScope {
        internal DockGroup Previous;
    }

    internal static StepScope EnterStep() {
        var scope = new StepScope { Previous = integrated };
        integrated = null;
        return scope;
    }

    internal static void LeaveStep(StepScope scope) { integrated = scope.Previous; }

    internal static void PrepareStep(DockGroup group, double seconds) {
        if (seconds <= 0 || FindSource(group) == null) return;
        AverageLiftDrag(group);
        AdvanceAnchor(group.Anchor, seconds, false, group);
        integrated = group;
    }

    internal static void AverageLiftDrag(DockGroup group) {
        if (!ReferenceEquals(integrated, group)) NativeAero(group);
    }

    static bool Fixed(ShipSitu situation) => situation.bIsBO || situation.bBOLocked || situation.bOrbitLocked;

    internal static ShipSitu FindSource(DockGroup group) {
        var anchor = group?.Anchor;
        if (anchor == null || Fixed(anchor) || anchor.NavData != null) return null;
        ShipSitu source = null;
        foreach (var member in group.DockedShips) {
            var ship = member?.Ship;
            if (ship == null || ship.bDestroyed || ship.objSS == null) continue;
            var situation = ship.objSS;
            if (Fixed(situation) || member.IsMooringTarget) return null;
            if (!ReferenceEquals(situation._dockGroup, group) || situation.NavData == null) continue;
            if (!ReferenceEquals(CourseOwner(situation.NavData), ship)) return null;
            // Conflicting member plans have no native arbitration rule. Do not
            // silently choose one pilot over another or move a moored station.
            if (source != null) return null;
            source = situation;
        }
        return source;
    }

    internal static bool DeferMember(ShipSitu situation, double seconds) => seconds > 0 && situation.NavData != null &&
        situation._dockGroup != null && !ReferenceEquals(executing, situation) &&
        ReferenceEquals(FindSource(situation._dockGroup), situation);

    internal static bool BracesSecured(DockGroup group) {
        foreach (var member in group.DockedShips) {
            var ship = member?.Ship;
            if (ship == null || ship.bDestroyed) continue;
            var connections = Docked(ship);
            if (connections == null) continue;
            foreach (var connection in connections) {
                var target = connection.Value;
                if (target == null || target.bDestroyed || !ReferenceEquals(target.DockGroup, group)) continue;
                if (!ship.TowBraceSecured(target.strRegID)) return false;
            }
        }
        return true;
    }

    static bool TorchCourse(NavData course) => course != null && (course.IsTorching ||
        (course.Origin != null && course.Destination != null && course.Destination.TorchFuelLevel < course.Origin.TorchFuelLevel));

    static void AbortUnsecured(ShipSitu source) {
        var ship = CourseOwner(source.NavData);
        source.ResetNavData();
        ship.ClearShipTarget();
        ship.SetReactorGPMValue("slidCycle", "0");
        ship.SetReactorGPMValue("slidFlow", "0");
        ship.StopManeuver(bPlayerNotice: false, bNoiseOnly: false);
        ship.SetThrust(0);
        var controller = AIShipManager.GetAIShipByRegID(ship.strRegID);
        if (controller != null && controller.ActiveCommandName == "FlyToAutoPilot") AIShipManager.UnregisterShip(ship);
        if (ship.Reactor != null) ship.Reactor.AddCondAmount("IsAutopilotAborted", 1);
        ship.bChangedStatus = true;
        ship.LogAdd("Autopilot disengaged: a towing brace is released or unsecured.", StarSystem.fEpoch, bShowEpoch: true);
        UnityEngine.Debug.LogWarning("[Ostrabus] " + ship.strRegID + ": autopilot disengaged because a towing brace is released or unsecured.");
    }

    internal static void AdvanceAnchor(ShipSitu anchor, double seconds, bool ignoreAcceleration, DockGroup group) {
        if (ReferenceEquals(integrated, group)) return;
        var source = seconds > 0 ? FindSource(group) : null;
        if (source == null || !ReferenceEquals(group.Anchor, anchor)) {
            anchor.TimeAdvance(seconds, ignoreAcceleration);
            return;
        }
        var previous = executing;
        var externalAcceleration = source.vAccEx;
        var lift = source.vAccLift;
        var drag = source.vAccDrag;
        try {
            executing = source;
            // The group step supplies gravity at the group's current location.
            // Retain it for native fallback when a course completes or aborts.
            source.vAccEx = anchor.vAccEx;
            source.vAccLift = anchor.vAccLift;
            source.vAccDrag = anchor.vAccDrag;
            if (TorchCourse(source.NavData) && !BracesSecured(group)) AbortUnsecured(source);
            source.TimeAdvance(seconds, ignoreAcceleration);
            float rotation = source.fRot - source._dockOffsetRot;
            double cosine = Math.Cos(rotation), sine = Math.Sin(rotation);
            double x = source._dockOffsetPos.X, y = source._dockOffsetPos.Y;
            anchor.CopyFrom(source, bKinematicsOnly: true);
            anchor.fRot = rotation;
            anchor.vPosx = source.vPosx - (x * cosine - y * sine);
            anchor.vPosy = source.vPosy - (x * sine + y * cosine);
        } finally {
            source.vAccEx = externalAcceleration;
            source.vAccLift = lift;
            source.vAccDrag = drag;
            executing = previous;
        }
    }

    internal static double InertialMass(Ship ship) {
        double ownMass = ship.Mass;
        var group = ship.DockGroup;
        if (group == null) return ownMass;
        double total = ownMass;
        bool foundOwner = false;
        foreach (var member in group.DockedShips) {
            var other = member?.Ship;
            if (ReferenceEquals(other, ship)) { foundOwner = true; continue; }
            if (other == null || other.bDestroyed || !ReferenceEquals(other.DockGroup, group)) continue;
            double mass = other.Mass;
            if (mass > 0 && !double.IsInfinity(mass)) total += mass;
        }
        return foundOwner ? total : ownMass;
    }
}
