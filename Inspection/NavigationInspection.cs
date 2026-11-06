using System;
using System.Collections.Generic;
using System.Diagnostics;
using HarmonyLib;
using Ostranauts.ShipGUIs.Utilities;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;

namespace Ostrabus.Inspection;

internal static class NavigationInspection {
    static readonly AccessTools.FieldRef<ShipSitu, double> SituVelocityX = AccessTools.FieldRefAccess<ShipSitu, double>("_vVelX");
    static readonly AccessTools.FieldRef<ShipSitu, double> SituVelocityY = AccessTools.FieldRefAccess<ShipSitu, double>("_vVelY");
    static readonly AccessTools.FieldRef<Ship, List<CondOwner>> RcsThrusters = AccessTools.FieldRefAccess<Ship, List<CondOwner>>("aRCSThrusters");
    static readonly AccessTools.FieldRef<Ship, int> RcsDistributionCount = AccessTools.FieldRefAccess<Ship, int>("nRCSDistroCount");
    static readonly AccessTools.FieldRef<Ship, float> RcsCount = AccessTools.FieldRefAccess<Ship, float>("fRCSCount");
    static readonly AccessTools.FieldRef<Ship, double> FuelEfficiency = AccessTools.FieldRefAccess<Ship, double>("fFuelEfficiencyMod");
    static readonly AccessTools.FieldRef<Ship, bool> TorchDriveThrusting = AccessTools.FieldRefAccess<Ship, bool>("bTorchDriveThrusting");
    static readonly AccessTools.FieldRef<NavData, List<NavDataPoint>> NavPoints = AccessTools.FieldRefAccess<NavData, List<NavDataPoint>>("_navPoints");
    static readonly AccessTools.FieldRef<NavData, double> NavPlanLastEpoch = AccessTools.FieldRefAccess<NavData, double>("fEpochLast");
    static readonly AccessTools.FieldRef<AIShip, ICommand> AIActiveCommand = AccessTools.FieldRefAccess<AIShip, ICommand>("_activeCommand");
    static readonly AccessTools.FieldRef<QueuedCommandsNode, ICommand> QueuedCurrent = AccessTools.FieldRefAccess<QueuedCommandsNode, ICommand>("currentCommand");
    static readonly AccessTools.FieldRef<QueuedCommandsNode, Queue<ICommand>> QueuedCommands = AccessTools.FieldRefAccess<QueuedCommandsNode, Queue<ICommand>>("commandQueue");
    static readonly AccessTools.FieldRef<StickySequenceCommand, List<ICommand>> StickyChildren = AccessTools.FieldRefAccess<StickySequenceCommand, List<ICommand>>("_childCommands");
    static readonly AccessTools.FieldRef<StickySequenceCommand, int> StickyCurrentChild = AccessTools.FieldRefAccess<StickySequenceCommand, int>("_currentChild");
    static readonly AccessTools.FieldRef<SequenceCommand, List<ICommand>> SequenceChildren = AccessTools.FieldRefAccess<SequenceCommand, List<ICommand>>("_childCommands");
    static readonly AccessTools.FieldRef<SequenceCommand, int> SequenceCurrentChild = AccessTools.FieldRefAccess<SequenceCommand, int>("_currentChild");
    static readonly AccessTools.FieldRef<FlyTo, ICommand> FlyToPath = AccessTools.FieldRefAccess<FlyTo, ICommand>("_flyToPath");
    static readonly AccessTools.FieldRef<FlyTo, ICommand> FlyToManual = AccessTools.FieldRefAccess<FlyTo, ICommand>("_flyToManual");
    const int MaxNavPoints = 16;
    const int MaxCommandDepth = 6;
    const double KilometersPerAu = 149597870.7;

    static bool OverBudget(long start) { return Stopwatch.GetTimestamp() - start >= Stopwatch.Frequency / 1000; }
    static Dictionary<string, object> Map(params object[] pairs) { return SnapshotJson.Map(pairs); }
    static string Limit(string value) { return value == null || value.Length <= 256 ? value : value.Substring(0, 256) + "[truncated]"; }

