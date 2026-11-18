using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using SolarSystem;
using UnityEngine;
using Ostranauts.Core;
using Ostranauts.Ships.Commands;
using Ostranauts.ShipGUIs.Utilities;
using Ostranauts.Ships.AIPilots;

static partial class Program
{
    static int checks;
    static CondOwner selected;
    static readonly List<string> transitions = new List<string>();
    static BodyOrbit nearestOverride;
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Field(object target, string name, object value) => AccessTools.Field(target.GetType(), name).SetValue(target, value);
    static void Check(bool valid, string message)
    {
        checks++;
        if (!valid)
            throw new Exception(message);
    }
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result)
    {
        __result = ReferenceEquals(__0, __1);
        return false;
    }
    static bool Unequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result)
    {
        __result = !ReferenceEquals(__0, __1);
        return false;
    }
    static bool Mass(ref double __result)
    {
        __result = 1000;
        return false;
    }
    static bool Selected(ref CondOwner __result)
    {
        __result = selected;
        return false;
    }
    static bool Skip() => false;
    static bool Gravity() => false;
    static bool Nearest(ref BodyOrbit __result)
    {
        __result = nearestOverride ?? CrewSim.system.aBOs["Phobos"];
        return false;
    }
    static bool NoPilot(ref bool __result)
    {
        __result = false;
        return false;
    }
    static bool NoSff(ref bool __result)
    {
        __result = false;
        return false;
    }
    static bool NoController(ref AIShip __result)
    {
        __result = null;
        return false;
    }
    static bool String(string strName, ref string __result)
    {
        __result = strName;
        return false;
    }
    static void DockEnter(Ship __instance)
    {
        transitions.Add("dock-enter " + __instance.strRegID + " lock=" + __instance.objSS.bBOLocked);
    }
    static void DockExit(Ship __instance)
    {
        transitions.Add("dock-exit " + __instance.strRegID + " lock=" + __instance.objSS.bBOLocked + " anchor=" + __instance.DockGroup?.Anchor?.bBOLocked);
    }
    static Ship Hull(string id, bool locked = false)
    {
        var ship = Empty<Ship>();
        ship.strRegID = id;
        ship.json = new JsonShip { aItems = new[] { new JsonItem { strID = id + "-port", fRotation = 0 } }, aDockingPorts = new[] { id + "-port" } };
        Field(ship, "nLoadState", Ship.Loaded.Shallow);
        Field(ship, "aDocked", new Dictionary<string, Ship>());
        Field(ship, "aPeople", new List<PersonSpec>());
        Field(ship, "aNavs", new List<CondOwner>());
        Field(ship, "aCores", new List<CondOwner>());
        ship.aWPs = new List<WaypointShip>();
        Field(ship, "aDockingPorts", new List<string> { id + "-port" });
        Field(ship, "mapICOs", Activator.CreateInstance(AccessTools.Field(typeof(Ship), "mapICOs").FieldType));
        Field(ship, "<objSS>k__BackingField", new ShipSitu(false) { Size = 100, bIgnoreGrav = true, vPosx = 1 + 1e-6, vPosy = 2, bBOLocked = locked, strBOPORShip = locked ? "Phobos" : null, vBOOffsetx = 1e-6, vBOOffsety = 0 });
        CrewSim.system.dictShips[id] = ship;
        return ship;
    }
    static void World()
    {
        nearestOverride = null;
        selected = null;
        CrewSim.system = Empty<StarSystem>();
        CrewSim.coPlayer = null;
        CrewSim.system.dictShips = new Dictionary<string, Ship>();
        CrewSim.system.aBOs = new Dictionary<string, BodyOrbit>();
        var body = Empty<BodyOrbit>();
        body.strName = "Phobos";
        body.dXReal = 1;
        body.dYReal = 2;
        body.dTimeCalcLast = 1000;
        body.dVelX = 1e-8;
        body.dVelY = 2e-8;
        body.fMass = 1.07e16;
        CrewSim.system.aBOs[body.strName] = body;
        StarSystem.fEpoch = 1000;
    }
    static void Baseline()
    {
        World();
        transitions.Clear();
        var visitor = Hull("VISITOR");
        var wreck = Hull("WRECK", true);
        wreck.DMGStatus = Ship.Damage.Derelict;
        visitor.objSS.vVelX = wreck.objSS.vVelX = 1e-8;
        visitor.objSS.vVelY = wreck.objSS.vVelY = 2e-8;
        visitor.Dock(wreck, false, "WRECK-port", "VISITOR-port");
        foreach (string row in transitions)
            Console.WriteLine(row);
        Check(!wreck.objSS.bBOLocked, "Native control did not reproduce lock loss.");
        Check(visitor.DockGroup != null && ReferenceEquals(visitor.DockGroup, wreck.DockGroup), "Native docking did not create a shared group.");
        Check(!wreck.DockGroup.Anchor.bBOLocked, "Native reciprocal docking retained anchor lock unexpectedly.");
        Console.WriteLine("REPRODUCED: native Ship.Dock loses a passive wreck lock and group anchor lock.");
    }
    static void GravityInheritance()
    {
        World();
        var visitor = Hull("VISITOR");
        var wreck = Hull("WRECK", true);
        wreck.objSS.bIgnoreGrav = false;
        visitor.objSS.bIgnoreGrav = false;
        visitor.Dock(wreck, false, "WRECK-port", "VISITOR-port");
        Check(!visitor.DockGroup.Anchor.bIgnoreGrav, "Initial docking unexpectedly disabled group gravity.");
        var hold = Empty<Hold>();
        Field(hold, "_shipUs", visitor);
        visitor.fAIDockingExpire = 600;
        hold.RunCommand(new Blackboard());
        Check(visitor.objSS.bIgnoreGrav, "Native Hold did not set the civilian's gravity-ignore flag.");
        Check(!visitor.DockGroup.Anchor.bIgnoreGrav, "Hold alone immediately changed the original group anchor.");
        var moorer = Hull("PLAYER");
        moorer.objSS.bIgnoreGrav = false;
        selected = Empty<CondOwner>();
        Field(selected, "_objShip", moorer);
        moorer.MoorTo(wreck, "MP|target", "MP|I|incoming");
        Check(!wreck.DockGroup.Anchor.bIgnoreGrav, "Player-led tethered group did not retain physical gravity.");
        moorer.UnMoorFrom(wreck);
        var group = wreck.DockGroup;
        Check(group != null && group.Anchor.bIgnoreGrav, "Native unmooring failed to reproduce gravity-ignore inheritance from the remaining civilian.");
        Check(!wreck.objSS.bIgnoreGrav, "The wreck's own flag changed; this is not the isolated anchor-copy defect.");
        double initial = group.Anchor.vVelX;
        group.Anchor.vAccEx = new Vector2(5e-12f, 0);
        group.TimeAdvance(100);
        Check(group.Anchor.vVelX == initial, "Gravity-disabled group did not coast with unchanged velocity.");
        group.Anchor.bIgnoreGrav = false;
        group.TimeAdvance(100);
        Check(Math.Abs(group.Anchor.vVelX - initial - 5e-10) < 1e-14, "Re-enabling only group gravity did not restore the expected acceleration.");
        Console.WriteLine("REPRODUCED: native Hold then player tether/unmoor copies gravity-ignore into the rebuilt group; velocity stops responding to gravity.");
    }
    static (Ship visitor, Ship wreck) Pair(bool reverse = false)
    {
        World();
        var visitor = Hull("VISITOR");
        var wreck = Hull("WRECK", true);
        wreck.DMGStatus = Ship.Damage.Derelict;
        visitor.objSS.vVelX = wreck.objSS.vVelX = 1e-8;
        visitor.objSS.vVelY = wreck.objSS.vVelY = 2e-8;
        if (reverse)
            wreck.Dock(visitor, false, "VISITOR-port", "WRECK-port");
        else
            visitor.Dock(wreck, false, "WRECK-port", "VISITOR-port");
        return (visitor, wreck);
    }
    static void Follow(Ship visitor, Ship wreck)
    {
        var group = wreck.DockGroup;
        var body = CrewSim.system.aBOs["Phobos"];
        double dx = group.Anchor.vPosx - body.dXReal, dy = group.Anchor.vPosy - body.dYReal;
        // Supply an independently moved body cache at the next native world step.
        body.dXReal += 2e-7;
        body.dYReal += 3e-7;
        body.dVelX += 3e-9;
        body.dVelY -= 2e-9;
        body.dTimeCalcLast = StarSystem.fEpoch += 600;
        visitor.objSS.TimeAdvance(600);
        wreck.objSS.TimeAdvance(600);
        group.TimeAdvance(600);
        Check(Math.Abs(group.Anchor.vPosx - body.dXReal - dx) < 1e-13 && Math.Abs(group.Anchor.vPosy - body.dYReal - dy) < 1e-13, "Passive group lost its body-relative offset.");
        Check(Math.Abs(visitor.objSS.vVelX - body.dVelX) < 1e-15 && Math.Abs(visitor.objSS.vVelY - body.dVelY) < 1e-15, "Visitor did not follow the anchored group velocity.");
    }
    static void Candidate(Harmony engine)
    {
        var candidate = new Harmony("offline.docking-orbit.candidate");
        candidate.CreateClassProcessor(typeof(Ostrabus.OstrabusPlugin).Assembly.GetType("Ostrabus.ShipNavigation.PassiveDockAttachmentPatch", true)).Patch();
        candidate.CreateClassProcessor(typeof(Ostrabus.OstrabusPlugin).Assembly.GetType("Ostrabus.ShipNavigation.PassiveUndockAttachmentPatch", true)).Patch();
        try
        {
            foreach (bool reverse in new[] { false, true })
            {
                var (visitor, wreck) = Pair(reverse);
                Check(wreck.objSS.bBOLocked && wreck.DockGroup.Anchor.bBOLocked, "Candidate failed to preserve the existing wreck anchor.");
                Follow(visitor, wreck);
                visitor.StopManeuver(false, false);
                Check(wreck.DockGroup.Anchor.bBOLocked, "A zero-thrust stop released the anchor.");
                // Preserving the anchor alone traps a legitimate towing command.
                visitor.objSS.vAccRCS = new Vector2(1e-10f, 0);
                visitor.UpdateDockedShips(visitor.objSS);
                Check(wreck.DockGroup.Anchor.bBOLocked, "Native input unexpectedly released a soft anchor.");
                visitor.objSS.vAccRCS = Vector2.zero;
                visitor.Undock(wreck);
                Check(wreck.objSS.bBOLocked && wreck.objSS.strBOPORShip == "Phobos", "Native detach lost the preserved reference.");
                Check(!visitor.objSS.bBOLocked && !visitor.IsDocked(), "Native detach incorrectly anchored the departing visitor.");
            }
            Console.WriteLine("REPRODUCED: lock preservation alone traps real native group input.");
            candidate.CreateClassProcessor(typeof(Ostrabus.OstrabusPlugin).Assembly.GetType("Ostrabus.ShipNavigation.PassiveAttachmentInputPatch", true)).Patch();
            {
                var (visitor, wreck) = Pair();
                var policy = typeof(Ostrabus.OstrabusPlugin).Assembly.GetType("Ostrabus.ShipNavigation.StationDockedCollisions", true);
                Check(!(bool)AccessTools.Method(policy, "IsExempt").Invoke(null, new object[] { visitor }),
                    "A body-locked wreck visit was incorrectly classified as station-docked collision immunity.");
                Check(!(bool)AccessTools.Method(policy, "IsExempt").Invoke(null, new object[] { wreck }),
                    "An authored wreck attachment was incorrectly classified as an actual station.");
            }
            foreach (bool torch in new[] { false, true })
            {
                var (visitor, wreck) = Pair();
                var group = wreck.DockGroup;
                visitor.StopManeuver(false, false);
                Check(group.Anchor.bBOLocked, "Zero-input Hold/Stop released a repaired anchor.");
                if (torch)
                    visitor.objSS.vAccIn = new Vector2(1e-10f, 0);
                else
                    visitor.objSS.vAccRCS = new Vector2(1e-10f, 0);
                double velocity = group.Anchor.vVelX;
                visitor.UpdateDockedShips(visitor.objSS);
                Check(!group.Anchor.bBOLocked && !wreck.objSS.bBOLocked, "Applied propulsion did not release the entire soft-locked group.");
                group.TimeAdvance(10);
                Check(Math.Abs((group.Anchor.vVelX - velocity) - 1e-9) < 1e-14, "Released group lost or misapplied the initiating thrust command.");
                group.UpdateAnchor();
                Check(!group.Anchor.bBOLocked, "A member resurrected the old lock after thrust.");
            }
            {
                var (visitor, wreck) = Pair();
                var group = wreck.DockGroup;
                visitor.objSS.fA = .01f;
                visitor.UpdateDockedShips(visitor.objSS);
                Check(!group.Anchor.bBOLocked && !wreck.objSS.bBOLocked, "An active attitude maneuver remained artificially frozen.");
                Check(group.Anchor.fA == .01f, "Initiating attitude input was lost during release.");
            }
            {
                var (visitor, wreck) = Pair();
                // HaulShip.RunCommand explicitly clears the attached wreck's
                // member flag before issuing real steering; its anchor persists.
                wreck.objSS.bBOLocked = false;
                visitor.objSS.vAccRCS = new Vector2(1e-10f, 0);
                visitor.UpdateDockedShips(visitor.objSS);
                Check(!visitor.DockGroup.Anchor.bBOLocked, "Native hauler release remained trapped by the old group anchor.");
            }
            {
                var (visitor, wreck) = Pair();
                var third = Hull("THIRD", true);
                var group = visitor.DockGroup;
                group.AddShip(third);
                visitor.objSS.vAccRCS = new Vector2(1e-10f, 0);
                visitor.UpdateDockedShips(visitor.objSS);
                Check(!group.Anchor.bBOLocked && !wreck.objSS.bBOLocked && !third.objSS.bBOLocked,
                    "Applied control left an attached third member locked.");
                group.UpdateAnchor();
                Check(!group.Anchor.bBOLocked, "Three-member reconstruction resurrected a released lock.");
            }
            {
                var (visitor, wreck) = Pair();
                visitor.objSS.fW = .2f;
                visitor.objSS.fA = 0;
                visitor.UpdateDockedShips(visitor.objSS);
                Check(wreck.objSS.bBOLocked && visitor.DockGroup.Anchor.bBOLocked,
                    "Residual spin without applied angular control released the attachment.");
            }
            {
                var (visitor, wreck) = Pair();
                wreck.objSS.bIsBO = true;
                StationControl(visitor, wreck);
            }
            {
                World();
                var visitor = Hull("VISITOR");
                var station = Hull("STATION", true);
                station.objSS.bIsBO = true;
                visitor.Dock(station, false, "STATION-port", "VISITOR-port");
                Check(station.objSS.bIsBO && visitor.DockGroup.Anchor.bIsBO, "Native station docking lost its fixed anchor.");
                StationControl(visitor, station);
            }
            {
                World();
                var visitor = Hull("VISITOR");
                var wreck = Hull("WRECK", true);
                var body = CrewSim.system.aBOs["Phobos"];
                wreck.objSS.LockToBOOrbit(body);
                double rate = wreck.objSS.fOrbitW;
                visitor.Dock(wreck, false, "WRECK-port", "VISITOR-port");
                Check(wreck.objSS.fOrbitW == rate && wreck.DockGroup.Anchor.fOrbitW == rate && rate > 0, "Circular orbital attachment was flattened to a stationary body offset.");
                double future = StarSystem.fEpoch + 600;
                wreck.objSS.GetBOOffsetPrediction(future, out double x, out double y);
                body.dTimeCalcLast = StarSystem.fEpoch = future;
                visitor.objSS.TimeAdvance(600);
                wreck.objSS.TimeAdvance(600);
                visitor.DockGroup.TimeAdvance(600);
                Check(Math.Abs(wreck.objSS.vPosx - body.dXReal - x) < 1e-13 && Math.Abs(wreck.objSS.vPosy - body.dYReal - y) < 1e-13, "Circular orbit phase stopped advancing during the visit.");
                double velocity = wreck.objSS.vVelX;
                visitor.objSS.vAccRCS = new Vector2(1e-10f, 0);
                visitor.UpdateDockedShips(visitor.objSS);
                Check(!wreck.objSS.bBOLocked && wreck.objSS.fOrbitW == 0, "Deliberate thrust failed to release a circular body attachment.");
                Check(Math.Abs(visitor.DockGroup.Anchor.vVelX - velocity) < 1e-14, "Orbital tangential momentum was lost during release.");
            }
            {
                var (visitor, wreck) = Pair();
                var rock = Hull("ROCK", true);
                rock.Classification = Ship.TypeClassification.Asteroid;
                visitor.DockGroup.AddShip(rock, true);
                visitor.objSS.vAccRCS = new Vector2(1e-10f, 0);
                visitor.UpdateDockedShips(visitor.objSS);
                Check(visitor.DockGroup.Anchor.bBOLocked, "A mooring/fixed-body group was released.");
            }
            {
                var (visitor, wreck) = Pair();
                wreck.objSS.bGrounded = true;
                visitor.objSS.vAccRCS = new Vector2(1e-10f, 0);
                visitor.UpdateDockedShips(visitor.objSS);
                Check(wreck.DockGroup.Anchor.bBOLocked, "Grounded group was released by visitor input.");
            }
            {
                var (visitor, wreck) = Pair();
                var moorer = Hull("MOORER");
                moorer.MoorTo(wreck, "MP|target", "MP|I|incoming");
                Check(wreck.objSS.bBOLocked && wreck.DockGroup.Anchor.bBOLocked, "Tether arrival lost the passive wreck anchor.");
                moorer.UnMoorFrom(wreck);
                Check(wreck.objSS.bBOLocked && wreck.DockGroup.Anchor.bBOLocked, "Tether departure released the remaining visiting pair.");
                visitor.Undock(wreck);
                Check(wreck.objSS.bBOLocked, "Final visitor departure failed native relock after tether use.");
            }
            {
                var (visitor, wreck) = Pair();
                var first = visitor.objSS.GetJSON();
                var second = wreck.objSS.GetJSON();
                Field(visitor, "<objSS>k__BackingField", new ShipSitu(first));
                Field(wreck, "<objSS>k__BackingField", new ShipSitu(second));
                var restored = new DockGroup();
                restored.AddShip(visitor);
                restored.AddShip(wreck);
                Check(restored.Anchor.bBOLocked && restored.Anchor.strBOPORShip == "Phobos", "Native saved situations/rebuilt group lost the reference.");
                Follow(visitor, wreck);
            }
            {
                var (visitor, wreck) = Pair();
                var simulation = typeof(Ostrabus.OstrabusPlugin).Assembly.GetType("Ostrabus.Orbital.SffSimulation", true);
                var orbital = typeof(Ostrabus.OstrabusPlugin).Assembly.GetType("Ostrabus.Orbital.OrbitalSimulation", true);
                var enabled = AccessTools.Property(orbital, "Enabled");
                bool previousEnabled = (bool)enabled.GetValue(null);
                enabled.SetValue(null, true);
                object capture = AccessTools.Method(simulation, "Begin").Invoke(null, new object[] { CrewSim.system, 7200d, true });
                Check(capture != null, "SFF capture was unavailable in the fixture.");
                try
                {
                    Check((int)AccessTools.Field(simulation, "excludedLocks").GetValue(capture) == 1, "Existing SFF replacement did not defer the fixed group to native body following.");
                }
                finally { ((IDisposable)capture).Dispose(); enabled.SetValue(null, previousEnabled); }
            }
            Console.WriteLine("Candidate: preservation, passive following, thrust/attitude release and fixed-body exclusions pass.");
            CourseChecks(candidate);
        }
        finally { candidate.UnpatchSelf(); }
    }
    static void StationControl(Ship visitor, Ship station)
    {
        var group = visitor.DockGroup;
        group.UpdateAnchor();
        visitor.objSS.vAccRCS = new Vector2(1e-10f, 0);
        visitor.UpdateDockedShips(visitor.objSS);
        Check(group.Anchor.bIsBO, "Actual station anchor was released by visitor thrust.");
    }
    static NavData Course(Ship owner)
    {
        var nav = new NavData(owner);
        var first = new ShipSitu(owner.objSS, false);
        first._dockGroup = null;
        first.bBOLocked = false;
        first.bOrbitLocked = false;
        var last = new ShipSitu(first, false);
        last.vPosx += 1.25e-6;
        last.vPosy += 2e-6;
        last.vVelX += 5e-9;
        first.vAccRCS = last.vAccRCS = new Vector2(5e-11f, 0);
        nav.AddNavDataPoint(new NavDataPoint(1000, first, 100, 0, 100000, rcsCount: 0F));
        nav.AddNavDataPoint(new NavDataPoint(1100, last, 100, 0, 100000, rcsCount: 0F));
        owner.objSS.NavData = nav;
        return nav;
    }
    static void CourseChecks(Harmony candidate)
    {
        var production = new Harmony("offline.docking-orbit.existing-tow");
        try
        {
            var plugin = typeof(Ostrabus.OstrabusPlugin).Assembly;
            production.CreateClassProcessor(plugin.GetType("Ostrabus.ShipNavigation.RouteCompletionPatch", true)).Patch();
            foreach (string name in new[] { "DockedCourseMemberPatch", "DockedCourseGroupPatch" })
                production.CreateClassProcessor(plugin.GetType("Ostrabus.ShipNavigation.Towing." + name, true)).Patch();
            var release = AccessTools.Method(plugin.GetType("Ostrabus.ShipNavigation.Towing.DockedCoursePlayback", true), "ReleaseAttachmentForCourse");
            foreach (string refusal in new[] { "competing", "future", "wrong-owner", "anchor-route", "station" })
            {
                var (visitor, wreck) = Pair();
                var course = Course(visitor);
                if (refusal == "competing") Course(wreck);
                if (refusal == "future") StarSystem.fEpoch = 999;
                if (refusal == "wrong-owner") Field(course, "_shipUs", wreck);
                if (refusal == "anchor-route") visitor.DockGroup.Anchor.NavData = course;
                if (refusal == "station") visitor.DockGroup.Anchor.bIsBO = true;
                release.Invoke(null, new object[] { visitor.DockGroup });
                Check(visitor.DockGroup.Anchor.bBOLocked && wreck.objSS.bBOLocked, "Course refusal released attachment: " + refusal);
                Check(visitor.objSS.NavData == course, "Course refusal silently discarded another owner's plan: " + refusal);
            }
            {
                var (visitor, wreck) = Pair();
                var course = Course(visitor);
                StarSystem.fEpoch = 1050;
                visitor.objSS.TimeAdvance(50);
                visitor.DockGroup.TimeAdvance(50);
                Check(!visitor.DockGroup.Anchor.bBOLocked, "Production tow playback failed to release a soft anchor.");
                Check(Math.Abs(visitor.objSS.vPosx - (course.Origin.ObjSS.vPosx + 6.25e-7)) < 1e-12, "Production tow playback lost the engaged course.");
            }
            foreach (bool groupOnly in new[] { false, true })
            {
                var (visitor, wreck) = Pair();
                var course = Course(visitor);
                StarSystem.fEpoch = 1050;
                if (!groupOnly)
                    visitor.objSS.TimeAdvance(50);
                visitor.DockGroup.TimeAdvance(50);
                Check(!visitor.DockGroup.Anchor.bBOLocked && !wreck.objSS.bBOLocked, "Engaged course did not release the passive attachment.");
                Check(Math.Abs(visitor.objSS.vPosx - (course.Origin.ObjSS.vPosx + 6.25e-7)) < 1e-12, "Existing production playback lost the released course position.");
                Check(Math.Abs(visitor.objSS.vVelX - (course.Origin.ObjSS.vVelX + 2.5e-9)) < 1e-14, "Existing production playback lost the released course velocity.");
            }
            foreach (bool groupOnly in new[] { false, true })
            {
                var (visitor, wreck) = Pair();
                var course = Course(visitor);
                StarSystem.fEpoch = 1300;
                if (!groupOnly)
                    visitor.objSS.TimeAdvance(300);
                visitor.DockGroup.TimeAdvance(300);
                Check(visitor.objSS.NavData == null && !wreck.objSS.bBOLocked, "A first skip beyond arrival could not finish and release the pending course.");
                double expected = course.Destination.ObjSS.vPosx + course.Destination.ObjSS.vVelX * 200;
                Check(Math.Abs(visitor.objSS.vPosx - expected) < 1e-12, "Attachment release broke completed-route remainder coasting.");
                Check(visitor.DockGroup.Anchor.vAccRCS == Vector2.zero && visitor.DockGroup.Anchor.vAccIn == Vector2.zero, "Completed route resurrected propulsion after attachment release.");
            }
            {
                var (visitor, wreck) = Pair();
                var course = Course(visitor);
                course.IsTorching = true;
                release.Invoke(null, new object[] { visitor.DockGroup });
                Check(wreck.objSS.bBOLocked, "Unbraced torch course released the passive attachment.");
                Check(visitor.objSS.NavData == null, "Unbraced course was left running against a locked anchor.");
                course = Course(visitor);
                course.IsTorching = true;
                Field(visitor, "aSecuredTowBraces", new Dictionary<string, string> { { "brace", "VISITOR-port" } });
                release.Invoke(null, new object[] { visitor.DockGroup });
                Check(!wreck.objSS.bBOLocked, "Braced torch course could not release the passive attachment.");
            }
            Console.WriteLine("Candidate: actual existing tow playback retains native course pose after deliberate release, including group-only stepping.");
        }
        finally { production.UnpatchSelf(); }
    }
    static int Main(string[] args)
    {
        var engine = new Harmony("offline.docking-orbit.environment");
        var oldWorld = CrewSim.system;
        var player = CrewSim.coPlayer;
        double epoch = StarSystem.fEpoch;
        try
        {
            engine.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Equality"), prefix: new HarmonyMethod(typeof(Program), nameof(Equal)));
            engine.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Inequality"), prefix: new HarmonyMethod(typeof(Program), nameof(Unequal)));
            engine.Patch(AccessTools.PropertyGetter(typeof(Ship), nameof(Ship.Mass)), prefix: new HarmonyMethod(typeof(Program), nameof(Mass)));
            engine.Patch(AccessTools.PropertySetter(typeof(Ship), nameof(Ship.Gravity)), prefix: new HarmonyMethod(typeof(Program), nameof(Gravity)));
            engine.Patch(AccessTools.PropertyGetter(typeof(GUIFFWD), nameof(GUIFFWD.Active)), prefix: new HarmonyMethod(typeof(Program), nameof(NoSff)));
            engine.Patch(AccessTools.Method(typeof(AIShipManager), nameof(AIShipManager.GetAIShipByRegID)), prefix: new HarmonyMethod(typeof(Program), nameof(NoController)));
            engine.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.ClearShipTarget)), prefix: new HarmonyMethod(typeof(Program), nameof(Skip)));
            engine.Patch(AccessTools.Method(typeof(Debug), nameof(Debug.LogWarning), new[] { typeof(object) }), prefix: new HarmonyMethod(typeof(Program), nameof(Skip)));
            engine.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.AccrueTowGroupLoad)), prefix: new HarmonyMethod(typeof(Program), nameof(Skip)));
            engine.Patch(AccessTools.Method(typeof(CrewSim), nameof(CrewSim.GetSelectedCrew)), prefix: new HarmonyMethod(typeof(Program), nameof(Selected)));
            engine.Patch(AccessTools.Method(typeof(StarSystem), nameof(StarSystem.GetNearestBO), new[] { typeof(ShipSitu), typeof(double), typeof(bool) }), prefix: new HarmonyMethod(typeof(Program), nameof(Nearest)));
            engine.Patch(AccessTools.PropertyGetter(typeof(Ship), nameof(Ship.NavAIManned)), prefix: new HarmonyMethod(typeof(Program), nameof(NoPilot)));
            engine.Patch(AccessTools.Method(typeof(DataHandler), nameof(DataHandler.GetString)), prefix: new HarmonyMethod(typeof(Program), nameof(String)));
            foreach (var method in AccessTools.GetDeclaredMethods(typeof(Ship)))
                if (method.Name == nameof(Ship.LogAdd) || method.Name == "AddDockingFees" || method.Name == nameof(Ship.CheckAccruedWear))
                    engine.Patch(method, prefix: new HarmonyMethod(typeof(Program), nameof(Skip)));
            engine.Patch(AccessTools.Method(typeof(BodyOrbit), nameof(BodyOrbit.UpdateTime)), prefix: new HarmonyMethod(typeof(Program), nameof(Skip)));
            var dock = AccessTools.Method(typeof(Ship), nameof(Ship.Dock), new[] { typeof(Ship), typeof(bool), typeof(string), typeof(string) });
            engine.Patch(dock, prefix: new HarmonyMethod(typeof(Program), nameof(DockEnter)), postfix: new HarmonyMethod(typeof(Program), nameof(DockExit)));
            if (args.Length == 1 && args[0] == "gravity-modes") {
                GravityModeChecks(engine);
                return 0;
            }
            Baseline();
            GravityInheritance();
            GravityPhysics(engine);
            HoldSafety(engine);
            AttachmentPhysics(engine);
            UndockReferenceChecks();
            Candidate(engine);
            LifecycleChecks(engine);
            CollateralChecks(engine);
            CompletionDependencyChecks();
            Console.WriteLine("Docking orbit study: " + checks + " checks passed.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { engine.UnpatchSelf(); CrewSim.system = oldWorld; CrewSim.coPlayer = player; StarSystem.fEpoch = epoch; }
    }
}
