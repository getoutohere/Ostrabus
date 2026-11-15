using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json;
using Ostrabus.Orbital.Economy;
using Ostranauts.Core;
using Ostranauts.Core.Models;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using Ostranauts.Trading;

static partial class EconomyChecks {
    static bool deliveryBuildFailure, deliveryRefreshFailure;
    static bool DeliveryRefresh() {
        if (deliveryRefreshFailure) throw new InvalidOperationException("fixture trader refresh failure");
        return false;
    }
    static void DeliveryBuild(ShipMarket __instance) {
        if (deliveryBuildFailure && !markets.Values.Any(m => ReferenceEquals(m, __instance)))
            throw new InvalidOperationException("fixture staged cache failure");
    }

    static (Ship ship, HaulerCargoPilot pilot, AIShip ai) DeliveryFlight(bool market = true) {
        Setup();
        var ship = Hull("FREIGHT");
        if (market) DeliveryMarket("FREIGHT", (3, 10));
        var pilot = new HaulerCargoPilot("SOURCE", ship, null);
        var ai = Empty<AIShip>();
        Field(ai, "_shipUs", ship); Field(ai, "_aiCharacter", pilot);
        Field(ai, "_behaviorTree", pilot.BehaviorTree); Field(ai, "<Blackboard>k__BackingField", pilot.Blackboard);
        AIShipManager.dictAIs["INTERREGIONAL"].Add(ai);
        var station = hulls["DEST"]; station.objSS.bIsBO = true;
        ship.shipScanTarget = station; ship.shipSituTarget = station.objSS;
        ship.objSS.vPosx = station.objSS.vPosx;
        deliveryRetirements = 0;
        return (ship, pilot, ai);
    }

