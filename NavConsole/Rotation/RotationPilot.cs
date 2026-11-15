using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Ostranauts.Core;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.AIPilots.Interfaces;
using Ostranauts.Ships.Commands;

namespace Ostrabus.NavConsole.Rotation;

internal struct RotationPose {
    internal bool Applied;
    internal float Rotation, Rate;
    internal Ship Ship;
}

// A normal native Auto controller owns this command. Only its angular integration
// is specialized; the world's position, velocity, gravity and collisions stay native.
internal sealed class OstrabusRotationPilot : BaseCommand {
    internal const string CommandName = "OstrabusRotationPilot";
    const string Format = "ostrabus.rotation.v1";
    // Large turns may use full native yaw authority, then coast at a bounded
    // angular speed and brake. Manual translation keeps its own throttle.
    const float YawLimit = 1;
    readonly IAICharacter character;
    internal readonly RotationOrder Order = new();
    internal AIShip Owner;
    internal ShipSitu TrackedSituation;
    internal string ConsoleId, HardwareId, TargetKind;
    internal double SensorX, SensorY;
    double sampleEpoch = double.NaN;
    double? sampleBearing;
    float translationX, translationY;
    double inputEpoch = double.NegativeInfinity;
    bool ready;
    public override string ActiveCommandName => CommandName;
    public override string DescriptionFriendly => Order.Hold ? "Holding bearing" : "Rotating";

    public OstrabusRotationPilot(IAICharacter pilot) : base(pilot) { character = pilot; }

    public override string[] SaveData {
        get => new[] { Format, Order.Heading.ToString("R", CultureInfo.InvariantCulture), Order.Offset.ToString("R", CultureInfo.InvariantCulture),
            Order.Hold ? "1" : "0", TargetKind ?? "", Order.TargetId ?? "", ConsoleId ?? "", HardwareId ?? "",
            SensorX.ToString("R", CultureInfo.InvariantCulture), SensorY.ToString("R", CultureInfo.InvariantCulture) };
        set {
            ready = false;
            Order.Cancel(); RotationRuntime.Detach(this);
            if (!ValidPayload(value)) return;
            Number(value[1], out double heading); Number(value[2], out double offset);
            Number(value[8], out double sx); Number(value[9], out double sy);
            ConsoleId = value[6]; HardwareId = value[7]; TargetKind = value[4]; SensorX = sx; SensorY = sy;
            Order.Restore(true, value[3] == "1", value[4].Length == 0 ? null : value[5], heading, offset);
            ready = true;
            RotationRuntime.Attach(this);
        }
    }

    static bool Number(string text, out double value) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && RotationPid.Finite(value);
    internal static bool ValidPayload(string[] value) => value != null && value.Length == 10 && !Array.Exists(value, text => text == null) &&
        value[0] == Format && value[6].Length > 0 && value[7].Length > 0 && Number(value[1], out _) && Number(value[2], out _) &&
        Number(value[8], out _) && Number(value[9], out _) && (value[3] == "0" || value[3] == "1") &&
        (value[4] == "" || value[4] == "ship" || value[4] == "body" || value[4] == "object") && (value[4] == "" || value[5].Length > 0);

    internal void Begin(CondOwner console, CondOwner hardware, RotationDirection direction, string kind, string id, double sx, double sy, bool hold) {
        ConsoleId = console.strID; HardwareId = hardware.strID; TargetKind = kind;
        SensorX = sx; SensorY = sy;
        double? bearing = Bearing(kind, id, out double angle) ? angle : (double?)null;
        if (!bearing.HasValue) TargetKind = null;
        Order.Command(direction, ShipUs.objSS.fRot, bearing.HasValue ? id : null, bearing);
        if (hold) Order.SetHold(true);
        ready = true;
        Remember();
    }

    public override CommandStatus RunCommand(Blackboard blackboard) {
        var anchor = ShipUs?.objSS?._dockGroup?.Anchor;
        if (!ready || !HardwareReady() || ShipUs.objSS == null || ShipUs.objSS.bBOLocked || ShipUs.objSS.bIsBO ||
            anchor != null && (anchor.bBOLocked || anchor.bIsBO)) {
            Order.Cancel(); RotationRuntime.Detach(this); return Success;
        }
        RotationRuntime.Attach(this);
        return Order.Active ? Running : Success;
    }
    public override CommandStatus ResolveInstantly(Blackboard blackboard) => Order.Active ? Running : Success;
    public override void OnExit(Blackboard blackboard) => RotationRuntime.Detach(this);

