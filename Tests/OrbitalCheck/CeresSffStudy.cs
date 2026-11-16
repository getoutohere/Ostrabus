using System;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Ostrabus.Orbital;
using System.Runtime.Serialization;
using HarmonyLib;
using Ostranauts.ShipGUIs.Utilities;

// Optional reproduction against an external extracted save; no player data is tracked.
static class CeresSffStudy {
    static bool Yes(ref bool __result) { __result = true; return false; }
    static MotionState NativeFinish(JToken saved, double end, BodyOrbit ceres) {
        var boundary = new Harmony("offline.compat-impact.boundaries");
        var priorEpoch = StarSystem.fEpoch;
        try {
            boundary.Patch(AccessTools.PropertyGetter(typeof(GUIFFWD), nameof(GUIFFWD.Active)), prefix: new HarmonyMethod(typeof(CeresSffStudy), nameof(Yes)));
            boundary.Patch(AccessTools.PropertyGetter(typeof(Ship), nameof(Ship.IsAIShip)), prefix: new HarmonyMethod(typeof(CeresSffStudy), nameof(Yes)));
            var ship = (Ship)FormatterServices.GetUninitializedObject(typeof(Ship));
            ship.strRegID = (string)saved["strRegID"];
            AccessTools.Field(typeof(Ship), "nLoadState").SetValue(ship, Ship.Loaded.Shallow);
            AccessTools.Field(typeof(Ship), "fRCSCount").SetValue(ship, (float)saved["nRCSCount"]);
            ship.fShallowRCSRemass = (double)saved["fShallowRCSRemass"];
            var situation = new ShipSitu(false);
            AccessTools.Field(typeof(Ship), "<objSS>k__BackingField").SetValue(ship, situation);
            var course = new NavData(ship);
            foreach (var point in saved["objSS"]["jnd"]["aPoints"]) {
                var raw = point["ObjSS"].ToObject<JsonShipSitu>();
                var state = new ShipSitu(false) { vPosx = raw.vPosx, vPosy = raw.vPosy, vVelX = raw.vVelX, vVelY = raw.vVelY,
                    vAccIn = raw.vAccIn, vAccRCS = raw.vAccRCS, vAccEx = raw.vAccEx, bIsBO = raw.bIsBO,
                    bBOLocked = raw.bBOLocked, bOrbitLocked = raw.bOrbitLocked, strBOPORShip = raw.boPORShip };
                course.AddNavDataPoint(new NavDataPoint((double)point["ArrivalTime"], state, (double)point["FuelLevel"],
                    (float)point["TorchCycle"], (double)point["TorchFuelLevel"], (float)point["RCSCount"]));
            }
            situation.NavData = course; StarSystem.fEpoch = end;
            bool result = course.TimeAdvance(situation);
            OrbitalMotion.BodyAt(ceres,end,out var center);
            Console.WriteLine(" nativeGuiFinish=" + result + " position=" + situation.vPosx + "," + situation.vPosy +
                " altitudeKm=" + (new DVector(situation.vPosx*OrbitalMotion.KmPerAU,situation.vPosy*OrbitalMotion.KmPerAU)-center.Position).Length);
            return OrbitalMotion.Read(situation);
        } finally { boundary.UnpatchSelf(); StarSystem.fEpoch = priorEpoch; }
    }
    internal static void CompatibilityImpacts(string folder) {
        var system = JObject.Parse(File.ReadAllText(Path.Combine(folder, "pre-sff-save-system.json")))["objSystem"];
        var bodies = new Dictionary<string, BodyOrbit>();
        foreach (string key in new[] { "aBOs", "aAsteroidFields" }) foreach (var node in system[key]) {
            var body = new BodyOrbit(node.ToObject<JsonBodyOrbitSave>()); bodies.Add(body.strName, body);
        }
        foreach (string pair in system["dictBOHierarchy"].Values<string>()) {
            var parts = pair.Split('=');
            if (parts.Length == 2 && bodies.TryGetValue(parts[0], out var child) && bodies.TryGetValue(parts[1], out var parent)) child.boParent = parent;
        }
        double start = (double)system["dfEpoch"], end = 65634086649.562645;
        var ceres = bodies["Ceres"];
        var environment = new SffEnvironment();
        foreach (var body in bodies.Values) {
            if (!body.IsPlaceholder() && !body.IsAsteroidField && body.fMass > 0 && OrbitalMotion.BodyAt(body, start, out _))
                environment.Bodies.Add(new SffBody(body));
        }
        MotionState Read(JToken state) => new MotionState(
            new DVector((double)state["vPosx"], (double)state["vPosy"]) * OrbitalMotion.KmPerAU,
            new DVector((double)state["vVelX"], (double)state["vVelY"]) * OrbitalMotion.KmPerAU);
        double Altitude(MotionState state, double epoch) {
            OrbitalMotion.BodyAt(ceres, epoch, out var center);
            return (state.Position - center.Position).Length - ceres.fRadius * OrbitalMotion.KmPerAU;
        }
        foreach (string id in new[] { "O-E3SE", "O-Y2L" }) {
            var ship = JToken.Parse(File.ReadAllText(Path.Combine(folder, "pre-sff-save-" + id + ".json")))[0];
            var state = Read(ship["objSS"]);
            var coast = SffDynamics.Propagate(environment, state, start, end-start, default, null, 0, null);
            Console.WriteLine(id + " caches=" + ship["nRCSCount"] + "/" + ship["nRCSCountRaw"] + "/" + ship["nRCSExhaustV"] +
                " savedAltitudeKm=" + Altitude(state,start) + " shipStateEpoch=" + ship["objSS"]["fPathLastEpoch"] +
                " unguidedImpact=" + coast.Impact?.Native.strName);
            Console.WriteLine(" shipCO=" + ship["shipCO"]?["strID"] + " conditions=" + string.Join(";", ship["shipCO"]?["aConds"]?.Values<string>() ?? Array.Empty<string>()));
            var points = ship["objSS"]?["jnd"]?["aPoints"] as JArray;
            if (points == null || points.Count < 2) continue;
            double minimum = double.PositiveInfinity;
            for (int i = 1; i < points.Count; i++) {
                double first = (double)points[i-1]["ArrivalTime"], last = (double)points[i]["ArrivalTime"];
                var a = Read(points[i-1]["ObjSS"]); var b = Read(points[i]["ObjSS"]);
                for (int sample = 0; sample <= 16; sample++) {
                    double fraction = sample/16d, epoch = first + (last-first)*fraction;
                    if (epoch < start) continue;
                    var position = a.Position + (b.Position-a.Position)*fraction;
                    minimum = Math.Min(minimum, Altitude(new MotionState(position,default),epoch));
                }
            }
            var final = points[points.Count-1]; double arrival = (double)final["ArrivalTime"];
            var after = SffDynamics.Propagate(environment, Read(final["ObjSS"]), arrival, Math.Max(0,end-arrival), default,null,0,null);
            Console.WriteLine(" rawPlannedMinAltitudeKm=" + minimum + " arrival=" + arrival +
                " finalStateEpoch=" + final["ObjSS"]["fPathLastEpoch"] + " postRouteCoastImpact=" + after.Impact?.Native.strName +
                " endAltitudeKm=" + Altitude(after.State,end));
            var actual = NativeFinish(ship,end,ceres);
            double nearestRock = double.PositiveInfinity; string nearestRockId = null;
            foreach (var field in system["aAsteroidFields"]) {
                OrbitalMotion.BodyAt(bodies[(string)field["strName"]],end,out var center);
                foreach (var rock in field["aStellarObjects"]) {
                    var situation = new ShipSitu(false) { vBOOffsetx=(double)rock["vBOOffsetx"], vBOOffsety=(double)rock["vBOOffsety"],
                        fOrbitW=(double?)rock["fOrbitW"] ?? 0, fOrbitTimeLast=(double?)rock["fOrbitTimeLast"] ?? 0 };
                    double distance = (new BodyLockedOffset(situation).At(center,end).Position-actual.Position).Length - (double)rock["nSize"]*.001;
                    if (distance < nearestRock) { nearestRock = distance; nearestRockId = (string)rock["strID"]; }
                }
            }
            Console.WriteLine(" nativeFinishNearestRock="+nearestRockId+" clearanceKm="+nearestRock);
        }
    }
    internal static void Run(string folder) {
        var system = JObject.Parse(File.ReadAllText(Path.Combine(folder, "system.json")));
        var bodies = new Dictionary<string, BodyOrbit>();
        foreach (string key in new[] { "aBOs", "aAsteroidFields" })
            foreach (var node in system[key]) {
                var body = new BodyOrbit(node.ToObject<JsonBodyOrbitSave>());
                bodies.Add(body.strName, body);
            }
        foreach (string pair in system["dictBOHierarchy"].Values<string>()) {
            var parts = pair.Split('=');
            if (parts.Length == 2 && bodies.TryGetValue(parts[0], out var child) && bodies.TryGetValue(parts[1], out var parent)) child.boParent = parent;
        }
        double start = (double)system["dfEpoch"], end = 65634053088.797806;
        foreach (var id in new[] { "BCER", "BCRS", "Ceres", "CeresAsteroidFieldCentral", "C108a", "C670p" }) {
            OrbitalMotion.BodyAt(bodies[id], start, out var first);
            OrbitalMotion.BodyAt(bodies[id], end, out var last);
            Console.WriteLine(id + " start=" + first.Position.X / OrbitalMotion.KmPerAU + "," + first.Position.Y / OrbitalMotion.KmPerAU +
                " end=" + last.Position.X / OrbitalMotion.KmPerAU + "," + last.Position.Y / OrbitalMotion.KmPerAU);
        }
        foreach (var id in new[] { "O-7X75", "O-326", "O-BDJ", "O-KGEE" }) {
            var root = JToken.Parse(File.ReadAllText(Path.Combine(folder, id + ".json")));
            var ship = root is JArray ? root[0] : root;
            var target = ship["objSituScanTarget"];
            Console.WriteLine(id + " target=" + ship["strTargetRegID"] + " targetBody=" + target?["boPORShip"]);
            if (target?["boPORShip"] != null && bodies.TryGetValue((string)target["boPORShip"], out var body)) {
                OrbitalMotion.BodyAt(body, end, out var actual);
                Console.WriteLine(" expected target end=" + (actual.Position.X / OrbitalMotion.KmPerAU + (double)target["vBOOffsetx"]) + "," +
                    (actual.Position.Y / OrbitalMotion.KmPerAU + (double)target["vBOOffsety"]));
            }
        }
    }
}
