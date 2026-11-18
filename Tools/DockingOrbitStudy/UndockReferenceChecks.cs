using System;
using HarmonyLib;

static partial class Program
{
    static bool FreezeUndockFixtureBody() => false;
    // Deliberately make the nearest natural body DIFFERENT from the retained
    // reference. The earlier Phobos fixture hid Undock's reference replacement
    // by supplying Phobos for both roles.
    static void UndockReferenceChecks()
    {
        int before = checks;
        var plugin = typeof(Ostrabus.OstrabusPlugin).Assembly;
        var patch = plugin.GetType("Ostrabus.ShipNavigation.PassiveUndockAttachmentPatch", true);
        var candidate = new Harmony("offline.docking-orbit.undock-reference");
        // World() supplies fixed synthetic body coordinates and velocities, not
        // orbital elements. Earlier physics studies remove the main body's stub;
        // restore this fixture boundary explicitly instead of letting native
        // zero-period NaNs make the missing-reference fallback pass by accident.
        candidate.Patch(AccessTools.Method(typeof(BodyOrbit), nameof(BodyOrbit.UpdateTime)),
            prefix: new HarmonyMethod(typeof(Program), nameof(FreezeUndockFixtureBody)));
        candidate.CreateClassProcessor(plugin.GetType("Ostrabus.ShipNavigation.PassiveDockAttachmentPatch", true)).Patch();
        try
        {
            foreach (bool repaired in new[] { false, true })
            {
                if (repaired) candidate.CreateClassProcessor(patch).Patch();
                foreach (string reference in new[] { "HQCH", "HQCH_DET", "Phobos" })
                foreach (bool reverse in new[] { false, true })
                foreach (bool departFromWreck in new[] { false, true })
                foreach (bool released in new[] { false, true })
                foreach (bool circular in new[] { false, true })
                {
                    World();
                    var old = CrewSim.system.aBOs["Phobos"];
                    var natural = Empty<BodyOrbit>();
                    natural.strName = reference.StartsWith("HQCH",StringComparison.Ordinal) ? "Mercury" : "Mars";
                    natural.dXReal = old.dXReal; natural.dYReal = old.dYReal;
                    natural.dVelX = old.dVelX; natural.dVelY = old.dVelY;
                    natural.dTimeCalcLast = StarSystem.fEpoch;
                    CrewSim.system.aBOs[natural.strName] = nearestOverride = natural;
                    if (reference != "Phobos")
                    {
                        old.strName = reference;
                        CrewSim.system.aBOs.Remove("Phobos");
                        CrewSim.system.aBOs[reference] = old;
                    }
                    var visitor = Hull("VISITOR");
                    var wreck = Hull("WRECK");
                    wreck.DMGStatus = Ship.Damage.Derelict;
                    if (circular) wreck.objSS.LockToBOOrbit(old); else wreck.objSS.LockToBO(old);
                    visitor.objSS.vVelX = old.dVelX; visitor.objSS.vVelY = old.dVelY;
                    if (reverse) wreck.Dock(visitor, false, "VISITOR-port", "WRECK-port");
                    else visitor.Dock(wreck, false, "WRECK-port", "VISITOR-port");
                    Check(wreck.objSS.bBOLocked && wreck.objSS.strBOPORShip == reference,
                        "Docking itself lost the original attachment");
                    if (released)
                    {
                        var helper = plugin.GetType("Ostrabus.ShipNavigation.PassiveAttachment", true);
                        Check((bool)AccessTools.Method(helper, "Release").Invoke(null, new object[] { wreck.DockGroup }),
                            "Actual release failed before departure");
                        // Too fast for native relock: the repair must not revive
                        // the former lock or bypass the normal speed limit.
                        wreck.objSS.vVelX += 1e-6;
                    }
                    double dx = wreck.objSS.vBOOffsetx, dy = wreck.objSS.vBOOffsety;
                    double rate = wreck.objSS.fOrbitW, phase = wreck.objSS.fOrbitTimeLast;
                    if (departFromWreck) wreck.Undock(visitor); else visitor.Undock(wreck);
                    if (released)
                        Check(!wreck.objSS.bBOLocked, "Released moving wreck was forcibly reattached");
                    else
                    {
                        string expected = repaired ? reference : natural.strName;
                        Check(wreck.objSS.bBOLocked && wreck.objSS.strBOPORShip == expected,
                            "Passive undock reference mismatch; repaired=" + repaired);
                        if (repaired)
                        {
                            Check(wreck.objSS.vBOOffsetx == dx && wreck.objSS.vBOOffsety == dy,
                                "Undocking recalculated the retained attachment offset");
                            Check(wreck.objSS.fOrbitW == rate && wreck.objSS.fOrbitTimeLast == phase,
                                "Undocking recalculated the retained circular attachment phase");
                        }
                    }
                    Check(!visitor.objSS.bBOLocked && !visitor.IsDocked() && !wreck.IsDocked(),
                        "Visitor departure acquired an attachment or left links behind");
                }
            }
            // A broken reference does not justify preserving its lock flag.
            World();
            var point = new ShipSitu(false) { bBOLocked = true, strBOPORShip = "missing", vPosx = 1, vPosy = 2,
                vVelX = 1e-8, vVelY = 2e-8 };
            var keep = AccessTools.Method(plugin.GetType("Ostrabus.ShipNavigation.PassiveAttachment", true), "UndockRelock");
            Check((bool)keep.Invoke(null, new object[] { point, CrewSim.system.aBOs["Phobos"] }) &&
                point.strBOPORShip == "Phobos", "Missing reference prevented native fallback relocking");
            Console.WriteLine("Undock reference A/B: " + (checks-before) + " checks passed; original HQCH/Mercury and Phobos/Mars mismatch reproduced.");
        }
        finally { nearestOverride = null; candidate.UnpatchSelf(); }
    }
}
