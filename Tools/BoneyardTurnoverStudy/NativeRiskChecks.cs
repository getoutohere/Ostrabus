using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Condowner.Utils;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.Ships.AIPilots;
using SolarSystem;
using UnityEngine;
using Object = UnityEngine.Object;

internal static partial class Program
{
    sealed class TrimProjectionComplete : Exception { }
    static string failedConditionOwner;
    static bool InjectOwnerFailure(CondOwner __instance)
    {
        if (__instance.strID == failedConditionOwner) throw new System.IO.IOException("Offline supplied base-CO cleanup failure.");
        return true;
    }
    static bool Equal(Object __0, Object __1, ref bool __result) { __result = ReferenceEquals(__0, __1); return false; }
    static bool Unequal(Object __0, Object __1, ref bool __result) { __result = !ReferenceEquals(__0, __1); return false; }
    static bool EmptyChildren(ref IEnumerator __result) { __result = Array.Empty<object>().GetEnumerator(); return false; }
    static GameObject FixtureGameObject(Component _) => null;
    static bool FixtureValidation(ref bool __result) { __result = true; return false; }
    static bool ObserveTrim(object message)
    {
        if (message is string text && text.StartsWith("Trimming phase 1 dictCOSaves after:", StringComparison.Ordinal))
            throw new TrimProjectionComplete();
        return false;
    }
    static IEnumerable<CodeInstruction> ReplaceUnityObjectLookup(IEnumerable<CodeInstruction> instructions)
    {
        int replaced = 0;
        var getter = AccessTools.PropertyGetter(typeof(Component), nameof(Component.gameObject));
        foreach (var code in instructions)
        {
            if (code.Calls(getter)) { code.opcode = OpCodes.Call; code.operand = AccessTools.Method(typeof(Program), nameof(FixtureGameObject)); replaced++; }
            yield return code;
        }
        if (replaced != 1) throw new InvalidOperationException("Expected one Unity object lookup in CondOwner.Destroy.");
    }

    // These replace constructor-created empty collections, not lifecycle logic.
    // Unity rendering/objects are supplied separately. Collection contents below
    // are explicit synthetic controls, not a complete saved-game reconstruction.
    static void EmptyCollections(object value)
    {
        foreach (var field in AccessTools.GetDeclaredFields(value.GetType()))
        {
            if (field.IsStatic) continue;
            if (field.FieldType.IsArray) field.SetValue(value, Array.CreateInstance(field.FieldType.GetElementType(), 0));
            else if (typeof(IEnumerable).IsAssignableFrom(field.FieldType) && field.FieldType.GetConstructor(Type.EmptyTypes) != null)
                field.SetValue(value, Activator.CreateInstance(field.FieldType));
        }
    }
    static void Field(object value, string name, object content) => AccessTools.Field(value.GetType(), name).SetValue(value, content);
    static CondOwner RiskCO(string id)
    {
        var co = Empty<CondOwner>();
        EmptyCollections(co);
        Field(co, "_strID", id);
        co.Clock = new Clock();
        co.bFreezeConds = true;
        Field(co, "bTriedGettingPfRef", true);
        Field(co, "_isHumanOrRobot", false);
        DataHandler.mapCOs[id] = co;
        return co;
    }
    static StarSystem RiskWorld()
    {
        var world = new StarSystem();
        CrewSim.system = world;
        CrewSim.objInstance = null;
        CrewSim.coPlayer = null;
        DataHandler.mapCOs = new Dictionary<string, CondOwner>();
        DataHandler.dictCOSaves = new Dictionary<string, JsonCondOwnerSave>();
        AIShipManager.dictAIs = new Dictionary<string, List<AIShip>>();
        FieldStatic(typeof(CrewSim), "aLoadedShips", new List<Ship>());
        CollisionManager.ClearQueue();
        return world;
    }
    static void FieldStatic(Type type, string name, object content) => AccessTools.Field(type, name).SetValue(null, content);
    static Ship RiskHull(string id)
    {
        var ship = Empty<Ship>();
        EmptyCollections(ship);
        ship.strRegID = id;
        ship.json = new JsonShip { strRegID = id, strName = "study-template", aItems = new[] { new JsonItem { strID = id + "-saved-item" } }, aCrew = Array.Empty<JsonItem>() };
        ship.fFirstVisit = 100;
        Field(ship, "nLoadState", Ship.Loaded.Shallow);
        Field(ship, "<objSS>k__BackingField", new ShipSitu(false) { Size = 100, bBOLocked = true, strBOPORShip = "study-anchor" });
        Field(ship, "<ShipCO>k__BackingField", RiskCO(id + "-ship-co"));
        Field(ship.ShipCO, "_objShip", ship);
        ship.tfBGs = Empty<Transform>();
        CrewSim.system.dictShips[id] = ship;
        CollisionManager.RegisterShip(ship);
        CrewSim.AddLoadedShip(ship);
        return ship;
    }

