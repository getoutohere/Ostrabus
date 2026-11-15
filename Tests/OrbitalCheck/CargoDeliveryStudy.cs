using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Ostrabus.Orbital.Economy;
using Ostranauts.Core;
using Ostranauts.Core.Models;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using Ostranauts.Trading;

static partial class EconomyChecks {
    static Ship deliveryNearest;
    static int deliveryRetirements;

    static bool DeliveryNearest(ref Ship __result) { __result = deliveryNearest; return false; }
    static bool DeliveryRetire(Ship shipAI, Ship shipStation) {
        deliveryRetirements++;
        shipAI.HideFromSystem = true;
        AIShipManager.dictAIs["INTERREGIONAL"].RemoveAll(ai => ReferenceEquals(ai.Ship, shipAI));
        return false;
    }

    static List<MarketItem> DeliveryItems(int count) => Enumerable.Range(0, count)
        .Select(i => new MarketItem { COCollectionName = "Metal", COName = "metal" }).ToList();

    static ShipMarket DeliveryMarket(string id, params (int stock, int capacity)[] actors) {
        var market = new ShipMarket(id);
        for (int i = 0; i < actors.Length; i++) {
            var actor = Config("actor" + i, null, actors[i].capacity);
            actor.Stock["Metal"] = DeliveryItems(actors[i].stock);
            market.AddMarketConfig(actor.COwnerId, actor);
        }
        market.BuildConfig();
        markets[id] = market;
        return market;
    }

