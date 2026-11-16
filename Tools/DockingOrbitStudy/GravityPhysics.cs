using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using SolarSystem;

static partial class Program
{
    static void GravityPhysics(Harmony engine)
    {
        var plugin = typeof(Ostrabus.OstrabusPlugin).Assembly;
        var orbital = plugin.GetType("Ostrabus.Orbital.OrbitalSimulation", true);
        var enabled = AccessTools.Property(orbital, "Enabled");
        bool previous = (bool)enabled.GetValue(null);
        var correction = new Harmony("offline.docking-orbit.gravity-physics");
        var update = AccessTools.Method(typeof(BodyOrbit), nameof(BodyOrbit.UpdateTime));
        engine.Unpatch(update, HarmonyPatchType.Prefix, engine.Id);
        try
        {
            enabled.SetValue(null, true);
            correction.CreateClassProcessor(plugin.GetType("Ostrabus.Orbital.OrbitalGravityPatch", true)).Patch();
            var saved = JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "PhobosBodies.json")));
            foreach (bool ignore in new[] { true, false })
                foreach (double step in new[] { .5, 5.333333333333333 })
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
                    var visitor = Hull("VISITOR");
                    var wreck = Hull("WRECK");
                    visitor.objSS.bIgnoreGrav = ignore;
                    wreck.objSS.bIgnoreGrav = false;
                    foreach (var ship in new[] { visitor, wreck })
                    {
                        ship.objSS.vPosx = moon.dXReal + 70 / 149597872d;
                        ship.objSS.vPosy = moon.dYReal;
                        ship.objSS.vVelX = moon.dVelX;
                        ship.objSS.vVelY = moon.dVelY;
                    }
                    var group = new DockGroup();
                    group.AddShip(visitor);
                    group.AddShip(wreck);
                    Check(group.Anchor.bIgnoreGrav == ignore, "Group did not inherit the controlled flag value.");
                    double vx = group.Anchor.vVelX, vy = group.Anchor.vVelY;
                    Field(world, "_dockingGroups", new List<Ship> { visitor, wreck });
                    Field(world, "_updatedGroups", new HashSet<string>());
                    var integrate = AccessTools.Method(typeof(StarSystem), "UpdateDockingGroups");
                    double elapsed = 0;
                    while (elapsed < 600)
                    {
                        double next = Math.Min(600, elapsed + step);
                        StarSystem.fEpoch = start + next;
                        foreach (var body in world.aBOs.Values)
                            body.UpdateTime(StarSystem.fEpoch);
                        integrate.Invoke(world, new object[] { next - elapsed });
                        elapsed = next;
                    }
                    double dx = (group.Anchor.vVelX - moon.dVelX) * 149597872000d;
                    double dy = (group.Anchor.vVelY - moon.dVelY) * 149597872000d;
                    double mismatch = Math.Sqrt(dx * dx + dy * dy);
                    var released = new ShipSitu(group.Anchor, false);
                    bool relocked = released.TryRelockToBO(moon);
                    if (ignore)
                    {
                        Check(group.Anchor.vVelX == vx && group.Anchor.vVelY == vy, "Gravity-disabled group acquired an unexplained impulse.");
                        Check(mismatch > 100 && !relocked, "Gravity-disabled group did not reproduce failed Phobos relock.");
                    }
                    else
                    {
                        Check(mismatch < 100 && relocked, "Ordinary group gravity failed to preserve native relock eligibility.");
                        Check(Math.Abs(group.Anchor.vVelX - vx) > 1e-11, "Physical group gravity supplied no acceleration.");
                    }
                    Console.WriteLine("Native group gravity: ignore=" + ignore + "; step=" + step +
                        "; 600s mismatch=" + mismatch + " m/s; native relock=" + relocked);
                    var sff = plugin.GetType("Ostrabus.Orbital.SffSimulation", true);
                    var preview = AccessTools.Method(sff, "Preview").Invoke(null, new object[] { world, 120d });
                    Check(preview != null, "SFF preview was unavailable for the physical group.");
                    try
                    {
                        int plans = ((System.Collections.ICollection)AccessTools.Field(sff, "plans").GetValue(preview)).Count;
                        Check(plans == (ignore ? 0 : 1), "Gravity-ignore removal did not admit the group to the existing SFF coasting model.");
                    }
                    finally { ((IDisposable)preview).Dispose(); }
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
