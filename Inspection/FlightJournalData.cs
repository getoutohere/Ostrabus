using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using HarmonyLib;
using Newtonsoft.Json;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using Ostranauts.ShipGUIs.Utilities;

namespace Ostrabus.Inspection;

internal static class FlightJournalData {
    internal const int MaxShips = 1024, MaxBodies = 128, MaxPoints = 128;
    internal struct Motion {
        public double XAU, YAU, VxAUPerSecond, VyAUPerSecond, OffsetXAU, OffsetYAU;
        public double RcsAx, RcsAy, InputAx, InputAy, ExternalAx, ExternalAy;
        public string ReferenceBody;
        public bool BodyLocked, OrbitLocked, IsBody, IgnoreGravity;
    }
    internal struct ShipRow {
        public string Id, Model, Region, Home, PilotType, Command, Reaction, TargetId, ScanTargetId, CombatTargetId, TargetObjectId, TargetDataId;
        public string[] DockedIds;
        public int ObjectIdentity, LoadState, Classification, DamageState, DockLinks, PlanPoints;
        public bool Destroyed, AI, HasSituation, TargetReferenceMatches, TargetObjectIsCurrent, DataSituationMatches, DockLinksTruncated, HasPlan, FuelCachesCurrent, TorchThrusting;
        public bool? SavedTorchDrive;
        public double CachedRcsKg, CachedRcsCapacityKg, CachedFusion, CachedRcsCount, CachedFusionThrustMax, PlanArrivalEpoch;
        public Motion Motion, TargetMotion, PlanDestination;
    }
    internal struct BodyRow {
        public string Id, Parent;
        public double XAU, YAU, VxAUPerSecond, VyAUPerSecond, PositionEpoch, VelocityEpoch;
        public double Mass, RadiusAU, PeriapsisAU, ApoapsisAU, Axis1AU, Axis2AU, Eccentricity, Angle, Period, Phase;
        public int Direction;
    }
    internal struct PlanPoint {
        public double ArrivalEpoch, RcsFuelRaw, TorchFuelRaw, TorchCycle;
        public bool TorchDeceleration;
        public Motion Motion;
    }
    internal sealed class Record {
        public long Sequence, TransitionId;
        public string Kind, Utc, Region, NearestAtc, PlayerShipId, Error, Message, Source, Level;
        public double Epoch, SkipSeconds, CaptureMs;
        public int WorldIdentity, TotalShips, TotalAis, TotalBodies, RegisteredCOs, StaleAiReferences;
        public bool ShipsTruncated, BodiesTruncated, AisTruncated, PlayerCourseTruncated, NewSffReport, EpochApproximate, LogTruncated;
        public object Orbital;
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public object Diagnostic;
        public BodyRow[] Bodies;
        public PlanPoint[] PlayerCourse;
        [JsonIgnore] internal ShipRow[] Buffer;
        [JsonIgnore] internal int Count;
        public ArraySegment<ShipRow> Ships => new ArraySegment<ShipRow>(Buffer ?? Array.Empty<ShipRow>(), 0, Count);
    }
    struct Pilot {
        internal string Region;
        internal AIShip AI;
    }
    static readonly ConcurrentStack<ShipRow[]> buffers = new ConcurrentStack<ShipRow[]>();
    static int pooled;
    static readonly Dictionary<Ship, Pilot> pilots = new Dictionary<Ship, Pilot>();
    static readonly HashSet<string> bodyIds = new HashSet<string>(StringComparer.Ordinal);
    static readonly AccessTools.FieldRef<AIShip, Ship> RawShip = AccessTools.FieldRefAccess<AIShip, Ship>("_shipUs");
    static readonly AccessTools.FieldRef<AIShip, ICommand> Active = AccessTools.FieldRefAccess<AIShip, ICommand>("_activeCommand");
    static readonly AccessTools.FieldRef<AIShip, ICommand> Reaction = AccessTools.FieldRefAccess<AIShip, ICommand>("_activeReaction");
    static readonly AccessTools.FieldRef<NavData, List<NavDataPoint>> Points = AccessTools.FieldRefAccess<NavData, List<NavDataPoint>>("_navPoints");
    static readonly AccessTools.FieldRef<Ship, Dictionary<string, Ship>> Docks = AccessTools.FieldRefAccess<Ship, Dictionary<string, Ship>>("aDocked");
    static readonly AccessTools.FieldRef<Ship, float> RcsCount = AccessTools.FieldRefAccess<Ship, float>("fRCSCount");
    static readonly AccessTools.FieldRef<Ship, bool> Torching = AccessTools.FieldRefAccess<Ship, bool>("bTorchDriveThrusting");