    internal static object ShipRoute(Ship ship, long start) {
        if (ship == null) { return null; }
        ShipSitu current = ship.objSS;
        TargetData targetData = ship.TargetData;
        ShipSitu targetSituation = ship.shipSituTarget;
        return Map("scanTargetShipId", ship.shipScanTarget?.strRegID,
            "stationKeepingTargetShipId", ship.shipStationKeepingTarget?.strRegID,
            "combatTargetShipId", ship.shipCombatTarget?.strRegID,
            "targetRegId", ship.targetRegID,
            "targetData", targetData == null ? null : Map("targetRegId", targetData.TargetRegId,
                "shipId", targetData.Ship?.strRegID, "hasValidTarget", targetData.HasValidTarget,
                "targetIsBodyOrbitLocked", targetData.TargetIsBoLocked,
                "situationMatchesShipTarget", ReferenceEquals(targetData.Situ, targetSituation),
                "situation", ReferenceEquals(targetData.Situ, targetSituation) ? null : Situation(targetData.Situ)),
            "situationTarget", Situation(targetSituation),
            "currentMotion", Motion(current),
            "motionRelativeToScanTarget", RelativeMotion(current, ship.shipScanTarget?.objSS, ship.shipScanTarget?.strRegID),
            "currentNavPlan", NavPlan(current?.NavData, StarSystem.fEpoch, start),
            "stationReturn", StationReturnForInspection(ship),
            "vorbNavigationGuard", VorbGuardForInspection(ship),
            "propulsion", PropulsionForInspection(ship),
            "lowFuelPolicy", Map("tankFractionThreshold", AIShipManager.LowFuelThreshold,
                "remainingDeltaVMaxSpeedMultiplier", AIShipManager.LowFuelDeltaVMulti,
                "evaluated", false,
                "note", "Game thresholds only. LowFuel was not called because it may scan fuel canisters for full/edit ships."));
    }

    static object StationReturnForInspection(Ship ship) {
        var ai = AIShipManager.dictAIs == null ? null : AIShipManager.GetAIShipByRegID(ship.strRegID);
        var board = ai?.Blackboard;
        return board == null ? null : Map("destinationShipId", board.Get<string>(global::Ostrabus.ShipNavigation.StationReturn.DestinationKey),
            "docking", board.Get<bool>(global::Ostrabus.ShipNavigation.StationReturn.DockingKey));
    }

    static object VorbGuardForInspection(Ship ship) {
        if (!Venus.VorbNavPlanGuard.TryGetState(ship, out Venus.VorbNavPlanGuard.RouteState state)) return null;
        return Map("manualFallbackRequested", state.ManualFallback, "reason", state.Reason,
            "targetShipId", state.TargetId, "rejectedPlansForTarget", state.Rejections,
            "rebasedRcsFuelKg", state.FuelAddedKg);
    }

    internal static object Commands(AIShip ai, long start) {
        if (ai == null) { return null; }
        var chain = new List<object>();
        ICommand command = AIActiveCommand(ai);
        if (command == null && ai.AICharacter?.PriorityQueue != null) { command = ai.AICharacter.PriorityQueue; }
        bool truncated = false;
        for (int depth = 0; command != null && depth < MaxCommandDepth; depth++) {
            if (OverBudget(start)) { truncated = true; break; }
            var node = new Dictionary<string, object> { ["type"] = command.GetType().Name };
            try { node["activeName"] = Limit(command.ActiveCommandName); }
            catch (Exception) { node["activeName"] = null; }
            string[] saveData = null;
            try { saveData = command.SaveData; }
            catch (Exception) { }
            if (saveData != null) {
                var values = new List<object>();
                for (int i = 0; i < saveData.Length && i < 8; i++) { values.Add(Limit(saveData[i])); }
                node["saveData"] = values;
                node["saveDataTruncated"] = saveData.Length > values.Count;
            }
            chain.Add(node);
            if (command is QueuedCommandsNode queued) {
                Queue<ICommand> pending = QueuedCommands(queued);
                var pendingNames = new List<object>();
                if (pending != null) {
                    int index = 0;
                    foreach (ICommand next in pending) {
                        if (index++ >= 8 || OverBudget(start)) { break; }
                        pendingNames.Add(next?.GetType().Name);
                    }
                }
                node["queuedCommandCount"] = pending?.Count ?? 0;
                node["queuedCommandTypes"] = pendingNames;
                node["queuedCommandsTruncated"] = (pending?.Count ?? 0) > pendingNames.Count;
                command = QueuedCurrent(queued);
                continue;
            }
            if (command is StickySequenceCommand sticky) {
                List<ICommand> children = StickyChildren(sticky);
                int index = StickyCurrentChild(sticky);
                node["currentChild"] = index;
                node["childCount"] = children?.Count ?? 0;
                command = children != null && index >= 0 && index < children.Count ? children[index] : null;
                continue;
            }
            if (command is SequenceCommand sequence) {
                List<ICommand> children = SequenceChildren(sequence);
                int index = SequenceCurrentChild(sequence);
                node["currentChild"] = index;
                node["childCount"] = children?.Count ?? 0;
                command = children != null && index >= 0 && index < children.Count ? children[index] : null;
                continue;
            }
            if (command is FlyTo flyTo) {
                ICommand path = FlyToPath(flyTo);
                ICommand manual = FlyToManual(flyTo);
                node["pathCommandType"] = path?.GetType().Name;
                node["manualCommandType"] = manual?.GetType().Name;
                command = path ?? manual;
                continue;
            }
            break;
        }
        if (command != null) { truncated = true; }
        return Map("chain", chain, "chainTruncated", truncated);
    }

