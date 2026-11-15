using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Ostranauts.Trading;

static partial class Program {
    // Exercise raw request parsing and production parameter validation, not an
    // invented query collection. Scene/market boundaries are the parent fixture's.
    static (int status, Dictionary<string,object> body) QuantityHttp(string target, string method = "GET") {
        var assembly = typeof(Ostrabus.OstrabusPlugin).Assembly;
        var transport = assembly.GetType("Ostrabus.Inspection.InspectionHttp", true);
        var http = Activator.CreateInstance(transport, true);
        AccessTools.Method(transport, "Parse").Invoke(http, new object[] {
            method + " " + target + " HTTP/1.1\r\nHost: 127.0.0.1:18764\r\nContent-Length: 0", 18764 });
        if (AccessTools.Field(transport, "Error").GetValue(http) is string transportError)
            return (400, new Dictionary<string,object> { ["error"] = transportError });
        var type = assembly.GetType("Ostrabus.Inspection.FreightRecovery", true);
        var parsed = new object[] { http, null, null };
        if (!(bool)AccessTools.Method(type, "ParseQuantity").Invoke(null, parsed))
            return (400, new Dictionary<string,object> { ["error"] = parsed[2] });
        var url = (Uri)AccessTools.Field(transport, "Url").GetValue(http);
        var query = (System.Collections.Specialized.NameValueCollection)AccessTools.Field(transport, "QueryString").GetValue(http);
        var reply = AccessTools.Method(type, "Run").Invoke(null, new object[] { url.AbsolutePath.TrimEnd('/'), method == "POST", query["confirm"], parsed[1] });
        return ((int)AccessTools.Field(reply.GetType(), "Status").GetValue(reply), (Dictionary<string,object>)AccessTools.Field(reply.GetType(), "Body").GetValue(reply));
    }
    static string QuantityTarget(int units, int source, int destination, string category = "Electronics", bool confirm = true) =>
        "/economy/freight/FREIGHT/recover-quantity/MTRS?category=" + Uri.EscapeDataString(category) +
        "&units=" + units + "&sourceStock=" + source + "&destinationStock=" + destination + (confirm ? "&confirm=RECOVER" : "");
    static void AddWater(int amount) {
        var collection = new DataCoCollection(new JsonDCOCollection { strName = "Water" });
        Set(collection, "_avgPrice", 1d); Set(collection, "_avgMass", 1d);
        DataHandler.dictDataCoCollections.Add("Water", collection);
        var stock = new List<MarketItem>();
        for (int i = 0; i < amount; i++) {
            string id = "water" + i;
            var definition = Empty<DataCO>(); definition.JCO = new JsonCondOwner { strName = id }; DataHandler.dictDataCOs.Add(id, definition);
            stock.Add(new MarketItem { COName = id, COCollectionName = "Water" });
        }
        var pod = RecoveryActor(0, 0, true);
        pod.Stock.Clear(); pod.Stock.Add("Water", stock); pod.MaxVirtualInventorySize.Clear(); pod.MaxVirtualInventorySize.Add("Water", amount);
        recoveryMarkets["FREIGHT"].AddMarketConfig("waterPod", pod); recoveryMarkets["FREIGHT"].BuildConfig();
    }
    static void CheckQuantityRecovery() {
        int beforeChecks = checks;
        RecoveryWorld(true, 800);
        string saved = RecoverySave();
        foreach (string query in new[] {
            "", "?category=Electronics&units=1&sourceStock=800", "?category=Electronics&units=0&sourceStock=800&destinationStock=0",
            "?category=Electronics&units=4097&sourceStock=5000&destinationStock=0", "?category=Electronics&units=-1&sourceStock=800&destinationStock=0",
            "?category=Electronics&units=1.0&sourceStock=800&destinationStock=0", "?category=Electronics&units=1&sourceStock=-1&destinationStock=0",
            "?category=Electronics&units=1&sourceStock=2147483648&destinationStock=0", "?category=Electronics&units=2&sourceStock=1&destinationStock=0",
            "?category=Electronics&units=1&sourceStock=800&destinationStock=-1", "?category=Bad%2FCategory&units=1&sourceStock=800&destinationStock=0",
            "?category=Electronics&units=1&units=2&sourceStock=800&destinationStock=0" })
            Check(QuantityHttp("/economy/freight/FREIGHT/recover-quantity/MTRS" + query).status == 400 && saved == RecoverySave(), "Invalid raw quantity request changed state: " + query);
        Check(QuantityHttp("/economy/freight/FREIGHT/recover-available/MTRS?category=Electronics&units=1&sourceStock=800&destinationStock=0").status == 400,
            "Quantity parameters were silently ignored on a legacy route.");
        Check(QuantityHttp(QuantityTarget(350, 800, 0, confirm: false), "POST").status == 400 && saved == RecoverySave(), "Exact transfer bypassed confirmation.");
        var preview = QuantityHttp(QuantityTarget(350, 800, 0));
        var plan = (Dictionary<string,object>)preview.body["plan"];
        Check(preview.status == 200 && (bool)preview.body["eligible"] && (int)plan["units"] == 350 &&
            (long)plan["remainingUnits"] == 450 && saved == RecoverySave() && recoverySyncs == 0, "Quantity preview changed stock or ignored the exact amount.");
        var source = recoveryMarkets["FREIGHT"]; var destination = recoveryMarkets["MTRS"];
        var sourcePod = source.GetConfigs()[0]; var sourceList = sourcePod.Stock["Electronics"];
        var result = QuantityHttp(QuantityTarget(350, 800, 0), "POST");
        Check(result.status == 200 && (bool)result.body["applied"] && source.GetStockCountForCategory("Electronics") == 450 &&
            destination.GetStockCountForCategory("Electronics") == 350, "Exact transfer filled all capacity or lost source items.");
        Check(ReferenceEquals(source, recoveryMarkets["FREIGHT"]) && ReferenceEquals(sourceList, sourcePod.Stock["Electronics"]), "Partial debit replaced source object/list identity.");
        Check(sourceList.Select(i => i.COName).SequenceEqual(Enumerable.Range(0,450).Select(i => "item" + i)) &&
            destination.GetConfigs().SelectMany(c => c.Stock["Electronics"]).Select(i => i.COName).OrderBy(n => n).SequenceEqual(Enumerable.Range(450,350).Select(i => "item" + i).OrderBy(n => n)),
            "Transferred concrete items do not match native tail withdrawal.");
        saved = RecoverySave();
        Check(QuantityHttp(QuantityTarget(350, 800, 0), "POST").status == 409 && saved == RecoverySave(), "Repeated request credited the same allocation twice.");
        Check(QuantityHttp(QuantityTarget(1, 450, 0), "POST").status == 409 && saved == RecoverySave(), "Stale receiver balance was accepted.");
        Check(QuantityHttp(QuantityTarget(351, 450, 350), "POST").status == 409 && saved == RecoverySave(), "Over-capacity quantity was partially credited.");
        Check(new ShipMarket(MarketManager.GetJSONSave().mapMarket["FREIGHT"]).GetStockCountForCategory("Electronics") == 450, "Quantity remainder failed native save round trip.");

        RecoveryWorld(true,3993); AddWater(875); saved = RecoverySave();
        Check(!(bool)Freight("/economy/freight/FREIGHT/recover-available/MTRS").body["eligible"], "Legacy manifest limit was changed.");
        result = QuantityHttp(QuantityTarget(100,3993,0), "POST");
        Check(result.status == 200 && recoveryMarkets["FREIGHT"].GetStockCountForCategory("Electronics") == 3893 &&
            recoveryMarkets["FREIGHT"].GetStockCountForCategory("Water") == 875 && (long)result.body["remainingUnits"] == 4768,
            "Bounded category recovery failed on a 4,868-item mixed manifest or moved an unselected category.");
        Check(new ShipMarket(MarketManager.GetJSONSave().mapMarket["FREIGHT"]).GetStockCountForCategory("Water") == 875,
            "Unselected large-source cargo did not survive native serialization.");
        RecoveryWorld(true,10); recoveryMarkets["FREIGHT"].GetConfigs()[0].Stock["Electronics"][9].COName = "unknown"; saved = RecoverySave();
        Check(QuantityHttp(QuantityTarget(1,10,0), "POST").status == 409 && saved == RecoverySave(), "Selected invalid tail item escaped validation.");
        RecoveryWorld(true,10); recoveryMarkets["FREIGHT"].GetConfigs()[0].Stock["Electronics"][0].COName = "unknown";
        Check(QuantityHttp(QuantityTarget(4,10,0), "POST").status == 200 && recoveryMarkets["FREIGHT"].GetStockCountForCategory("Electronics") == 6,
            "Unselected invalid prefix was incorrectly flattened into a bounded tail selection.");
        saved = RecoverySave();
        Check(QuantityHttp(QuantityTarget(6,6,4), "POST").status == 409 && saved == RecoverySave(), "Later selected invalid item was silently credited.");

        foreach (string defect in new[] { "live", "owner", "alias", "cache", "staging", "nonpod", "capacity", "unknown-category" }) {
            RecoveryWorld(true,100);
            var pod = recoveryMarkets["FREIGHT"].GetConfigs()[0];
            if (defect == "live") CrewSim.system.dictShips.Add("FREIGHT", recoveryHull);
            if (defect == "owner") ((Dictionary<string,string>)AccessTools.Field(typeof(StarSystem),"dictShipOwners").GetValue(CrewSim.system))["FREIGHT"] = "PLAYER";
            if (defect == "alias") {
                var other = new ShipMarket("OTHER"); other.AddMarketConfig("shared",pod); other.BuildConfig(); recoveryMarkets.Add("OTHER",other);
            }
            if (defect == "cache") pod.Stock["Electronics"].RemoveAt(0);
            if (defect == "staging") recoveryStageFailure = true;
            if (defect == "nonpod") Back(pod,"CargoSpecs",null);
            if (defect == "capacity") { pod.MaxVirtualInventorySize["Electronics"] = 0; recoveryMarkets["FREIGHT"].BuildConfig(); }
            saved = RecoverySave();
            result = QuantityHttp(QuantityTarget(50,100,0,defect == "unknown-category" ? "Unknown" : "Electronics"), "POST");
            Check(result.status >= 400 && saved == RecoverySave(), "Unsafe bounded recovery mutated cargo: " + defect);
        }
        RecoveryWorld(true,17000); saved = RecoverySave();
        Check(QuantityHttp(QuantityTarget(1,17000,0), "POST").status == 409 && saved == RecoverySave(), "Affected category staging bound was bypassed.");
        RecoveryWorld(true,100);
        var largeStore = RecoveryActor(17000,20000);
        recoveryMarkets["MTRS"].AddMarketConfig("largeStore",largeStore); recoveryMarkets["MTRS"].BuildConfig(); saved = RecoverySave();
        Check(QuantityHttp(QuantityTarget(1,100,17000), "POST").status == 409 && saved == RecoverySave(), "Receiver category staging bound was bypassed.");
        RecoveryWorld(true,5000);
        foreach (var store in recoveryMarkets["MTRS"].GetConfigs()) store.MaxVirtualInventorySize["Electronics"] = 3000;
        recoveryMarkets["MTRS"].BuildConfig();
        result = QuantityHttp(QuantityTarget(4096,5000,0), "POST");
        Check(result.status == 200 && recoveryMarkets["FREIGHT"].GetStockCountForCategory("Electronics") == 904 && recoveryMarkets["MTRS"].GetStockCountForCategory("Electronics") == 4096,
            "Maximum bounded transfer failed conservation.");
        RecoveryWorld(true,100); recoverySyncFailure = true;
        result = QuantityHttp(QuantityTarget(50,100,0), "POST");
        Check(result.status == 200 && (bool)result.body["credited"] && result.body["traderSynchronizationError"] != null, "Post-commit synchronization failure lost the transfer receipt.");
        saved = RecoverySave();
        Check(QuantityHttp(QuantityTarget(50,100,0), "POST").status == 409 && saved == RecoverySave(), "Sync failure made the committed quantity replayable.");
        RecoveryWorld(true,100);
        result = QuantityHttp(QuantityTarget(100,100,0), "POST");
        Check(result.status == 200 && (bool)result.body["sourceMarketRemoved"] && !recoveryMarkets.ContainsKey("FREIGHT"), "Final selected transfer failed ordinary source cleanup.");
        Check(QuantityHttp(QuantityTarget(100,100,0), "POST").status == 404, "Completed source could be credited again.");
        Console.WriteLine((checks - beforeChecks) + " exact-quantity recovery checks passed.");
    }
}