    static void NativeRiskChecks(string work)
    {
        var h = new Harmony("offline.boneyard-turnover-risks");
        try
        {
            h.Patch(AccessTools.Method(typeof(Object), "op_Equality"), prefix: new HarmonyMethod(typeof(Program), nameof(Equal)));
            h.Patch(AccessTools.Method(typeof(Object), "op_Inequality"), prefix: new HarmonyMethod(typeof(Program), nameof(Unequal)));
            h.Patch(AccessTools.Method(typeof(Object), nameof(Object.DestroyImmediate), new[] { typeof(Object) }), prefix: new HarmonyMethod(typeof(Program), nameof(Quiet)));
            h.Patch(AccessTools.Method(typeof(Transform), nameof(Transform.GetEnumerator)), prefix: new HarmonyMethod(typeof(Program), nameof(EmptyChildren)));
            h.Patch(AccessTools.Method(typeof(CondOwner), nameof(CondOwner.ValidateParent)), prefix: new HarmonyMethod(typeof(Program), nameof(FixtureValidation)));
            h.Patch(AccessTools.Method(typeof(CondOwner), nameof(CondOwner.Destroy)), transpiler: new HarmonyMethod(typeof(Program), nameof(ReplaceUnityObjectLookup)));
            h.Patch(AccessTools.Method(typeof(CondOwner), nameof(CondOwner.Destroy)), prefix: new HarmonyMethod(typeof(Program), nameof(InjectOwnerFailure)));
            h.Patch(AccessTools.Method(typeof(Debug), nameof(Debug.LogWarning), new[] { typeof(object) }), prefix: new HarmonyMethod(typeof(Program), nameof(Quiet)));
            NativeHullRemoval();
            NativeDanglingReferences();
            NativeOrphanRemoval();
            NativePartialFailure();
            NativeUnresolvedOccupant();
            StructuralRiskControls();
            NativeSaveTrim(h);
            NativeQueueOrdering(h);
            NativeDeferredPlacement();
            MetadataRiskChecks(h, work);
        }
        finally { h.UnpatchSelf(); CrewSim.objInstance = null; CrewSim.coPlayer = null; }
    }

    static void NativeHullRemoval()
    {
        var world = RiskWorld();
        var hull = RiskHull("BT-VISITED");
        var shipCo = hull.ShipCO;
        ((Dictionary<string, string>)AccessTools.Field(typeof(StarSystem), "dictShipOwners").GetValue(world))[hull.strRegID] = "study-yard";
        var situ = hull.objSS;
        hull.Destroy(isDespawning: false);
        Check(hull.bDestroyed && hull.objSS == null && hull.ShipCO == null && shipCo.bDestroyed,
            "Actual native shallow hull/base-CO destruction did not complete.");
        Check(!world.dictShips.ContainsKey(hull.strRegID) && world.GetShipOwner(hull.strRegID) == "UNREGISTERED",
            "Native destruction left ship/ownership registries.");
        Check(!DataHandler.mapCOs.ContainsKey(shipCo.strID) && CrewSim.GetAllLoadedShips().Count == 0,
            "Native destruction left the base CO or loaded-ship registration.");
        Check(situ.strBOPORShip == null && !world.GetJSONSave().dictShips.Contains(hull.strRegID),
            "Native save still includes the removed hull.");
        var queue = AccessTools.Field(typeof(CollisionManager), "_shipQueue").GetValue(null);
        Check((int)AccessTools.Method(queue.GetType(), "Count").Invoke(queue, null) == 0, "Native collision queue retained a destroyed hull.");
        Console.WriteLine("Risk check: actual native Ship.Destroy/CondOwner.Destroy/RemoveShip remove a visited saved-only shallow hull, base CO, ownership and collision/loaded registries.");
    }

