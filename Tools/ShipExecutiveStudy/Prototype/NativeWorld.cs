using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.Core.Models;
using SolarSystem;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using Ostranauts.Ships.Comms;
using Ostranauts.Ships.Sensors;
using Ostranauts.ShipGUIs.Utilities;
using Ostranauts.Trading;
using UnityEngine;

namespace ShipExecutivePrototype;

// Explicit scene/physics/perception boundary. Command bodies, branch ordering,
// nested retreat, target application, route playback and fuel settlement run
// natively. This fixture does NOT simulate Unity bodies, projectiles or people.
sealed class NativeWorld
{
    internal static NativeWorld Current;
    internal readonly Dictionary<string, Ship> Ships = new Dictionary<string, Ship>();
    // Native physical and deferred registries describe one logical world.
    // Presence flags retain native null-versus-empty initialization semantics.
    internal bool StellarShipsPresent = true, StellarDerelictsPresent = true;
    internal Ship Subject, Home, Enemy;
    internal NavyPilot Pilot;
    internal AIShip AI;
    internal CondOwner Captain;
    internal ShipSitu Patrol;
    internal bool PilotAvailable = true, Threat, LowFuel, Damaged, Retired, RefuseArrival, PlanFails;
    internal int StopCalls, NativeShotRequests, ArrivalCalls, ClearanceCalls, PlanCalls;
    internal double TargetRefreshEpoch;
    internal const double RouteSeconds = 40;
    internal double RouteDuration = RouteSeconds;
    internal Action BeforeArrival;
    // Optional physical-tank provider input, persisted by the fixture world.
    // This is not production fuel storage or a replacement gas simulation.
    internal Dictionary<string, double> PhysicalFuel;
    internal string FuelDonor;
    internal double FuelMinimum;
    internal Dictionary<string, Dictionary<string, double>> DialogueConditions;

    internal static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    internal static void Set(object target, string field, object value) => AccessTools.Field(target.GetType(), field).SetValue(target, value);
    internal static void Back(object target, string field, object value) => Set(target, "<" + field + ">k__BackingField", value);

    internal NativeWorld(double epoch = 1000)
    {
        Current = this;
        StarSystem.fEpoch = epoch;
        CrewSim.system = Empty<StarSystem>();
        CrewSim.system.dictShips = Ships;
        CrewSim.system.dictStellarObjects = new DictionaryArray<string, IStellarObject>();
        CrewSim.system.aBOs = new Dictionary<string, BodyOrbit>();
        CrewSim.coPlayer = Empty<CondOwner>();
        CrewSim.coPlayer.ship = Vessel("PLAYER", .1);
        Home = Vessel("HOME", 0);
        Home.objSS.bIsBO = true;
        Home.objSS.strBOPORShip = "HOME";
        Subject = Vessel("SUBJECT", 1e-7);
        Enemy = Vessel("ENEMY", 2e-7);
        Captain = Empty<CondOwner>();
        Captain.bAlive = true;
        Captain.ship = Subject;
        Patrol = new ShipSitu(false) { vPosx = 8e-7, bBOLocked = true, strBOPORShip = "HOME" };
        AIShipManager.dictAIs = new Dictionary<string, List<AIShip>> { ["HOME"] = new List<AIShip>() };

        Pilot = new NavyPilot("HOME", Subject);
        AI = Empty<AIShip>();
        Set(AI, "_shipUs", Subject);
        Set(AI, "_aiCharacter", Pilot);
        Set(AI, "_behaviorTree", Pilot.BehaviorTree);
        Back(AI, "Blackboard", Pilot.Blackboard);
        Back(AI, "HomeStation", "HOME");
        AIShipManager.dictAIs["HOME"].Add(AI);
        var enemyAI = Empty<AIShip>();
        Set(enemyAI, "_shipUs", Enemy);
        Back(enemyAI, "Blackboard", new Blackboard());
        AIShipManager.dictAIs["HOME"].Add(enemyAI);
        AccessTools.Field(typeof(AIShipManager), "_shipATCLast").SetValue(null, Home);
        AIShipManager.strATCLast = "HOME";
    }



