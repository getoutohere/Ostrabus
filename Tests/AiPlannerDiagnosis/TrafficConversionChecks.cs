using System;
using HarmonyLib;
using Ostrabus.MiscBugFixes;
using Ostranauts.Core;
using Ostranauts.Ships;
using Ostranauts.Ships.Commands;

static partial class LifecycleChecks {
    static bool failStellarConversion;
    static Action duringStellarConversion;
    static bool RecreateStellar(StarSystem __instance, string __0, ShipSitu __2, ref IStellarObject __result) {
        if (failStellarConversion) throw new InvalidOperationException("offline: stellar materialization failed");
        var recreated = new StellarFixture { strID = __0 };
        Field(recreated, "<objSS>k__BackingField", new ShipSitu(__2, false));
        __instance.dictStellarObjects[__0] = recreated;
        Action nested = duringStellarConversion; duringStellarConversion = null; nested?.Invoke();
        __result = recreated;
        return false;
    }
    static void TrafficConversion(StarSystem world, Harmony h) {
        var destroy = AccessTools.Method(typeof(Ship), nameof(Ship.Destroy));
        h.Patch(AccessTools.Method(typeof(Ship), "UnMoorFrom"), prefix: new HarmonyMethod(typeof(LifecycleChecks), nameof(Quiet)));
        h.Patch(AccessTools.Method(typeof(ShipSitu), "PushbackFrom"), prefix: new HarmonyMethod(typeof(LifecycleChecks), nameof(Quiet)));
        h.Patch(AccessTools.Method(typeof(StarSystem), "CreateStellarObject", new[] { typeof(string), typeof(string), typeof(ShipSitu), typeof(Ship.TypeClassification) }),
            prefix: new HarmonyMethod(typeof(LifecycleChecks), nameof(RecreateStellar)));
        var attempt = AccessTools.Method(typeof(Undock), "AttemptToUnMoor");
        foreach (bool repaired in new[] { false, true }) {
            if (!repaired) {
                h.Unpatch(destroy, AccessTools.Method(typeof(ShipDestroyedTargetsPatch), "Prefix"));
                h.Unpatch(destroy, AccessTools.Method(typeof(ShipDestroyedTargetsPatch), "Finalizer"));
            } else {
                h.CreateClassProcessor(typeof(ShipDestroyedTargetsPatch)).Patch();
                h.CreateClassProcessor(typeof(ShipStellarConversionTargetsPatch)).Patch();
            }
            string id = "conversion-asteroid-" + repaired;
            var asteroid = LifetimeShip(world, id); asteroid.Classification = Ship.TypeClassification.Asteroid;
            asteroid.strTemplateName = "fixture-asteroid-template"; asteroid.fLastVisit = 0;
            var visitor = LifetimeShip(world, "leaving-asteroid-" + repaired);
            var observer = LifetimeShip(world, "approaching-asteroid-" + repaired);
            var stellar = new StellarFixture { strID = id };
            Field(stellar, "<objSS>k__BackingField", new ShipSitu(asteroid.objSS, false));
            new TargetData(stellar).ApplyToShip(observer); SavedGoal(observer); var route = LifetimeRoute(observer);
            var independent = observer.shipSituTarget;
            var direct = LifetimeShip(world, "direct-conversion-" + repaired);
            new TargetData(asteroid).ApplyToShip(direct); SavedGoal(direct); var directRoute = LifetimeRoute(direct);
            direct.shipCombatTarget = direct.shipStationKeepingTarget = direct.shipUndock = asteroid;
            direct.json.strCombatTargetID = direct.json.strStationKeepingTargetID = direct.json.strUndockID = id;
            var copied = LifetimeShip(world, "copied-conversion-" + repaired);
            var copy = new ShipSitu(asteroid.objSS, false) { vBOOffsetx = 1e-7 };
            new TargetData(asteroid) { Situ = copy }.ApplyToShip(copied); SavedGoal(copied);
            var copyRoute = LifetimeRoute(copied);
            var coordinate = LifetimeShip(world, "coordinate-conversion-" + repaired);
            coordinate.shipScanTarget = asteroid;
            coordinate.shipSituTarget = new ShipSitu(false) { vPosx = 35 };
            SavedGoal(coordinate); var coordinateRoute = LifetimeRoute(coordinate);
            var result = (CommandStatus)attempt.Invoke(new Undock(new Program.Pilot { ShipUs = visitor }), new object[] { asteroid });
            Check(result.Status == CommandCode.Finished && asteroid.bDestroyed && world.GetShipByRegID(id) == null && world.dictStellarObjects.TryGetValue(id, out _),
                "Native unmoor did not replace the Ship representation with the same logical stellar identity");
            Check(observer.targetRegID == id && ReferenceEquals(observer.objSS.NavData, route),
                "Native conversion unexpectedly removed another observer's independent stellar-object goal");
            if (!repaired) continue;
            var converted = world.dictStellarObjects[id];
            Check(ReferenceEquals(observer.shipSituTarget, independent) && ReferenceEquals(observer.TargetData.Situ, independent),
                "Conversion replaced an independently owned stellar waypoint");
            Check(direct.shipScanTarget == null && direct.TargetData.Ship == null && ReferenceEquals(direct.shipSituTarget, converted.objSS)
                && ReferenceEquals(direct.TargetData.Situ, converted.objSS) && ReferenceEquals(direct.objSS.NavData, directRoute) && direct.targetRegID == id,
                "Direct asteroid goal did not switch representation while retaining its course");
            Check(direct.shipCombatTarget == null && direct.shipStationKeepingTarget == null && direct.shipUndock == null,
                "Conversion retains obsolete auxiliary Ship references");
            Check(direct.json.strScanTargetID == null && direct.json.strCombatTargetID == null && direct.json.strStationKeepingTargetID == null
                && direct.json.strUndockID == null && direct.json.strTargetRegID == id && direct.json.objSituScanTarget != null,
                "Conversion left saved Ship references or erased the stellar identity");
            Check(ReferenceEquals(copied.shipSituTarget, copy) && copied.TargetData.Ship == null && copied.shipScanTarget == null
                && ReferenceEquals(copied.objSS.NavData, copyRoute) && copy.vBOOffsetx == 1e-7 && copied.targetRegID == id,
                "Conversion damaged an offset waypoint or retained its old Ship");
            Check(coordinate.shipScanTarget == null && coordinate.shipSituTarget.vPosx == 35 && ReferenceEquals(coordinate.objSS.NavData, coordinateRoute),
                "Conversion cancelled a scanned-only observer's unrelated coordinate course");
            foreach (var traveler in new[] { observer, direct, copied }) {
                traveler.CheckTargets();
                Check(traveler.targetRegID == id && traveler.objSS.NavData != null && traveler.shipScanTarget == null,
                    "Native target restoration lost the converted goal or resurrected a retired Ship");
                Check(traveler.fShallowRCSRemass == 100 && traveler.fShallowFusionRemain == 200
                    && traveler.objSS.vPosx == 1 && traveler.objSS.vPosy == 2 && traveler.objSS.vVelX == 3e-9 && traveler.objSS.vVelY == 4e-9,
                    "Conversion correction changed fuel or momentum");
            }
            Check(ShipReplacementTargetsPatch.Replacing == null, "Conversion scope retains the old asteroid");
        }
        // A failed conversion is a real loss. Keep the original exception and
        // invalidate its goal, restoring an enclosing scope even on failure.
        var lost = LifetimeShip(world, "failed-conversion-asteroid"); lost.Classification = Ship.TypeClassification.Asteroid;
        lost.strTemplateName = "fixture-template";
        var approaching = LifetimeShip(world, "failed-conversion-observer");
        new TargetData(lost).ApplyToShip(approaching); SavedGoal(approaching); LifetimeRoute(approaching);
        var board = LifetimeBoard(approaching);
        var enclosing = LifetimeShip(world, "conversion-enclosing-scope");
        ShipReplacementTargetsPatch.Replacing = enclosing; failStellarConversion = true;
        try {
            bool threw = false;
            try { attempt.Invoke(new Undock(new Program.Pilot { ShipUs = approaching }), new object[] { lost }); }
            catch (System.Reflection.TargetInvocationException ex) { threw = ex.InnerException is InvalidOperationException; }
            Check(threw && ReferenceEquals(ShipReplacementTargetsPatch.Replacing, enclosing), "Failed conversion swallowed an exception or lost its enclosing scope");
            CheckCleared(approaching, board, "failed stellar conversion");
        } finally { failStellarConversion = false; ShipReplacementTargetsPatch.Replacing = null; }

        var outer = LifetimeShip(world, "nested-conversion-outer"); outer.Classification = Ship.TypeClassification.Asteroid; outer.strTemplateName = "fixture-template";
        var inner = LifetimeShip(world, "nested-conversion-inner"); inner.Classification = Ship.TypeClassification.Asteroid; inner.strTemplateName = "fixture-template";
        var leaving = LifetimeShip(world, "nested-conversion-visitor");
        duringStellarConversion = () => {
            Check(ReferenceEquals(ShipReplacementTargetsPatch.Replacing, outer), "Outer conversion scope missing");
            attempt.Invoke(new Undock(new Program.Pilot { ShipUs = leaving }), new object[] { inner });
            Check(ReferenceEquals(ShipReplacementTargetsPatch.Replacing, outer), "Nested conversion did not restore the outer scope");
        };
        try {
            attempt.Invoke(new Undock(new Program.Pilot { ShipUs = leaving }), new object[] { outer });
            Check(ShipReplacementTargetsPatch.Replacing == null && world.dictStellarObjects.TryGetValue(inner.strRegID, out _)
                && world.dictStellarObjects.TryGetValue(outer.strRegID, out _), "Nested conversion retained scope or lost a stellar object");
        } finally { duringStellarConversion = null; }

        var permanent = LifetimeShip(world, "permanently-destroyed-asteroid"); permanent.Classification = Ship.TypeClassification.Asteroid;
        permanent.strTemplateName = "fixture-template";
        new TargetData(permanent).ApplyToShip(approaching); SavedGoal(approaching); LifetimeRoute(approaching);
        permanent.Destroy(); CheckCleared(approaching, board, "permanent asteroid destruction outside conversion");
        Console.WriteLine("Native asteroid conversion preserves independent/copied goals and routes; production correction rebinds direct goals, clears retired references, and handles failed/nested conversion and permanent destruction.");
    }
}