    static void NativeDanglingReferences()
    {
        RiskWorld();
        var hull = RiskHull("BT-TARGET");
        var visitor = RiskHull("BT-VISITOR");
        visitor.shipScanTarget = hull;
        visitor.shipSituTarget = hull.objSS;
        visitor.targetRegID = hull.strRegID;
        visitor.json.strScanTargetID = hull.strRegID;
        hull.Destroy(isDespawning: false);
        Check(ReferenceEquals(visitor.shipScanTarget, hull) && visitor.targetRegID == hull.strRegID &&
            visitor.shipSituTarget != null, "Native destruction no longer retains inbound target references.");
        Check(!visitor.bCheckTargets, "Native destruction now schedules inbound target restoration.");
        visitor.CheckTargets();
        Check(visitor.shipScanTarget == null && visitor.targetRegID == hull.strRegID,
            "Native CheckTargets no longer demonstrates partial reference cleanup.");
        Console.WriteLine("Risk reproduced: native destruction leaves another ship's target references; CheckTargets clears a resolved ship pointer but can retain its target ID/situation.");
    }

    static void NativeOrphanRemoval()
    {
        RiskWorld();
        var hull = RiskHull("BT-ORPHANED");
        var child = RiskCO("BT-orphan-item");
        Field(child, "_objShip", hull);
        Field(child, "_objCOParent", RiskCO("BT-unresolved-parent"));
        var items = AccessTools.Field(typeof(Ship), "mapICOs").GetValue(hull);
        AccessTools.Method(items.GetType(), "Add", new[] { typeof(string), typeof(CondOwner) }).Invoke(items, new object[] { child.strID, child });
        hull.Destroy(isDespawning: false);
        Check(hull.bDestroyed && DataHandler.mapCOs.ContainsKey(child.strID) && !child.bDestroyed && ReferenceEquals(child.ship, hull),
            "Native orphan-item leftover counterexample no longer reproduces.");
        Console.WriteLine("Risk reproduced: a hull with an external-parent item can finish native destruction while its orphan CO remains registered against the destroyed hull.");
    }

    static void NativePartialFailure()
    {
        var world = RiskWorld();
        var hull = RiskHull("BT-PARTIAL-FAILURE");
        var baseCo = hull.ShipCO;
        failedConditionOwner = baseCo.strID;
        try
        {
            hull.Destroy(isDespawning: false);
            throw new InvalidOperationException("Supplied cleanup fault did not interrupt native destruction.");
        }
        catch (System.IO.IOException) { }
        finally { failedConditionOwner = null; }
        Check(!world.dictShips.ContainsKey(hull.strRegID) && hull.bDestroyed && !baseCo.bDestroyed &&
            DataHandler.mapCOs.ContainsKey(baseCo.strID) && hull.objSS != null,
            "Native cleanup fault no longer demonstrates a partially removed hull.");
        Check(!world.GetJSONSave().dictShips.Contains(hull.strRegID), "Partially removed hull unexpectedly remains in native save enumeration.");
        Console.WriteLine("Fault injection: native Destroy is not transactional; a supplied base-CO failure occurs after hull deregistration. Preflight/postconditions and fail-stop handling are required, not a promise of rollback.");
    }

    static void NativeUnresolvedOccupant()
    {
        RiskWorld();
        var hull = RiskHull("BT-OCCUPANCY");
        var person = Empty<PersonSpec>();
        person.strCO = "BT-unresolved-person";
        hull.People.Add(person);
        Check(hull.People.Count == 1 && hull.GetPeople(false).Count == 0,
            "Native resolved-people count no longer hides an unresolved occupancy record.");
        Console.WriteLine("Risk reproduced: native GetPeople returns zero for a hull with a saved, unresolved PersonSpec; occupancy protection must check native/saved rosters.");
    }

