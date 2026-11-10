using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using LitJson;
using Ostranauts.Core;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;
using Ostranauts.ShipGUIs.Utilities;
using UnityEngine;

// Execute the native unload/replacement and target restoration code. Only Unity
// presentation, asset materialization and item serialization are fixture boundaries.
static class LifecycleChecks {
    static int checks;
    static GameObject go;
    static Transform transform;
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Field(object target, string name, object value) => AccessTools.Field(target.GetType(), name).SetValue(target, value);
    static void Check(bool ok, string reason) { checks++; if (!ok) throw new Exception(reason); }
    static bool Quiet() => false;
    static bool ExpectedGuardWarning(object message) => !message.ToString().StartsWith("[VORB Nav Guard] Rejected route for guard-hauler:", StringComparison.Ordinal);
    static bool TransformOf(ref Transform __result) { __result = transform; return false; }
    static bool Find(ref GameObject __result) { __result = go; return false; }
    static bool Amount(ref double __result) { __result = 0; return false; }
    static bool Items(ref List<CondOwner> __result) { __result = new List<CondOwner>(); return false; }
    static bool Children(ref IEnumerator __result) { __result = Array.Empty<Transform>().GetEnumerator(); return false; }
    static bool Save(Ship __instance, string strName, ref JsonShip __result) {
        __result = new JsonShip { strName = strName, strRegID = __instance.strRegID,
            objSS = __instance.objSS.GetJSON(), strScanTargetID = __instance.shipScanTarget?.strRegID,
            strTargetRegID = __instance.targetRegID, objSituScanTarget = __instance.shipSituTarget?.GetJSON() };
        return false;
    }
    static void InitializeCollections(object instance) {
        foreach (FieldInfo field in instance.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)) {
            Type type = field.FieldType;
            if (type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type) && !type.IsAbstract && !type.IsArray && type.GetConstructor(Type.EmptyTypes) != null)
                field.SetValue(instance, Activator.CreateInstance(type));
        }
    }
    static bool ConstructShip(Ship __instance, GameObject go) {
        InitializeCollections(__instance);
        __instance.gameObject = go;
        Field(__instance, "<objSS>k__BackingField", new ShipSitu(false));
        Field(__instance, "<ShipCO>k__BackingField", Empty<CondOwner>());
        __instance.tfBGs = transform;
        return false;
    }
    static bool Materialize(Ship __instance, Ship.Loaded nLoad) {
        // No tile/CO assets are needed. Match native InitShip's initial situation
        // restoration, using the actual ShipSitu(JsonShipSitu) constructor.
        __instance.strRegID = __instance.json.strRegID;
        Field(__instance, "<objSS>k__BackingField", new ShipSitu(__instance.json.objSS));
        Field(__instance, "nLoadState", nLoad);
        return false;
    }
    static Ship ShipAt(string id, ShipSitu situation) {
        var ship = Empty<Ship>(); ConstructShip(ship, go); ship.strRegID = id;
        Field(ship, "<objSS>k__BackingField", situation);
        Field(ship, "nLoadState", Ship.Loaded.Full);
        ship.Classification = Ship.TypeClassification.GroundStation;
        return ship;
    }
    static void Patch(Harmony h, Type type, string name, string prefix) => h.Patch(AccessTools.Method(type, name), prefix: new HarmonyMethod(typeof(LifecycleChecks), prefix));
    static void Singleton(Harmony h, string name, string endpoint) {
        Type type = AccessTools.TypeByName(name);
        Type singleton = typeof(MonoSingleton<>).MakeGenericType(type);
        AccessTools.Field(singleton, "_instance").SetValue(null, FormatterServices.GetUninitializedObject(type));
        Patch(h, type, endpoint, nameof(Quiet));
    }
    internal static int Run() {
        var h = new Harmony("offline.ai-target.lifecycle");
        try {
            go = Empty<GameObject>(); transform = Empty<Transform>();
            h.Patch(AccessTools.Constructor(typeof(GameObject), new[] { typeof(string) }), prefix: new HarmonyMethod(typeof(LifecycleChecks), nameof(Quiet)));
            h.Patch(AccessTools.Constructor(typeof(Ship), new[] { typeof(GameObject) }), prefix: new HarmonyMethod(typeof(LifecycleChecks), nameof(ConstructShip)));
            h.Patch(AccessTools.PropertyGetter(typeof(GameObject), "transform"), prefix: new HarmonyMethod(typeof(LifecycleChecks), nameof(TransformOf)));
            h.Patch(AccessTools.Method(typeof(Transform), "SetParent", new[] { typeof(Transform), typeof(bool) }), prefix: new HarmonyMethod(typeof(LifecycleChecks), nameof(Quiet)));
            Patch(h, typeof(GameObject), "Find", nameof(Find));
            Patch(h, typeof(Transform), "GetEnumerator", nameof(Children));
            h.Patch(AccessTools.Method(typeof(UnityEngine.Object), "DestroyImmediate", new[] { typeof(UnityEngine.Object) }), prefix: new HarmonyMethod(typeof(LifecycleChecks), nameof(Quiet)));
            h.Patch(AccessTools.Method(typeof(Debug), "Log", new[] { typeof(object) }), prefix: new HarmonyMethod(typeof(LifecycleChecks), nameof(Quiet)));
            Patch(h, typeof(CondOwner), "Destroy", nameof(Quiet));
            h.Patch(AccessTools.Method(typeof(CondOwner), "GetCondAmount", new[] { typeof(string) }), prefix: new HarmonyMethod(typeof(LifecycleChecks), nameof(Amount)));
            h.Patch(AccessTools.Method(typeof(CondOwner), "GetCondAmount", new[] { typeof(string), typeof(bool) }), prefix: new HarmonyMethod(typeof(LifecycleChecks), nameof(Amount)));
            Patch(h, typeof(CondOwner), "ZeroCondAmount", nameof(Quiet));
            Patch(h, typeof(CondOwner), "SetCondAmount", nameof(Quiet));
            Patch(h, typeof(CollisionManager), "UnregisterShip", nameof(Quiet));
            Patch(h, typeof(Ship), "ToggleVis", nameof(Quiet));
            Patch(h, typeof(Ship), "InitShip", nameof(Materialize));
            Patch(h, typeof(Ship), "GetJSON", nameof(Save));
            Patch(h, typeof(Ship), "GetICOs1", nameof(Items));
            Singleton(h, "Ostranauts.TargetVisualization.TargetVisController", "ClearTargetVis");
            Singleton(h, "GUIRenderTargets", "ResetLoadedPortraits");

            var system = Empty<StarSystem>(); InitializeCollections(system); CrewSim.system = system;
            AIShipManager.dictAIs = new Dictionary<string, List<AIShip>>();
            AccessTools.Field(typeof(CrewSim), "aLoadedShips").SetValue(null, new List<Ship>());
            DataHandler.dictShips = new Dictionary<string, JsonShip>();
            DataHandler.dictCOSaves = new Dictionary<string, JsonCondOwnerSave>();
            DataHandler.mapCOs = new Dictionary<string, CondOwner>();
            DataHandler.dictCOs = new Dictionary<string, JsonCondOwner>();
            var player = Empty<CondOwner>(); Field(player, "_strID", "fixture-player"); CrewSim.coPlayer = player;
            DataHandler.mapCOs[player.strID] = player;
            var crewSim = Empty<CrewSim>();

            foreach (string id in new[] { "OKLG", "VORB", "cargo-destination" }) {
                var live = new ShipSitu(false) { bIsBO = true, bGrounded = true, strBOPORShip = id,
                    vPosx = 1.6, vPosy = -1.4, vVelX = 1.4e-7, vVelY = 4.6e-8 };
                var station = ShipAt(id, live); system.dictShips[id] = station;
                ((Dictionary<string, string>)AccessTools.Field(typeof(StarSystem), "dictShipOwners").GetValue(system))[id] = "fixture-owner";
                var observer = ShipAt("observer-" + id, new ShipSitu(false));
                system.dictShips[observer.strRegID] = observer;
                var target = new TargetData(station); target.ApplyToShip(observer);
                Check(ReferenceEquals(observer.shipSituTarget, live), id + ": normal targeting did not share live situation");
                var copied = new ShipSitu(live, false);
                Check(copied.strBOPORShip == id, id + ": copy lost reference");
                var serialized = JsonMapper.ToJson(live.GetJSON());
                var restored = new ShipSitu(JsonMapper.ToObject<JsonShipSitu>(serialized));
                Check(restored.strBOPORShip == id && restored.bIsBO, id + ": native JSON round-trip lost reference");

                // This executes native SaveToShallow -> DestroyAndReload -> Ship.Destroy,
                // including removal/replacement in the real StarSystem dictionary.
                crewSim.SaveToShallow(station);
                Ship replacement = system.GetShipByRegID(id);
                Check(replacement != null && !ReferenceEquals(replacement, station), id + ": native unload did not replace station");
                Check(replacement.objSS.strBOPORShip == id, id + ": replacement lost its own reference");
                Check(station.bDestroyed && station.objSS == null, id + ": old station was not destroyed");
                Check(ReferenceEquals(observer.shipScanTarget, station), id + ": observer ship reference was unexpectedly rebound");
                Check(ReferenceEquals(observer.shipSituTarget, live) && live.strBOPORShip == null && live.bIsBO && live.vVelX != 0,
                    id + ": unload did not reproduce malformed target");
                Check(copied.strBOPORShip == id && restored.strBOPORShip == id, id + ": independently owned snapshots were damaged");

                observer.json = new JsonShip { strScanTargetID = id, strTargetRegID = id,
                    objSituScanTarget = JsonMapper.ToObject<JsonShipSitu>(JsonMapper.ToJson(observer.shipSituTarget.GetJSON())) };
                observer.CheckTargets();
                Check(ReferenceEquals(observer.shipScanTarget, replacement), id + ": reload did not resolve replacement station");
                Check(observer.shipSituTarget.bIsBO && observer.shipSituTarget.strBOPORShip == null,
                    id + ": native reload did not preserve malformed situation independently of scan ship");
                Console.WriteLine(id + ": native unload cleared shared target; native reload resolved new ship but retained broken snapshot.");
            }
            h.CreateClassProcessor(typeof(Ostrabus.MiscBugFixes.ShipReplacementTargetsPatch)).Patch();
            foreach (string role in new[] { "salvager", "hauler", "interplanetary-cargo", "player-torch", "long-distance" }) {
                string id = "station-" + role;
                var live = new ShipSitu(false) { bIsBO = true, strBOPORShip = id, vPosx = 2, vVelX = 1.4e-7 };
                var station = ShipAt(id, live); system.dictShips[id] = station;
                var observer = ShipAt("traveler-" + role, new ShipSitu(false)); system.dictShips[observer.strRegID] = observer;
                var target = new TargetData(station); target.ApplyToShip(observer);
                observer.shipCombatTarget = observer.shipStationKeepingTarget = observer.shipUndock = station;
                var route = new NavData(observer) { IsTorching = role == "player-torch" };
                route.AddNavDataPoint(new NavDataPoint(1000, new ShipSitu(false) { vPosx = 1 }, 1000, 0, 1));
                route.AddNavDataPoint(new NavDataPoint(role == "long-distance" ? 1e12 : 1000 + 3600 * 24 * 30,
                    new ShipSitu(false) { vPosx = role == "long-distance" ? 1000 : 2 }, 500, 0, 0.5));
                observer.objSS.NavData = route;
                string routeBefore = JsonMapper.ToJson(route.GetJSON());
                var offset = new ShipSitu(live, false) { vBOOffsetx = 1e-7, IsRegionBorderPoint = true };
                var offsetObserver = ShipAt("offset-" + role, new ShipSitu(false)); system.dictShips[offsetObserver.strRegID] = offsetObserver;
                var offsetData = new TargetData(station) { Situ = offset }; offsetData.ApplyToShip(offsetObserver);
                var unrelated = ShipAt("other-" + role, new ShipSitu(false)); system.dictShips[unrelated.strRegID] = unrelated;
                var unrelatedData = new TargetData(unrelated); unrelatedData.ApplyToShip(unrelated);
                for (int repeat = 0; repeat < 2; repeat++) {
                    crewSim.SaveToShallow(station);
                    Ship replacement = system.GetShipByRegID(id);
                    Check(ReferenceEquals(observer.shipScanTarget, replacement) && ReferenceEquals(observer.shipSituTarget, replacement.objSS), role + ": live target references not transferred");
                    Check(ReferenceEquals(observer.TargetData, target) && ReferenceEquals(target.Ship, replacement) && ReferenceEquals(target.Situ, replacement.objSS), role + ": TargetData identity or shared references broken");
                    Check(ReferenceEquals(observer.shipCombatTarget, replacement) && ReferenceEquals(observer.shipStationKeepingTarget, replacement) && ReferenceEquals(observer.shipUndock, replacement), role + ": auxiliary navigation reference dangling");
                    Check(ReferenceEquals(observer.objSS.NavData, route) && JsonMapper.ToJson(route.GetJSON()) == routeBefore, role + ": existing route modified");
                    Check(ReferenceEquals(offsetObserver.shipSituTarget, offset) && ReferenceEquals(offsetData.Situ, offset) && offset.vBOOffsetx == 1e-7 && offset.strBOPORShip == id, role + ": independent offset waypoint modified");
                    Check(ReferenceEquals(offsetObserver.shipScanTarget, replacement) && ReferenceEquals(offsetData.Ship, replacement), role + ": offset target's ship reference dangling");
                    Check(ReferenceEquals(unrelated.TargetData, unrelatedData) && ReferenceEquals(unrelated.shipScanTarget, unrelated) && ReferenceEquals(unrelated.shipSituTarget, unrelated.objSS), role + ": unrelated target modified");
                    Check(observer.targetRegID == id && observer.objSS.vPosx == 0 && observer.objSS.vVelX == 0, role + ": target identity or traveler motion changed");
                    station = replacement;
                }
                var saved = JsonMapper.ToObject<JsonShipSitu>(JsonMapper.ToJson(observer.shipSituTarget.GetJSON()));
                observer.json = new JsonShip { strScanTargetID = id, strTargetRegID = id, objSituScanTarget = saved };
                observer.CheckTargets();
                Check(observer.shipSituTarget.strBOPORShip == id, role + ": saved target was corrupted after patched unload");
                Check(JsonMapper.ToJson(route.GetJSON()) == routeBefore, role + ": reload fixture changed route");
            }
            GuardIntegration(crewSim, system, h);
            Console.WriteLine(checks + " native target lifecycle checks passed.");
            return checks;
        } finally { h.UnpatchSelf(); }
    }
    static void GuardIntegration(CrewSim crewSim, StarSystem system, Harmony h) {
        h.CreateClassProcessor(typeof(Ostrabus.ShipNavigation.RetargetFuelPatch)).Patch();
        h.CreateClassProcessor(typeof(Ostrabus.ShipNavigation.ClearTargetFuelPatch)).Patch();
        var station = ShipAt("VORB", new ShipSitu(false) { bIsBO = true, strBOPORShip = "VORB" }); system.dictShips["VORB"] = station;
        var observer = ShipAt("guard-hauler", new ShipSitu(false)); observer.IsAIShip = true;
        Field(observer, "nLoadState", Ship.Loaded.Shallow); system.dictShips[observer.strRegID] = observer;
        var controller = Empty<AIShip>(); Field(controller, "_shipUs", observer);
        Field(controller, "_aiCharacter", new Program.Pilot { ShipUs = observer, Role = AIType.HaulerDeployer });
        var board = new Blackboard(); Field(controller, "<Blackboard>k__BackingField", board);
        Field(controller, "<HomeStation>k__BackingField", "VORB");
        AIShipManager.dictAIs["guard-fixture"] = new List<AIShip> { controller };
        new TargetData(station).ApplyToShip(observer);
        NavData Route() {
            var route = new NavData(observer);
            route.AddNavDataPoint(new NavDataPoint(StarSystem.fEpoch, new ShipSitu(false), 100, 0, 0));
            route.AddNavDataPoint(new NavDataPoint(StarSystem.fEpoch + 1000, new ShipSitu(false) { vPosx = .00001 }, 50, 0, 0));
            observer.objSS.NavData = route; return route;
        }
        var original = Route(); string originalJson = JsonMapper.ToJson(original.GetJSON());
        board.Set("TripFuelPrediction", "same-logical-leg&50");
        Check(Ostrabus.Venus.VorbNavPlanGuard.ValidateCurrentPlan(observer), "Initial VORB fixture plan rejected");
        crewSim.SaveToShallow(station); station = system.GetShipByRegID("VORB");
        Check(Ostrabus.Venus.VorbNavPlanGuard.ValidateCurrentPlan(observer) && ReferenceEquals(observer.objSS.NavData, original), "VORB guard discarded a healthy route after native station replacement and reference transfer");
        Check(JsonMapper.ToJson(original.GetJSON()) == originalJson, "Replacement changed route contents");
        Check(board.HasKey("TripFuelPrediction"), "Replacement cleared the unchanged leg's fuel prediction");
        new TargetData(station).ApplyToShip(observer);
        Check(ReferenceEquals(observer.objSS.NavData, original), "Reapplying the same station discarded its route");
        var sameCopy = new ShipSitu(station.objSS, false);
        new TargetData(station) { Situ = sameCopy }.ApplyToShip(observer);
        Check(ReferenceEquals(observer.objSS.NavData, original) && Ostrabus.Venus.VorbNavPlanGuard.ValidateCurrentPlan(observer), "Equivalent restored station snapshot discarded its route");
        var shifted = new ShipSitu(sameCopy, false) { vBOOffsetx = 1e-7 };
        new TargetData(station) { Situ = shifted }.ApplyToShip(observer);
        Check(observer.objSS.NavData == null, "Changing an offset with the same station ID kept the old route");
        Check(!board.HasKey("TripFuelPrediction"), "Changed offset retained the previous leg's fuel prediction");
        Route(); Ostrabus.Venus.VorbNavPlanGuard.ValidateCurrentPlan(observer);
        var other = ShipAt("other-station", new ShipSitu(false) { bIsBO = true, strBOPORShip = "other-station" }); system.dictShips[other.strRegID] = other;
        new TargetData(other).ApplyToShip(observer);
        Check(observer.objSS.NavData == null, "Changing stations kept the old route");
        Route(); Ostrabus.Venus.VorbNavPlanGuard.ValidateCurrentPlan(observer);
        Ostrabus.Venus.VorbNavPlanGuard.TryGetState(observer, out var state); state.ManualFallback = true; state.Reason = "test fallback";
        new TargetData(other) { Situ = new ShipSitu(other.objSS, false) }.ApplyToShip(observer);
        Check(Ostrabus.Venus.VorbNavPlanGuard.TryGetState(observer, out var preserved) && ReferenceEquals(state, preserved) && preserved.ManualFallback, "Equivalent target reset the fallback state");
        observer.shipSituTarget = new ShipSitu(other.objSS, false) { vBOOffsety = 2e-7 };
        Ostrabus.Venus.VorbNavPlanGuard.ValidateCurrentPlan(observer);
        Check(observer.objSS.NavData == null && !state.ManualFallback, "Direct waypoint change not invalidated");
        // Conditional-removal investigation: general target repair does not
        // perform route validation or account for fuel added after planning.
        var warning = AccessTools.Method(typeof(Ostrabus.OstrabusPlugin.Sink), "LogWarning");
        h.Patch(warning, prefix: new HarmonyMethod(typeof(LifecycleChecks), nameof(ExpectedGuardWarning)));
        foreach (string defect in new[] { "negative-fuel", "expired", "non-finite", "reverse-time" }) {
            Ostrabus.Venus.VorbNavPlanGuard.TargetChanged(observer);
            var invalid = new NavData(observer);
            double epoch = StarSystem.fEpoch;
            invalid.AddNavDataPoint(new NavDataPoint(defect == "expired" ? epoch - 20 : epoch, new ShipSitu(false), 100, 0, 0));
            invalid.AddNavDataPoint(new NavDataPoint(defect == "expired" ? epoch - 10 : defect == "reverse-time" ? epoch - 1 : epoch + 100,
                new ShipSitu(false) { vPosx = defect == "non-finite" ? double.NaN : .00001 }, defect == "negative-fuel" ? -1 : 50, 0, 0));
            observer.objSS.NavData = invalid;
            Check(!Ostrabus.Venus.VorbNavPlanGuard.ValidateCurrentPlan(observer) && observer.objSS.NavData == null,
                "VORB guard no longer rejects independent " + defect + " input");
            Check(Ostrabus.Venus.VorbNavPlanGuard.TryGetState(observer, out var rejected) && rejected.ManualFallback,
                "Rejected plan did not retain manual fallback");
        }
        h.Unpatch(warning, HarmonyPatchType.Prefix, h.Id);
        Ostrabus.Venus.VorbNavPlanGuard.TargetChanged(observer);
        var fuelPlan = Route();
        double plannedUse = fuelPlan.Origin.FuelLevel - fuelPlan.Destination.FuelLevel;
        Ostrabus.ShipNavigation.RcsReplenishment.Rebase(observer, fuelPlan, 20);
        Check(fuelPlan.Origin.FuelLevel == 120 && fuelPlan.Destination.FuelLevel == 70 &&
            fuelPlan.Origin.FuelLevel - fuelPlan.Destination.FuelLevel == plannedUse,
            "Fuel-addition rebasing is not independent of target-reference integrity");
    }
}