    internal static void Release(Record record) {
        var buffer = record?.Buffer;
        if (buffer == null) return;
        record.Buffer = null; record.Count = 0;
        Array.Clear(buffer, 0, buffer.Length);
        if (Interlocked.Increment(ref pooled) <= 4) buffers.Push(buffer);
        else Interlocked.Decrement(ref pooled);
    }
    internal static Motion Read(ShipSitu s) => s == null ? default : new Motion {
        XAU = s.vPosx, YAU = s.vPosy, VxAUPerSecond = s.vVelX, VyAUPerSecond = s.vVelY,
        OffsetXAU = s.vBOOffsetx, OffsetYAU = s.vBOOffsety, ReferenceBody = s.strBOPORShip,
        RcsAx = s.vAccRCS.x, RcsAy = s.vAccRCS.y, InputAx = s.vAccIn.x, InputAy = s.vAccIn.y,
        ExternalAx = s.vAccEx.x, ExternalAy = s.vAccEx.y, BodyLocked = s.bBOLocked, OrbitLocked = s.bOrbitLocked,
        IsBody = s.bIsBO, IgnoreGravity = s.bIgnoreGrav
    };
    static ShipRow Row(Ship ship) {
        pilots.TryGetValue(ship, out var pilot);
        var ai = pilot.AI;
        var plan = ship.objSS?.NavData;
        var points = plan == null ? null : Points(plan);
        var target = ship.shipSituTarget;
        var targetObject = ship.TargetData?.Ship;
        Ship currentTarget = null;
        if (targetObject?.strRegID != null) CrewSim.system?.dictShips?.TryGetValue(targetObject.strRegID, out currentTarget);
        var docks = Docks(ship);
        int dockCount = Math.Min(16, docks?.Count ?? 0);
        var dockIds = dockCount == 0 ? Array.Empty<string>() : new string[dockCount];
        int dockIndex = 0;
        if (docks != null) foreach (var docked in docks.Values) {
            if (dockIndex >= dockIds.Length) break;
            dockIds[dockIndex++] = docked?.strRegID;
        }
        return new ShipRow {
            Id = ship.strRegID, Model = ship.model, ObjectIdentity = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(ship), LoadState = (int)ship.LoadState,
            Classification = (int)ship.Classification, DamageState = (int)ship.DMGStatus,
            Destroyed = ship.bDestroyed, AI = ship.IsAIShip, DockLinks = docks?.Count ?? 0, DockedIds = dockIds, DockLinksTruncated = (docks?.Count ?? 0) > dockIds.Length,
            Region = pilot.Region, Home = ai?.HomeStation, PilotType = ai?.AICharacter?.GetType().Name,
            Command = ai == null ? null : Active(ai)?.GetType().Name, Reaction = ai == null ? null : Reaction(ai)?.GetType().Name,
            CachedRcsKg = ship.fShallowRCSRemass, CachedRcsCapacityKg = ship.fShallowRCSRemassMax, CachedFusion = ship.fShallowFusionRemain,
            FuelCachesCurrent = ship.LoadState <= Ship.Loaded.Shallow, CachedRcsCount = RcsCount(ship), CachedFusionThrustMax = ship.fFusionThrustMax,
            SavedTorchDrive = ship.json?.bFusionTorch, TorchThrusting = Torching(ship),
            TargetId = ship.targetRegID, ScanTargetId = ship.shipScanTarget?.strRegID, CombatTargetId = ship.shipCombatTarget?.strRegID,
            TargetObjectId = targetObject?.strRegID, TargetReferenceMatches = targetObject == null || ReferenceEquals(target, targetObject.objSS),
            TargetObjectIsCurrent = targetObject == null || ReferenceEquals(targetObject, currentTarget),
            TargetDataId = ship.TargetData?.TargetRegId, DataSituationMatches = ReferenceEquals(ship.TargetData?.Situ, target),
            HasSituation = ship.objSS != null, Motion = Read(ship.objSS), TargetMotion = Read(target),
            HasPlan = plan != null, PlanPoints = points?.Count ?? 0,
            PlanDestination = Read(points != null && points.Count > 0 ? points[points.Count - 1]?.ObjSS : null),
            PlanArrivalEpoch = points != null && points.Count > 0 ? points[points.Count - 1]?.ArrivalTime ?? double.NaN : double.NaN
        };
    }
    static BodyRow Body(BodyOrbit body) => new BodyRow { Id = body.strName, Parent = body.boParent?.strName,
        XAU = body.dXReal, YAU = body.dYReal, VxAUPerSecond = body.dVelX, VyAUPerSecond = body.dVelY,
        PositionEpoch = body.dTimeCalcLast, VelocityEpoch = body.dVelCalcLast,
        Mass = body.fMass, RadiusAU = body.fRadius, PeriapsisAU = body.fPerh, ApoapsisAU = body.fAph,
        Axis1AU = body.fAxis1, Axis2AU = body.fAxis2, Eccentricity = body.fEcc, Angle = body.fAngle,
        Period = body.fPeriod, Phase = body.fPeriodShift, Direction = body.nOrbitDirection };
    internal static Record Capture(string kind) {
        var record = new Record { Kind = kind };
        try {
            if (buffers.TryPop(out var buffer)) Interlocked.Decrement(ref pooled);
            else buffer = new ShipRow[MaxShips];
            record.Buffer = buffer;
            var system = CrewSim.system;
            var player = CrewSim.coPlayer?.ship;
            record.WorldIdentity = system == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(system);
            record.Region = AIShipManager.strATCLast; record.NearestAtc = CollisionManager.strATCClosest;
            record.PlayerShipId = player?.strRegID; record.RegisteredCOs = DataHandler.mapCOs?.Count ?? 0;
            record.TotalShips = system?.dictShips?.Count ?? 0; record.TotalBodies = system?.aBOs?.Count ?? 0;
            int inspectedAi = 0;
            if (AIShipManager.dictAIs != null) foreach (var region in AIShipManager.dictAIs) {
                if (region.Value == null) continue;
                record.TotalAis += region.Value.Count;
                foreach (var ai in region.Value) {
                    if (++inspectedAi > MaxShips * 4) { record.AisTruncated = true; break; }
                    var ship = ai == null ? null : RawShip(ai);
                    if (ship?.strRegID == null || system?.dictShips == null || !system.dictShips.TryGetValue(ship.strRegID, out var actual) || !ReferenceEquals(actual, ship)) record.StaleAiReferences++;
                    if (ship != null && pilots.Count < MaxShips) pilots[ship] = new Pilot { Region = region.Key, AI = ai };
                }
            }
            if (player != null) buffer[record.Count++] = Row(player);
            if (system?.dictShips != null) foreach (var ship in system.dictShips.Values) {
                if (ship == null || ReferenceEquals(ship, player)) continue;
                if (record.Count >= MaxShips) { record.ShipsTruncated = true; break; }
                buffer[record.Count++] = Row(ship);
            }
            int bodyCount = Math.Min(MaxBodies, record.TotalBodies);
            record.Bodies = new BodyRow[bodyCount];
            record.BodiesTruncated = record.TotalBodies > bodyCount;
            int index = 0;
            if (system?.aBOs != null) {
                BodyOrbit reference = null;
                if (player?.objSS?.strBOPORShip != null) system.aBOs.TryGetValue(player.objSS.strBOPORShip, out reference);
                for (int depth = 0; reference != null && depth < 32 && index < bodyCount; depth++, reference = reference.boParent) {
                    if (!bodyIds.Add(reference.strName)) break;
                    record.Bodies[index++] = Body(reference);
                }
                // Preserve real planets/moons before filling the cap with placeholder orbits.
                for (int pass = 0; pass < 2 && index < bodyCount; pass++) foreach (var body in system.aBOs.Values) {
                    if (index >= bodyCount) break;
                    if (body == null || (pass == 0 && body.nDrawFlagsBody == 1) || !bodyIds.Add(body.strName)) continue;
                    record.Bodies[index++] = Body(body);
                }
            }
            if (index != bodyCount) Array.Resize(ref record.Bodies, index);
            var plan = player?.objSS?.NavData;
            var source = plan == null ? null : Points(plan);
            if (source != null) {
                int count = Math.Min(MaxPoints, source.Count); record.PlayerCourse = new PlanPoint[count];
                record.PlayerCourseTruncated = count < source.Count;
                for (int i = 0; i < count; i++) {
                    var point = source[i];
                    if (point == null) continue;
                    record.PlayerCourse[i] = new PlanPoint { ArrivalEpoch = point.ArrivalTime, RcsFuelRaw = point.FuelLevel,
                        TorchFuelRaw = point.TorchFuelLevel, TorchCycle = point.TorchCycle,
                        TorchDeceleration = point.TorchDecelReq, Motion = Read(point.ObjSS) };
                }
            }
            record.Orbital = global::Ostrabus.Orbital.OrbitalSimulation.Snapshot();
            return record;
        } catch { Release(record); throw; }
        finally { pilots.Clear(); bodyIds.Clear(); }
    }
}
