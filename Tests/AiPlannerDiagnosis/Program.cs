using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Runtime.Serialization;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ostranauts.Core;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots.Interfaces;
using Ostranauts.Ships.Commands;
using Ostranauts.ShipGUIs.Utilities;

static class Program {
    static int checks;
    static BodyOrbit reference;
    static double startEpoch, targetX, targetY, targetVx, targetVy;
    static readonly List<object> iterations = new List<object>();
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Check(bool ok, string reason) { checks++; if (!ok) throw new Exception(reason); }
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = ReferenceEquals(__0, __1); return false; }
    static bool Unequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = !ReferenceEquals(__0, __1); return false; }
    static bool Acceleration(ref double __result) { __result = 1.9016982118751132E-10; return false; }
    static bool Fuel(ref double __result) { __result = 0; return false; }
    static bool Gravity(ref BodyOrbit boClosest, ref BodyOrbit __result) { boClosest = reference; __result = reference; return false; }
    static bool NoDetour(ref ShipSitu __result) { __result = null; return false; }
    static bool Collision(ref float __result) { __result = 0; return false; }
    static bool Body(string strName, ref BodyOrbit __result) { __result = strName == "missing-body" ? null : reference; return false; }
    static bool NoCargo(ref double __result) { __result = 0; return false; }
    static bool BodyTime(BodyOrbit __instance, double dTime) {
        __instance.dXReal = targetX + targetVx * (dTime - startEpoch);
        __instance.dYReal = targetY + targetVy * (dTime - startEpoch);
        __instance.dVelX = targetVx; __instance.dVelY = targetVy;
        return false;
    }
    static void Planned(NavData __result, double fDurationEst) => iterations.Add(new {
        estimate = fDurationEst, duration = __result.Destination.ArrivalTime - __result.Origin.ArrivalTime,
        destinationX = __result.Destination.ObjSS.vPosx, destinationY = __result.Destination.ObjSS.vPosy
    });
    static double Number(JToken token) => token.Value<double>();
    static NavDataPoint Point(JToken record, double time, bool target = false, bool repaired = false) {
        var pos = record["positionAU"]; var vel = record["velocityAUPerSecond"];
        var situ = new ShipSitu(false) { vPosx = Number(pos["x"]), vPosy = Number(pos["y"]),
            vVelX = Number(vel["x"]), vVelY = Number(vel["y"]), bIsBO = target,
            strBOPORShip = repaired ? "linear-station" : null };
        return new NavDataPoint(time, situ, 1000, 0, 0);
    }
    static NavData Replay(JObject data, bool repaired) {
        var originRecord = data["navigation"]["currentNavPlan"]["points"][0];
        var targetRecord = data["navigation"]["situationTarget"];
        startEpoch = Number(originRecord["arrivalEpoch"]); StarSystem.fEpoch = startEpoch;
        targetX = Number(targetRecord["positionAU"]["x"]); targetY = Number(targetRecord["positionAU"]["y"]);
        targetVx = Number(targetRecord["velocityAUPerSecond"]["x"]); targetVy = Number(targetRecord["velocityAUPerSecond"]["y"]);
        reference.dXReal = targetX; reference.dYReal = targetY; reference.dVelX = targetVx; reference.dVelY = targetVy;
        var ship = Empty<Ship>();
        var pilot = new Pilot { ShipUs = ship, Speed = double.Parse((string)data["commands"]["chain"][0]["saveData"][0], CultureInfo.InvariantCulture) };
        var planner = new FlyToPath(pilot);
        iterations.Clear();
        return planner.PlanTrip4(Point(originRecord, startEpoch), Point(targetRecord, startEpoch, true, repaired), 1, 1);
    }
    internal static NavData PlanCurrent(Ship ship, double epoch, double speed) {
        startEpoch = epoch; StarSystem.fEpoch = epoch;
        ShipSitu target = ship.shipSituTarget;
        targetX = target.vPosx; targetY = target.vPosy; targetVx = target.vVelX; targetVy = target.vVelY;
        reference.dXReal = targetX; reference.dYReal = targetY; reference.dVelX = targetVx; reference.dVelY = targetVy;
        var planner = new FlyToPath(new Pilot { ShipUs = ship, Speed = speed });
        return planner.PlanTrip4(new NavDataPoint(epoch, new ShipSitu(ship.objSS, false), ship.fShallowRCSRemass, 0, 0),
            new NavDataPoint(epoch, new ShipSitu(target, false), 0, 0, 0), 1, 1);
    }
    static int AuditSaves() {
        string root = Environment.GetEnvironmentVariable("USERPROFILE") + "/AppData/LocalLow/Blue Bottle Games/Ostranauts/Saves";
        var rows = new List<object>();
        foreach (string folder in Directory.GetDirectories(root)) {
            string[] files;
            try { files = Directory.GetFiles(folder, "*.zip"); }
            catch (DirectoryNotFoundException) { Console.WriteLine("Skipped inaccessible directory: " + folder); continue; }
            foreach (string file in files) {
                using (var archive = ZipFile.OpenRead(file)) {
                    foreach (string id in new[] { "O-CMK", "O-ZWZA", "O-GLN", "O-QVQ" }) {
                        var entry = archive.GetEntry("ships/" + id + ".json");
                        if (entry == null) continue;
                        JObject data;
                        using (var reader = new StreamReader(entry.Open()))
                        using (var json = new JsonTextReader(reader) { MaxDepth = null }) {
                            JToken record = JToken.Load(json);
                            data = (JObject)(record is JArray array ? array[0] : record);
                        }
                        JToken target = data["objSituScanTarget"];
                        rows.Add(new { save = Path.GetFileName(folder), id, targetId = (string)data["strTargetRegID"],
                            scanId = (string)data["strScanTargetID"], body = (string)target?["boPORShip"],
                            isBody = (bool?)target?["bIsBO"], x = (double?)target?["vPosx"],
                            y = (double?)target?["vPosy"], velocityX = (double?)target?["vVelX"] });
                    }
                }
            }
        }
        File.WriteAllText("obj/AI-TARGET-SAVE-AUDIT.json", JsonConvert.SerializeObject(rows, Formatting.Indented));
        Console.WriteLine(rows.Count + " target records inspected across saved archives.");
        return 0;
    }
    static int AuditCargo() {
        string path = Environment.GetEnvironmentVariable("USERPROFILE") + "/AppData/LocalLow/Blue Bottle Games/Ostranauts/Saves/autosave_4_next_1/autosave_4_next_1.zip";
        using (var archive = ZipFile.OpenRead(path))
        using (var stream = new StreamReader(archive.GetEntry("Ayden Kelly.json").Open()))
        using (var reader = new JsonTextReader(stream) { MaxDepth = null }) {
            while (reader.Read()) {
                if (reader.TokenType != JsonToken.PropertyName) continue;
                string name = (string)reader.Value;
                reader.Read();
                if (name == "objMarketSave") {
                    var market = JObject.Load(reader);
                    JToken cargo = market["mapMarket"]?["O-0PDS"];
                    File.WriteAllText("obj/NAVFIX-CARGO-MARKET.json", cargo?.ToString() ?? "null");
                    Console.WriteLine(cargo?.ToString() ?? "No market entry for O-0PDS");
                    return 0;
                }
                reader.Skip();
            }
        }
        throw new Exception("Market save not found");
    }
    static int Main(string[] args) {
        if (args.Length != 0 && args[0] == "audit-cargo") return AuditCargo();
        if (args.Length != 0 && args[0] == "audit-saves") return AuditSaves();
        var h = new Harmony("offline.ai-planner.diagnosis");
        h.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Equality"), prefix: new HarmonyMethod(typeof(Program), nameof(Equal)));
        h.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Inequality"), prefix: new HarmonyMethod(typeof(Program), nameof(Unequal)));
        if (args.Length != 0 && args[0] == "ceres") { CeresAuditChecks.Run(); return 0; }
        if (args.Length != 0 && args[0] == "audit-priority") { CeresAuditChecks.Run(true); return 0; }
        if (args.Length != 0 && args[0] == "lifecycle") { LifecycleChecks.Run(args.Length > 1 ? args[1] : null); return 0; }
        if (args.Length != 0 && args[0] == "traffic") { TrafficChecks.Run(); return 0; }
        if (args.Length != 0 && args[0] == "hidden-visits") { TrafficChecks.Run(true); return 0; }
        if (args.Length != 0 && args[0] == "civilian-visits") { TrafficChecks.Run(false, true); return 0; }
        if (args.Length != 0 && args[0] == "traffic-conversion") { LifecycleChecks.Run(null, true); return 0; }
        if (args.Length != 0 && args[0] == "region-cleanup") { LifecycleChecks.Run(null, false, true); return 0; }
        h.Patch(AccessTools.PropertyGetter(typeof(Ship), "RCSAccelMax"), prefix: new HarmonyMethod(typeof(Program), nameof(Acceleration)));
        foreach (var method in AccessTools.GetDeclaredMethods(typeof(Ship))) if (method.Name == "CalculateRCSFuelConsumption") h.Patch(method, prefix: new HarmonyMethod(typeof(Program), nameof(Fuel)));
        h.Patch(AccessTools.Method(typeof(StarSystem), "GetGreatestGravBO"), prefix: new HarmonyMethod(typeof(Program), nameof(Gravity)));
        h.Patch(AccessTools.Method(typeof(StarSystem), "GetBO"), prefix: new HarmonyMethod(typeof(Program), nameof(Body)));
        h.Patch(AccessTools.Method(typeof(BodyOrbit), "UpdateTime"), prefix: new HarmonyMethod(typeof(Program), nameof(BodyTime)));
        h.Patch(AccessTools.Method(typeof(FlightCPU), "GetDockingNavPoint", new[] { typeof(ShipSitu), typeof(ShipSitu), typeof(double), typeof(bool) }), prefix: new HarmonyMethod(typeof(Program), nameof(NoDetour)));
        h.Patch(AccessTools.Method(typeof(CollisionManager), "GetCollisionDistanceAU", new[] { typeof(ShipSitu), typeof(ShipSitu) }), prefix: new HarmonyMethod(typeof(Program), nameof(Collision)));
        h.Patch(AccessTools.Method(typeof(FlyToPath), "PlanTrip4Sub"), postfix: new HarmonyMethod(typeof(Program), nameof(Planned)));
        CrewSim.system = Empty<StarSystem>(); reference = Empty<BodyOrbit>(); reference.strName = "linear-station";
        if (args.Length != 0 && args[0] == "retired-target") return AuditRetiredTarget(args[1], double.Parse(args[2], CultureInfo.InvariantCulture));
        if (args.Length != 0 && args[0] == "recovery") {
            h.Unpatch(AccessTools.PropertyGetter(typeof(Ship), "RCSAccelMax"), HarmonyPatchType.Prefix, h.Id);
            foreach (var method in AccessTools.GetDeclaredMethods(typeof(Ship))) if (method.Name == "CalculateRCSFuelConsumption") h.Unpatch(method, HarmonyPatchType.Prefix, h.Id);
            h.Patch(AccessTools.Method(AccessTools.TypeByName("MarketManager"), "GetCargoMassForShip"), prefix: new HarmonyMethod(typeof(Program), nameof(NoCargo)));
            RecoveryChecks.Run(PlanCurrent); return 0;
        }
        var outcomes = new List<object>();
        foreach (string id in new[] { "O-CMK", "O-ZWZA", "O-GLN", "O-QVQ" }) {
            var data = (JObject)JObject.Parse(File.ReadAllText(Ostrabus.Development.InstallationPaths.Fixture("AiPlanner/RING-ai-" + id + ".json")))["data"];
            var frozen = Point(data["navigation"]["situationTarget"], 0, true).ObjSS;
            double x = frozen.vPosx; frozen.UpdateTime(100000);
            Check(frozen.vPosx == x && frozen.vVelX != 0, "Unreferenced target did not reproduce frozen-position/nonzero-velocity state");
            NavData broken = Replay(data, false);
            double brokenSeconds = broken.Destination.ArrivalTime - broken.Origin.ArrivalTime;
            var brokenIterations = iterations.ToArray();
            Check(brokenSeconds > 1e17 && iterations.Count == 11, "Native full planner did not produce runaway convergence");
            double capturedSeconds = Number(data["navigation"]["currentNavPlan"]["destination"]["arrivalEpoch"]) - startEpoch;
            Check(Math.Abs(brokenSeconds / capturedSeconds - 1) < 2e-8, "Replay does not match the captured live route duration");
            NavData corrected = Replay(data, true);
            double correctedSeconds = corrected.Destination.ArrivalTime - corrected.Origin.ArrivalTime;
            Check(correctedSeconds > 0 && correctedSeconds < 86400, "Consistent moving-target control did not produce a reasonable route");
            Console.WriteLine(id + ": unreferenced=" + brokenSeconds.ToString("G8") + " s; referenced moving-target control=" + correctedSeconds.ToString("F2") + " s.");
            outcomes.Add(new { id, brokenSeconds, correctedSeconds, brokenIterations, correctedIterations = iterations.ToArray() });
        }
        File.WriteAllText("obj/AI-PLANNER-DIAGNOSIS.json", JsonConvert.SerializeObject(outcomes, Formatting.Indented));
        Console.WriteLine(checks + " native AI planner diagnosis checks passed.");
        return 0;
    }
    static int AuditRetiredTarget(string path, double speed) {
        // Real saved kinematics, native planner and situation copying. The same
        // prescribed acceleration, gravity, fuel and collision fixture boundaries
        // as the original diagnosis apply; this does not replay a whole sortie.
        JToken saved = JToken.Parse(File.ReadAllText(path));
        if (saved is JArray array) saved = array[0];
        JToken first = saved["objSS"]["jnd"]["aPoints"][0];
        JArray points = (JArray)saved["objSS"]["jnd"]["aPoints"];
        var target = new ShipSitu(saved["objSituScanTarget"].ToObject<JsonShipSitu>());
        var vessel = Empty<Ship>();
        AccessTools.Field(typeof(Ship), "<objSS>k__BackingField").SetValue(vessel, new ShipSitu(first["ObjSS"].ToObject<JsonShipSitu>()));
        vessel.fShallowRCSRemass = Number(first["FuelLevel"]);
        vessel.shipSituTarget = target;
        double epoch = Number(first["ArrivalTime"]);
        Check(target.bBOLocked && !target.bIsBO && target.strBOPORShip == null,
            "Captured target must be an anchored ship situation with its body reference removed");
        double before = target.vPosx;
        target.UpdateTime(epoch + 100000);
        Check(target.vPosx == before && target.vVelX != 0, "Retired target must retain velocity while its position stays frozen");
        Check(new TargetData(target).HasValidTarget, "Native target validity unexpectedly rejects the retired situation");
        iterations.Clear();
        NavData broken = PlanCurrent(vessel, epoch, speed);
        double duration = broken.Destination.ArrivalTime - broken.Origin.ArrivalTime;
        double capturedDuration = Number(points[points.Count - 1]["ArrivalTime"]) - epoch;
        Check(duration > 1e17 && iterations.Count == 11, "Native retired-target replay did not reproduce divergent planning");
        Check(Math.Abs(duration / capturedDuration - 1) < 2e-8, "Native replay differs from captured route duration");
        var brokenIterations = iterations.ToArray();
        // A diagnostic control only: a genuinely destroyed ship must be discarded,
        // not rebound to this artificial moving body in a production repair.
        target.strBOPORShip = "linear-station";
        iterations.Clear();
        NavData control = PlanCurrent(vessel, epoch, speed);
        double controlDuration = control.Destination.ArrivalTime - control.Origin.ArrivalTime;
        Check(controlDuration > 0 && controlDuration < 86400, "Consistent-motion control did not converge");
        File.WriteAllText("obj/ConsoleIssues/retired-target-planner-replay.json", JsonConvert.SerializeObject(new {
            duration, capturedDuration, controlDuration, brokenIterations, controlIterations = iterations.ToArray()
        }, Formatting.Indented));
        Console.WriteLine("Retired-target replay: " + duration.ToString("G12") + " s; captured: " + capturedDuration.ToString("G12") +
            " s; consistent-motion control: " + controlDuration.ToString("F2") + " s. " + checks + " checks passed.");
        return 0;
    }
    internal sealed class Pilot : IAICharacter {
        internal double Speed;
        internal AIType Role = AIType.Scav;
        public Ship ShipUs { get; set; }
        public CondOwner CaptainUs => null;
        public QueuedCommandsNode PriorityQueue { get; set; }
        public ICommand BehaviorTree { get; set; }
        public Blackboard Blackboard { get; set; }
        public AIType AIType => Role;
        public TargetData GetTarget() => null;
        public TargetData GetTargetHome() => null;
        public double MaxSpeed(bool? inAtmo = null) => Speed;
        public ICommand FFWD(ICommand command, Blackboard blackboard) => command;
    }
}