    static void DeliveryRepairs(Action<bool, string> check, Harmony fixture) {
        var production = new Harmony("offline.delivery-study.production");
        try {
            foreach (var type in new[] { typeof(MarketWithdrawalPatch), typeof(MarketBatchDepositPatch),
                typeof(CargoHullSizingPatch), typeof(CargoTransferPatch), typeof(CargoArrivalPatch), typeof(CargoEscapeArrivalPatch) })
                check(production.CreateClassProcessor(type).Patch().Count == (type == typeof(CargoArrivalPatch) ? 2 : 1),
                    "Production cargo hook binds: " + type.Name);

            Setup();
            var market = DeliveryMarket("SOURCE", (2, 10), (2, 10), (6, 10));
            check(market.RemoveStockForCategory("Metal", 8, 0).Count == 8 && market.GetStockCountForCategory("Metal") == 2,
                "Withdrawal fills the available request across three actors.");
            Setup(); market = DeliveryMarket("SOURCE", (2, 10), (2, 10), (6, 10));
            check(market.RemoveStockForCategory("Metal", 8, 0, "actor1").Count == 8 && market.GetStockCountForCategory("Metal") == 2,
                "Preferred withdrawal consumes only each actor's incremental output.");
            check(market.RemoveStockForCategory("Metal", 8, 0).Count == 2 && market.GetStockCountForCategory("Metal") == 0,
                "Source shortage returns only real available goods.");

            for (int a = 0; a <= 5; a++) for (int b = 0; b <= 5; b++) for (int count = 0; count <= 12; count++) {
                Setup(); market = DeliveryMarket("DEST", (0, a), (0, b));
                var items = DeliveryItems(count);
                market.AddStockForCategory("Metal", items, 0);
                check(market.GetStockCountForCategory("Metal") == Math.Min(count, a + b) && items.Count == count,
                    "Batch deposit conserves accepted stock and does not mutate caller input.");
            }
            Setup(); market = DeliveryMarket("DEST", (0, 3), (0, 10));
            market.AddStockForCategory("Metal", DeliveryItems(5), 0, "actor0");
            check(market.GetStockCountForCategory("Metal") == 5 && market.GetConfigs()[0].Stock["Metal"].Count == 3,
                "Preferred deposit forwards only the two-item tail.");
            Setup(); market = DeliveryMarket("DEST", (7, 3), (0, 10));
            market.AddStockForCategory("Metal", DeliveryItems(5), 0, "actor0");
            check(market.GetConfigs()[0].Stock["Metal"].Count == 7 && market.GetConfigs()[1].Stock["Metal"].Count == 5,
                "Existing actor overfill cannot enlarge the incoming batch or make tail indexing negative.");

            // Exercise the actual native ship-to-ship transaction caller, not
            // just the patched stock primitives.
            Setup();
            var origin = DeliveryMarket("SOURCE", (2, 10), (2, 10), (6, 10));
            var destination = DeliveryMarket("DEST", (0, 3), (0, 10));
            MarketManager.RegisterShipToShipTransaction("Metal", "SOURCE", "DEST", origin.GetConfigs()[0], 8);
            check(origin.GetStockCountForCategory("Metal") == 2 && destination.GetStockCountForCategory("Metal") == 8,
                "Native player ship-to-ship transaction moves all eight exactly once.");

            Setup(); deliveryNearest = hulls["SOURCE"];
            var collection = DataHandler.dictDataCoCollections["Metal"];
            var four = new TradeRouteDTO { OriginStation = "SOURCE", DestinationStation = "DEST", CoCollection = collection,
                Amount = MarketManager.CARGOPOD_DEFAULTMASSCAPACITY * 4 };
            var one = new TradeRouteDTO { OriginStation = "SOURCE", DestinationStation = "DEST", CoCollection = collection, Amount = 1 };
            check(AIShipManager.GetCargoShipForRoute(new List<TradeRouteDTO> { four, one }) == "InterregionalCargoShip8Pods" &&
                AIShipManager.GetCargoShipForRoute(new List<TradeRouteDTO> { one, four }) == "InterregionalCargoShip8Pods",
                "Hull sizing accumulates the bundle and is order independent.");
            check(CargoHullSizingPatch.Sum(int.MaxValue, 1) == int.MaxValue, "Sizing saturates instead of wrapping to a small hull.");

            Setup(); var source = DeliveryMarket("FREIGHT", (3, 10));
            destination = DeliveryMarket("DEST", (0, 2));
            var actor = destination.GetConfigs()[0]; var stock = actor.Stock["Metal"]; var prices = destination.PriceModifiers;
            source.GetConfigs()[0].Stock["Metal"][0].COName = "first-type";
            source.GetConfigs()[0].Stock["Metal"][1].COName = "second-type";
            MarketManager.UnregisterCargoShip("FREIGHT", "DEST");
            check(!markets.ContainsKey("FREIGHT") && destination.GetStockCountForCategory("Metal") == 2,
                "Valid full destination still accepts what fits and discards overflow by native policy.");
            check(ReferenceEquals(actor, destination.GetConfigs()[0]) && ReferenceEquals(stock, actor.Stock["Metal"]) &&
                ReferenceEquals(prices, destination.PriceModifiers) && stock.Select(i => i.COName).SequenceEqual(new[] { "first-type", "second-type" }),
                "Arrival preserves receiver identities and the actual accepted item definitions.");
            MarketManager.UnregisterCargoShip("FREIGHT", "DEST");
            check(destination.GetStockCountForCategory("Metal") == 2, "Completed arrival replay cannot duplicate goods.");

            Setup(); source = DeliveryMarket("FREIGHT", (3, 10));
            string before = JsonConvert.SerializeObject(MarketManager.GetJSONSave());
            MarketManager.UnregisterCargoShip("FREIGHT", "MISSING");
            check(ReferenceEquals(source, markets["FREIGHT"]) && before == JsonConvert.SerializeObject(MarketManager.GetJSONSave()),
                "Missing market leaves the complete original manifest untouched.");
            MarketManager.UnregisterCargoShip("FREIGHT", "FREIGHT");
            check(before == JsonConvert.SerializeObject(MarketManager.GetJSONSave()), "Self-transfer cannot erase a manifest.");
            var restored = new ShipMarket(MarketManager.GetJSONSave().mapMarket["FREIGHT"]);
            check(restored.GetStockCountForCategory("Metal") == 3, "Failed handoff survives native save restoration.");

            Setup(); DeliveryMarket("FREIGHT", (3, 10)); destination = DeliveryMarket("DEST", (0, 2));
            before = JsonConvert.SerializeObject(MarketManager.GetJSONSave());
            check(!CargoDelivery.Refund("FREIGHT", "DEST") && before == JsonConvert.SerializeObject(MarketManager.GetJSONSave()),
                "Rejected-departure refund never dumps goods or partially changes either market.");
            destination.GetConfigs()[0].MaxVirtualInventorySize["Metal"] = 3; destination.BuildConfig();
            check(CargoDelivery.Refund("FREIGHT", "DEST") && !markets.ContainsKey("FREIGHT") && destination.GetStockCountForCategory("Metal") == 3,
                "Refund commits once all source cargo fits.");

            fixture.Patch(AccessTools.Method(typeof(ShipMarket), nameof(ShipMarket.BuildConfig)),
                prefix: new HarmonyMethod(typeof(EconomyChecks), nameof(DeliveryBuild)));
            Setup(); DeliveryMarket("FREIGHT", (3, 10)); DeliveryMarket("DEST", (0, 10));
            before = JsonConvert.SerializeObject(MarketManager.GetJSONSave());
            deliveryBuildFailure = true;
            check(!CargoDelivery.Deliver("FREIGHT", "DEST") && before == JsonConvert.SerializeObject(MarketManager.GetJSONSave()),
                "Staging exception leaves both live markets intact.");
            deliveryBuildFailure = false;

            fixture.Patch(AccessTools.Method(typeof(ShipMarket), nameof(ShipMarket.RequestTraderUpdate)),
                prefix: new HarmonyMethod(typeof(EconomyChecks), nameof(DeliveryRefresh)));
            deliveryRefreshFailure = true;
            check(CargoDelivery.Deliver("FREIGHT", "DEST") && !markets.ContainsKey("FREIGHT") && markets["DEST"].GetStockCountForCategory("Metal") == 3,
                "Trader UI failure cannot roll back committed cargo or trigger a duplicate refund.");
            deliveryRefreshFailure = false;

            var flight = DeliveryFlight();
            markets.Remove("DEST");
            var cargoDock = flight.pilot.BehaviorTree.FindCommand(typeof(DockCargoAndDespawn));
            check(cargoDock.RunCommand(flight.pilot.Blackboard).Status == CommandCode.Failure && deliveryRetirements == 0 &&
                markets["FREIGHT"].GetStockCountForCategory("Metal") == 3 && !flight.ship.HideFromSystem,
                "Normal cargo arrival does not retire or clear targets after a missing-market failure.");
            check(cargoDock.ResolveInstantly(flight.pilot.Blackboard).Status == CommandCode.Failure && deliveryRetirements == 0,
                "SFF native arrival also preserves the loaded carrier when handoff fails.");
            DeliveryMarket("DEST", (0, 2));
            check(cargoDock.RunCommand(flight.pilot.Blackboard).Status == CommandCode.Finished && deliveryRetirements == 1 &&
                flight.ship.HideFromSystem && !markets.ContainsKey("FREIGHT") && markets["DEST"].GetStockCountForCategory("Metal") == 2,
                "Retry at the same restored destination retires normally after accepting two and dumping one.");

            flight = DeliveryFlight();
            var escape = flight.pilot.BehaviorTree.FindCommand(typeof(Dock));
            check(escape.ResolveInstantly(flight.pilot.Blackboard).Status == CommandCode.Finished && deliveryRetirements == 1 &&
                !markets.ContainsKey("FREIGHT") && markets["DEST"].GetStockCountForCategory("Metal") == 3,
                "Emergency ordinary station Dock now unloads before native crew retirement.");
            flight = DeliveryFlight(); markets.Remove("DEST");
            escape = flight.pilot.BehaviorTree.FindCommand(typeof(Dock));
            check(escape.ResolveInstantly(flight.pilot.Blackboard).Status == CommandCode.Failure && deliveryRetirements == 0 &&
                ReferenceEquals(flight.ship.shipScanTarget, hulls["DEST"]),
                "Emergency missing-market failure preserves cargo, crew and target instead of pretending to dock.");
            flight = DeliveryFlight(false);
            check(flight.pilot.BehaviorTree.FindCommand(typeof(Dock)).ResolveInstantly(flight.pilot.Blackboard).Status == CommandCode.Finished &&
                deliveryRetirements == 1, "PASS emergency docking still retires without requiring a freight market.");

            flight = DeliveryFlight();
            var civilian = Empty<ScavPilot>(); Field(civilian, "<ShipUs>k__BackingField", flight.ship);
            check(new Dock(civilian).ResolveInstantly(new Blackboard()).Status == CommandCode.Finished && deliveryRetirements == 1 &&
                markets.ContainsKey("FREIGHT"), "Noncargo pilots retain ordinary Dock without unloading arbitrary virtual stock.");

            flight = DeliveryFlight();
            Field(flight.ai, "_activeCommand", flight.pilot.BehaviorTree.FindCommand(typeof(FlyTo)));
            check(!SffEconomy.IsFreight(flight.ship), "SFF economy excludes the actual Flee/FlyTo instance.");
            var main = ((List<ICommand>)AccessTools.Field(typeof(SelectorCommand), "_childCommands").GetValue(flight.pilot.BehaviorTree))
                .OfType<StickySequenceCommand>().Single();
            Field(flight.ai, "_activeCommand", main.FindCommand(typeof(FlyTo)));
            check(SffEconomy.IsFreight(flight.ship), "SFF economy accepts the actual Main/FlyTo instance.");
            flight.pilot.PriorityQueue.EnqueueCommand(new FlyTo(flight.pilot));
            check(!SffEconomy.IsFreight(flight.ship), "Queued instructions retain native SFF control over an otherwise eligible freight hull.");
            flight = DeliveryFlight();
            var branches = (List<ICommand>)AccessTools.Field(typeof(SelectorCommand), "_childCommands").GetValue(flight.pilot.BehaviorTree);
            var threat = branches.OfType<SequenceCommand>().Single();
            var flee = ((List<ICommand>)AccessTools.Field(typeof(SequenceCommand), "_childCommands").GetValue(threat)).OfType<StickySequenceCommand>().Single();
            Field(flee, "_currentChild", 2);
            var savedTree = JsonConvert.DeserializeObject<JsonCommandPayload[]>(JsonConvert.SerializeObject(flight.ai.GetBehaviorTreeSaveData()));
            var restoredPilot = new HaulerCargoPilot("SOURCE", flight.ship, null);
            Field(flight.ai, "_aiCharacter", restoredPilot); Field(flight.ai, "_behaviorTree", restoredPilot.BehaviorTree);
            Field(flight.ai, "<Blackboard>k__BackingField", restoredPilot.Blackboard);
            flight.ai.LoadBehaviorTreeSave(savedTree);
            check(!SffEconomy.IsFreight(flight.ship), "Native save/load retains escape ownership even before the first active leaf is dispatched.");
            DeliverySffRepairs(check, fixture);
            Console.WriteLine("Production freight accounting, delivery and emergency-arrival checks passed.");
        } finally {
            deliveryBuildFailure = deliveryRefreshFailure = false;
            production.UnpatchSelf();
        }
    }