    internal Ship Vessel(string id, double x)
    {
        var ship = Empty<Ship>();
        ship.strRegID = id;
        ship.IsAIShip = id != "PLAYER";
        ship.fShallowRCSRemass = ship.fShallowRCSRemassMax = 300;
        ship.RCSCount = 10;
        Set(ship, "nRCSCountRaw", 10);
        Set(ship, "fRCSExhaustV", 10f);
        Back(ship, "objSS", new ShipSitu(false) { vPosx = x });
        Set(ship, "nLoadState", Ship.Loaded.Shallow);
        Set(ship, "aDocked", new Dictionary<string, Ship>());
        Set(ship, "aCores", new List<CondOwner>());
        Back(ship, "Comms", Empty<Comms>());
        Set(ship.Comms, "_ship", ship);
        Set(ship.Comms, "_messageLog", new List<ShipMessage>());
        Back(ship, "ElectronicSystems", Empty<ElectronicSystems>());
        Back(ship, "WeaponsSystem", Empty<WeaponsSystem>());
        Back(ship, "ShipCO", Empty<CondOwner>());
        ship.json = new JsonShip();
        var items = AccessTools.Property(typeof(JsonShip), "aItems");
        items.SetValue(ship.json, Array.CreateInstance(items.PropertyType.GetElementType(), 100));
        Ships[id] = ship;
        return ship;
    }

    internal Ship ReplaceHome()
    {
        var ship = Vessel("HOME", 0);
        ship.objSS.bIsBO = true;
        return ship;
    }
    internal readonly List<string> SensorEvents = new List<string>();
    internal Ship Resolve(string id) => id != null && Ships.TryGetValue(id, out var ship) ? ship : null;
    internal string NavigationTargetId => Subject.shipScanTarget?.strRegID ??
        (Subject.targetRegID != null && CrewSim.system.dictStellarObjects.TryGetValue(Subject.targetRegID, out var entry) &&
            ReferenceEquals(Subject.shipSituTarget, entry.objSS) ? entry.strID : null);
    internal TargetData ResolveTarget(string id)
    {
        var physical = Resolve(id);
        if (physical != null && !physical.bDestroyed && !physical.bRemove && !physical.HideFromSystem)
            return new TargetData(physical);
        return id != null && CrewSim.system.dictStellarObjects.TryGetValue(id, out var deferred) ? new TargetData(deferred) : null;
    }
    internal JsonStellarShipSave[] SaveStellarShips()
    {
        var entries = CrewSim.system.dictStellarObjects.OfType<StellarShip>().Select(s => new JsonStellarShipSave(s)).ToArray();
        return StellarShipsPresent || entries.Length > 0 ? entries : null;
    }
    internal JsonStellarDerelictSave[] SaveStellarDerelicts()
    {
        var entries = CrewSim.system.dictStellarObjects.OfType<StellarDerelict>().Select(s => new JsonStellarDerelictSave(s)).ToArray();
        return StellarDerelictsPresent || entries.Length > 0 ? entries : null;
    }
    internal void LoadStellar(JsonStellarShipSave[] ships, JsonStellarDerelictSave[] derelicts)
    {
        StellarShipsPresent = ships != null;
        StellarDerelictsPresent = derelicts != null;
        CrewSim.system.dictStellarObjects.Clear();
        foreach (var entry in ships ?? Array.Empty<JsonStellarShipSave>())
            CrewSim.system.dictStellarObjects[entry.strID] = new StellarShip(entry);
        foreach (var entry in derelicts ?? Array.Empty<JsonStellarDerelictSave>())
            CrewSim.system.dictStellarObjects[entry.strID] = new StellarDerelict(entry);
    }
    internal bool IsCanonical(Ship ship, AIShip ai) => ReferenceEquals(Resolve(ship.strRegID), ship) && ReferenceEquals(ai, AI)
        && ReferenceEquals(ai.Ship, ship) && ReferenceEquals(ai.AICharacter, Pilot)
        && ReferenceEquals(AIShipManager.GetAIShipByRegID(ship.strRegID), ai);
    internal void StopControls()
    {
        StopCalls++;
        Subject.StopManeuver(false, false);
        Subject.SetThrust(0);
        Subject.objSS.ResetNavData();
    }
    internal void Coast(double seconds)
    {
        Subject.objSS.vPosx += Subject.objSS.vVelX * seconds;
        Subject.objSS.vPosy += Subject.objSS.vVelY * seconds;
    }

