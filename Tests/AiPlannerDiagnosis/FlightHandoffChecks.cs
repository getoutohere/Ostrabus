using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using Ostrabus.ShipNavigation;
using Ostranauts.Core;
using Ostranauts.Ships.Commands;
using Ostranauts.ShipGUIs.Utilities;

// Execute native route exits before/after the production control repair.
// Fuel-policy differences remain observations, not a request for stricter fuel.
static class FlightHandoffChecks {
    static int checks;
    static bool collision, reactorGui;
    static NavData proposed;
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Set(object target, string field, object value) => AccessTools.Field(target.GetType(), field).SetValue(target, value);
    static void Check(bool ok, string reason) { checks++; if (!ok) throw new Exception(reason); }
    static bool Quiet() => false;
    static bool No(ref bool __result) { __result = false; return false; }
    static bool Collision(ref bool __result) { __result = collision; return false; }
    static bool Gpm(ref string __result) { __result = reactorGui ? "0.4" : ""; return false; }
    static bool Clearance(ref float __result) { __result = 1e-12f; return false; }
    static bool Yes(ref bool __result) { __result = true; return false; }
    static bool Plan(ref NavData __result) { __result = proposed; return false; }
    static bool Sliders(ref FlyToPath.SliderData __result) { __result = new FlyToPath.SliderData(null); return false; }
    static bool Point(FlyToPath __instance, ShipSitu __0, double __1, ref NavDataPoint __result) {
        __result = new NavDataPoint(__1, new ShipSitu(__0, false), __instance.ShipUs.fShallowRCSRemass, 0, 5000); return false;
    }
    static bool Zero(UnityEngine.Vector2 value) => value.x == 0 && value.y == 0;
    static bool Same(UnityEngine.Vector2 left, UnityEngine.Vector2 right) => left.x == right.x && left.y == right.y;
    sealed class ManualBoundary : BaseCommand {
        internal int Calls;
        internal UnityEngine.Vector2 InputOnEntry;
        internal ManualBoundary(Program.Pilot pilot) : base(pilot) { }
        public override CommandStatus RunCommand(Blackboard board) {
            Calls++; InputOnEntry = ShipUs.objSS.vAccIn;
            ShipUs.objSS.vAccRCS = new UnityEngine.Vector2(3e-11f, -4e-11f);
            return Running;
        }
    }
    static Ship Hull(Ship.Loaded load = Ship.Loaded.Shallow) {
        var ship = Empty<Ship>(); ship.strRegID = "healthy-handoff-fixture"; ship.IsAIShip = true;
        Set(ship, "<objSS>k__BackingField", new ShipSitu(false)); Set(ship, "nLoadState", load);
        Set(ship, "aDocked", new Dictionary<string, Ship>());
        var points = AccessTools.Field(typeof(Ship), "aWPs"); points.SetValue(ship, Activator.CreateInstance(points.FieldType));
        Set(ship, "aNavs", new List<CondOwner> { Empty<CondOwner>() });
        ship.fShallowRCSRemass = 500; ship.objSS.vVelX = 4e-8; ship.objSS.vVelY = -3e-8;
        ship.objSS.vAccIn = new UnityEngine.Vector2(-2.77775e-11f, -1.232682e-10f);
        ship.objSS.vAccRCS = new UnityEngine.Vector2(1e-11f, 2e-11f);
        Set(ship, "bTorchDriveThrusting", true);
        CrewSim.system.dictShips[ship.strRegID] = ship;
        return ship;
    }
    internal static void Run() {
        var h = new Harmony("offline.flight-handoff.boundaries");
        var production = new Harmony("offline.flight-handoff.production");
        var audioField = AccessTools.Field(typeof(AudioManager), "am"); var previousAudio = audioField.GetValue(null);
        void Prefix(Type type, string method, string handler) => h.Patch(AccessTools.Method(type, method), prefix: new HarmonyMethod(typeof(FlightHandoffChecks), handler));
        try {
            CrewSim.system = Empty<StarSystem>(); CrewSim.system.dictShips = new Dictionary<string, Ship>(); StarSystem.fEpoch = 1000;
            audioField.SetValue(null, Empty<AudioManager>());
            foreach (var method in AccessTools.GetDeclaredMethods(typeof(AudioManager)))
                if (method.Name == "PlayAudioEmitter" || method.Name == "TweakAudioEmitter") h.Patch(method, prefix: new HarmonyMethod(typeof(FlightHandoffChecks), nameof(Quiet)));
            Prefix(typeof(ShipSitu), "UpdateTime", nameof(Quiet));
            Prefix(typeof(Ship), nameof(Ship.IsDocked), nameof(No));
            Prefix(typeof(AIShipManager), nameof(AIShipManager.IsOnCollisionCourse), nameof(Collision));
            Prefix(typeof(Ship), nameof(Ship.GetReactorGPMValue), nameof(Gpm));
            Prefix(typeof(Ship), nameof(Ship.SetReactorGPMValue), nameof(Quiet));
            Prefix(typeof(Ship), nameof(Ship.UpdateDockedShips), nameof(Quiet));
            Prefix(typeof(Ship), nameof(Ship.Maneuver), nameof(Quiet));
            Prefix(typeof(GUIOrbitDraw), nameof(GUIOrbitDraw.TriggerShipRedraw), nameof(Quiet));
            h.Patch(AccessTools.Method(typeof(CollisionManager), nameof(CollisionManager.GetCollisionDistanceAU), new[] { typeof(ShipSitu), typeof(ShipSitu) }),
                prefix: new HarmonyMethod(typeof(FlightHandoffChecks), nameof(Clearance)));
            foreach (bool anchored in new[] { false, true }) {
                collision = anchored; reactorGui = false;
                var ship = Hull(); var pilot = new Program.Pilot { ShipUs = ship, Blackboard = new Blackboard() };
                ship.shipSituTarget = new ShipSitu(false) { vPosx = .0001, bBOLocked = anchored };
                ship.objSS.NavData = new NavData(ship);
                var flight = new FlyTo(pilot); var manual = new ManualBoundary(pilot); Set(flight, "_flyToManual", manual);
                double vx = ship.objSS.vVelX, vy = ship.objSS.vVelY; var input = ship.objSS.vAccIn;
                Check(flight.RunCommand(pilot.Blackboard).Status == CommandCode.Running && manual.Calls == 1, "Native manual handoff did not run");
                Check(ship.objSS.NavData == null && Same(ship.objSS.vAccIn, input),
                    "Vanilla did not reproduce discarded route with residual torch acceleration");
                Check(ship.objSS.vVelX == vx && ship.objSS.vVelY == vy, "Handoff fixture altered momentum");
                NavigationRoute.Stop(ship);
                Check(Zero(ship.objSS.vAccIn) && Zero(ship.objSS.vAccRCS), "Existing explicit stop did not release route forces");
                Check(ship.objSS.vVelX == vx && ship.objSS.vVelY == vy && ship.fShallowRCSRemass == 500,
                    "Existing explicit stop changed momentum or fuel");
            }
            foreach (var load in new[] { Ship.Loaded.Shallow, Ship.Loaded.Full }) {
                var ship = Hull(load); var pilot = new Program.Pilot { ShipUs = ship };
                reactorGui = load == Ship.Loaded.Full;
                AccessTools.Method(typeof(FlyToPath), "TorchOff").Invoke(new FlyToPath(pilot), null);
                Check(!Zero(ship.objSS.vAccIn) == (load == Ship.Loaded.Shallow),
                    "Native TorchOff did not reproduce GUI-dependent shallow shutdown gap");
            }
            // A still-owned planned torch course must remain untouched while the
            // native path leaf runs. This investigation is not a speed clamp.
            var ongoing = Hull(); collision = false; reactorGui = false;
            ongoing.shipSituTarget = new ShipSitu(false) { bBOLocked = true, vPosx = .001 };
            var course = new NavData(ongoing); ongoing.objSS.NavData = course;
            course.AddNavDataPoint(new NavDataPoint(StarSystem.fEpoch, new ShipSitu(false), 500, 1, 5000));
            course.AddNavDataPoint(new NavDataPoint(StarSystem.fEpoch + 200, new ShipSitu(false) { vPosx = .001 }, 500, 1, 4800));
            var p = new Program.Pilot { ShipUs = ongoing, Blackboard = new Blackboard() };
            var before = ongoing.objSS.vAccIn;
            Check(new FlyTo(p).RunCommand(p.Blackboard).Status == CommandCode.Running &&
                ReferenceEquals(ongoing.objSS.NavData, course) && Same(ongoing.objSS.vAccIn, before),
                "Control course unexpectedly modified");

            // Native escape keeps an obsolete route. The repaired setter now
            // releases that route's forces while preserving actual momentum.
            var escaping = Hull(); var escapePilot = new Program.Pilot { ShipUs = escaping, Blackboard = new Blackboard() };
            var escapeHome = Empty<Ship>(); escapeHome.strRegID = "escape-home";
            Set(escapeHome, "<objSS>k__BackingField", new ShipSitu(false) { bIsBO = true, vPosx = .001 });
            var escape = new TargetEscapeStation(escapePilot); var setter = AccessTools.Method(typeof(TargetEscapeStation), "SetTarget");
            var oldCourse = new NavData(escaping); escaping.objSS.NavData = oldCourse; var oldForce = escaping.objSS.vAccIn;
            setter.Invoke(escape, new object[] { escapeHome });
            Check(ReferenceEquals(escaping.objSS.NavData, oldCourse), "Native escape setter no longer reproduces stale route");
            h.CreateClassProcessor(typeof(EscapeTargetPatch)).Patch();
            escaping.TargetData = null; escaping.shipScanTarget = null; escaping.shipSituTarget = null;
            setter.Invoke(escape, new object[] { escapeHome });
            Check(escaping.objSS.NavData == null && Zero(escaping.objSS.vAccIn) && Zero(escaping.objSS.vAccRCS),
                "Escape repair retained abandoned course forces");
            escaping.objSS.NavData = oldCourse; escaping.objSS.vAccIn = oldForce;
            setter.Invoke(escape, new object[] { escapeHome });
            Check(ReferenceEquals(escaping.objSS.NavData, oldCourse) && Same(escaping.objSS.vAccIn, oldForce),
                "Reapplying the same escape destination canceled an owned course");

            production.CreateClassProcessor(typeof(FlightControlHandoffPatch)).Patch();
            production.CreateClassProcessor(typeof(ShallowTorchShutdownPatch)).Patch();
            production.CreateClassProcessor(typeof(ShallowRcsTorchReleasePatch)).Patch();
            foreach (var load in new[] { Ship.Loaded.Shallow, Ship.Loaded.Edit, Ship.Loaded.Full })
            foreach (bool ai in new[] { false, true })
            foreach (bool anchored in new[] { false, true })
            foreach (bool route in new[] { false, true }) {
                var ship = Hull(load); ship.IsAIShip = ai;
                ship.objSS.fW = .25f; var force = ship.objSS.vAccIn;
                double vx = ship.objSS.vVelX, vy = ship.objSS.vVelY;
                ship.objSS.NavData = route ? new NavData(ship) : null;
                ship.shipSituTarget = new ShipSitu(false) { bBOLocked = anchored, vPosx = .001 };
                collision = anchored;
                var pilot = new Program.Pilot { ShipUs = ship, Blackboard = new Blackboard() };
                var flight = new FlyTo(pilot); var manual = new ManualBoundary(pilot); Set(flight, "_flyToManual", manual);
                flight.RunCommand(pilot.Blackboard);
                bool affected = ai && load == Ship.Loaded.Shallow;
                Check(manual.Calls == 1 && ship.objSS.NavData == null, "Handoff changed native dispatch or route removal");
                Check(affected ? Zero(manual.InputOnEntry) : Same(manual.InputOnEntry, force),
                    "RCS leaf received stale torch force, or physical/player controls changed");
                Check(Same(ship.objSS.vAccRCS, new UnityEngine.Vector2(3e-11f, -4e-11f)), "New RCS command erased");
                Check(ship.objSS.vVelX == vx && ship.objSS.vVelY == vy && ship.objSS.fW == .25f && ship.fShallowRCSRemass == 500,
                    "Control release changed momentum, spin or fuel");
                if (affected) {
                    var rcs = ship.objSS.vAccRCS;
                    ship.objSS.TimeAdvance(10);
                    Check(Math.Abs(ship.objSS.vVelX - vx - (double)rcs.x * 10) < 1e-20 &&
                        Math.Abs(ship.objSS.vVelY - vy - (double)rcs.y * 10) < 1e-20,
                        "Native integration still applied phantom torch force, or lost fresh RCS force");
                }
            }
            foreach (bool ai in new[] { false, true }) {
                var paused = Hull(); paused.IsAIShip = ai; paused.fAIPauseTimer = StarSystem.fEpoch + 100;
                paused.objSS.NavData = new NavData(paused); var beforePause = paused.objSS.vAccIn;
                var pilot = new Program.Pilot { ShipUs = paused, Blackboard = new Blackboard() };
                Check(new FlyTo(pilot).RunCommand(pilot.Blackboard).Status == CommandCode.Running && paused.objSS.NavData == null,
                    "Paused FlyTo no longer retains native Running behavior");
                Check(ai ? Zero(paused.objSS.vAccIn) : Same(beforePause, paused.objSS.vAccIn), "Pause control-release scope incorrect");
            }
            foreach (var load in new[] { Ship.Loaded.None, Ship.Loaded.Shallow, Ship.Loaded.Edit, Ship.Loaded.Full })
            foreach (bool ai in new[] { false, true })
            foreach (bool gui in new[] { false, true }) {
                var ship = Hull(load); ship.IsAIShip = ai; reactorGui = gui;
                AccessTools.Method(typeof(FlyToPath), "TorchOff").Invoke(new FlyToPath(new Program.Pilot { ShipUs = ship }), null);
                bool stopped = ai && load == Ship.Loaded.Shallow || gui && load >= Ship.Loaded.Edit;
                Check(Zero(ship.objSS.vAccIn) == stopped, "Explicit shutdown changed player/physical/native behavior or failed shallow AI");
            }
            var disabled = Hull(); Set(disabled, "aNavs", new List<CondOwner>()); Set(disabled, "fRCSCount", 0f);
            reactorGui = false;
            AccessTools.Method(typeof(FlyToPath), "TorchOff").Invoke(new FlyToPath(new Program.Pilot { ShipUs = disabled }), null);
            Check(Zero(disabled.objSS.vAccIn) && disabled.NavCount == 0 && disabled.RCSCount == 0,
                "Releasing phantom control restored missing navigation/propulsion equipment");
            // Direct combat/undock/tow RCS steering need not pass through FlyTo.
            // The physical RCS leaf is a boundary; the production actuator
            // prefix and native SetThrust execute, including active-course guards.
            foreach (var load in new[] { Ship.Loaded.None, Ship.Loaded.Shallow, Ship.Loaded.Edit, Ship.Loaded.Full })
            foreach (bool ai in new[] { false, true })
            foreach (bool ownedCourse in new[] { false, true })
            foreach (float seconds in new[] { 0f, .125f }) {
                var ship = Hull(load); ship.IsAIShip = ai;
                var force = ship.objSS.vAccIn; double vx = ship.objSS.vVelX, vy = ship.objSS.vVelY;
                var ownerCourse = ownedCourse ? new NavData(ship) : null; ship.objSS.NavData = ownerCourse;
                ship.Maneuver(1, 0, 0, 0, seconds);
                bool stopped = load == Ship.Loaded.Shallow && ai && !ownedCourse && seconds > 0;
                Check(stopped ? Zero(ship.objSS.vAccIn) : Same(ship.objSS.vAccIn, force), "Direct RCS force-release scope failed");
                Check(ReferenceEquals(ship.objSS.NavData, ownerCourse) && ship.objSS.vVelX == vx && ship.objSS.vVelY == vy && ship.fShallowRCSRemass == 500,
                    "Direct RCS control release changed course ownership, momentum or fuel");
            }
            foreach (var load in new[] { Ship.Loaded.Shallow, Ship.Loaded.Full })
            foreach (bool ai in new[] { false, true }) {
                var ship = Hull(load); ship.IsAIShip = ai; collision = false;
                ship.shipSituTarget = new ShipSitu(false) { bBOLocked = true, vPosx = .001 };
                var liveCourse = new NavData(ship) { IsTorching = true }; ship.objSS.NavData = liveCourse;
                liveCourse.AddNavDataPoint(new NavDataPoint(StarSystem.fEpoch, new ShipSitu(false), 500, 1, 5000));
                liveCourse.AddNavDataPoint(new NavDataPoint(StarSystem.fEpoch + 200, new ShipSitu(false) { vPosx = .001 }, 500, 1, 4800));
                var force = ship.objSS.vAccIn;
                var pilot = new Program.Pilot { ShipUs = ship, Blackboard = new Blackboard() };
                Check(new FlyTo(pilot).RunCommand(pilot.Blackboard).Status == CommandCode.Running &&
                    ReferenceEquals(ship.objSS.NavData, liveCourse) && Same(ship.objSS.vAccIn, force), "Active owned torch course canceled");
            }
            bool rejected = false;
            try { FlightControlHandoffPatch.Transpiler(Array.Empty<CodeInstruction>()); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Unknown native handoff IL accepted");

            // The planner is a boundary returning the SAME geometric candidate
            // to two real consumers. Native selection rejects empty/negative
            // arrival fuel, whereas execution commits it without that test.
            Prefix(typeof(FlyToPath), nameof(FlyToPath.PlanTrip4), nameof(Plan));
            Prefix(typeof(FlyToPath), nameof(FlyToPath.GetNavSliderValues), nameof(Sliders));
            Prefix(typeof(FlyToPath), nameof(FlyToPath.CreateNavPointStatic), nameof(Point));
            h.Patch(AccessTools.PropertyGetter(typeof(Ship), nameof(Ship.NavAIManned)), prefix: new HarmonyMethod(typeof(FlightHandoffChecks), nameof(Yes)));
            foreach (double arrivalFuel in new[] { -25d, 0d, 25d }) {
                var ship = Hull(); ship.objSS.vAccIn = default; ship.objSS.vAccRCS = default;
                ship.fShallowRCSRemass = 100;
                ship.shipSituTarget = new ShipSitu(false) { bBOLocked = true, vPosx = .001 };
                var pilot = new Program.Pilot { ShipUs = ship, Blackboard = new Blackboard() };
                proposed = new NavData(ship);
                proposed.AddNavDataPoint(new NavDataPoint(StarSystem.fEpoch, new ShipSitu(false), 100, 0, 5000));
                proposed.AddNavDataPoint(new NavDataPoint(StarSystem.fEpoch + 200, new ShipSitu(false) { vPosx = .001 }, arrivalFuel, 0, 5000));
                var selection = new TargetNewIfNone(pilot);
                var accepted = AccessTools.Method(typeof(TargetNewIfNone), "GenerateNavData").Invoke(selection, new object[] { new Ostranauts.Ships.TargetData(ship.shipSituTarget) });
                Check((accepted != null) == (arrivalFuel > 0), "Native selection fuel gate differs from source");
                Check(new FlyToPath(pilot).RunCommand(pilot.Blackboard).Status == CommandCode.Running && ReferenceEquals(ship.objSS.NavData, proposed),
                    "Native executor did not reproduce fuel-admission bypass");
            }
            var emptyFuel = Hull(); emptyFuel.fShallowRCSRemass = 0;
            emptyFuel.shipSituTarget = new ShipSitu(false) { bBOLocked = true, vPosx = .001 };
            proposed = new NavData(emptyFuel);
            proposed.AddNavDataPoint(new NavDataPoint(StarSystem.fEpoch, new ShipSitu(false), 0, 0, 0));
            proposed.AddNavDataPoint(new NavDataPoint(StarSystem.fEpoch + 200, new ShipSitu(false) { vPosx = .001 }, -25, 0, 0));
            var emptyPilot = new Program.Pilot { ShipUs = emptyFuel, Blackboard = new Blackboard() };
            Check(new FlyToPath(emptyPilot).RunCommand(emptyPilot.Blackboard).Status == CommandCode.Running &&
                ReferenceEquals(emptyFuel.objSS.NavData, proposed) && emptyFuel.fShallowRCSRemass == 0,
                "Control repair blocked deliberate empty-tank shallow navigation or granted fuel");
            Console.WriteLine("Flight handoff: " + checks + " native reproduction/production repair checks passed; shallow fuel-tolerant execution preserved.");
        } finally { production.UnpatchSelf(); h.UnpatchSelf(); audioField.SetValue(null, previousAudio); CrewSim.system = null; }
    }
}