    static void DeliverySffRepairs(Action<bool, string> check, Harmony fixture) {
        // These are chronological catch-up tests, not direct transfer calls.
        foreach (bool receiverExists in new[] { false, true }) {
            var flight = DeliveryFlight();
            AccessTools.Field(typeof(AIShipManager), "fTimeOfNextTradeCheck").SetValue(null, start + 100000);
            if (receiverExists) DeliveryMarket("DEST", (0, 2)); else markets.Remove("DEST");
            double destinationKm = hulls["DEST"].objSS.vPosx * Ostrabus.Orbital.OrbitalMotion.KmPerAU;
            flight.ship.objSS.NavData = Leg(flight.ship, start, 600, destinationKm, destinationKm, 100, 100);
            using (var context = SffEconomy.Begin(CrewSim.system, 1800, true)) {
                check(context != null, "Existing ordinary cargo flight enters economic SFF.");
                StarSystem.fEpoch = start + 1800;
                context.Run();
                if (receiverExists) {
                    check(!markets.ContainsKey("FREIGHT") && markets["DEST"].GetStockCountForCategory("Metal") == 2 &&
                        flight.ship.HideFromSystem && deliveryRetirements == 1,
                        "Chronological SFF accepts two, dumps one and retires the carrier normally.");
                } else {
                    check(markets["FREIGHT"].GetStockCountForCategory("Metal") == 3 && !flight.ship.HideFromSystem && deliveryRetirements == 0,
                        "Chronological SFF retains cargo and carrier after a missing-market arrival.");
                }
            }
        }

        // Force rejection after actual loading and separately prevent refund.
        // The new carrier must remain registered at its original real spawn
        // state rather than being destroyed with its manifest still attached.
        fixture.Patch(AccessTools.Constructor(typeof(AIShip), new[] { typeof(string), typeof(AIType), typeof(string) }),
            prefix: new HarmonyMethod(typeof(EconomyChecks), nameof(Constructor)));
        fixture.Patch(AccessTools.Method(typeof(DataHandler), nameof(DataHandler.GetMarketConfig)),
            prefix: new HarmonyMethod(typeof(EconomyChecks), nameof(Pod)));
        fixture.Patch(AccessTools.Method(typeof(AIShipManager), nameof(AIShipManager.GetCargoShipForRoute)),
            prefix: new HarmonyMethod(typeof(EconomyChecks), nameof(Loot)));
        fixture.Patch(AccessTools.Method(typeof(AIShipManager), nameof(AIShipManager.AddAIToDict)),
            prefix: new HarmonyMethod(typeof(EconomyChecks), nameof(Register)));
        fixture.Patch(AccessTools.Method(typeof(FlyToPath), nameof(FlyToPath.PlanTrip4)),
            prefix: new HarmonyMethod(typeof(EconomyChecks), nameof(DeliveryRejectPlan)));
        fixture.Patch(AccessTools.PropertyGetter(typeof(Ship), nameof(Ship.ShipCO)),
            prefix: new HarmonyMethod(typeof(EconomyChecks), nameof(ShipCo)));
        fixture.Patch(AccessTools.Method(typeof(CondOwner), nameof(CondOwner.HasCond), new[] { typeof(string) }),
            prefix: new HarmonyMethod(typeof(EconomyChecks), nameof(HasCond)));
        Setup();
        var loading = new Harmony("offline.delivery-study.loading");
        try {
            loading.CreateClassProcessor(typeof(CargoLoadingPatch)).Patch();
            loading.CreateClassProcessor(typeof(CargoTradeBundlePatch)).Patch();
            deliveryRetirements = 0;
            using (var context = SffEconomy.Begin(CrewSim.system, 10800, true)) {
                StarSystem.fEpoch = start + 10800;
                context.Run();
                var ship = hulls["CARGO1"];
                check(markets.ContainsKey("CARGO1") && !ship.bDestroyed && !ship.HideFromSystem && deliveryRetirements == 0 &&
                    AIShipManager.dictAIs["INTERREGIONAL"].Any(ai => ReferenceEquals(ai.Ship, ship)),
                    "Failed departure with an impossible refund retains the original loaded ship and controller.");
                check(ReferenceEquals(ship.shipScanTarget, hulls["SOURCE"]) && ship.objSS.NavData == null && ship.objSS.vPosx == 0,
                    "Failed refund restores real spawn position and native origin docking goal.");
            }
        } finally { loading.UnpatchSelf(); }
    }

    static bool DeliveryRejectPlan(ref Ostranauts.ShipGUIs.Utilities.NavData __result) {
        // Simulate loss of the originating market after loading. This is a
        // bounded failure fixture, not a claimed normal economic event.
        markets.Remove("SOURCE");
        __result = null;
        return false;
    }
}
