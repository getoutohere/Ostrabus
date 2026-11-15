using System;
using HarmonyLib;
using Ostrabus.NavConsole.Rotation;
using UnityEngine;

static partial class Program {
    static float observedYaw;
    static void Observe(float fR) { if (RotationRuntime.Actuating) observedYaw = fR; }

    static void Boundaries(Harmony fixture) {
        RotationRuntime.Cleanup();
        var controlled = Hull("equivalence-controlled");
        Order(controlled, Math.PI);
        fixture.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.Maneuver)),
            prefix: new HarmonyMethod(typeof(Program), nameof(Observe)));
        controlled.Maneuver(.1f, .2f, 0, 0, .02f);
        controlled.objSS.TimeAdvance(.02);
        fixture.Unpatch(AccessTools.Method(typeof(Ship), nameof(Ship.Maneuver)), AccessTools.Method(typeof(Program), nameof(Observe)));
        var native = Hull("equivalence-native");
        native.Maneuver(.1f, .2f, observedYaw, 0, .02f);
        native.objSS.TimeAdvance(.02);
        Check(controlled.fShallowRCSRemass == native.fShallowRCSRemass, "Controlled step differs from native fuel debit");
        Check(controlled.objSS.vAccRCS == native.objSS.vAccRCS && controlled.objSS.vPosx == native.objSS.vPosx &&
            controlled.objSS.vPosy == native.objSS.vPosy && controlled.objSS.vVelX == native.objSS.vVelX &&
            controlled.objSS.vVelY == native.objSS.vVelY, "Combined translation differs from native physics");
        Check(controlled.objSS.fRot == native.objSS.fRot && controlled.objSS.fW == native.objSS.fW,
            "Normal angular step differs from native actuation/integration");

        RotationRuntime.Cleanup();
        var removed = Hull("module-removed");
        Order(removed, Math.PI);
        removed.objSS.TimeAdvance(.02);
        removed.json.aItems[1].strParentID = "uninstalled";
        double fuel = removed.fShallowRCSRemass;
        removed.objSS.TimeAdvance(.02);
        Check(removed.fShallowRCSRemass == fuel, "Cached installation survived an in-place parent change");

        RotationRuntime.Cleanup();
        var powered = Hull("console-power");
        powered.json.aCOs = new[] { new JsonCondOwnerSave { strID = "console-power-nav", aConds = new[] { "IsOff=1.0x0" } } };
        Order(powered, Math.PI);
        powered.objSS.TimeAdvance(.02);
        powered.json.aCOs[0].aConds[0] = "IsOff=1.0x1";
        fuel = powered.fShallowRCSRemass;
        powered.objSS.TimeAdvance(.02);
        Check(powered.fShallowRCSRemass == fuel, "In-place saved power change escaped cache invalidation");

        RotationRuntime.Cleanup();
        var lost = Hull("lost-target-tracker");
        var target = Hull("lost-target"); target.objSS.vPosx = .001;
        var holding = Order(lost, -Math.PI / 2, 0, true, target.strRegID);
        lost.objSS.TimeAdvance(.02);
        target.HideFromSystem = true;
        lost.objSS.TimeAdvance(.02);
        Check(!holding.Order.Active && !holding.Order.Hold, "Hidden target retained automatic bearing control");

        RotationRuntime.Cleanup();
        var locked = Hull("locked-hull");
        Order(locked, Math.PI);
        locked.objSS.bBOLocked = true;
        fuel = locked.fShallowRCSRemass;
        locked.objSS.TimeAdvance(.02);
        Check(locked.fShallowRCSRemass == fuel, "Body-locked hull received rotation thrust");
        RotationRuntime.Cleanup();
        Console.WriteLine("Native step equivalence, shallow parent/power invalidation, target loss and orbit-lock boundaries passed.");
    }
}