    internal bool BelongsTo(AIShip ai) => ai != null && ReferenceEquals(ai.Ship, ShipUs) && ReferenceEquals(ai.AICharacter, character) && ai.AIType == AIType.Auto;
    internal void SetTranslation(float x, float y) { translationX = x; translationY = y; inputEpoch = StarSystem.fEpoch; }
    internal void SetHold(bool enabled) {
        Order.SetHold(enabled);
        Remember();
        // OFF releases the actuator; it is not an attitude-stop command.
        // Retire the native controller immediately so pending input is no longer
        // captured. Remember the original order before cancellation clears it.
        if (!enabled) RotationRuntime.Cancel(ShipUs);
    }
    internal void Remember() {
        if (DataHandler.mapCOs != null && DataHandler.mapCOs.TryGetValue(ConsoleId, out var console) &&
            console?.mapGUIPropMaps != null && console.mapGUIPropMaps.TryGetValue("Panel A", out var map)) {
            string saved = JsonConvert.SerializeObject(SaveData);
            map[RotationModule.LastCommandKey] = saved;
            // GUIData keeps a separate working map; SaveAndClose publishes it
            // over Panel A. Write through its native setter while it is open.
            var ui = GUIOrbitDraw.Instance;
            if (ui != null && ui.COSelfBase()?.strID == ConsoleId)
                ui.SetPropMapData(RotationModule.LastCommandKey, saved);
        }
    }

    bool HardwareReady() {
        if (ShipUs == null || ShipUs.bDestroyed) return false;
        if (ShipUs.LoadState <= Ship.Loaded.Shallow) return SavedHardwareReady();
        if (DataHandler.mapCOs == null || !DataHandler.mapCOs.TryGetValue(ConsoleId, out var console) ||
            !DataHandler.mapCOs.TryGetValue(HardwareId, out var hardware) || console == null || hardware == null ||
            console.bDestroyed || hardware.bDestroyed || !ReferenceEquals(console.ship, ShipUs) || !ReferenceEquals(hardware.ship, ShipUs) ||
            console.HasCond("IsOff") || console.HasCond("IsDamagedSoftware") || hardware.HasCond("IsDamaged")) return false;
        var parent = hardware.objCOParent;
        for (int depth = 0; depth < 32 && parent != null; depth++, parent = parent.objCOParent)
            if (ReferenceEquals(parent, console)) return true;
        return false;
    }

    JsonItem[] checkedItems;
    JsonCondOwnerSave[] checkedConditions;
    JsonItem checkedModule;
    readonly List<JsonItem> checkedParents = new();
    readonly List<string> checkedParentIds = new();
    readonly List<JsonCondOwnerSave> checkedOwners = new();
    readonly List<string[]> checkedEquations = new();
    bool savedHardwareValid;
    bool SavedSnapshotUnchanged() {
        for (int i = 0; i < checkedParents.Count; i++)
            if ((checkedParents[i].strParentID ?? checkedParents[i].strSlotParentID) != checkedParentIds[i]) return false;
        for (int i = 0; i < checkedOwners.Count; i++) {
            var equations = checkedOwners[i].aConds;
            var previous = checkedEquations[i];
            if (equations == null || previous == null) { if (!ReferenceEquals(equations, previous)) return false; continue; }
            if (equations.Length != previous.Length) return false;
            for (int j = 0; j < equations.Length; j++) if (equations[j] != previous[j]) return false;
        }
        return true;
    }
    bool SavedHardwareReady() {
        var json = ShipUs.json;
        if (json?.aItems == null) return false;
        if (ReferenceEquals(checkedItems, json.aItems) && ReferenceEquals(checkedConditions, json.aCOs) && SavedSnapshotUnchanged())
            return savedHardwareValid && checkedModule?.strName == RotationModule.ItemName;
        checkedItems = json.aItems; checkedConditions = json.aCOs;
        checkedParents.Clear(); checkedParentIds.Clear(); checkedOwners.Clear(); checkedEquations.Clear();
        savedHardwareValid = false;
        JsonItem module = null, console = null;
        foreach (var item in checkedItems) {
            if (item?.strID == HardwareId) module = item;
            if (item?.strID == ConsoleId) console = item;
        }
        if (module == null || console == null || module.strName != RotationModule.ItemName) return false;
        checkedModule = module;
        JsonItem child = module;
        for (int depth = 0; depth < 32 && child != null; depth++) {
            string parent = child.strParentID ?? child.strSlotParentID;
            checkedParents.Add(child); checkedParentIds.Add(parent);
            if (parent == ConsoleId) { savedHardwareValid = true; break; }
            child = null;
            if (parent == null) break;
            foreach (var item in checkedItems) if (item?.strID == parent) { child = item; break; }
        }
        if (!savedHardwareValid || checkedConditions == null) return savedHardwareValid;
        foreach (var co in checkedConditions) {
            if (co == null || co.strID != HardwareId && co.strID != ConsoleId) continue;
            checkedOwners.Add(co); checkedEquations.Add(co.aConds == null ? null : (string[])co.aConds.Clone());
            if (co.aConds == null) continue;
            foreach (string equation in co.aConds) {
                // CondOwner.GetJSON writes resolved counts as Name=1.0xCount.
                // Loot.ParseCondEquation is a randomized loot evaluator, not a
                // saved-state reader: do not consume gameplay RNG to inspect it.
                if (string.IsNullOrEmpty(equation)) continue;
                int equals = equation.IndexOf('=');
                if (equals < 0) continue;
                string name = equation.Substring(0, equals);
                if (name != "IsDamaged" && (co.strID != ConsoleId || name != "IsOff" && name != "IsDamagedSoftware")) continue;
                int count = equation.IndexOf('x', equals + 1);
                if (count >= 0 && Number(equation.Substring(count + 1), out double amount) && amount > 0) savedHardwareValid = false;
            }
        }
        return savedHardwareValid;
    }

