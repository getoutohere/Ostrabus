using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using SolarSystem;

static partial class Program
{
    // Exact historical offsets and real body ephemerides; no live objects,
    // player inventories or save writers enter this optional regression.
    static void SavedMercuryChecks(Harmony engine, string originalPath, string currentPath)
    {
        int before = checks;
        var original = JObject.Parse(File.ReadAllText(originalPath))["fields"]["objSystem"].ToObject<JsonStarSystemSave>();
        var current = JObject.Parse(File.ReadAllText(currentPath))["fields"]["objSystem"].ToObject<JsonStarSystemSave>();
        Check(original.dfEpochGameStart == current.dfEpochGameStart && original.dfEpoch < current.dfEpoch,
            "Mercury fixtures belong to different worlds or are reversed.");
        var records = original.aStellarDerelicts.Where(x => x.nMapID == 100 && x.strParent == "HQCH_DET" && x.strOwner == "HQCHCiv").ToArray();
        Check(records.Length == 12 && records.All(x => x.fOrbitW == 0), "Expected the exact original twelve fixed Mercury wrecks.");
        var update = AccessTools.Method(typeof(BodyOrbit), nameof(BodyOrbit.UpdateTime));
        engine.Unpatch(update, HarmonyPatchType.Prefix, engine.Id);
        var production = new Harmony("offline.docking-orbit.saved-mercury");
        var assembly = typeof(Ostrabus.OstrabusPlugin).Assembly;
        foreach (string name in new[] { "PassiveDockAttachmentPatch", "PassiveUndockAttachmentPatch", "PassiveAttachmentInputPatch" })
            production.CreateClassProcessor(assembly.GetType("Ostrabus.ShipNavigation." + name, true)).Patch();
        try
        {
            foreach (var record in records)
            foreach (bool reverse in new[] { false, true })
            foreach (bool wreckDeparts in new[] { false, true })
            foreach (bool appliedMovement in new[] { false, true })
            {
                World();
                var world = CrewSim.system;
                world.aBOs.Clear();
                foreach (var body in current.aBOs) world.aBOs[body.strName] = new BodyOrbit(body);
                world.boStar = world.aBOs["Sol"];
                foreach (var body in world.aBOs.Values) if (body != world.boStar) body.boParent = world.boStar;
                foreach (string link in current.dictBOHierarchy)
                {
                    var parts = link.Split('=');
                    if (parts.Length == 2 && world.aBOs.TryGetValue(parts[0], out var child) && world.aBOs.TryGetValue(parts[1], out var parent))
                        child.boParent = parent;
                }
                StarSystem.fEpoch = current.dfEpoch;
                nearestOverride = world.aBOs["Mercury"];
                var wreck = Hull("WRECK");
                var visitor = Hull("VISITOR");
                var pose = new StellarDerelict(record).objSS;
                pose.UpdateTime(StarSystem.fEpoch);
                Field(wreck, "<objSS>k__BackingField", pose);
                wreck.DMGStatus = Ship.Damage.Derelict;
                var expected = new ShipSitu(pose, false);
                visitor.objSS.vPosx = pose.vPosx + 1e-8;
                visitor.objSS.vPosy = pose.vPosy;
                visitor.objSS.vVelX = pose.vVelX;
                visitor.objSS.vVelY = pose.vVelY;
                if (reverse) wreck.Dock(visitor, false, "VISITOR-port", "WRECK-port");
                else visitor.Dock(wreck, false, "WRECK-port", "VISITOR-port");
                Check(wreck.objSS.bBOLocked && wreck.objSS.strBOPORShip == "HQCH_DET" && wreck.DockGroup.Anchor.bBOLocked,
                    "Actual Mercury placement lost its attachment during docking: " + record.strID);
                const double skip = 7200;
                StarSystem.fEpoch += skip;
                wreck.objSS.UpdateTime(StarSystem.fEpoch);
                expected.UpdateTime(StarSystem.fEpoch);
                Field(world, "_dockingGroups", new List<Ship> { visitor, wreck });
                Field(world, "_updatedGroups", new HashSet<string>());
                AccessTools.Method(typeof(StarSystem), "UpdateDockingGroups").Invoke(world, new object[] { skip });
                Check((wreck.objSS.vPos - expected.vPos).magnitude * 149597872d < .001,
                    "Actual Mercury dock group departed its attached trajectory during a two-hour step.");
                if (appliedMovement)
                {
                    var helper = assembly.GetType("Ostrabus.ShipNavigation.PassiveAttachment", true);
                    Check((bool)AccessTools.Method(helper, "Release").Invoke(null, new object[] { wreck.DockGroup }),
                        "Applied movement could not release the real Mercury placement.");
                    wreck.objSS.vVelX += 1e-6;
                }
                if (wreckDeparts) wreck.Undock(visitor); else visitor.Undock(wreck);
                if (appliedMovement)
                    Check(!wreck.objSS.bBOLocked, "Undocking forcibly restored an intentionally released attachment.");
                else
                {
                    Check(wreck.objSS.bBOLocked && wreck.objSS.strBOPORShip == "HQCH_DET",
                        "Actual Mercury placement reattached to the planet during undocking: " + record.strID);
                    Check(wreck.objSS.vBOOffsetx == record.vBOOffsetx && wreck.objSS.vBOOffsety == record.vBOOffsety && wreck.objSS.fOrbitW == 0,
                        "Actual Mercury passive visit altered historical offsets or orbital rate.");
                    Check((wreck.objSS.vPos - expected.vPos).magnitude * 149597872d < .001,
                        "Actual Mercury passive departure altered position.");
                    var reloaded = new ShipSitu(LitJson.JsonMapper.ToObject<JsonShipSitu>(LitJson.JsonMapper.ToJson(wreck.objSS.GetJSON())));
                    reloaded.UpdateTime(StarSystem.fEpoch);
                    Check(reloaded.strBOPORShip == "HQCH_DET" && reloaded.bBOLocked &&
                        (reloaded.vPos - expected.vPos).magnitude * 149597872d < .001,
                        "Actual Mercury attachment failed native serialization after undocking.");
                }
                Check(!visitor.objSS.bBOLocked && !visitor.IsDocked() && !wreck.IsDocked(),
                    "Actual Mercury departure retained docking links or attached the visitor.");
            }
            Console.WriteLine("Saved Mercury docking: " + (checks - before) + " checks passed for all twelve original placements, both docking/departure directions, two-hour group following, native round trips and deliberate movement release.");
        }
        finally
        {
            nearestOverride = null;
            production.UnpatchSelf();
            engine.Patch(update, prefix: new HarmonyMethod(typeof(Program), nameof(Skip)));
        }
    }
}
