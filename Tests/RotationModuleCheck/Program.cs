using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ostrabus.Development;
using Ostrabus.NavConsole.Rotation;
using Ostranauts.Core;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using UnityEngine;

static partial class Program {
    static int checks;
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Set(object value, string field, object data) => AccessTools.Field(value.GetType(), field).SetValue(value, data);
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = ReferenceEquals(__0, __1); return false; }
    static bool Unequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = !ReferenceEquals(__0, __1); return false; }
    static bool Quiet() => false;
    static bool NoRotor(ref float __result) { __result = 0; return false; }
    static bool TimeScale(ref float __result) { __result = 1; return false; }
    static bool NoCargo(ref double __result) { __result = 0; return false; }
    sealed class Pilot : BaseCharacter {
        public Pilot(Ship ship) { ShipUs = ship; }
        public override AIType AIType => AIType.Auto;
        public override TargetData GetTarget() => null;
        public override double MaxSpeed(bool? inAtmo) => 0;
        public override ICommand FFWD(ICommand command, Blackboard board) => null;
    }

    static Ship Hull(string id) {
        var ship = Empty<Ship>(); ship.strRegID = id;
        Set(ship, "<objSS>k__BackingField", new ShipSitu(false));
        Set(ship, "nLoadState", Ship.Loaded.Shallow);
        Set(ship, "fShallowMass", 10000d);
        ship.fShallowRCSRemass = 10000;
        Set(ship, "fRCSCount", 8f); Set(ship, "nRCSDistroCount", 4);
        Set(ship, "nRCSCountRaw", 8); Set(ship, "fRCSExhaustV", 8f);
        Set(ship, "fFuelEfficiencyMod", 1d);
        Set(ship, "aDocked", new Dictionary<string, Ship>());
        var waypoints = AccessTools.Field(typeof(Ship), "aWPs");
        waypoints.SetValue(ship, Activator.CreateInstance(waypoints.FieldType));
        Set(ship, "<ShipCO>k__BackingField", Empty<CondOwner>());
        ship.json = new JsonShip {
            aItems = new[] { new JsonItem { strID = id + "-nav", strName = "ItmStationNav" },
                new JsonItem { strID = id + "-module", strName = RotationModule.ItemName, strParentID = id + "-nav" } },
            aCOs = Array.Empty<JsonCondOwnerSave>()
        };
        CrewSim.system.dictShips[id] = ship;
        return ship;
    }

    static OstrabusRotationPilot Order(Ship ship, double heading, double offset = 0, bool hold = false, string target = "") {
        var pilot = new Pilot(ship);
        var ai = Empty<AIShip>(); Set(ai, "_shipUs", ship); Set(ai, "_aiCharacter", pilot);
        AIShipManager.dictAIs["fixture"].Add(ai);
        var command = new OstrabusRotationPilot(pilot) { Owner = ai };
        command.SaveData = new[] { "ostrabus.rotation.v1", heading.ToString("R", CultureInfo.InvariantCulture),
            offset.ToString("R", CultureInfo.InvariantCulture), hold ? "1" : "0", target.Length > 0 ? "ship" : "", target,
            ship.strRegID + "-nav", ship.strRegID + "-module", "0", "0" };
        return command;
    }

    static void Simulate(Ship ship, OstrabusRotationPilot command, double dt, double seconds) {
        for (double t = 0; t < seconds; t += dt) {
            StarSystem.fEpoch += dt;
            ship.objSS.TimeAdvance(dt);
            Check(RotationPid.Finite(ship.objSS.fRot) && RotationPid.Finite(ship.objSS.fW), "Nonfinite physical rotation");
            Check(ship.objSS.fA == 0, "Angular force escaped its owned integration step");
            Check(ship.fShallowRCSRemass >= 0, "Negative RCS fuel");
        }
    }

    static void Directions() {
        foreach (RotationDirection direction in Enum.GetValues(typeof(RotationDirection))) {
            var order = new RotationOrder();
            order.Command(direction, 0.4, null, null);
            double expected = direction == RotationDirection.Left ? 0.4 + Math.PI / 2 : direction == RotationDirection.Right ? 0.4 - Math.PI / 2 : 0.4 + Math.PI;
            Check(Math.Abs(RotationPid.Wrap(order.Heading - expected)) < 1e-12, "No-target direction semantics");
            order.Command(direction, 0.4, "original", 1.2);
            expected = 1.2 + (direction == RotationDirection.Left ? Math.PI / 2 : direction == RotationDirection.Right ? -Math.PI / 2 : direction == RotationDirection.Backward ? Math.PI : 0);
            Check(Math.Abs(RotationPid.Wrap(order.Heading - expected)) < 1e-12, "Target direction semantics");
            order.SetHold(true);
            order.ObserveBearing(1.3, 1);
            order.Step(0, 0, 0.02, 0.1);
            Check(order.TargetId == "original" && order.Hold, "Hold changed original target identity");
            order.Cancel();
            Check(!order.Active && !order.Hold && !order.HasLastCommand, "Manual cancellation retained state");
        }
        Check(Math.Abs(RotationPid.Wrap(359 * Math.PI / 180 - Math.PI / 180) + 2 * Math.PI / 180) < 1e-12, "Wrap boundary chose long turn");
        var pid = new RotationPid();
        Check(pid.Step(double.NaN, 0, 0, 1, 1) == 0 && pid.Step(1, 0, 0, 0, 1) == 0, "Invalid PID input produced torque");
    }

    static void Assets() {
        string root = "NavConsole/Rotation/Assets";
        var overlays = new Dictionary<string, JsonCOOverlay>();
        DataHandler.JsonToData(root + "/data/cooverlays/rotation.json", overlays);
        var maps = new Dictionary<string, JsonGUIPropMap>();
        DataHandler.JsonToData(root + "/data/guipropmaps/rotation.json", maps);
        var loots = new Dictionary<string, JsonLoot>();
        DataHandler.JsonToData(root + "/data/loot/rotation.json", loots);
        Check(overlays.Count == 2 && maps.Count == 1 && loots.Count == 4, "Native asset loader missed definitions");
        var raw = JArray.Parse(File.ReadAllText(root + "/data/cooverlays/rotation.json"));
        Check((string)raw[0]["strCOBase"] == "ItmNavModMoboScreen", "Wrong physical 1x1 base");
        Check(raw[0]["mapModeSwitches"].Values<string>().Contains(RotationModule.ItemName + "Dmg"), "Damage conversion loses module identity");
        var map = maps.Values.Single().dictGUIPropMap;
        string position = map[Array.IndexOf(map, "strDefaultPos") + 1];
        var p = position.Split('|').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
        Check(Math.Abs(p[2] - p[0] - 0.1) < 1e-9 && Math.Abs(p[3] - p[1] - 0.2) < 1e-9, "Module footprint is not native 1x1");
        var native = JArray.Parse(File.ReadAllText(InstallationPaths.Data + "loot/loot.json")).OfType<JObject>().GroupBy(x => (string)x["strName"]).ToDictionary(x => x.Key, x => x.Last());
        var stock = JArray.Parse(File.ReadAllText(root + "/data/loot/rotation_stock.json")).OfType<JObject>().ToArray();
        foreach (var entry in stock) {
            var copy = (JObject)entry.DeepClone();
            var field = (string)copy["strName"] == "ItmNavStationModsAll" ? "aCOs" : "aLoots";
            var list = (JArray)copy[field];
            Check(list.Last.Value<string>().StartsWith(RotationModule.ItemName + "="), "Stock addition missing");
            list.Last.Remove();
            Check(JToken.DeepEquals(copy, native[(string)copy["strName"]]), "Stock override changed existing inventory");
        }
        Check(stock.Length == 20, "Vendor/editor coverage changed; rebase stock assets");
    }

    static int Main() {
        Ostrabus.OstrabusPlugin.Logger = new BepInEx.Logging.ManualLogSource("RotationCheck");
        Ostrabus.OstrabusPlugin.Logger.LogEvent += (_, entry) => {
            if ((entry.Level & (BepInEx.Logging.LogLevel.Error | BepInEx.Logging.LogLevel.Warning)) != 0)
                throw new Exception("Production rotation diagnostic: " + entry.Data);
        };
        var fixture = new Harmony("ostrabus.rotation.fixture");
        var production = new Harmony("ostrabus.rotation.production");
        try {
            fixture.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Equality"), prefix: new HarmonyMethod(typeof(Program), nameof(Equal)));
            fixture.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Inequality"), prefix: new HarmonyMethod(typeof(Program), nameof(Unequal)));
            fixture.Patch(AccessTools.PropertyGetter(typeof(Ship), "CurrentRotorEfficiency"), prefix: new HarmonyMethod(typeof(Program), nameof(NoRotor)));
            fixture.Patch(AccessTools.PropertyGetter(typeof(Time), "timeScale"), prefix: new HarmonyMethod(typeof(Program), nameof(TimeScale)));
            fixture.Patch(AccessTools.Method(typeof(Ostranauts.Trading.MarketManager), "GetCargoMassForShip"), prefix: new HarmonyMethod(typeof(Program), nameof(NoCargo)));
            fixture.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.UpdateDockedShips)), prefix: new HarmonyMethod(typeof(Program), nameof(Quiet)));
            fixture.Patch(AccessTools.Method(typeof(GUIOrbitDraw), nameof(GUIOrbitDraw.TriggerShipRedraw)), prefix: new HarmonyMethod(typeof(Program), nameof(Quiet)));
            fixture.Patch(AccessTools.Method(typeof(CondOwner), nameof(CondOwner.SetCondAmount)), prefix: new HarmonyMethod(typeof(Program), nameof(Quiet)));
            CrewSim.system = Empty<StarSystem>(); CrewSim.system.dictShips = new Dictionary<string, Ship>();
            DataHandler.mapCOs = new Dictionary<string, CondOwner>();
            AIShipManager.dictAIs = new Dictionary<string, List<AIShip>> { ["fixture"] = new List<AIShip>() };
            production.CreateClassProcessor(typeof(RotationPhysicsPatch)).Patch();
            production.CreateClassProcessor(typeof(RotationManualInputPatch)).Patch();
            Directions(); Assets(); UiInput(fixture);
            NativeControls(fixture, production);
            Boundaries(fixture);
            GroupChecks(fixture);
            DockingInput(fixture, production);
            RememberedCommand();
            MovingTargets();
            TrackingFrames(fixture);
            HoldRelease();
            HoldReleaseAudio(fixture);
            foreach (double dt in new[] { 0.01, 0.02, 0.1, 0.5, 1.0, 2.0, 60.0, 3600.0 })
            foreach (double start in new[] { -3.1, 0.0, 3.1 }) {
                RotationRuntime.Cleanup();
                var ship = Hull("turn-" + dt + "-" + start); ship.objSS.fRot = (float)start;
                var command = Order(ship, start + Math.PI);
                double fuel = ship.fShallowRCSRemass;
                Simulate(ship, command, dt, dt >= 60 ? dt * 2 : 150);
                double error = Math.Abs(RotationPid.Wrap(command.Order.Heading - ship.objSS.fRot));
                Check(error < 0.02 && Math.Abs(ship.objSS.fW) < 0.003, "Turn failed dt=" + dt + " start=" + start + " error=" + error + " rate=" + ship.objSS.fW);
                Check(ship.fShallowRCSRemass < fuel, "Turn consumed no native RCS fuel");
                Check(ship.objSS.vPosx == 0 && ship.objSS.vVelX == 0 && ship.objSS.vAccIn.x == 0, "Rotation changed linear motion");
                Check(!command.Order.Active, "One-shot never completed dt=" + dt);
            }
            Console.WriteLine(checks + " rotation direction, native asset and RCS/physics checks passed.");
            return 0;
        } finally { RotationRuntime.Cleanup(); production.UnpatchSelf(); fixture.UnpatchSelf(); }
    }
}

namespace Ostrabus {
    internal static class OstrabusPlugin { internal static BepInEx.Logging.ManualLogSource Logger = null; }
}
