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
    // Study-only alternative: retain an authored attachment, without clearing
    // the native Hold's gravity exemption or inventing a new attachment.
    static void AttachmentPhysics(Harmony engine)
    {
        var candidate = new Harmony("offline.docking-orbit.attachment-physics");
        var update = AccessTools.Method(typeof(BodyOrbit), nameof(BodyOrbit.UpdateTime));
        engine.Unpatch(update, HarmonyPatchType.Prefix, engine.Id);
        candidate.CreateClassProcessor(typeof(Ostrabus.OstrabusPlugin).Assembly.GetType("Ostrabus.ShipNavigation.PassiveDockAttachmentPatch", true)).Patch();
        try
        {
            var saved = JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "PhobosBodies.json")));
            foreach (bool reverse in new[] { false, true })
                foreach (bool circular in new[] { false, true })
                    foreach (double step in new[] { .5, 5.333333333333333, 7200 })
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
                        wreck.DMGStatus = Ship.Damage.Derelict;
                        wreck.objSS.bIgnoreGrav = false;
                        foreach (var ship in new[] { visitor, wreck })
                        {
                            ship.objSS.vPosx = moon.dXReal + 70 / 149597872d;
                            ship.objSS.vPosy = moon.dYReal;
                            ship.objSS.vVelX = moon.dVelX;
                            ship.objSS.vVelY = moon.dVelY;
                        }
                        if (circular)
                            wreck.objSS.LockToBOOrbit(moon);
                        else
                            wreck.objSS.LockToBO(moon);
                        if (reverse)
                            wreck.Dock(visitor, false, "VISITOR-port", "WRECK-port");
                        else
                            visitor.Dock(wreck, false, "WRECK-port", "VISITOR-port");
                        Check(wreck.objSS.bBOLocked && wreck.DockGroup.Anchor.bBOLocked, "Passive docking lost the authored attachment.");
                        var hold = Empty<Hold>();
                        Field(hold, "_shipUs", visitor);
                        visitor.fAIDockingExpire = 600;
                        hold.RunCommand(new Blackboard());
                        Check(visitor.objSS.bIgnoreGrav, "Attachment alternative changed native Hold policy.");
                        var moorer = Hull("PLAYER");
                        moorer.MoorTo(wreck, "MP|target", "MP|I|incoming");
                        moorer.UnMoorFrom(wreck);
                        var group = wreck.DockGroup;
                        Check(group.Anchor.bBOLocked, "Tether reconstruction lost the existing attachment.");
                        double end = start + (step == 7200 ? 7200 : 600);
                        var expected = new ShipSitu(wreck.objSS, false);
                        Field(world, "_dockingGroups", new List<Ship> { visitor, wreck });
                        Field(world, "_updatedGroups", new HashSet<string>());
                        var integrate = AccessTools.Method(typeof(StarSystem), "UpdateDockingGroups");
                        double epoch = start;
                        while (epoch < end)
                        {
                            double next = Math.Min(end, epoch + step);
                            StarSystem.fEpoch = next;
                            foreach (var body in world.aBOs.Values)
                                body.UpdateTime(next);
                            // StarSystem.UpdateShip advances locked members as
                            // well as the later UpdateDockingGroups anchor pass.
                            wreck.objSS.TimeAdvance(next - epoch);
                            integrate.Invoke(world, new object[] { next - epoch });
                            epoch = next;
                        }
                        expected.TimeAdvance(end - start);
                        Check(wreck.objSS.bBOLocked && group.Anchor.bBOLocked && visitor.objSS.bIgnoreGrav,
                            "Following the body changed attachment or native Hold policy.");
                        double dx = (wreck.objSS.vPosx - expected.vPosx) * 149597872000d;
                        double dy = (wreck.objSS.vPosy - expected.vPosy) * 149597872000d;
                        double error = Math.Sqrt(dx * dx + dy * dy);
                        Check(error < .1, "Passive visit diverged from the original body's attached trajectory.");
                        visitor.Undock(wreck);
                        Check(wreck.objSS.bBOLocked && wreck.objSS.strBOPORShip == "Phobos", "Visitor departure failed to retain native wreck relock.");
                        Check(!visitor.objSS.bBOLocked && !visitor.IsDocked(), "Departing visitor acquired an unwanted attachment.");
                        Console.WriteLine("Attached visit: reverse=" + reverse + "; circular=" + circular + "; step=" + step +
                            "; duration=" + (end - start) + "; wreck error=" + error + " m; Hold exemption retained.");
                    }
        }
        finally
        {
            candidate.UnpatchSelf();
            engine.Patch(update, prefix: new HarmonyMethod(typeof(Program), nameof(Skip)));
        }
    }
}