    // Investigation prototype for the NEW feature's conservative boundary, not
    // a runtime patch or a complete field-membership/plot/claim resolver.
    static bool StructurallySafe(Ship hull, string player, HashSet<string> obligations)
    {
        if (hull == null || hull.bDestroyed || hull.bRemove || hull.objSS == null || hull.json == null ||
            hull.LoadState != Ship.Loaded.Shallow || hull.Classification != Ship.TypeClassification.Default ||
            hull.objSS.bIsBO || hull.IsAIShip || AIShipManager.GetAIShipByRegID(hull.strRegID) != null ||
            hull.HasDockGroup || hull.IsDocked() || hull.objSS.NavData != null ||
            hull.People == null || hull.People.Count != 0 ||
            hull.json.aCrew?.Length > 0 || hull.json.aShallowPSpecs?.Length > 0 ||
            obligations.Contains(hull.strRegID) || CrewSim.system.GetShipOwner(hull.strRegID) == player) return false;
        var items = AccessTools.Field(typeof(Ship), "mapICOs").GetValue(hull);
        if (items == null || (int)AccessTools.Property(items.GetType(), "Count").GetValue(items) != 0) return false;
        foreach (var other in CrewSim.system.dictShips.Values)
        {
            if (other == null || ReferenceEquals(other, hull)) continue;
            if (ReferenceEquals(other.shipScanTarget, hull) || ReferenceEquals(other.shipCombatTarget, hull) ||
                ReferenceEquals(other.shipStationKeepingTarget, hull) || ReferenceEquals(other.shipUndock, hull) ||
                ReferenceEquals(other.shipSituTarget, hull.objSS) || other.targetRegID == hull.strRegID ||
                ReferenceEquals(other.TargetData?.Ship, hull) || other.TargetData?.TargetRegId == hull.strRegID ||
                other.json?.strScanTargetID == hull.strRegID || other.json?.strCombatTargetID == hull.strRegID ||
                other.json?.strStationKeepingTargetID == hull.strRegID || other.json?.strUndockID == hull.strRegID ||
                other.json?.strTargetRegID == hull.strRegID) return false;
        }
        return true;
    }
    static void StructuralRiskControls()
    {
        RiskWorld();
        var obligations = new HashSet<string>();
        var hull = RiskHull("BT-ELIGIBILITY");
        var other = RiskHull("BT-OTHER");
        Check(StructurallySafe(hull, "BT-player", obligations), "A visited saved-only shallow hull was excluded by the structural prototype.");
        Field(hull, "nLoadState", Ship.Loaded.Full);
        Check(!StructurallySafe(hull, "BT-player", obligations), "A currently full-loaded hull was admitted.");
        Field(hull, "nLoadState", Ship.Loaded.Shallow);
        hull.People.Add(Empty<PersonSpec>());
        Check(!StructurallySafe(hull, "BT-player", obligations), "Unresolved occupancy was admitted.");
        hull.People.Clear();
        hull.json.aCrew = new[] { new JsonItem { strID = "saved-person" } };
        Check(!StructurallySafe(hull, "BT-player", obligations), "Saved crew payload was admitted.");
        hull.json.aCrew = Array.Empty<JsonItem>();
        hull.json.aShallowPSpecs = new[] { new JsonItem { strID = "shallow-person" } };
        Check(!StructurallySafe(hull, "BT-player", obligations), "Saved shallow-person payload was admitted.");
        hull.json.aShallowPSpecs = null;
        var items = AccessTools.Field(typeof(Ship), "mapICOs").GetValue(hull);
        var liveItem = RiskCO("BT-live-item");
        AccessTools.Method(items.GetType(), "Add", new[] { typeof(string), typeof(CondOwner) }).Invoke(items, new object[] { liveItem.strID, liveItem });
        Check(!StructurallySafe(hull, "BT-player", obligations), "Partially loaded runtime inventory was admitted.");
        AccessTools.Method(items.GetType(), "Clear", Type.EmptyTypes).Invoke(items, null);
        obligations.Add(hull.strRegID);
        Check(!StructurallySafe(hull, "BT-player", obligations), "A supplied plot/bounty obligation was admitted.");
        obligations.Clear();
        ((Dictionary<string, string>)AccessTools.Field(typeof(StarSystem), "dictShipOwners").GetValue(CrewSim.system))[hull.strRegID] = "BT-player";
        Check(!StructurallySafe(hull, "BT-player", obligations), "Native player ownership was admitted.");
        ((Dictionary<string, string>)AccessTools.Field(typeof(StarSystem), "dictShipOwners").GetValue(CrewSim.system)).Clear();
        hull.IsAIShip = true;
        Check(!StructurallySafe(hull, "BT-player", obligations), "A registered AI-mode hull was admitted.");
        hull.IsAIShip = false;
        Field(hull.objSS, "_dockGroup", new DockGroup());
        Check(!StructurallySafe(hull, "BT-player", obligations), "Dock/tether group membership was admitted.");
        Field(hull.objSS, "_dockGroup", null);
        foreach (string field in new[] { "shipScanTarget", "shipCombatTarget", "shipStationKeepingTarget", "shipUndock" })
        {
            Field(other, field, hull);
            Check(!StructurallySafe(hull, "BT-player", obligations), "Incoming native pointer was admitted: " + field);
            Field(other, field, null);
        }
        other.shipSituTarget = hull.objSS;
        Check(!StructurallySafe(hull, "BT-player", obligations), "Incoming situation reference was admitted.");
        other.shipSituTarget = null;
        other.TargetData = new Ostranauts.Ships.TargetData(hull);
        Check(!StructurallySafe(hull, "BT-player", obligations), "Structured native TargetData was admitted.");
        other.TargetData = null;
        other.targetRegID = hull.strRegID;
        Check(!StructurallySafe(hull, "BT-player", obligations), "Incoming current target ID was admitted.");
        other.targetRegID = null;
        foreach (string property in new[] { "strScanTargetID", "strCombatTargetID", "strStationKeepingTargetID", "strUndockID", "strTargetRegID" })
        {
            typeof(JsonShip).GetProperty(property).SetValue(other.json, hull.strRegID);
            Check(!StructurallySafe(hull, "BT-player", obligations), "Incoming saved target ID was admitted: " + property);
            typeof(JsonShip).GetProperty(property).SetValue(other.json, null);
        }
        Check(StructurallySafe(hull, "BT-player", obligations), "Temporary protections permanently excluded the survivor.");
        Console.WriteLine("Structural prototype: visited saved-only hulls pass; full/partial loading, unresolved/saved occupants, native ownership, obligations, groups and incoming target forms are retained.");
    }

