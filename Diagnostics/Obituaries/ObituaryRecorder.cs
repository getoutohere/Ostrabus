using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using BepInEx.Configuration;
using HarmonyLib;
using Ostranauts.ShipGUIs.Utilities;
using Ostranauts.Ships.AIPilots;

namespace Ostrabus.Diagnostics.Obituaries;

internal static class ObituaryRecorder {
    internal static bool Enabled;
    internal static ObituaryStore Store;
    static string session = Guid.NewGuid().ToString("N"), branch = Guid.NewGuid().ToString("N");
    static string sourceSave;
    static long failures, restored, disposals, eventId;
    static volatile bool loading;
    [ThreadStatic] internal static Scope Current;
    [ThreadStatic] static bool observing;
    static readonly FieldInfo Factions = AccessTools.Field(typeof(CondOwner), "aFactions");
    static readonly FieldInfo CachedGas = AccessTools.Field(typeof(CondOwner), "_gasContainerComponentReference");
    static readonly FieldInfo RawHull = AccessTools.Field(typeof(AIShip), "_shipUs");
    static readonly FieldInfo NavPoints = AccessTools.Field(typeof(NavData), "_navPoints");
    static readonly string NativeMvid = typeof(CondOwner).Assembly.ManifestModule.ModuleVersionId.ToString();
    static readonly string PluginMvid = typeof(ObituaryRecorder).Assembly.ManifestModule.ModuleVersionId.ToString();
    internal static void Initialize(ConfigFile config) {
        Enabled = config.Bind("Diagnostics", "Obituaries", true,
            "Automatically record NPC deaths and living removals to bounded external JSONL files (64 MiB total). No save changes. Restart to apply.").Value;
        if (!Enabled) return;
        try {
            Store = new ObituaryStore(Path.Combine(BepInEx.Paths.BepInExRootPath, "Obituaries"));
            CrewSim.OnGameFinishedLoading.AddListener(Loaded);
            Emit("recorder-start", null, null, null);
        } catch (Exception ex) { Enabled = false; Fault(ex); }
    }
    internal static void Stop() {
        try {
            if (Enabled) { Emit("recorder-stop", null, null, null); CrewSim.OnGameFinishedLoading.RemoveListener(Loaded); }
        } catch (Exception ex) { Fault(ex); }
        finally { Enabled = false; Store?.Stop(); }
    }
    internal static object Status() => Map("enabled", Enabled, "session", session, "branch", branch, "loading", loading, "sourceSave", sourceSave,
        "captureErrors", Interlocked.Read(ref failures), "restoredDeadSuppressed", Interlocked.Read(ref restored),
        "runtimeDisposals", Interlocked.Read(ref disposals), "storage", Store?.Status());
    internal static void Fault(Exception ex) {
        // Never throw from observation or call gameplay logging recursively.
        Interlocked.Increment(ref failures);
        if (Interlocked.Read(ref failures) == 1) try { OstrabusPlugin.Logger?.LogWarning("[Obituaries] Evidence capture failed: " + ex.GetType().Name + ": " + ex.Message); } catch { }
    }
    internal static void BeginLoad(string filename) {
        try {
            if (!Enabled) return;
            branch = Guid.NewGuid().ToString("N"); loading = true;
            sourceSave = Cut(Path.GetFileName(filename), 128);
            Emit("load-start", null, null, null);
        } catch (Exception ex) { Fault(ex); }
    }
    internal static void WorldReset() { if (Enabled && !loading) BeginLoad(null); }
    static void Loaded() {
        try { if (Enabled) { loading = false; Emit("load-complete", null, null, null); } } catch (Exception ex) { Fault(ex); }
    }
    internal static string Cut(string text, int max = 128) => text == null || text.Length <= max ? text : text.Substring(0, max) + "[truncated]";
    internal static Dictionary<string, object> Map(params object[] pairs) {
        var result = new Dictionary<string, object>();
        for (int i = 0; i < pairs.Length; i += 2) result[(string)pairs[i]] = pairs[i + 1];
        return result;
    }
    internal static string Stack() => Cut(new StackTrace(2, false).ToString(), 6144);
    internal static bool Person(CondOwner co) => !ReferenceEquals(co, null) && !co.bDestroyed &&
        (co.pspec != null || co.mapConds != null && (co.mapConds.ContainsKey("IsHuman") || co.mapConds.ContainsKey("IsRobot")));
    internal static bool PersonData(CondOwner co, JsonCondOwner definition, JsonCondOwnerSave saved) {
        if (Person(co) || saved?.social != null) return true;
        bool Tagged(string[] conditions) {
            if (conditions != null) foreach (string tag in conditions)
                if (tag == "IsHuman" || tag == "IsRobot" || tag != null &&
                    (tag.StartsWith("IsHuman=", StringComparison.Ordinal) || tag.StartsWith("IsRobot=", StringComparison.Ordinal))) return true;
            return false;
        }
        return Tagged(saved?.aConds) || Tagged(definition?.aStartingConds);
    }
    static bool Canonical(CondOwner co) => co.strID != null && DataHandler.mapCOs != null &&
        DataHandler.mapCOs.TryGetValue(co.strID, out var registered) && ReferenceEquals(registered, co);
    internal static bool Eligible(CondOwner co) {
        if (!Enabled || observing || ReferenceEquals(co, null) || co.bDestroyed) return false;
        // Initialization may run before registration. A scoped saved person is
        // still relevant; arbitrary unregistered portrait/editor clones are not.
        if (Person(co) && Canonical(co)) return true;
        for (var scope = Current; scope != null; scope = scope.Previous)
            if (ReferenceEquals(scope.Actor, co) || scope.PersonSpec != null && ReferenceEquals(scope.PersonSpec, co.pspec)) return true;
        return false;
    }
    internal sealed class Scope {
        internal readonly Scope Previous;
        internal readonly string Kind;
        internal readonly CondOwner Actor;
        internal readonly PersonSpec PersonSpec;
        internal readonly bool? SavedAlive;
        internal readonly Ship Ship;
        internal readonly string VesselEvent;
        internal object VesselSnapshot;
        internal Dictionary<CondOwner, object> Detached;
        internal int DetachedOmitted;
        internal Scope(string kind, CondOwner actor = null, bool? savedAlive = null, PersonSpec person = null, Ship ship = null, string vesselEvent = null) {
            Previous = Current; Kind = kind; Actor = actor; SavedAlive = savedAlive; PersonSpec = person; Ship = ship; VesselEvent = vesselEvent; Current = this;
        }
        internal void Close() { Current = Previous; Detached?.Clear(); }
    }
    sealed class ActorReferences : IEqualityComparer<CondOwner> {
        internal static readonly ActorReferences Instance = new ActorReferences();
        public bool Equals(CondOwner x, CondOwner y) => ReferenceEquals(x, y);
        public int GetHashCode(CondOwner co) => RuntimeHelpers.GetHashCode(co);
    }
    static bool RuntimeDisposal() {
        for (var scope = Current; scope != null; scope = scope.Previous)
            if (scope.Kind == "world-disposal" || scope.Kind == "ship-replacement") return true;
        return false;
    }
    static bool RestoredCorpse(CondOwner co) {
        for (var scope = Current; scope != null; scope = scope.Previous)
            if (scope.SavedAlive == false && (ReferenceEquals(scope.Actor, co) || scope.PersonSpec != null &&
                (ReferenceEquals(scope.PersonSpec, co.pspec) || scope.PersonSpec.strCO != null && scope.PersonSpec.strCO == co.strID))) return true;
        return false;
    }
    internal static void Detaching(CondOwner co) {
        try {
            // Only retain references until the owning synchronous operation
            // returns. No lifetime NPC registry or polling of people/ships.
            if (Current == null || !Eligible(co) || ReferenceEquals(co.ship, null)) return;
            Current.Detached ??= new Dictionary<CondOwner, object>(ActorReferences.Instance);
            if (Current.Detached.Count < 64 || Current.Detached.ContainsKey(co)) Current.Detached[co] = Vessel(co.ship);
            else Current.DetachedOmitted++;
        } catch (Exception ex) { Fault(ex); }
    }
    static object Location(CondOwner co) {
        if (!ReferenceEquals(co.ship, null)) return Vessel(co.ship);
        for (var scope = Current; scope != null; scope = scope.Previous) {
            if (scope.Detached != null && scope.Detached.TryGetValue(co, out var detached)) return detached;
            if (!ReferenceEquals(scope.Ship, null)) return scope.VesselSnapshot ?? Vessel(scope.Ship);
        }
        return Map("id", Cut(co.jCOS?.strRegIDLast), "source", "saved-last-location; current location unavailable");
    }
    static string[] Strings(IEnumerable<string> source, int limit = 16) {
        var result = new List<string>();
        if (source != null) foreach (string value in source) { if (result.Count == limit) break; result.Add(Cut(value)); }
        return result.ToArray();
    }
    static object Conditions(CondOwner co) {
        var selected = new List<object>(); int omitted = 0;
        // Fatal conditions take priority if a pathological actor has many stats.
        if (co.mapConds != null) for (int pass = 0; pass < 2; pass++) foreach (var pair in co.mapConds) {
            var c = pair.Value;
            if (c == null || (pass == 0) != c.bFatal || !c.bFatal && !c.bKO && !Relevant(pair.Key)) continue;
            if (selected.Count == 48) { omitted++; continue; }
            selected.Add(Map("id", Cut(pair.Key), "value", c.fCount, "fatal", c.bFatal, "ko", c.bKO));
        }
        return Map("selected", selected, "omitted", omitted);
    }
    static bool Relevant(string name) => name != null && (name.StartsWith("Dc", StringComparison.Ordinal) ||
        name.StartsWith("Wound", StringComparison.Ordinal) || name.StartsWith("StatBlood", StringComparison.Ordinal) ||
        name.StartsWith("StatO2", StringComparison.Ordinal) || name == "IsDead" || name == "IsWoundVital" ||
        name == "InSocialCombat" || name == "IsPlayer" || name == "IsPlayerCrew");
    internal static Dictionary<string, object> Actor(CondOwner co) => Map("id", Cut(co.strID), "name", Cut(co.FriendlyName),
        "runtimeIdentity", RuntimeHelpers.GetHashCode(co), "alive", co.bAlive, "registered", Canonical(co),
        "actorEpoch", co.Clock?.Epoch, "personSpecId", Cut(co.pspec?.strCO),
        "factions", Strings(Factions.GetValue(co) as IEnumerable<string>), "factionsTruncated", (Factions.GetValue(co) as ICollection<string>)?.Count > 16,
        "conditions", Conditions(co),
        "currentAction", Cut(co.aQueue != null && co.aQueue.Count > 0 ? co.aQueue[0]?.strName : null),
        "location", Location(co), "environment", Environment(co));
    static double? Amount(CondOwner co, string id) => co?.mapConds != null && co.mapConds.TryGetValue(id, out var c) ? c?.fCount : null;
    static object Environment(CondOwner co) {
        var room = co.currentRoom;
        var owner = room?.CO;
        var gas = ReferenceEquals(owner, null) ? null : CachedGas.GetValue(owner) as GasContainer;
        var mols = new List<object>();
        if (!ReferenceEquals(gas, null) && gas.mapGasMols1 != null) foreach (var pair in gas.mapGasMols1) {
            if (mols.Count == 12) break;
            mols.Add(Map("gas", Cut(pair.Key), "moles", pair.Value));
        }
        return Map("source", "cached room and gas fields; no atmosphere update or component lookup", "roomId", Cut(owner?.strID),
            "pressurePa", Amount(owner, "StatGasPressure"), "temperatureK", Amount(owner, "StatGasTemp"),
            "volumeM3", Amount(owner, "StatVolume"), "gasMoles", mols,
            "gasMolesTruncated", !ReferenceEquals(gas, null) && gas.mapGasMols1?.Count > 12, "roomPresent", room != null);
    }
    internal static object Vessel(Ship ship) {
        if (ReferenceEquals(ship, null)) return null;
        var s = ship.objSS;
        return Map("id", Cut(ship.strRegID), "name", Cut(ship.publicName), "source", "stored pre-operation state",
            "classification", ship.Classification.ToString(), "loadState", ship.LoadState.ToString(), "destroyed", ship.bDestroyed,
            "hidden", ship.HideFromSystem, "removeFlag", ship.bRemove, "target", Cut(ship.targetRegID),
            "retirement", Map("staleEpochRaw", Amount(ship.ShipCO, "IsStale"), "lastPlayerVisitEpoch", ship.fLastVisit,
                "despawnedAt", Cut(ship.strAIDespawnedAt), "isAI", ship.IsAIShip, "shipConditionsFrozen", ship.ShipCO?.bFreezeConds),
            "rcsKgCached", ship.fShallowRCSRemass, "controllers", Controllers(ship), "dockGroup", Docking(s), "route", Route(s?.NavData),
            "reference", Reference(s),
            "motion", s == null ? null : Map("xAU", s.vPosx, "yAU", s.vPosy, "vxAUPerSecond", s.vVelX, "vyAUPerSecond", s.vVelY,
                "bodyReference", Cut(s.strBOPORShip), "bodyLocked", s.bBOLocked, "orbitLocked", s.bOrbitLocked, "isBody", s.bIsBO,
                "inputAcceleration", Map("x", s.vAccIn.x, "y", s.vAccIn.y), "rcsAcceleration", Map("x", s.vAccRCS.x, "y", s.vAccRCS.y),
                "externalAcceleration", Map("x", s.vAccEx.x, "y", s.vAccEx.y), "dragAcceleration", Map("x", s.vAccDrag.x, "y", s.vAccDrag.y),
                "liftAcceleration", Map("x", s.vAccLift.x, "y", s.vAccLift.y), "lastOrbitUpdateEpoch", s.fOrbitTimeLast,
                "rotationRad", s.fRot, "rotationRadPerSecond", s.fW, "routeEndEpoch", RouteEnd(s.NavData)),
            "note", "Cached coordinates, not recalculated collision distances. Reference IDs may identify natural bodies or manufactured stations.");
    }
    static double? RouteEnd(NavData route) {
        var points = route == null ? null : NavPoints.GetValue(route) as List<NavDataPoint>;
        return points != null && points.Count > 0 ? points[points.Count - 1]?.ArrivalTime : null;
    }
    static object Reference(ShipSitu s) {
        if (s?.strBOPORShip == null) return null;
        var world = CrewSim.system;
        if (world?.aBOs != null && world.aBOs.TryGetValue(s.strBOPORShip, out var body) && body != null)
            return Map("id", Cut(s.strBOPORShip), "type", body.GetType().FullName, "cachedEpoch", body.dTimeCalcLast,
                "xAU", body.dXReal, "yAU", body.dYReal, "radiusAU", body.fRadius,
                "note", "Native BodyOrbit reference; not assumed to be a natural celestial surface.");
        if (world?.dictShips != null && world.dictShips.TryGetValue(s.strBOPORShip, out var ship) && !ReferenceEquals(ship, null))
            return Map("id", Cut(s.strBOPORShip), "type", "Ship", "classification", ship.Classification.ToString());
        return Map("id", Cut(s.strBOPORShip), "type", "unresolved");
    }
    static object Controllers(Ship ship) {
        int scanned = 0; var matches = new List<object>();
        if (AIShipManager.dictAIs != null) foreach (var group in AIShipManager.dictAIs) {
            if (group.Value == null) continue;
            foreach (var ai in group.Value) {
                if (++scanned > 4096) return Map("matches", matches, "scanComplete", false);
                if (ai == null || !ReferenceEquals(RawHull.GetValue(ai), ship)) continue;
                if (matches.Count == 8) return Map("matches", matches, "scanComplete", false);
                string command = null;
                try { command = ai.ActiveCommandName; } catch (NullReferenceException) { }
                matches.Add(Map("region", Cut(group.Key), "role", ai.AICharacter?.AIType.ToString(),
                    "home", Cut(ai.HomeStation), "command", Cut(command)));
            }
        }
        return Map("matches", matches, "scanComplete", true);
    }
    static object Docking(ShipSitu situation) {
        var group = situation?._dockGroup;
        var ids = new List<string>();
        if (group?.DockedShips != null) foreach (var member in group.DockedShips) {
            if (ids.Count == 16) break;
            ids.Add(Cut(member?.RegId));
        }
        return Map("id", Cut(group?.ID), "members", ids, "truncated", group?.DockedShips?.Count > 16);
    }
    static object Route(NavData route) {
        if (route == null) return null;
        var points = NavPoints.GetValue(route) as List<NavDataPoint>;
        var rows = new List<object>();
        // Select fixed evenly spaced samples; never advance or clone native routes.
        if (points != null) for (int i = 0, count = Math.Min(points.Count, 8); i < count; i++) {
            int index = count < 2 ? 0 : (int)((long)i * (points.Count - 1) / (count - 1));
            var p = points[index]; var s = p?.ObjSS;
            rows.Add(Map("index", index, "epoch", p?.ArrivalTime, "rcsKg", p?.FuelLevel, "torchFuel", p?.TorchFuelLevel,
                "xAU", s?.vPosx, "yAU", s?.vPosy, "vxAUPerSecond", s?.vVelX, "vyAUPerSecond", s?.vVelY,
                "bodyReference", Cut(s?.strBOPORShip)));
        }
        return Map("isTorching", route.IsTorching, "inAtmo", route.InAtmo, "pointCount", points?.Count,
            "samples", rows, "sampled", points?.Count > 8, "source", "stored waypoint states; no playback or ephemeris updates");
    }
    internal sealed class Observation {
        internal CondOwner Actor;
        internal Dictionary<string, object> Before;
        internal bool Alive;
        internal bool CorpseRestore;
        internal string Stack;
        internal string NpcId, ShipId;
    }
    internal static Observation Before(CondOwner co, bool killing) {
        bool previousObservation = observing;
        Observation result = null;
        try {
            if (!Eligible(co)) return null;
            if (!killing && RuntimeDisposal()) { Interlocked.Increment(ref disposals); return null; }
            if (!co.bAlive) return null;
            observing = true;
            result = new Observation { Actor = co, Alive = co.bAlive, CorpseRestore = RestoredCorpse(co), NpcId = Cut(co.strID),
                ShipId = Cut(co.ship?.strRegID), Stack = Stack() };
            result.Before = Actor(co);
            if (result.Before.TryGetValue("location", out var location) && location is Dictionary<string, object> map && map.TryGetValue("id", out var id))
                result.ShipId = id as string;
            return result;
        } catch (Exception ex) {
            Fault(ex);
            if (result != null) result.Before = Map("id", result.NpcId, "evidenceIncomplete", true, "captureError", Cut(ex.Message, 256));
            return result;
        } finally { observing = previousObservation; }
    }
    internal static void After(Observation state, bool killing, Exception exception) {
        if (state == null) return;
        try {
            bool dead = state.Alive && !state.Actor.bAlive;
            bool removed = !killing && state.Actor.bDestroyed;
            if (!dead && !removed && exception == null) return;
            if (state.CorpseRestore && dead) { Interlocked.Increment(ref restored); return; }
            string kind = killing && dead ? "death" : removed ? (dead ? "removal-after-death" : "living-removal") : "operation-failed";
            var evidence = Map("before", state.Before, "aliveAfter", state.Actor.bAlive, "destroyedAfter", state.Actor.bDestroyed,
                "registeredAfter", Canonical(state.Actor),
                "callerStack", state.Stack, "exception", Cut(exception?.ToString(), 2048),
                "verdict", dead ? "Observed alive-to-dead transition; upstream legitimacy unclassified." :
                    "Runtime removal observed; persistent identity death is not established.");
            if (dead && state.Actor.mapConds != null) evidence["conditionsAfter"] = Conditions(state.Actor);
            Emit(kind, state.NpcId, state.ShipId, evidence);
        } catch (Exception ex) { Fault(ex); }
        finally {
            if (!killing) for (var scope = Current; scope != null; scope = scope.Previous) scope.Detached?.Remove(state.Actor);
        }
    }
    internal static Scope BeginShip(Ship ship, bool despawning) {
        Scope scope = null;
        try {
            if (!Enabled || ReferenceEquals(ship, null) || ship.bDestroyed || ship.People == null || ship.People.Count == 0) return null;
            // Projectile and empty-hull teardown is a hot path. There can be no
            // crew record here; avoid snapshot allocation and controller scans.
            bool disposal = RuntimeDisposal();
            string id = session + ":v" + Interlocked.Increment(ref eventId);
            scope = new Scope(despawning ? "ship-despawn" : "ship-destruction", ship: ship, vesselEvent: id);
            if (disposal) return scope;
            scope.VesselSnapshot = Vessel(ship);
            var crew = new List<object>(); int total = 0;
            if (ship.People != null) foreach (var person in ship.People) {
                if (person == null) continue;
                total++;
                if (crew.Count == 32) continue;
                CondOwner co = null;
                if (person.strCO != null) DataHandler.mapCOs?.TryGetValue(person.strCO, out co);
                crew.Add(Map("id", Cut(person.strCO), "alive", ReferenceEquals(co, null) ? person.bAlive : co.bAlive,
                    "aliveSource", ReferenceEquals(co, null) ? "person-spec" : "runtime-actor"));
            }
            if (total > 0) Emit(despawning ? "vessel-removal-begin" : "vessel-destruction-begin", null, Cut(ship.strRegID),
                Map("ship", scope.VesselSnapshot, "crew", crew, "crewCount", total, "crewTruncated", total > 32,
                    "callerStack", Stack(), "note", "Entry evidence; completion and crew outcomes are separate events."));
            return scope;
        } catch (Exception ex) { Fault(ex); return scope; }
    }
    internal static void EndScope(Scope scope, Exception exception = null) {
        if (scope == null) return;
        try {
            if (exception != null && scope.VesselEvent != null) Emit("vessel-operation-failed", null, Cut(scope.Ship?.strRegID), Map("exception", Cut(exception.ToString(), 2048)));
        } catch (Exception ex) { Fault(ex); } finally { scope.Close(); }
    }
    internal static void Emit(string kind, string npcId, string shipId, object evidence) {
        if (!Enabled || Store == null) return;
        var operations = new List<string>(); string vessel = null; int omitted = 0;
        var scope = Current;
        for (; scope != null && operations.Count < 16; scope = scope.Previous) {
            operations.Add(scope.Kind); if (vessel == null) vessel = scope.VesselEvent;
            omitted += scope.DetachedOmitted;
        }
        Store.Enqueue(Map("format", "ostrabus-obituary-v1", "session", session, "branch", branch,
            "utc", DateTime.UtcNow.ToString("O"), "epoch", StarSystem.fEpoch, "loading", loading, "sourceSave", sourceSave,
            "runtimeWorldIdentity", CrewSim.system == null ? (int?)null : RuntimeHelpers.GetHashCode(CrewSim.system),
            "nativeMvid", NativeMvid, "pluginMvid", PluginMvid, "kind", kind, "npcId", npcId, "shipId", shipId,
            "mechanism", operations.Count > 0 ? operations[0] : "caller-stack", "operations", operations, "vesselEvent", vessel,
            "contextTruncated", scope != null || omitted > 0,
            "sff", GUIFFWD.Active, "jumpSource", global::Ostrabus.Orbital.JumpBoundary.Current?.Source,
            "captureErrorsSoFar", Interlocked.Read(ref failures), "evidence", evidence));
    }
}
