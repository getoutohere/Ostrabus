using System;
using System.Collections.Generic;
using System.Linq;
using Ostranauts.Core;
using Ostranauts.Core.Models;
using Ostranauts.Trading;
using Ostranauts.Ships.AIPilots;
using Ostranauts.ShipGUIs.Utilities;
using Ostranauts.Ships;
using Ostranauts.Ships.Commands;
using HarmonyLib;

namespace ShipExecutivePrototype;

// World data and Unity presentation fixtures only. Production service scopes,
// dynamics, timelines, producer/stock algorithms and their hooks are not mocked.
sealed class WorldServiceFixtures : IDisposable
{
    readonly Harmony hooks = new Harmony("offline.executive-world.data");
    internal static WorldServiceFixtures Current;
    internal readonly NativeWorld World;
    internal readonly MarketManager Manager;
    internal readonly Dictionary<string, ShipMarket> Markets = new Dictionary<string, ShipMarket>();
    internal int TradeRequests, TraderRefreshes;
    internal readonly List<string> Errors = new List<string>();
    internal readonly BodyOrbit Body;
    readonly BepInEx.Logging.ManualLogSource previousLogger;
    readonly object oldCollections, oldCos;
    readonly List<System.Reflection.MethodInfo> arrivalFixture = new List<System.Reflection.MethodInfo>();
    internal readonly Dictionary<string, int> Arrivals = new Dictionary<string, int>();
    internal readonly Dictionary<string, int> Playbacks = new Dictionary<string, int>();
    internal readonly Dictionary<string, int> NativeWrappers = new Dictionary<string, int>();
    internal int MixedPlaybackOwners;

    internal WorldServiceFixtures(NativeWorld world, bool resetSubject = true)
    {
        Current = this;
        World = world;
        var logger = AccessTools.Field(typeof(Ostrabus.OstrabusPlugin), "Logger");
        previousLogger = (BepInEx.Logging.ManualLogSource)logger.GetValue(null);
        var log = new BepInEx.Logging.ManualLogSource("offline-world-services");
        log.LogEvent += (_, e) => { if ((e.Level & (BepInEx.Logging.LogLevel.Error | BepInEx.Logging.LogLevel.Warning)) != 0) Errors.Add(e.Data.ToString()); };
        logger.SetValue(null, log);
        Body = NativeWorld.Empty<BodyOrbit>();
        Body.strName = "HOME";
        Body.fMass = 1e18;
        Body.fPeriod = 31556926;
        Body.fRadius = 1e-12;
        Body.aAtmospheres = Array.Empty<JsonAtmosphere>();
        CrewSim.system.aBOs["HOME"] = Body;
        CrewSim.system.boStar = Body;
        world.Home.objSS.vPosx = world.Home.objSS.vBOOffsetx = 1e-5;
        if (resetSubject)
            world.Subject.objSS.vPosx = 1.1e-5;
        world.Enemy.objSS.vPosx = 1.2e-5;
        world.Patrol.vPosx = world.Patrol.vBOOffsetx = 1.3e-5;
        Manager = NativeWorld.Empty<MarketManager>();
        NativeWorld.Set(Manager, "_market", Markets);
        NativeWorld.Set(Manager, "_regionalMarketMap", new Dictionary<string, List<string>>());
        NativeWorld.Set(Manager, "_init", true);
        NativeWorld.Set(Manager, "_lastUpdateTime", StarSystem.fEpoch);
        AccessTools.Field(typeof(AIShipManager), "fTimeOfNextTradeCheck").SetValue(null, StarSystem.fEpoch + 7200);
        Patch(AccessTools.PropertyGetter(typeof(MonoSingleton<MarketManager>), "Instance"), nameof(MarketManagerInstance));
        Patch(AccessTools.PropertyGetter(typeof(ShipMarket), "BlockadeDiscount"), nameof(Discount));
        Patch(AccessTools.Method(typeof(ShipMarket), "RequestTraderUpdate"), nameof(Refresh));
        Patch(AccessTools.Method(typeof(MarketManager), "GetTradeRoute"), nameof(Trades));
        hooks.Patch(AccessTools.Method(typeof(NavData), "TimeAdvance"), prefix: new HarmonyMethod(typeof(WorldServiceFixtures), nameof(Playback)));
        hooks.Patch(AccessTools.Method(typeof(AIShip), "AddCommand"), prefix: new HarmonyMethod(typeof(WorldServiceFixtures), nameof(Queued)));
        var arrival = AccessTools.Method(typeof(CrewSim), "DockAndDespawn");
        foreach (var p in Harmony.GetPatchInfo(arrival).Prefixes.Where(p => p.owner == "offline.executive-prototype.world"))
            arrivalFixture.Add(p.PatchMethod);
        hooks.Unpatch(arrival, HarmonyPatchType.Prefix, "offline.executive-prototype.world");
        Patch(arrival, nameof(Arrive));
        oldCollections = DataHandler.dictDataCoCollections;
        oldCos = DataHandler.dictDataCOs;
        DataHandler.dictDataCOs = new Dictionary<string, DataCO>();
        DataHandler.dictDataCoCollections = new Dictionary<string, DataCoCollection>();
        foreach (string name in new[] { "Metal", "Product" })
        {
            var co = NativeWorld.Empty<DataCO>();
            co.JCO = new JsonCondOwner { strName = name.ToLowerInvariant() };
            DataHandler.dictDataCOs[co.Name] = co;
            var collection = new DataCoCollection(new JsonDCOCollection { strName = name, aIncludeCOs = new[] { co.Name } });
            NativeWorld.Set(collection, "_avgPrice", 10d);
            NativeWorld.Set(collection, "_avgMass", 1d);
            DataHandler.dictDataCoCollections[name] = collection;
        }
    }