    static void NativeSaveTrim(Harmony h)
    {
        var world = RiskWorld();
        var keep = RiskHull("BT-KEEP");
        DataHandler.dictCOSaves["BT-old-item"] = new JsonCondOwnerSave { strID = "BT-old-item", strRegIDLast = "BT-REMOVED" };
        DataHandler.dictCOSaves["BT-kept-item"] = new JsonCondOwnerSave { strID = "BT-kept-item", strRegIDLast = keep.strRegID };
        CrewSim.coPlayer = RiskCO("BT-player");
        var log = AccessTools.Method(typeof(Debug), nameof(Debug.Log), new[] { typeof(object) });
        h.Patch(log, prefix: new HarmonyMethod(typeof(Program), nameof(ObserveTrim)));
        try
        {
            AccessTools.Method(typeof(LoadManager), "SaveGameData").Invoke(Empty<LoadManager>(), new object[] { "offline-unused-path/" });
            throw new InvalidOperationException("Expected to stop the save study after its native orphan trimming phase.");
        }
        catch (TargetInvocationException error) when (error.InnerException is TrimProjectionComplete) { }
        finally { h.Unpatch(log, HarmonyPatchType.Prefix, h.Id); }
        Check(!DataHandler.dictCOSaves.ContainsKey("BT-old-item") && DataHandler.dictCOSaves.ContainsKey("BT-kept-item"),
            "Actual native save trimming retained removed-owner CO data or lost a surviving owner.");
        Check(world.dictShips.ContainsKey(keep.strRegID), "Native trim removed a surviving hull.");
        Console.WriteLine("Native save projection: actual SaveGameData trims orphan saved COs after their owner hull is absent; the study stops immediately after that phase, before Unity save capture.");
    }