    static bool Position(ShipSitu situation, out double x, out double y) {
        x = y = 0;
        if (situation == null) return false;
        if ((situation.bBOLocked || situation.bOrbitLocked || situation.bIsBO) && situation.strBOPORShip != null) {
            var parent = CrewSim.system.GetBO(situation.strBOPORShip);
            if (parent == null) return false;
            parent.UpdateTime(StarSystem.fEpoch);
            x = parent.dXReal + situation.vBOOffsetx;
            y = parent.dYReal + situation.vBOOffsety;
        } else { x = situation.vPosx; y = situation.vPosy; }
        return true;
    }

    internal bool Bearing(string kind, string id, out double bearing) {
        bearing = 0;
        if (string.IsNullOrEmpty(id) || CrewSim.system == null || ShipUs?.objSS == null) return false;
        double x, y;
        if (kind == "ship") {
            var target = CrewSim.system.GetShipByRegID(id);
            if (target?.objSS == null || target.bDestroyed || target.HideFromSystem || ReferenceEquals(target, ShipUs)) return false;
            if (!Position(target.objSS, out x, out y)) return false;
            x += SensorX; y += SensorY;
        } else if (kind == "body") {
            var body = CrewSim.system.GetBO(id);
            if (body == null) return false;
            body.UpdateTime(StarSystem.fEpoch);
            x = body.dXReal; y = body.dYReal;
        } else if (kind == "object") {
            if (!CrewSim.system.dictStellarObjects.TryGetValue(id, out var target) || target?.objSS == null) return false;
            if (!Position(target.objSS, out x, out y)) return false;
            x += SensorX; y += SensorY;
        } else return false;
        if (!Position(ShipUs.objSS, out double ownX, out double ownY)) return false;
        double dx = x - ownX, dy = y - ownY;
        if (!RotationPid.Finite(dx) || !RotationPid.Finite(dy) || dx * dx + dy * dy < 1e-30) return false;
        bearing = Math.Atan2(dy, dx) - Math.PI / 2;
        return true;
    }

    internal void Sample() {
        sampleEpoch = StarSystem.fEpoch;
        sampleBearing = Bearing(TargetKind, Order.TargetId, out double angle) ? angle : (double?)null;
    }

