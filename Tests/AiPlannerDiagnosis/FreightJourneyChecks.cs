using System;
using System.IO;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ostrabus.ShipNavigation;
using Ostranauts.Core;
using Ostranauts.Ships;
using Ostranauts.Ships.Commands;
using Ostranauts.ShipGUIs.Utilities;

static class FreightJourneyChecks {
    delegate BodyOrbit Gravity(StarSystem world, ShipSitu situation, ref UnityEngine.Vector2 acceleration, ref BodyOrbit nearest);
    internal static void Run(string path, string id, bool departure = false) {
        var scene = new Harmony("offline.freight-journey.scene");
        var production = new Harmony("offline.freight-journey.production");
        var full = System.Reflection.Assembly.LoadFrom(System.IO.Path.GetFullPath("bin/Debug/netstandard2.1/Ostrabus.dll"));
        Type Patch(string name) => full.GetType("Ostrabus.ShipNavigation." + name, true);
        var orbital = full.GetType("Ostrabus.Orbital.OrbitalSimulation", true);
        var enabled = AccessTools.Property(orbital, "Enabled");
        bool previousEnabled = (bool)enabled.GetValue(null);
        try {
            FreightCourseChecks.Scene(scene);
            foreach (string name in new[] { "AiRcsAccelerationPatch", "AiRendezvousValidationPatch", "FreightRendezvousFailurePatch",
                "AiCoursePlaybackPatch", "AiManualCelestialPatch", "AvoidanceFlightPatch", "AIShipAvoidancePatch",
                "FlightControlHandoffPatch", "FlightPilotAvailabilityPatch", "ShallowTorchShutdownPatch", "ShallowRcsTorchReleasePatch",
                "DepartureWaypointPatch", "RouteCompletionPatch" }) production.CreateClassProcessor(Patch(name)).Patch();
            production.CreateClassProcessor(full.GetType("Ostrabus.Orbital.OrbitalGravityPatch", true)).Patch();
            enabled.SetValue(null, true);
            var gravityQuery = AccessTools.MethodDelegate<Gravity>(AccessTools.Method(typeof(StarSystem), "GetGreatestGravBOFast"));
            var data = JObject.Parse(File.ReadAllText(path));
            var world = FreightCourseChecks.World(data);
            var ship = FreightCourseChecks.Vessel(data["ships"][id]);
            ship.objSS.NavData = new NavData(data["ships"][id]["objSS"]["jnd"].ToObject<JsonNavData>());
            if (departure) {
                var route = ship.objSS.NavData;
                StarSystem.fEpoch = route.Origin.ArrivalTime;
                ship.objSS.CopyFrom(route.Origin.ObjSS, false);
                ship.objSS.NavData = route;
                ship.fShallowRCSRemass = route.Origin.FuelLevel;
                ship.fShallowFusionRemain = route.Origin.TorchFuelLevel;
            }
            var pilot = new FreightCourseChecks.Pilot { ShipUs = ship, Blackboard = new Blackboard() };
            var planner = new FlyTo(pilot);
            double start = StarSystem.fEpoch;
            double minimum = double.PositiveInfinity;
            int samples = 0, leg = 0, manualTicks = 0;
            bool arrived = false;
            for (int turn = 0; turn < 20000 && leg < 24; turn++) {
                foreach (var body in world.aBOs.Values) body.UpdateTime(StarSystem.fEpoch);
                foreach (var station in world.dictShips.Values) if (station.objSS.bIsBO) station.objSS.UpdateTime(StarSystem.fEpoch);
                var status = planner.RunCommand(pilot.Blackboard);
                if (status.Status == CommandCode.Finished) { arrived = true; break; }
                var route = ship.objSS.NavData;
                if (route == null) {
                    if (status.Status != CommandCode.Running) throw new Exception(id + ": failed at leg " + leg + ", status=" + status.Status);
                    manualTicks++;
                    for (int step = 0; step < 8; step++) {
                        double epoch = StarSystem.fEpoch + .125;
                        // Native gravity and native steering/propulsion remain
                        // active. Only the engine's frame clock is supplied.
                        var gravity = default(UnityEngine.Vector2);
                        BodyOrbit closest = null;
                        gravityQuery(world, ship.objSS, ref gravity, ref closest);
                        ship.objSS.vAccEx = gravity;
                        StarSystem.fEpoch = epoch;
                        foreach (var body in world.aBOs.Values) body.UpdateTime(epoch);
                        ship.objSS.TimeAdvance(.125);
                        foreach (var body in world.aBOs.Values) {
                            if (body.IsPlaceholder() || body.IsAsteroidField || !(body.fRadius > 0)) continue;
                            double km = (ship.objSS.GetDistance(body.dXReal, body.dYReal) - body.fRadius - ship.objSS.GetRadiusAU()) * 149597872d;
                            minimum = Math.Min(minimum, km);
                            if (!(km > 0)) throw new Exception(id + ": manual steering intersected " + body.strName + " at tick " + manualTicks);
                        }
                    }
                    if (manualTicks % 100 == 0 || manualTicks == 1) Console.WriteLine(id + " manual ticks " + manualTicks +
                        " rcsMax=" + ship.RCSAccelMax * 149597872000d + " rcs=" + ship.objSS.vAccRCS.magnitude * 149597872000d +
                        " gravity=" + ship.objSS.vAccEx.magnitude * 149597872000d + " minKm=" + minimum);
                    continue;
                }
                var reference = NativeCourseGeometry.Inspect(route, StarSystem.fEpoch, ship.objSS.GetRadiusAU());
                samples += reference.Samples; minimum = Math.Min(minimum, reference.ClearanceKm);
                if (!(reference.ClearanceKm > 0)) throw new Exception(id + ": native reference found an intersection with " + reference.Body + " (" + reference.ClearanceKm + " km) at leg " + leg);
                double end = route.Destination.ArrivalTime;
                if (!(end > StarSystem.fEpoch) || end - start > 86400 * 120) throw new Exception(id + ": non-advancing or excessive voyage");
                var milestones = new System.Collections.Generic.SortedSet<double>();
                foreach (var point in route.GetNavDataPoints()) if (point.ArrivalTime > StarSystem.fEpoch) milestones.Add(point.ArrivalTime);
                milestones.Add(reference.Epoch);
                // Actual native playback on every breakpoint plus its independent
                // worst-approach time. Completion uses the existing handoff fix.
                foreach (double epoch in milestones) {
                    if (epoch <= StarSystem.fEpoch) continue;
                    double elapsed = epoch - StarSystem.fEpoch; StarSystem.fEpoch = epoch;
                    foreach (var body in world.aBOs.Values) body.UpdateTime(epoch);
                    ship.objSS.TimeAdvance(elapsed);
                    if (!ReferenceEquals(ship.shipScanTarget, world.dictShips[(string)data["ships"][id]["strScanTargetID"]]))
                        throw new Exception(id + ": native playback replaced the mission destination");
                }
                double after = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(end) + 1);
                double remainder = after - StarSystem.fEpoch; StarSystem.fEpoch = after;
                ship.objSS.TimeAdvance(remainder);
                if (ship.objSS.NavData != null) throw new Exception(id + ": native course did not complete");
                Console.WriteLine(id + " leg " + leg + ": " + ((end - start) / 3600).ToString("F2") + " h, clearance=" + reference.ClearanceKm.ToString("F3") + " km");
                leg++;
            }
            double handoff = (double)AccessTools.Method(typeof(BaseCommand), "GetHandOffDistance").Invoke(planner,
                new object[] { (double)CollisionManager.GetCollisionDistanceAU(ship.objSS, ship.shipSituTarget), ship.shipScanTarget });
            if (!arrived || leg == 24 || ship.objSS.GetRangeTo(ship.shipSituTarget) > handoff)
                throw new Exception(id + ": waypoint loop without final station handoff");
            Console.WriteLine(new JObject { ["id"] = id, ["arrived"] = true, ["legs"] = leg,
                ["hours"] = (StarSystem.fEpoch - start) / 3600, ["minimumClearanceKm"] = minimum, ["nativeSamples"] = samples, ["manualTicks"] = manualTicks,
                ["productionMvid"] = full.ManifestModule.ModuleVersionId.ToString(), ["gameMvid"] = typeof(Ship).Module.ModuleVersionId.ToString()
            }.ToString(Newtonsoft.Json.Formatting.None));
        } finally { enabled.SetValue(null, previousEnabled); production.UnpatchSelf(); scene.UnpatchSelf(); CrewSim.system = null; }
    }
}