    // These assertions reproduce defects in shipped methods. Passing means the
    // failure was demonstrated, not that a production repair has been applied.
    internal static void DeliveryStudy(Action<bool, string> check) {
        var fixture = new Harmony("offline.delivery-study.boundaries");
        void Prefix(Type type, string name, string method) => fixture.Patch(
            AccessTools.Method(type, name), prefix: new HarmonyMethod(typeof(EconomyChecks), method));
        try {
            fixture.Patch(AccessTools.PropertyGetter(typeof(MonoSingleton<MarketManager>), "Instance"),
                prefix: new HarmonyMethod(typeof(EconomyChecks), nameof(Manager)));
            fixture.Patch(AccessTools.PropertyGetter(typeof(ShipMarket), nameof(ShipMarket.BlockadeDiscount)),
                prefix: new HarmonyMethod(typeof(EconomyChecks), nameof(Blockade)));
            Prefix(typeof(ShipMarket), nameof(ShipMarket.RequestTraderUpdate), nameof(Sync));
            fixture.Patch(AccessTools.Method(typeof(UnityEngine.Debug), "Log", new[] { typeof(object) }),
                prefix: new HarmonyMethod(typeof(EconomyChecks), nameof(StudyQuiet)));

            Setup();
            var market = DeliveryMarket("SOURCE", (2, 10), (2, 10), (6, 10));
            var withdrawn = market.RemoveStockForCategory("Metal", 8, 0);
            check(withdrawn.Count == 6 && market.GetStockCountForCategory("Metal") == 4,
                "Native multi-actor withdrawal underfills an available eight-unit request with six.");
            Console.WriteLine("withdrawal: requested 8 of 10 available; removed " + withdrawn.Count);

            Setup();
            market = DeliveryMarket("DEST", (0, 10), (0, 10));
            market.AddStockForCategory("Metal", DeliveryItems(5), 0);
            check(market.GetStockCountForCategory("Metal") == 10,
                "Native batch deposit repeats five incoming items across two actors.");
            Console.WriteLine("batch deposit: supplied 5; stored " + market.GetStockCountForCategory("Metal"));

            Setup();
            market = DeliveryMarket("DEST", (0, 3), (0, 10));
            market.AddStockForCategory("Metal", DeliveryItems(5), 0, "actor0");
            check(market.GetStockCountForCategory("Metal") == 8,
                "Native preferred-actor deposit repeats the accepted prefix in the next actor.");

            Setup();
            DeliveryMarket("FREIGHT", (3, 10));
            market = DeliveryMarket("DEST", (0, 2));
            MarketManager.UnregisterCargoShip("FREIGHT", "DEST");
            check(!markets.ContainsKey("FREIGHT") && market.GetStockCountForCategory("Metal") == 2,
                "Native arrival discards one of three units when destination capacity is two.");
            Console.WriteLine("full destination: sent 3; received 2; carrier record removed");

            Setup();
            DeliveryMarket("FREIGHT", (3, 10));
            MarketManager.UnregisterCargoShip("FREIGHT", "MISSING");
            check(!markets.ContainsKey("FREIGHT") && !markets.ContainsKey("MISSING"),
                "Native arrival removes all cargo before noticing the destination market is missing.");

            // The integer deposit overload is a useful control: it threads the
            // remainder correctly and does not have the batch overload defect.
            Setup();
            market = DeliveryMarket("DEST", (0, 3), (0, 10));
            market.AddStockForCategory(DeliveryItems(1)[0], 5, 0);
            check(market.GetStockCountForCategory("Metal") == 5,
                "Native individual-item deposit conserves five units across actors.");

            Prefix(typeof(StarSystem), nameof(StarSystem.GetNearestStationRegional), nameof(DeliveryNearest));
            Setup();
            deliveryNearest = hulls["SOURCE"];
            AccessTools.Field(typeof(AIShipManager), "dictSpawnRegIDs").SetValue(null, new Dictionary<string, List<string>>());
            var collection = DataHandler.dictDataCoCollections["Metal"];
            var large = new TradeRouteDTO { OriginStation = "SOURCE", DestinationStation = "DEST",
                CoCollection = collection, Amount = MarketManager.CARGOPOD_DEFAULTMASSCAPACITY * 9 };
            var small = new TradeRouteDTO { OriginStation = "SOURCE", DestinationStation = "DEST",
                CoCollection = collection, Amount = 1 };
            string largeFirst = AIShipManager.GetCargoShipForRoute(new List<TradeRouteDTO> { large, small });
            string smallFirst = AIShipManager.GetCargoShipForRoute(new List<TradeRouteDTO> { small, large });
            check(largeFirst == "InterregionalCargoShip4Pods" && smallFirst == "InterregionalCargoShip36Pods",
                "Native hull selection uses only the last consignment, making size depend on bundle order.");
            Console.WriteLine("same bundle reversed: " + largeFirst + " versus " + smallFirst);

            // Construct the authored C# cargo tree without spawning a new hull.
            // Only Unity/crew retirement boundaries are replaced, never routing
            // or market transfer. Execute the actual Flee/Dock leaf.
            Prefix(typeof(BaseCharacter), "AssignCaptain", nameof(StudyQuiet));
            Prefix(typeof(Ship), nameof(Ship.Maneuver), nameof(StudyQuiet));
            Prefix(typeof(Dock), "Cleanup", nameof(StudyQuiet));
            Prefix(typeof(CrewSim), nameof(CrewSim.DockAndDespawn), nameof(DeliveryRetire));
            Setup();
            var ship = Hull("FREIGHT");
            Field(CrewSim.system, "dictShipOwners", new Dictionary<string, string>());
            DeliveryMarket("FREIGHT", (3, 10));
            var pilot = new HaulerCargoPilot("SOURCE", ship, null);
            ship.shipScanTarget = hulls["DEST"]; ship.shipSituTarget = hulls["DEST"].objSS;
            // Ordinary Dock's station branch tests bIsBO, not bBOLocked.
            hulls["DEST"].objSS.bIsBO = true;
            fixture.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.IsStation)),
                prefix: new HarmonyMethod(typeof(EconomyChecks), nameof(Station)));
            var controller = Empty<AIShip>();
            Field(controller, "_shipUs", ship);
            Field(controller, "_aiCharacter", pilot);
            Field(controller, "_behaviorTree", pilot.BehaviorTree);
            Field(controller, "<Blackboard>k__BackingField", pilot.Blackboard);
            // FindCommand returns the first FlyTo: Flee precedes Main in the
            // constructor. No fabricated command names or parallel tree here.
            Field(controller, "_activeCommand", pilot.BehaviorTree.FindCommand(typeof(FlyTo)));
            AIShipManager.dictAIs["INTERREGIONAL"].Add(controller);
            check(controller.ActiveCommandName == "FlyTo" && !SffEconomy.IsFreight(ship),
                "SFF admission now distinguishes the escape leaf despite its shared FlyTo name.");
            Console.WriteLine("SFF admission: native Flee/FlyTo is excluded from economic delivery catch-up");
            deliveryRetirements = 0;
            var dock = pilot.BehaviorTree.FindCommand(typeof(Dock));
            check(dock != null, "Cargo threat-response tree contains the ordinary Dock leaf.");
            var status = dock.ResolveInstantly(pilot.Blackboard);
            check(status.Status == CommandCode.Finished && deliveryRetirements == 1 && ship.HideFromSystem &&
                markets["FREIGHT"].GetStockCountForCategory("Metal") == 3,
                "Native cargo emergency docking retires the carrier without transferring its manifest.");
            Console.WriteLine("emergency Dock: retired carrier; all 3 units remain in its market");

            // Native FFWD chooses the cargo-arrival command even when the last
            // FlyTo belongs to Flee. Isolate physical placement/crew machinery;
            // the role's FFWD dispatch and actual market handoff remain native.
            Prefix(typeof(BaseCommand), "PlaceWithinDockingRange", nameof(StudyQuiet));
            ship.HideFromSystem = false;
            int before = markets["DEST"].GetStockCountForCategory("Metal");
            pilot.FFWD(pilot.BehaviorTree.FindCommand(typeof(FlyTo)), pilot.Blackboard);
            check(!markets.ContainsKey("FREIGHT") && markets["DEST"].GetStockCountForCategory("Metal") == before + 3 &&
                deliveryRetirements == 2,
                "Native FFWD unloads that same escape flight, unlike native normal-time Dock.");
            Console.WriteLine("native FFWD of escape: unloaded the same 3 units before retirement");

            // After target loss the cargo pilot has no destination other than
            // current target or origin. This is a model boundary, not a claim
            // that every lost-target event is itself a defect.
            ship.shipScanTarget = null; ship.shipSituTarget = null;
            check(ReferenceEquals(pilot.GetTarget().Ship, hulls["SOURCE"]),
                "Cargo target loss falls back to origin; destination is not retained separately.");
            DeliveryRepairs(check, fixture);
        } finally {
            fixture.UnpatchSelf();
            SffEconomy.Reset();
            Console.WriteLine("Delivery study executes shipped market/selection/docking methods; no live mutation.");
        }
    }
}
