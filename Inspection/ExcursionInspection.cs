using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using HarmonyLib;
using Ostranauts.ShipGUIs.NavStation;

namespace Ostrabus.Inspection;

internal static class ExcursionInspection {
    const int LeaseSeconds = 60;
    const double KmPerAU = 149597870.7;
    internal struct BodyState {
        internal string Id, Parent;
        internal double X, Y, Vx, Vy, PositionEpoch, VelocityEpoch;
        internal double Periapsis, Apoapsis, Axis1, Axis2, Eccentricity, Angle, Period, Phase, Mass, Radius;
        internal int Direction;
    }
    internal struct Motion {
        internal string Id, Reference;
        internal double Epoch, X, Y, Vx, Vy, OffsetX, OffsetY;
        internal float RcsX, RcsY, InputX, InputY, ExternalX, ExternalY;
        internal bool BodyLocked, OrbitLocked, IsBody;
        internal BodyState Body;
    }
    internal struct Transition {
        internal string Kind, Error;
        internal string Vrel, Vcrs, Range, Target, Label;
        internal double VrelValue, VrelEpoch, VcrsEpoch;
        internal bool Display, VrelAvailable;
        internal int Frame;
        internal Motion Before, After;
    }
    internal struct Scope {
        internal bool Active, Root;
        internal string Kind;
        internal Motion Before;
    }
    static readonly ExcursionRing<Transition> Events = new ExcursionRing<Transition>(256);
    static long leaseUntil;
    static int threadId, session;
    static string playerId, watchedId;
    static int nesting;
    static string vrelText, vcrsText, rangeText, readoutTarget, vrelLabel;
    static double vrelEpoch, vcrsEpoch, rangeEpoch, vrelRaw;
    static int vrelFrame, vcrsFrame, rangeFrame;
    static bool vrelAvailable;
    static long lastDisplayTicks;
    internal static bool Active => leaseUntil > Stopwatch.GetTimestamp() && threadId == Thread.CurrentThread.ManagedThreadId;
    static Dictionary<string, object> Map(params object[] pairs) { return SnapshotJson.Map(pairs); }
    static Ship PlayerShip() {
        if (playerId == null || CrewSim.system?.dictShips == null) return null;
        CrewSim.system.dictShips.TryGetValue(playerId, out Ship ship);
        return ship;
    }
    internal static void Reset() {
        leaseUntil = 0; threadId = 0; nesting = 0;
        playerId = watchedId = vrelText = vcrsText = rangeText = readoutTarget = vrelLabel = null;
        vrelEpoch = vcrsEpoch = rangeEpoch = vrelRaw = 0;
        vrelFrame = vcrsFrame = rangeFrame = 0; vrelAvailable = false;
        lastDisplayTicks = 0;
        Events.Clear();
    }
    internal static void ObserverFailed(Exception error) {
        leaseUntil = 0; nesting = 0;
        InspectionLog.Add("Excursion", "Warning", "Recorder stopped: " + error.GetType().Name + ": " + error.Message);
    }
    static BodyState Body(BodyOrbit body) {
        if (body == null) return default;
        return new BodyState { Id = body.strName, Parent = body.boParent?.strName, X = body.dXReal, Y = body.dYReal,
            Vx = body.dVelX, Vy = body.dVelY, PositionEpoch = body.dTimeCalcLast, VelocityEpoch = body.dVelCalcLast,
            Periapsis = body.fPerh, Apoapsis = body.fAph, Axis1 = body.fAxis1, Axis2 = body.fAxis2,
            Eccentricity = body.fEcc, Angle = body.fAngle, Period = body.fPeriod, Phase = body.fPeriodShift,
            Mass = body.fMass, Radius = body.fRadius, Direction = body.nOrbitDirection };
    }
    static Motion Capture(Ship ship) {
        var situation = ship?.objSS;
        if (situation == null) return default;
        return new Motion { Id = ship.strRegID, Epoch = StarSystem.fEpoch, X = situation.vPosx, Y = situation.vPosy,
            Vx = situation.vVelX, Vy = situation.vVelY, Reference = situation.strBOPORShip,
            OffsetX = situation.vBOOffsetx, OffsetY = situation.vBOOffsety,
            BodyLocked = situation.bBOLocked, OrbitLocked = situation.bOrbitLocked, IsBody = situation.bIsBO,
            RcsX = situation.vAccRCS.x, RcsY = situation.vAccRCS.y, InputX = situation.vAccIn.x, InputY = situation.vAccIn.y,
            ExternalX = situation.vAccEx.x, ExternalY = situation.vAccEx.y,
            Body = Body(CrewSim.system?.GetBO(situation.strBOPORShip)) };
    }
    internal static Scope Begin(string kind, Ship ship, ShipSitu situation = null) {
        if (!Active) return default;
        Ship player = PlayerShip();
        if (player == null || (situation != null ? !ReferenceEquals(player.objSS, situation) : !ReferenceEquals(player, ship))) return default;
        bool root = nesting++ == 0;
        return new Scope { Active = true, Root = root, Kind = kind, Before = root ? Capture(player) : default };
    }
    internal static void End(Scope scope, Exception error) {
        if (!scope.Active) return;
        nesting = Math.Max(0, nesting - 1);
        if (!scope.Root) return;
        Motion after = Capture(PlayerShip()), before = scope.Before;
        if (before.Id != after.Id || after.Id == null) return;
        if (before.BodyLocked == after.BodyLocked && before.OrbitLocked == after.OrbitLocked && before.Reference == after.Reference && before.Vx == after.Vx && before.Vy == after.Vy && error == null) return;
        Events.Add(new Transition { Kind = scope.Kind, Frame = UnityEngine.Time.frameCount, Before = before, After = after, Error = error?.GetType().Name });
    }
    internal static void Readout(Ship owner, string prefix, string value) {
        if (!Active || owner?.strRegID != playerId) return;
        int frame = UnityEngine.Time.frameCount;
        double epoch = StarSystem.fEpoch;
        if (prefix.StartsWith("VREL", StringComparison.Ordinal) || (prefix.Length == 0 && value.StartsWith("VREL", StringComparison.Ordinal))) {
            vrelText = value; vrelLabel = prefix; vrelEpoch = epoch; vrelFrame = frame;
            vrelAvailable = prefix.Length != 0;
            vrelRaw = GUIOrbitDraw.Instance?.fVRel ?? 0;
            readoutTarget = GUIOrbitDraw.crossHairInfo?._strRegID;
        } else if (prefix.StartsWith("VCRS", StringComparison.Ordinal) || (prefix.Length == 0 && value.StartsWith("VCRS", StringComparison.Ordinal))) {
            vcrsText = value; vcrsEpoch = epoch; vcrsFrame = frame;
        } else if (prefix.IndexOf("RNG", StringComparison.Ordinal) >= 0) {
            rangeText = value; rangeEpoch = epoch; rangeFrame = frame;
        }
    }
    internal static void CaptureReadout(Ship owner) {
        if (!Active || owner?.strRegID != playerId || vrelFrame != UnityEngine.Time.frameCount) return;
        long now = Stopwatch.GetTimestamp();
        if (now - lastDisplayTicks < Stopwatch.Frequency / 20) return;
        lastDisplayTicks = now;
        Events.Add(new Transition { Kind = "navDisplay", Display = true, After = Capture(owner), Frame = vrelFrame,
            Vrel = vrelText, Vcrs = vcrsText, Range = rangeText, Target = readoutTarget, Label = vrelLabel,
            VrelValue = vrelRaw, VrelAvailable = vrelAvailable, VrelEpoch = vrelEpoch, VcrsEpoch = vcrsEpoch });
    }
    static object BodyJson(BodyState body) {
        if (body.Id == null) return null;
        return Map("id", body.Id, "parentId", body.Parent, "xAU", body.X, "yAU", body.Y, "vxAUPerSecond", body.Vx, "vyAUPerSecond", body.Vy,
            "positionCalculationEpoch", body.PositionEpoch, "velocityCalculationEpoch", body.VelocityEpoch,
            "periapsisAU", body.Periapsis, "apoapsisAU", body.Apoapsis, "majorAxisAU", body.Axis1, "minorAxisAU", body.Axis2,
            "eccentricity", body.Eccentricity, "angleDegrees", body.Angle, "periodSeconds", body.Period,
            "phaseSeconds", body.Phase, "massKg", body.Mass, "radiusAU", body.Radius, "direction", body.Direction);
    }
    static object MotionJson(Motion motion) {
        if (motion.Id == null) return null;
        return Map("id", motion.Id, "captureEpoch", motion.Epoch, "xAU", motion.X, "yAU", motion.Y, "vxAUPerSecond", motion.Vx, "vyAUPerSecond", motion.Vy,
            "referenceBodyId", motion.Reference, "bodyOrbitLocked", motion.BodyLocked, "orbitLocked", motion.OrbitLocked, "isBodyOrbit", motion.IsBody,
            "bodyOffsetXAU", motion.OffsetX, "bodyOffsetYAU", motion.OffsetY,
            "rcsAcceleration", Map("x", motion.RcsX, "y", motion.RcsY), "inputAcceleration", Map("x", motion.InputX, "y", motion.InputY),
            "externalAcceleration", Map("x", motion.ExternalX, "y", motion.ExternalY), "referenceBody", BodyJson(motion.Body));
    }
    internal static InspectionApi.Reply Snapshot(string shipId, long since, int limit) {
        bool fresh = !Active;
        if (fresh) {
            Reset(); session++;
            playerId = GUIDockSys.instance?.COSelf?.ship?.strRegID ?? CrewSim.coPlayer?.ship?.strRegID;
        }
        threadId = Thread.CurrentThread.ManagedThreadId;
        leaseUntil = Stopwatch.GetTimestamp() + LeaseSeconds * Stopwatch.Frequency;
        if (!string.IsNullOrEmpty(shipId)) watchedId = shipId;
        else if (watchedId == null) watchedId = GUIOrbitDraw.crossHairInfo?._strRegID;
        var ids = new List<string>(8);
        void Add(string id) { if (!string.IsNullOrEmpty(id) && ids.Count < 8 && !ids.Contains(id)) ids.Add(id); }
        Add(playerId); Add("VORB"); Add("VORB|Aux"); Add(watchedId); Add(GUIOrbitDraw.crossHairInfo?._strRegID);
        if (watchedId != null && CrewSim.system.dictShips.TryGetValue(watchedId, out Ship watched)) Add(watched?.shipScanTarget?.strRegID);
        var ships = new List<object>(8);
        var bodies = new Dictionary<string, object>(StringComparer.Ordinal);
        void AddBody(BodyOrbit body) { if (body != null && !bodies.ContainsKey(body.strName)) bodies.Add(body.strName, BodyJson(Body(body))); }
        AddBody(CrewSim.system.GetBO("Sol")); AddBody(CrewSim.system.GetBO("Venus")); AddBody(CrewSim.system.GetBO("VORB")); AddBody(CrewSim.system.GetBO("VORB|Aux"));
        foreach (string id in ids) {
            if (!CrewSim.system.dictShips.TryGetValue(id, out Ship ship) || ship?.objSS == null) { ships.Add(Map("id", id, "resolved", false)); continue; }
            Motion motion = Capture(ship);
            ships.Add(Map("id", id, "name", ship.publicName, "loadState", ship.LoadState.ToString(), "destroyed", ship.bDestroyed,
                "motion", MotionJson(motion), "scanTargetId", ship.shipScanTarget?.strRegID, "stationKeepingTargetId", ship.shipStationKeepingTarget?.strRegID));
            AddBody(CrewSim.system.GetBO(motion.Reference));
        }
        var events = new List<object>(limit);
        long cursor = Math.Max(since, Events.Oldest - 1);
        for (long sequence = cursor + 1; sequence <= Events.Latest && events.Count < limit; sequence++) {
            if (!Events.TryRead(sequence, out Transition transition)) continue;
            double dx = transition.After.Vx - transition.Before.Vx, dy = transition.After.Vy - transition.Before.Vy;
            events.Add(Map("sequence", sequence, "kind", transition.Kind, "frame", transition.Frame,
                "before", MotionJson(transition.Before), "after", MotionJson(transition.After), "velocityChangeMPerSecond", transition.Display ? null : (object)(Math.Sqrt(dx * dx + dy * dy) * KmPerAU * 1000),
                "navDisplay", transition.Display ? Map("targetId", transition.Target, "vrelLabel", transition.Label, "vrelText", transition.Vrel,
                    "vrelMPerSecond", transition.VrelAvailable ? (object)(transition.VrelValue * KmPerAU * 1000) : null,
                    "vcrsText", transition.Vcrs, "rangeText", transition.Range, "vrelEpoch", transition.VrelEpoch, "vcrsEpoch", transition.VcrsEpoch) : null,
                "error", transition.Error));
            cursor = sequence;
        }
        return new InspectionApi.Reply(200, Map("session", session, "frame", UnityEngine.Time.frameCount, "paused", CrewSim.Paused,
            "playerShipId", playerId, "characterShipId", CrewSim.coPlayer?.ship?.strRegID, "watchedShipId", watchedId,
            "recordingLeaseSeconds", LeaseSeconds, "maximumDisplaySamplesPerRealSecond", 20, "ships", ships, "bodies", bodies,
            "navDisplay", Map("targetId", readoutTarget, "vrelLabel", vrelLabel, "vrelText", vrelText, "vrelMPerSecond", vrelAvailable ? (object)(vrelRaw * KmPerAU * 1000) : null,
                "vrelEpoch", vrelEpoch, "vrelFrame", vrelFrame, "vcrsText", vcrsText, "vcrsEpoch", vcrsEpoch, "vcrsFrame", vcrsFrame,
                "rangeText", rangeText, "rangeEpoch", rangeEpoch, "rangeFrame", rangeFrame),
            "events", events, "nextSince", since > Events.Latest ? Events.Latest : cursor, "latestSequence", Events.Latest, "oldestAvailableSequence", Events.Oldest,
            "overwritten", Math.Max(0, Events.Latest - Events.Capacity), "cursorOverrun", since < Events.Oldest - 1, "cursorAhead", since > Events.Latest,
            "note", "One Unity-frame read of stored state; bodies can have different calculation epochs. No UpdateTime, projection, force, fuel scan or navigation command is invoked. Display fields are last observed game-generated strings and may be older than this snapshot. The bounded events include orbit hooks and completed nav readouts capped at 20 Hz wall time while polled; captureEpoch is not a ship integration timestamp."));
    }
}

