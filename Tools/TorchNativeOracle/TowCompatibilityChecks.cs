using System;
using System.Collections.Generic;
using HarmonyLib;
using Ostrabus.MiscBugFixes;
using Ostrabus.ShipNavigation.Towing;
using Ostrabus.TorchCalculator;
using Ostranauts.ShipGUIs.Utilities;
using Ostranauts.Ships.Commands;
using Ostranauts.Utils.Models;
using SolarSystem;
using UnityEngine;

static partial class Program {
    static double thrustForce;
    static DockGroup TowGroup() {
        var tow = Empty<Ship>();
        tow.strRegID = "TORCH-TOW";
        Field(tow, "<objSS>k__BackingField", new ShipSitu(false));
        var group = new DockGroup { Anchor = new ShipSitu(false) };
        Field(ship, "aDocked", new Dictionary<string, Ship> { ["tug-port"] = tow });
        Field(tow, "aDocked", new Dictionary<string, Ship> { ["tow-port"] = ship });
        Field(ship, "aSecuredTowBraces", new Dictionary<string, string> { ["brace"] = "tug-port" });
        foreach (var member in new[] { ship, tow }) {
            member.objSS._dockGroup = group;
            member.objSS._dockOffsetPos = new Point(member == ship ? 1e-8 : -1e-8, 0);
            member.objSS._dockOffsetRot = member == ship ? .3f : -.3f;
            group.DockedShips.Add(new DockGroupMember(member, false));
        }
        return group;
    }
    static void RunTowCompatibilityChecks() {
        int before = checks;
        var patches = new Harmony("offline.torch-tow.compatibility");
        try {
            patches.CreateClassProcessor(typeof(DockedCourseMemberPatch)).Patch();
            patches.CreateClassProcessor(typeof(DockedCourseGroupPatch)).Patch();
            patches.CreateClassProcessor(typeof(TorchTowMassPatch)).Patch();
            patches.Patch(AccessTools.PropertySetter(typeof(Ship), nameof(Ship.Gravity)), prefix: new HarmonyMethod(typeof(Program), nameof(Skip)));
            patches.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.AccrueTowGroupLoad)), prefix: new HarmonyMethod(typeof(Program), nameof(Skip)));
            var run = AccessTools.Method(typeof(FusionIC), "Run");
            var burn = AccessTools.Method(typeof(FusionIC), "Fusion");
            foreach (int pairs in new[] { 1, 2, 4 }) {
                var fusion = Make(new ReactorFit { Feeders = pairs, Lasers = pairs }, TorchModel.Ideal, .2, .5);
                playbackReactor = fusion;
                run.Invoke(fusion, new object[] { .27 });
                Values[core]["StatICCoreTemp"] = TorchModel.Ideal;
                burn.Invoke(fusion, new object[] { .27, new JsonPowerInfo(), 0d });
                double force = thrustForce, acceleration = ship.objSS.vAccIn.magnitude;
                double fuel = Read(he3[0], "StatSolidHe3");
                var group = TowGroup();
                double soloAcceleration = ship.GetMaxTorchThrust(.2f) * 2;
                burn.Invoke(fusion, new object[] { .27, new JsonPowerInfo(), 0d });
                Near(thrustForce, force, "Tow mass does not reduce the engine's actual force twice");
                Near(ship.objSS.vAccIn.magnitude, acceleration / 2, "Native engine force accelerates the combined mass");
                Near(ship.GetMaxTorchThrust(.2f), soloAcceleration / 2, "Planning matches native thrust conversion");
                Check(Read(he3[0], "StatSolidHe3") < fuel, "Towing still consumes native reactants.");
                var station = Empty<Ship>(); station.strRegID = "TORCH-MOORING";
                Field(station, "<objSS>k__BackingField", new ShipSitu(false) { bIsBO = true });
                station.objSS._dockGroup = group;
                Field(station, "aDocked", new Dictionary<string, Ship> { ["tug-port"] = ship });
                ((Dictionary<string, Ship>)AccessTools.Field(typeof(Ship), "aDocked").GetValue(ship))["station-port"] = station;
                group.DockedShips.Add(new DockGroupMember(station, false));
                burn.Invoke(fusion, new object[] { .27, new JsonPowerInfo(), 0d });
                Near(thrustForce, force, "Departure preview changed the reactor's real engine force");
                Near(ship.objSS.vAccIn.magnitude, acceleration/3, "Departure preview discounted actual moored mass");
                Near(ship.GetMaxTorchThrust(.2f), soloAcceleration/2, "Moored preview failed to retain only the departing tow");
            }
            foreach (double pilot in new[] { 1d, .75 }) {
                var nav = SetupCourse(pilot: pilot);
                var group = TowGroup();
                var profile = TorchCourseFuel.Find(nav, false);
                double last = fixtureEpoch;
                foreach (double epoch in new[] { 2500d, 8500, nav.Destination.ArrivalTime }) {
                    fixtureEpoch = epoch; StarSystem.fEpoch = epoch;
                    ship.objSS.TimeAdvance(epoch - last);
                    group.TimeAdvance(epoch - last);
                    Check(ReferenceEquals(ship.objSS.NavData, nav) && !profile.Invalid, "Towing invalidated a supported native fuel profile.");
                    Near(ship.fShallowFusionRemain, TorchCourseFuel.Budget(profile, epoch), "Group playback uses the same corrected fuel budget", 1e-6);
                    Near((ship.objSS.vPos - group.Anchor.vPos).magnitude, 1e-8, "Corrected playback retains the tow's rigid offset", 1e-6);
                    Check(TorchCourseFuel.Playing == null, "Group playback retained the pending fuel patch context.");
                    var autopilot = Empty<FlyToAutoPilot>(); Field(autopilot, "_shipUs", ship);
                    Check((bool)AccessTools.Method(typeof(FlyToAutoPilot), "RequirementsMet").Invoke(autopilot, null), "Native autopilot rejected corrected towing fuel.");
                    last = epoch;
                }
                Near(ship.objSS.vPosx, nav.Destination.ObjSS.vPosx, "Corrected tow reaches the native braking endpoint", 1e-7);
            }
            var abortCourse = SetupCourse();
            var abortGroup = TowGroup();
            fixtureEpoch = 2500; StarSystem.fEpoch = fixtureEpoch;
            ship.objSS.TimeAdvance(2400); abortGroup.TimeAdvance(2400);
            double fuelBeforeRelease = Read(he3[0], "StatSolidHe3");
            Field(ship, "aSecuredTowBraces", null);
            fixtureEpoch = 2600; StarSystem.fEpoch = fixtureEpoch;
            ship.objSS.TimeAdvance(100); abortGroup.TimeAdvance(100);
            Check(ship.objSS.NavData == null, "Released brace left the corrected course engaged.");
            Check(Controls["slidCycle"] == "0" && Controls["slidFlow"] == "0", "Brace abort did not idle native reactor controls.");
            Near(Read(he3[0], "StatSolidHe3"), fuelBeforeRelease, "Brace abort still debited the old course budget");
            Check(Read(core, "IsAutopilotAborted") > 0, "Brace abort did not use the native UI notification condition.");
            Check(TorchCourseFuel.Playing == null, "Brace abort leaked the corrected playback scope.");
            Console.WriteLine("Torch tow integration: " + (checks - before) + " native reactor-force, debit, corrected fuel and grouped-playback checks passed.");
        } finally {
            patches.UnpatchSelf();
            playbackReactor = null;
        }
    }
}