    static void NativeQueueOrdering(Harmony h)
    {
        var spawn = AccessTools.Method(typeof(EnvironmentEntities), "SpawnShipFromStellarObject",
            new[] { typeof(Ostranauts.Ships.IStellarObject), typeof(string) });
        h.Patch(spawn, prefix: new HarmonyMethod(typeof(Program), nameof(ObserveQueuedSpawn)));
        var world = RiskWorld();
        var anchor = Empty<BodyOrbit>();
        anchor.strName = "study-anchor";
        anchor.fPeriod = 31556926;
        anchor.dTimeCalcLast = anchor.dVelCalcLast = StarSystem.fEpoch;
        world.aBOs[anchor.strName] = anchor;
        var station = RiskHull("BT-REGION");
        station.objSS.bIsBO = true;
        station.objSS.bIsRegion = true;
        var old = new StellarDerelict(new JsonStellarDerelictSave
            { strID = "BT-QUEUED-OLD", strLoot = "study", nSize = 100, nMapID = 100, strParent = anchor.strName });
        var replacement = new StellarDerelict(new JsonStellarDerelictSave
            { strID = "BT-QUEUED-NEW", strLoot = "study", nSize = 100, nMapID = 100, strParent = anchor.strName });
        var queue = (Queue<string>)AccessTools.Field(typeof(EnvironmentEntities), "_derelictSpawnQueue").GetValue(null);

        EnvironmentEntities.ClearDerelictSpawnQueue();
        world.dictStellarObjects[replacement.strID] = replacement;
        EnvironmentEntities.LoadEnvironmentForRegion(station.strRegID);
        Check(queue.Count == 1 && queue.Peek() == replacement.strID, "Replacement before native environment load did not queue its new ID.");
        lastQueued = null;
        EnvironmentEntities.ProcessDerelictSpawnQueue();
        Check(lastQueued == replacement.strID && queue.Count == 0, "Native spawn queue did not process the pre-load replacement.");

        world.dictStellarObjects.Remove(replacement.strID);
        world.dictStellarObjects[old.strID] = old;
        EnvironmentEntities.LoadEnvironmentForRegion(station.strRegID);
        world.dictStellarObjects.Remove(old.strID);
        world.dictStellarObjects[replacement.strID] = replacement;
        lastQueued = null;
        EnvironmentEntities.ProcessDerelictSpawnQueue();
        Check(lastQueued == null && queue.Count == 0 && world.dictStellarObjects.ContainsKey(replacement.strID),
            "Expected the native queue to miss a replacement published after its old ID was queued.");

        EnvironmentEntities.LoadEnvironmentForRegion(station.strRegID);
        EnvironmentEntities.LoadEnvironmentForRegion(station.strRegID);
        Check(queue.Count == 2 && queue.All(id => id == replacement.strID), "Native repeated region-load calls no longer duplicate queued IDs.");
        EnvironmentEntities.UnloadEnvironmentForRegion(null, null);
        Check(queue.Count == 0, "Native environment-unload boundary failed to clear pending spawn IDs.");
        Console.WriteLine("Native queue risks: replacement must precede environment loading; late replacement is not materialized, repeated load calls can queue duplicates, and unload clears pending IDs.");
    }
    static string lastQueued;
    static void NativeDeferredPlacement()
    {
        var world = RiskWorld();
        var anchor = Empty<BodyOrbit>();
        anchor.strName = "BT-PLACEMENT-ANCHOR";
        anchor.fPeriod = 31556926;
        anchor.dTimeCalcLast = anchor.dVelCalcLast = StarSystem.fEpoch;
        world.aBOs[anchor.strName] = anchor;
        var first = new ShipSitu(false) { Size = 100 };
        Check(world.SetSituToRandomSafeCoords(first, 1e-5, 1e-5, 0, 0, suppressLog: true), "Native initial placement fixture failed.");
        var deferred = new StellarDerelict(new JsonStellarDerelictSave
            { strID = "BT-DEFERRED-OBSTACLE", strLoot = "study", strParent = anchor.strName, nSize = 100 });
        deferred.objSS.CopyFrom(first, false);
        deferred.objSS.LockToBO(anchor);
        world.dictStellarObjects[deferred.strID] = deferred;
        var next = new ShipSitu(false) { Size = 100 };
        // The host's deterministic random boundary produces the same candidate.
        bool accepted = world.SetSituToRandomSafeCoords(next, 1e-5, 1e-5, 0, 0, suppressLog: true);
        Check(accepted && next.GetRangeTo(deferred.objSS) < next.GetRadiusAU() + deferred.objSS.GetRadiusAU(),
            "Native safe-coordinate helper no longer ignores overlapping deferred wrecks.");
        Console.WriteLine("Native placement risk: an actual successful SetSituToRandomSafeCoords call can overlap a deferred wreck; check deferred and staged replacements as well as native physical ships.");
    }
    static bool ObserveQueuedSpawn(Ostranauts.Ships.IStellarObject __0, ref Ship __result)
    {
        lastQueued = __0.strID;
        __result = null; // Ship construction/Unity scene is the supplied boundary.
        return false;
    }
}