[HarmonyPatch]
internal static class ExcursionOrbitHooks {
    static IEnumerable<System.Reflection.MethodBase> TargetMethods() {
        yield return AccessTools.Method(typeof(Ship), nameof(Ship.LockToOrbit));
        yield return AccessTools.Method(typeof(Ship), nameof(Ship.UnlockFromOrbit));
        yield return AccessTools.Method(typeof(ShipSitu), nameof(ShipSitu.LockToOrbit), new[] { typeof(BodyOrbit), typeof(double) });
    }
    static void Prefix(object __instance, System.Reflection.MethodBase __originalMethod, out ExcursionInspection.Scope __state) {
        __state = default;
        if (!ExcursionInspection.Active) return;
        try { __state = ExcursionInspection.Begin(__originalMethod.Name, __instance as Ship, __instance as ShipSitu); }
        catch (Exception ex) { ExcursionInspection.ObserverFailed(ex); }
    }
    static void Finalizer(Exception __exception, ExcursionInspection.Scope __state) {
        if (!__state.Active) return;
        try { ExcursionInspection.End(__state, __exception); }
        catch (Exception ex) { ExcursionInspection.ObserverFailed(ex); }
    }
}

[HarmonyPatch(typeof(NavModTargetData), "AddLine", new[] { typeof(string), typeof(string), typeof(string) })]
internal static class ExcursionReadoutHook {
    static AccessTools.FieldRef<NavModBase, CondOwner> owner;
    internal static Ship OwnerShip(NavModTargetData control) {
        owner ??= AccessTools.FieldRefAccess<NavModBase, CondOwner>("COSelf");
        return owner(control)?.ship;
    }
    static void Postfix(NavModTargetData __instance, string pre, string data) {
        if (!ExcursionInspection.Active) return;
        try {
            ExcursionInspection.Readout(OwnerShip(__instance), pre ?? "", data ?? "");
        } catch (Exception ex) { ExcursionInspection.ObserverFailed(ex); }
    }
}

[HarmonyPatch(typeof(NavModTargetData), "UpdateUI")]
internal static class ExcursionReadoutCompleteHook {
    static void Postfix(NavModTargetData __instance) {
        if (!ExcursionInspection.Active) return;
        try { ExcursionInspection.CaptureReadout(ExcursionReadoutHook.OwnerShip(__instance)); }
        catch (Exception ex) { ExcursionInspection.ObserverFailed(ex); }
    }
}