    internal NavData Route(ShipSitu goal)
    {
        PlanCalls++;
        if (PlanFails || goal == null)
            return null;
        var route = new NavData(Subject);
        var a = new ShipSitu(false) { vPosx = Subject.objSS.vPosx, vPosy = Subject.objSS.vPosy, vVelX = Subject.objSS.vVelX, vVelY = Subject.objSS.vVelY };
        var b = new ShipSitu(false) { vPosx = goal.vPosx, vPosy = goal.vPosy, vVelX = goal.vVelX, vVelY = goal.vVelY };
        route.AddNavDataPoint(new NavDataPoint(StarSystem.fEpoch, a, Subject.fShallowRCSRemass, 0, 0, Subject.RCSCount));
        route.AddNavDataPoint(new NavDataPoint(StarSystem.fEpoch + RouteDuration, b, Math.Max(0, Subject.fShallowRCSRemass - 5), 0, 0, Subject.RCSCount));
        return route;
    }

    internal static void Install(Harmony h)
    {
        void Patch(Type type, string method, string handler, Type[] parameters = null) => h.Patch(AccessTools.Method(type, method, parameters), prefix: new HarmonyMethod(typeof(NativeWorld), handler));
        void Getter(Type type, string property, string handler) => h.Patch(AccessTools.PropertyGetter(type, property), prefix: new HarmonyMethod(typeof(NativeWorld), handler));
        Patch(typeof(UnityEngine.Object), "op_Equality", nameof(Equal));
        Patch(typeof(UnityEngine.Object), "op_Inequality", nameof(Unequal));
        Patch(typeof(Comms), nameof(Comms.GetCaptain), nameof(GetCaptain));
        Patch(typeof(DataHandler), nameof(DataHandler.GetCondTrigger), nameof(Quiet), new[] { typeof(string) });
        Patch(typeof(StarSystem), nameof(StarSystem.GetShipOwner), nameof(Owner));
        Patch(typeof(StarSystem), nameof(StarSystem.GetNearestStationRegional), nameof(Nearest));
        Patch(typeof(StarSystem), nameof(StarSystem.GetNearestStation), nameof(Nearest));
        Patch(typeof(AIShipManager), nameof(AIShipManager.GetAllStationsInATCRegion), nameof(Stations));
        Patch(typeof(AIShipManager), nameof(AIShipManager.LowFuel), nameof(IsLow));
        foreach (var method in AccessTools.GetDeclaredMethods(typeof(AIShipManager)).Where(m => m.Name == nameof(AIShipManager.BingoFuelCheck)))
            h.Patch(method, prefix: new HarmonyMethod(typeof(NativeWorld), nameof(No)));
        Patch(typeof(NavyPilot), nameof(NavyPilot.MaxSpeed), nameof(Speed));
        Patch(typeof(NavyPilot), nameof(NavyPilot.GetTarget), nameof(PatrolTarget));
        Patch(typeof(ShipSitu), nameof(ShipSitu.UpdateTime), nameof(UpdateTime));
        Getter(typeof(Ship), nameof(Ship.NavAIManned), nameof(Available));
        Getter(typeof(Ship), nameof(Ship.NavPlayerManned), nameof(No));
        Getter(typeof(Ship), nameof(Ship.RCSAccelMax), nameof(Accel));
        Getter(typeof(Ship), nameof(Ship.HasDockingPorts), nameof(HasPorts));
        Patch(typeof(Ship), nameof(Ship.HasOpenDockingPorts), nameof(Yes));
        Patch(typeof(Ship), nameof(Ship.IsDocked), nameof(No));
        Patch(typeof(Ship), nameof(Ship.IsStation), nameof(IsStation));
        Patch(typeof(Ship), nameof(Ship.GetPeople), nameof(People), new[] { typeof(bool) });
        Patch(typeof(Ship), nameof(Ship.Maneuver), nameof(Maneuver));
        Patch(typeof(Ship), nameof(Ship.StopManeuver), nameof(Stop));
        Patch(typeof(Ship), nameof(Ship.SetThrust), nameof(Thrust));
        Patch(typeof(Ship), nameof(Ship.RemoveGasMass), nameof(RemoveFuel));
        Patch(typeof(CondOwner), nameof(CondOwner.GetCondAmount), nameof(Condition), new[] { typeof(string) });
        Patch(typeof(CondOwner), nameof(CondOwner.GetFactionScore), nameof(FactionScore), new[] { typeof(string) });
        Patch(typeof(CollisionManager), nameof(CollisionManager.GetCollisionDistanceAU), nameof(Collision), new[] { typeof(ShipSitu), typeof(ShipSitu) });
        Patch(typeof(AIShipManager), nameof(AIShipManager.IsOnCollisionCourse), nameof(No));

        Patch(typeof(FlyToPath), "TorchOff", nameof(Quiet));
        Patch(typeof(Ostranauts.ShipGUIs.NavStation.NavModTorchDrive), "GetLimiterSafetyMax", nameof(Limiter));
        Patch(typeof(FlyToPath), nameof(FlyToPath.PlanTrip4), nameof(Plan));
        Patch(typeof(TargetNewIfNone), "GenerateNavData", nameof(Generate));
        Patch(typeof(AreThreatsNearby), nameof(AreThreatsNearby.RunCommand), nameof(Perception));
        Patch(typeof(AmIArmed), nameof(AmIArmed.RunCommand), nameof(Armed));
        Patch(typeof(SetEngagementProfile), nameof(SetEngagementProfile.RunCommand), nameof(Profile));
        Patch(typeof(DoIHaveDecoys), nameof(DoIHaveDecoys.RunCommand), nameof(FailedGuard));
        Patch(typeof(SurrenderDemand), nameof(SurrenderDemand.RunCommand), nameof(FailedGuard));
        Patch(typeof(ElectronicSystems), nameof(ElectronicSystems.TurnOnAllSensors), nameof(SensorOn));
        Patch(typeof(ElectronicSystems), nameof(ElectronicSystems.TurnOffActiveSensors), nameof(SensorOff));
        h.Patch(AccessTools.Constructor(typeof(ShipSignature), new[] { typeof(Ship), typeof(Ship) }), prefix: new HarmonyMethod(typeof(NativeWorld), nameof(Quiet)));
        Patch(typeof(ElectronicSystems), nameof(ElectronicSystems.GetSensorRangeKm), nameof(SensorRange));
        Patch(typeof(WeaponsSystem), nameof(WeaponsSystem.GetWeaponsAndRotations), nameof(Weapons));
        h.Patch(AccessTools.Method(typeof(Shoot), nameof(Shoot.RunCommand)), prefix: new HarmonyMethod(typeof(NativeWorld), nameof(ObserveShoot)));
        Patch(typeof(Comms), nameof(Comms.AIGetClearance), nameof(Clearance));
        Patch(typeof(Comms), nameof(Comms.SendMessage), nameof(Message), new[] { typeof(string), typeof(string), typeof(string) });
        Patch(typeof(CrewSim), nameof(CrewSim.DockAndDespawn), nameof(Arrival));
        Patch(typeof(GUIOrbitDraw), nameof(GUIOrbitDraw.TriggerShipRedraw), nameof(Quiet));
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
    static bool Quiet() => false;
    static bool Yes(ref bool __result)
    {
        __result = true;
        return false;
    }
    static bool No(ref bool __result)
    {
        __result = false;
        return false;
    }
    static bool GetCaptain(ref CondOwner captain, ref bool __result)
    {
        captain = Current.Captain;
        __result = captain != null;
        return false;
    }
    static bool Owner(ref string __result)
    {
        __result = "fixture-faction";
        return false;
    }
    static bool Nearest(ref Ship __result)
    {
        __result = Current.Home;
        return false;
    }
    static bool Stations(ref List<string> __result)
    {
        __result = new List<string> { Current.Home.strRegID };
        return false;
    }
    static bool IsLow(ref bool __result)
    {
        __result = Current.LowFuel;
        return false;
    }
    static bool Speed(ref double __result)
    {
        __result = 1e-9;
        return false;
    }
    static bool PatrolTarget(ref TargetData __result)
    {
        __result = new TargetData(Current.Patrol);
        return false;
    }
    static bool UpdateTime(ShipSitu __instance, double __0)
    {
        Current.TargetRefreshEpoch = __0;
        // Circular targets must execute native phase/velocity refresh. Body
        // ephemerides still have the explicit fixture provider in NavyFixtures.
        return __instance.fOrbitW != 0;
    }
    static bool Available(ref bool __result)
    {
        __result = Current.PilotAvailable;
        return false;
    }
    static bool Accel(ref double __result)
    {
        __result = 1.9016982118751132e-10;
        return false;
    }
    static bool HasPorts(Ship __instance, ref bool __result)
    {
        __result = ReferenceEquals(__instance, Current.Home);
        return false;
    }
    static bool IsStation(Ship __instance, ref bool __result)
    {
        __result = ReferenceEquals(__instance, Current.Home);
        return false;
    }
    static bool People(ref List<CondOwner> __result)
    {
        __result = new List<CondOwner>();
        return false;
    }
    static bool Maneuver(Ship __instance, float fX, float fY, float fR)
    {
        __instance.objSS.vAccRCS = new Vector2(fX * 1e-10f, fY * 1e-10f);
        __instance.objSS.fA = fR;
        return false;
    }
    static bool Stop(Ship __instance)
    {
        __instance.objSS.vAccRCS = default;
        __instance.objSS.fA = 0;
        return false;
    }
    static bool Thrust(Ship __instance)
    {
        __instance.objSS.vAccIn = default;
        return false;
    }
    static bool RemoveFuel(Ship __instance, float __0, ref float __result)
    {
        float paid = Math.Min((float)__instance.fShallowRCSRemass, __0);
        __instance.fShallowRCSRemass -= paid;
        __result = paid;
        return false;
    }
    static bool Condition(string __0, ref double __result)
    {
        __result = __0 == "IsDamaged" && Current.Damaged ? 100 : 0;
        return false;
    }
    static bool FactionScore(ref float __result)
    {
        __result = 100;
        return false;
    }
    static bool Limiter(ref float __result)
    {
        __result = 1;
        return false;
    }

    static bool Collision(ref float __result)
    {
        __result = 0;
        return false;
    }
    static bool Plan(NavDataPoint navDestination, ref NavData __result)
    {
        __result = Current.Route(navDestination.ObjSS);
        return false;
    }
    static bool Generate(TargetData target, ref NavData __result)
    {
        __result = Current.Route(target?.Situ);
        return false;
    }
    static bool Perception(BaseCommand __instance, Blackboard blackboard, ref CommandStatus __result)
    {
        if (Current.Threat)
        {
            blackboard.Set("Enemies", new List<Ship> { Current.Enemy });
            blackboard.Set("ClosestEnemy", Current.Enemy);
        }
        else
        {
            blackboard.Set("Enemies", new List<Ship>());
            blackboard.Set("InCombat", false);
        }
        __result = new CommandStatus(Current.Threat ? CommandCode.Finished : CommandCode.Failure, __instance);
        return false;
    }
    static bool Armed(BaseCommand __instance, ref CommandStatus __result)
    {
        __result = new CommandStatus(CommandCode.Finished, __instance);
        return false;
    }
    static bool Profile(BaseCommand __instance, Blackboard blackboard, ref CommandStatus __result)
    {
        blackboard.Set("EnagementProfile", 1);
        blackboard.Set("EnagementRange", 9.6);
        __result = new CommandStatus(CommandCode.Finished, __instance);
        return false;
    }
    static bool FailedGuard(BaseCommand __instance, ref CommandStatus __result)
    {
        __result = new CommandStatus(CommandCode.Failure, __instance);
        return false;
    }
    static bool SensorOn()
    {
        Current.SensorEvents.Add("on");
        return false;
    }
    static bool SensorOff()
    {
        Current.SensorEvents.Add("off");
        return false;
    }
    static bool SensorRange(ref double __result)
    {
        __result = 1e9;
        return false;
    }
    static bool Weapons(ref List<System.Tuple<DataCOWrapper, float>> __result)
    {
        __result = new List<System.Tuple<DataCOWrapper, float>>();
        return false;
    }
    static void ObserveShoot()
    {
        Current.NativeShotRequests++;
    }
    static bool Clearance(ref bool __result)
    {
        Current.ClearanceCalls++;
        __result = true;
        return false;
    }
    static bool Message(ref bool __result)
    {
        __result = true;
        return false;
    }
    static bool Arrival(Ship __0, Ship __1)
    {
        Current.BeforeArrival?.Invoke();
        Current.ArrivalCalls++;
        if (!Current.RefuseArrival)
        {
            Current.Retired = true;
            __0.HideFromSystem = true;
            Current.Captain.ship = __1;
            AIShipManager.dictAIs["HOME"].Clear();
        }
        return false;
    }
}
