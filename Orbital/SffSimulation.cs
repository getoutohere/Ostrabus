using System;
using System.Collections.Generic;
using System.Diagnostics;
using HarmonyLib;
using Ostranauts.Ships.AIPilots;
using Ostrabus.Inspection;
using Ostranauts.ShipGUIs.Utilities;

namespace Ostrabus.Orbital;

internal sealed class SffSimulation : IDisposable {
    internal static SffSimulation Current;
    internal static bool Enabled = true;
    static object last = SnapshotJson.Map("state", "No corrected SFF jump recorded.");
    static readonly AccessTools.FieldRef<Ship, float> AeroCoefficient = AccessTools.FieldRefAccess<Ship, float>("fAeroCoefficient");
    static readonly AccessTools.FieldRef<NavData, List<NavDataPoint>> NavPoints = AccessTools.FieldRefAccess<NavData, List<NavDataPoint>>("_navPoints");
    internal sealed class Plan {
        internal readonly List<Ship> Ships = new List<Ship>();
        internal ShipSitu Anchor;
        internal MotionState Initial;
        internal SffOutcome Outcome;
        internal AIShip Pilot;
        internal Ship TargetShip;
        internal ShipSitu Target;
        internal double Handoff, ArrivalSpeed, Rotation, AngularVelocity, AngularAcceleration;
        internal bool Travel, Applied, Ready, ImpactResolved;
        internal bool Detour;
        internal string Mode;
        internal int AeroModels;
    }
    sealed class Entry {
        internal Plan Plan;
        internal DVector Offset;
        internal double RotationOffset;
        internal bool Member;
    }
    readonly Dictionary<ShipSitu, Entry> entries = new Dictionary<ShipSitu, Entry>();
    readonly Dictionary<AIShip, Plan> pilots = new Dictionary<AIShip, Plan>();
    readonly List<Plan> plans = new List<Plan>();
    readonly List<object> rows = new List<object>();
    readonly SffEnvironment environment = new SffEnvironment();
    readonly SffSimulation previous;
    readonly double start, seconds;
    readonly long began;
    int excludedRoutes, excludedCombat, excludedLocks, excludedOther, excludedInvalid, excludedHidden, failures, suppressedCatchup, collisions;
    bool beforeAi, disposed, rowsTruncated;
    double preparationMs;

