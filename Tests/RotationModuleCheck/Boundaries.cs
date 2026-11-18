using System;
using HarmonyLib;
using Ostrabus.NavConsole.Rotation;
using Ostrabus.ShipNavigation;
using UnityEngine;

static partial class Program {
    static float observedYaw;
    static void Observe(float fR) { if (RotationRuntime.Actuating) observedYaw = fR; }

    // 1.1.0.2 removes Maneuver's zero-exhaust early return. Exercise that exact
    // native branch with both hooks, including the deferred rotation actuator.
    static void ExhaustFallback(Harmony fixture, Harmony production) {
        var method = AccessTools.Method(typeof(Ship), nameof(Ship.Maneuver));
        var rotationPrefix = AccessTools.Method(typeof(RotationManualInputPatch), "Prefix");
        var handoff = new Harmony("offline.rotation.exhaust-handoff");
        Ship Vessel(string id, float exhaust, bool ai) {
            var ship = Hull(id);
            Set(ship, "fRCSExhaustV", exhaust); Set(ship, "bAIShip", ai);
            return ship;
        }
        bool Thrusting(Ship ship) => ship.objSS.vAccRCS.x != 0 || ship.objSS.vAccRCS.y != 0;
        bool SameThrust(Ship a, Ship b) => a.objSS.vAccRCS.x == b.objSS.vAccRCS.x && a.objSS.vAccRCS.y == b.objSS.vAccRCS.y;
        try {
            foreach (float exhaust in new[] { 8f, 0f }) foreach (bool ai in new[] { false, true }) {
                RotationRuntime.Cleanup();
                production.Unpatch(method, rotationPrefix); handoff.UnpatchSelf();
                var native = Vessel("exhaust-native-" + exhaust + ai, exhaust, ai);
                native.Maneuver(.1f, .2f, .3f, 0, .02f);
                Check(Thrusting(native) && native.objSS.fW != 0,
                    "Native exhaust fallback unavailable: exhaust=" + exhaust + "; AI=" + ai);
                production.Patch(method, prefix: new HarmonyMethod(rotationPrefix));
                handoff.CreateClassProcessor(typeof(ShallowRcsTorchReleasePatch)).Patch();
                var hooked = Vessel("exhaust-hooked-" + exhaust + ai, exhaust, ai);
                if (ai) { hooked.objSS.vAccIn.x = 1e-9f; Set(hooked, "bTorchDriveThrusting", true); }
                hooked.Maneuver(.1f, .2f, .3f, 0, .02f);
                Check(SameThrust(hooked, native) && hooked.objSS.fW == native.objSS.fW &&
                    hooked.objSS.fA == native.objSS.fA && hooked.fShallowRCSRemass == native.fShallowRCSRemass,
                    "Maneuver hooks changed native fallback thrust, torque or fuel: " + exhaust + "; AI=" + ai);
                Check(hooked.objSS.vAccIn.x == 0 && hooked.objSS.vAccIn.y == 0, "AI torch release lost during exhaust fallback");

                var controlled = Vessel("exhaust-controlled-" + exhaust + ai, exhaust, ai);
                var order = Order(controlled, Math.PI);
                fixture.Patch(method, prefix: new HarmonyMethod(typeof(Program), nameof(Observe)));
                controlled.Maneuver(.1f, .2f, 0, 0, .02f);
                controlled.objSS.TimeAdvance(.02);
                fixture.Unpatch(method, AccessTools.Method(typeof(Program), nameof(Observe)));
                Check(Thrusting(controlled) && controlled.objSS.fW != 0,
                    "Rotation controller suppressed the native exhaust fallback");
                RotationRuntime.Cleanup();
                production.Unpatch(method, rotationPrefix); handoff.UnpatchSelf();
                var reference = Vessel("exhaust-reference-" + exhaust + ai, exhaust, ai);
                reference.Maneuver(.1f, .2f, observedYaw, 0, .02f);
                reference.objSS.TimeAdvance(.02);
                Check(SameThrust(controlled, reference) &&
                    controlled.fShallowRCSRemass == reference.fShallowRCSRemass &&
                    controlled.objSS.fRot == reference.objSS.fRot && controlled.objSS.fW == reference.objSS.fW,
                    "Deferred rotation step differs from unhooked native fallback");
                production.Patch(method, prefix: new HarmonyMethod(rotationPrefix));
                handoff.CreateClassProcessor(typeof(ShallowRcsTorchReleasePatch)).Patch();
            }
            foreach (string missing in new[] { "thrusters", "distributor", "fuel" }) {
                RotationRuntime.Cleanup();
                var ship = Vessel("exhaust-missing-" + missing, 0, false);
                if (missing == "thrusters") Set(ship, "fRCSCount", 0f);
                if (missing == "distributor") Set(ship, "nRCSDistroCount", 0);
                if (missing == "fuel") ship.fShallowRCSRemass = 0;
                double fuel = ship.fShallowRCSRemass;
                Order(ship, Math.PI);
                ship.Maneuver(.1f, .2f, 0, 0, .02f);
                ship.objSS.TimeAdvance(.02);
                Check(!Thrusting(ship) && ship.objSS.fW == 0 && ship.fShallowRCSRemass == fuel,
                    "Exhaust fallback bypassed missing " + missing);
            }
            Console.WriteLine("23 exhaust fallback checks passed: unhooked native versus rotation/torch-release hooks, physical rotation, fuel and hardware gates.");
        } finally {
            fixture.Unpatch(method, AccessTools.Method(typeof(Program), nameof(Observe)));
            handoff.UnpatchSelf(); RotationRuntime.Cleanup();
        }
    }

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
