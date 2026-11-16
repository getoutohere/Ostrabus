using System;
using System.Collections.Generic;
using HarmonyLib;
using Ostrabus.NavConsole.Rotation;
using Ostranauts.Core.Models;
using Ostranauts.Ships;

static partial class Program {
    static double parentEpoch;
    static bool ParentMotion(BodyOrbit __instance, double __0) {
        __instance.dXReal = 2 + (__0 - parentEpoch) * 20000 / 149597870700.0;
        __instance.dYReal = 1;
        return false;
    }
    sealed class Rock : IStellarObject {
        public int MapID { get; set; }
        public string strID { get; set; }
        public string strLoot { get; set; }
        public ShipSitu objSS { get; } = new ShipSitu(false);
    }

    static void TrackingFrames(Harmony fixture) {
        double epoch = StarSystem.fEpoch;
        float? firstHeading = null;
        foreach (bool targetFirst in new[] { true, false }) {
            RotationRuntime.Cleanup(); StarSystem.fEpoch = epoch;
            var observer = Hull("ordered-observer-" + targetFirst);
            var contact = Hull("ordered-contact-" + targetFirst);
            observer.objSS.vPosx = contact.objSS.vPosx = 2;
            observer.objSS.vPosy = 1; contact.objSS.vPosy = 1 + 1000 / 149597870700.0;
            observer.objSS.vVelX = 20020 / 149597870700.0;
            contact.objSS.vVelX = 20000 / 149597870700.0;
            var tracking = Order(observer, 0, hold: true, target: contact.strRegID);
            for (int i = 0; i < 1200; i++) {
                RotationRuntime.SampleWorld(); StarSystem.fEpoch += .1;
                if (targetFirst) contact.objSS.TimeAdvance(.1);
                observer.objSS.TimeAdvance(.1);
                if (!targetFirst) contact.objSS.TimeAdvance(.1);
            }
            if (firstHeading.HasValue) Check(observer.objSS.fRot == firstHeading.Value, "Ship iteration order changed the held bearing");
            else firstHeading = observer.objSS.fRot;
            Check(tracking.Order.Hold, "Order-independent tracking lost its hold");
        }
        fixture.Patch(AccessTools.Method(typeof(BodyOrbit), nameof(BodyOrbit.UpdateTime)), prefix: new HarmonyMethod(typeof(Program), nameof(ParentMotion)));
        CrewSim.system.aBOs = new Dictionary<string, BodyOrbit>();
        CrewSim.system.dictStellarObjects = new DictionaryArray<string, IStellarObject>();
        RotationRuntime.Cleanup();
        var ship = Hull("anchored-rock-tracker");
        var parent = Empty<BodyOrbit>(); parent.strName = "moving-rock-parent";
        CrewSim.system.aBOs[parent.strName] = parent;
        parentEpoch = StarSystem.fEpoch;
        var rock = new Rock { strID = "moving-rock" };
        rock.objSS.bBOLocked = true; rock.objSS.strBOPORShip = parent.strName;
        rock.objSS.vBOOffsety = 1000 / 149597870700.0;
        CrewSim.system.dictStellarObjects[rock.strID] = rock;
        // Like a native unloaded asteroid, the stored world position is absent
        // or a frame behind. NavPOI instead reads parent position plus offset.
        var hold = Order(ship, 0, hold: true, target: rock.strID); hold.TargetKind = "object";
        double worst = 0;
        for (int i = 0; i < 1200; i++) {
            double t = i * .1;
            ship.objSS.vPosx = 2 + t * 20020 / 149597870700.0;
            ship.objSS.vPosy = 1;
            ship.objSS.vVelX = 20020 / 149597870700.0;
            RotationRuntime.SampleWorld();
            StarSystem.fEpoch += .1;
            ship.objSS.TimeAdvance(.1);
            // Native ordering advances the asteroid after the observing ship.
            parent.UpdateTime(StarSystem.fEpoch);
            rock.objSS.vPosx = parent.dXReal;
            rock.objSS.vPosy = parent.dYReal + rock.objSS.vBOOffsety;
            double expected = Math.Atan2(1000, -20 * (t + .1)) - Math.PI / 2;
            if (t > 10) worst = Math.Max(worst, Math.Abs(RotationPid.Wrap(ship.objSS.fRot - expected)) * 180 / Math.PI);
        }
        Check(hold.Order.Hold && worst < .06, "Anchored asteroid tracking mixed current ship and stale asteroid positions: " + worst);
        Console.WriteLine("Native asteroid parent/offset and ship-before-asteroid phases: 20 km/s common motion plus 20 m/s cross motion; max error " + worst + " degrees.");
        fixture.Unpatch(AccessTools.Method(typeof(BodyOrbit), nameof(BodyOrbit.UpdateTime)), AccessTools.Method(typeof(Program), nameof(ParentMotion)));

        RotationRuntime.Cleanup();
        var turn = Hull("strong-coast-brake");
        var order = Order(turn, Math.PI);
        fixture.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.Maneuver)), prefix: new HarmonyMethod(typeof(Program), nameof(Observe)));
        double coast = 0, peak = 0, brake = 0, elapsed = 0;
        for (; order.Order.Active && elapsed < 60; elapsed += .02) {
            double before = turn.fShallowRCSRemass;
            StarSystem.fEpoch += .02; turn.objSS.TimeAdvance(.02);
            peak = Math.Max(peak, observedYaw); brake = Math.Min(brake, observedYaw);
            if (observedYaw == 0 && Math.Abs(turn.objSS.fW) > .4f) {
                coast += .02;
                Check(turn.fShallowRCSRemass == before, "Middle of the turn burned fuel while commanded to coast");
            }
        }
        fixture.Unpatch(AccessTools.Method(typeof(Ship), nameof(Ship.Maneuver)), AccessTools.Method(typeof(Program), nameof(Observe)));
        Check(!order.Order.Active && elapsed < 15 && peak > .9 && brake < -.5 && coast > 2,
            "Large turn did not accelerate/coast/brake decisively: time=" + elapsed + " peak=" + peak + " brake=" + brake + " coast=" + coast);
        Console.WriteLine("180-degree native turn: " + elapsed + " seconds, " + coast + " seconds with zero rotational thrust, peak/braking " + peak + "/" + brake + ".");
        RotationRuntime.Cleanup();
    }
}