    internal RotationPose Advance(double duration, ShipSitu integration) {
        var situ = ShipUs.objSS;
        if (!ready || !Order.Active || situ.HasNavData() || situ.bBOLocked || situ.bIsBO ||
            integration.HasNavData() || integration.bBOLocked || integration.bIsBO || !HardwareReady()) return default;
        if (TargetKind == "ship" && Order.TargetId != null) {
            var docked = ShipUs.GetAllDockedShips();
            for (int i = 0; i < docked.Count; i++)
                if (docked[i]?.strRegID == Order.TargetId) { Order.Cancel(); return default; }
        }
        float originalRotation = situ.fRot, originalRate = situ.fW;
        float integrationRotation = integration.fRot, integrationRate = integration.fW;
        bool grouped = !ReferenceEquals(situ, integration);
        // Native StarSystem updates ships, then asteroids, then dock anchors.
        // Use the common pre-update sample rather than mixing those phases.
        if (!RotationPid.Finite(sampleEpoch) || Math.Abs(sampleEpoch - (StarSystem.fEpoch - duration)) > 0.0001) Sample();
        Order.ObserveBearing(sampleBearing, sampleEpoch);
        if (DataHandler.mapCOs != null && DataHandler.mapCOs.TryGetValue(ConsoleId, out var console) &&
            console?.mapGUIPropMaps != null && console.mapGUIPropMaps.TryGetValue("Panel A", out var map)) {
            if (map.TryGetValue("nKnobEngineMode", out var mode) && mode != "1") { Order.Cancel(); return default; }
        }
        int steps = duration <= 2 ? 1 : (int)Math.Min(512, Math.Max(1, Math.Ceiling(duration / 0.1)));
        double dt = duration <= 2 ? duration : 0.1;
        double elapsed = 0;
        bool manualTranslation = duration <= 2 && StarSystem.fEpoch - inputEpoch <= 2;
        RotationRuntime.Actuating = true;
        try {
            if (grouped) {
                situ.fRot = integration.fRot + situ._dockOffsetRot;
                situ.fW = integration.fW;
            }
            for (int step = 0; step < steps; step++) {
                dt = Math.Min(dt, duration - elapsed);
                double yaw = Order.Step(situ.fRot, situ.fW, dt, YawLimit, elapsed);
                ShipUs.Maneuver(manualTranslation ? translationX : 0, manualTranslation ? translationY : 0, (float)yaw, 0, (float)dt, Ship.EngineMode.RCS);
                if (situ.fW == 0) situ.fA = 0;
                float before = situ.fW;
                situ.fRot += (float)(situ.fW * dt + situ.fA * 0.5 * dt * dt);
                situ.fW += (float)(situ.fA * dt);
                if (before * situ.fW < 0) situ.fW = 0;
                situ.fA = 0;
                elapsed += dt;
                if (!Order.Active || Order.Quiet && Order.TargetRate == 0 && situ.fW == 0) break;
            }
            // Bounded bulk control work must not erase remaining inertial spin.
            // Native linear/world integration still advances the entire period.
            situ.fRot += (float)(situ.fW * (duration - elapsed));
            var pose = new RotationPose { Applied = true, Ship = ShipUs,
                Rotation = situ.fRot - (grouped ? situ._dockOffsetRot : 0), Rate = situ.fW };
            // Native TimeAdvance still integrates every linear/external force exactly
            // once. Its angular result is replaced by the bounded control integration.
            return pose;
        } finally {
            situ.fRot = originalRotation;
            situ.fW = originalRate;
            situ.fA = 0;
            if (grouped) {
                integration.fRot = integrationRotation;
                integration.fW = integrationRate;
                integration.fA = 0;
            }
            RotationRuntime.Actuating = false;
        }
    }
}

internal static class RotationRuntime {
    static readonly Dictionary<ShipSitu, OstrabusRotationPilot> Commands = new();
    static StarSystem boundSystem;
    internal static bool Actuating;
    internal static Ship DockInputShip;
    internal static bool HasCommands {
        get {
            if (!ReferenceEquals(boundSystem, CrewSim.system)) { Commands.Clear(); boundSystem = CrewSim.system; }
            return Commands.Count != 0;
        }
    }