    void Patch(System.Reflection.MethodBase method, string name) => hooks.Patch(method, prefix: new HarmonyMethod(typeof(WorldServiceFixtures), name));
    static bool MarketManagerInstance(ref MarketManager __result)
    {
        __result = Current.Manager;
        return false;
    }
    static bool Discount(ref float __result)
    {
        __result = 0;
        return false;
    }
    static bool Refresh()
    {
        Current.TraderRefreshes++;
        return false;
    }
    static bool Trades(ref List<TradeRouteDTO> __result)
    {
        Current.TradeRequests++;
        __result = new List<TradeRouteDTO>();
        return false;
    }
    static void Playback(NavData __instance)
    {
        var ship = (Ship)AccessTools.Field(typeof(NavData), "_shipUs").GetValue(__instance);
        Current.Playbacks.TryGetValue(ship.strRegID, out int count);
        Current.Playbacks[ship.strRegID] = count + 1;
        object economy = RetainedWorldServices.Scope("Economy.SffEconomy");
        if (economy != null && (bool)AccessTools.Method(economy.GetType(), "Owns").Invoke(economy, new object[] { ship }))
            Current.MixedPlaybackOwners++;
    }
    static void Queued(AIShip __instance, ICommand command)
    {
        if (!(command is OneShotWrapperCommand))
            return;
        string id = __instance.Ship.strRegID;
        Current.NativeWrappers.TryGetValue(id, out int count);
        Current.NativeWrappers[id] = count + 1;
    }

    static bool Arrive(Ship shipAI, Ship shipStation)
    {
        var current = Current;
        var world = current.World;
        if (ReferenceEquals(shipAI, world.Subject))
            world.BeforeArrival?.Invoke();
        current.Arrivals.TryGetValue(shipAI.strRegID, out int n);
        current.Arrivals[shipAI.strRegID] = n + 1;
        shipAI.HideFromSystem = true;
        foreach (var ais in AIShipManager.dictAIs.Values)
            ais.RemoveAll(a => ReferenceEquals(a.Ship, shipAI));
        if (ReferenceEquals(shipAI, world.Subject))
        {
            world.ArrivalCalls++;
            world.Retired = true;
            world.Captain.ship = shipStation;
        }
        return false;
    }

    internal Producer Production(string name, string output, float perHour, string input = null) => new Producer(new JsonProductionMap
    {
        strName = name,
        strOutputCollection = output,
        fOutputPerHour = perHour,
        nOutputPerRun = 1,
        aInputCollections = input == null ? Array.Empty<string>() : new[] { input + "=1" }
    })
    {
        LastUpdateTime = StarSystem.fEpoch
    };

    internal ShipMarket Market(string id, Producer producer = null, int metal = 0)
    {
        var config = NativeWorld.Empty<MarketActorConfig>();
        config.COwnerId = id + "-trader";
        NativeWorld.Set(config, "_productionMaps", producer == null ? new List<Producer>() : new List<Producer> { producer });
        config.Stock = new Dictionary<string, List<MarketItem>>();
        config.Stock["Metal"] = Enumerable.Range(0, metal).Select(_ => new MarketItem { COName = "metal", COCollectionName = "Metal" }).ToList();
        config.MaxVirtualInventorySize = new Dictionary<string, int> { ["Metal"] = 100, ["Product"] = 100 };
        var market = new ShipMarket(id);
        market.AddMarketConfig(config.COwnerId, config);
        market.BuildConfig();
        Markets[id] = market;
        return market;
    }

    internal Ship Cargo(double duration = 4000, int metal = 4)
    {
        var ship = World.Vessel("CARGO", 1.4e-5);
        var pilot = new HaulerCargoPilot("HOME", ship, null);
        var ai = NativeWorld.Empty<AIShip>();
        NativeWorld.Set(ai, "_shipUs", ship);
        NativeWorld.Set(ai, "_aiCharacter", pilot);
        NativeWorld.Set(ai, "_behaviorTree", pilot.BehaviorTree);
        NativeWorld.Back(ai, "Blackboard", pilot.Blackboard);
        NativeWorld.Back(ai, "HomeStation", "HOME");
        AIShipManager.dictAIs["HOME"].Add(ai);
        new TargetData(World.Home).ApplyToShip(ship);
        var route = new NavData(ship);
        route.AddNavDataPoint(new NavDataPoint(StarSystem.fEpoch, new ShipSitu(ship.objSS, false), 300, 0, 100));
        route.AddNavDataPoint(new NavDataPoint(StarSystem.fEpoch + duration, new ShipSitu(World.Home.objSS, false), 290, 0, 90));
        ship.objSS.NavData = route;
        Market(ship.strRegID, metal: metal);
        return ship;
    }

    public void Dispose()
    {
        hooks.UnpatchSelf();
        var original = new Harmony("offline.executive-prototype.world");
        foreach (var prefix in arrivalFixture)
            original.Patch(AccessTools.Method(typeof(CrewSim), "DockAndDespawn"), prefix: new HarmonyMethod(prefix));
        DataHandler.dictDataCoCollections = (Dictionary<string, DataCoCollection>)oldCollections;
        DataHandler.dictDataCOs = (Dictionary<string, DataCO>)oldCos;
        AccessTools.Field(typeof(Ostrabus.OstrabusPlugin), "Logger").SetValue(null, previousLogger);
    }
}
