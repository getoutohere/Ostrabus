using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ostrabus.Inspection;
using Ostrabus.Orbital.Economy;
using Ostranauts.Core;
using Ostranauts.Core.Models;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Trading;

static partial class EconomyChecks {
    static bool Forbidden() => throw new Exception("Inspection called a mutating or lazy native helper.");
    static JObject Inspect(string path, int offset = 0, int limit = 50, long? began = null) {
        var reply = EconomyInspection.Query(path, offset, limit, began ?? Stopwatch.GetTimestamp());
        if (reply.Status != 200) throw new Exception(path + " returned " + reply.Status + ": " + SnapshotJson.Encode(reply.Body));
        return JObject.Parse(SnapshotJson.Encode(reply.Body));
    }
    static JObject InspectPage(string path, int limit = 50) {
        var all = new JArray(); int offset = 0;
        for (int page = 0; page < 100; page++) {
            var data = Inspect(path, offset, limit);
            foreach (var row in data["items"]) all.Add(row);
            if (data["nextOffset"].Type == JTokenType.Null) { data["items"] = all; return data; }
            int next = (int)data["nextOffset"];
            if (next <= offset) throw new Exception("Nonadvancing economy cursor.");
            offset = next;
        }
        throw new Exception("Economy pagination did not finish.");
    }
    static void InspectionChecks(Action<bool, string> check) {
        Setup();
        var singleton = AccessTools.Field(typeof(MonoSingleton<MarketManager>), "_instance");
        singleton.SetValue(null, manager);
        hulls["SOURCE"].objSS.bIsBO = hulls["DEST"].objSS.bIsBO = true;
        var root = Inspect("/economy");
        check((int)root["markets"] == 2 && (double)root["secondsUntilDispatchCheck"] == 7200, "Economy overview reads native clocks");
        check(InspectPage("/economy/stations")["items"].Count() == 2, "Station markets enumerated");
        var stock = InspectPage("/economy/stations/DEST/stock")["items"].Single(x => (string)x["category"] == "Metal");
        check((int)stock["stock"] == 0 && (int)stock["space"] == 100 && (bool)stock["nativeDemandEligible"], "Stock reports Metal shortage and native demand");
        var recipe = InspectPage("/economy/stations/DEST/production")["items"].Single();
        check((string)recipe["status"] == "missingInputs" && (int)recipe["inputs"][0]["shortfall"] == 1, "Production identifies missing input");
        check((double)recipe["cycleIntervalSeconds"] == 1200 && (double)recipe["nextCycleEpoch"] == start + 1200, "Recipe timing inferred without IsReady");
        var trade = InspectPage("/economy/stations/DEST/trade/Metal")["items"].Single();
        check((bool)trade["importPairEligible"] && (int)trade["importAmountBeforeHubRouting"] == 50, "Read-only commodity supply/demand pairing");
        Market("HIDDEN", null, 0);
        check(InspectPage("/economy/markets", 1)["items"].Count() == 3 && InspectPage("/economy/stations", 1)["items"].Count() == 2, "Filtered pages advance over non-stations");
        Field(manager, "_regionalMarketMap", new Dictionary<string, List<string>> { { "DEST", new List<string> { "DEST", "SOURCE" } } });
        check(InspectPage("/economy/hubs")["items"][0]["members"].Count() == 2, "Raw hub routing map exposed");

        // Match the native float-division/double-comparison boundary, including its rounding.
        markets["SOURCE"].RemoveStockForCategory("Metal", 30, 0);
        bool nativeSupply = markets["SOURCE"].GetItemsWithSurplusOrDemand().Any(r => r.OriginStation == "SOURCE" && r.CoCollection.Name == "Metal");
        var edge = InspectPage("/economy/markets/SOURCE/stock")["items"].Single(x => (string)x["category"] == "Metal");
        check((bool)edge["nativeSupplyEligible"] == nativeSupply, "Supply eligibility matches native 20-percent float boundary");
        var producer = markets["DEST"].GetConfigs()[0].GetProductionMaps()[0]; producer.LastUpdateTime = 0;
        check((string)InspectPage("/economy/markets/DEST/production")["items"][0]["status"] == "missingInputs" && producer.LastUpdateTime == 0,
            "Uninitialized recipe clock is not initialized by inspection");

        var pilot = new AIShip("SOURCE", AIType.HaulerCargo, "fixture"); var ship = pilot.Ship;
        ship.shipScanTarget = hulls["DEST"]; ship.shipSituTarget = hulls["DEST"].objSS;
        AIShipManager.AddAIToDict(pilot, "INTERREGIONAL");
        MarketManager.RegisterAICargoHauler(ship, new List<TradeRouteDTO> { new TradeRouteDTO {
            OriginStation = "SOURCE", DestinationStation = "DEST", CoCollection = DataHandler.dictDataCoCollections["Metal"], Amount = 20
        } });
        var manifests = InspectPage("/economy/freight/CARGO1/manifest", 3);
        check(manifests["items"].Count() == 20 && manifests["items"].All(i => (string)i["itemType"] == "metal"), "Manifest pages expose actual virtual item types");
        var freight = InspectPage("/economy/freight")["items"].Single();
        check((bool)freight["flight"]["cargoPilot"] && (string)freight["flight"]["destination"] == "DEST", "Freight links manifest to raw AI registration");
        check(!(bool)freight["flight"]["planEndIsDeliveryEta"], "Planned endpoint not misrepresented as delivery ETA");
        check((bool)InspectPage("/economy/stations/DEST/freight")["items"].Single()["incoming"], "Station view identifies inbound cargo");
        check((bool)InspectPage("/economy/stations/SOURCE/freight")["items"].Single()["homeMatchesStation"], "Station view identifies its home freighter");
        var stale = markets["CARGO1"].GetConfigs()[0].Stock["Metal"]; stale.RemoveAt(0);
        check((int)InspectPage("/economy/markets/CARGO1/stock")["items"].Single(x => (string)x["category"] == "Metal")["stock"] == 20 &&
            InspectPage("/economy/freight/CARGO1/manifest")["items"].Count() == 19, "Backing-versus-cached stock discrepancy stays visible, not repaired");
        var detached = Inspect("/economy/freight/CARGO1/manifest", 0, 1); stale[0].COName = "changed";
        check((string)detached["items"][0]["itemType"] == "metal", "Inspection returns detached item fields");
        var overdueBudget = Inspect("/economy/markets", 0, 50, 0);
        check((bool)overdueBudget["budgetLimited"] && overdueBudget["items"].Count() == 1 && (int)overdueBudget["nextOffset"] == 1, "Budget-limited page makes forward progress");
        check(EconomyInspection.Query("/economy/markets", 1, 5, 0).Status == 503, "Expired offset scan does not return a nonadvancing cursor");
        check(EconomyInspection.Query("/economy/markets/missing", 0, 5, Stopwatch.GetTimestamp()).Status == 404 &&
            EconomyInspection.Query("/economy/nonsense", 0, 5, Stopwatch.GetTimestamp()).Status == 404, "Unknown economy paths and markets return 404");

        string before = JsonConvert.SerializeObject(MarketManager.GetJSONSave());
        double clock = (double)AccessTools.Field(typeof(AIShipManager), "fTimeOfNextTradeCheck").GetValue(null);
        var guard = new Harmony("offline.economy.inspection.readonly");
        try {
            foreach (var method in new[] {
                AccessTools.Method(typeof(MarketManager), nameof(MarketManager.GetTradeRoute)),
                AccessTools.Method(typeof(Producer), nameof(Ostranauts.Trading.Producer.IsReady)),
                AccessTools.Method(typeof(Producer), nameof(Ostranauts.Trading.Producer.Run)),
                AccessTools.Method(typeof(MarketManager), nameof(MarketManager.Run)),
                AccessTools.Method(typeof(ShipMarket), nameof(ShipMarket.RequestTraderUpdate)),
                AccessTools.Method(typeof(DataCoCollection), nameof(DataCoCollection.GetAverageMass)),
                AccessTools.Method(typeof(DataCoCollection), nameof(DataCoCollection.GetAveragePrice)),
                AccessTools.Method(typeof(ShipSitu), nameof(ShipSitu.UpdateTime)),
                AccessTools.PropertyGetter(typeof(AIShip), nameof(AIShip.Ship))
            }) guard.Patch(method, prefix: new HarmonyMethod(typeof(EconomyChecks), nameof(Forbidden)) { priority = Priority.First });
            foreach (string path in new[] { "/economy", "/economy/stations", "/economy/markets", "/economy/hubs", "/economy/freight",
                "/economy/stations/DEST", "/economy/stations/DEST/stock", "/economy/stations/DEST/production", "/economy/stations/DEST/actors",
                "/economy/stations/DEST/trade/Metal", "/economy/freight/CARGO1", "/economy/freight/CARGO1/manifest" })
                check(Inspect(path).Count > 0, "Read-only helper guard: " + path);
        } finally { guard.UnpatchSelf(); }
        check(before == JsonConvert.SerializeObject(MarketManager.GetJSONSave()) && producer.LastUpdateTime == 0 &&
            clock == (double)AccessTools.Field(typeof(AIShipManager), "fTimeOfNextTradeCheck").GetValue(null), "Queries preserve stock, native clocks and producer initialization");
        singleton.SetValue(null, null);
        check(EconomyInspection.Query("/economy", 0, 5, Stopwatch.GetTimestamp()).Status == 503 && singleton.GetValue(null) == null,
            "No singleton creation before economy initialization");

        Setup(); singleton.SetValue(null, manager);
        using (var context = SffEconomy.Begin(CrewSim.system, 21600, true)) { StarSystem.fEpoch += 21600; context.Run(); }
        check(Inspect("/economy")["lastSffEconomy"]["records"][0]["cargo"]["Metal"].Value<int>() == 56,
            "Actual SFF cargo reports serialize through the inspection encoder");
    }
}
namespace Ostrabus.Inspection {
    internal sealed class InspectionApi {
        internal sealed class Reply {
            internal int Status; internal object Body;
            internal Reply(int status, object body) { Status = status; Body = body; }
        }
    }
}