    internal static void Attach(OstrabusRotationPilot pilot) {
        if (!ReferenceEquals(boundSystem, CrewSim.system)) { Commands.Clear(); boundSystem = CrewSim.system; }
        if (pilot.ShipUs?.objSS == null) return;
        if (pilot.TrackedSituation != null && !ReferenceEquals(pilot.TrackedSituation, pilot.ShipUs.objSS) &&
            Commands.TryGetValue(pilot.TrackedSituation, out var previous) && ReferenceEquals(previous, pilot)) Commands.Remove(pilot.TrackedSituation);
        pilot.TrackedSituation = pilot.ShipUs.objSS;
        Commands[pilot.TrackedSituation] = pilot;
    }
    internal static void SampleWorld() {
        if (!HasCommands) return;
        foreach (var pilot in Commands.Values) pilot.Sample();
    }
    internal static void Detach(OstrabusRotationPilot pilot) {
        if (pilot.TrackedSituation != null && Commands.TryGetValue(pilot.TrackedSituation, out var current) && ReferenceEquals(current, pilot)) Commands.Remove(pilot.TrackedSituation);
        pilot.TrackedSituation = null;
    }
    internal static OstrabusRotationPilot Find(Ship ship) {
        if (!HasCommands || ship?.objSS == null || !Commands.TryGetValue(ship.objSS, out var pilot)) return null;
        var owner = CurrentOwner(ship);
        if (!pilot.BelongsTo(owner)) { Detach(pilot); return null; }
        pilot.Owner = owner;
        return pilot;
    }
    // The native lookup allocates LINQ closures. This hot path also refuses
    // ambiguous duplicate controllers instead of choosing one to steer a hull.
    static AIShip CurrentOwner(Ship ship) {
        AIShip found = null;
        if (ship == null || AIShipManager.dictAIs == null) return null;
        foreach (var region in AIShipManager.dictAIs.Values) {
            if (region == null) continue;
            foreach (var candidate in region) {
                if (candidate?.Ship == null || !ReferenceEquals(candidate.Ship, ship) && candidate.Ship.strRegID != ship.strRegID) continue;
                if (found != null && !ReferenceEquals(found, candidate)) return null;
                found = candidate;
            }
        }
        return found;
    }
    internal static RotationPose Advance(ShipSitu situ, double duration) {
        if (Commands.TryGetValue(situ, out var pilot)) {
            // Native DockGroup advances its shared anchor after its members.
            // Spend fuel and integrate angular control only on that anchor step.
            if (pilot.ShipUs.objSS._dockGroup?.Anchor != null) return default;
        } else {
            foreach (var candidate in Commands.Values) {
                if (!ReferenceEquals(candidate.ShipUs?.objSS?._dockGroup?.Anchor, situ)) continue;
                if (pilot != null && !ReferenceEquals(pilot, candidate)) return default;
                pilot = candidate;
            }
        }
        if (pilot == null || Find(pilot.ShipUs) == null) return default;
        try { return pilot.Advance(duration, situ); }
        catch (Exception error) {
            pilot.Order.Cancel(); Detach(pilot);
            OstrabusPlugin.Logger?.LogError("[Rotation] Controller stopped after an actuation failure: " + error);
            return default;
        }
    }
    static OstrabusRotationPilot Create(Ship ship) {
        CancelGroup(ship);
        AIShipManager.UnregisterShip(ship);
        ship.objSS.ResetNavData();
        ship.ClearShipTarget();
        var ai = AIShipManager.AddAIToShip(ship, AIType.Auto, "INTERREGIONAL", new JsonAIShipSave {
            strATCLast = AIShipManager.strATCLast, strRegId = ship.strRegID,
            strHomeStation = string.IsNullOrEmpty(AIShipManager.strATCLast) ? ship.strRegID : AIShipManager.strATCLast, enumAIType = AIType.Auto
        });
        if (ai == null) return null;
        var pilot = new OstrabusRotationPilot(ai.AICharacter) { Owner = ai };
        ai.AddCommand(pilot);
        return pilot;
    }
    internal static void Start(Ship ship, CondOwner console, CondOwner hardware, RotationDirection direction, string kind, string id, double sx, double sy, bool hold) {
        var pilot = Create(ship);
        if (pilot == null) return;
        pilot.Begin(console, hardware, direction, kind, id, sx, sy, hold);
        Attach(pilot);
    }
    internal static OstrabusRotationPilot Resume(Ship ship, string[] saved, CondOwner console, CondOwner hardware) {
        if (!OstrabusRotationPilot.ValidPayload(saved)) return null;
        saved = (string[])saved.Clone();
        saved[6] = console.strID; saved[7] = hardware.strID;
        var pilot = Create(ship);
        if (pilot == null) return null;
        pilot.SaveData = saved;
        return pilot;
    }
    internal static void Cancel(Ship ship) {
        var pilot = Find(ship);
        if (pilot == null) return;
        pilot.Order.Cancel();
        Detach(pilot);
        AIShipManager.UnregisterShip(ship);
    }
    internal static void CancelGroup(Ship ship) {
        Cancel(ship);
        var members = ship.GetAllDockedShips();
        for (int i = 0; i < members.Count; i++) if (members[i] != null) Cancel(members[i]);
    }
    internal static void Withdrawn(AIShip owner) {
        if (owner?.Ship?.objSS != null && Commands.TryGetValue(owner.Ship.objSS, out var pilot) &&
            pilot.BelongsTo(owner) && !ReferenceEquals(CurrentOwner(owner.Ship), owner)) Detach(pilot);
    }
    internal static void Cleanup() { Commands.Clear(); boundSystem = null; Actuating = false; DockInputShip = null; }
}