    internal static object PropulsionForInspection(Ship ship) {
        double capacity = ship.fShallowRCSRemassMax;
        bool shallowCurrent = ship.LoadState <= Ship.Loaded.Shallow;
        return Map("rcsFuelIsCurrent", shallowCurrent,
            "rcsFuelKg", ship.fShallowRCSRemass, "rcsCapacityKg", capacity,
            "rcsFuelPercent", shallowCurrent && capacity > 0 ? (object)(100d * ship.fShallowRCSRemass / capacity) : null,
            "materializedRcsThrusterObjects", RcsThrusters(ship)?.Count, "rcsDistributionCount", RcsDistributionCount(ship),
            "rcsCount", RcsCount(ship), "fuelEfficiencyMod", FuelEfficiency(ship),
            "shallowFusionFuelRaw", ship.fShallowFusionRemain, "fusionFuelIsCurrent", shallowCurrent,
            "fusionThrustMaxRaw", ship.fFusionThrustMax,
            "fusionPelletMaxRaw", ship.fFusionPelletMax, "fusionReactorRunning", ship.bFusionReactorRunning,
            "torchDriveThrusting", TorchDriveThrusting(ship),
            "savedTorchDriveFlag", ship.json?.bFusionTorch,
            "note", "Raw stored/runtime fields only. Materialized thruster count may be incomplete for shallow ships. No canister scan, fuel calculation, trigger evaluation, or thrust command is performed.");
    }

    static object Motion(ShipSitu situation) {
        if (situation == null) { return null; }
        return Map("positionAU", Map("x", situation.vPosx, "y", situation.vPosy),
            "velocityAUPerSecond", Map("x", SituVelocityX(situation), "y", SituVelocityY(situation)),
            "rcsAccelerationRaw", Map("x", situation.vAccRCS.x, "y", situation.vAccRCS.y),
            "inputAccelerationRaw", Map("x", situation.vAccIn.x, "y", situation.vAccIn.y),
            "rotationalVelocity", situation.fW, "rotationRadians", situation.fRot,
            "isBodyOrbit", situation.bIsBO, "isRegion", situation.bIsRegion,
            "bodyOrbitLocked", situation.bBOLocked, "orbitLocked", situation.bOrbitLocked,
            "ignoreGravity", situation.bIgnoreGrav, "grounded", situation.bGrounded,
            "orbitBody", situation.boReference?.strName, "closestBody", situation.BOClosest?.strName,
            "greatestGravityBody", situation.BOGreatestGrav?.strName, "bodyOffsetShipId", situation.strBOPORShip);
    }

    static object Situation(ShipSitu situation) {
        if (situation == null) { return null; }
        return Motion(situation);
    }

