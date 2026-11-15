using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using Ostrabus.NavConsole;
using Ostrabus.NavConsole.Outlines;
using Ostrabus.ShipNavigation.Collisions;
using Ostranauts.ShipGUIs.NavStation;
using Ostranauts.ShipGUIs.Utilities;
using SolarSystem;
using UnityEngine.UI;

static partial class Program {
    static Slider rangeDistanceSlider;
    static float rangeSliderValue;
    static bool RangeSlider(Slider __instance, float value) {
        // The real prefab slider clamps its 0..10 mapped range in its setter.
        if (ReferenceEquals(__instance, rangeDistanceSlider)) rangeSliderValue = Math.Max(0, Math.Min(10, value));
        return false;
    }
    // Negative control: the former UI consumed the complete formation bound.
    static bool OldRange(Ship a, Ship b, float native, ref float __result) {
        __result = HullDocking.RadiusSum(a, b, native);
        return false;
    }
    // Negative control: the former scan kept its original native hull allowance.
    static bool OldWarning(float nativeCombined, ref float __result) {
        __result = nativeCombined;
        return false;
    }
    static void WarmRangeRadius(Ship ship, double metres) {
        double side = metres / (Envelope.MapMetresPerTile * Math.Sqrt(2));
        WarmCollision(ship, new Envelope(new[] {
            new HullPoint(-side, -side), new HullPoint(side, -side),
            new HullPoint(side, side), new HullPoint(-side, side)
        }, 4), true);
    }
    static void RangeCases() {
        int before = checks;
        var world = CrewSim.system;
        var selected = GUIOrbitDraw.CrossHairTarget;
        var nav = GUIOrbitDraw.Instance;
        var flag = PhysicalCollisions.Flag;
        var environment = new Harmony("offline.hull.range.environment");
        var production = new Harmony("offline.hull.range.production");
        try {
            HullDisplay.Reset(); PhysicalCollisions.Reset();
            PhysicalCollisions.Flag = Empty<ConfigEntry<bool>>();
            Field(PhysicalCollisions.Flag, "_typedValue", true);
            var us = CollisionHull("RANGE-PLAYER", 0, 0);
            var target = CollisionHull("RANGE-TARGET", 528.250401048672, 4883.742807888587);
            var rock = CollisionHull("RANGE-ASTEROID", -1875.513337156103, 3213.931476139651);
            us.objSS.Size = 500; target.objSS.Size = 480; rock.objSS.Size = 880;
            rock.Classification = Ship.TypeClassification.Asteroid;
            WarmRangeRadius(us, 1145.4256591796875); WarmRangeRadius(target, 878.63531494140625);
            CrewSim.system = Empty<StarSystem>();
            CrewSim.system.dictShips = new Dictionary<string, Ship> {
                [us.strRegID] = us, [target.strRegID] = target, [rock.strRegID] = rock
            };
            CrewSim.system.aBOs = new Dictionary<string, BodyOrbit>();
            environment.Patch(AccessTools.PropertyGetter(typeof(Ship), nameof(Ship.Mass)), prefix: new HarmonyMethod(typeof(Program), nameof(DockingMass)));
            environment.Patch(AccessTools.Method(typeof(CrewSim), nameof(CrewSim.GetSelectedCrew)), prefix: new HarmonyMethod(typeof(Program), nameof(NoDockingCrew)));
            environment.Patch(AccessTools.Method(typeof(NavModTargetData), "AddLine", new[] { typeof(string), typeof(string), typeof(string) }), prefix: new HarmonyMethod(typeof(Program), nameof(Skip)));
            environment.Patch(AccessTools.PropertySetter(typeof(Slider), nameof(Slider.value)), prefix: new HarmonyMethod(typeof(Program), nameof(RangeSlider)));
            var group = new DockGroup(); group.AddShip(target); group.AddShip(rock, true);
            double groupBound = HullDocking.Bound(target, group.Anchor);
            double groupAllowance = HullDocking.DistanceAU(us, target);
            double expected = us.objSS.GetDistance(target.objSS) - HullDocking.PairDistanceAU(us, target);
            Check(expected / HullZoom.MetresToAU > 2888 && expected / HullZoom.MetresToAU < 2889, "Captured hull clearance fixture drifted.");
            var owner = Empty<CondOwner>(); Field(owner, "_objShip", us);
            var info = Empty<NavModTargetData>(); Field(info, "COSelf", owner);
            var moor = Empty<NavModMooringControl>(); Field(moor, "COSelf", owner);
            rangeDistanceSlider = Empty<Slider>();
            Field(moor, "sldrDist", rangeDistanceSlider); Field(moor, "sldrVRel", Empty<Slider>());
            var poi = Empty<NavPOI>(); Field(poi, "_ship", target); GUIOrbitDraw.CrossHairTarget = poi;
            GUIOrbitDraw.Instance = Empty<GUIOrbitDraw>();
            Field(GUIOrbitDraw.Instance, "dictPropMap", new Dictionary<string, string>());
            var readout = AccessTools.Method(typeof(NavModTargetData), "SetStringsVRelShip");
            var sliders = AccessTools.Method(typeof(NavModMooringControl), "UpdateSliders");
            var canMoor = AccessTools.Method(typeof(NavModMooringControl), "CanMoor");
            production.CreateClassProcessor(typeof(HullDockingGeometryPatch)).Patch();
            production.CreateClassProcessor(typeof(HullDockingHandoffPatch)).Patch();
            var pair = AccessTools.Method(typeof(HullDocking), nameof(HullDocking.PairRadiusSum));
            environment.Patch(pair, prefix: new HarmonyMethod(typeof(Program), nameof(OldRange)));
            readout.Invoke(info, new object[] { false }); sliders.Invoke(moor, null);
            Check(GUIOrbitDraw.Instance.dRNG == 0 && rangeSliderValue == 0, "Negative control failed to reproduce zero readout/slider.");
            Check((bool)canMoor.Invoke(moor, null), "Negative control failed to reproduce premature tether range.");
            environment.Unpatch(pair, HarmonyPatchType.Prefix, environment.Id);
            readout.Invoke(info, new object[] { false }); sliders.Invoke(moor, null);
            Check(Math.Abs(GUIOrbitDraw.Instance.dRNG - expected) / HullZoom.MetresToAU < .002, "Native target data still measures the asteroid group.");
            Check(rangeSliderValue > 0, "Native mooring slider still reports zero range.");
            Check(!(bool)canMoor.Invoke(moor, null), "Native tether can engage beyond its selected-hull 2.5 km margin.");
            Check(HullDocking.DistanceAU(us, target) == groupAllowance && HullDocking.Bound(target, group.Anchor) == groupBound, "UI correction shrank placement/avoidance group bounds.");
            var flight = Empty<Ostranauts.Ships.Commands.FlyToPath>(); Field(flight, "_shipUs", us);
            double handoff = (double)AccessTools.Method(typeof(Ostranauts.Ships.Commands.BaseCommand), "GetHandOffDistance")
                .Invoke(flight, new object[] { (double)CollisionManager.GetCollisionDistanceAU(us, target), target });
            Check(Math.Abs(handoff - groupAllowance * 3) < 1e-14, "AI approach lost its complete formation allowance.");
            Field(PhysicalCollisions.Flag, "_typedValue", false);
            readout.Invoke(info, new object[] { false });
            Check(Math.Abs(GUIOrbitDraw.Instance.dRNG - (us.objSS.GetDistance(target.objSS) - CollisionManager.GetCollisionDistanceAU(us, target))) < 1e-14, "Feature-off readout no longer uses native radii.");
            Field(PhysicalCollisions.Flag, "_typedValue", true);
            var station = CollisionHull("RANGE-STATION", 0, 0); station.Classification = Ship.TypeClassification.OrbitalStation; station.objSS.bIsBO = true;
            group.AddShip(station);
            Check(HullDocking.PairDistanceAU(us, target) == CollisionManager.GetCollisionDistanceAU(us, target), "Station-connected hull acquired alternative UI geometry.");
            Check(HullDocking.WarningThreshold(1e-7f, us, target) == 1e-7f, "Station-connected hull acquired alternative warning geometry.");
            group.RemoveShip(station); group.RemoveShip(target); group.RemoveShip(rock);
            CrewSim.system.dictShips.Remove(rock.strRegID);
            ProximityRangeCases(us, target, production, environment);
            Console.WriteLine("Hull range/proximity audit: " + (checks - before) + " checks passed (native UI, tether, warning candidates and both patch orders).");
        } finally {
            production.UnpatchSelf(); environment.UnpatchSelf();
            rangeDistanceSlider = null; GUIOrbitDraw.Instance = nav; GUIOrbitDraw.CrossHairTarget = selected;
            CrewSim.system = world; PhysicalCollisions.Flag = flag; ProximityRange.Reset(default); HullDisplay.Reset(); PhysicalCollisions.Reset();
        }
    }
    static void ProximityRangeCases(Ship us, Ship target, Harmony production, Harmony environment) {
        // Other UI patches have completed their checks; independently exercise
        // both installation orders on the same real native collision scan.
        production.UnpatchSelf();
        double radii = HullDisplay.Radius(us) + HullDisplay.Radius(target);
        Field(us, "nLoadState", Ship.Loaded.Full); us.aNavs = new List<CondOwner>();
        target.objSS.vPosx = target.objSS.vPosy = us.objSS.vPosx = 0;
        var warning = AccessTools.Method(typeof(HullDocking), nameof(HullDocking.WarningThreshold));
        foreach (bool reverse in new[] { false, true }) {
            foreach (var patch in reverse ? new[] { typeof(ProximityCollisionPatch), typeof(HullCollisionScanPatch) } : new[] { typeof(HullCollisionScanPatch), typeof(ProximityCollisionPatch) })
                production.CreateClassProcessor(patch).Patch();
            ProximityRange.Set(us, 5);
            us.objSS.vPosy = (radii + 4500) * HullZoom.MetresToAU;
            environment.Patch(warning, prefix: new HarmonyMethod(typeof(Program), nameof(OldWarning)));
            ClearImpactMemory(); CollisionManager.CheckCollisions(us);
            Check(!us.aProxCurrent.Contains(target.strRegID), "Negative control failed to reproduce late 5 km warning.");
            environment.Unpatch(warning, HarmonyPatchType.Prefix, environment.Id);
            foreach (int km in new[] { 5, 10, 15, 20 }) {
                ProximityRange.Set(us, km);
                foreach (double delta in new[] { -1d, 1d }) {
                    us.objSS.vPosy = (radii + km * 1000 + delta) * HullZoom.MetresToAU;
                    ClearImpactMemory(); CollisionManager.CheckCollisions(us);
                    Check(us.aProxCurrent.Contains(target.strRegID) == (delta < 0), "Warning disagrees with hull clearance: order " + reverse + ", range " + km + ", delta " + delta);
                }
            }
            ProximityRange.Set(us, 5);
            us.objSS.vPosy = (radii + 4500) * HullZoom.MetresToAU;
            Field(PhysicalCollisions.Flag, "_typedValue", false);
            ClearImpactMemory(); CollisionManager.CheckCollisions(us);
            Check(!us.aProxCurrent.Contains(target.strRegID), "Feature-off scan did not restore native warning threshold.");
            Field(PhysicalCollisions.Flag, "_typedValue", true);
            float native = CollisionManager.GetCollisionDistanceAU(us, target);
            foreach (float factor in new[] { 1f, 2f, 10f }) {
                float buffer = ProximityRange.NativeShipAU * factor;
                Check(Math.Abs(HullDocking.WarningThreshold(buffer + native, us, target) - (buffer + HullDocking.PairDistanceAU(us, target))) < 1e-13, "Warning correction changed the native speed buffer.");
            }
            foreach (var kind in new[] { Ship.TypeClassification.Asteroid, Ship.TypeClassification.OrbitalStation, Ship.TypeClassification.Projectile }) {
                target.Classification = kind;
                Check(HullDocking.WarningThreshold(1e-7f, us, target) == 1e-7f, "Excluded warning pair changed: " + kind);
            }
            target.Classification = Ship.TypeClassification.Default;
            for (int i = 0; i < 100; i++) HullDocking.WarningThreshold(1e-7f, us, target);
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10000; i++) HullDocking.WarningThreshold(1e-7f, us, target);
            Check(GC.GetAllocatedBytesForCurrentThread() == allocated, "Warning geometry allocates per candidate.");
            production.UnpatchSelf();
        }
    }
}