    SffSimulation(double epoch, double duration) {
        start = epoch; seconds = duration; began = Stopwatch.GetTimestamp(); previous = Current;
    }
    internal static SffSimulation Begin(StarSystem system, double duration, bool isSff) {
        if (!OrbitalSimulation.Enabled || !Enabled || !isSff || !OrbitAcceleration.Finite(duration) || duration < 60 || system?.dictShips == null) return null;
        var context = new SffSimulation(StarSystem.fEpoch, duration);
        try {
            foreach (var body in system.aBOs.Values) {
                if (body == null || body.IsPlaceholder() || body.IsAsteroidField || body.fMass <= 0 || !OrbitAcceleration.Finite(body.fMass)) continue;
                if (!OrbitalMotion.BodyAt(body, context.start, out _)) continue;
                context.environment.Bodies.Add(new SffBody(body));
            }
            if (context.environment.Bodies.Count == 0) return null;
            var seen = new HashSet<SolarSystem.DockGroup>();
            foreach (var ship in system.dictShips.Values) {
                if (ship == null || ship.bDestroyed || ship.bRemove || ship.objSS == null) continue;
                if (ship.HideFromSystem) { context.Hidden(ship.strRegID); continue; }
                var members = new List<Ship>();
                ShipSitu anchor = ship.objSS;
                if (ship.HasDockGroup && ship.DockGroup.Anchor != null) {
                    var group = ship.DockGroup;
                    if (!seen.Add(group)) continue;
                    anchor = group.Anchor;
                    foreach (var member in group.DockedShips) if (member?.Ship != null && !member.Ship.bDestroyed) members.Add(member.Ship);
                } else members.Add(ship);
                context.Prepare(system, anchor, members);
            }
            context.preparationMs = (Stopwatch.GetTimestamp() - context.began) * 1000d / Stopwatch.Frequency;
            Current = context;
            return context;
        } catch (Exception ex) {
            context.Dispose();
            Ostrabus.OstrabusPlugin.Logger?.LogError("[Orbital SFF] Preparation failed; original SFF retained: " + ex);
            return null;
        }
    }
    void Prepare(StarSystem system, ShipSitu anchor, List<Ship> members) {
        foreach (var ship in members) if (Economy.SffEconomy.Current?.Owns(ship) == true) { excludedOther++; return; }
        if (anchor.NavData != null) { excludedRoutes++; return; }
        if (anchor.bIsBO || anchor.bBOLocked || anchor.bOrbitLocked) { excludedLocks++; return; }
        if (anchor.bIgnoreGrav || members.Count == 0) { excludedOther++; return; }
        foreach (var ship in members) {
            if (ship.HideFromSystem) { Hidden(Ids(members)); return; }
            if (ship.objSS.NavData != null) { excludedRoutes++; return; }
            if (ship.objSS.bIsBO || ship.objSS.bBOLocked || ship.objSS.bOrbitLocked) { excludedLocks++; return; }
            if (ship.shipCombatTarget != null || ship.Classification == Ship.TypeClassification.Projectile) { excludedCombat++; return; }
        }
        var plan = new Plan { Anchor = anchor, Initial = OrbitalMotion.Read(anchor), Rotation = anchor.fRot,
            AngularVelocity = anchor.fW, AngularAcceleration = anchor.fA };
        plan.Ships.AddRange(members);
        foreach (var ship in members) {
            var ai = AIShipManager.dictAIs != null ? AIShipManager.GetAIShipByRegID(ship.strRegID) : null;
            if (ai == null) continue;
            if (IsCombatCommand(ai.ActiveCommandName)) { excludedCombat++; return; }
            if (plan.Pilot == null && IsTravelCommand(ai.ActiveCommandName)) plan.Pilot = ai;
        }
        try {
            double collisionRadius = members.Count > 1 ? 0 : anchor.GetRadiusAU() * OrbitalMotion.KmPerAU;
            var initialBody = plan.Initial.Finite ? environment.Select(plan.Initial.Position, start, out _) : null;
            double initialRadius = initialBody != null ? (plan.Initial.Position - initialBody.At(start).Position).Length : double.NaN;
            if (initialBody == null || !OrbitAcceleration.Finite(initialRadius) || !OrbitAcceleration.Finite(collisionRadius) || collisionRadius < 0 ||
                initialRadius <= initialBody.Radius + collisionRadius) {
                excludedInvalid++;
                AddRow(SnapshotJson.Map("ships", Ids(members), "mode", "nativeInvalidStart", "body", initialBody?.Native.strName,
                    "initialRadiusKm", OrbitAcceleration.Finite(initialRadius) ? (object)initialRadius : null,
                    "bodyRadiusKm", initialBody?.Radius, "reason", "Nonfinite or already inside body at capture; no replacement trajectory or new impact."));
                return;
            }
            SffFlight flight = null;
            if (plan.Pilot != null) {
                var ship = plan.Pilot.Ship;
                plan.TargetShip = ship.shipScanTarget;
                plan.Target = ship.shipScanTarget?.objSS ?? ship.shipSituTarget;
                plan.Travel = plan.Target != null;
                if (plan.Travel) {
                    var target = CaptureTarget(system, plan.Target);
                    double collision = CollisionManager.GetCollisionDistanceAU(anchor, plan.Target) * OrbitalMotion.KmPerAU;
                    plan.Handoff = Math.Max(.01, collision * (plan.TargetShip != null && plan.TargetShip.Classification != Ship.TypeClassification.Asteroid ? 3 : 1.5));
                    plan.ArrivalSpeed = plan.TargetShip != null && plan.TargetShip.IsDocked() && !plan.TargetShip.IsStation() ? .02 : .1;
                    double acceleration = .5 * Math.Min(ship.RCSAccelMax, 1.9016982118751132e-10) * OrbitalMotion.KmPerAU;
                    double speed = plan.Pilot.AICharacter.MaxSpeed(false) * OrbitalMotion.KmPerAU;
                    if (target != null && acceleration > 0 && speed > 0 && OrbitAcceleration.Finite(acceleration) && OrbitAcceleration.Finite(speed))
                        flight = new SffFlight { Target = target, Acceleration = acceleration, MaxSpeed = speed,
                            MaxSpeedAtmo = plan.Pilot.AICharacter.MaxSpeed(true) * OrbitalMotion.KmPerAU, HandoffKm = plan.Handoff, ArrivalSpeed = plan.ArrivalSpeed };
                }
            }
            bool aiControlled = false;
            foreach (var ship in members) aiControlled |= ship.IsAIShip;
            DVector engines = aiControlled ? default : new DVector(anchor.vAccIn.x + anchor.vAccRCS.x + anchor.vAccEx.x,
                anchor.vAccIn.y + anchor.vAccRCS.y + anchor.vAccEx.y) * OrbitalMotion.KmPerAU;
            var aero = CaptureAero(members, out plan.AeroModels);
            plan.Outcome = SffDynamics.Propagate(environment, plan.Initial, start, seconds, engines, aero, collisionRadius, flight);
            plan.Mode = plan.Outcome.Impact != null ? "impact" : plan.Outcome.Arrived ? "arrived" : plan.Travel ? "partial" : engines.Length > 0 ? "powered" : "coast";
            plan.Detour = flight?.UsedDetour ?? false;
            plans.Add(plan);
            entries[anchor] = new Entry { Plan = plan };
            foreach (var ship in members) {
                if (!ReferenceEquals(ship.objSS, anchor)) entries[ship.objSS] = new Entry { Plan = plan, Member = true,
                    Offset = new DVector(ship.objSS._dockOffsetPos.X, ship.objSS._dockOffsetPos.Y) * OrbitalMotion.KmPerAU,
                    RotationOffset = ship.objSS._dockOffsetRot };
                var ai = AIShipManager.dictAIs != null ? AIShipManager.GetAIShipByRegID(ship.strRegID) : null;
                if (ai != null && plan.Travel) pilots[ai] = plan;
            }
        } catch (Exception ex) {
            failures++;
            AddRow(SnapshotJson.Map("ships", Ids(members), "mode", "nativeFallback", "reason", ex.Message));
            Ostrabus.OstrabusPlugin.Logger?.LogWarning("[Orbital SFF] Native fallback for " + Ids(members) + ": " + ex.Message);
        }
    }
    void Hidden(string ships) {
        excludedHidden++;
        AddRow(SnapshotJson.Map("ships", ships, "mode", "nativeHidden", "reason", "Hidden/background hull; no replacement trajectory or impact."));
    }
    static bool Hidden(Plan plan) {
        foreach (var ship in plan.Ships) if (ship.HideFromSystem) return true;
        return false;
    }
    internal static SffAero[] CaptureAero(List<Ship> members, out int count) {
        // Ship.UpdateGravAndAtmo returns before CalculateLiftDrag unless the ship
        // is Full with tiles. Do not invent atmosphere simulation for shallow ships.
        count = 0;
        foreach (var ship in members) if (ship.LoadState == Ship.Loaded.Full && ship.aTiles != null && ship.aTiles.Count > 0) count++;
        if (count == 0) return null;
        double mass = 0;
        foreach (var ship in members) mass += Math.Max(1, ship.Mass);
        var aero = new SffAero[count];
        int i = 0;
        foreach (var ship in members) {
            if (ship.LoadState != Ship.Loaded.Full || ship.aTiles == null || ship.aTiles.Count == 0) continue;
            double lift = AeroCoefficient(ship); if (lift == 0) lift = 1;
            double width = (ship.nCols + ship.nRows) * .32 / 2;
            double drag = width * (3 + 12 * Math.Max(0, Math.Min(1, (width - 3) / 50)));
            double ownMass = Math.Max(1, ship.Mass);
            aero[i++] = new SffAero { Mass = ownMass, Lift = lift / ownMass, Front = drag / Math.Max(1, lift / 100), Side = drag,
                Rotation = ship.objSS.fRot, RotationRate = ship.objSS.fW, AngularAcceleration = ship.objSS.fA, Weight = ownMass / mass };
        }
        return aero;
    }
    ISffTarget CaptureTarget(StarSystem system, ShipSitu situation) {
        // Copy scalar waypoints; never invoke playback, consume fuel or update live points.
        if (situation.NavData != null) {
            var points = NavPoints(situation.NavData);
            if (points == null || points.Count < 2 || points.Count > 4096) return null;
            var captured = new SffPathTarget.Point[points.Count];
            for (int i = 0; i < points.Count; i++) {
                var point = points[i];
                if (point?.ObjSS == null || !OrbitAcceleration.Finite(point.ArrivalTime) || i > 0 && point.ArrivalTime <= points[i - 1].ArrivalTime) return null;
                BodyOrbit body = null;
                if ((point.ObjSS.bIsBO || point.ObjSS.bBOLocked || point.ObjSS.bOrbitLocked) && point.ObjSS.strBOPORShip != null) system.aBOs.TryGetValue(point.ObjSS.strBOPORShip, out body);
                captured[i] = new SffPathTarget.Point { Epoch = point.ArrivalTime, State = OrbitalMotion.Read(point.ObjSS), Body = body,
                    Offset = new DVector(point.ObjSS.vBOOffsetx, point.ObjSS.vBOOffsety) * OrbitalMotion.KmPerAU };
            }
            return new SffPathTarget { Points = captured };
        }
        var model = new SffTarget { Initial = OrbitalMotion.Read(situation), Start = start };
        if (situation.bIsBO || situation.bBOLocked || situation.bOrbitLocked) {
            if (situation.strBOPORShip == null || !system.aBOs.TryGetValue(situation.strBOPORShip, out model.Locked)) return null;
            model.Offset = new DVector(situation.vBOOffsetx, situation.vBOOffsety) * OrbitalMotion.KmPerAU;
        } else model.Reference = environment.Select(model.Initial.Position, start, out _);
        return model;
    }
    internal static bool IsTravelCommand(string command) => command != null && (command.StartsWith("FlyTo", StringComparison.Ordinal) || command == "HaulShip" || command == "Dock");
    internal static bool IsCombatCommand(string command) => command != null && (command.IndexOf("Shoot", StringComparison.OrdinalIgnoreCase) >= 0 ||
        command.IndexOf("Attack", StringComparison.OrdinalIgnoreCase) >= 0 || command.IndexOf("Combat", StringComparison.OrdinalIgnoreCase) >= 0 || command.IndexOf("Evad", StringComparison.OrdinalIgnoreCase) >= 0);
    static string Ids(List<Ship> ships) {
        var ids = new string[ships.Count];
        for (int i = 0; i < ids.Length; i++) ids[i] = ships[i].strRegID;
        return string.Join(",", ids);
    }
    MotionState EndState(Entry entry) {
        var state = entry.Plan.Outcome.State;
        if (!entry.Member) return state;
        Rotation(entry.Plan, out double rotation, out _);
        DVector offset = new DVector(entry.Offset.X * Math.Cos(rotation) - entry.Offset.Y * Math.Sin(rotation),
            entry.Offset.X * Math.Sin(rotation) + entry.Offset.Y * Math.Cos(rotation));
        return new MotionState(state.Position + offset, state.Velocity);
    }
    void Rotation(Plan plan, out double rotation, out double velocity) {
        rotation = plan.Rotation + plan.AngularVelocity * seconds + .5 * plan.AngularAcceleration * seconds * seconds;
        velocity = plan.AngularVelocity + plan.AngularAcceleration * seconds;
        if (velocity * plan.AngularVelocity < 0) velocity = 0;
    }
    internal bool Advance(ShipSitu situation, double duration, bool ignoreAcceleration) {
        if (ignoreAcceleration || duration != seconds || StarSystem.fEpoch != start + seconds || !entries.TryGetValue(situation, out Entry entry)) return false;
        // Recheck at dispatch: another mod may have installed a plan/lock during the update.
        if (situation.NavData != null || situation.bIsBO || situation.bBOLocked || situation.bOrbitLocked || situation.bIgnoreGrav) return false;
        var plan = entry.Plan;
        if (Hidden(plan)) return false;
        OrbitalMotion.Write(situation, EndState(entry));
        Rotation(plan, out double rotation, out double angularVelocity);
        situation.fRot = (float)(rotation + (entry.Member ? entry.RotationOffset : 0));
        situation.fW = (float)angularVelocity;
        if (angularVelocity == 0 && plan.AngularVelocity * plan.AngularAcceleration < 0) situation.fA = 0;
        situation.strBOPORShip = plan.Outcome.Body?.Native.strName;
        var acceleration = plan.Outcome.Acceleration * (1 / OrbitalMotion.KmPerAU);
        situation.fAccelMagSquaredLast = acceleration.Dot(acceleration);
        if (!entry.Member) plan.Applied = true;
        return true;
    }
    internal bool Gravity(ShipSitu situation, ref UnityEngine.Vector2 gravity, ref BodyOrbit nearest, ref BodyOrbit selected) {
        if (StarSystem.fEpoch != start + seconds || !entries.TryGetValue(situation, out Entry entry) || situation.NavData != null || situation.bBOLocked || situation.bOrbitLocked || situation.bIsBO) return false;
        if (Hidden(entry.Plan)) return false;
        var state = EndState(entry);
        var body = environment.Select(state.Position, start + seconds, out var close);
        if (body == null) return false;
        DVector force = OrbitalMotion.Gravity(state.Position - body.At(start + seconds).Position, body.Mu) * (1 / OrbitalMotion.KmPerAU);
        gravity = new UnityEngine.Vector2((float)force.X, (float)force.Y);
        nearest = close?.Native; selected = body.Native; return true;
    }
    internal void BeforeAi() {
        if (beforeAi) return;
        beforeAi = true;
        foreach (var plan in plans) {
            if (!plan.Applied || Hidden(plan)) continue;
            if (plan.Outcome.Impact != null) {
                try {
                    foreach (var ship in plan.Ships) if (ship != null && !ship.bDestroyed && !plan.ImpactResolved) {
                        CollisionManager.CheckCollisions(ship);
                        plan.ImpactResolved |= ship.bDestroyed;
                    }
                    // Native collision response moves members directly. Rebuild the
                    // surviving group anchor so it cannot overwrite that bounce.
                    foreach (var ship in plan.Ships) if (ship != null && !ship.bDestroyed && ship.HasDockGroup) { ship.DockGroup.UpdateAnchor(); break; }
                } catch (Exception ex) { failures++; Ostrabus.OstrabusPlugin.Logger?.LogError("[Orbital SFF] Collision handling: " + ex); }
                collisions++;
            }
            if (!plan.Travel || plan.Target == null || plan.Outcome.Impact != null) continue;
            if (plan.TargetShip != null && plan.TargetShip.bDestroyed) continue;
            ShipSitu actualTarget = plan.TargetShip?.objSS ?? plan.Target;
            var target = OrbitalMotion.Read(actualTarget);
            var actual = OrbitalMotion.Read(plan.Anchor);
            plan.Ready = (actual.Position - target.Position).Length <= plan.Handoff && (actual.Velocity - target.Velocity).Length <= plan.ArrivalSpeed;
        }
    }
    internal bool AllowCatchup(AIShip pilot) {
        if (!pilots.TryGetValue(pilot, out Plan plan) || !plan.Applied || Hidden(plan)) return true;
        if (plan.Ready && plan.Outcome.Impact == null) return true;
        suppressedCatchup++; return false;
    }
    void AddRow(object row) { if (rows.Count < 512) rows.Add(row); else rowsTruncated = true; }
    internal static object Snapshot() => last;
    internal static void Reset() { Current?.Dispose(); Current = null; last = SnapshotJson.Map("state", "No corrected SFF jump recorded."); }
    public void Dispose() {
        if (disposed) return;
        disposed = true;
        try {
            foreach (var plan in plans) AddRow(SnapshotJson.Map("ships", Ids(plan.Ships), "mode", plan.Mode, "applied", plan.Applied,
                "targetId", plan.TargetShip?.strRegID, "arrivalAfterSeconds", plan.Outcome.Arrived ? (object)plan.Outcome.ArrivalAfter : null,
                "nativeArrivalAllowed", plan.Ready, "steps", plan.Outcome.Steps, "gravitySwitches", plan.Outcome.Switches,
                "atmosphere", plan.Outcome.Atmosphere, "budgetFallback", plan.Outcome.BudgetFallback,
                "planetaryDetour", plan.Detour, "impactBody", plan.Outcome.Impact?.Native.strName, "aerodynamicModels", plan.AeroModels));
            last = SnapshotJson.Map("startEpoch", start, "skipSeconds", seconds, "prepared", plans.Count,
                "nativeCourses", excludedRoutes, "nativeCombat", excludedCombat, "nativeLocked", excludedLocks,
                "nativeOther", excludedOther, "nativeInvalidStart", excludedInvalid, "nativeHidden", excludedHidden, "failures", failures, "suppressedInstantCatchup", suppressedCatchup,
                "impactChecks", collisions, "preparationMs", preparationMs, "elapsedWallMs", (Stopwatch.GetTimestamp() - began) * 1000d / Stopwatch.Frequency,
                "items", rows.ToArray(), "itemsTruncated", rowsTruncated);
        } finally {
            entries.Clear(); pilots.Clear(); plans.Clear(); rows.Clear(); environment.Bodies.Clear();
            if (ReferenceEquals(Current, this)) Current = previous;
        }
    }
}
