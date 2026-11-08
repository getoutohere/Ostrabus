using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using HarmonyLib;
using Newtonsoft.Json;
using Ostrabus.Orbital;
using Ostrabus.Orbital.Economy;
using Ostranauts.Core;
using Ostranauts.Core.Models;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using Ostranauts.ShipGUIs.Utilities;
using Ostranauts.Trading;

static partial class EconomyChecks {
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Field(object target, string name, object value) => AccessTools.Field(target.GetType(), name).SetValue(target, value);
    static MarketManager manager;
    static readonly Dictionary<string, ShipMarket> markets = new Dictionary<string, ShipMarket>();
    static readonly Dictionary<string, Ship> hulls = new Dictionary<string, Ship>();
    static double start = 1000000;
    static int serial, planCalls, syncs, movedFreighters;
    static bool rejectPlan, throwPlan;
    static Func<FlyToPath,NavDataPoint,NavDataPoint,NavData> stagedPlan;
    static bool Manager(ref MarketManager __result) { __result = manager; return false; }
    static bool Blockade(ref float __result) { __result = 0; return false; }
    static bool Station(Ship __instance, ref bool __result) { __result = __instance.strRegID == "SOURCE" || __instance.strRegID == "DEST"; return false; }
    static bool Radius(ref float __result) { __result = .000000001f; return false; }
    static bool Fuel(Ship __instance, ref double __result) { __result = __instance.fShallowRCSRemass; return false; }
    static bool Sync() { syncs++; return false; }
    static bool Registry(string __0, ref Ship __result) { hulls.TryGetValue(__0 ?? "", out __result); return false; }
    static bool Body(string strName, ref BodyOrbit __result) { CrewSim.system.aBOs.TryGetValue(strName, out __result); return false; }
    static bool Loot(ref string __result) { __result = "FixtureCargo"; return false; }
    static bool Pod(ref MarketActorConfig __result) { __result = Config("pod", null, 100); __result.MaxVirtualInventorySize.Clear(); Field(__result, "<CargoSpecs>k__BackingField", new List<JsonCargoSpec> { new JsonCargoSpec { strName = "standard", bIsDefault = true } }); return false; }
    static bool ShipCo(ref CondOwner __result) { __result = Empty<CondOwner>(); return false; }
    static bool HasCond(ref bool __result) { __result = false; return false; }
    static bool SffActive(ref bool __result) { __result = true; return false; }
    static void Motion(Ship ship) { if (ship.strRegID.StartsWith("CARGO")) movedFreighters++; }
    static IEnumerable<CodeInstruction> MotionBody(IEnumerable<CodeInstruction> _) => new[] {
        new CodeInstruction(System.Reflection.Emit.OpCodes.Ldarg_2),
        new CodeInstruction(System.Reflection.Emit.OpCodes.Call, AccessTools.Method(typeof(EconomyChecks), nameof(Motion))),
        new CodeInstruction(System.Reflection.Emit.OpCodes.Ret)
    };
    static bool World(StarSystem __instance, double fTimeDelta) {
        StarSystem.fEpoch += fTimeDelta;
        foreach (var ship in hulls.Values.ToArray()) AccessTools.Method(typeof(StarSystem), "UpdateShip").Invoke(__instance, new object[] { fTimeDelta, ship });
        AIShipManager.FFWD(fTimeDelta);
        AIShipManager.CheckTradeRoutes();
        MarketManager.Run();
        return false;
    }
    static bool Constructor(AIShip __instance, string stationID) {
        var ship = Hull("CARGO" + ++serial); ship.MarketConfigs = new Dictionary<string, string> { { "pod", "fixture" } };
        ship.fShallowRCSRemass = 100; ship.fShallowFusionRemain = 100;
        var character = Empty<HaulerCargoPilot>(); Field(character, "<ShipUs>k__BackingField", ship);
        Field(character, "<PriorityQueue>k__BackingField", new QueuedCommandsNode("queue", character));
        Field(__instance, "_shipUs", ship); Field(__instance, "_aiCharacter", character);
        Field(__instance, "<HomeStation>k__BackingField", stationID);
        return false;
    }
    static bool Register(AIShip ship) { AIShipManager.dictAIs["INTERREGIONAL"].Add(ship); return false; }
    static bool Despawn(Ship shipAI, Ship shipStation) { shipAI.HideFromSystem = true; AIShipManager.dictAIs["INTERREGIONAL"].RemoveAll(a => ReferenceEquals(a.Ship, shipAI)); return false; }
    static bool Destroy(Ship __instance) { __instance.bDestroyed = true; return false; }
    static bool Plan(FlyToPath __instance, NavDataPoint navOrigin, NavDataPoint navDestination, ref NavData __result) {
        planCalls++;
        if (stagedPlan != null) { __result=stagedPlan(__instance,navOrigin,navDestination); return false; }
        if (throwPlan) throw new InvalidOperationException("fixture planner failure");
        if (rejectPlan) { __result = null; return false; }
        var ship = (Ship)AccessTools.Property(typeof(BaseCommand), "ShipUs").GetValue(__instance);
        __result = new NavData(ship);
        for (int i = 0; i < 86; i++) {
            double t = i / 85d;
            var point = new ShipSitu(navDestination.ObjSS, false);
            point.bIsBO = point.bBOLocked = point.bOrbitLocked = false;
            // Fixture flight takes four hours; finish meets the real native docking distance/speed.
            point.vPosx = navOrigin.ObjSS.vPosx + (navDestination.ObjSS.vPosx - navOrigin.ObjSS.vPosx) * t;
            point.vPosy = navOrigin.ObjSS.vPosy + (navDestination.ObjSS.vPosy - navOrigin.ObjSS.vPosy) * t;
            __result.AddNavDataPoint(new NavDataPoint(navOrigin.ArrivalTime + 14400 * t, point, 100, 0, 100));
        }
        return false;
    }
    static Ship Hull(string id) {
        var ship = Empty<Ship>(); ship.strRegID = id; ship.IsAIShip = true;
        Field(ship, "nLoadState", Ship.Loaded.Shallow); Field(ship, "<objSS>k__BackingField", new ShipSitu(false));
        ship.objSS.strBOPORShip = "Sol";
        Field(ship, "aDocked", new Dictionary<string, Ship>());
        hulls[id] = ship; CrewSim.system.dictShips[id] = ship;
        return ship;
    }
    static DataCoCollection Collection(string name) {
        var collection = new DataCoCollection(new JsonDCOCollection { strName = name });
        Field(collection, "_avgPrice", 10d); Field(collection, "_avgMass", 1d);
        return collection;
    }
    static MarketActorConfig Config(string name, Producer producer, int limit) {
        var config = Empty<MarketActorConfig>(); config.COwnerId = name;
        Field(config, "_productionMaps", producer == null ? new List<Producer>() : new List<Producer> { producer });
        config.Stock = new Dictionary<string, List<MarketItem>>();
        config.MaxVirtualInventorySize = new Dictionary<string, int> { { "Metal", limit }, { "Product", limit } };
        return config;
    }
    static ShipMarket Market(string id, Producer producer, int initialMetal) {
        var market = new ShipMarket(id); var config = Config(id + "trader", producer, 100);
        config.Stock["Metal"] = Enumerable.Range(0, initialMetal).Select(_ => new MarketItem { COCollectionName = "Metal", COName = "metal" }).ToList();
        market.AddMarketConfig(config.COwnerId, config); market.BuildConfig(); markets[id] = market;
        return market;
    }
    static Producer Producer(string name, string output, float rate, string input = null) => new Producer(new JsonProductionMap {
        strName = name, strOutputCollection = output, fOutputPerHour = rate, nOutputPerRun = 1,
        aInputCollections = input == null ? Array.Empty<string>() : new[] { input + "=1" }
    }) { LastUpdateTime = start };
    static void Setup() {
        serial = planCalls = syncs = 0; rejectPlan = throwPlan = false; stagedPlan=null; hulls.Clear(); markets.Clear();
        CrewSim.system = Empty<StarSystem>(); CrewSim.system.dictShips = new Dictionary<string, Ship>();
        var root = Empty<BodyOrbit>(); root.strName = "Sol"; root.fPeriod = 31556926;
        CrewSim.system.aBOs = new Dictionary<string, BodyOrbit> { { "Sol", root } };
        CrewSim.coPlayer = Empty<CondOwner>();
        Field(CrewSim.coPlayer, "m_CachedPtr", new IntPtr(1));
        AIShipManager.strATCLast = "SOURCE"; AIShipManager.dictAIs = new Dictionary<string, List<AIShip>> { { "INTERREGIONAL", new List<AIShip>() } };
        manager = Empty<MarketManager>(); Field(manager, "_market", markets); Field(manager, "_regionalMarketMap", new Dictionary<string, List<string>>());
        Field(manager, "m_CachedPtr", new IntPtr(1));
        Field(manager, "_init", true); Field(manager, "_lastUpdateTime", start);
        AccessTools.Field(typeof(AIShipManager), "fTimeOfNextTradeCheck").SetValue(null, start + 7200);
        AccessTools.Property(typeof(OrbitalSimulation), "Enabled").SetValue(null, true);
        SffSimulation.Enabled = SffEconomy.Enabled = true;
        DataHandler.dictDataCoCollections = new Dictionary<string, DataCoCollection> { { "Metal", Collection("Metal") }, { "Product", Collection("Product") } };
        var metal = Empty<DataCO>(); metal.JCO = new JsonCondOwner { strName = "metal" };
        DataHandler.dictDataCOs = new Dictionary<string, DataCO> { { "metal", metal } };
        DataHandler.dictCargoSpecs = new Dictionary<string, JsonCargoSpec> { { "standard", new JsonCargoSpec { strName = "standard", bIsDefault = true } } };
        Hull("SOURCE").objSS.bBOLocked = true;
        var destination = Hull("DEST"); destination.objSS.bBOLocked = true; destination.objSS.vBOOffsetx = .0001; destination.objSS.vPosx = .0001;
        Market("SOURCE", Producer("mine", "Metal", 3), 50);
        Market("DEST", Producer("factory", "Product", 3, "Metal"), 0);
        StarSystem.fEpoch = start;
    }
    internal static void Run(Action<bool, string> check) {
        Ostrabus.OstrabusPlugin.Logger = new BepInEx.Logging.ManualLogSource("economy-check");
        Ostrabus.OstrabusPlugin.Logger.LogEvent += (_, entry) => Console.WriteLine(entry.Data);
        var h = new Harmony("offline.economy.fixture"); var patches = new Harmony("offline.economy.production");
        void Patch(System.Reflection.MethodBase method, string prefix) => h.Patch(method, prefix: new HarmonyMethod(typeof(EconomyChecks), prefix));
        try {
            Patch(AccessTools.PropertyGetter(typeof(MonoSingleton<MarketManager>), "Instance"), nameof(Manager));
            Patch(AccessTools.PropertyGetter(typeof(ShipMarket), nameof(ShipMarket.BlockadeDiscount)), nameof(Blockade));
            Patch(AccessTools.Method(typeof(Ship), nameof(Ship.IsStation)), nameof(Station));
            Patch(AccessTools.PropertyGetter(typeof(Ship), nameof(Ship.ShipCO)), nameof(ShipCo));
            Patch(AccessTools.Method(typeof(CondOwner), nameof(CondOwner.HasCond), new[] { typeof(string) }), nameof(HasCond));
            Patch(AccessTools.Method(typeof(Ship), nameof(Ship.GetRCSRemain)), nameof(Fuel));
            Patch(AccessTools.Method(typeof(ShipSitu), nameof(ShipSitu.GetRadiusAU)), nameof(Radius));
            Patch(AccessTools.Method(typeof(StarSystem), nameof(StarSystem.GetShipByRegID)), nameof(Registry));
            Patch(AccessTools.Method(typeof(StarSystem), nameof(StarSystem.GetBO)), nameof(Body));
            Patch(AccessTools.Method(typeof(AIShipManager), nameof(AIShipManager.GetCargoShipForRoute)), nameof(Loot));
            Patch(AccessTools.Method(typeof(DataHandler), nameof(DataHandler.GetMarketConfig)), nameof(Pod));
            Patch(AccessTools.Constructor(typeof(AIShip), new[] { typeof(string), typeof(AIType), typeof(string) }), nameof(Constructor));
            Patch(AccessTools.Method(typeof(AIShipManager), nameof(AIShipManager.AddAIToDict)), nameof(Register));
            Patch(AccessTools.Method(typeof(CrewSim), nameof(CrewSim.DockAndDespawn)), nameof(Despawn));
            Patch(AccessTools.Method(typeof(Ship), nameof(Ship.Destroy)), nameof(Destroy));
            Patch(AccessTools.Method(typeof(FlyToPath), nameof(FlyToPath.PlanTrip4)), nameof(Plan));
            Patch(AccessTools.Method(typeof(ShipMarket), nameof(ShipMarket.RequestTraderUpdate)), nameof(Sync));
            foreach (var type in new[] { typeof(FreightNoWakePatch), typeof(FreightPlanningClockPatch), typeof(CargoLoadingPatch), typeof(EconomicProductionPatch), typeof(EconomicDispatchPatch), typeof(EconomicTraderSyncPatch), typeof(EconomicFreightMotionPatch), typeof(EconomicFreightCatchupPatch), typeof(EconomicCatchupPatch) })
                check(patches.CreateClassProcessor(type).Patch().Count == (type == typeof(FreightPlanningClockPatch) ? 4 : 1), "Economy hook binds " + type.Name);
            TimelineChecks(check);
            LoadingChecks(check, patches);
            NativeChecks(check);
            PlanningChecks(check);
            StageChecks(check, h);
            InspectionChecks(check);
            IntegrationChecks(check, h, patches);
        } finally { SffEconomy.Current?.Dispose(); patches.UnpatchSelf(); h.UnpatchSelf(); }
    }
    static void IntegrationChecks(Action<bool, string> check, Harmony fixture, Harmony patches) {
        fixture.Patch(AccessTools.PropertyGetter(typeof(GUIFFWD), nameof(GUIFFWD.Active)), prefix: new HarmonyMethod(typeof(EconomyChecks), nameof(SffActive)));
        fixture.Patch(AccessTools.Method(typeof(StarSystem), nameof(StarSystem.Update)), prefix: new HarmonyMethod(typeof(EconomyChecks), nameof(World)) { priority = Priority.Last });
        fixture.Patch(AccessTools.Method(typeof(StarSystem), "UpdateShip"), transpiler: new HarmonyMethod(typeof(EconomyChecks), nameof(MotionBody)));
        patches.CreateClassProcessor(typeof(OrbitalStepObservationPatch)).Patch();
        Setup(); movedFreighters = 0;
        CrewSim.system.Update(10800);
        check(SffEconomy.Current == null && FreightPlanningScope.Current == null, "World finalizer releases economic and planning contexts");
        check(markets.ContainsKey("CARGO1") && !hulls["CARGO1"].HideFromSystem && planCalls == 1, "Real world-hook sequence creates one partial flight without native duplicate dispatch");
        CrewSim.system.Update(10800);
        check(movedFreighters == 0, "Managed freight bypasses the overlapping native UpdateShip path");
        check(markets.Count == 2 && hulls["CARGO1"].HideFromSystem && markets["DEST"].GetStockCountForCategory("Product") == 1,
            "Real FFWD hooks deliver once and suppress overlapping native production");
        check(SffEconomy.Current == null && SffSimulation.Current == null && StarSystem.fEpoch == start + 21600, "Finalizer cleanup and global clock after split world updates");
    }
    static void PlanningChecks(Action<bool, string> check) {
        Setup();
        var body = new BodyOrbit(new JsonBodyOrbitSave { strName = "moving", fAxis1 = 2, fAxis2 = 2, fPerh = 1, fAph = 1, fPeriod = 100000, nOrbitDirection = 1 });
        CrewSim.system.aBOs[body.strName] = body;
        var situation = hulls["SOURCE"].objSS; situation.strBOPORShip = body.strName;
        StarSystem.fEpoch = start + 21600; situation.UpdateTime(StarSystem.fEpoch);
        var original = OrbitalMotion.Read(situation);
        double departure = start + 7200;
        using (new FreightPlanningScope(departure)) {
            check(StarSystem.fEpoch == start + 21600 && FreightPlanningScope.Epoch() == departure, "Historical planner clock is isolated from global time");
            check((OrbitalMotion.Read(situation).Position - original.Position).Length > 1000, "Planner sees station at historical departure");
            var point = new ShipSitu(false); point.vPosx = situation.vPosx;
            point.LockToBO(body);
            check(body.dTimeCalcLast == departure, "Native default LockToBO consumes the scoped clock");
            var before = OrbitalMotion.Read(situation);
            CrewSim.system.IsWithinNoWakeRangeOfAnyStation(situation, departure + 300);
            check((OrbitalMotion.Read(situation).Position - before.Position).Length == 0, "No-wake queries leave live station caches unchanged");
        }
        check(FreightPlanningScope.Current == null && FreightPlanningScope.Epoch() == StarSystem.fEpoch, "Planner scope released");
        check((OrbitalMotion.Read(situation).Position - original.Position).Length == 0, "Station caches restored exactly");
        check(body.dTimeCalcLast == StarSystem.fEpoch, "Body ephemerides restored to live epoch");
        new ShipSitu(false).LockToBO(body);
        check(body.dTimeCalcLast == StarSystem.fEpoch, "Ordinary native planning retains the real clock");
        try { using (new FreightPlanningScope(departure)) throw new InvalidOperationException("fixture"); } catch (InvalidOperationException) { }
        check(FreightPlanningScope.Current == null && (OrbitalMotion.Read(situation).Position - original.Position).Length == 0, "Exception restores planner scope and live station");
    }
    static void LoadingChecks(Action<bool, string> check, Harmony patches) {
        var method = AccessTools.Method(typeof(MarketManager), nameof(MarketManager.RegisterAICargoHauler));
        void Load() {
            var ship = Hull("LOADER"); ship.MarketConfigs = new Dictionary<string, string> { { "pod", "fixture" } };
            var route = new TradeRouteDTO { OriginStation = "SOURCE", DestinationStation = "DEST", CoCollection = DataHandler.dictDataCoCollections["Metal"], Amount = 50 };
            MarketManager.RegisterAICargoHauler(ship, new List<TradeRouteDTO> { route });
        }
        patches.Unpatch(method, HarmonyPatchType.Transpiler, patches.Id);
        Setup(); Load();
        check(markets["SOURCE"].GetStockCountForCategory("Metal") == 50 && markets["LOADER"].GetStockCountForCategory("Metal") == 50,
            "Native loading reproduces source cargo duplication");
        patches.CreateClassProcessor(typeof(CargoLoadingPatch)).Patch();
        Setup(); Load();
        check(markets["SOURCE"].GetStockCountForCategory("Metal") == 0 && markets["LOADER"].GetStockCountForCategory("Metal") == 50,
            "Patched native loading conserves the shipment");
        for (int capacity = 0; capacity <= 20; capacity++) for (int count = 0; count <= 20; count++) {
            var pod = Config("test", null, capacity);
            var items = Enumerable.Range(0, count).Select(_ => new MarketItem { COCollectionName = "Metal" }).ToList();
            int overflow = CargoLoadingPatch.Add(pod, "Metal", items);
            check(overflow == Math.Max(0, count - capacity), "Pod overflow preserved");
            check(pod.Stock.TryGetValue("Metal", out var loaded) ? loaded.Count == Math.Min(count, capacity) : count == 0 || capacity == 0, "Accepted cargo count preserved");
            check(items.Count == (overflow == 0 ? 0 : count), "Remainder handed to native overflow handling");
        }
    }
    static void NativeChecks(Action<bool, string> check) {
        Setup();
        using (var context = SffEconomy.Begin(CrewSim.system, 21600, true)) {
            check(context != null, "Economic context created");
            StarSystem.fEpoch = start + 21600;
            context.Run();
            check(context.Completed, "Economic catch-up completed");
            check(StarSystem.fEpoch == start + 21600, "Global clock never rewound");
            check(planCalls == 1, "One native-planner dispatch; actual=" + planCalls + ", source=" + markets["SOURCE"].GetStockCountForCategory("Metal") + ", prices=" + string.Join(",", markets["SOURCE"].PriceModifiers));
            check(markets["SOURCE"].GetStockCountForCategory("Metal") == 12, "Source produces through all six hours and loads real cargo");
            check(markets["DEST"].GetStockCountForCategory("Product") == 1, "Delivery only feeds production from arrival onward");
            check(markets["DEST"].GetStockCountForCategory("Metal") == 55, "Manifest conserved on delivery");
            check(!markets.ContainsKey("CARGO1") && hulls["CARGO1"].HideFromSystem, "Delivered market unregistered and native despawn used");
            check((double)AccessTools.Field(typeof(AIShipManager), "fTimeOfNextTradeCheck").GetValue(null) == start + 28800, "Dispatch clock advanced through three due checks");
            string before = JsonConvert.SerializeObject(MarketManager.GetJSONSave());
            context.Run();
            check(before == JsonConvert.SerializeObject(MarketManager.GetJSONSave()), "Catch-up idempotent");
            check(!EconomicProductionPatch.Prefix() && !EconomicDispatchPatch.Prefix(), "Native duplicate economics suppressed");
        }
        check(SffEconomy.Current == null && EconomicProductionPatch.Prefix() && EconomicDispatchPatch.Prefix(), "Context clears and native behavior restored");
        Setup(); rejectPlan = true;
        using (var context = SffEconomy.Begin(CrewSim.system, 21600, true)) {
            StarSystem.fEpoch = start + 21600; context.Run();
            check(markets["SOURCE"].GetStockCountForCategory("Metal") == 68, "Rejected plans return all cargo to source");
            check(markets["DEST"].GetStockCountForCategory("Metal") == 0 && markets.Count == 2, "Rejected ships leave no manifests");
        }
        Setup();
        using (var context = SffEconomy.Begin(CrewSim.system, 10800, true)) {
            StarSystem.fEpoch = start + 10800; context.Run();
            var ship = hulls["CARGO1"];
            check(!ship.HideFromSystem && ship.objSS.NavData != null, "Partial flight persists native NavData");
            check(Math.Abs(ship.objSS.vPosx - .000025) < 1e-12, "Departure at hour two receives one hour of travel, not three");
            check(markets["CARGO1"].GetStockCountForCategory("Metal") == 56 && markets["DEST"].GetStockCountForCategory("Metal") == 0, "Partial shipment stays in transit");
            var saved = MarketManager.GetJSONSave();
            var restored = new ShipMarket(saved.mapMarket["CARGO1"]);
            check(restored.GetStockCountForCategory("Metal") == 56, "Native market save/reload retains in-flight cargo");
        }
        using (var context = SffEconomy.Begin(CrewSim.system, 10800, true)) {
            StarSystem.fEpoch += 10800; context.Run();
            check(markets["SOURCE"].GetStockCountForCategory("Metal") == 12 && markets["DEST"].GetStockCountForCategory("Metal") == 55 && markets["DEST"].GetStockCountForCategory("Product") == 1,
                "Two three-hour skips equal a six-hour skip, including pre-existing in-flight cargo");
            check(planCalls == 1 && markets.Count == 2 && hulls["CARGO1"].HideFromSystem, "Existing shipment delivered exactly once across skips");
        }
        Setup(); throwPlan = true;
        using (var context = SffEconomy.Begin(CrewSim.system, 21600, true)) {
            StarSystem.fEpoch += 21600; context.Run();
            check(context.Completed && markets.Count == 2 && markets["SOURCE"].GetStockCountForCategory("Metal") == 68,
                "Planner exceptions return manifests and do not abandon later production");
            check(AIShipManager.dictAIs["INTERREGIONAL"].Count == 0 && hulls.Where(p => p.Key.StartsWith("CARGO")).All(p => p.Value.bDestroyed), "Failed creation leaves no registered pilots or live hulls");
        }
        Setup();
        SffEconomy.Enabled = false;
        check(SffEconomy.Begin(CrewSim.system, 21600, true) == null, "Economy toggle preserves vanilla");
        SffEconomy.Enabled = true;
        check(SffEconomy.Begin(CrewSim.system, 21600, false) == null && SffEconomy.Begin(CrewSim.system, 59, true) == null, "Ordinary updates do not allocate a timeline");
        var producer = markets["SOURCE"].GetConfigs()[0].GetProductionMaps()[0];
        Field(producer, "_outputPerHour", 1e9f);
        check(SffEconomy.Begin(CrewSim.system, 21600, true) == null && markets["SOURCE"].GetStockCountForCategory("Metal") == 50 && producer.LastUpdateTime == start,
            "Oversized timeline rejects before mutations");
        check(SffEconomy.Current == null, "Failure cleanup releases context");
    }
    static void TimelineChecks(Action<bool, string> check) {
        var events = new List<string>(); var timeline = new EconomicTimeline(0, 21600);
        timeline.Add(7200, 2, at => { events.Add("dispatch:" + at); return at + 7200; });
        timeline.Add(7200, 1, at => { events.Add("production:" + at); return double.PositiveInfinity; });
        timeline.Add(7200, 0, at => { events.Add("arrival:" + at); return double.PositiveInfinity; });
        timeline.Run();
        check(events.SequenceEqual(new[] { "arrival:7200", "production:7200", "dispatch:7200", "dispatch:14400", "dispatch:21600" }), "Economic event ordering and three dispatch windows");
        check(timeline.Executed == 5, "Timeline count");
        var broken = new EconomicTimeline(0, 1); broken.Add(0, 0, at => at);
        bool threw = false; try { broken.Run(); } catch (InvalidOperationException) { threw = true; }
        check(threw, "Nonadvancing activity rejected");
    }
}
