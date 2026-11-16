using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ostranauts.Core;
using Ostranauts.Ships.Commands;
using SolarSystem;

static partial class Program
{
    // Counterexample to treating a running Hold as proof of a safe free orbit.
    // This is a constructed native-accepted state, not an observed ship incident.
    static void HoldSafety(Harmony engine)
    {
        var plugin = typeof(Ostrabus.OstrabusPlugin).Assembly;
        var enabled = AccessTools.Property(plugin.GetType("Ostrabus.Orbital.OrbitalSimulation", true), "Enabled");
        bool previous = (bool)enabled.GetValue(null);
        var correction = new Harmony("offline.docking-orbit.hold-safety");
        var update = AccessTools.Method(typeof(BodyOrbit), nameof(BodyOrbit.UpdateTime));
        engine.Unpatch(update, HarmonyPatchType.Prefix, engine.Id);
        try
        {
            enabled.SetValue(null, true);
            correction.CreateClassProcessor(plugin.GetType("Ostrabus.Orbital.OrbitalGravityPatch", true)).Patch();
            var saved = JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "PhobosBodies.json")));
            foreach (bool ignore in new[] { true, false })
            {
                World();
                var world = CrewSim.system;
                world.aBOs.Clear();
                foreach (string name in new[] { "Sol", "Mars", "Phobos" })
                    world.aBOs[name] = new BodyOrbit(saved[name].ToObject<JsonBodyOrbitSave>());
                var sun = world.aBOs["Sol"];
                var mars = world.aBOs["Mars"];
                var moon = world.aBOs["Phobos"];
                world.boStar = sun;
                mars.boParent = sun;
                moon.boParent = mars;
                sun.boChildren = new List<BodyOrbit> { mars };
                mars.boChildren = new List<BodyOrbit> { moon };
                Field(world, "fGravAccelConstant", 2e-44f);
                const double start = 65634302058.282005;
                StarSystem.fEpoch = start;
                foreach (var body in world.aBOs.Values)
                    body.UpdateTime(start);
                var visitor = Hull("HOLDING");
                var wreck = Hull("PASSIVE");
                foreach (var ship in new[] { visitor, wreck })
                {
                    ship.objSS.vPosx = mars.dXReal + mars.fRadius + 300 / 149597872d;
                    ship.objSS.vPosy = mars.dYReal;
                    ship.objSS.vVelX = mars.dVelX;
                    ship.objSS.vVelY = mars.dVelY;
                }
                double startingRadiusKm = (visitor.objSS.vPosx - mars.dXReal) * 149597872d;
                foreach (var layer in mars.aAtmospheres)
                    Check(startingRadiusKm > layer.fMaxAltitude, "Hold safety control did not start above the native atmosphere.");
                visitor.shipSituTarget = new ShipSitu(false) { vVelX = mars.dVelX, vVelY = mars.dVelY };
                visitor.fAIDockingExpire = double.NegativeInfinity;
                var hold = Empty<Hold>();
                Field(hold, "_shipUs", visitor);
                var board = new Blackboard();
                Check(hold.CanExecute(board), "Native Hold rejected the controlled zero-relative-velocity state.");
                visitor.fAIDockingExpire = 600;
                hold.RunCommand(board);
                Check(visitor.objSS.bIgnoreGrav && visitor.objSS.vAccRCS.x == 0 && visitor.objSS.vAccRCS.y == 0,
                    "Native Hold did not stop propulsion and disable gravity.");
                var group = new DockGroup();
                group.AddShip(visitor);
                group.AddShip(wreck);
                group.Anchor.bIgnoreGrav = ignore;
                Field(world, "_dockingGroups", new List<Ship> { visitor, wreck });
                Field(world, "_updatedGroups", new HashSet<string>());
                var integrate = AccessTools.Method(typeof(StarSystem), "UpdateDockingGroups");
                bool crossed = false;
                double elapsed = 0, altitude = 300;
                while (elapsed < 600)
                {
                    elapsed += .5;
                    StarSystem.fEpoch = start + elapsed;
                    foreach (var body in world.aBOs.Values)
                        body.UpdateTime(StarSystem.fEpoch);
                    integrate.Invoke(world, new object[] { .5 });
                    double dx = group.Anchor.vPosx - mars.dXReal, dy = group.Anchor.vPosy - mars.dYReal;
                    altitude = (Math.Sqrt(dx * dx + dy * dy) - mars.fRadius) * 149597872d;
                    if (altitude <= 0)
                    {
                        crossed = true;
                        break;
                    }
                }
                Check(crossed == !ignore, "Hold counterexample did not distinguish gravity-free waiting from an unsafe free fall.");
                Console.WriteLine("Constructed Hold safety control: ignore=" + ignore + "; elapsed=" + elapsed +
                    " s; altitude=" + altitude + " km; crossed Mars surface=" + crossed);
            }
        }
        finally
        {
            correction.UnpatchSelf();
            enabled.SetValue(null, previous);
            engine.Patch(update, prefix: new HarmonyMethod(typeof(Program), nameof(Skip)));
        }
    }
}