    static object NavPlan(NavData navData, double epoch, long start) {
        if (navData == null) { return null; }
        List<NavDataPoint> source = NavPoints(navData);
        if (source == null) { return Map("pointCount", 0, "points", new List<object>()); }
        var points = new List<object>();
        int count = Math.Min(source.Count, MaxNavPoints);
        for (int i = 0; i < count && !OverBudget(start); i++) {
            NavDataPoint point = source[i];
            ShipSitu situation = point?.ObjSS;
            points.Add(point == null ? null : Map("index", i, "arrivalEpoch", point.ArrivalTime,
                "secondsUntilArrival", point.ArrivalTime - epoch,
                "positionAU", situation == null ? null : Map("x", situation.vPosx, "y", situation.vPosy),
                "velocityAUPerSecond", Map("x", point.VelX, "y", point.VelY),
                "fuelLevelRaw", point.FuelLevel, "torchCycleRaw", point.TorchCycle,
                "torchFuelLevelRaw", point.TorchFuelLevel, "torchDecelerationRequired", point.TorchDecelReq));
        }
        int previousIndex = -1;
        int nextIndex = -1;
        bool searchComplete = true;
        string segmentState;
        if (source.Count == 0) { segmentState = "empty"; }
        else if (source.Count == 1) { segmentState = "singlePoint"; }
        else {
            segmentState = "afterFinalPoint";
            for (int i = 1; i < source.Count; i++) {
                if (OverBudget(start)) { searchComplete = false; break; }
                if (epoch <= source[i].ArrivalTime) {
                    previousIndex = i - 1;
                    nextIndex = i;
                    segmentState = epoch < source[0].ArrivalTime ? "beforeFirstPoint" : "betweenPoints";
                    break;
                }
            }
            if (!searchComplete && previousIndex < 0) { segmentState = "searchIncomplete"; }
        }
        object activeSegment = null;
        if (previousIndex >= 0 && nextIndex >= 0) {
            NavDataPoint previous = source[previousIndex];
            NavDataPoint next = source[nextIndex];
            double duration = next.ArrivalTime - previous.ArrivalTime;
            activeSegment = Map("previous", NavPoint(previous, previousIndex, epoch),
                "next", NavPoint(next, nextIndex, epoch),
                "progress", duration > 0 ? (epoch - previous.ArrivalTime) / duration : null);
        }
        NavDataPoint destination = source.Count > 0 ? source[source.Count - 1] : null;
        return Map("pointCount", source.Count, "points", points, "pointsTruncated", source.Count > points.Count,
            "activeSegmentState", segmentState, "activeSegmentSearchComplete", searchComplete,
            "activeSegment", activeSegment,
            "destination", destination == null ? null : NavPoint(destination, source.Count - 1, epoch),
            "isTorching", navData.IsTorching, "inAtmosphere", navData.InAtmo,
            "lastAdvanceEpoch", NavPlanLastEpoch(navData), "lastPlanEpoch", NavPlanLastEpoch(navData),
            "flowMultiplierPlot", navData.fFlowMultPlot,
            "note", "Stored navigation plan; it is not recalculated. activeSegment is selected from stored arrival epochs, matching the game's waypoint-pair search. lastAdvanceEpoch is the game's fEpochLast field; lastPlanEpoch is retained as a legacy alias and does not mean the route was replanned. Arrival and fuel values are raw NavData points.");
    }

    static object NavPoint(NavDataPoint point, int index, double epoch) {
        ShipSitu situation = point?.ObjSS;
        return point == null ? null : Map("index", index, "arrivalEpoch", point.ArrivalTime,
            "secondsUntilArrival", point.ArrivalTime - epoch,
            "positionAU", situation == null ? null : Map("x", situation.vPosx, "y", situation.vPosy),
            "velocityAUPerSecond", Map("x", point.VelX, "y", point.VelY),
            "rcsAccelerationRaw", situation == null ? null : Map("x", situation.vAccRCS.x, "y", situation.vAccRCS.y),
            "inputAccelerationRaw", situation == null ? null : Map("x", situation.vAccIn.x, "y", situation.vAccIn.y),
            "isBodyOrbit", situation?.bIsBO, "bodyOrbitLocked", situation?.bBOLocked,
            "orbitBody", situation?.boReference?.strName ?? situation?.strBOPORShip,
            "fuelLevelRaw", point.FuelLevel, "torchCycleRaw", point.TorchCycle,
            "torchFuelLevelRaw", point.TorchFuelLevel, "torchDecelerationRequired", point.TorchDecelReq);
    }

    static object RelativeMotion(ShipSitu ship, ShipSitu target, string targetId) {
        if (ship == null || target == null) { return null; }
        double dx = ship.vPosx - target.vPosx;
        double dy = ship.vPosy - target.vPosy;
        double distance = Math.Sqrt(dx * dx + dy * dy);
        double relativeVx = SituVelocityX(ship) - SituVelocityX(target);
        double relativeVy = SituVelocityY(ship) - SituVelocityY(target);
        double rangeRate = distance > 0 ? (dx * relativeVx + dy * relativeVy) / distance : 0;
        return Map("targetShipId", targetId,
            "centerDistanceKm", distance * KilometersPerAu,
            "relativeVelocityKmPerSecond", Map("x", relativeVx * KilometersPerAu, "y", relativeVy * KilometersPerAu),
            "rangeRateKmPerSecond", rangeRate * KilometersPerAu,
            "positiveRangeRateMeansSeparating", true,
            "note", "Computed from stored ship and scan-target coordinates and velocities without calling UpdateTime; it may be stale if either ShipSitu has not advanced to the current epoch.");
    }
}
